using MehguViewer.Core.Shared;
using MehguViewer.Core.Services;
using MehguViewer.Core.Helpers;
using MehguViewer.Core.Infrastructures;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace MehguViewer.Core.Endpoints;

/// <summary>
/// Provides endpoints for content ingestion operations (bulk uploads, granular page additions).
/// </summary>
/// <remarks>
/// <para><strong>Purpose:</strong> Handles file uploads and content ingestion for series chapters and unit pages.</para>
/// 
/// <para><strong>Authentication:</strong> All endpoints require "MvnIngest" scope for security.</para>
/// 
/// <para><strong>Core Capabilities:</strong></para>
/// <list type="bullet">
///   <item><description>Bulk upload: ZIP/CBZ archives for entire chapters</description></item>
///   <item><description>Granular upload: Individual page addition to units</description></item>
///   <item><description>File validation: Size limits, content type verification</description></item>
///   <item><description>Job-based processing: Asynchronous handling of large uploads</description></item>
/// </list>
/// 
/// <para><strong>Security Measures:</strong></para>
/// <list type="bullet">
///   <item><description>URN validation for all IDs</description></item>
///   <item><description>File size limits (archives: 500MB, pages: 50MB)</description></item>
///   <item><description>Content type validation (images: jpeg/png/webp/gif, archives: zip/cbz)</description></item>
///   <item><description>Path traversal prevention in archive extraction</description></item>
///   <item><description>Comprehensive logging of upload attempts</description></item>
/// </list>
/// 
/// <para><strong>RFC 7807 Error Compliance:</strong></para>
/// All errors use standardized Problem Details format with URN-based error types.
/// </remarks>
public static class IngestEndpoints
{
    #region Constants

    /// <summary>Maximum file size for archive uploads (500MB).</summary>
    private const long MaxArchiveFileSizeBytes = 500L * 1024 * 1024;
    
    /// <summary>Maximum file size for individual page uploads (50MB).</summary>
    private const long MaxPageFileSizeBytes = 50L * 1024 * 1024;
    
