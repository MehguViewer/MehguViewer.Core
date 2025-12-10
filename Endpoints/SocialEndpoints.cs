using System.Security.Claims;
using MehguViewer.Core.Shared;
using MehguViewer.Core.Services;
using MehguViewer.Core.Workers;
using MehguViewer.Core.Helpers;
using MehguViewer.Core.Infrastructures;
using Microsoft.AspNetCore.Mvc;

namespace MehguViewer.Core.Endpoints;

/// <summary>
/// Provides HTTP endpoints for social features including comments and voting/rating systems.
/// </summary>
/// <remarks>
/// <para><strong>Purpose:</strong> Enables user interaction through comments and votes on series/units.</para>
/// 
/// <para><strong>Endpoints:</strong></para>
/// <list type="bullet">
///   <item><description>GET /api/v1/comments - List comments for a target resource</description></item>
///   <item><description>POST /api/v1/comments - Create a new comment (requires MvnSocial permission)</description></item>
///   <item><description>POST /api/v1/votes - Cast or update a vote (requires MvnSocial permission)</description></item>
/// </list>
/// 
/// <para><strong>Authorization:</strong></para>
/// <list type="bullet">
///   <item><description>Read operations (ListComments): Public access</description></item>
///   <item><description>Write operations (CreateComment, CastVote): Requires MvnSocial policy</description></item>
/// </list>
/// 
/// <para><strong>Security Considerations:</strong></para>
/// <list type="bullet">
///   <item><description>All URNs validated using UrnHelper</description></item>
///   <item><description>Comment content sanitized to prevent XSS</description></item>
///   <item><description>Rate limiting applied via middleware</description></item>
///   <item><description>User context extracted from JWT claims</description></item>
/// </list>
/// </remarks>
public static class SocialEndpoints
{
    #region Constants
    
    private const int MaxCommentLength = 10000;
    private const int MinCommentLength = 1;
    private const int MaxVoteValue = 1;
    private const int MinVoteValue = -1;
    
    #endregion

    #region Endpoint Mapping
    
    /// <summary>
    /// Maps social feature endpoints to the application's route builder.
    /// </summary>
    /// <param name="app">The endpoint route builder to add routes to.</param>
    public static void MapSocialEndpoints(this IEndpointRouteBuilder app)
    {
        var comments = app.MapGroup("/api/v1/comments");
        comments.MapGet("/", ListComments)
            .WithName("ListComments");
        comments.MapPost("/", CreateComment)
            .RequireAuthorization("MvnSocial")
            .WithName("CreateComment");

        var votes = app.MapGroup("/api/v1/votes")
            .RequireAuthorization("MvnSocial");
        votes.MapPost("/", CastVote)
            .WithName("CastVote");
    }
    
    #endregion

    #region List Comments
    
    /// <summary>
    /// Retrieves comments for a specified target resource with optional filtering and pagination.
    /// </summary>
    /// <param name="target_urn">URN of the target resource (series, unit, etc.).</param>
    /// <param name="depth">Optional depth for nested comment threads (not yet implemented).</param>
    /// <param name="cursor">Optional pagination cursor for fetching next page of results.</param>
    /// <param name="repo">Repository instance injected by DI.</param>
    /// <param name="loggerFactory">Logger factory for creating scoped loggers.</param>
    /// <param name="context">HTTP context for request metadata.</param>
    /// <returns>OK result with comment list and pagination metadata, or BadRequest if validation fails.</returns>
    /// <remarks>
    /// <para><strong>Validation:</strong></para>
    /// <list type="bullet">
    ///   <item><description>target_urn must be provided and non-empty</description></item>
    ///   <item><description>target_urn must be a valid URN format</description></item>
    /// </list>
    /// 
    /// <para><strong>Logging:</strong></para>
    /// <list type="bullet">
    ///   <item><description>Debug: Log entry with parameters</description></item>
    ///   <item><description>Info: Successful query with result count</description></item>
    ///   <item><description>Warning: Invalid URN or empty target</description></item>
    /// </list>
    /// </remarks>
    private static async Task<IResult> ListComments(
        [FromQuery] string target_urn, 
        [FromQuery] int? depth, 
        [FromQuery] string? cursor, 
        [FromServices] IRepository repo,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext context)
    {
        var logger = loggerFactory.CreateLogger("MehguViewer.Core.Endpoints.SocialEndpoints");
        logger.LogDebug("ListComments invoked - TargetUrn: {TargetUrn}, Depth: {Depth}, Cursor: {Cursor}", 
            target_urn, depth, cursor);

        await Task.CompletedTask;

        // Validation: Check if target_urn is provided
        if (string.IsNullOrWhiteSpace(target_urn))
        {
            logger.LogWarning("ListComments failed - Target URN is missing");
            return ResultsExtensions.BadRequest("Target URN is required", context.Request.Path);
        }

        // Validation: Validate URN format for security
        if (!UrnHelper.TryParse(target_urn, out _))
        {
            logger.LogWarning("ListComments failed - Invalid URN format: {TargetUrn}", target_urn);
            return ResultsExtensions.BadRequest("Invalid target URN format", context.Request.Path);
        }

        // Retrieve comments from repository
        var comments = repo.GetComments(target_urn).ToArray();
        
        logger.LogInformation("ListComments succeeded - TargetUrn: {TargetUrn}, Count: {Count}", 
            target_urn, comments.Length);

        return Results.Ok(new CommentListResponse(comments, new CursorPagination(null, false)));
    }
    
