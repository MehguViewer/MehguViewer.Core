using MehguViewer.Core.Services;
using MehguViewer.Core.Workers;
using MehguViewer.Core.Helpers;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace MehguViewer.Core.Endpoints;

/// <summary>
/// Provides HTTP endpoints for asset retrieval and management in the MehguViewer system.
/// </summary>
/// <remarks>
/// <para><strong>Asset Management:</strong></para>
/// Assets in MehguViewer represent media files (images, pages) associated with series units.
/// This endpoint handles delivery of assets with support for multiple image variants.
/// 
/// <para><strong>Supported Image Variants:</strong></para>
/// <list type="bullet">
///   <item><description><strong>THUMBNAIL:</strong> 400x600px - Optimized for grid views and previews</description></item>
///   <item><description><strong>WEB:</strong> 800x1200px (default) - Standard viewing experience</description></item>
///   <item><description><strong>RAW:</strong> 1600x2400px - High-quality original resolution</description></item>
/// </list>
/// 
/// <para><strong>Security Features:</strong></para>
/// <list type="bullet">
///   <item><description>URN validation to prevent injection attacks</description></item>
///   <item><description>Authorization via JWT (MvnRead scope required)</description></item>
///   <item><description>Query string token support for embedded image tags</description></item>
///   <item><description>Request logging for audit trails</description></item>
/// </list>
/// 
/// <para><strong>Performance Optimizations:</strong></para>
/// <list type="bullet">
///   <item><description>HTTP client pooling via IHttpClientFactory</description></item>
///   <item><description>Stream-based responses to minimize memory usage</description></item>
///   <item><description>Request timing telemetry for monitoring</description></item>
/// </list>
/// 
/// <para><strong>Future Enhancements:</strong></para>
/// <list type="bullet">
///   <item><description>Direct storage access (S3, Azure Blob, local filesystem)</description></item>
///   <item><description>CDN integration for static asset delivery</description></item>
///   <item><description>On-the-fly image resizing and caching</description></item>
///   <item><description>Series ownership validation for access control</description></item>
/// </list>
/// </remarks>
public static class AssetEndpoints
{
    #region Constants

    /// <summary>Image variant identifiers matching Proto specification</summary>
    private const string VariantThumbnail = "THUMBNAIL";
    private const string VariantWeb = "WEB";
    private const string VariantRaw = "RAW";
    
    /// <summary>Default variant when none specified</summary>
    private const string DefaultVariant = VariantWeb;
    
    /// <summary>Image dimensions for each variant (width x height)</summary>
    private const string ThumbnailSize = "400x600";
    private const string WebSize = "800x1200";
    private const string RawSize = "1600x2400";
    
    /// <summary>Content type for image responses</summary>
    private const string ImageContentType = "image/png";
    
    /// <summary>Performance threshold for slow asset retrieval warnings (milliseconds)</summary>
    private const int SlowAssetThresholdMs = 2000;

    #endregion

    #region Endpoint Registration

    /// <summary>
    /// Registers asset-related HTTP endpoints with the application's routing system.
    /// </summary>
    /// <param name="app">The endpoint route builder to register routes with.</param>
    /// <remarks>
    /// All endpoints require JWT authentication with "MvnRead" scope.
    /// Query string token authentication is handled by middleware for embedded image tags.
    /// </remarks>
    public static void MapAssetEndpoints(this IEndpointRouteBuilder app)
    {
        // Note: Auth is handled via Query String token in Program.cs middleware for img tags
        var group = app.MapGroup("/api/v1/assets")
            .RequireAuthorization("MvnRead")
            .WithTags("Assets")
            .WithDescription("Asset retrieval and management endpoints");

        group.MapGet("/{assetUrn}", GetAsset)
            .WithName("GetAsset")
            .WithSummary("Retrieve an asset by URN with optional variant")
            .Produces<Stream>(200, ImageContentType)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(404)
            .ProducesProblem(500);
    }

    #endregion

    #region Endpoint Handlers

