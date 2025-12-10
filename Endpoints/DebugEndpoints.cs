using MehguViewer.Core.Services;
using MehguViewer.Core.Helpers;
using MehguViewer.Core.Shared;
using MehguViewer.Core.Infrastructures;
using Microsoft.AspNetCore.Mvc;

#pragma warning disable CS1998 // Async method lacks 'await' operators

namespace MehguViewer.Core.Endpoints;

/// <summary>
/// Debug and diagnostic endpoints for development and troubleshooting.
/// </summary>
/// <remarks>
/// <para><strong>Security Notice:</strong></para>
/// These endpoints should be protected in production environments.
/// Consider implementing authentication/authorization or environment-based filtering.
/// 
/// <para><strong>Available Endpoints:</strong></para>
/// <list type="bullet">
///   <item><strong>POST /api/v1/debug/seed:</strong> Populates repository with test data</item>
///   <item><strong>GET /api/v1/debug/cache-info:</strong> Returns cache statistics and filesystem state</item>
/// </list>
/// 
/// <para><strong>Best Practices:</strong></para>
/// <list type="bullet">
///   <item>Disable in production via environment checks</item>
///   <item>Implement rate limiting to prevent abuse</item>
///   <item>Log all debug endpoint access with request details</item>
///   <item>Return sanitized data without sensitive information</item>
/// </list>
/// </remarks>
public static class DebugEndpoints
{
    #region Constants
    
    /// <summary>Logger category name for debug endpoints.</summary>
    private const string LoggerCategory = "MehguViewer.Core.Endpoints.DebugEndpoints";
    
    #endregion

    #region Endpoint Registration
    
    /// <summary>
    /// Registers debug endpoints to the application route builder.
    /// </summary>
    /// <param name="app">The endpoint route builder instance.</param>
    /// <remarks>
    /// Endpoints are mapped without authorization requirements.
    /// Consider adding .RequireAuthorization() in production scenarios.
    /// </remarks>
    public static void MapDebugEndpoints(this IEndpointRouteBuilder app)
    {
        if (app == null)
        {
            throw new ArgumentNullException(nameof(app));
        }

        app.MapPost("/api/v1/debug/seed", SeedDebugData)
            .WithName("SeedDebugData")
            .WithTags("Debug")
            .Produces<DebugResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError);
            