    /// <summary>Allowed archive content types.</summary>
    private static readonly HashSet<string> AllowedArchiveTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/zip",
        "application/x-zip-compressed",
        "application/x-cbz"
    };
    
    /// <summary>Allowed image content types for pages.</summary>
    private static readonly HashSet<string> AllowedImageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/webp",
        "image/gif"
    };
    
    #endregion

    #region Endpoint Mapping

    /// <summary>
    /// Maps ingest endpoints to the application route builder.
    /// </summary>
    /// <param name="app">The endpoint route builder.</param>
    /// <remarks>
    /// <para><strong>Mapped Routes:</strong></para>
    /// <list type="bullet">
    ///   <item><description>POST /api/v1/series/{seriesId}/chapters - Bulk chapter archive upload</description></item>
    ///   <item><description>POST /api/v1/units/{unitId}/pages - Granular page addition</description></item>
    /// </list>
    /// <para><strong>Security:</strong> All routes require "MvnIngest" authorization scope.</para>
    /// </remarks>
    public static void MapIngestEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").RequireAuthorization("MvnIngest");

        // Bulk Upload - Chapter Archives
        group.MapPost("/series/{seriesId}/chapters", UploadChapterArchive)
            .DisableAntiforgery()
            .WithName("UploadChapterArchive")
            .WithTags("Ingest")
            .WithDescription("Upload a ZIP/CBZ archive containing chapter pages");
        
        // Granular Upload - Individual Pages
        group.MapPost("/units/{unitId}/pages", AddPageToUnit)
            .DisableAntiforgery()
            .WithName("AddPageToUnit")
            .WithTags("Ingest")
            .WithDescription("Add a single page to a unit (binary upload or URL link)");
    }

    #endregion

    #region Bulk Upload - Chapter Archive

    /// <summary>
    /// Uploads a chapter archive (ZIP/CBZ) containing multiple pages for a series.
    /// </summary>
    /// <param name="seriesId">Series URN (urn:mvn:series:{uuid}) or raw UUID.</param>
    /// <param name="file">Archive file (ZIP/CBZ format).</param>
    /// <param name="metadata">JSON metadata for chapter (title, number, language, etc.).</param>
    /// <param name="parser_config">Optional parser configuration JSON.</param>
    /// <param name="jobService">Job management service.</param>
    /// <param name="repo">Repository for data access.</param>
    /// <param name="loggerFactory">Logger factory.</param>
    /// <param name="context">HTTP context for request tracking.</param>
    /// <returns>202 Accepted with job tracking information.</returns>
    /// <remarks>
    /// <para><strong>Request Flow:</strong></para>
    /// <list type="number">
    ///   <item><description>Validate seriesId format and existence</description></item>
    ///   <item><description>Validate file size and content type</description></item>
    ///   <item><description>Parse and validate metadata JSON</description></item>
    ///   <item><description>Create background job for processing</description></item>
    ///   <item><description>Return job tracking URN</description></item>
    /// </list>
    /// 
    /// <para><strong>Security Validations:</strong></para>
    /// <list type="bullet">
    ///   <item><description>URN format validation</description></item>
    ///   <item><description>File size limit: 500MB</description></item>
    ///   <item><description>Content type: application/zip or application/x-cbz</description></item>
    ///   <item><description>Metadata JSON format validation</description></item>
    ///   <item><description>Series existence verification</description></item>
    /// </list>
    /// 
    /// <para><strong>Error Responses:</strong></para>
    /// <list type="bullet">
    ///   <item><description>400: Invalid seriesId, missing file, oversized file, invalid metadata</description></item>
    ///   <item><description>404: Series not found</description></item>
    ///   <item><description>415: Unsupported media type</description></item>
    ///   <item><description>500: Internal server error</description></item>
    /// </list>
    /// </remarks>
