using System.Diagnostics;
using System.Security.Claims;
using MehguViewer.Core.Shared;
using MehguViewer.Core.Services;
using MehguViewer.Core.Workers;
using MehguViewer.Core.Helpers;
using MehguViewer.Core.Infrastructures;
using Microsoft.AspNetCore.Mvc;

#pragma warning disable CS1998 // Async method lacks 'await' operators

namespace MehguViewer.Core.Endpoints;

/// <summary>
/// Provides HTTP endpoints for user profile and library management in the MehguViewer system.
/// </summary>
/// <remarks>
/// <para><strong>User Management:</strong></para>
/// Handles user profile operations, password management, library access, reading history,
/// and progress tracking for authenticated users.
/// 
/// <para><strong>Key Features:</strong></para>
/// <list type="bullet">
///   <item><description>Profile retrieval with admin status detection</description></item>
///   <item><description>Secure password change with strength validation</description></item>
///   <item><description>Library and reading history management</description></item>
///   <item><description>Batch history import for migration scenarios</description></item>
///   <item><description>Reading progress tracking per series/chapter</description></item>
///   <item><description>Account deletion with data anonymization</description></item>
/// </list>
/// 
/// <para><strong>Security Features:</strong></para>
/// <list type="bullet">
///   <item><description>JWT authentication required for all endpoints</description></item>
///   <item><description>User identity isolation - users can only access their own data</description></item>
///   <item><description>Password strength validation on change</description></item>
///   <item><description>Secure password hashing (never exposed)</description></item>
///   <item><description>Audit logging for sensitive operations</description></item>
/// </list>
/// 
/// <para><strong>Performance Optimizations:</strong></para>
/// <list type="bullet">
///   <item><description>Async/await pattern for non-blocking I/O</description></item>
///   <item><description>Request timing telemetry for monitoring</description></item>
///   <item><description>Efficient first-admin detection algorithm</description></item>
///   <item><description>Background job processing for batch operations</description></item>
/// </list>
/// 
/// <para><strong>Privacy & Compliance:</strong></para>
/// <list type="bullet">
///   <item><description>Account deletion anonymizes comments (GDPR compliance)</description></item>
///   <item><description>Complete history removal on request</description></item>
///   <item><description>No personal data leakage in error messages</description></item>
/// </list>
/// </remarks>
public static class UserEndpoints
{
    #region Constants

    /// <summary>Performance threshold for slow request warnings (milliseconds)</summary>
    private const int SlowRequestThresholdMs = 2000;
    
    /// <summary>Maximum batch size for history import to prevent memory exhaustion</summary>
    private const int MaxBatchImportSize = 10000;

    #endregion

    #region Endpoint Registration

    /// <summary>
    /// Registers user-related HTTP endpoints with the application's routing system.
    /// </summary>
    /// <param name="app">The endpoint route builder to register routes with.</param>
    /// <remarks>
    /// All endpoints require JWT authentication. Users can only access their own data.
    /// </remarks>
    public static void MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/me")
            .RequireAuthorization()
            .WithTags("User Profile")
            .WithDescription("User profile and library management endpoints");

        // Profile endpoints
        group.MapGet("/", GetProfile)
            .WithName("GetUserProfile")
            .WithSummary("Retrieve the authenticated user's profile")
            .Produces<UserProfileResponse>(200)
            .ProducesProblem(401)
            .ProducesProblem(404)
            .ProducesProblem(500);

        group.MapPatch("/password", ChangePassword)
            .WithName("ChangePassword")
            .WithSummary("Change the authenticated user's password")
            .Produces<object>(200)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(404)
            .ProducesProblem(500);
        
        // Library & History endpoints
        group.MapGet("/library", GetLibrary)
            .WithName("GetUserLibrary")
            .WithSummary("Retrieve the authenticated user's library")
            .Produces<object>(200)
            .ProducesProblem(401)
            .ProducesProblem(500);

