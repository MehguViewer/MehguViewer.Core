using MehguViewer.Core.Shared;
using MehguViewer.Core.Services;
using MehguViewer.Core.Workers;
using MehguViewer.Core.Helpers;
using MehguViewer.Core.Infrastructures;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace MehguViewer.Core.Endpoints;

/// <summary>
/// Provides comprehensive HTTP endpoints for system administration, configuration,
/// user management, and database operations in the MehguViewer system.
/// </summary>
/// <remarks>
/// <para><strong>System Management:</strong></para>
/// This endpoint class handles critical system-level operations including:
/// <list type="bullet">
///   <item><description>System configuration and setup status</description></item>
///   <item><description>User management (CRUD operations for users)</description></item>
///   <item><description>Database configuration and connection testing</description></item>
///   <item><description>Storage and cache management</description></item>
///   <item><description>Taxonomy configuration and validation</description></item>
///   <item><description>System statistics and monitoring</description></item>
///   <item><description>Data export/import operations</description></item>
///   <item><description>Logs management</description></item>
/// </list>
/// 
/// <para><strong>Security Features:</strong></para>
/// <list type="bullet">
///   <item><description>Role-based authorization (MvnAdmin, MvnRead, MvnSocial)</description></item>
///   <item><description>First admin protection for critical operations</description></item>
///   <item><description>Passkey and password-based authentication</description></item>
///   <item><description>Setup-aware security (some endpoints accessible before setup)</description></item>
///   <item><description>Request logging and audit trails</description></item>
///   <item><description>Input validation and sanitization</description></item>
/// </list>
/// 
/// <para><strong>Performance Optimizations:</strong></para>
/// <list type="bullet">
///   <item><description>Async/await pattern for non-blocking I/O</description></item>
///   <item><description>Diagnostic timing for slow request detection</description></item>
///   <item><description>Efficient file system operations</description></item>
///   <item><description>Optimized database queries</description></item>
/// </list>
/// </remarks>
public static class SystemEndpoints
{
    #region Constants

    /// <summary>Performance threshold for slow request warnings (milliseconds)</summary>
    private const int SlowRequestThresholdMs = 2000;
    
    /// <summary>Default maximum number of log entries to return</summary>
    private const int DefaultLogCount = 100;
    
    /// <summary>Minimum allowed thumbnail size in pixels</summary>
    private const int MinThumbnailSize = 100;
    
    /// <summary>Maximum allowed thumbnail size in pixels</summary>
    private const int MaxThumbnailSize = 500;
    
    /// <summary>Minimum allowed web image size in pixels</summary>
    private const int MinWebSize = 800;
    
    /// <summary>Maximum allowed web image size in pixels</summary>
    private const int MaxWebSize = 2000;
    
    /// <summary>Minimum allowed JPEG quality (1-100)</summary>
    private const int MinJpegQuality = 50;
    
    /// <summary>Maximum allowed JPEG quality (1-100)</summary>
    private const int MaxJpegQuality = 100;
    
    /// <summary>Default thumbnail size in pixels</summary>
    private const int DefaultThumbnailSize = 200;
    
    /// <summary>Default web image size in pixels</summary>
    private const int DefaultWebSize = 1200;
    
    /// <summary>Default JPEG quality (1-100)</summary>
    private const int DefaultJpegQuality = 85;
    
    /// <summary>Default storage path for data files</summary>
    private const string DefaultStoragePath = "./data";
    
    /// <summary>Embedded PostgreSQL version</summary>
    private const string EmbeddedPostgresVersion = "15.3.0";

    #endregion

    #region Storage Settings (Configurable)

    // Storage settings (loaded from configuration, persisted to appsettings.json)
    private static int _thumbnailSize = DefaultThumbnailSize;
    private static int _webSize = DefaultWebSize;
    private static int _jpegQuality = DefaultJpegQuality;
    private static string _storagePath = DefaultStoragePath;
    private static long _cacheBytes = 0;
    private static IConfiguration? _configuration;

    #endregion

    #region Endpoint Registration

    /// <summary>
    /// Registers system-related HTTP endpoints with the application's routing system.
    /// </summary>
    /// <param name="app">The endpoint route builder to register routes with.</param>
    /// <remarks>
    /// Endpoints are organized into logical groups:
    /// - Public endpoints (node metadata, instance info, taxonomy)
    /// - Setup endpoints (accessible during initial setup)
    /// - User management (MvnAdmin required)
    /// - System administration (MvnAdmin required)
    /// - Database configuration (setup-aware or MvnAdmin)
    /// - Logs management (MvnAdmin required)
    /// - Export/Import operations (MvnAdmin required)
    /// </remarks>
    public static void MapSystemEndpoints(this IEndpointRouteBuilder app)
    {
        // Public endpoints (no authentication required)
        app.MapGet("/.well-known/mehgu-node", GetNodeMetadata)
            .WithName("GetNodeMetadata")
            .WithTags("System")
            .WithSummary("Get node metadata for federation discovery");
            
        app.MapGet("/api/v1/instance", GetInstance)
            .WithName("GetInstance")
            .WithTags("System")
            .WithSummary("Get node instance information");
            
        app.MapGet("/api/v1/taxonomy", GetTaxonomy)
            .WithName("GetTaxonomy")
            .WithTags("System")
            .WithSummary("Get taxonomy data (tags, authors, content warnings)");
            
        app.MapGet("/api/v1/system/setup-status", GetSetupStatus)
            .WithName("GetSetupStatus")
            .WithTags("System")
            .WithSummary("Check if initial setup is complete");

        // System configuration endpoints
        app.MapGet("/api/v1/system/config", GetSystemConfig)
            .RequireAuthorization("MvnRead")
            .WithName("GetSystemConfig")
            .WithTags("System", "Configuration");
            
        app.MapGet("/api/v1/admin/configuration", GetSystemConfig)
            .RequireAuthorization("MvnAdmin")
            .WithName("GetSystemConfigAdmin")
            .WithTags("Admin", "Configuration");
            
        app.MapPatch("/api/v1/admin/configuration", PatchSystemConfig)
            .RequireAuthorization("MvnAdmin")
            .WithName("PatchSystemConfig")
            .WithTags("Admin", "Configuration")
            .WithSummary("Partially update system configuration");
            
        app.MapPut("/api/v1/system/config", UpdateSystemConfig)
            .RequireAuthorization("MvnAdmin")
            .WithName("UpdateSystemConfig")
            .WithTags("Admin", "Configuration");
            
        app.MapPut("/api/v1/system/metadata", UpdateNodeMetadata)
            .RequireAuthorization("MvnAdmin")
            .WithName("UpdateNodeMetadata")
            .WithTags("Admin", "Configuration");

        // Initial setup endpoint (no auth required during setup)
        app.MapPost("/api/v1/system/admin/password", CreateAdminUser)
            .WithName("CreateAdminUser")
            .WithTags("Setup")
            .WithSummary("Create the first admin user during initial setup");

        // User management endpoints
        app.MapPost("/api/v1/users", CreateUser)
            .RequireAuthorization("MvnAdmin")
            .WithName("CreateUser")
            .WithTags("Admin", "Users");
            
        app.MapGet("/api/v1/users", ListUsers)
            .RequireAuthorization("MvnAdmin")
            .WithName("ListUsers")
            .WithTags("Admin", "Users");
            
        app.MapPatch("/api/v1/users/{id}", UpdateUser)
            .RequireAuthorization("MvnAdmin")
            .WithName("UpdateUser")
            .WithTags("Admin", "Users");
            
        app.MapDelete("/api/v1/users/{id}", DeleteUser)
            .RequireAuthorization("MvnAdmin")
            .WithName("DeleteUser")
            .WithTags("Admin", "Users");

        // System statistics and monitoring
        app.MapGet("/api/v1/admin/stats", GetSystemStats)
            .RequireAuthorization("MvnAdmin")
            .WithName("GetSystemStats")
            .WithTags("Admin", "Monitoring");
            
        app.MapGet("/api/v1/admin/storage", GetStorageStats)
            .RequireAuthorization("MvnAdmin")
            .WithName("GetStorageStats")
            .WithTags("Admin", "Storage");
            
        app.MapPatch("/api/v1/admin/storage", UpdateStorageSettings)
            .RequireAuthorization("MvnAdmin")
            .WithName("UpdateStorageSettings")
            .WithTags("Admin", "Storage");
            
        app.MapPost("/api/v1/admin/storage/clear-cache", ClearCache)
            .RequireAuthorization("MvnAdmin")
            .WithName("ClearCache")
            .WithTags("Admin", "Storage");
            
        app.MapPost("/api/v1/system/refresh-cache", RefreshCache)
            .RequireAuthorization("MvnRead")
            .WithName("RefreshCache")
            .WithTags("System", "Cache");

        // Reports (social feature)
        app.MapPost("/api/v1/reports", CreateReport)
            .RequireAuthorization("MvnSocial")
            .WithName("CreateReport")
            .WithTags("Social");
        
        // Logs endpoints
        app.MapGet("/api/v1/admin/logs", GetLogs)
            .RequireAuthorization("MvnAdmin")
            .WithName("GetLogs")
            .WithTags("Admin", "Logs");
            
        app.MapDelete("/api/v1/admin/logs", ClearLogs)
            .RequireAuthorization("MvnAdmin")
            .WithName("ClearLogs")
            .WithTags("Admin", "Logs");
        
        // Database configuration endpoints (accessible during setup or with admin auth)
        app.MapPost("/api/v1/system/database/test", TestDatabaseConnection)
            .WithName("TestDatabaseConnection")
            .WithTags("Setup", "Database");
            
        app.MapPost("/api/v1/system/database/configure", ConfigureDatabase)
            .WithName("ConfigureDatabase")
            .WithTags("Setup", "Database");
            
        app.MapGet("/api/v1/system/database/embedded-status", GetEmbeddedDatabaseStatus)
            .WithName("GetEmbeddedDatabaseStatus")
            .WithTags("Setup", "Database");
            
        app.MapPost("/api/v1/system/database/use-embedded", UseEmbeddedDatabase)
            .WithName("UseEmbeddedDatabase")
            .WithTags("Setup", "Database");
        
        // Admin-only database reconfiguration (after setup is complete)
        app.MapPost("/api/v1/admin/database/test", TestDatabaseConnection)
            .RequireAuthorization("MvnAdmin")
            .WithName("TestDatabaseConnectionAdmin")
            .WithTags("Admin", "Database");
            
        app.MapPost("/api/v1/admin/database/configure", ConfigureDatabase)
            .RequireAuthorization("MvnAdmin")
            .WithName("ConfigureDatabaseAdmin")
            .WithTags("Admin", "Database");
            
        app.MapGet("/api/v1/admin/database/embedded-status", GetEmbeddedDatabaseStatus)
            .RequireAuthorization("MvnAdmin")
            .WithName("GetEmbeddedDatabaseStatusAdmin")
            .WithTags("Admin", "Database");
            
        app.MapPost("/api/v1/admin/database/use-embedded", UseEmbeddedDatabase)
            .RequireAuthorization("MvnAdmin")
            .WithName("UseEmbeddedDatabaseAdmin")
            .WithTags("Admin", "Database");
        
        // Danger zone endpoints (first admin only)
        app.MapPost("/api/v1/admin/reset-data", ResetAllData)
            .RequireAuthorization("MvnAdmin")
            .WithName("ResetAllData")
            .WithTags("Admin", "DangerZone")
            .WithSummary("Reset all data (first admin only)");
            
        app.MapPost("/api/v1/admin/reset-database", ResetDatabase)
            .RequireAuthorization("MvnAdmin")
            .WithName("ResetDatabase")
            .WithTags("Admin", "DangerZone")
            .WithSummary("Reset database schema (first admin only)");
        
        // Taxonomy configuration endpoints
        app.MapGet("/api/v1/admin/taxonomy", GetTaxonomyConfig)
            .RequireAuthorization("MvnAdmin")
            .WithName("GetTaxonomyConfig")
            .WithTags("Admin", "Taxonomy");
            
        app.MapPut("/api/v1/admin/taxonomy", UpdateTaxonomyConfig)
            .RequireAuthorization("MvnAdmin")
            .WithName("UpdateTaxonomyConfig")
            .WithTags("Admin", "Taxonomy");
            
        app.MapPatch("/api/v1/admin/taxonomy", PatchTaxonomyConfig)
            .RequireAuthorization("MvnAdmin")
            .WithName("PatchTaxonomyConfig")
            .WithTags("Admin", "Taxonomy");
            
        app.MapPost("/api/v1/admin/taxonomy/validate", RunTaxonomyValidation)
            .RequireAuthorization("MvnAdmin")
            .WithName("RunTaxonomyValidation")
            .WithTags("Admin", "Taxonomy");
        
        // Export/Import endpoints
        app.MapGet("/api/v1/admin/export/series", ExportAllSeries)
            .RequireAuthorization("MvnAdmin")
            .WithName("ExportAllSeries")
            .WithTags("Admin", "Export");
            
        app.MapPost("/api/v1/admin/export/series-to-files", ExportSeriesToFiles)
            .RequireAuthorization("MvnAdmin")
            .WithName("ExportSeriesToFiles")
            .WithTags("Admin", "Export");
    }