    /// <summary>
    /// Retrieves an asset (image/page) by its URN with support for multiple image variants.
    /// </summary>
    /// <param name="assetUrn">The URN identifier for the asset (format: urn:mvn:asset:{guid}).</param>
    /// <param name="variant">Optional image variant: THUMBNAIL, WEB (default), or RAW.</param>
    /// <param name="httpClientFactory">HTTP client factory for external requests (injected).</param>
    /// <param name="logger">Logger instance for diagnostic logging (injected).</param>
    /// <param name="context">Current HTTP context for request/response details.</param>
    /// <returns>
    /// A stream containing the asset image data, or an RFC 7807 Problem Details response on error.
    /// </returns>
    /// <remarks>
    /// <para><strong>Current Implementation (Prototype):</strong></para>
    /// Returns placeholder images from placehold.co to simulate asset delivery.
    /// This demonstrates the API contract and variant selection logic.
    /// 
    /// <para><strong>Production Implementation Requirements:</strong></para>
    /// <list type="number">
    ///   <item><description>Validate URN format and extract asset identifier</description></item>
    ///   <item><description>Verify user has read access to the parent series</description></item>
    ///   <item><description>Fetch asset from storage backend (S3, Azure Blob, local disk)</description></item>
    ///   <item><description>Apply image processing for requested variant (if not pre-cached)</description></item>
    ///   <item><description>Set appropriate caching headers for CDN/browser caching</description></item>
    ///   <item><description>Track metrics for monitoring and optimization</description></item>
    /// </list>
    /// 
    /// <para><strong>Security Considerations:</strong></para>
    /// <list type="bullet">
    ///   <item><description>URN validation prevents path traversal attacks</description></item>
    ///   <item><description>Authorization ensures only authenticated users with proper scopes access assets</description></item>
    ///   <item><description>Future: Series-level permission checks to enforce content access control</description></item>
    /// </list>
    /// 
    /// <para><strong>Performance Notes:</strong></para>
    /// Requests taking longer than 2000ms trigger warning logs for investigation.
    /// Stream-based responses minimize memory consumption for large images.
    /// </remarks>
    private static async Task<IResult> GetAsset(
        string assetUrn,
        [FromQuery] string? variant,
        [FromServices] IHttpClientFactory httpClientFactory,
        [FromServices] ILogger<IResult> logger,
        HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var requestPath = context.Request.Path;
        var traceId = context.TraceIdentifier;
        
        // === Phase 1: Input Validation ===
        logger.LogDebug(
            "Asset request received: URN={AssetUrn}, Variant={Variant}, TraceId={TraceId}",
            assetUrn, variant ?? "default", traceId);
        
        if (string.IsNullOrWhiteSpace(assetUrn))
        {
            logger.LogWarning(
                "Asset request rejected: Missing URN (TraceId: {TraceId})",
                traceId);
            return ResultsExtensions.BadRequest("Asset URN is required.", requestPath, traceId);
        }

        // Validate URN format (basic validation - full URN parsing would use UrnHelper)
        if (!assetUrn.StartsWith("urn:mvn:asset:", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning(
                "Asset request rejected: Invalid URN format '{AssetUrn}' (TraceId: {TraceId})",
                assetUrn, traceId);
            return ResultsExtensions.BadRequest(
                "Invalid asset URN format. Expected: urn:mvn:asset:{id}",
                requestPath,
                traceId);
        }

        // === Phase 2: Variant Selection & Validation ===
        var selectedVariant = NormalizeVariant(variant);
        var imageSize = GetImageSizeForVariant(selectedVariant);
        
        logger.LogDebug(
            "Variant resolved: Requested={RequestedVariant}, Selected={SelectedVariant}, Size={ImageSize}",
            variant ?? "none", selectedVariant, imageSize);

        // === Phase 3: Asset Retrieval (Prototype Implementation) ===
        // TODO: Replace with actual storage backend implementation
        // Production implementation should:
        // 1. Parse URN to extract asset ID: UrnHelper.TryParseUrn(assetUrn, out var parts)
        // 2. Query repository for asset metadata and storage location
        // 3. Validate user has access to parent series (series ownership/permissions)
        // 4. Fetch from storage: S3, Azure Blob Storage, or local filesystem
        // 5. Apply ImageProcessingService if variant needs generation
        // 6. Set Cache-Control headers for CDN optimization
        
        try
        {
            logger.LogInformation(
                "Retrieving asset (prototype mode): URN={AssetUrn}, Variant={Variant}",
                assetUrn, selectedVariant);
            
            var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10); // Prevent hanging requests
            
            var placeholderUrl = $"https://placehold.co/{imageSize}/png?text={Uri.EscapeDataString(assetUrn)}";
            var response = await client.GetAsync(placeholderUrl);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Asset not found: URN={AssetUrn}, Variant={Variant}, StatusCode={StatusCode} (TraceId: {TraceId})",
                    assetUrn, selectedVariant, response.StatusCode, traceId);
                return ResultsExtensions.NotFound(
                    $"Asset '{assetUrn}' not found",
                    requestPath,
                    traceId);
            }