    #endregion

    #region Create Comment
    
    /// <summary>
    /// Creates a new comment on a target resource (series, unit, etc.).
    /// </summary>
    /// <param name="request">Comment creation request containing target URN and markdown content.</param>
    /// <param name="user">Authenticated user principal from JWT.</param>
    /// <param name="repo">Repository instance injected by DI.</param>
    /// <param name="loggerFactory">Logger factory for creating scoped loggers.</param>
    /// <param name="context">HTTP context for request metadata.</param>
    /// <returns>Created result with new comment, or error result if validation fails.</returns>
    /// <remarks>
    /// <para><strong>Authorization:</strong> Requires MvnSocial policy (authenticated user with social permissions).</para>
    /// 
    /// <para><strong>Validation:</strong></para>
    /// <list type="bullet">
    ///   <item><description>User must be authenticated (checked by authorization policy)</description></item>
    ///   <item><description>target_urn must be provided and valid URN format</description></item>
    ///   <item><description>Comment body must be between 1 and 10,000 characters</description></item>
    ///   <item><description>Markdown content sanitized to prevent XSS</description></item>
    /// </list>
    /// 
    /// <para><strong>Logging:</strong></para>
    /// <list type="bullet">
    ///   <item><description>Debug: Log entry with sanitized parameters</description></item>
    ///   <item><description>Info: Comment created successfully with ID and target</description></item>
    ///   <item><description>Warning: Validation failures, missing authentication</description></item>
    ///   <item><description>Error: Repository failures or unexpected exceptions</description></item>
    /// </list>
    /// </remarks>
    private static async Task<IResult> CreateComment(
        [FromBody] CommentCreate request, 
        ClaimsPrincipal user, 
        [FromServices] IRepository repo,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext context)
    {
        var logger = loggerFactory.CreateLogger("MehguViewer.Core.Endpoints.SocialEndpoints");
        
        await Task.CompletedTask;

        // Extract user identity from claims
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        var username = user.FindFirstValue(ClaimTypes.Name);
        
        logger.LogDebug("CreateComment invoked - UserId: {UserId}, TargetUrn: {TargetUrn}, BodyLength: {BodyLength}", 
            userId, request?.target_urn, request?.body_markdown?.Length ?? 0);

        // Validation: Check authentication
        if (string.IsNullOrEmpty(userId))
        {
            logger.LogWarning("CreateComment failed - User not authenticated");
            return ResultsExtensions.Unauthorized("Authentication required", context.Request.Path);
        }

        // Validation: Check request object
        if (request == null)
        {
            logger.LogWarning("CreateComment failed - Request body is null");
            return ResultsExtensions.BadRequest("Request body is required", context.Request.Path);
        }

        // Validation: Check target URN
        if (string.IsNullOrWhiteSpace(request.target_urn))
        {
            logger.LogWarning("CreateComment failed - Target URN is missing - UserId: {UserId}", userId);
            return ResultsExtensions.BadRequest("Target URN is required", context.Request.Path);
        }

        // Validation: Validate URN format
        if (!UrnHelper.TryParse(request.target_urn, out _))
        {
            logger.LogWarning("CreateComment failed - Invalid URN format: {TargetUrn} - UserId: {UserId}", 
                request.target_urn, userId);
            return ResultsExtensions.BadRequest("Invalid target URN format", context.Request.Path);
        }

        // Validation: Check comment body
        if (string.IsNullOrWhiteSpace(request.body_markdown))
        {
            logger.LogWarning("CreateComment failed - Comment body is empty - UserId: {UserId}", userId);
            return ResultsExtensions.BadRequest("Comment body is required", context.Request.Path);
        }

        // Validation: Check comment length
        if (request.body_markdown.Length < MinCommentLength || request.body_markdown.Length > MaxCommentLength)
        {
            logger.LogWarning("CreateComment failed - Invalid comment length: {Length} (allowed: {Min}-{Max}) - UserId: {UserId}", 
                request.body_markdown.Length, MinCommentLength, MaxCommentLength, userId);
            return ResultsExtensions.BadRequest(
                $"Comment body must be between {MinCommentLength} and {MaxCommentLength} characters", 
                context.Request.Path);
        }

        // Security: Sanitize markdown content (basic HTML entity encoding for XSS prevention)
        var sanitizedContent = System.Net.WebUtility.HtmlEncode(request.body_markdown);

        try
        {
            // Create comment entity
            var comment = new Comment(
                UrnHelper.CreateCommentUrn(),
                sanitizedContent,
                new AuthorSnapshot(userId, username ?? "User", "", "user"),
                DateTime.UtcNow,
                0
            );

            // Persist to repository
            repo.AddComment(comment);
            
            logger.LogInformation("CreateComment succeeded - CommentId: {CommentId}, TargetUrn: {TargetUrn}, UserId: {UserId}", 
                comment.id, request.target_urn, userId);
            
            return Results.Created($"/api/v1/comments?target_urn={request.target_urn}", comment);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "CreateComment failed - Unexpected error - TargetUrn: {TargetUrn}, UserId: {UserId}", 
                request.target_urn, userId);
            return ResultsExtensions.InternalServerError("Failed to create comment", context.Request.Path);
        }
    }
    
    #endregion

    #region Cast Vote
    
    /// <summary>
    /// Casts or updates a vote/rating on a target resource (series, unit, comment, etc.).
    /// </summary>
    /// <param name="vote">Vote data containing target ID, type, and value.</param>
    /// <param name="user">Authenticated user principal from JWT.</param>
    /// <param name="repo">Repository instance injected by DI.</param>
    /// <param name="loggerFactory">Logger factory for creating scoped loggers.</param>
    /// <param name="context">HTTP context for request metadata.</param>
    /// <returns>OK result if vote recorded successfully, or error result if validation fails.</returns>
    /// <remarks>
    /// <para><strong>Authorization:</strong> Requires MvnSocial policy (authenticated user with social permissions).</para>
    /// 
    /// <para><strong>Validation:</strong></para>
    /// <list type="bullet">
    ///   <item><description>User must be authenticated (checked by authorization policy)</description></item>
    ///   <item><description>target_id must be provided and valid URN format</description></item>
    ///   <item><description>Vote value must be within allowed range (-1 to 1: downvote, neutral, upvote)</description></item>
    ///   <item><description>Duplicate votes from same user are updated (upsert behavior)</description></item>
    /// </list>
    /// 
    /// <para><strong>Vote Values:</strong></para>
    /// <list type="bullet">
    ///   <item><description>-1: Downvote/Negative</description></item>
    ///   <item><description>0: Neutral/Remove vote</description></item>
    ///   <item><description>1: Upvote/Positive</description></item>
    /// </list>
    /// 
    /// <para><strong>Logging:</strong></para>
    /// <list type="bullet">
    ///   <item><description>Debug: Log entry with vote details</description></item>
    ///   <item><description>Info: Vote recorded successfully</description></item>
    ///   <item><description>Warning: Validation failures, invalid vote value</description></item>
    ///   <item><description>Error: Repository failures or unexpected exceptions</description></item>
    /// </list>
    /// </remarks>
    private static async Task<IResult> CastVote(
        [FromBody] Vote vote, 
        ClaimsPrincipal user, 
        [FromServices] IRepository repo,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext context)
    {
        var logger = loggerFactory.CreateLogger("MehguViewer.Core.Endpoints.SocialEndpoints");
        
        await Task.CompletedTask;

        // Extract user identity from claims
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        
        logger.LogDebug("CastVote invoked - UserId: {UserId}, TargetId: {TargetId}, Value: {Value}", 
            userId, vote?.target_id, vote?.value);

        // Validation: Check authentication
        if (string.IsNullOrEmpty(userId))
        {
            logger.LogWarning("CastVote failed - User not authenticated");
            return ResultsExtensions.Unauthorized("Authentication required", context.Request.Path);
        }

        // Validation: Check vote object
        if (vote == null)
        {
            logger.LogWarning("CastVote failed - Request body is null - UserId: {UserId}", userId);
            return ResultsExtensions.BadRequest("Request body is required", context.Request.Path);
        }

        // Validation: Check target ID
        if (string.IsNullOrWhiteSpace(vote.target_id))
        {
            logger.LogWarning("CastVote failed - Target ID is missing - UserId: {UserId}", userId);
            return ResultsExtensions.BadRequest("Target ID is required", context.Request.Path);
        }

        // Validation: Validate URN format
        if (!UrnHelper.TryParse(vote.target_id, out _))
        {
            logger.LogWarning("CastVote failed - Invalid URN format: {TargetId} - UserId: {UserId}", 
                vote.target_id, userId);
            return ResultsExtensions.BadRequest("Invalid target ID format", context.Request.Path);
        }

        // Validation: Check vote value range
        if (vote.value < MinVoteValue || vote.value > MaxVoteValue)
        {
            logger.LogWarning("CastVote failed - Invalid vote value: {Value} (allowed: {Min} to {Max}) - UserId: {UserId}", 
                vote.value, MinVoteValue, MaxVoteValue, userId);
            return ResultsExtensions.BadRequest(
                $"Vote value must be between {MinVoteValue} and {MaxVoteValue}", 
                context.Request.Path);
        }

        try
        {
            // Persist vote to repository (upsert behavior)
            repo.AddVote(userId, vote);
            
            logger.LogInformation("CastVote succeeded - UserId: {UserId}, TargetId: {TargetId}, Value: {Value}", 
                userId, vote.target_id, vote.value);
            
            return Results.Ok(new { success = true, message = "Vote recorded successfully" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "CastVote failed - Unexpected error - TargetId: {TargetId}, UserId: {UserId}", 
                vote.target_id, userId);
            return ResultsExtensions.InternalServerError("Failed to record vote", context.Request.Path);
        }
    }
    
    #endregion
}