    #endregion

    #region Export/Import Endpoints

    /// <summary>
    /// Exports all series data as JSON.
    /// </summary>
    /// <param name="repo">Repository instance for data access.</param>
    /// <param name="logger">Logger instance for diagnostic logging.</param>
    /// <param name="context">Current HTTP context.</param>
    /// <returns>JSON array containing all series data.</returns>
    private static async Task<IResult> ExportAllSeries(
        [FromServices] IRepository repo,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext context)
    {
        var logger = loggerFactory.CreateLogger("MehguViewer.Core.Endpoints.SystemEndpoints");
        var stopwatch = Stopwatch.StartNew();
        var traceId = context.TraceIdentifier;
        
        await Task.CompletedTask;
        
        logger.LogInformation("Exporting all series data (TraceId: {TraceId})", traceId);
        
        try
        {
            var series = repo.ListSeries().ToArray();
            
            stopwatch.Stop();
            logger.LogInformation(
                "Series export completed: Count={Count}, Duration={DurationMs}ms (TraceId: {TraceId})",
                series.Length, stopwatch.ElapsedMilliseconds, traceId);
            
            return Results.Ok(new ExportResponse(series.Length, series));
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.LogError(ex,
                "Failed to export series: Duration={DurationMs}ms (TraceId: {TraceId})",
                stopwatch.ElapsedMilliseconds, traceId);
            
            return ResultsExtensions.InternalServerError(
                "Failed to export series data",
                context.Request.Path,
                traceId);
        }
    }
    
    /// <summary>
    /// Exports series data to individual JSON files on disk.
    /// </summary>
    /// <param name="fileService">File-based series service for saving files.</param>
    /// <param name="repo">Repository instance for data access.</param>
    /// <param name="logger">Logger instance for diagnostic logging.</param>
    /// <param name="context">Current HTTP context.</param>
    /// <returns>Export summary with success count and any errors.</returns>
    private static async Task<IResult> ExportSeriesToFiles(
        [FromServices] FileBasedSeriesService fileService,
        [FromServices] IRepository repo,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext context)
    {
        var logger = loggerFactory.CreateLogger("MehguViewer.Core.Endpoints.SystemEndpoints");
        var stopwatch = Stopwatch.StartNew();
        var traceId = context.TraceIdentifier;
        
        logger.LogInformation("Exporting series to files (TraceId: {TraceId})", traceId);
        
        var series = repo.ListSeries().ToList();
        var savedCount = 0;
        var errors = new List<string>();
        
        foreach (var s in series)
        {
            try
            {
                await fileService.SaveSeriesAsync(s);
                savedCount++;
                
                if (savedCount % 10 == 0)
                {
                    logger.LogDebug("Exported {Count}/{Total} series to files", savedCount, series.Count);
                }
            }
            catch (Exception ex)
            {
                var errorMsg = $"{s.id}: {ex.Message}";
                errors.Add(errorMsg);
                logger.LogWarning(ex, "Failed to export series {SeriesId} to file", s.id);
            }
        }
        
        stopwatch.Stop();
        
        if (errors.Count > 0)
        {
            logger.LogWarning(
                "Series file export completed with errors: Success={SuccessCount}/{Total}, Errors={ErrorCount}, Duration={DurationMs}ms (TraceId: {TraceId})",
                savedCount, series.Count, errors.Count, stopwatch.ElapsedMilliseconds, traceId);
        }
        else
        {
            logger.LogInformation(
                "Series file export completed successfully: Count={Count}, Duration={DurationMs}ms (TraceId: {TraceId})",
                savedCount, stopwatch.ElapsedMilliseconds, traceId);
        }
        
        return Results.Ok(new ExportToFilesResponse(savedCount, series.Count, errors.ToArray()));
    }

    #endregion

    #region Logs Endpoints