            var stream = await response.Content.ReadAsStreamAsync();
            
            stopwatch.Stop();
            var elapsedMs = stopwatch.ElapsedMilliseconds;
            
            // Performance monitoring
            if (elapsedMs > SlowAssetThresholdMs)
            {
                logger.LogWarning(
                    "Slow asset retrieval detected: URN={AssetUrn}, Variant={Variant}, Duration={DurationMs}ms (TraceId: {TraceId})",
                    assetUrn, selectedVariant, elapsedMs, traceId);
            }
            else
            {
                logger.LogInformation(
                    "Asset retrieved successfully: URN={AssetUrn}, Variant={Variant}, Duration={DurationMs}ms (TraceId: {TraceId})",
                    assetUrn, selectedVariant, elapsedMs, traceId);
            }
            
            // Return stream with appropriate content type
            // TODO: Set caching headers: Cache-Control, ETag, Last-Modified
            return Results.Stream(stream, ImageContentType);
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            logger.LogError(ex,
                "HTTP error retrieving asset: URN={AssetUrn}, Variant={Variant}, Duration={DurationMs}ms (TraceId: {TraceId})",
                assetUrn, selectedVariant, stopwatch.ElapsedMilliseconds, traceId);
            
            return ResultsExtensions.InternalServerError(
                "Failed to retrieve asset due to network error",
                requestPath,
                traceId);
        }
        catch (TaskCanceledException ex)
        {
            stopwatch.Stop();
            logger.LogError(ex,
                "Timeout retrieving asset: URN={AssetUrn}, Variant={Variant}, Duration={DurationMs}ms (TraceId: {TraceId})",
                assetUrn, selectedVariant, stopwatch.ElapsedMilliseconds, traceId);
            
            return ResultsExtensions.InternalServerError(
                "Asset retrieval timed out",
                requestPath,
                traceId);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.LogError(ex,
                "Unexpected error retrieving asset: URN={AssetUrn}, Variant={Variant}, Duration={DurationMs}ms (TraceId: {TraceId})",
                assetUrn, selectedVariant, stopwatch.ElapsedMilliseconds, traceId);
            
            return ResultsExtensions.InternalServerError(
                "An unexpected error occurred while retrieving the asset",
                requestPath,
                traceId);
        }
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Normalizes and validates the requested image variant.
    /// </summary>
    /// <param name="variant">The requested variant string (case-insensitive).</param>
    /// <returns>A normalized variant identifier (THUMBNAIL, WEB, or RAW). Defaults to WEB if invalid/null.</returns>
    private static string NormalizeVariant(string? variant)
    {
        if (string.IsNullOrWhiteSpace(variant))
            return DefaultVariant;

        return variant.ToUpperInvariant() switch
        {
            VariantThumbnail => VariantThumbnail,
            VariantWeb => VariantWeb,
            VariantRaw => VariantRaw,
            _ => DefaultVariant // Invalid variant defaults to WEB
        };
    }

    /// <summary>
    /// Maps an image variant to its corresponding pixel dimensions.
    /// </summary>
    /// <param name="variant">The normalized variant identifier (THUMBNAIL, WEB, or RAW).</param>
    /// <returns>A string representing the image dimensions in "WIDTHxHEIGHT" format.</returns>
    private static string GetImageSizeForVariant(string variant)
    {
        return variant switch
        {
            VariantThumbnail => ThumbnailSize,
            VariantWeb => WebSize,
            VariantRaw => RawSize,
            _ => WebSize // Fallback to WEB size
        };
    }

    #endregion
}