        app.MapGet("/api/v1/debug/cache-info", GetCacheInfo)
            .WithName("GetCacheInfo")
            .WithTags("Debug")
            .Produces<object>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError);
    }
    
    #endregion

    #region Endpoint Handlers

    /// <summary>
    /// Seeds the repository with debug/test data.
    /// </summary>
    /// <param name="repo">Repository instance for data persistence.</param>
    /// <param name="loggerFactory">Logger factory for creating endpoint logger.</param>
    /// <param name="environment">Web host environment to check current environment.</param>
    /// <param name="httpContext">HTTP context for trace ID and instance path.</param>
    /// <returns>Success response with confirmation message or error details.</returns>
    /// <remarks>
    /// <para><strong>Operation:</strong></para>
    /// Calls SeedDebugData() on the repository to populate test entities.
    /// Implementation depends on repository type (Postgres/Memory).
    /// 
    /// <para><strong>Security:</strong></para>
    /// WARNING: This operation can overwrite existing data in development mode.
    /// Should be disabled or protected in production environments.
    /// 
    /// <para><strong>Logging:</strong></para>
    /// <list type="bullet">
    ///   <item>Info: Successful seed operation with trace ID</item>
    ///   <item>Warning: Attempt to seed in non-development environment</item>
    ///   <item>Error: Seeding failures with exception details</item>
    /// </list>
    /// </remarks>
    private static async Task<IResult> SeedDebugData(
        [FromServices] IRepository repo,
        [FromServices] ILoggerFactory loggerFactory,
        [FromServices] IWebHostEnvironment environment,
        HttpContext httpContext)
    {
        if (repo == null) throw new ArgumentNullException(nameof(repo));
        if (loggerFactory == null) throw new ArgumentNullException(nameof(loggerFactory));
        if (environment == null) throw new ArgumentNullException(nameof(environment));
        if (httpContext == null) throw new ArgumentNullException(nameof(httpContext));

        var logger = loggerFactory.CreateLogger(LoggerCategory);
        var traceId = httpContext.TraceIdentifier;
        var instancePath = httpContext.Request.Path.Value ?? "/api/v1/debug/seed";

        // Security: Warn if attempting to seed in non-development environments
        if (!environment.IsDevelopment())
        {
            logger.LogWarning(
                "Debug seed endpoint accessed in {Environment} environment. TraceId: {TraceId}",
                environment.EnvironmentName,
                traceId);
        }

        try
        {
            logger.LogInformation(
                "Starting debug data seed operation. TraceId: {TraceId}",
                traceId);

            // Execute synchronous seed operation
            await Task.Run(() => repo.SeedDebugData());

            logger.LogInformation(
                "Debug data seeded successfully. TraceId: {TraceId}",
                traceId);

            return Results.Ok(new DebugResponse("Debug data seeded successfully."));
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to seed debug data. TraceId: {TraceId}",
                traceId);

            return ResultsExtensions.InternalServerError(
                "An error occurred while seeding debug data.",
                instancePath,
                traceId);
        }
    }
    
    /// <summary>
    /// Retrieves cache statistics and filesystem information.
    /// </summary>
    /// <param name="fileService">File-based series service for cache access.</param>
    /// <param name="loggerFactory">Logger factory for creating endpoint logger.</param>
    /// <param name="httpContext">HTTP context for trace ID and instance path.</param>
    /// <returns>Cache information including series count, IDs, and filesystem state.</returns>
    /// <remarks>
    /// <para><strong>Response Structure:</strong></para>
    /// <code>
    /// {
    ///   "seriesCount": 10,
    ///   "seriesIds": [{ "id": "urn:mvn:series:uuid", "title": "Series Name" }],
    ///   "dataPath": "/path/to/data/series",
    ///   "filesystemDirs": ["series-dir-1", "series-dir-2"],
    ///   "cacheLoadedAt": "2024-12-10T10:30:00Z"
    /// }
    /// </code>
    /// 
    /// <para><strong>Use Cases:</strong></para>
    /// <list type="bullet">
    ///   <item>Verify cache is loaded and synchronized with filesystem</item>
    ///   <item>Identify orphaned directories (in filesystem but not in cache)</item>
    ///   <item>Troubleshoot series discovery and loading issues</item>
    ///   <item>Monitor cache size and performance</item>
    /// </list>
    /// 
    /// <para><strong>Performance:</strong></para>
    /// ListSeries() returns cached data - operation is fast (O(1) cache access).
    /// Filesystem directory enumeration may be slower for large datasets.
    /// 
    /// <para><strong>Logging:</strong></para>
    /// <list type="bullet">
    ///   <item>Debug: Cache access details and series count</item>
    ///   <item>Info: Successful cache info retrieval</item>
    ///   <item>Warning: Filesystem access issues or missing directories</item>
    ///   <item>Error: Cache access failures with exception details</item>
    /// </list>
    /// </remarks>
    private static async Task<IResult> GetCacheInfo(
        [FromServices] FileBasedSeriesService fileService,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext httpContext)
    {
        if (fileService == null) throw new ArgumentNullException(nameof(fileService));
        if (loggerFactory == null) throw new ArgumentNullException(nameof(loggerFactory));
        if (httpContext == null) throw new ArgumentNullException(nameof(httpContext));

        var logger = loggerFactory.CreateLogger(LoggerCategory);
        var traceId = httpContext.TraceIdentifier;
        var instancePath = httpContext.Request.Path.Value ?? "/api/v1/debug/cache-info";

        try
        {
            logger.LogDebug(
                "Retrieving cache information. TraceId: {TraceId}",
                traceId);

            // Retrieve cached series (fast operation - O(1) cache access)
            var allSeries = fileService.ListSeries().ToList();
            var basePath = fileService.GetSeriesBasePath();

            logger.LogDebug(
                "Cache contains {SeriesCount} series. BasePath: {BasePath}. TraceId: {TraceId}",
                allSeries.Count,
                basePath,
                traceId);

            // Enumerate filesystem directories
            string[] filesystemDirs;
            try
            {
                filesystemDirs = Directory.Exists(basePath)
                    ? Directory.GetDirectories(basePath)
                        .Select(Path.GetFileName)
                        .Where(name => !string.IsNullOrEmpty(name))
                        .Cast<string>()
                        .ToArray()
                    : Array.Empty<string>();

                logger.LogDebug(
                    "Found {DirectoryCount} directories in filesystem. TraceId: {TraceId}",
                    filesystemDirs.Length,
                    traceId);
            }
            catch (UnauthorizedAccessException uaEx)
            {
                logger.LogWarning(
                    uaEx,
                    "Access denied when reading filesystem directories at {BasePath}. TraceId: {TraceId}",
                    basePath,
                    traceId);
                filesystemDirs = Array.Empty<string>();
            }
            catch (DirectoryNotFoundException dnfEx)
            {
                logger.LogWarning(
                    dnfEx,
                    "Directory not found at {BasePath}. TraceId: {TraceId}",
                    basePath,
                    traceId);
                filesystemDirs = Array.Empty<string>();
            }

            // Build response payload
            var cacheInfo = new
            {
                seriesCount = allSeries.Count,
                seriesIds = allSeries.Select(s => new
                {
                    id = s.id ?? "unknown",
                    title = s.title ?? "Untitled"
                }).ToArray(),
                dataPath = basePath ?? string.Empty,
                filesystemDirs,
                cacheLoadedAt = DateTime.UtcNow // Placeholder - enhance if FileBasedSeriesService tracks load time
            };

            logger.LogInformation(
                "Cache info retrieved successfully. Series: {SeriesCount}, Directories: {DirCount}. TraceId: {TraceId}",
                cacheInfo.seriesCount,
                filesystemDirs.Length,
                traceId);

            return Results.Ok(cacheInfo);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to retrieve cache information. TraceId: {TraceId}",
                traceId);

            return ResultsExtensions.InternalServerError(
                "An error occurred while retrieving cache information.",
                instancePath,
                traceId);
        }
    }
    
    #endregion
}