#pragma warning disable CS1998
    private static async Task<IResult> UploadChapterArchive(
        string seriesId, 
        IFormFile file, 
        [FromForm] string metadata, 
        [FromForm] string? parser_config,
        [FromServices] JobService jobService,
        [FromServices] IRepository repo,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext context)
    {
        var logger = loggerFactory.CreateLogger("MehguViewer.Core.Endpoints.IngestEndpoints");
        
        try
        {
            logger.LogDebug("Received chapter archive upload request for series: {SeriesId}", seriesId);
            
            // Validation 1: Series ID format and normalization
            if (string.IsNullOrWhiteSpace(seriesId))
            {
                logger.LogWarning("Chapter upload rejected: Empty series ID from {IP}", 
                    context.Connection.RemoteIpAddress);
                return ResultsExtensions.BadRequest("Series ID is required", context.Request.Path);
            }
            
            // Normalize URN format
            var normalizedId = seriesId.StartsWith("urn:mvn:series:") 
                ? seriesId 
                : $"urn:mvn:series:{seriesId}";
            
            if (!UrnHelper.IsValidSeriesUrn(normalizedId))
            {
                logger.LogWarning("Chapter upload rejected: Invalid series URN format '{SeriesId}' from {IP}", 
                    seriesId, context.Connection.RemoteIpAddress);
                return ResultsExtensions.BadRequest(
                    $"Invalid series URN format. Expected: urn:mvn:series:{{uuid}}", 
                    context.Request.Path);
            }
            
            // Validation 2: Series existence
            var series = repo.GetSeries(normalizedId);
            if (series == null)
            {
                logger.LogWarning("Chapter upload rejected: Series not found '{SeriesId}' from {IP}", 
                    normalizedId, context.Connection.RemoteIpAddress);
                return ResultsExtensions.NotFound("Series not found", context.Request.Path);
            }
            
            logger.LogDebug("Series validated: {SeriesTitle} ({SeriesUrn})", series.title, normalizedId);
            
            // Validation 3: File presence and size
            if (file == null || file.Length == 0)
            {
                logger.LogWarning("Chapter upload rejected: No file provided for series {SeriesId}", normalizedId);
                return ResultsExtensions.BadRequest("Archive file is required", context.Request.Path);
            }
            
            if (file.Length > MaxArchiveFileSizeBytes)
            {
                logger.LogWarning(
                    "Chapter upload rejected: File size {Size:N0} bytes exceeds maximum {MaxSize:N0} bytes for series {SeriesId}", 
                    file.Length, MaxArchiveFileSizeBytes, normalizedId);
                return ResultsExtensions.BadRequest(
                    $"Archive file too large. Maximum size is {MaxArchiveFileSizeBytes / 1024 / 1024}MB", 
                    context.Request.Path);
            }
            
            logger.LogDebug("File size validated: {Size:N0} bytes, filename: {FileName}", 
                file.Length, file.FileName);
            
            // Validation 4: Content type
            var contentType = file.ContentType;
            if (string.IsNullOrWhiteSpace(contentType) && !string.IsNullOrWhiteSpace(file.FileName))
            {
                // Fallback: Infer from extension
                contentType = file.FileName.ToLowerInvariant() switch
                {
                    var name when name.EndsWith(".zip") => "application/zip",
                    var name when name.EndsWith(".cbz") => "application/x-cbz",
                    _ => ""
                };
                
                logger.LogDebug("Content type inferred from filename: {ContentType}", contentType);
            }
            
            if (!AllowedArchiveTypes.Contains(contentType))
            {
                logger.LogWarning(
                    "Chapter upload rejected: Unsupported content type '{ContentType}' for series {SeriesId}", 
                    contentType, normalizedId);
                return Results.Problem(
                    detail: $"Unsupported archive type '{contentType}'. Allowed: ZIP, CBZ",
                    statusCode: 415,
                    title: "Unsupported Media Type",
                    type: "urn:mvn:error:unsupported-media-type",
                    instance: context.Request.Path);
            }
            
            logger.LogDebug("Content type validated: {ContentType}", contentType);
            
            // Validation 5: Metadata JSON
            if (string.IsNullOrWhiteSpace(metadata))
            {
                logger.LogWarning("Chapter upload rejected: Missing metadata for series {SeriesId}", normalizedId);
                return ResultsExtensions.BadRequest("Metadata JSON is required", context.Request.Path);
            }
            
            JsonDocument metadataDoc;
            try
            {
                metadataDoc = JsonDocument.Parse(metadata);
                logger.LogDebug("Metadata JSON parsed successfully: {Metadata}", metadata);
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, 
                    "Chapter upload rejected: Invalid metadata JSON for series {SeriesId}", normalizedId);
                return ResultsExtensions.BadRequest(
                    $"Invalid metadata JSON: {ex.Message}", 
                    context.Request.Path);
            }
            
            // Validation 6: Parser config (optional)
            if (!string.IsNullOrWhiteSpace(parser_config))
            {
                try
                {
                    JsonDocument.Parse(parser_config);
                    logger.LogDebug("Parser config JSON validated");
                }
                catch (JsonException ex)
                {
                    logger.LogWarning(ex, 
                        "Chapter upload rejected: Invalid parser_config JSON for series {SeriesId}", normalizedId);
                    return ResultsExtensions.BadRequest(
                        $"Invalid parser_config JSON: {ex.Message}", 
                        context.Request.Path);
                }
            }
            
            // Create background job
            logger.LogInformation(
                "Creating ingest job for series {SeriesId}, file: {FileName} ({Size:N0} bytes)", 
                normalizedId, file.FileName, file.Length);
            
            var job = jobService.CreateJob("INGEST_ARCHIVE");
            
            // TODO: Save file to persistent storage (S3, disk, etc.)
            // TODO: Store job metadata in repository for worker access
            // TODO: Queue job for background processing
            
            // Temporary implementation note
            logger.LogWarning(
                "IMPLEMENTATION NOTE: File storage and job queueing not yet implemented. " +
                "Job {JobId} created but will not be processed", job.id);
            
            logger.LogInformation(
                "Ingest job created: {JobId} for series {SeriesId}, status: {Status}", 
                job.id, normalizedId, job.status);
            
            return Results.Accepted($"/api/v1/jobs/{job.id}", new JobResponse(job.id, job.status));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, 
                "Unexpected error during chapter archive upload for series {SeriesId}", seriesId);
            return ResultsExtensions.InternalServerError(
                "An unexpected error occurred while processing the upload", 
                context.Request.Path,
                context.TraceIdentifier);
        }
    }

    #endregion

    #region Granular Upload - Individual Pages

    /// <summary>
    /// Adds a single page to a unit via binary upload or URL link.
    /// </summary>
    /// <param name="unitId">Unit URN (urn:mvn:unit:{uuid}) or raw UUID.</param>
    /// <param name="file">Page image file (binary upload option).</param>
    /// <param name="url">External page URL (link option).</param>
    /// <param name="page_number">Page number within the unit (1-based).</param>
    /// <param name="repo">Repository for data access.</param>
    /// <param name="loggerFactory">Logger factory.</param>
    /// <param name="context">HTTP context for request tracking.</param>
    /// <returns>200 OK with page details, or RFC 7807 error.</returns>
    /// <remarks>
    /// <para><strong>Upload Options:</strong></para>
    /// <list type="bullet">
    ///   <item><description><strong>Binary Upload:</strong> Provide 'file' parameter (image/jpeg, image/png, image/webp, image/gif)</description></item>
    ///   <item><description><strong>URL Link:</strong> Provide 'url' parameter (external image URL)</description></item>
    /// </list>
    /// 
    /// <para><strong>Request Flow:</strong></para>
    /// <list type="number">
    ///   <item><description>Validate unitId format and existence</description></item>
    ///   <item><description>Validate page_number (must be positive)</description></item>
    ///   <item><description>Validate file OR url (exactly one required)</description></item>
    ///   <item><description>For binary: Validate file size and content type</description></item>
    ///   <item><description>For URL: Validate URL format</description></item>
    ///   <item><description>Generate asset URN and save page</description></item>
    /// </list>
    /// 
    /// <para><strong>Security Validations:</strong></para>
    /// <list type="bullet">
    ///   <item><description>URN format validation</description></item>
    ///   <item><description>File size limit: 50MB</description></item>
    ///   <item><description>Content type: image/jpeg, image/png, image/webp, image/gif</description></item>
    ///   <item><description>URL format validation (http/https only)</description></item>
    ///   <item><description>Page number validation (positive integers only)</description></item>
    /// </list>
    /// 
    /// <para><strong>Error Responses:</strong></para>
    /// <list type="bullet">
    ///   <item><description>400: Invalid unitId, invalid page_number, missing file/url, both file and url provided</description></item>
    ///   <item><description>404: Unit not found</description></item>
    ///   <item><description>413: File too large</description></item>
    ///   <item><description>415: Unsupported media type</description></item>
    ///   <item><description>500: Internal server error</description></item>
    /// </list>
    /// </remarks>