    /// <summary>
    /// Retrieves system logs with optional filtering.
    /// </summary>
    /// <param name="count">Maximum number of log entries to return.</param>
    /// <param name="level">Optional log level filter (Debug, Information, Warning, Error).</param>
    /// <param name="logsService">Logs service for accessing log data.</param>
    /// <param name="loggerFactory">Logger factory for creating logger.</param>
    /// <param name="context">Current HTTP context.</param>
    /// <returns>Log entries matching the specified criteria.</returns>
    /// <summary>
    /// Retrieves system logs with optional filtering.
    /// </summary>
    /// <param name="count">Maximum number of log entries to return.</param>
    /// <param name="level">Optional log level filter (Debug, Information, Warning, Error).</param>
    /// <param name="logsService">Logs service for accessing log data.</param>
    /// <param name="loggerFactory">Logger factory for creating logger.</param>
    /// <param name="context">Current HTTP context.</param>
    /// <returns>Log entries matching the specified criteria.</returns>
    private static async Task<IResult> GetLogs(
        [FromQuery] int count, 
        [FromQuery] string? level, 
        [FromServices] LogsService logsService,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext context)
    {
        var logger = loggerFactory.CreateLogger("MehguViewer.Core.Endpoints.SystemEndpoints");
        var stopwatch = Stopwatch.StartNew();
        var traceId = context.TraceIdentifier;
        
        await Task.CompletedTask;
        
        logger.LogDebug(
            "Fetching logs: Count={Count}, Level={Level} (TraceId: {TraceId})",
            count, level ?? "All", traceId);
        
        try
        {
            var effectiveCount = count > 0 ? count : DefaultLogCount;
            var logs = logsService.GetLogs(effectiveCount, level);
            var totalCount = logsService.GetLogCount();
            
            stopwatch.Stop();
            logger.LogDebug(
                "Logs retrieved: Returned={ReturnedCount}, Total={TotalCount}, Duration={DurationMs}ms (TraceId: {TraceId})",
                logs.Count(), totalCount, stopwatch.ElapsedMilliseconds, traceId);
            
            return Results.Ok(new LogsResponse(logs.ToArray(), totalCount));
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.LogError(ex,
                "Failed to retrieve logs: Duration={DurationMs}ms (TraceId: {TraceId})",
                stopwatch.ElapsedMilliseconds, traceId);
            
            return ResultsExtensions.InternalServerError(
                "Failed to retrieve system logs",
                context.Request.Path,
                traceId);
        }
    }

    /// <summary>
    /// Clears all system logs.
    /// </summary>
    /// <param name="logsService">Logs service for clearing log data.</param>
    /// <param name="loggerFactory">Logger factory for creating logger.</param>
    /// <param name="context">Current HTTP context.</param>
    /// <returns>Success confirmation.</returns>
    private static async Task<IResult> ClearLogs(
        [FromServices] LogsService logsService,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext context)
    {
        var logger = loggerFactory.CreateLogger("MehguViewer.Core.Endpoints.SystemEndpoints");
        var traceId = context.TraceIdentifier;
        
        await Task.CompletedTask;
        
        var previousCount = logsService.GetLogCount();
        logger.LogWarning("Clearing all logs: PreviousCount={Count} (TraceId: {TraceId})", previousCount, traceId);
        
        logsService.Clear();
        
        logger.LogInformation("Logs cleared successfully (TraceId: {TraceId})", traceId);
        return Results.Ok(new ClearCacheResponse("Logs cleared"));
    }

    #endregion

    #region Database Configuration Endpoints