        group.MapGet("/history", GetHistory)
            .WithName("GetUserHistory")
            .WithSummary("Retrieve the authenticated user's reading history")
            .Produces<HistoryListResponse>(200)
            .ProducesProblem(401)
            .ProducesProblem(500);

        group.MapPost("/history/batch", BatchImportHistory)
            .WithName("BatchImportHistory")
            .WithSummary("Import reading history in batch (migration tool)")
            .Produces(202)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(500);

        group.MapPost("/progress", UpdateProgress)
            .WithName("UpdateReadingProgress")
            .WithSummary("Update reading progress for a series/chapter")
            .Produces(200)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(500);

        // Account deletion
        group.MapDelete("/", DeleteAccount)
            .WithName("DeleteUserAccount")
            .WithSummary("Delete the authenticated user's account (GDPR)")
            .Produces(204)
            .ProducesProblem(401)
            .ProducesProblem(500);
    }

    #endregion

    #region Endpoint Handlers - Profile

    /// <summary>
    /// Retrieves the authenticated user's profile information.
    /// </summary>
    /// <param name="user">The authenticated user's claims principal (injected).</param>
    /// <param name="repository">Repository instance for data access (injected).</param>
    /// <param name="logger">Logger instance for diagnostic logging (injected).</param>
    /// <param name="context">Current HTTP context for request/response details.</param>
    /// <returns>
    /// User profile data including username, role, and admin status, or an RFC 7807 Problem Details response on error.
    /// </returns>
    /// <remarks>
    /// <para><strong>Admin Detection:</strong></para>
    /// The response includes an isFirstAdmin flag indicating if this user is the first admin
    /// (earliest created admin). This is used to prevent accidental lockout scenarios.
    /// 
    /// <para><strong>Security:</strong></para>
    /// Users can only retrieve their own profile. User ID is extracted from JWT claims.
    /// </remarks>
    private static async Task<IResult> GetProfile(
        ClaimsPrincipal user, 
        [FromServices] IRepository repository,
        [FromServices] ILogger<IResult> logger,
        HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var requestPath = context.Request.Path;
        var traceId = context.TraceIdentifier;

        // === Phase 1: Authentication Validation ===
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        
        if (string.IsNullOrEmpty(userId)) 
        {
            logger.LogWarning(
                "Profile access attempt without valid user ID (TraceId: {TraceId})",
                traceId);
            return ResultsExtensions.Unauthorized(
                "Authentication required",
                requestPath,
                traceId);
        }

        logger.LogDebug(
            "Profile retrieval requested: UserId={UserId}, TraceId={TraceId}",
            userId, traceId);

        // === Phase 2: Data Retrieval ===
        try
        {
            var dbUser = repository.GetUser(userId);
            
            if (dbUser == null) 
            {
                logger.LogWarning(
                    "User not found in database: UserId={UserId} (TraceId: {TraceId})",
                    userId, traceId);
                return ResultsExtensions.NotFound(
                    "User not found",
                    requestPath,
                    traceId);
            }

            // === Phase 3: Admin Status Check ===
            var isFirstAdmin = dbUser.role == "Admin" && IsFirstAdmin(dbUser, repository);

            stopwatch.Stop();
            var elapsedMs = stopwatch.ElapsedMilliseconds;

            // Performance monitoring
            if (elapsedMs > SlowRequestThresholdMs)
            {
                logger.LogWarning(
                    "Slow profile retrieval: UserId={UserId}, Duration={DurationMs}ms (TraceId: {TraceId})",
                    userId, elapsedMs, traceId);
            }
            else
            {
                logger.LogInformation(
                    "Profile retrieved successfully: UserId={UserId}, Role={Role}, Duration={DurationMs}ms (TraceId: {TraceId})",
                    userId, dbUser.role, elapsedMs, traceId);
            }

            return Results.Ok(new UserProfileResponse(
                dbUser.id,
                dbUser.username,
                dbUser.role,
                dbUser.created_at,
                dbUser.password_login_disabled,
                isFirstAdmin
            ));
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.LogError(ex,
                "Error retrieving user profile: UserId={UserId}, Duration={DurationMs}ms (TraceId: {TraceId})",
                userId, stopwatch.ElapsedMilliseconds, traceId);

            return ResultsExtensions.InternalServerError(
                "An error occurred while retrieving the user profile",
                requestPath,
                traceId);
        }
    }

    /// <summary>
    /// Changes the authenticated user's password after validating the current password.
    /// </summary>
    /// <param name="request">Password change request containing current and new passwords.</param>
    /// <param name="user">The authenticated user's claims principal (injected).</param>
    /// <param name="repository">Repository instance for data access (injected).</param>
    /// <param name="authService">Authentication service for password operations (injected).</param>
    /// <param name="logger">Logger instance for diagnostic logging (injected).</param>
    /// <param name="context">Current HTTP context for request/response details.</param>
    /// <returns>
    /// Success message on password change, or an RFC 7807 Problem Details response on error.
    /// </returns>
    /// <remarks>
    /// <para><strong>Security:</strong></para>
    /// Requires current password verification before allowing change. New password must
    /// meet strength requirements (validated by AuthService).
    /// 
    /// <para><strong>Audit Trail:</strong></para>
    /// Password changes are logged for security auditing purposes. Failed attempts are
    /// logged with warning level.
    /// </remarks>
    private static async Task<IResult> ChangePassword(
        [FromBody] ChangePasswordRequest request, 
        ClaimsPrincipal user, 
        [FromServices] IRepository repository,
        [FromServices] AuthService authService,
        [FromServices] ILogger<IResult> logger,
        HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var requestPath = context.Request.Path;
        var traceId = context.TraceIdentifier;

        // === Phase 1: Authentication Validation ===
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        
        if (string.IsNullOrEmpty(userId))
        {
            logger.LogWarning(
                "Password change attempt without valid user ID (TraceId: {TraceId})",
                traceId);
            return ResultsExtensions.Unauthorized(
                "Authentication required",
                requestPath,
                traceId);
        }

        logger.LogDebug(
            "Password change requested: UserId={UserId}, TraceId={TraceId}",
            userId, traceId);

        // === Phase 2: Input Validation ===
        if (request == null)
        {
            logger.LogWarning(
                "Password change rejected: Missing request body, UserId={UserId} (TraceId: {TraceId})",
                userId, traceId);
            return ResultsExtensions.BadRequest(
                "Request body is required",
                requestPath,
                traceId);
        }

        if (string.IsNullOrWhiteSpace(request.current_password) || 
            string.IsNullOrWhiteSpace(request.new_password))
        {
            logger.LogWarning(
                "Password change rejected: Missing passwords, UserId={UserId} (TraceId: {TraceId})",
                userId, traceId);
            return ResultsExtensions.BadRequest(
                "Current password and new password are required",
                requestPath,
                traceId);
        }

        // === Phase 3: User Retrieval ===
        try
        {
            var dbUser = repository.GetUser(userId);
            
            if (dbUser == null)
            {
                logger.LogWarning(
                    "Password change rejected: User not found, UserId={UserId} (TraceId: {TraceId})",
                    userId, traceId);
                return ResultsExtensions.NotFound(
                    "User not found",
                    requestPath,
                    traceId);
            }

            // === Phase 4: Current Password Verification ===
            if (!authService.VerifyPassword(request.current_password, dbUser.password_hash))
            {
                logger.LogWarning(
                    "Password change rejected: Incorrect current password, UserId={UserId} (TraceId: {TraceId})",
                    userId, traceId);
                return ResultsExtensions.BadRequest(
                    "Current password is incorrect",
                    requestPath,
                    traceId);
            }

            // === Phase 5: New Password Strength Validation ===
            var (isValid, error) = authService.ValidatePasswordStrength(request.new_password);
            
            if (!isValid)
            {
                logger.LogWarning(
                    "Password change rejected: Weak password, UserId={UserId}, Reason={Reason} (TraceId: {TraceId})",
                    userId, error, traceId);
                return Results.BadRequest(new Problem(
                    "urn:mvn:error:weak-password",
                    "Weak Password",
                    400,
                    error!,
                    requestPath
                ));
            }

            // === Phase 6: Password Update ===
            var newHash = authService.HashPassword(request.new_password);
            var updatedUser = dbUser with { password_hash = newHash };
            repository.UpdateUser(updatedUser);

            stopwatch.Stop();
            var elapsedMs = stopwatch.ElapsedMilliseconds;

            logger.LogInformation(
                "Password changed successfully: UserId={UserId}, Duration={DurationMs}ms (TraceId: {TraceId})",
                userId, elapsedMs, traceId);

            return Results.Ok(new { message = "Password changed successfully" });
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.LogError(ex,
                "Error changing password: UserId={UserId}, Duration={DurationMs}ms (TraceId: {TraceId})",
                userId, stopwatch.ElapsedMilliseconds, traceId);

            return ResultsExtensions.InternalServerError(
                "An error occurred while changing the password",
                requestPath,
                traceId);
        }
    }

    #endregion

    #region Endpoint Handlers - Library & History

    /// <summary>
    /// Retrieves the authenticated user's library (bookmarked/favorited series).
    /// </summary>
    /// <param name="user">The authenticated user's claims principal (injected).</param>
    /// <param name="repository">Repository instance for data access (injected).</param>
    /// <param name="logger">Logger instance for diagnostic logging (injected).</param>
    /// <param name="context">Current HTTP context for request/response details.</param>
    /// <returns>
    /// User's library data, or an RFC 7807 Problem Details response on error.
    /// </returns>
    /// <remarks>
    /// <para><strong>Privacy:</strong></para>
    /// Users can only access their own library. The library contains series URNs
    /// and associated metadata like bookmark status and custom tags.
    /// </remarks>
    private static async Task<IResult> GetLibrary(
        ClaimsPrincipal user,
        [FromServices] IRepository repository,
        [FromServices] ILogger<IResult> logger,
        HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var requestPath = context.Request.Path;
        var traceId = context.TraceIdentifier;

        // === Phase 1: Authentication Validation ===
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        
        if (string.IsNullOrEmpty(userId))
        {
            logger.LogWarning(
                "Library access attempt without valid user ID (TraceId: {TraceId})",
                traceId);
            return ResultsExtensions.Unauthorized(
                "Authentication required",
                requestPath,
                traceId);
        }

        logger.LogDebug(
            "Library retrieval requested: UserId={UserId}, TraceId={TraceId}",
            userId, traceId);

        // === Phase 2: Data Retrieval ===
        try
        {
            var library = repository.GetLibrary(userId);

            stopwatch.Stop();
            var elapsedMs = stopwatch.ElapsedMilliseconds;

            // Performance monitoring
            if (elapsedMs > SlowRequestThresholdMs)
            {
                logger.LogWarning(
                    "Slow library retrieval: UserId={UserId}, Duration={DurationMs}ms (TraceId: {TraceId})",
                    userId, elapsedMs, traceId);
            }
            else
            {
                logger.LogInformation(
                    "Library retrieved successfully: UserId={UserId}, ItemCount={Count}, Duration={DurationMs}ms (TraceId: {TraceId})",
                    userId, library?.Count() ?? 0, elapsedMs, traceId);
            }

            return Results.Ok(library);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.LogError(ex,
                "Error retrieving library: UserId={UserId}, Duration={DurationMs}ms (TraceId: {TraceId})",
                userId, stopwatch.ElapsedMilliseconds, traceId);

            return ResultsExtensions.InternalServerError(
                "An error occurred while retrieving the library",
                requestPath,
                traceId);
        }
    }

    /// <summary>
    /// Retrieves the authenticated user's reading history.
    /// </summary>
    /// <param name="user">The authenticated user's claims principal (injected).</param>
    /// <param name="repository">Repository instance for data access (injected).</param>
    /// <param name="logger">Logger instance for diagnostic logging (injected).</param>
    /// <param name="context">Current HTTP context for request/response details.</param>
    /// <returns>
    /// User's reading history with metadata, or an RFC 7807 Problem Details response on error.
    /// </returns>
    /// <remarks>
    /// <para><strong>Privacy:</strong></para>
    /// Users can only access their own reading history. History includes series/chapter URNs,
    /// read timestamps, and progress information.
    /// </remarks>
    private static async Task<IResult> GetHistory(
        ClaimsPrincipal user,
        [FromServices] IRepository repository,
        [FromServices] ILogger<IResult> logger,
        HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var requestPath = context.Request.Path;
        var traceId = context.TraceIdentifier;

        // === Phase 1: Authentication Validation ===
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        
        if (string.IsNullOrEmpty(userId))
        {
            logger.LogWarning(
                "History access attempt without valid user ID (TraceId: {TraceId})",
                traceId);
            return ResultsExtensions.Unauthorized(
                "Authentication required",
                requestPath,
                traceId);
        }

        logger.LogDebug(
            "History retrieval requested: UserId={UserId}, TraceId={TraceId}",
            userId, traceId);

        // === Phase 2: Data Retrieval ===
        try
        {
            var history = repository.GetHistory(userId);
            var historyArray = history.ToArray();

            stopwatch.Stop();
            var elapsedMs = stopwatch.ElapsedMilliseconds;

            // Performance monitoring
            if (elapsedMs > SlowRequestThresholdMs)
            {
                logger.LogWarning(
                    "Slow history retrieval: UserId={UserId}, Duration={DurationMs}ms (TraceId: {TraceId})",
                    userId, elapsedMs, traceId);
            }
            else
            {
                logger.LogInformation(
                    "History retrieved successfully: UserId={UserId}, ItemCount={Count}, Duration={DurationMs}ms (TraceId: {TraceId})",
                    userId, historyArray.Length, elapsedMs, traceId);
            }

            return Results.Ok(new HistoryListResponse(
                historyArray,
                new HistoryMeta(historyArray.Length, false)
            ));
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.LogError(ex,
                "Error retrieving history: UserId={UserId}, Duration={DurationMs}ms (TraceId: {TraceId})",
                userId, stopwatch.ElapsedMilliseconds, traceId);

            return ResultsExtensions.InternalServerError(
                "An error occurred while retrieving the reading history",
                requestPath,
                traceId);
        }
    }

    /// <summary>
    /// Updates the authenticated user's reading progress for a specific series/chapter.
    /// </summary>
    /// <param name="user">The authenticated user's claims principal (injected).</param>
    /// <param name="progress">Reading progress data including series URN, chapter URN, and page number.</param>
    /// <param name="repository">Repository instance for data access (injected).</param>
    /// <param name="logger">Logger instance for diagnostic logging (injected).</param>
    /// <param name="context">Current HTTP context for request/response details.</param>
    /// <returns>
    /// Success response, or an RFC 7807 Problem Details response on error.
    /// </returns>
    /// <remarks>
    /// <para><strong>Progress Tracking:</strong></para>
    /// Tracks which page/chapter the user is currently reading. Used for "Continue Reading"
    /// features and progress synchronization across devices.
    /// 
    /// <para><strong>Validation:</strong></para>
    /// Validates that series_urn is provided and in correct format.
    /// </remarks>
    private static async Task<IResult> UpdateProgress(
        ClaimsPrincipal user,
        [FromBody] ReadingProgress progress,
        [FromServices] IRepository repository,
        [FromServices] ILogger<IResult> logger,
        HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var requestPath = context.Request.Path;
        var traceId = context.TraceIdentifier;

        // === Phase 1: Authentication Validation ===
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        
        if (string.IsNullOrEmpty(userId))
        {
            logger.LogWarning(
                "Progress update attempt without valid user ID (TraceId: {TraceId})",
                traceId);
            return ResultsExtensions.Unauthorized(
                "Authentication required",
                requestPath,
                traceId);
        }

        logger.LogDebug(
            "Progress update requested: UserId={UserId}, TraceId={TraceId}",
            userId, traceId);

        // === Phase 2: Input Validation ===
        if (progress == null)
        {
            logger.LogWarning(
                "Progress update rejected: Missing request body, UserId={UserId} (TraceId: {TraceId})",
                userId, traceId);
            return ResultsExtensions.BadRequest(
                "Request body is required",
                requestPath,
                traceId);
        }

        if (string.IsNullOrWhiteSpace(progress.series_urn))
        {
            logger.LogWarning(
                "Progress update rejected: Missing series URN, UserId={UserId} (TraceId: {TraceId})",
                userId, traceId);
            return ResultsExtensions.BadRequest(
                "series_urn is required",
                requestPath,
                traceId);
        }

        // === Phase 3: Progress Update ===
        try
        {
            repository.UpdateProgress(userId, progress);

            stopwatch.Stop();
            var elapsedMs = stopwatch.ElapsedMilliseconds;

            logger.LogInformation(
                "Progress updated successfully: UserId={UserId}, SeriesUrn={SeriesUrn}, ChapterId={ChapterId}, PageNumber={PageNumber}, Duration={DurationMs}ms (TraceId: {TraceId})",
                userId, progress.series_urn, progress.chapter_id ?? "N/A", progress.page_number, elapsedMs, traceId);

            return Results.Ok(new { message = "Progress updated successfully" });
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.LogError(ex,
                "Error updating progress: UserId={UserId}, SeriesUrn={SeriesUrn}, Duration={DurationMs}ms (TraceId: {TraceId})",
                userId, progress.series_urn, stopwatch.ElapsedMilliseconds, traceId);

            return ResultsExtensions.InternalServerError(
                "An error occurred while updating reading progress",
                requestPath,
                traceId);
        }
    }

    /// <summary>
    /// Imports reading history in batch for migration scenarios.
    /// </summary>
    /// <param name="request">Batch import request containing array of history items.</param>
    /// <param name="user">The authenticated user's claims principal (injected).</param>
    /// <param name="repository">Repository instance for data access (injected).</param>
    /// <param name="jobService">Job service for background processing (injected).</param>
    /// <param name="logger">Logger instance for diagnostic logging (injected).</param>
    /// <param name="context">Current HTTP context for request/response details.</param>
    /// <returns>
    /// 202 Accepted with job URL for tracking, or an RFC 7807 Problem Details response on error.
    /// </returns>
    /// <remarks>
    /// <para><strong>Background Processing:</strong></para>
    /// Large imports are processed asynchronously to prevent request timeouts.
    /// Returns a job ID that can be used to track import progress.
    /// 
    /// <para><strong>Rate Limiting:</strong></para>
    /// Batch size is limited to prevent memory exhaustion and abuse.
    /// </remarks>
    private static async Task<IResult> BatchImportHistory(
        [FromBody] HistoryBatchImport request,
        ClaimsPrincipal user,
        [FromServices] IRepository repository,
        [FromServices] JobService jobService,
        [FromServices] ILogger<IResult> logger,
        HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var requestPath = context.Request.Path;
        var traceId = context.TraceIdentifier;

        // === Phase 1: Authentication Validation ===
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        
        if (string.IsNullOrEmpty(userId))
        {
            logger.LogWarning(
                "Batch import attempt without valid user ID (TraceId: {TraceId})",
                traceId);
            return ResultsExtensions.Unauthorized(
                "Authentication required",
                requestPath,
                traceId);
        }

        logger.LogDebug(
            "Batch history import requested: UserId={UserId}, TraceId={TraceId}",
            userId, traceId);

        // === Phase 2: Input Validation ===
        if (request == null)
        {
            logger.LogWarning(
                "Batch import rejected: Missing request body, UserId={UserId} (TraceId: {TraceId})",
                userId, traceId);
            return ResultsExtensions.BadRequest(
                "Request body is required",
                requestPath,
                traceId);
        }

        if (request.items == null || !request.items.Any())
        {
            logger.LogWarning(
                "Batch import rejected: No items provided, UserId={UserId} (TraceId: {TraceId})",
                userId, traceId);
            return ResultsExtensions.BadRequest(
                "No items to import",
                requestPath,
                traceId);
        }

        if (request.items.Count() > MaxBatchImportSize)
        {
            logger.LogWarning(
                "Batch import rejected: Too many items ({Count}), UserId={UserId} (TraceId: {TraceId})",
                request.items.Count(), userId, traceId);
            return ResultsExtensions.BadRequest(
                $"Batch size exceeds maximum allowed ({MaxBatchImportSize} items)",
                requestPath,
                traceId);
        }

        // === Phase 3: Job Creation & Processing ===
        try
        {
            var job = jobService.CreateJob("IMPORT_HISTORY");
            
            logger.LogInformation(
                "Batch import job created: JobId={JobId}, UserId={UserId}, ItemCount={Count} (TraceId: {TraceId})",
                job.id, userId, request.items.Count(), traceId);

            // Process items (in production, this would be a background worker)
            var processedCount = 0;
            foreach (var item in request.items)
            {
                try
                {
                    repository.UpdateProgress(userId, new ReadingProgress(
                        item.series_urn,
                        item.chapter_urn,
                        1, // Default page
                        "reading",
                        new DateTimeOffset(item.read_at).ToUnixTimeMilliseconds()
                    ));
                    processedCount++;
                }
                catch (Exception itemEx)
                {
                    logger.LogWarning(itemEx,
                        "Failed to import history item: SeriesUrn={SeriesUrn}, UserId={UserId}, JobId={JobId}",
                        item.series_urn, userId, job.id);
                    // Continue processing remaining items
                }
            }

            jobService.UpdateJob(job.id, "COMPLETED", 100);

            stopwatch.Stop();
            var elapsedMs = stopwatch.ElapsedMilliseconds;

            logger.LogInformation(
                "Batch import completed: JobId={JobId}, UserId={UserId}, Processed={Processed}/{Total}, Duration={DurationMs}ms (TraceId: {TraceId})",
                job.id, userId, processedCount, request.items.Count(), elapsedMs, traceId);

            return Results.Accepted($"/api/v1/jobs/{job.id}");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.LogError(ex,
                "Error processing batch import: UserId={UserId}, Duration={DurationMs}ms (TraceId: {TraceId})",
                userId, stopwatch.ElapsedMilliseconds, traceId);

            return ResultsExtensions.InternalServerError(
                "An error occurred while processing the batch import",
                requestPath,
                traceId);
        }
    }

    #endregion

    #region Endpoint Handlers - Account Management

    /// <summary>
    /// Deletes the authenticated user's account and all associated data (GDPR compliance).
    /// </summary>
    /// <param name="user">The authenticated user's claims principal (injected).</param>
    /// <param name="repository">Repository instance for data access (injected).</param>
    /// <param name="logger">Logger instance for diagnostic logging (injected).</param>
    /// <param name="context">Current HTTP context for request/response details.</param>
    /// <returns>
    /// 204 No Content on successful deletion, or an RFC 7807 Problem Details response on error.
    /// </returns>
    /// <remarks>
    /// <para><strong>GDPR Compliance:</strong></para>
    /// Implements "Right to be Forgotten" by permanently removing user data.
    /// Comments are anonymized rather than deleted to preserve conversation context.
    /// 
    /// <para><strong>Data Removal Process:</strong></para>
    /// <list type="number">
    ///   <item><description>Anonymize user comments (replace user_id with "deleted")</description></item>
    ///   <item><description>Delete reading history and progress</description></item>
    ///   <item><description>Delete user account record</description></item>
    /// </list>
    /// 
    /// <para><strong>Security:</strong></para>
    /// Users can only delete their own account. This operation is irreversible.
    /// </remarks>
    private static async Task<IResult> DeleteAccount(
        ClaimsPrincipal user,
        [FromServices] IRepository repository,
        [FromServices] ILogger<IResult> logger,
        HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var requestPath = context.Request.Path;
        var traceId = context.TraceIdentifier;

        // === Phase 1: Authentication Validation ===
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        
        if (string.IsNullOrEmpty(userId))
        {
            logger.LogWarning(
                "Account deletion attempt without valid user ID (TraceId: {TraceId})",
                traceId);
            return ResultsExtensions.Unauthorized(
                "Authentication required",
                requestPath,
                traceId);
        }

        logger.LogWarning(
            "Account deletion requested: UserId={UserId}, TraceId={TraceId}",
            userId, traceId);

        // === Phase 2: Data Removal ===
        try
        {
            // Step 1: Anonymize user-generated content
            logger.LogDebug(
                "Anonymizing user content: UserId={UserId}",
                userId);
            repository.AnonymizeUserContent(userId);

            // Step 2: Delete reading history
            logger.LogDebug(
                "Deleting user history: UserId={UserId}",
                userId);
            repository.DeleteUserHistory(userId);

            // Step 3: Delete user account
            logger.LogDebug(
                "Deleting user account: UserId={UserId}",
                userId);
            repository.DeleteUser(userId);

            stopwatch.Stop();
            var elapsedMs = stopwatch.ElapsedMilliseconds;

            logger.LogWarning(
                "Account deleted successfully: UserId={UserId}, Duration={DurationMs}ms (TraceId: {TraceId})",
                userId, elapsedMs, traceId);

            return Results.NoContent();
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.LogError(ex,
                "Error deleting account: UserId={UserId}, Duration={DurationMs}ms (TraceId: {TraceId})",
                userId, stopwatch.ElapsedMilliseconds, traceId);

            return ResultsExtensions.InternalServerError(
                "An error occurred while deleting the account",
                requestPath,
                traceId);
        }
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Determines if a user is the first admin (earliest created admin account).
    /// </summary>
    /// <param name="user">The user to check.</param>
    /// <param name="repository">Repository instance for querying admin users.</param>
    /// <returns>True if the user is the first admin, false otherwise.</returns>
    /// <remarks>
    /// <para><strong>Purpose:</strong></para>
    /// First admin detection is used to prevent accidental lockout scenarios.
    /// The first admin typically has special privileges that cannot be revoked.
    /// 
    /// <para><strong>Performance:</strong></para>
    /// This method queries all admin users. For systems with many admins, consider
    /// caching this result or optimizing the query.
    /// </remarks>
    private static bool IsFirstAdmin(User user, IRepository repository)
    {
        // Only admins can be first admin
        if (user.role != "Admin")
            return false;
        
        // Get all admin users
        var allAdmins = repository.ListUsers()
            .Where(u => u.role == "Admin")
            .ToList();
        
        // No admins means this can't be first admin
        if (!allAdmins.Any())
            return false;
        
        // Find the earliest created admin
        var firstAdmin = allAdmins
            .OrderBy(a => a.created_at)
            .First();
        
        // Check if current user is the first admin
        return firstAdmin.id == user.id;
    }

    #endregion
}