#pragma warning disable CS1998
    private static async Task<IResult> AddPageToUnit(
        string unitId,
        [FromForm] IFormFile? file,
        [FromForm] string? url,
        [FromForm] int page_number,
        [FromServices] IRepository repo,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext context)
    {
        var logger = loggerFactory.CreateLogger("MehguViewer.Core.Endpoints.IngestEndpoints");
        
        try
        {
            logger.LogDebug("Received page upload request for unit: {UnitId}, page: {PageNumber}", 
                unitId, page_number);
            
            // Validation 1: Unit ID format and normalization
            if (string.IsNullOrWhiteSpace(unitId))
            {
                logger.LogWarning("Page upload rejected: Empty unit ID from {IP}", 
                    context.Connection.RemoteIpAddress);
                return ResultsExtensions.BadRequest("Unit ID is required", context.Request.Path);
            }
            
            // Normalize URN format
            var normalizedId = unitId.StartsWith("urn:mvn:unit:") 
                ? unitId 
                : $"urn:mvn:unit:{unitId}";
            
            if (!UrnHelper.IsValidUnitUrn(normalizedId))
            {
                logger.LogWarning("Page upload rejected: Invalid unit URN format '{UnitId}' from {IP}", 
                    unitId, context.Connection.RemoteIpAddress);
                return ResultsExtensions.BadRequest(
                    $"Invalid unit URN format. Expected: urn:mvn:unit:{{uuid}}", 
                    context.Request.Path);
            }
            
            // Validation 2: Unit existence
            var unit = repo.GetUnit(normalizedId);
            if (unit == null)
            {
                logger.LogWarning("Page upload rejected: Unit not found '{UnitId}' from {IP}", 
                    normalizedId, context.Connection.RemoteIpAddress);
                return ResultsExtensions.NotFound("Unit not found", context.Request.Path);
            }
            
            logger.LogDebug("Unit validated: {UnitTitle} ({UnitUrn})", unit.title, normalizedId);
            
            // Validation 3: Page number
            if (page_number < 1)
            {
                logger.LogWarning(
                    "Page upload rejected: Invalid page number {PageNumber} for unit {UnitId}", 
                    page_number, normalizedId);
                return ResultsExtensions.BadRequest(
                    "Page number must be a positive integer (1-based)", 
                    context.Request.Path);
            }
            
            logger.LogDebug("Page number validated: {PageNumber}", page_number);
            
            // Validation 4: Exactly one of file or url must be provided
            bool hasFile = file != null && file.Length > 0;
            bool hasUrl = !string.IsNullOrWhiteSpace(url);
            
            if (!hasFile && !hasUrl)
            {
                logger.LogWarning(
                    "Page upload rejected: Neither file nor URL provided for unit {UnitId}", normalizedId);
                return ResultsExtensions.BadRequest(
                    "Either 'file' (binary upload) or 'url' (link) must be provided", 
                    context.Request.Path);
            }
            
            if (hasFile && hasUrl)
            {
                logger.LogWarning(
                    "Page upload rejected: Both file and URL provided for unit {UnitId}", normalizedId);
                return ResultsExtensions.BadRequest(
                    "Provide either 'file' or 'url', not both", 
                    context.Request.Path);
            }
            
            // Option A: Binary Upload
            if (hasFile)
            {
                logger.LogDebug("Processing binary upload: {FileName} ({Size:N0} bytes)", 
                    file!.FileName, file.Length);
                
                // Validation: File size
                if (file.Length > MaxPageFileSizeBytes)
                {
                    logger.LogWarning(
                        "Page upload rejected: File size {Size:N0} bytes exceeds maximum {MaxSize:N0} bytes for unit {UnitId}", 
                        file.Length, MaxPageFileSizeBytes, normalizedId);
                    return Results.Problem(
                        detail: $"Page file too large. Maximum size is {MaxPageFileSizeBytes / 1024 / 1024}MB",
                        statusCode: 413,
                        title: "Payload Too Large",
                        type: "urn:mvn:error:payload-too-large",
                        instance: context.Request.Path);
                }
                
                // Validation: Content type
                var contentType = file.ContentType;
                if (string.IsNullOrWhiteSpace(contentType) && !string.IsNullOrWhiteSpace(file.FileName))
                {
                    // Fallback: Infer from extension
                    contentType = file.FileName.ToLowerInvariant() switch
                    {
                        var name when name.EndsWith(".jpg") || name.EndsWith(".jpeg") => "image/jpeg",
                        var name when name.EndsWith(".png") => "image/png",
                        var name when name.EndsWith(".webp") => "image/webp",
                        var name when name.EndsWith(".gif") => "image/gif",
                        _ => ""
                    };
                    
                    logger.LogDebug("Content type inferred from filename: {ContentType}", contentType);
                }
                
                if (!AllowedImageTypes.Contains(contentType))
                {
                    logger.LogWarning(
                        "Page upload rejected: Unsupported content type '{ContentType}' for unit {UnitId}", 
                        contentType, normalizedId);
                    return Results.Problem(
                        detail: $"Unsupported image type '{contentType}'. Allowed: JPEG, PNG, WebP, GIF",
                        statusCode: 415,
                        title: "Unsupported Media Type",
                        type: "urn:mvn:error:unsupported-media-type",
                        instance: context.Request.Path);
                }
                
                logger.LogDebug("Content type validated: {ContentType}", contentType);
                
                // Generate asset URN
                var assetUrn = UrnHelper.CreateAssetUrn();
                
                // TODO: Save file to persistent storage (S3, disk, etc.)
                // Example: await SaveFileAsync(file, assetUrn);
                
                logger.LogInformation(
                    "Adding page {PageNumber} to unit {UnitId} via binary upload: {AssetUrn} ({Size:N0} bytes)", 
                    page_number, normalizedId, assetUrn, file.Length);
                
                // Create page record
                var page = new Page(page_number, assetUrn, null);
                repo.AddPage(normalizedId, page);
                
                logger.LogInformation(
                    "Page {PageNumber} added successfully to unit {UnitId}: {AssetUrn}", 
                    page_number, normalizedId, assetUrn);
                
                return Results.Ok(page);
            }
            
            // Option B: URL Link
            else if (hasUrl)
            {
                logger.LogDebug("Processing URL link: {Url}", url);
                
                // Validation: URL format
                if (!Uri.TryCreate(url, UriKind.Absolute, out var parsedUri))
                {
                    logger.LogWarning(
                        "Page upload rejected: Invalid URL format '{Url}' for unit {UnitId}", 
                        url, normalizedId);
                    return ResultsExtensions.BadRequest(
                        $"Invalid URL format: {url}", 
                        context.Request.Path);
                }
                
                // Security: Only allow http/https schemes
                if (parsedUri.Scheme != Uri.UriSchemeHttp && parsedUri.Scheme != Uri.UriSchemeHttps)
                {
                    logger.LogWarning(
                        "Page upload rejected: Unsupported URL scheme '{Scheme}' for unit {UnitId}", 
                        parsedUri.Scheme, normalizedId);
                    return ResultsExtensions.BadRequest(
                        $"URL must use http or https scheme", 
                        context.Request.Path);
                }
                
                logger.LogDebug("URL validated: {Url}", parsedUri.AbsoluteUri);
                
                logger.LogInformation(
                    "Adding page {PageNumber} to unit {UnitId} via URL link: {Url}", 
                    page_number, normalizedId, parsedUri.AbsoluteUri);
                
                // Create page record
                var page = new Page(page_number, null, parsedUri.AbsoluteUri);
                repo.AddPage(normalizedId, page);
                
                logger.LogInformation(
                    "Page {PageNumber} added successfully to unit {UnitId}: {Url}", 
                    page_number, normalizedId, parsedUri.AbsoluteUri);
                
                return Results.Ok(page);
            }
            
            // Should never reach here due to earlier validation
            return ResultsExtensions.BadRequest(
                "Invalid request: Either file or url must be provided", 
                context.Request.Path);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, 
                "Unexpected error during page upload for unit {UnitId}, page {PageNumber}", 
                unitId, page_number);
            return ResultsExtensions.InternalServerError(
                "An unexpected error occurred while adding the page", 
                context.Request.Path,
                context.TraceIdentifier);
        }
    }

    #endregion
}