    /// <summary>
    /// Retrieves the status of the embedded PostgreSQL database.
    /// </summary>
    /// <param name="embeddedPg">Embedded PostgreSQL service instance.</param>
    /// <param name="repo">Dynamic repository for database access.</param>
    /// <param name="loggerFactory">Logger factory for creating logger.</param>
    /// <param name="context">Current HTTP context.</param>
    /// <returns>Embedded database status including availability, port, and data status.</returns>
    /// <remarks>
    /// This endpoint is accessible during setup without authentication.
    /// After setup is complete, requires MvnAdmin authorization.
    /// </remarks>
    private static async Task<IResult> GetEmbeddedDatabaseStatus(
        [FromServices] EmbeddedPostgresService embeddedPg,
        [FromServices] DynamicRepository repo,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext context)
    {
        var logger = loggerFactory.CreateLogger("MehguViewer.Core.Endpoints.SystemEndpoints");
        var traceId = context.TraceIdentifier;
        
        await Task.CompletedTask;
        
        logger.LogDebug("Checking embedded database status (TraceId: {TraceId})", traceId);
        
        // If setup is complete and user is not authenticated as admin, reject
        if (repo.GetSystemConfig().is_setup_complete && !context.User.IsInRole("Admin"))
        {
            logger.LogWarning(
                "Unauthorized embedded database status request after setup complete (TraceId: {TraceId})",
                traceId);
            return Results.Unauthorized();
        }
        
        try
        {
            // Check if the embedded database has existing data
            bool hasData = false;
            if (embeddedPg.IsRunning && !string.IsNullOrEmpty(embeddedPg.ConnectionString))
            {
                hasData = repo.TestConnection(embeddedPg.ConnectionString);
            }
            
            var status = new EmbeddedDatabaseStatus(
                available: embeddedPg.EmbeddedModeEnabled || !embeddedPg.StartupFailed,
                running: embeddedPg.IsRunning,
                enabled: embeddedPg.EmbeddedModeEnabled,
                port: embeddedPg.Port,
                has_data: hasData,
                version: EmbeddedPostgresVersion,
                data_directory: embeddedPg.IsRunning ? "pg_data" : null,
                error_message: embeddedPg.StartupFailed ? "Embedded PostgreSQL failed to start" : null
            );
            
            logger.LogInformation(
                "Embedded database status: Running={Running}, HasData={HasData}, Port={Port} (TraceId: {TraceId})",
                status.running, status.has_data, status.port, traceId);
            
            return Results.Ok(status);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to check embedded database status (TraceId: {TraceId})",
                traceId);
            
            return ResultsExtensions.InternalServerError(
                "Failed to check embedded database status",
                context.Request.Path,
                traceId);
        }
    }

    /// <summary>
    /// Switches the system to use the embedded PostgreSQL database.
    /// </summary>
    /// <param name="request">Request specifying whether to reset existing data.</param>
    /// <param name="embeddedPg">Embedded PostgreSQL service instance.</param>
    /// <param name="repo">Dynamic repository for database initialization.</param>
    /// <param name="loggerFactory">Logger factory for creating logger.</param>
    /// <param name="context">Current HTTP context.</param>
    /// <returns>Success confirmation with connection details or error.</returns>
    private static async Task<IResult> UseEmbeddedDatabase(
        [FromBody] UseEmbeddedDatabaseRequest request, 
        [FromServices] EmbeddedPostgresService embeddedPg,
        [FromServices] DynamicRepository repo,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext context)
    {
        var logger = loggerFactory.CreateLogger("MehguViewer.Core.Endpoints.SystemEndpoints");
        var traceId = context.TraceIdentifier;
        
        await Task.CompletedTask;
        
        logger.LogInformation(
            "Switching to embedded database: ResetData={ResetData} (TraceId: {TraceId})",
            request.reset_data, traceId);
        
        // If setup is complete and user is not authenticated as admin, reject
        if (repo.GetSystemConfig().is_setup_complete && !context.User.IsInRole("Admin"))
        {
            logger.LogWarning(
                "Unauthorized embedded database switch attempt after setup complete (TraceId: {TraceId})",
                traceId);
            return Results.Unauthorized();
        }

        // Check if embedded is available and running
        if (embeddedPg.StartupFailed || !embeddedPg.IsRunning)
        {
            logger.LogError(
                "Embedded database unavailable: StartupFailed={StartupFailed}, Running={Running} (TraceId: {TraceId})",
                embeddedPg.StartupFailed, embeddedPg.IsRunning, traceId);
            
            return Results.BadRequest(new Problem(
                "EMBEDDED_DB_UNAVAILABLE", 
                "Embedded PostgreSQL is not available", 
                400, 
                embeddedPg.StartupFailed ? "Embedded PostgreSQL failed to start" : "Embedded PostgreSQL is not running",
                "/api/v1/system/database/use-embedded"));
        }

        try
        {
            // Initialize the repository with embedded connection, optionally resetting data
            await repo.InitializeAsync(request.reset_data);
            
            logger.LogInformation(
                "Successfully switched to embedded database: DataReset={DataReset} (TraceId: {TraceId})",
                request.reset_data, traceId);
            
            return Results.Ok(new UseEmbeddedDatabaseResponse(
                request.reset_data ? "Using embedded PostgreSQL (data reset)" : "Using embedded PostgreSQL", 
                embeddedPg.ConnectionString));
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to initialize embedded database (TraceId: {TraceId})",
                traceId);
            
            return Results.BadRequest(new Problem(
                "EMBEDDED_DB_INIT_FAILED", 
                "Failed to initialize embedded database", 
                400, 
                ex.Message,
                "/api/v1/system/database/use-embedded"));
        }
    }

    /// <summary>
    /// Tests connectivity to a PostgreSQL database with the provided configuration.
    /// </summary>
    /// <param name="config">Database connection configuration.</param>
    /// <param name="repo">Dynamic repository for connection testing.</param>
    /// <param name="loggerFactory">Logger factory for creating logger.</param>
    /// <param name="context">Current HTTP context.</param>
    /// <returns>Test result indicating success and whether the database has existing data.</returns>
    private static async Task<IResult> TestDatabaseConnection(
        [FromBody] DatabaseConfig config,
        [FromServices] DynamicRepository repo,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext context)
    {
        var logger = loggerFactory.CreateLogger("MehguViewer.Core.Endpoints.SystemEndpoints");
        var stopwatch = Stopwatch.StartNew();
        var traceId = context.TraceIdentifier;
        
        await Task.CompletedTask;
        
        logger.LogInformation(
            "Testing database connection: Host={Host}, Port={Port}, Database={Database}, Username={Username} (TraceId: {TraceId})",
            config.host, config.port, config.database, config.username, traceId);
        
        // If setup is complete and user is not authenticated as admin, reject
        if (repo.GetSystemConfig().is_setup_complete && !context.User.IsInRole("Admin"))
        {
            logger.LogWarning(
                "Unauthorized database test attempt after setup complete (TraceId: {TraceId})",
                traceId);
            return Results.Unauthorized();
        }
        
        var connString = $"Host={config.host};Port={config.port};Database={config.database};Username={config.username};Password={config.password}";
        
        try
        {
            bool testResult = repo.TestConnection(connString);
            
            if (!testResult)
            {
                stopwatch.Stop();
                logger.LogWarning(
                    "Database connection test failed: Host={Host}, Duration={DurationMs}ms (TraceId: {TraceId})",
                    config.host, stopwatch.ElapsedMilliseconds, traceId);
                
                return Results.BadRequest(new Problem(
                    "DB_CONNECTION_FAILED",
                    "Failed to connect to database",
                    400,
                    "Connection test returned false",
                    "/api/v1/system/database/test"));
            }
            
            stopwatch.Stop();
            logger.LogInformation(
                "Database connection test successful: Duration={DurationMs}ms (TraceId: {TraceId})",
                stopwatch.ElapsedMilliseconds, traceId);
            
            // Return success - hasData is not relevant for connection testing
            return Results.Ok(new DatabaseTestResponse(false));
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.LogError(ex,
                "Database connection test failed: Host={Host}, Duration={DurationMs}ms (TraceId: {TraceId})",
                config.host, stopwatch.ElapsedMilliseconds, traceId);
            
            return Results.BadRequest(new Problem(
                "DB_CONNECTION_FAILED",
                "Failed to connect to database",
                400,
                ex.Message,
                "/api/v1/system/database/test"));
        }
    }

    /// <summary>
    /// Configures the system to use a PostgreSQL database with the provided settings.
    /// </summary>
    /// <param name="config">Database configuration including host, port, credentials, and reset flag.</param>
    /// <param name="repo">Dynamic repository for database switching.</param>
    /// <param name="loggerFactory">Logger factory for creating logger.</param>
    /// <param name="context">Current HTTP context.</param>
    /// <returns>Success confirmation or error details.</returns>
    private static async Task<IResult> ConfigureDatabase(
        [FromBody] DatabaseSetupRequest config,
        [FromServices] DynamicRepository repo,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext context)
    {
        var logger = loggerFactory.CreateLogger("MehguViewer.Core.Endpoints.SystemEndpoints");
        var stopwatch = Stopwatch.StartNew();
        var traceId = context.TraceIdentifier;
        
        await Task.CompletedTask;
        
        logger.LogInformation(
            "Configuring database: Host={Host}, Port={Port}, Database={Database}, Reset={Reset} (TraceId: {TraceId})",
            config.host, config.port, config.database, config.reset, traceId);
        
        // If setup is complete and user is not authenticated as admin, reject
        if (repo.GetSystemConfig().is_setup_complete && !context.User.IsInRole("Admin"))
        {
            logger.LogWarning(
                "Unauthorized database configuration attempt after setup complete (TraceId: {TraceId})",
                traceId);
            return Results.Unauthorized();
        }
        
        var connString = $"Host={config.host};Port={config.port};Database={config.database};Username={config.username};Password={config.password}";
        
        try
        {
            repo.SwitchToPostgres(connString, config.reset);
            
            stopwatch.Stop();
            logger.LogInformation(
                "Database configured successfully: Duration={DurationMs}ms (TraceId: {TraceId})",
                stopwatch.ElapsedMilliseconds, traceId);
            
            return Results.Ok();
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.LogError(ex,
                "Failed to configure database: Host={Host}, Duration={DurationMs}ms (TraceId: {TraceId})",
                config.host, stopwatch.ElapsedMilliseconds, traceId);
            
            return Results.BadRequest(new Problem(
                "DB_CONFIG_FAILED",
                "Failed to configure database",
                400,
                ex.Message,
                "/api/v1/system/database/configure"));
        }
    }

    #endregion

    #region System Status and Statistics Endpoints

    /// <summary>
    /// Checks whether the initial system setup has been completed.
    /// </summary>
    /// <param name="repo">Repository instance for accessing system configuration.</param>
    /// <param name="loggerFactory">Logger factory for creating logger.</param>
    /// <param name="context">Current HTTP context.</param>
    /// <returns>Setup status response.</returns>
    private static async Task<IResult> GetSetupStatus(
        [FromServices] IRepository repo,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext context)
    {
        var logger = loggerFactory.CreateLogger("MehguViewer.Core.Endpoints.SystemEndpoints");
        var traceId = context.TraceIdentifier;
        
        await Task.CompletedTask;
        
        try
        {
            var isSetupComplete = repo.GetSystemConfig().is_setup_complete;
            
            logger.LogDebug(
                "Setup status check: IsComplete={IsComplete} (TraceId: {TraceId})",
                isSetupComplete, traceId);
            
            return Results.Ok(new SetupStatusResponse(isSetupComplete));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to check setup status (TraceId: {TraceId})", traceId);
            
            return ResultsExtensions.InternalServerError(
                "Failed to check setup status",
                context.Request.Path,
                traceId);
        }
    }

    /// <summary>
    /// Retrieves system statistics including user count and storage usage.
    /// </summary>
    /// <param name="repo">Repository instance for accessing system data.</param>
    /// <param name="loggerFactory">Logger factory for creating logger.</param>
    /// <param name="context">Current HTTP context.</param>
    /// <returns>System statistics.</returns>
    private static async Task<IResult> GetSystemStats(
        [FromServices] IRepository repo,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext context)
    {
        var logger = loggerFactory.CreateLogger("MehguViewer.Core.Endpoints.SystemEndpoints");
        var stopwatch = Stopwatch.StartNew();
        var traceId = context.TraceIdentifier;
        
        await Task.CompletedTask;
        
        logger.LogDebug("Retrieving system statistics (TraceId: {TraceId})", traceId);
        
        try
        {
            var stats = repo.GetSystemStats();
            
            // Calculate actual storage size from ./data directory
            var dataPath = Path.Combine(Directory.GetCurrentDirectory(), "data");
            var totalStorage = CalculateDirectorySize(dataPath, logger);
            
            stopwatch.Stop();
            
            if (stopwatch.ElapsedMilliseconds > SlowRequestThresholdMs)
            {
                logger.LogWarning(
                    "Slow system stats request: Duration={DurationMs}ms (TraceId: {TraceId})",
                    stopwatch.ElapsedMilliseconds, traceId);
            }
            else
            {
                logger.LogDebug(
                    "System stats retrieved: Users={Users}, Storage={StorageBytes}, Duration={DurationMs}ms (TraceId: {TraceId})",
                    stats.total_users, totalStorage, stopwatch.ElapsedMilliseconds, traceId);
            }
            
            return Results.Ok(new SystemStats(stats.total_users, totalStorage, stats.uptime_seconds));
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.LogError(ex,
                "Failed to retrieve system stats: Duration={DurationMs}ms (TraceId: {TraceId})",
                stopwatch.ElapsedMilliseconds, traceId);
            
            return ResultsExtensions.InternalServerError(
                "Failed to retrieve system statistics",
                context.Request.Path,
                traceId);
        }
    }
    
    /// <summary>
    /// Calculates the total size of all files in a directory recursively.
    /// </summary>
    /// <param name="directoryPath">Path to the directory to measure.</param>
    /// <param name="logger">Logger instance for error logging.</param>
    /// <returns>Total size in bytes, or 0 if directory doesn't exist or on error.</returns>
    private static long CalculateDirectorySize(string directoryPath, ILogger? logger = null)
    {
        if (!Directory.Exists(directoryPath))
        {
            logger?.LogDebug("Directory does not exist: {Path}", directoryPath);
            return 0;
        }
            
        try
        {
            var files = Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories);
            return files.Sum(file => new FileInfo(file).Length);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to calculate directory size: {Path}", directoryPath);
            return 0;
        }
    }



    #endregion

    #region Storage Management Endpoints

    /// <summary>
    /// Initializes storage settings from application configuration.
    /// </summary>
    /// <param name="configuration">Application configuration instance.</param>
    /// <remarks>
    /// This method should be called during application startup to load
    /// storage configuration from appsettings.json.
    /// </remarks>
    public static void InitializeStorageSettings(IConfiguration configuration)
    {
        _configuration = configuration;
        _storagePath = configuration.GetValue<string>("Storage:DataPath") ?? DefaultStoragePath;
        _thumbnailSize = configuration.GetValue<int>("Storage:ThumbnailSize", DefaultThumbnailSize);
        _webSize = configuration.GetValue<int>("Storage:WebSize", DefaultWebSize);
        _jpegQuality = configuration.GetValue<int>("Storage:JpegQuality", DefaultJpegQuality);
    }

    /// <summary>
    /// Retrieves storage statistics including asset count, cache size, and configuration.
    /// </summary>
    /// <param name="repo">Repository instance (unused but required for DI).</param>
    /// <param name="loggerFactory">Logger factory for creating logger.</param>
    /// <param name="context">Current HTTP context.</param>
    /// <returns>Storage statistics and configuration.</returns>
    private static async Task<IResult> GetStorageStats(
        [FromServices] IRepository repo,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext context)
    {
        var logger = loggerFactory.CreateLogger("MehguViewer.Core.Endpoints.SystemEndpoints");
        var stopwatch = Stopwatch.StartNew();
        var traceId = context.TraceIdentifier;
        
        await Task.CompletedTask;
        
        logger.LogDebug("Retrieving storage statistics (TraceId: {TraceId})", traceId);
        
        try
        {
            // Calculate actual asset count by scanning the data directory
            var assetCount = 0;
            var dataPath = Path.Combine(Directory.GetCurrentDirectory(), "data");
            
            if (Directory.Exists(dataPath))
            {
                // Count all image files in the data directory
                var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
                assetCount = Directory.GetFiles(dataPath, "*.*", SearchOption.AllDirectories)
                    .Count(f => imageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()));
            }
            
            stopwatch.Stop();
            
            if (stopwatch.ElapsedMilliseconds > SlowRequestThresholdMs)
            {
                logger.LogWarning(
                    "Slow storage stats request: Duration={DurationMs}ms (TraceId: {TraceId})",
                    stopwatch.ElapsedMilliseconds, traceId);
            }
            else
            {
                logger.LogDebug(
                    "Storage stats retrieved: AssetCount={AssetCount}, Duration={DurationMs}ms (TraceId: {TraceId})",
                    assetCount, stopwatch.ElapsedMilliseconds, traceId);
            }
            
            return Results.Ok(new StorageStatsResponse(
                assetCount, _cacheBytes, _storagePath, _thumbnailSize, _webSize, _jpegQuality));
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.LogError(ex,
                "Failed to retrieve storage stats: Duration={DurationMs}ms (TraceId: {TraceId})",
                stopwatch.ElapsedMilliseconds, traceId);
            
            return ResultsExtensions.InternalServerError(
                "Failed to retrieve storage statistics",
                context.Request.Path,
                traceId);
        }
    }

    /// <summary>
    /// Updates storage configuration settings (thumbnail size, web size, JPEG quality).
    /// </summary>
    /// <param name="request">Storage settings update request.</param>
    /// <param name="loggerFactory">Logger factory for creating logger.</param>
    /// <param name="context">Current HTTP context.</param>
    /// <returns>Updated storage statistics.</returns>
    /// <remarks>
    /// Settings are validated against defined min/max constraints and persisted to appsettings.json.
    /// </remarks>
    private static async Task<IResult> UpdateStorageSettings(
        StorageSettingsUpdate request,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext context)
    {
        var logger = loggerFactory.CreateLogger("MehguViewer.Core.Endpoints.SystemEndpoints");
        var traceId = context.TraceIdentifier;
        
        await Task.CompletedTask;
        
        logger.LogInformation(
            "Updating storage settings: ThumbnailSize={ThumbnailSize}, WebSize={WebSize}, JpegQuality={JpegQuality} (TraceId: {TraceId})",
            request.thumbnail_size, request.web_size, request.jpeg_quality, traceId);
        
        // Validate and apply thumbnail size
        if (request.thumbnail_size.HasValue)
        {
            if (request.thumbnail_size >= MinThumbnailSize && request.thumbnail_size <= MaxThumbnailSize)
            {
                _thumbnailSize = request.thumbnail_size.Value;
                logger.LogDebug("Updated thumbnail size: {Size}", _thumbnailSize);
            }
            else
            {
                logger.LogWarning(
                    "Invalid thumbnail size: {Size} (valid range: {Min}-{Max})",
                    request.thumbnail_size, MinThumbnailSize, MaxThumbnailSize);
            }
        }
        
        // Validate and apply web size
        if (request.web_size.HasValue)
        {
            if (request.web_size >= MinWebSize && request.web_size <= MaxWebSize)
            {
                _webSize = request.web_size.Value;
                logger.LogDebug("Updated web size: {Size}", _webSize);
            }
            else
            {
                logger.LogWarning(
                    "Invalid web size: {Size} (valid range: {Min}-{Max})",
                    request.web_size, MinWebSize, MaxWebSize);
            }
        }
        
        // Validate and apply JPEG quality
        if (request.jpeg_quality.HasValue)
        {
            if (request.jpeg_quality >= MinJpegQuality && request.jpeg_quality <= MaxJpegQuality)
            {
                _jpegQuality = request.jpeg_quality.Value;
                logger.LogDebug("Updated JPEG quality: {Quality}", _jpegQuality);
            }
            else
            {
                logger.LogWarning(
                    "Invalid JPEG quality: {Quality} (valid range: {Min}-{Max})",
                    request.jpeg_quality, MinJpegQuality, MaxJpegQuality);
            }
        }
        
        // Persist settings to appsettings.json
        try
        {
            var appSettingsPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
            if (File.Exists(appSettingsPath))
            {
                var json = await File.ReadAllTextAsync(appSettingsPath);
                var settings = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);
                
                var updatedSettings = new Dictionary<string, object>();
                foreach (var prop in settings.EnumerateObject())
                {
                    if (prop.Name == "Storage")
                    {
                        updatedSettings["Storage"] = new
                        {
                            DataPath = _storagePath,
                            ThumbnailSize = _thumbnailSize,
                            WebSize = _webSize,
                            JpegQuality = _jpegQuality
                        };
                    }
                    else
                    {
                        updatedSettings[prop.Name] = prop.Value;
                    }
                }
                
                var updatedJson = System.Text.Json.JsonSerializer.Serialize(updatedSettings, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
                });
                
                await File.WriteAllTextAsync(appSettingsPath, updatedJson);
                logger.LogInformation("Storage settings persisted to appsettings.json (TraceId: {TraceId})", traceId);
            }
        }
        catch (Exception ex)
        {
            // Log error but don't fail the request - settings are still in memory
            logger.LogWarning(ex, "Failed to persist storage settings to appsettings.json (TraceId: {TraceId})", traceId);
        }
        
        return Results.Ok(new StorageStatsResponse(0, _cacheBytes, _storagePath, _thumbnailSize, _webSize, _jpegQuality));
    }

    /// <summary>
    /// Clears the in-memory cache.
    /// </summary>
    /// <param name="loggerFactory">Logger factory for creating logger.</param>
    /// <param name="context">Current HTTP context.</param>
    /// <returns>Success confirmation.</returns>
    private static async Task<IResult> ClearCache(
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext context)
    {
        var logger = loggerFactory.CreateLogger("MehguViewer.Core.Endpoints.SystemEndpoints");
        var traceId = context.TraceIdentifier;
        
        await Task.CompletedTask;
        
        logger.LogWarning("Clearing cache: PreviousSize={Size} bytes (TraceId: {TraceId})", _cacheBytes, traceId);
        _cacheBytes = 0;
        logger.LogInformation("Cache cleared successfully (TraceId: {TraceId})", traceId);
        
        return Results.Ok(new ClearCacheResponse("Cache cleared"));
    }

    /// <summary>
    /// Refreshes the series cache from the filesystem.
    /// </summary>
    /// <param name="fileService">File-based series service for cache refresh.</param>
    /// <param name="loggerFactory">Logger factory for creating logger.</param>
    /// <param name="context">Current HTTP context.</param>
    /// <returns>Success confirmation.</returns>
    private static async Task<IResult> RefreshCache(
        [FromServices] FileBasedSeriesService fileService,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext context)
    {
        var logger = loggerFactory.CreateLogger("MehguViewer.Core.Endpoints.SystemEndpoints");
        var stopwatch = Stopwatch.StartNew();
        var traceId = context.TraceIdentifier;
        
        logger.LogInformation("Refreshing series cache from filesystem (TraceId: {TraceId})", traceId);
        
        try
        {
            await fileService.RefreshCacheAsync();
            
            stopwatch.Stop();
            logger.LogInformation(
                "Series cache refreshed successfully: Duration={DurationMs}ms (TraceId: {TraceId})",
                stopwatch.ElapsedMilliseconds, traceId);
            
            return Results.Ok(new ClearCacheResponse("Series cache refreshed from filesystem"));
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.LogError(ex,
                "Failed to refresh series cache: Duration={DurationMs}ms (TraceId: {TraceId})",
                stopwatch.ElapsedMilliseconds, traceId);
            
            return ResultsExtensions.InternalServerError(
                "Failed to refresh series cache",
                context.Request.Path,
                traceId);
        }
    }

    #endregion

    #region Report and Configuration Endpoints

    /// <summary>
    /// Creates a new report for content or user behavior.
    /// </summary>
    /// <param name="report">Report details including target and reason.</param>
    /// <param name="repo">Repository instance for storing the report.</param>
    /// <param name="loggerFactory">Logger factory for creating logger.</param>
    /// <param name="context">Current HTTP context.</param>
    /// <returns>Accepted confirmation or validation error.</returns>
    private static async Task<IResult> CreateReport(
        [FromBody] Report report,
        [FromServices] IRepository repo,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext context)
    {
        var logger = loggerFactory.CreateLogger("MehguViewer.Core.Endpoints.SystemEndpoints");
        var traceId = context.TraceIdentifier;
        
        await Task.CompletedTask;
        
        logger.LogInformation(
            "Creating report: Target={Target}, Reason={Reason} (TraceId: {TraceId})",
            report.target_urn, report.reason, traceId);
        
        // Validate required fields
        if (string.IsNullOrWhiteSpace(report.target_urn))
        {
            logger.LogWarning("Report rejected: Missing target URN (TraceId: {TraceId})", traceId);
            return Results.BadRequest(new Problem(
                "VALIDATION_ERROR",
                "Target URN is required",
                400,
                null,
                "/api/v1/reports"));
        }
        
        if (string.IsNullOrWhiteSpace(report.reason))
        {
            logger.LogWarning("Report rejected: Missing reason (TraceId: {TraceId})", traceId);
            return Results.BadRequest(new Problem(
                "VALIDATION_ERROR",
                "Reason is required",
                400,
                null,
                "/api/v1/reports"));
        }

        try
        {
            repo.AddReport(report);
            logger.LogInformation("Report created successfully: Target={Target} (TraceId: {TraceId})", report.target_urn, traceId);
            return Results.Accepted();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create report (TraceId: {TraceId})", traceId);
            
            return ResultsExtensions.InternalServerError(
                "Failed to create report",
                context.Request.Path,
                traceId);
        }
    }

    /// <summary>
    /// Retrieves the current system configuration.
    /// </summary>
    /// <param name="repo">Repository instance for accessing configuration.</param>
    /// <param name="loggerFactory">Logger factory for creating logger.</param>
    /// <param name="context">Current HTTP context.</param>
    /// <returns>System configuration object.</returns>
    private static async Task<IResult> GetSystemConfig(
        [FromServices] IRepository repo,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext context)
    {
        var logger = loggerFactory.CreateLogger("MehguViewer.Core.Endpoints.SystemEndpoints");
        var traceId = context.TraceIdentifier;
        
        await Task.CompletedTask;
        
        logger.LogDebug("Retrieving system configuration (TraceId: {TraceId})", traceId);
        
        try
        {
            var config = repo.GetSystemConfig();
            return Results.Ok(config);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to retrieve system configuration (TraceId: {TraceId})", traceId);
            
            return ResultsExtensions.InternalServerError(
                "Failed to retrieve system configuration",
                context.Request.Path,
                traceId);
        }
    }

    /// <summary>
    /// Partially updates system configuration (PATCH semantics).
    /// </summary>
    /// <param name="update">Partial configuration update with nullable fields.</param>
    /// <param name="repo">Repository instance for configuration management.</param>
    /// <param name="loggerFactory">Logger factory for creating logger.</param>
    /// <param name="context">Current HTTP context.</param>
    /// <returns>Updated configuration object.</returns>
    /// <remarks>
    /// Only fields explicitly provided (non-null) in the update request will be modified.
    /// Existing values are preserved for null fields.
    /// </remarks>
    private static async Task<IResult> PatchSystemConfig(
        SystemConfigUpdate update,
        [FromServices] IRepository repo,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext context)
    {
        var logger = loggerFactory.CreateLogger("MehguViewer.Core.Endpoints.SystemEndpoints");
        var traceId = context.TraceIdentifier;
        
        await Task.CompletedTask;
        
        logger.LogInformation("Patching system configuration (TraceId: {TraceId})", traceId);
        
        try
        {
            // Get current config from database
            var current = repo.GetSystemConfig();
            
            // Merge: only update fields that are explicitly provided (not null)
            var merged = new SystemConfig(
                is_setup_complete: update.is_setup_complete ?? current.is_setup_complete,
                registration_open: update.registration_open ?? current.registration_open,
                maintenance_mode: update.maintenance_mode ?? current.maintenance_mode,
                motd_message: update.motd_message ?? current.motd_message,
                default_language_filter: update.default_language_filter ?? current.default_language_filter,
                max_login_attempts: update.max_login_attempts ?? current.max_login_attempts,
                lockout_duration_minutes: update.lockout_duration_minutes ?? current.lockout_duration_minutes,
                token_expiry_hours: update.token_expiry_hours ?? current.token_expiry_hours,
                cloudflare_enabled: update.cloudflare_enabled ?? current.cloudflare_enabled,
                cloudflare_site_key: update.cloudflare_site_key ?? current.cloudflare_site_key,
                cloudflare_secret_key: update.cloudflare_secret_key ?? current.cloudflare_secret_key,
                require_2fa_passkey: update.require_2fa_passkey ?? current.require_2fa_passkey,
                require_password_for_danger_zone: update.require_password_for_danger_zone ?? current.require_password_for_danger_zone
            );
            
            repo.UpdateSystemConfig(merged);
            
            logger.LogInformation("System configuration patched successfully (TraceId: {TraceId})", traceId);
            return Results.Ok(merged);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to patch system configuration (TraceId: {TraceId})", traceId);
            
            return ResultsExtensions.InternalServerError(
                "Failed to update system configuration",
                context.Request.Path,
                traceId);
        }
    }

    /// <summary>
    /// Replaces the entire system configuration (PUT semantics).
    /// </summary>
    /// <param name="config">Complete system configuration object.</param>
    /// <param name="repo">Repository instance for configuration management.</param>
    /// <param name="loggerFactory">Logger factory for creating logger.</param>
    /// <param name="context">Current HTTP context.</param>
    /// <returns>Updated configuration object.</returns>
    private static async Task<IResult> UpdateSystemConfig(
        SystemConfig config,
        [FromServices] IRepository repo,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext context)
    {
        var logger = loggerFactory.CreateLogger("MehguViewer.Core.Endpoints.SystemEndpoints");
        var traceId = context.TraceIdentifier;
        
        await Task.CompletedTask;
        
        logger.LogInformation("Updating system configuration (TraceId: {TraceId})", traceId);
        
        try
        {
            repo.UpdateSystemConfig(config);
            
            logger.LogInformation("System configuration updated successfully (TraceId: {TraceId})", traceId);
            return Results.Ok(config);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update system configuration (TraceId: {TraceId})", traceId);
            
            return ResultsExtensions.InternalServerError(
                "Failed to update system configuration",
                context.Request.Path,
                traceId);
        }
    }

    #endregion

    #region User Management Endpoints

    /// <summary>
    /// Creates the first admin user during initial system setup.
    /// </summary>
    /// <param name="request">Admin password request.</param>
    /// <param name="repo">Repository instance for user management.</param>
    /// <param name="authService">Authentication service for password handling.</param>
    /// <param name="loggerFactory">Logger factory for creating logger.</param>
    /// <param name="context">Current HTTP context.</param>
    /// <returns>Success confirmation or validation/conflict error.</returns>
    /// <remarks>
    /// This endpoint is only accessible before the first admin user is created.
    /// After that, it returns a 409 Conflict error.
    /// </remarks>
    private static async Task<IResult> CreateAdminUser(
        AdminPasswordRequest request, 
        [FromServices] IRepository repo,
        [FromServices] AuthService authService,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext context)
    {
        var logger = loggerFactory.CreateLogger("MehguViewer.Core.Endpoints.SystemEndpoints");
        var traceId = context.TraceIdentifier;
        
        await Task.CompletedTask;
        
        logger.LogInformation("Creating admin user (TraceId: {TraceId})", traceId);
        
        if (string.IsNullOrWhiteSpace(request.password))
        {
            logger.LogWarning("Admin user creation rejected: Missing password (TraceId: {TraceId})", traceId);
            return Results.BadRequest(new Problem(
                "VALIDATION_ERROR",
                "Password is required",
                400,
                null,
                "/api/v1/system/admin/password"));
        }

        // Validate password strength
        var (isValid, error) = authService.ValidatePasswordStrength(request.password);
        if (!isValid)
        {
            logger.LogWarning(
                "Admin user creation rejected: Weak password (TraceId: {TraceId})",
                traceId);
            return Results.BadRequest(new Problem(
                "WEAK_PASSWORD",
                error!,
                400,
                null,
                "/api/v1/system/admin/password"));
        }

        if (repo.IsAdminSet())
        {
            logger.LogWarning("Admin user creation rejected: Admin already exists (TraceId: {TraceId})", traceId);
            return Results.Conflict(new Problem(
                "ADMIN_EXISTS",
                "Admin user already exists",
                409,
                null,
                "/api/v1/system/admin/password"));
        }
        
        try
        {
            var user = new User(
                UrnHelper.CreateUserUrn(),
                "admin",
                authService.HashPassword(request.password),
                "Admin",
                DateTime.UtcNow);
            repo.AddUser(user);
            
            logger.LogInformation("Admin user created successfully (TraceId: {TraceId})", traceId);
            return Results.Ok();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create admin user (TraceId: {TraceId})", traceId);
            
            return ResultsExtensions.InternalServerError(
                "Failed to create admin user",
                context.Request.Path,
                traceId);
        }
    }

    private static async Task<IResult> CreateUser(
        UserCreate request, 
        [FromServices] IRepository repo,
        [FromServices] AuthService authService)
    {
        await Task.CompletedTask;
        if (string.IsNullOrWhiteSpace(request.username) || string.IsNullOrWhiteSpace(request.password))
            return Results.BadRequest(new Problem("VALIDATION_ERROR", "Username and password are required", 400, null, "/api/v1/users"));

        // Validate password strength
        var (isValid, error) = authService.ValidatePasswordStrength(request.password);
        if (!isValid)
            return Results.BadRequest(new Problem("WEAK_PASSWORD", error!, 400, null, "/api/v1/users"));

        if (repo.GetUserByUsername(request.username) != null) 
            return Results.Conflict(new Problem("USER_EXISTS", "A user with this username already exists", 409, null, "/api/v1/users"));
        
        var user = new User(UrnHelper.CreateUserUrn(), request.username, authService.HashPassword(request.password), request.role, DateTime.UtcNow);
        repo.AddUser(user);
        return Results.Ok(user);
    }

    private static async Task<IResult> ListUsers([FromServices] IRepository repo)
    {
        await Task.CompletedTask;
        return Results.Ok(repo.ListUsers());
    }

    private static async Task<IResult> UpdateUser(
        string id, 
        UserUpdate request, 
        [FromServices] IRepository repo, 
        HttpContext context,
        [FromServices] AuthService authService)
    {
        await Task.CompletedTask;
        
        var user = repo.GetUser(id);
        if (user == null)
            return Results.NotFound(new Problem("USER_NOT_FOUND", "User not found", 404, null, $"/api/v1/users/{id}"));

        // Check if trying to modify the first admin
        var currentUserId = context.User.FindFirst("sub")?.Value 
                           ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        
        if (user.role == "Admin" && IsFirstAdmin(user, repo) && currentUserId != user.id)
        {
            return Results.Json(new Problem(
                "urn:mvn:error:first-admin-protected",
                "The first admin account cannot be modified by other administrators.",
                403,
                "First Admin Protected",
                $"/api/v1/users/{id}"
            ), AppJsonSerializerContext.Default.Problem, statusCode: 403);
        }

        // Update role if provided
        var newRole = request.role ?? user.role;
        var newPasswordHash = user.password_hash;
        var newPasswordLoginDisabled = request.password_login_disabled ?? user.password_login_disabled;
        
        // Update password if provided
        if (!string.IsNullOrEmpty(request.password))
        {
            var (isValid, error) = authService.ValidatePasswordStrength(request.password);
            if (!isValid)
                return Results.BadRequest(new Problem("WEAK_PASSWORD", error!, 400, null, $"/api/v1/users/{id}"));
            
            newPasswordHash = authService.HashPassword(request.password);
        }
        
        var updatedUser = user with { 
            role = newRole, 
            password_hash = newPasswordHash,
            password_login_disabled = newPasswordLoginDisabled
        };
        repo.UpdateUser(updatedUser);
        
        return Results.Ok(updatedUser);
    }

    private static async Task<IResult> DeleteUser(string id, [FromServices] IRepository repo, HttpContext context)
    {
        await Task.CompletedTask;
        
        var user = repo.GetUser(id);
        if (user == null)
            return Results.NotFound(new Problem("USER_NOT_FOUND", "User not found", 404, null, $"/api/v1/users/{id}"));
        
        // Check if trying to delete the first admin
        var currentUserId = context.User.FindFirst("sub")?.Value 
                           ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        
        if (user.role == "Admin" && IsFirstAdmin(user, repo) && currentUserId != user.id)
        {
            return Results.Json(new Problem(
                "urn:mvn:error:first-admin-protected",
                "The first admin account cannot be deleted by other administrators.",
                403,
                "First Admin Protected",
                $"/api/v1/users/{id}"
            ), AppJsonSerializerContext.Default.Problem, statusCode: 403);
        }
        
        repo.DeleteUser(id);
        return Results.Ok();
    }

    /// <summary>
    /// Check if a user is the first admin (earliest created_at among admins).
    /// </summary>
    private static bool IsFirstAdmin(User user, IRepository repo)
    {
        if (user.role != "Admin")
            return false;
        
        var allAdmins = repo.ListUsers().Where(u => u.role == "Admin").ToList();
        if (!allAdmins.Any())
            return false;
        
        var firstAdmin = allAdmins.OrderBy(a => a.created_at).First();
        return firstAdmin.id == user.id;
    }

    private static async Task<IResult> UpdateNodeMetadata(NodeMetadata metadata, [FromServices] IRepository repo)
    {
        await Task.CompletedTask;
        repo.UpdateNodeMetadata(metadata);
        return Results.Ok(metadata);
    }

    private static async Task<IResult> GetNodeMetadata([FromServices] IRepository repo)
    {
        await Task.CompletedTask;
        return Results.Ok(repo.GetNodeMetadata());
    }

    private static async Task<IResult> GetInstance()
    {
        await Task.CompletedTask;
        return Results.Ok(new NodeManifest(
            "urn:mvn:node:example",
            "MehguViewer Core",
            "A MehguViewer Core Node",
            "1.0.0",
            "MehguViewer.Core (NativeAOT)",
            "admin@example.com",
            false,
            new NodeFeatures(true, false),
            null
        ));
    }

    private static async Task<IResult> GetTaxonomy([FromServices] IRepository repo)
    {
        await Task.CompletedTask;
        
        // Auto-generate taxonomy from public series data
        var allSeries = repo.ListSeries();
        
        // Aggregate tags from all series
        var aggregatedTags = allSeries
            .SelectMany(s => s.tags)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t)
            .ToArray();
        
        // Aggregate authors from all series - deduplicate by name (case-insensitive), keep first ID
        var aggregatedAuthors = allSeries
            .SelectMany(s => s.authors)
            .GroupBy(a => a.name.ToLowerInvariant())
            .Select(g => g.First())
            .OrderBy(a => a.name)
            .ToArray();
        
        // Aggregate scanlators from all series - deduplicate by name (case-insensitive), keep first ID
        // Also include scanlators from localized metadata
        var seriesScanlators = allSeries.SelectMany(s => s.scanlators);
        var localizedScanlators = allSeries
            .Where(s => s.localized != null)
            .SelectMany(s => s.localized!.Values)
            .Where(lm => lm.scanlators != null)
            .SelectMany(lm => lm.scanlators!);
        
        var aggregatedScanlators = seriesScanlators
            .Concat(localizedScanlators)
            .GroupBy(s => s.name.ToLowerInvariant())
            .Select(g => g.First())
            .OrderBy(s => s.name)
            .ToArray();
        
        // Aggregate groups from all series - deduplicate by name (case-insensitive), keep first ID
        var aggregatedGroups = allSeries
            .Where(s => s.groups != null)
            .SelectMany(s => s.groups!)
            .GroupBy(g => g.name.ToLowerInvariant())
            .Select(g => g.First())
            .OrderBy(g => g.name)
            .ToArray();
        
        // Get stored config for fallback
        var config = repo.GetTaxonomyConfig();
        
        return Results.Ok(new TaxonomyData(
            tags: aggregatedTags.Length > 0 ? aggregatedTags : config.tags,
            content_warnings: ContentWarnings.All, // Always return all possible content warnings
            types: MediaTypes.All, // Fixed media types
            authors: aggregatedAuthors.Length > 0 ? aggregatedAuthors : config.authors,
            scanlators: aggregatedScanlators.Length > 0 ? aggregatedScanlators : config.scanlators,
            groups: aggregatedGroups.Length > 0 ? aggregatedGroups : config.groups
        ));
    }

    private static async Task<IResult> GetTaxonomyConfig([FromServices] IRepository repo)
    {
        await Task.CompletedTask;
        return Results.Ok(repo.GetTaxonomyConfig());
    }

    private static async Task<IResult> UpdateTaxonomyConfig([FromBody] TaxonomyConfig config, [FromServices] IRepository repo)
    {
        await Task.CompletedTask;
        repo.UpdateTaxonomyConfig(config);
        return Results.Ok(config);
    }

    private static async Task<IResult> PatchTaxonomyConfig([FromBody] TaxonomyConfigUpdate update, [FromServices] IRepository repo)
    {
        await Task.CompletedTask;
        
        // Get current config from database
        var current = repo.GetTaxonomyConfig();
        
        // Merge: only update fields that are explicitly provided (not null)
        var merged = new TaxonomyConfig(
            tags: update.tags ?? current.tags,
            content_warnings: update.content_warnings ?? current.content_warnings,
            types: update.types ?? current.types,
            authors: update.authors ?? current.authors,
            scanlators: update.scanlators ?? current.scanlators,
            groups: update.groups ?? current.groups
        );
        
        repo.UpdateTaxonomyConfig(merged);
        return Results.Ok(merged);
    }

    private static async Task<IResult> ResetAllData([FromBody] ResetRequest request, [FromServices] IRepository repo, HttpContext context, [FromServices] PasskeyService passkeyService, [FromServices] AuthService authService)
    {
        await Task.CompletedTask;
        
        // Get current user from token
        var username = context.User.Identity?.Name;
        var userId = context.User.FindFirst("sub")?.Value 
                     ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(userId))
            return Results.Unauthorized();

        // Only first admin can access danger zone
        var currentUser = repo.GetUser(userId);
        if (currentUser == null || currentUser.role != "Admin" || !IsFirstAdmin(currentUser, repo))
        {
            return Results.Json(new Problem(
                "urn:mvn:error:forbidden",
                "Access Denied",
                403,
                "Only the first administrator can perform this action.",
                "/api/v1/admin/reset-data"
            ), AppJsonSerializerContext.Default.Problem, statusCode: 403);
        }

        // Validate authentication - either password or passkey
        bool authenticated = false;
        
        if (request.passkey != null)
        {
            // Validate passkey
            authenticated = await ValidatePasskeyVerification(request.passkey, userId, repo, passkeyService, context);
        }
        else if (!string.IsNullOrEmpty(request.password_hash))
        {
            // Validate password (legacy flow)
            var user = repo.GetUserByUsername(username);
            authenticated = user != null && user.role == "Admin" && authService.VerifyPassword(request.password_hash, user.password_hash);
        }
        
        if (!authenticated)
            return Results.Unauthorized();

        try
        {
            repo.ResetAllData();
            return Results.Ok(new { message = "All data has been reset successfully" });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new Problem("RESET_FAILED", "Failed to reset data", 400, ex.Message, "/api/v1/admin/reset-data"));
        }
    }

    private static async Task<IResult> ResetDatabase([FromBody] ResetRequest request, [FromServices] DynamicRepository repo, HttpContext context, [FromServices] PasskeyService passkeyService, [FromServices] AuthService authService)
    {
        await Task.CompletedTask;
        
        // Get current user from token
        var username = context.User.Identity?.Name;
        var userId = context.User.FindFirst("sub")?.Value 
                     ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(userId))
            return Results.Unauthorized();

        // Only first admin can access danger zone
        var currentUser = repo.GetUser(userId);
        if (currentUser == null || currentUser.role != "Admin" || !IsFirstAdmin(currentUser, repo))
        {
            return Results.Json(new Problem(
                "urn:mvn:error:forbidden",
                "Access Denied",
                403,
                "Only the first administrator can perform this action.",
                "/api/v1/admin/reset-database"
            ), AppJsonSerializerContext.Default.Problem, statusCode: 403);
        }

        // Validate authentication - either password or passkey
        bool authenticated = false;
        
        if (request.passkey != null)
        {
            // Validate passkey
            authenticated = await ValidatePasskeyVerification(request.passkey, userId, repo, passkeyService, context);
        }
        else if (!string.IsNullOrEmpty(request.password_hash))
        {
            // Validate password (legacy flow)
            var user = repo.GetUserByUsername(username);
            authenticated = user != null && user.role == "Admin" && authService.VerifyPassword(request.password_hash, user.password_hash);
        }
        
        if (!authenticated)
            return Results.Unauthorized();

        try
        {
            repo.ResetDatabase();
            return Results.Ok(new { message = "Database has been reset successfully" });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new Problem("RESET_FAILED", "Failed to reset database", 400, ex.Message, "/api/v1/admin/reset-database"));
        }
    }

    private static async Task<bool> ValidatePasskeyVerification(
        PasskeyVerificationData passkeyData, 
        string userId, 
        IRepository repo,
        PasskeyService passkeyService,
        HttpContext context)
    {
        await Task.CompletedTask;
        
        try
        {
            // Validate and consume the stored challenge
            var (valid, storedChallenge, storedUserId) = passkeyService.ValidateChallenge(passkeyData.challenge_id);
            if (!valid || storedChallenge == null)
                return false;

            // Get user's passkeys
            var userPasskeys = repo.GetPasskeysByUser(userId);
            var matchingPasskey = userPasskeys.FirstOrDefault(p => 
            {
                var credId = Convert.ToBase64String(Base64UrlDecode(passkeyData.id));
                return p.credential_id == credId || p.credential_id == passkeyData.id;
            });

            if (matchingPasskey == null)
                return false;

            // Construct the authentication request
            var authRequest = new PasskeyAuthenticationRequest(
                id: passkeyData.id,
                raw_id: passkeyData.raw_id,
                response: new PasskeyAuthenticatorAssertionResponse(
                    passkeyData.response.client_data_json,
                    passkeyData.response.authenticator_data,
                    passkeyData.response.signature,
                    passkeyData.response.user_handle
                ),
                type: passkeyData.type
            );

            // Get the expected origin from the request
            var scheme = context.Request.Scheme;
            var host = context.Request.Host.ToString();
            var expectedOrigin = $"{scheme}://{host}";
            
            // Verify the assertion using the PasskeyService
            var (success, verifiedUserId, newSignCount, error) = passkeyService.VerifyAuthentication(
                authRequest,
                matchingPasskey,
                storedChallenge,
                expectedOrigin
            );

            if (success && newSignCount > matchingPasskey.sign_count)
            {
                // Update sign count
                var updatedPasskey = matchingPasskey with { sign_count = newSignCount };
                repo.UpdatePasskey(updatedPasskey);
            }

            return success;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Passkey verification error: {ex.Message}");
            return false;
        }
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var output = input.Replace('-', '+').Replace('_', '/');
        switch (output.Length % 4)
        {
            case 2: output += "=="; break;
            case 3: output += "="; break;
        }
        return Convert.FromBase64String(output);
    }

    private static async Task<IResult> RunTaxonomyValidation(
        [FromServices] TaxonomyValidationService validationService,
        [FromServices] JobService jobService,
        HttpContext context)
    {
        // Create a job for this validation run
        var job = jobService.CreateJob("taxonomy-validation");
        
        try
        {
            jobService.UpdateJob(job.id, "PROCESSING", 0);

            // Run the full validation
            var report = await validationService.RunFullValidationAsync();

            var totalIssues = report.SeriesIssues.Length + report.UnitIssues.Length;
            
            if (totalIssues == 0)
            {
                jobService.UpdateJob(
                    job.id, 
                    "COMPLETED", 
                    100, 
                    null, 
                    $"Validation completed. No issues found. Checked {report.TotalSeries} series and {report.TotalUnits} units.");
                
                return Results.Ok(new
                {
                    job_id = job.id,
                    status = "completed",
                    report = new
                    {
                        validated_at = report.ValidatedAt,
                        total_series = report.TotalSeries,
                        total_units = report.TotalUnits,
                        series_issues = report.SeriesIssues,
                        unit_issues = report.UnitIssues,
                        summary = report.Summary
                    }
                });
            }
            else
            {
                jobService.UpdateJob(
                    job.id, 
                    "COMPLETED", 
                    100, 
                    null, 
                    $"Validation found {totalIssues} issues. Series issues: {report.SeriesIssues.Length}, Unit issues: {report.UnitIssues.Length}");
                
                return Results.Ok(new
                {
                    job_id = job.id,
                    status = "completed_with_issues",
                    report = new
                    {
                        validated_at = report.ValidatedAt,
                        total_series = report.TotalSeries,
                        total_units = report.TotalUnits,
                        series_issues = report.SeriesIssues,
                        unit_issues = report.UnitIssues,
                        summary = report.Summary
                    }
                });
            }
        }
        catch (Exception ex)
        {
            jobService.UpdateJob(
                job.id, 
                "FAILED", 
                0, 
                null, 
                $"Validation failed: {ex.Message}");
            
            return ResultsExtensions.InternalServerError($"Validation failed: {ex.Message}", context.Request.Path);
        }
    }

    #endregion
}
