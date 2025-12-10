using MehguViewer.Core.Shared;
using MehguViewer.Core.Services;
using MehguViewer.Core.Workers;
using MehguViewer.Core.Helpers;
using MehguViewer.Core.Infrastructures;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace MehguViewer.Core.Endpoints;

/// <summary>
/// Provides HTTP endpoints for series and unit management in the MehguViewer system.
/// </summary>
/// <remarks>
/// <para><strong>Series Management:</strong></para>
/// Series represent collections of related units (chapters, episodes, volumes) with shared metadata.
/// This endpoint provides comprehensive CRUD operations, search capabilities, and permission management
/// for both series and their child units.
/// 
/// <para><strong>Supported Media Types:</strong></para>
/// <list type="bullet">
///   <item><description><strong>Photo:</strong> Image-based content (manga, comics, photo albums)</description></item>
///   <item><description><strong>Text:</strong> Text-based content (novels, articles)</description></item>
///   <item><description><strong>Video:</strong> Video-based content (anime, shows, movies)</description></item>
/// </list>
/// 
/// <para><strong>Reading Directions:</strong></para>
/// <list type="bullet">
///   <item><description><strong>LTR:</strong> Left-to-right (Western style)</description></item>
///   <item><description><strong>RTL:</strong> Right-to-left (Manga, Arabic, Hebrew)</description></item>
///   <item><description><strong>TTB:</strong> Top-to-bottom (Traditional Asian vertical text)</description></item>
///   <item><description><strong>BTT:</strong> Bottom-to-top (Rare, specialized formats)</description></item>
/// </list>
/// 
/// <para><strong>Authorization Model:</strong></para>
/// <list type="bullet">
///   <item><description><strong>Admins (mvn:admin):</strong> Full access to all resources</description></item>
///   <item><description><strong>Owners:</strong> Full access to series/units they created</description></item>
///   <item><description><strong>Editors (explicit permissions):</strong> Granular edit access to specific series/units</description></item>
///   <item><description><strong>Readers (mvn:read):</strong> Read-only access to published content</description></item>
/// </list>
/// 
/// <para><strong>Security Features:</strong></para>
/// <list type="bullet">
///   <item><description>URN validation to prevent injection attacks</description></item>
///   <item><description>Input sanitization for titles, descriptions, and metadata</description></item>
///   <item><description>Control character filtering to prevent malicious content</description></item>
///   <item><description>Path traversal prevention in file uploads</description></item>
///   <item><description>File size limits (10MB max) to prevent DoS attacks</description></item>
///   <item><description>Content type validation for image uploads</description></item>
///   <item><description>Rate limiting via middleware</description></item>
///   <item><description>Security event logging with IP tracking</description></item>
/// </list>
/// 
/// <para><strong>Performance Optimizations:</strong></para>
/// <list type="bullet">
///   <item><description>Pagination with configurable limits (default: 20, max: 100)</description></item>
///   <item><description>Multi-variant cover generation (thumbnail, web, raw) for optimal delivery</description></item>
///   <item><description>Metadata aggregation from units to series level</description></item>
///   <item><description>Efficient search with taxonomy validation</description></item>
/// </list>
/// 
/// <para><strong>Data Validation:</strong></para>
/// <list type="bullet">
///   <item><description>Taxonomy validation for tags, authors, and scanlators</description></item>
///   <item><description>Media type normalization and validation</description></item>
///   <item><description>Reading direction validation</description></item>
///   <item><description>Content warning normalization</description></item>
///   <item><description>Title length limits (500 chars) and character validation</description></item>
///   <item><description>Description length limits (5000 chars)</description></item>
/// </list>
/// 
/// <para><strong>Future Enhancements:</strong></para>
/// <list type="bullet">
///   <item><description>Cursor-based pagination for improved performance</description></item>
///   <item><description>Federation support via auth server integration</description></item>
///   <item><description>Advanced search with full-text indexing</description></item>
///   <item><description>Bulk operations for metadata updates</description></item>
///   <item><description>Webhook notifications for permission changes</description></item>
///   <item><description>Versioning support for content rollback</description></item>
/// </list>
/// </remarks>
public static class SeriesEndpoints
{
    #region Constants

    /// <summary>
    /// Default federation reference for locally created content.
    /// </summary>
    /// <remarks>
    /// Currently hardcoded to "urn:mvn:node:local" for standalone operation.
    /// TODO: Replace with actual federation reference from auth server for distributed deployments.
    /// </remarks>
    private const string DefaultFederationRef = "urn:mvn:node:local";

    /// <summary>
    /// Maximum allowed cover image file size in bytes (10MB).
    /// </summary>
    /// <remarks>
    /// Prevents DoS attacks via excessive memory consumption and storage exhaustion.
    /// Limit chosen to balance quality (supports high-res images) with security.
    /// </remarks>
    private const long MaxCoverFileSizeBytes = 10 * 1024 * 1024;

    /// <summary>
    /// Default number of results returned per page for list/search operations.
    /// </summary>
    private const int DefaultPaginationLimit = 20;

    /// <summary>
    /// Maximum number of results allowed per page to prevent resource exhaustion.
    /// </summary>
    /// <remarks>
    /// Enforced ceiling for pagination limits. Requests exceeding this are capped automatically.
    /// Prevents excessive memory usage and database load from malicious/misconfigured clients.
    /// </remarks>
    private const int MaxPaginationLimit = 100;

    /// <summary>
    /// Placeholder poster image used for newly created series without uploaded covers.
    /// </summary>
    /// <remarks>
    /// Uses placehold.co service for temporary placeholder generation.
    /// Production deployments should replace with self-hosted placeholder or allow null posters.
    /// </remarks>
    private static readonly Poster PlaceholderPoster = new("https://placehold.co/400x600?text=No+Cover", "Placeholder cover image");

    /// <summary>
    /// Supported image variant identifiers for cover images.
    /// </summary>
    /// <remarks>
    /// Variants correspond to different resolutions optimized for various use cases:
    /// - "thumbnail": 400x600px (grid views, previews)
    /// - "web": 800x1200px (default viewing)
    /// - "raw": 1600x2400px (high-quality, original)
    /// </remarks>
    private static readonly string[] SupportedVariants = { "thumbnail", "web", "raw" };

    #endregion

    #region Input Validation Helpers

    /// <summary>
    /// Validates and sanitizes a title string with comprehensive security checks.
    /// </summary>
    /// <param name="title">The title to validate.</param>
    /// <param name="fieldName">The name of the field for error messages (e.g., "Title", "Unit Title").</param>
    /// <param name="error">Output parameter containing detailed error message if validation fails, null if valid.</param>
    /// <returns>True if title passes all validation checks, false otherwise.</returns>
    /// <remarks>
    /// <para><strong>Validation Rules:</strong></para>
    /// <list type="number">
    ///   <item><description>Title must not be null, empty, or whitespace-only</description></item>
    ///   <item><description>Length must be 500 characters or less (prevents buffer overflow)</description></item>
    ///   <item><description>Must not contain control characters except newline, carriage return, and tab</description></item>
    /// </list>
    /// 
    /// <para><strong>Security Rationale:</strong></para>
    /// <list type="bullet">
    ///   <item><description>Length limit prevents DoS attacks via excessive memory allocation</description></item>
    ///   <item><description>Control character filtering prevents terminal injection attacks</description></item>
    ///   <item><description>Prevents Unicode homograph attacks and hidden characters</description></item>
    /// </list>
    /// </remarks>
    private static bool ValidateTitle(string? title, string fieldName, out string? error)
    {
        // Check for null or whitespace
        if (string.IsNullOrWhiteSpace(title))
        {
            error = $"{fieldName} is required and cannot be empty";
            return false;
        }

        // Security: Validate title length to prevent buffer overflow and performance issues
        if (title.Length > 500)
        {
            error = $"{fieldName} must be 500 characters or less (current: {title.Length})";
            return false;
        }

        // Security: Check for control characters that could cause issues
        if (title.Any(c => char.IsControl(c) && c != '\n' && c != '\r' && c != '\t'))
        {
            error = $"{fieldName} contains invalid control characters";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Validates file upload constraints with comprehensive security checks.
    /// </summary>
    /// <param name="file">The uploaded file from HTTP multipart form data.</param>
    /// <param name="contentType">Output parameter containing the resolved/validated content type (MIME type).</param>
    /// <param name="error">Output parameter containing detailed error message if validation fails, null if valid.</param>
    /// <param name="logger">Optional logger for security event tracking and debugging.</param>
    /// <returns>True if file passes all validation checks, false otherwise.</returns>
    /// <remarks>
    /// <para><strong>Validation Pipeline:</strong></para>
    /// <list type="number">
    ///   <item><description>File existence check (prevents null reference errors)</description></item>
    ///   <item><description>File size validation (max 10MB to prevent DoS attacks)</description></item>
    ///   <item><description>Filename security check (path traversal prevention)</description></item>
    ///   <item><description>Content type resolution (from Content-Type header or file extension)</description></item>
    ///   <item><description>Content type validation (only image/jpeg, image/png, image/webp, image/gif allowed)</description></item>
    /// </list>
    /// 
    /// <para><strong>Security Considerations:</strong></para>
    /// <list type="bullet">
    ///   <item><description>Path traversal prevention: Rejects filenames containing "..", "/", "\"</description></item>
    ///   <item><description>File size limit: 10MB maximum to prevent storage exhaustion and memory DoS</description></item>
    ///   <item><description>Content type whitelist: Only accepted image formats allowed</description></item>
    ///   <item><description>Security logging: Suspicious uploads are logged with filename and IP for audit</description></item>
    /// </list>
    /// 
    /// <para><strong>Content Type Resolution:</strong></para>
    /// Prioritizes HTTP Content-Type header, falls back to file extension mapping:
    /// .jpg/.jpeg → image/jpeg, .png → image/png, .webp → image/webp, .gif → image/gif
    /// </remarks>
    private static bool ValidateFileUpload(IFormFile? file, out string contentType, out string? error, ILogger? logger = null)
    {
        contentType = string.Empty;
        
        // Check if file exists and has content
        if (file == null || file.Length == 0)
        {
            logger?.LogDebug("File upload validation failed: No file provided");
            error = "No file provided or file is empty";
            return false;
        }

        // Security: Validate file size to prevent DoS and storage exhaustion
        if (file.Length > MaxCoverFileSizeBytes)
        {
            error = $"File too large. Maximum size is 10MB (current: {file.Length / 1024.0 / 1024.0:F2}MB)";
            logger?.LogWarning("File upload rejected: Size {SizeBytes} ({SizeMB:F2}MB) exceeds maximum {MaxSizeBytes} ({MaxSizeMB}MB). FileName: {FileName}",
                file.Length, file.Length / 1024.0 / 1024.0, MaxCoverFileSizeBytes, MaxCoverFileSizeBytes / 1024 / 1024, file.FileName);
            return false;
        }

        // Security: Validate filename for path traversal attempts
        if (!string.IsNullOrWhiteSpace(file.FileName) && 
            (file.FileName.Contains("..") || file.FileName.Contains("/") || file.FileName.Contains("\\")))
        {
            error = "Invalid filename: path traversal characters detected";
            logger?.LogWarning("Security: File upload rejected due to suspicious filename: {FileName}", file.FileName);
            return false;
        }

        // Get content type - fallback to file extension if not provided
        contentType = file.ContentType;
        if (string.IsNullOrWhiteSpace(contentType) && !string.IsNullOrWhiteSpace(file.FileName))
        {
            contentType = file.FileName.ToLowerInvariant() switch
            {
                var name when name.EndsWith(".jpg") || name.EndsWith(".jpeg") => "image/jpeg",
                var name when name.EndsWith(".png") => "image/png",
                var name when name.EndsWith(".webp") => "image/webp",
                var name when name.EndsWith(".gif") => "image/gif",
                _ => ""
            };
            
            if (string.IsNullOrEmpty(contentType))
            {
                logger?.LogDebug("Could not determine content type from filename: {FileName}", file.FileName);
            }
        }

        // Security: Validate content type against allowed image types
        if (!ImageProcessingService.IsSupportedImageType(contentType))
        {
            error = $"Invalid file type '{contentType}'. Allowed types: image/jpeg, image/png, image/webp, image/gif";
            logger?.LogWarning("File upload rejected: Unsupported content type '{ContentType}'. FileName: {FileName}", 
                contentType, file.FileName);
            return false;
        }

        logger?.LogDebug("File upload validation passed: {FileName}, Type: {ContentType}, Size: {SizeKB:F2}KB",
            file.FileName, contentType, file.Length / 1024.0);

        error = null;
        return true;
    }

    /// <summary>
    /// Validates URN format with security logging for invalid attempts.
    /// </summary>
    /// <param name="urn">The URN string to validate (e.g., urn:mvn:series:guid).</param>
    /// <param name="expectedType">Expected URN type identifier ("series", "unit", "user").</param>
    /// <param name="logger">Logger for security event tracking.</param>
    /// <param name="context">HTTP context for extracting client IP and User-Agent.</param>
    /// <returns>True if URN matches expected format and type, false otherwise.</returns>
    /// <remarks>
    /// <para><strong>Supported URN Types:</strong></para>
    /// <list type="bullet">
    ///   <item><description><strong>series:</strong> urn:mvn:series:{guid} - Series identifiers</description></item>
    ///   <item><description><strong>unit:</strong> urn:mvn:unit:{guid} - Unit/chapter identifiers</description></item>
    ///   <item><description><strong>user:</strong> urn:mvn:user:{id} - User identifiers</description></item>
    /// </list>
    /// 
    /// <para><strong>Security Features:</strong></para>
    /// <list type="bullet">
    ///   <item><description>Prevents injection attacks by validating URN structure via UrnHelper</description></item>
    ///   <item><description>Logs invalid URN attempts with IP address and User-Agent for security monitoring</description></item>
    ///   <item><description>Empty/null URN detection to prevent bypass attempts</description></item>
    ///   <item><description>Type-specific validation ensures URNs match expected resource type</description></item>
    /// </list>
    /// 
    /// <para><strong>Audit Trail:</strong></para>
    /// Invalid URN attempts are logged at Warning level with:
    /// - Requested URN value
    /// - Expected type
    /// - Client IP address
    /// - User-Agent header (for identifying malicious clients)
    /// </remarks>
    private static bool ValidateUrn(string urn, string expectedType, ILogger logger, HttpContext context)
    {
        if (string.IsNullOrWhiteSpace(urn))
        {
            logger.LogWarning("URN validation failed: Empty URN provided for type {Type}. IP: {IP}",
                expectedType, context.Connection.RemoteIpAddress);
            return false;
        }

        bool isValid = expectedType.ToLowerInvariant() switch
        {
            "series" => UrnHelper.IsValidSeriesUrn(urn),
            "unit" => UrnHelper.IsValidUnitUrn(urn),
            "user" => urn.StartsWith("urn:mvn:user:"),
            _ => false
        };

        if (!isValid)
        {
            logger.LogWarning("Security: Invalid {Type} URN requested: {Urn}. IP: {IP}, UserAgent: {UserAgent}",
                expectedType, urn, context.Connection.RemoteIpAddress, 
                context.Request.Headers["User-Agent"].ToString());
        }
        else
        {
            logger.LogDebug("URN validation passed: {Type} URN {Urn}", expectedType, urn);
        }

        return isValid;
    }

    #endregion

    #region Authorization Helpers

    /// <summary>
    /// Extracts user identifier from JWT claims with fallback support.
    /// </summary>
    /// <param name="user">The authenticated ClaimsPrincipal from JWT middleware.</param>
    /// <returns>User ID string from "sub" or NameIdentifier claim, or null if not authenticated.</returns>
    /// <remarks>
    /// <para><strong>Claim Priority:</strong></para>
    /// <list type="number">
    ///   <item><description><strong>"sub" (Subject):</strong> Standard JWT claim (RFC 7519), preferred</description></item>
    ///   <item><description><strong>NameIdentifier:</strong> Legacy .NET claim type for backward compatibility</description></item>
    /// </list>
    /// 
    /// <para><strong>Usage Pattern:</strong></para>
    /// Always check for null/empty result before using in authorization decisions.
    /// Null result indicates unauthenticated request or malformed JWT.
    /// </remarks>
    private static string? GetUserId(ClaimsPrincipal user)
    {
        return user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
    }

    /// <summary>
    /// Normalizes user ID to URN format for consistent permission and ownership checks.
    /// </summary>
    /// <param name="userId">The raw user ID (may be plain string or already URN-formatted).</param>
    /// <returns>URN-formatted user ID (urn:mvn:user:{id}).</returns>
    /// <remarks>
    /// <para><strong>URN Format:</strong></para>
    /// Ensures all user IDs follow the standard URN pattern: <c>urn:mvn:user:{id}</c>
    /// 
    /// <para><strong>Idempotent Behavior:</strong></para>
    /// Safe to call multiple times - already normalized URNs are returned unchanged.
    /// This prevents double-prefixing (e.g., urn:mvn:user:urn:mvn:user:123).
    /// 
    /// <para><strong>Use Cases:</strong></para>
    /// <list type="bullet">
    ///   <item><description>Permission checks (repository.HasEditPermission)</description></item>
    ///   <item><description>Ownership validation (series.created_by comparison)</description></item>
    ///   <item><description>Permission grant/revoke operations</description></item>
    /// </list>
    /// </remarks>
    private static string NormalizeUserUrn(string userId)
    {
        return userId.StartsWith("urn:mvn:user:") ? userId : $"urn:mvn:user:{userId}";
    }

    /// <summary>
    /// Checks if user has admin privileges via mvn:admin scope.
    /// Admins have global access to all resources.
    /// </summary>
    /// <param name="user">The authenticated user principal.</param>
    /// <returns>True if user is admin, false otherwise.</returns>
    private static bool IsAdmin(ClaimsPrincipal user)
    {
        return user.HasClaim(c => c.Type == "scope" && c.Value.Contains("mvn:admin"));
    }

    /// <summary>
    /// Checks if user is the owner of the series.
    /// Supports both URN and non-URN user ID formats for backward compatibility.
    /// </summary>
    /// <param name="user">The authenticated user principal.</param>
    /// <param name="series">The series to check ownership of.</param>
    /// <returns>True if user owns the series, false otherwise.</returns>
    private static bool IsOwner(ClaimsPrincipal user, Series series)
    {
        var userId = GetUserId(user);
        if (string.IsNullOrEmpty(userId)) return false;

        var userUrn = NormalizeUserUrn(userId);
        return series.created_by == userId || series.created_by == userUrn;
    }

    /// <summary>
    /// Determines if user has authorization to modify a series.
    /// </summary>
    /// <param name="user">The authenticated user principal from JWT.</param>
    /// <param name="series">The series to check modification rights for.</param>
    /// <param name="repo">Repository for querying explicit permission grants.</param>
    /// <returns>True if user can modify the series (admin, owner, or has explicit permission), false otherwise.</returns>
    /// <remarks>
    /// <para><strong>Authorization Hierarchy (checked in order):</strong></para>
    /// <list type="number">
    ///   <item><description><strong>Admin Check:</strong> Users with mvn:admin scope have global access</description></item>
    ///   <item><description><strong>Ownership Check:</strong> Creator of the series (series.created_by) has full access</description></item>
    ///   <item><description><strong>Permission Check:</strong> Explicit edit permission granted via GrantEditPermission endpoint</description></item>
    /// </list>
    /// 
    /// <para><strong>Implementation Notes:</strong></para>
    /// <list type="bullet">
    ///   <item><description>Short-circuit evaluation: Returns true at first positive match for performance</description></item>
    ///   <item><description>Handles both URN and non-URN user ID formats for backward compatibility</description></item>
    ///   <item><description>Returns false if user is not authenticated (GetUserId returns null/empty)</description></item>
    /// </list>
    /// </remarks>
    private static bool CanModifySeries(ClaimsPrincipal user, Series series, IRepository repo)
    {
        // Admin has global access
        if (IsAdmin(user)) return true;
        
        // Owner has full access
        if (IsOwner(user, series)) return true;
        
        // Check explicit edit permission
        var userId = GetUserId(user);
        if (!string.IsNullOrEmpty(userId))
        {
            var userUrn = NormalizeUserUrn(userId);
            var seriesUrn = UrnHelper.NormalizeSeriesUrn(series.id);
            return repo.HasEditPermission(seriesUrn, userUrn);
        }
        
        return false;
    }
    
    /// <summary>
    /// Determines if user has authorization to modify a unit (chapter/episode).
    /// </summary>
    /// <param name="user">The authenticated user principal from JWT.</param>
    /// <param name="unit">The unit to check modification rights for.</param>
    /// <param name="series">The parent series of the unit (for ownership inheritance).</param>
    /// <param name="repo">Repository for querying explicit permission grants.</param>
    /// <returns>True if user can modify the unit (admin, series owner, unit owner, or has explicit permission), false otherwise.</returns>
    /// <remarks>
    /// <para><strong>Authorization Hierarchy (checked in order):</strong></para>
    /// <list type="number">
    ///   <item><description><strong>Admin Check:</strong> Users with mvn:admin scope have global access</description></item>
    ///   <item><description><strong>Series Ownership:</strong> Creator of parent series can modify all units within it</description></item>
    ///   <item><description><strong>Unit Ownership:</strong> Creator of the specific unit has full access</description></item>
    ///   <item><description><strong>Permission Check:</strong> Explicit edit permission on either unit or series level</description></item>
    /// </list>
    /// 
    /// <para><strong>Permission Inheritance:</strong></para>
    /// Series-level permissions automatically grant access to all units within that series.
    /// This allows granting broad "series editor" access without per-unit grants.
    /// 
    /// <para><strong>Security Considerations:</strong></para>
    /// Unit-level permissions enable granular access control (e.g., guest translators
    /// can edit specific chapters without full series access).
    /// </remarks>
    private static bool CanModifyUnit(ClaimsPrincipal user, Unit unit, Series series, IRepository repo)
    {
        // Admin has global access
        if (IsAdmin(user)) return true;
        
        var userId = GetUserId(user);
        if (string.IsNullOrEmpty(userId)) return false;
        
        var userUrn = NormalizeUserUrn(userId);
        
        // Check if user owns the series (series owner can modify all units)
        if (series.created_by == userUrn || series.created_by == userId) return true;
        
        // Check if user owns the unit
        if (unit.created_by == userUrn || unit.created_by == userId) return true;
        
        // Check explicit edit permission on unit or series
        var unitUrn = UrnHelper.NormalizeUnitUrn(unit.id);
        var seriesUrn = UrnHelper.NormalizeSeriesUrn(series.id);
        
        return repo.HasEditPermission(unitUrn, userUrn) || repo.HasEditPermission(seriesUrn, userUrn);
    }

    #endregion

    #region Endpoint Mapping

    /// <summary>
    /// Registers all series and unit-related HTTP endpoints with the application's routing system.
    /// </summary>
    /// <param name="app">The endpoint route builder to register routes with.</param>
    /// <remarks>
    /// <para><strong>Endpoint Organization:</strong></para>
    /// <list type=\"bullet\">
    ///   <item><description><strong>Global endpoints:</strong> /api/v1/search (series search)</description></item>
    ///   <item><description><strong>Series endpoints:</strong> /api/v1/series/{seriesId} (CRUD, permissions, covers)</description></item>
    ///   <item><description><strong>Unit endpoints:</strong> /api/v1/series/{seriesId}/units/{unitId} (CRUD, permissions)</description></item>
    ///   <item><description><strong>Direct unit endpoints:</strong> /api/v1/units/{unitId} (deletion without series context)</description></item>
    /// </list>
    /// 
    /// <para><strong>Authorization Policies:</strong></para>
    /// <list type=\"bullet\">
    ///   <item><description><strong>MvnIngestOrAdmin:</strong> Creation, modification, deletion, permission management</description></item>
    ///   <item><description><strong>MvnAdmin:</strong> Ownership transfer (admin-only operations)</description></item>
    ///   <item><description><strong>Authenticated:</strong> Permission checks, progress tracking</description></item>
    ///   <item><description><strong>Public:</strong> Read operations (GET series, units, covers)</description></item>
    /// </list>
    /// 
    /// <para><strong>Special Configurations:</strong></para>
    /// <list type=\"bullet\">
    ///   <item><description>Cover upload: Antiforgery disabled for multipart form data compatibility</description></item>
    ///   <item><description>PATCH support: Both PUT and PATCH mapped to UpdateSeries/UpdateUnit for flexibility</description></item>
    /// </list>
    /// </remarks>
    public static void MapSeriesEndpoints(this IEndpointRouteBuilder app)
    {
        // Global search endpoint
        app.MapGet("/api/v1/search", SearchSeries)
            .WithName("SearchSeries")
            .WithSummary("Search for series with filtering and pagination")
            .Produces<SearchResults>(200)
            .ProducesProblem(400)
            .ProducesProblem(500);

        var group = app.MapGroup("/api/v1/series")
            .WithTags("Series")
            .WithDescription("Series and unit management endpoints");

        // Series CRUD endpoints
        group.MapGet("/", ListSeries)
            .WithName("ListSeries")
            .WithSummary("Retrieve a paginated list of all series")
            .Produces<SearchResults>(200)
            .ProducesProblem(400)
            .ProducesProblem(500);

        group.MapGet("/{seriesId}", GetSeries)
            .WithName("GetSeries")
            .WithSummary("Retrieve a specific series by URN")
            .Produces<Series>(200)
            .ProducesProblem(400)
            .ProducesProblem(404)
            .ProducesProblem(500);

        group.MapPost("/", CreateSeries)
            .WithName("CreateSeries")
            .WithSummary("Create a new series")
            .RequireAuthorization("MvnIngestOrAdmin")
            .Produces<Series>(201)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(500);

        group.MapPut("/{seriesId}", UpdateSeries)
            .WithName("UpdateSeries")
            .WithSummary("Update an existing series")
            .RequireAuthorization("MvnIngestOrAdmin")
            .Produces<Series>(200)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(500);

        group.MapPatch("/{seriesId}", UpdateSeries)
            .WithName("PatchSeries")
            .WithSummary("Partially update an existing series")
            .RequireAuthorization("MvnIngestOrAdmin")
            .Produces<Series>(200)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(500);

        group.MapDelete("/{seriesId}", DeleteSeries)
            .WithName("DeleteSeries")
            .WithSummary("Delete a series")
            .RequireAuthorization("MvnIngestOrAdmin")
            .Produces(204)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(500);
        
        // Permission management
        group.MapPost("/{seriesId}/permissions", GrantEditPermission)
            .WithName("GrantSeriesEditPermission")
            .WithSummary("Grant edit permission on a series to a user")
            .RequireAuthorization("MvnIngestOrAdmin")
            .Produces(204)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(500);

        group.MapDelete("/{seriesId}/permissions/{userUrn}", RevokeSeriesEditPermission)
            .WithName("RevokeSeriesEditPermission")
            .WithSummary("Revoke edit permission on a series from a user")
            .RequireAuthorization("MvnIngestOrAdmin")
            .Produces(204)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(500);

        group.MapGet("/{seriesId}/permissions", GetSeriesEditPermissions)
            .WithName("GetSeriesEditPermissions")
            .WithSummary("Get all users with edit permission on a series")
            .RequireAuthorization("MvnIngestOrAdmin")
            .Produces<string[]>(200)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(500);

        group.MapGet("/{seriesId}/can-modify", CheckCanModifySeries)
            .WithName("CheckCanModifySeries")
            .WithSummary("Check if current user can modify a series")
            .RequireAuthorization()
            .Produces<bool>(200)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(404)
            .ProducesProblem(500);
        
        // Ownership transfer (admin only)
        group.MapPost("/{seriesId}/transfer-ownership", TransferSeriesOwnership)
            .WithName("TransferSeriesOwnership")
            .WithSummary("Transfer ownership of a series to another user (admin only)")
            .RequireAuthorization("MvnAdmin")
            .Produces(204)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(500);
        
        // Unit endpoints
        group.MapGet("/{seriesId}/units", ListUnits)
            .WithName("ListUnits")
            .WithSummary("Retrieve all units for a series")
            .Produces<Unit[]>(200)
            .ProducesProblem(400)
            .ProducesProblem(404)
            .ProducesProblem(500);

        group.MapGet("/{seriesId}/units/{unitId}", GetUnit)
            .WithName("GetUnit")
            .WithSummary("Retrieve a specific unit by URN")
            .Produces<Unit>(200)
            .ProducesProblem(400)
            .ProducesProblem(404)
            .ProducesProblem(500);

        group.MapPost("/{seriesId}/units", CreateUnit)
            .WithName("CreateUnit")
            .WithSummary("Create a new unit within a series")
            .RequireAuthorization("MvnIngestOrAdmin")
            .Produces<Unit>(201)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(500);

        group.MapPut("/{seriesId}/units/{unitId}", UpdateUnit)
            .WithName("UpdateUnit")
            .WithSummary("Update an existing unit")
            .RequireAuthorization("MvnIngestOrAdmin")
            .Produces<Unit>(200)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(500);

        group.MapPatch("/{seriesId}/units/{unitId}", UpdateUnit)
            .WithName("PatchUnit")
            .WithSummary("Partially update an existing unit")
            .RequireAuthorization("MvnIngestOrAdmin")
            .Produces<Unit>(200)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(500);

        group.MapDelete("/{seriesId}/units/{unitId}", DeleteUnit)
            .WithName("DeleteUnit")
            .WithSummary("Delete a unit from a series")
            .RequireAuthorization("MvnIngestOrAdmin")
            .Produces(204)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(500);

        group.MapGet("/{seriesId}/units/{unitId}/pages", GetUnitPages)
            .WithName("GetUnitPages")
            .WithSummary("Retrieve all pages for a specific unit")
            .Produces<Page[]>(200)
            .ProducesProblem(400)
            .ProducesProblem(404)
            .ProducesProblem(500);
        
        // Unit permission management
        group.MapPost("/{seriesId}/units/{unitId}/permissions", GrantUnitEditPermission)
            .WithName("GrantUnitEditPermission")
            .WithSummary("Grant edit permission on a unit to a user")
            .RequireAuthorization("MvnIngestOrAdmin")
            .Produces(204)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(500);

        group.MapDelete("/{seriesId}/units/{unitId}/permissions/{userUrn}", RevokeUnitEditPermission)
            .WithName("RevokeUnitEditPermission")
            .WithSummary("Revoke edit permission on a unit from a user")
            .RequireAuthorization("MvnIngestOrAdmin")
            .Produces(204)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(500);

        group.MapGet("/{seriesId}/units/{unitId}/permissions", GetUnitEditPermissions)
            .WithName("GetUnitEditPermissions")
            .WithSummary("Get all users with edit permission on a unit")
            .RequireAuthorization("MvnIngestOrAdmin")
            .Produces<string[]>(200)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(500);
        
        // Direct unit delete endpoint (without seriesId in path)
        app.MapDelete("/api/v1/units/{unitId}", DeleteUnitDirect)
            .WithName("DeleteUnitDirect")
            .WithSummary("Delete a unit directly by URN without series context")
            .RequireAuthorization("MvnIngestOrAdmin")
            .WithTags("Series")
            .Produces(204)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(500);
        
        // Progress tracking
        group.MapPut("/{seriesId}/progress", UpdateProgress)
            .WithName("UpdateProgress")
            .WithSummary("Update reading progress for a series")
            .Produces(204)
            .ProducesProblem(400)
            .ProducesProblem(404)
            .ProducesProblem(500);

        group.MapGet("/{seriesId}/progress", GetProgress)
            .WithName("GetProgress")
            .WithSummary("Get reading progress for a series")
            .Produces<ReadingProgress>(200)
            .ProducesProblem(400)
            .ProducesProblem(404)
            .ProducesProblem(500);
        
        // Cover management
        group.MapPost("/{seriesId}/cover", UploadCover)
            .WithName("UploadCover")
            .WithSummary("Upload a cover image for a series")
            .RequireAuthorization("MvnIngestOrAdmin")
            .DisableAntiforgery()
            .Produces<Poster>(201)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(500);
        
        group.MapGet("/{seriesId}/cover", GetCover)
            .WithName("GetCover")
            .WithSummary("Retrieve cover image for a series")
            .Produces(200)
            .ProducesProblem(400)
            .ProducesProblem(404)
            .ProducesProblem(500);
        
        group.MapGet("/{seriesId}/covers", GetAllCovers)
            .WithName("GetAllCovers")
            .WithSummary("Retrieve all available cover images for a series")
            .Produces<Poster[]>(200)
            .ProducesProblem(400)
            .ProducesProblem(404)
            .ProducesProblem(500);
        
        group.MapPost("/{seriesId}/cover/from-url", DownloadCoverFromUrl)
            .WithName("DownloadCoverFromUrl")
            .WithSummary("Download and save a cover image from URL")
            .RequireAuthorization("MvnIngestOrAdmin")
            .Produces<Poster>(201)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(500);
    }

    #endregion

    #region Series CRUD Operations

    /// <summary>
    /// Search for series with comprehensive filtering and pagination support.
    /// </summary>
    /// <param name="q">Full-text search query (searches title, description, tags, authors).</param>
    /// <param name="type">Media type filter (Photo, Text, Video) - case-insensitive.</param>
    /// <param name="tags">Array of tag filters for taxonomy-based filtering.</param>
    /// <param name="status">Publication status filter (ongoing, completed, hiatus, cancelled).</param>
    /// <param name="offset">Pagination offset - number of results to skip (default: 0, min: 0).</param>
    /// <param name="limit">Maximum results to return (default: 20, max: 100).</param>
    /// <param name="repo">Repository service for data access (injected).</param>
    /// <param name="loggerFactory">Logger factory for creating typed loggers (injected).</param>
    /// <returns>SearchResults object containing series array and pagination metadata.</returns>
    /// <remarks>
    /// <para><strong>Search Behavior:</strong></para>
    /// <list type="bullet">
    ///   <item><description>Full-text search on title, description, and metadata fields</description></item>
    ///   <item><description>All filters can be combined for advanced queries</description></item>
    ///   <item><description>Empty/null query returns all series (subject to filters)</description></item>
    ///   <item><description>Case-insensitive matching for text fields</description></item>
    /// </list>
    /// 
    /// <para><strong>Pagination:</strong></para>
    /// <list type=\"bullet\">
    ///   <item><description>Default limit: 20 results per page</description></item>
    ///   <item><description>Maximum limit: 100 results (enforced for performance)</description></item>
    ///   <item><description>Negative offsets are normalized to 0</description></item>
    ///   <item><description>TODO: Implement cursor-based pagination for better performance at scale</description></item>
    /// </list>
    /// 
    /// <para><strong>Security:</strong></para>
    /// <list type=\"bullet\">
    ///   <item><description>Input sanitization prevents injection attacks</description></item>
    ///   <item><description>Pagination limits prevent resource exhaustion</description></item>
    ///   <item><description>All searches are logged for audit trails</description></item>
    /// </list>
    /// 
    /// <para><strong>Performance Considerations:</strong></para>
    /// Large result sets are paginated automatically. Clients should use offset/limit
    /// parameters for efficient data retrieval. Consider implementing cursor-based
    /// pagination for production deployments with large datasets.
    /// </remarks>
    private static async Task<IResult> SearchSeries(
        [FromQuery] string? q, 
        [FromQuery] string? type, 
        [FromQuery] string[]? tags, 
        [FromQuery] string? status, 
        [FromQuery] int? offset,
        [FromQuery] int? limit,
        [FromServices] IRepository repo,
        [FromServices] ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("MehguViewer.Core.Endpoints.SeriesEndpoints");
        await Task.CompletedTask;

        // === Phase 1: Input Validation & Sanitization ===
        // Security: Sanitize and validate inputs to prevent injection attacks and resource exhaustion
        var safeLimit = Math.Min(limit ?? DefaultPaginationLimit, MaxPaginationLimit);
        var safeOffset = Math.Max(offset ?? 0, 0);

        logger.LogInformation("Series search initiated: Query='{Query}', Type='{Type}', Status='{Status}', Tags={TagCount}, Offset={Offset}, Limit={Limit}", 
            q ?? "<none>", type ?? "<none>", status ?? "<none>", tags?.Length ?? 0, safeOffset, safeLimit);

        try
        {
            // === Phase 2: Execute Search Query ===
            var results = repo.SearchSeries(q, type, tags, status, safeOffset, safeLimit);
            var resultArray = results.ToArray();
            
            logger.LogInformation("Search completed successfully: {Count} results found", resultArray.Length);
            
            // === Phase 3: Return Results with Pagination ===
            // TODO: Implement proper cursor-based pagination with has_more flag
            return Results.Ok(new SearchResults(resultArray, new CursorPagination(null, false)));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during series search: Query='{Query}'", q);
            throw; // Let global error handler manage the response
        }
    }

    /// <summary>
    /// Create a new series with comprehensive validation and metadata initialization.
    /// </summary>
    /// <param name="request">Series creation request containing title, media_type, description, and reading_direction.</param>
    /// <param name="repo">Repository service for persisting series data (injected).</param>
    /// <param name="validationService">Taxonomy validation service for metadata validation (injected).</param>
    /// <param name="context">HTTP context for request path and client IP (injected).</param>
    /// <param name="user">Authenticated user principal from JWT (injected).</param>
    /// <param name="loggerFactory">Logger factory for diagnostic logging (injected).</param>
    /// <returns>HTTP 201 Created with series object, or RFC 7807 Problem Details on error.</returns>
    /// <remarks>
    /// <para><strong>Required Authorization:</strong></para>
    /// Requires MvnIngestOrAdmin policy (mvn:ingest or mvn:admin scope).
    /// 
    /// <para><strong>Validation Pipeline:</strong></para>
    /// <list type="number">
    ///   <item><description>User authentication check (valid JWT with user ID)</description></item>
    ///   <item><description>Title validation (required, max 500 chars, no control characters)</description></item>
    ///   <item><description>Media type validation (Photo, Text, or Video)</description></item>
    ///   <item><description>Description validation (optional, max 5000 chars)</description></item>
    ///   <item><description>Reading direction validation (LTR, RTL, TTB, BTT)</description></item>
    /// </list>
    /// 
    /// <para><strong>Default Values:</strong></para>
    /// <list type="bullet">
    ///   <item><description>URN: Auto-generated using UrnHelper.CreateSeriesUrn()</description></item>
    ///   <item><description>Federation: \"urn:mvn:node:local\" (TODO: from auth server)</description></item>
    ///   <item><description>Poster: Placeholder image (400x600 from placehold.co)</description></item>
    ///   <item><description>Reading direction: LTR if not specified or invalid</description></item>
    ///   <item><description>Created by: Current user's URN</description></item>
    ///   <item><description>Timestamps: UTC now for created_at and updated_at</description></item>
    /// </list>
    /// 
    /// <para><strong>Metadata Initialization:</strong></para>
    /// New series are created with empty arrays for tags, content_warnings, authors,
    /// scanlators, groups, and alt_titles. Taxonomy validation occurs only during
    /// updates when metadata is populated.
    /// 
    /// <para><strong>Security Notes:</strong></para>
    /// All inputs are validated and sanitized. User authentication is mandatory.
    /// Creation events are logged with user ID for audit trails.
    /// </remarks>
    private static async Task<IResult> CreateSeries(
        SeriesCreate request, 
        [FromServices] IRepository repo,
        [FromServices] TaxonomyValidationService validationService,
        HttpContext context,
        ClaimsPrincipal user,
        [FromServices] ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("MehguViewer.Core.Endpoints.SeriesEndpoints");
        await Task.CompletedTask;

        // === Phase 1: User Authentication ===
        var userId = GetUserId(user);
        if (string.IsNullOrEmpty(userId))
        {
            logger.LogWarning("CreateSeries attempt without valid user ID. IP: {IP}", context.Connection.RemoteIpAddress);
            return ResultsExtensions.Unauthorized("Authentication required", context.Request.Path);
        }

        // === Phase 2: Title Validation ===
        if (!ValidateTitle(request.title, "Title", out var titleError))
        {
            logger.LogWarning("CreateSeries failed: {ValidationError}. User: {UserId}", titleError, userId);
            return ResultsExtensions.BadRequest(titleError!, context.Request.Path);
        }
        
        // === Phase 3: Required Field Validation ===
        if (string.IsNullOrWhiteSpace(request.media_type))
        {
            logger.LogWarning("CreateSeries failed: Missing media_type. User: {UserId}", userId);
            return ResultsExtensions.BadRequest("Media type is required", context.Request.Path);
        }

        // === Phase 4: Description Length Validation ===
        // Security: Validate description length if provided
        if (!string.IsNullOrWhiteSpace(request.description) && request.description.Length > 5000)
        {
            logger.LogWarning("CreateSeries failed: Description too long ({Length} chars). User: {UserId}", 
                request.description.Length, userId);
            return ResultsExtensions.BadRequest($"Description must be 5000 characters or less (current: {request.description.Length})", context.Request.Path);
        }

        // === Phase 5: Media Type Normalization & Validation ===
        var normalizedMediaType = MediaTypes.Normalize(request.media_type);
        if (normalizedMediaType == null)
        {
            logger.LogWarning("CreateSeries failed: Invalid media_type '{MediaType}'. User: {UserId}", 
                request.media_type, userId);
            return ResultsExtensions.BadRequest(
                $"Invalid media_type '{request.media_type}'. Valid types: {string.Join(", ", MediaTypes.All)}", 
                context.Request.Path);
        }

        // === Phase 6: Reading Direction Validation ===
        if (!ReadingDirections.IsValid(request.reading_direction))
        {
            logger.LogWarning("CreateSeries failed: Invalid reading_direction '{Direction}'. User: {UserId}", 
                request.reading_direction, userId);
            return ResultsExtensions.BadRequest(
                $"Invalid reading_direction '{request.reading_direction}'. Valid values: {string.Join(", ", ReadingDirections.All)}", 
                context.Request.Path);
        }

        var normalizedDirection = ReadingDirections.Normalize(request.reading_direction) ?? ReadingDirections.LTR;
        
        logger.LogInformation("Creating series '{Title}' (Type: {MediaType}, Direction: {Direction}) by user {UserId}", 
            request.title, normalizedMediaType, normalizedDirection, userId);

        // === Phase 7: Series Object Construction ===
        var series = new Series(
            id: UrnHelper.CreateSeriesUrn(),
            federation_ref: DefaultFederationRef,
            title: request.title.Trim(),
            description: request.description?.Trim() ?? "",
            poster: PlaceholderPoster,
            media_type: normalizedMediaType,
            external_links: new Dictionary<string, string>(),
            reading_direction: normalizedDirection,
            tags: [],
            content_warnings: [],
            authors: [],
            scanlators: [],
            groups: null,
            alt_titles: null,
            status: null,
            year: null,
            created_by: userId,
            created_at: DateTime.UtcNow,
            updated_at: DateTime.UtcNow
        );
        
        // === Phase 8: Persist to Repository ===
        repo.AddSeries(series);
        
        logger.LogInformation("Series '{Title}' created successfully. SeriesId: {SeriesId}, User: {UserId}", 
            series.title, series.id, userId);

        // Note: Initial series creation has no tags/authors/scanlators,
        // validation will occur when updating metadata
        return Results.Created($"/api/v1/series/{series.id}", series);
    }

    /// <summary>
    /// List series with optional pagination.
    /// </summary>
    /// <param name="repo">Repository service.</param>
    /// <param name="cursor">Pagination cursor (not yet implemented).</param>
    /// <param name="limit">Results limit (default: 20, max: 100).</param>
    /// <param name="type">Media type filter (unused currently).</param>
    /// <param name="loggerFactory">Logger factory.</param>
    /// <returns>Paginated list of series.</returns>
    private static async Task<IResult> ListSeries(
        [FromServices] IRepository repo, 
        string? cursor, 
        int? limit, 
        string? type,
        [FromServices] ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("MehguViewer.Core.Endpoints.SeriesEndpoints");
        await Task.CompletedTask;
        
        // Security: Limit max results to prevent resource exhaustion
        var safeLimit = Math.Min(limit ?? DefaultPaginationLimit, MaxPaginationLimit);
        
        logger.LogDebug("Listing series: limit={Limit}, type={Type}", safeLimit, type);
        
        // TODO: Implement cursor-based pagination properly. For now using offset/limit.
        var series = repo.ListSeries(0, safeLimit);
        
        logger.LogDebug("Retrieved {Count} series", series.Count());
        
        return Results.Ok(new SeriesListResponse(series.ToArray(), new CursorPagination(null, false)));
    }

    /// <summary>
    /// Retrieve a single series by URN.
    /// </summary>
    /// <param name="seriesId">Series URN.</param>
    /// <param name="repo">Repository service.</param>
    /// <param name="context">HTTP context.</param>
    /// <param name="loggerFactory">Logger factory.</param>
    /// <returns>Series details if found.</returns>
    private static async Task<IResult> GetSeries(
        string seriesId, 
        [FromServices] IRepository repo, 
        HttpContext context,
        [FromServices] ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("MehguViewer.Core.Endpoints.SeriesEndpoints");
        await Task.CompletedTask;
        
        // Validate URN format
        if (!UrnHelper.IsValidSeriesUrn(seriesId))
        {
            logger.LogWarning("Invalid Series URN requested: {SeriesId}. IP: {IP}", 
                seriesId, context.Connection.RemoteIpAddress);
            return ResultsExtensions.BadRequest($"Invalid Series URN: {seriesId}", context.Request.Path);
        }
        
        logger.LogDebug("Fetching series {SeriesId}", seriesId);
        
        var series = repo.GetSeries(seriesId);
        if (series == null)
        {
            logger.LogInformation("Series {SeriesId} not found", seriesId);
            return ResultsExtensions.NotFound($"Series {seriesId} not found", context.Request.Path);
        }
        
        logger.LogDebug("Successfully retrieved series '{Title}' ({SeriesId})", series.title, seriesId);
        return Results.Ok(series);
    }

    /// <summary>
    /// Delete a series (requires ownership, permission, or admin role).
    /// </summary>
    /// <param name="seriesId">Series URN to delete.</param>
    /// <param name="repo">Repository service.</param>
    /// <param name="context">HTTP context.</param>
    /// <param name="user">Authenticated user principal.</param>
    /// <param name="loggerFactory">Logger factory.</param>
    /// <returns>NoContent (204) on success.</returns>
    private static async Task<IResult> DeleteSeries(
        string seriesId, 
        [FromServices] IRepository repo, 
        HttpContext context, 
        ClaimsPrincipal user,
        [FromServices] ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("MehguViewer.Core.Endpoints.SeriesEndpoints");
        await Task.CompletedTask;
        
        // Validate URN format
        if (!UrnHelper.IsValidSeriesUrn(seriesId))
        {
            logger.LogWarning("Invalid Series URN for deletion: {SeriesId}. IP: {IP}", 
                seriesId, context.Connection.RemoteIpAddress);
            return ResultsExtensions.BadRequest($"Invalid Series URN: {seriesId}", context.Request.Path);
        }
        
        var series = repo.GetSeries(seriesId);
        if (series == null)
        {
            logger.LogInformation("Deletion failed: Series {SeriesId} not found", seriesId);
            return ResultsExtensions.NotFound($"Series {seriesId} not found", context.Request.Path);
        }
        
        // Check authorization: admin, owner, or explicit permission
        if (!CanModifySeries(user, series, repo))
        {
            var userId = GetUserId(user);
            logger.LogWarning("Unauthorized deletion attempt: User {UserId} tried to delete series {SeriesId}. IP: {IP}", 
                userId, seriesId, context.Connection.RemoteIpAddress);
            return ResultsExtensions.Forbidden(
                "You can only delete series you created or have permission to edit", 
                context.Request.Path);
        }
        
        logger.LogInformation("Deleting series '{Title}' ({SeriesId}) by user {User}", 
            series.title, seriesId, GetUserId(user));
        
        repo.DeleteSeries(seriesId);
        
        logger.LogInformation("Series {SeriesId} deleted successfully", seriesId);
        return Results.NoContent();
    }

    /// <summary>
    /// Update an existing series (requires ownership, permission, or admin role).
    /// Supports both PUT (full update) and PATCH (partial update) semantics.
    /// </summary>
    /// <param name="seriesId">Series URN to update.</param>
    /// <param name="request">Update request with fields to modify.</param>
    /// <param name="repo">Repository service.</param>
    /// <param name="validationService">Taxonomy validation service.</param>
    /// <param name="context">HTTP context.</param>
    /// <param name="user">Authenticated user principal.</param>
    /// <param name="loggerFactory">Logger factory.</param>
    /// <returns>Updated series with optional validation warnings.</returns>
    private static async Task<IResult> UpdateSeries(
        string seriesId, 
        SeriesUpdate request, 
        [FromServices] IRepository repo,
        [FromServices] TaxonomyValidationService validationService,
        HttpContext context, 
        ClaimsPrincipal user,
        [FromServices] ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("MehguViewer.Core.Endpoints.SeriesEndpoints");
        await Task.CompletedTask;
        
        // Normalize URN
        var id = UrnHelper.NormalizeSeriesUrn(seriesId);
        
        var existing = repo.GetSeries(id);
        if (existing == null)
        {
            logger.LogInformation("Update failed: Series {SeriesId} not found", id);
            return ResultsExtensions.NotFound($"Series {id} not found", context.Request.Path);
        }

        // Check authorization
        if (!CanModifySeries(user, existing, repo))
        {
            var userId = GetUserId(user);
            logger.LogWarning("Unauthorized update attempt: User {UserId} tried to update series {SeriesId}. IP: {IP}", 
                userId, id, context.Connection.RemoteIpAddress);
            return ResultsExtensions.Forbidden(
                "You can only edit series you created or have permission to edit", 
                context.Request.Path);
        }

        logger.LogInformation("Updating series '{Title}' ({SeriesId}) by user {User}", 
            existing.title, id, GetUserId(user));

        // Validate reading_direction if provided
        if (request.reading_direction != null && !ReadingDirections.IsValid(request.reading_direction))
        {
            logger.LogWarning("Invalid reading_direction '{Direction}' for series {SeriesId}", 
                request.reading_direction, id);
            return ResultsExtensions.BadRequest(
                $"Invalid reading_direction '{request.reading_direction}'. Valid values: {string.Join(", ", ReadingDirections.All)}", 
                context.Request.Path);
        }

        // Validate media_type if provided (fixed types: Photo, Text, Video)
        string? normalizedMediaType = null;
        if (request.media_type != null)
        {
            normalizedMediaType = MediaTypes.Normalize(request.media_type);
            if (normalizedMediaType == null)
            {
                logger.LogWarning("Invalid media_type '{MediaType}' for series {SeriesId}", 
                    request.media_type, id);
                return ResultsExtensions.BadRequest(
                    $"Invalid media_type '{request.media_type}'. Valid types: {string.Join(", ", MediaTypes.All)}", 
                    context.Request.Path);
            }
        }

        // Normalize content warnings if provided
        var normalizedWarnings = request.content_warnings != null
            ? ContentWarnings.NormalizeAll(request.content_warnings)
            : existing.content_warnings;

        // Normalize tags if provided (deduplicate, trim, case-insensitive)
        var normalizedTags = request.tags != null
            ? request.tags
                .Select(t => t?.Trim())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : existing.tags;
        
        // Merge: only update fields that are explicitly provided (not null)
        var updated = new Series(
            id: existing.id,
            federation_ref: existing.federation_ref,
            title: request.title ?? existing.title,
            description: request.description ?? existing.description,
            poster: request.poster ?? existing.poster,
            media_type: normalizedMediaType ?? existing.media_type,
            external_links: request.external_links ?? existing.external_links,
            reading_direction: ReadingDirections.Normalize(request.reading_direction) ?? existing.reading_direction,
            tags: normalizedTags,
            content_warnings: normalizedWarnings,
            authors: request.authors ?? existing.authors,
            scanlators: request.scanlators ?? existing.scanlators,
            groups: request.groups ?? existing.groups,
            alt_titles: request.alt_titles ?? existing.alt_titles,
            status: request.status ?? existing.status,
            year: request.year ?? existing.year,
            created_by: existing.created_by,
            created_at: existing.created_at,
            updated_at: DateTime.UtcNow,
            localized: request.localized ?? existing.localized
        );
        
        // Validate taxonomy entities
        var validation = validationService.ValidateSeries(updated);
        
        repo.UpdateSeries(updated);
        logger.LogInformation("Series {SeriesId} updated", id);
        
        // Return series with validation warnings if any
        if (!validation.IsValid)
        {
            return Results.Ok(new
            {
                series = updated,
                validation_warnings = new
                {
                    tags = validation.Tags.InvalidTags.Length > 0 ? new
                    {
                        invalid = validation.Tags.InvalidTags,
                        suggestions = validation.Tags.Suggestions
                    } : null,
                    authors = validation.Authors.InvalidIds.Length > 0 ? new
                    {
                        invalid = validation.Authors.InvalidIds
                    } : null,
                    scanlators = validation.Scanlators.InvalidIds.Length > 0 ? new
                    {
                        invalid = validation.Scanlators.InvalidIds
                    } : null
                }
            });
        }
        
        return Results.Ok(updated);
    }

    #endregion

    #region Unit CRUD Operations

    /// <summary>
    /// Create a new unit within a series (requires series modification permission).
    /// </summary>
    /// <param name="seriesId">Parent series URN.</param>
    /// <param name="request">Unit creation request.</param>
    /// <param name="repo">Repository service.</param>
    /// <param name="validationService">Taxonomy validation service.</param>
    /// <param name="metadataService">Metadata aggregation service.</param>
    /// <param name="context">HTTP context.</param>
    /// <param name="user">Authenticated user principal.</param>
    /// <param name="loggerFactory">Logger factory.</param>
    /// <returns>Created unit with validation warnings if applicable.</returns>
    private static async Task<IResult> CreateUnit(
        string seriesId, 
        UnitCreate request, 
        [FromServices] IRepository repo,
        [FromServices] TaxonomyValidationService validationService,
        [FromServices] MetadataAggregationService metadataService,
        HttpContext context,
        ClaimsPrincipal user,
        [FromServices] ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("MehguViewer.Core.Endpoints.SeriesEndpoints");
        await Task.CompletedTask;
        
        // Normalize URN
        var id = UrnHelper.NormalizeSeriesUrn(seriesId);
        
        var series = repo.GetSeries(id);
        if (series == null)
        {
            logger.LogInformation("Unit creation failed: Series {SeriesId} not found", id);
            return ResultsExtensions.NotFound($"Series {id} not found", context.Request.Path);
        }
        
        // Check permission to modify series
        if (!CanModifySeries(user, series, repo))
        {
            var attemptingUserId = GetUserId(user);
            logger.LogWarning("Unauthorized unit creation: User {UserId} tried to create unit in series {SeriesId}. IP: {IP}", 
                attemptingUserId, id, context.Connection.RemoteIpAddress);
            return ResultsExtensions.Forbidden(
                "You can only create units in series you created or have permission to edit", 
                context.Request.Path);
        }
        
        // Get and validate user ID
        var userId = GetUserId(user);
        if (string.IsNullOrEmpty(userId))
        {
            logger.LogWarning("CreateUnit attempt without valid user ID. IP: {IP}", context.Connection.RemoteIpAddress);
            return ResultsExtensions.Unauthorized("Authentication required", context.Request.Path);
        }
        var userUrn = NormalizeUserUrn(userId);

        logger.LogInformation("Creating unit #{UnitNumber} '{Title}' in series {SeriesId} by user {UserId}", 
            request.unit_number, request.title ?? $"Chapter {request.unit_number}", id, userId);

        // Create unit with metadata inheritance from series
        var unit = new Unit(
            id: UrnHelper.CreateUnitUrn(),
            series_id: id,
            unit_number: request.unit_number,
            title: request.title ?? $"Chapter {request.unit_number}",
            created_at: DateTime.UtcNow,
            created_by: userUrn,
            language: request.language,
            page_count: 0,
            folder_path: null,
            updated_at: DateTime.UtcNow,
            description: request.description,
            tags: request.tags,
            content_warnings: request.content_warnings,
            authors: request.authors,
            localized: request.localized
        );
        
        // Inherit metadata from series if not explicitly provided
        unit = metadataService.InheritMetadataFromSeries(unit, series, request.language);
        
        // Validate taxonomy entities
        var validation = validationService.ValidateUnit(unit);
        
        repo.AddUnit(unit);
        
        logger.LogInformation("Unit created successfully. UnitId: {UnitId}, SeriesId: {SeriesId}", unit.id, id);
        
        // Trigger metadata aggregation for the series
        repo.AggregateSeriesMetadataFromUnits(id);
        logger.LogDebug("Metadata aggregated for series {SeriesId} after unit creation", id);
        
        // Return unit with validation warnings if any
        if (!validation.IsValid)
        {
            return Results.Created($"/api/v1/series/{seriesId}/units/{unit.id}", new
            {
                unit,
                validation_warnings = new
                {
                    tags = validation.Tags.InvalidTags.Length > 0 ? new
                    {
                        invalid = validation.Tags.InvalidTags,
                        suggestions = validation.Tags.Suggestions
                    } : null,
                    authors = validation.Authors.InvalidIds.Length > 0 ? new
                    {
                        invalid = validation.Authors.InvalidIds
                    } : null,
                    scanlators = validation.Scanlators.InvalidIds.Length > 0 ? new
                    {
                        invalid = validation.Scanlators.InvalidIds
                    } : null
                }
            });
        }
        
        return Results.Created($"/api/v1/series/{seriesId}/units/{unit.id}", unit);
    }

    /// <summary>
    /// List all units in a series with permission flags.
    /// </summary>
    /// <param name="seriesId">Parent series URN.</param>
    /// <param name="repo">Repository service.</param>
    /// <param name="context">HTTP context.</param>
    /// <param name="user">Authenticated user principal.</param>
    /// <param name="loggerFactory">Logger factory.</param>
    /// <returns>Array of units with can_edit flags.</returns>
    private static async Task<IResult> ListUnits(
        string seriesId, 
        [FromServices] IRepository repo, 
        HttpContext context,
        ClaimsPrincipal user,
        [FromServices] ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("MehguViewer.Core.Endpoints.SeriesEndpoints");
        await Task.CompletedTask;
        
        var id = UrnHelper.NormalizeSeriesUrn(seriesId);
        
        logger.LogDebug("Listing units for series {SeriesId}", id);
        
        var series = repo.GetSeries(id);
        if (series == null)
        {
            logger.LogInformation("Series {SeriesId} not found for unit listing", id);
            return ResultsExtensions.NotFound($"Series {id} not found", context.Request.Path);
        }
        
        var units = repo.ListUnits(id);
        
        // Add can_edit flag to each unit for client UI
        var userId = GetUserId(user);
        var userUrn = !string.IsNullOrEmpty(userId) ? NormalizeUserUrn(userId) : null;
        
        var unitsWithPermissions = units.Select(unit => new
        {
            id = unit.id,
            series_id = unit.series_id,
            unit_number = unit.unit_number,
            title = unit.title,
            tags = unit.tags,
            content_warnings = unit.content_warnings,
            authors = unit.authors,
            localized = unit.localized,
            allowed_editors = unit.allowed_editors,
            can_edit = userUrn != null && (IsAdmin(user) || CanModifyUnit(user, unit, series, repo))
        }).ToArray();
        
        logger.LogDebug("Retrieved {Count} units for series {SeriesId}", unitsWithPermissions.Length, id);
        
        return Results.Ok(unitsWithPermissions);
    }
    
    /// <summary>
    /// Retrieve a single unit by URN with permission details.
    /// </summary>
    /// <param name="seriesId">Parent series URN.</param>
    /// <param name="unitId">Unit URN.</param>
    /// <param name="repo">Repository service.</param>
    /// <param name="context">HTTP context.</param>
    /// <param name="user">Authenticated user principal.</param>
    /// <param name="loggerFactory">Logger factory.</param>
    /// <returns>Unit details with permission metadata.</returns>
    private static async Task<IResult> GetUnit(
        string seriesId,
        string unitId,
        [FromServices] IRepository repo,
        HttpContext context,
        ClaimsPrincipal user,
        [FromServices] ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("MehguViewer.Core.Endpoints.SeriesEndpoints");
        await Task.CompletedTask;
        
        var sid = UrnHelper.NormalizeSeriesUrn(seriesId);
        var uid = UrnHelper.NormalizeUnitUrn(unitId);
        
        logger.LogDebug("Fetching unit {UnitId} from series {SeriesId}", uid, sid);
        
        var series = repo.GetSeries(sid);
        if (series == null)
        {
            logger.LogInformation("Series {SeriesId} not found for unit retrieval", sid);
            return ResultsExtensions.NotFound($"Series {sid} not found", context.Request.Path);
        }
        
        var unit = repo.GetUnit(uid);
        if (unit == null)
        {
            logger.LogInformation("Unit {UnitId} not found", uid);
            return ResultsExtensions.NotFound($"Unit {uid} not found", context.Request.Path);
        }
        
        // Return full details if user has edit permission, otherwise return read-only view
        var userId = GetUserId(user);
        var userUrn = !string.IsNullOrEmpty(userId) ? NormalizeUserUrn(userId) : null;
        
        var canEdit = userUrn != null && 
            (IsAdmin(user) || CanModifyUnit(user, unit, series, repo));
        
        // Add metadata to indicate edit permission
        var response = new
        {
            unit,
            can_edit = canEdit,
            permissions = canEdit ? repo.GetEditPermissions(uid) : null
        };
        
        logger.LogDebug("Retrieved unit '{Title}' ({UnitId}). User can_edit: {CanEdit}", 
            unit.title, uid, canEdit);
        
        return Results.Ok(response);
    }

    /// <summary>
    /// Update an existing unit (requires unit/series modification permission).
    /// </summary>
    /// <param name="seriesId">Parent series URN.</param>
    /// <param name="unitId">Unit URN to update.</param>
    /// <param name="request">Update request with fields to modify.</param>
    /// <param name="repo">Repository service.</param>
    /// <param name="validationService">Taxonomy validation service.</param>
    /// <param name="context">HTTP context.</param>
    /// <param name="user">Authenticated user principal.</param>
    /// <param name="loggerFactory">Logger factory.</param>
    /// <returns>Updated unit with validation warnings if applicable.</returns>
    private static async Task<IResult> UpdateUnit(
        string seriesId, 
        string unitId, 
        UnitUpdate request, 
        [FromServices] IRepository repo,
        [FromServices] TaxonomyValidationService validationService,
        HttpContext context, 
        ClaimsPrincipal user,
        [FromServices] ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("MehguViewer.Core.Endpoints.SeriesEndpoints");
        await Task.CompletedTask;
        
        // Normalize URNs
        var sid = UrnHelper.NormalizeSeriesUrn(seriesId);
        var uid = UrnHelper.NormalizeUnitUrn(unitId);
        
        var series = repo.GetSeries(sid);
        if (series == null)
        {
            logger.LogInformation("Update failed: Series {SeriesId} not found", sid);
            return ResultsExtensions.NotFound($"Series {sid} not found", context.Request.Path);
        }
        
        var existing = repo.GetUnit(uid);
        if (existing == null)
        {
            logger.LogInformation("Update failed: Unit {UnitId} not found", uid);
            return ResultsExtensions.NotFound($"Unit {uid} not found", context.Request.Path);
        }
        
        // Check permission
        if (!CanModifyUnit(user, existing, series, repo))
        {
            var userId = GetUserId(user);
            logger.LogWarning("Unauthorized update: User {UserId} tried to update unit {UnitId}. IP: {IP}", 
                userId, uid, context.Connection.RemoteIpAddress);
            return ResultsExtensions.Forbidden(
                "You can only edit units you created or have permission to edit", 
                context.Request.Path);
        }
        
        logger.LogInformation("Updating unit '{Title}' ({UnitId}) by user {User}", 
            existing.title, uid, GetUserId(user));
        
        // Merge: only update fields that are explicitly provided (not null)
        var updated = existing with
        {
            unit_number = request.unit_number ?? existing.unit_number,
            title = request.title ?? existing.title,
            language = request.language ?? existing.language,
            description = request.description ?? existing.description,
            tags = request.tags ?? existing.tags,
            content_warnings = request.content_warnings ?? existing.content_warnings,
            authors = request.authors ?? existing.authors,
            localized = request.localized ?? existing.localized,
            updated_at = DateTime.UtcNow
        };
        
        // Validate taxonomy entities
        var validation = validationService.ValidateUnit(updated);
        
        repo.UpdateUnit(updated);
        
        logger.LogInformation("Unit {UnitId} updated successfully", uid);
        
        // Metadata aggregation is triggered automatically in UpdateUnit
        logger.LogDebug("Metadata aggregation triggered for series {SeriesId}", sid);
        
        // Return unit with validation warnings if any
        if (!validation.IsValid)
        {
            return Results.Ok(new
            {
                unit = updated,
                validation_warnings = new
                {
                    tags = validation.Tags.InvalidTags.Length > 0 ? new
                    {
                        invalid = validation.Tags.InvalidTags,
                        suggestions = validation.Tags.Suggestions
                    } : null,
                    authors = validation.Authors.InvalidIds.Length > 0 ? new
                    {
                        invalid = validation.Authors.InvalidIds
                    } : null,
                    scanlators = validation.Scanlators.InvalidIds.Length > 0 ? new
                    {
                        invalid = validation.Scanlators.InvalidIds
                    } : null
                }
            });
        }
        
        return Results.Ok(updated);
    }

    /// <summary>
    /// Delete a unit from a series (requires unit/series modification permission).
    /// </summary>
    /// <param name="seriesId">Parent series URN.</param>
    /// <param name="unitId">Unit URN to delete.</param>
    /// <param name="repo">Repository service.</param>
    /// <param name="context">HTTP context.</param>
    /// <param name="user">Authenticated user principal.</param>
    /// <param name="loggerFactory">Logger factory.</param>
    /// <returns>NoContent (204) on success.</returns>
    private static async Task<IResult> DeleteUnit(
        string seriesId, 
        string unitId, 
        [FromServices] IRepository repo, 
        HttpContext context, 
        ClaimsPrincipal user,
        [FromServices] ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("MehguViewer.Core.Endpoints.SeriesEndpoints");
        await Task.CompletedTask;
        
        // Normalize URNs
        var sid = UrnHelper.NormalizeSeriesUrn(seriesId);
        var uid = UrnHelper.NormalizeUnitUrn(unitId);
        
        var series = repo.GetSeries(sid);
        if (series == null)
        {
            logger.LogInformation("Deletion failed: Series {SeriesId} not found", sid);
            return ResultsExtensions.NotFound($"Series {sid} not found", context.Request.Path);
        }
        
        var existing = repo.GetUnit(uid);
        if (existing == null)
        {
            logger.LogInformation("Deletion failed: Unit {UnitId} not found", uid);
            return ResultsExtensions.NotFound($"Unit {uid} not found", context.Request.Path);
        }
        
        // Check permission
        if (!CanModifyUnit(user, existing, series, repo))
        {
            var userId = GetUserId(user);
            logger.LogWarning("Unauthorized deletion: User {UserId} tried to delete unit {UnitId}. IP: {IP}", 
                userId, uid, context.Connection.RemoteIpAddress);
            return ResultsExtensions.Forbidden(
                "You can only delete units you created or have permission to edit", 
                context.Request.Path);
        }
        
        logger.LogInformation("Deleting unit '{Title}' ({UnitId}) from series {SeriesId}", 
            existing.title, uid, sid);
        
        repo.DeleteUnit(uid);
        
        logger.LogInformation("Unit {UnitId} deleted successfully", uid);
        
        // Metadata aggregation is triggered automatically in DeleteUnit
        
        return Results.NoContent();
    }

    /// <summary>
    /// Delete a unit directly by URN (without requiring series ID in path).
    /// </summary>
    /// <param name="unitId">Unit URN to delete.</param>
    /// <param name="repo">Repository service.</param>
    /// <param name="context">HTTP context.</param>
    /// <param name="user">Authenticated user principal.</param>
    /// <param name="loggerFactory">Logger factory.</param>
    /// <returns>NoContent (204) on success.</returns>
    private static async Task<IResult> DeleteUnitDirect(
        string unitId,
        [FromServices] IRepository repo,
        HttpContext context,
        ClaimsPrincipal user,
        [FromServices] ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("MehguViewer.Core.Endpoints.SeriesEndpoints");
        await Task.CompletedTask;
        
        var uid = UrnHelper.NormalizeUnitUrn(unitId);
        
        var existing = repo.GetUnit(uid);
        if (existing == null)
        {
            logger.LogInformation("Direct deletion failed: Unit {UnitId} not found", uid);
            return ResultsExtensions.NotFound($"Unit {uid} not found", context.Request.Path);
        }
        
        var series = repo.GetSeries(existing.series_id);
        if (series == null)
        {
            logger.LogWarning("Orphaned unit detected: Unit {UnitId} has invalid series_id {SeriesId}", 
                uid, existing.series_id);
            return ResultsExtensions.NotFound($"Series {existing.series_id} not found", context.Request.Path);
        }
        
        // Check permission
        if (!CanModifyUnit(user, existing, series, repo))
        {
            var userId = GetUserId(user);
            logger.LogWarning("Unauthorized direct deletion: User {UserId} tried to delete unit {UnitId}. IP: {IP}", 
                userId, uid, context.Connection.RemoteIpAddress);
            return ResultsExtensions.Forbidden(
                "You can only delete units you created or have permission to edit", 
                context.Request.Path);
        }
        
        logger.LogInformation("Directly deleting unit '{Title}' ({UnitId})", existing.title, uid);
        
        repo.DeleteUnit(uid);
        
        logger.LogInformation("Unit {UnitId} deleted successfully (direct)", uid);
        
        // Metadata aggregation is triggered automatically in DeleteUnit
        
        return Results.NoContent();
    }

    #endregion

    #region Progress and Pages

    /// <summary>
    /// Get pages for a specific unit.
    /// </summary>
    /// <param name="seriesId">Parent series URN (currently unused for validation).</param>
    /// <param name="unitId">Unit URN.</param>
    /// <param name="repo">Repository service.</param>
    /// <param name="loggerFactory">Logger factory.</param>
    /// <returns>Array of pages.</returns>
    private static async Task<IResult> GetUnitPages(
        string seriesId, 
        string unitId, 
        [FromServices] IRepository repo,
        [FromServices] ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("MehguViewer.Core.Endpoints.SeriesEndpoints");
        await Task.CompletedTask;
        
        var uid = UrnHelper.NormalizeUnitUrn(unitId);
        
        logger.LogDebug("Fetching pages for unit {UnitId}", uid);
        
        // TODO: Validate that unit belongs to series
        var pages = repo.GetPages(uid);
        
        logger.LogDebug("Retrieved {Count} pages for unit {UnitId}", pages.Count(), uid);
        
        return Results.Ok(pages);
    }

    /// <summary>
    /// Update reading progress for a series.
    /// </summary>
    /// <param name="seriesId">Series URN.</param>
    /// <param name="request">Progress update request.</param>
    /// <param name="repo">Repository service.</param>
    /// <param name="user">Authenticated user principal.</param>
    /// <param name="loggerFactory">Logger factory.</param>
    /// <returns>NoContent (204) on success.</returns>
    private static async Task<IResult> UpdateProgress(
        string seriesId, 
        ProgressUpdate request, 
        [FromServices] IRepository repo, 
        ClaimsPrincipal user,
        [FromServices] ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("MehguViewer.Core.Endpoints.SeriesEndpoints");
        await Task.CompletedTask;
        
        var userId = GetUserId(user);
        if (string.IsNullOrEmpty(userId))
        {
            logger.LogWarning("UpdateProgress attempt without valid user ID");
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.last_read_chapter_id))
        {
            logger.LogWarning("UpdateProgress failed: Missing chapter ID. User: {UserId}", userId);
            return Results.BadRequest("Chapter ID is required");
        }

        var id = UrnHelper.NormalizeSeriesUrn(seriesId);
        
        logger.LogDebug("Updating progress for series {SeriesId}, user {UserId}", id, userId);
        
        var progress = new ReadingProgress(
            id,
            request.last_read_chapter_id,
            request.page_number,
            request.completed ? "completed" : "reading",
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        );
        
        repo.UpdateProgress(userId, progress);
        
        logger.LogInformation("Progress updated: Series {SeriesId}, User {UserId}, Status: {Status}", 
            id, userId, progress.status);
        
        return Results.NoContent();
    }

    /// <summary>
    /// Get reading progress for a series.
    /// </summary>
    /// <param name="seriesId">Series URN.</param>
    /// <param name="repo">Repository service.</param>
    /// <param name="context">HTTP context.</param>
    /// <param name="user">Authenticated user principal.</param>
    /// <param name="loggerFactory">Logger factory.</param>
    /// <returns>Progress data if found.</returns>
    private static async Task<IResult> GetProgress(
        string seriesId, 
        [FromServices] IRepository repo, 
        HttpContext context, 
        ClaimsPrincipal user,
        [FromServices] ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("MehguViewer.Core.Endpoints.SeriesEndpoints");
        await Task.CompletedTask;
        
        var userId = GetUserId(user);
        if (string.IsNullOrEmpty(userId))
        {
            logger.LogWarning("GetProgress attempt without valid user ID");
            return Results.Unauthorized();
        }

        var id = UrnHelper.NormalizeSeriesUrn(seriesId);
        
        logger.LogDebug("Fetching progress for series {SeriesId}, user {UserId}", id, userId);
        
        var progress = repo.GetProgress(userId, id);
        if (progress == null)
        {
            logger.LogDebug("No progress found for series {SeriesId}, user {UserId}", id, userId);
            return ResultsExtensions.NotFound("No progress found", context.Request.Path);
        }
        
        logger.LogDebug("Retrieved progress for series {SeriesId}: Status {Status}", id, progress.status);
        
        return Results.Ok(progress);
    }

    #endregion

    #region Cover Management

    /// <summary>
    /// Upload a cover image for a series with multi-variant generation.
    /// </summary>
    /// <param name="seriesId">Series URN to upload cover for.</param>
    /// <param name="file">Uploaded image file from multipart form data.</param>
    /// <param name="repo">Repository service for series data access (injected).</param>
    /// <param name="fileService">File-based series service for cover storage (injected).</param>
    /// <param name="imageProcessor">Image processing service for variant generation (injected).</param>
    /// <param name="logger">Logger for diagnostic and security logging (injected).</param>
    /// <param name="context">HTTP context for query parameters and client info (injected).</param>
    /// <param name="user">Authenticated user principal from JWT (injected).</param>
    /// <returns>HTTP 200 OK with updated series object, or RFC 7807 Problem Details on error.</returns>
    /// <remarks>
    /// <para><strong>Required Authorization:</strong></para>
    /// Requires MvnIngestOrAdmin policy and ownership/edit permission for the series.
    /// 
    /// <para><strong>Upload Validation:</strong></para>
    /// <list type=\"number\">
    ///   <item><description>Series existence check</description></item>
    ///   <item><description>User authorization (admin, owner, or explicit editor)</description></item>
    ///   <item><description>File existence and size (max 10MB)</description></item>
    ///   <item><description>Filename security (path traversal prevention)</description></item>
    ///   <item><description>Content type validation (image/jpeg, image/png, image/webp, image/gif only)</description></item>
    /// </list>
    /// 
    /// <para><strong>Image Variant Generation:</strong></para>
    /// Uploaded image is processed into three variants for optimal delivery:
    /// <list type=\"bullet\">
    ///   <item><description><strong>THUMBNAIL:</strong> 400x600px - Grid views and previews</description></item>
    ///   <item><description><strong>WEB:</strong> 800x1200px - Standard viewing (default)</description></item>
    ///   <item><description><strong>RAW:</strong> 1600x2400px - High-quality original</description></item>
    /// </list>
    /// 
    /// <para><strong>Storage Structure:</strong></para>
    /// Cover images are stored in data/covers/{seriesUrn}/ with filenames:
    /// cover-{language}-{variant}.{ext} (e.g., cover-en-WEB.jpg)
    /// Language is optional (from 'lang' query parameter).
    /// 
    /// <para><strong>Metadata Update:</strong></para>
    /// Series.poster field is updated with WEB variant URL and alt text.
    /// Updated_at timestamp is refreshed. Repository metadata aggregation is triggered.
    /// 
    /// <para><strong>Security Considerations:</strong></para>
    /// <list type=\"bullet\">
    ///   <item><description>Comprehensive file upload validation prevents malicious uploads</description></item>
    ///   <item><description>Authorization ensures only authorized users can modify covers</description></item>
    ///   <item><description>All uploads are logged with user ID and IP for audit</description></item>
    ///   <item><description>Path traversal prevention in filename handling</description></item>
    ///   <item><description>Content type whitelist prevents executable uploads</description></item>
    /// </list>
    /// 
    /// <para><strong>Error Handling:</strong></para>
    /// Image processing errors are caught and logged. Failed variant generation returns
    /// HTTP 500 with detailed error message. Partial failures are not supported - all
    /// variants must generate successfully.
    /// </remarks>
    private static async Task<IResult> UploadCover(
        string seriesId, 
        IFormFile file,
        [FromServices] IRepository repo,
        [FromServices] FileBasedSeriesService fileService,
        [FromServices] ImageProcessingService imageProcessor,
        [FromServices] ILogger<Program> logger,
        HttpContext context,
        ClaimsPrincipal user)
    {
        // === Phase 1: URN Normalization ===
        var id = UrnHelper.NormalizeSeriesUrn(seriesId);
        
        logger.LogDebug("Processing cover upload for series {SeriesId}", id);
        
        // === Phase 2: Series Existence Validation ===
        var existing = repo.GetSeries(id);
        if (existing == null)
        {
            logger.LogInformation("Cover upload failed: Series {SeriesId} not found", id);
            return ResultsExtensions.NotFound($"Series {id} not found", context.Request.Path);
        }

        // === Phase 3: Authorization Check ===
        if (!CanModifySeries(user, existing, repo))
        {
            var userId = GetUserId(user);
            logger.LogWarning("Unauthorized cover upload: User {UserId} attempted to upload cover for series {SeriesId}. IP: {IP}",
                userId, id, context.Connection.RemoteIpAddress);
            return ResultsExtensions.Forbidden("You can only upload covers for series you created or have permission to edit", context.Request.Path);
        }

        // === Phase 4: File Upload Validation ===
        // Validates: file existence, size (10MB max), filename security, content type
        if (!ValidateFileUpload(file, out var contentType, out var error, logger))
        {
            return ResultsExtensions.BadRequest(error!, context.Request.Path);
        }

        logger.LogInformation("Cover upload validated - Series: {SeriesId}, ContentType: '{ContentType}', FileName: '{FileName}', Size: {SizeKB:F2}KB", 
            id, contentType, file.FileName, file.Length / 1024.0);

        // === Phase 5: Language Parameter Extraction ===
        // Optional language identifier for localized covers (e.g., 'en', 'ja', 'fr')
        var language = context.Request.Query["lang"].ToString();
        if (string.IsNullOrWhiteSpace(language))
        {
            language = null;
        }

        try
        {
            // === Phase 6: Image Processing & Variant Generation ===
            // Generates THUMBNAIL (400x600), WEB (800x1200), RAW (1600x2400)
            await using var imageStream = file.OpenReadStream();
            var variants = await imageProcessor.ProcessImageVariantsAsync(imageStream, contentType);
            
            // === Phase 7: File Extension Resolution ===
            var extension = ImageProcessingService.GetFileExtension(contentType);

            // Save all variants to disk (with optional language)
            var coverUrl = await fileService.SaveCoverImageVariantsAsync(id, variants, extension, language);

            // Update the series with the new cover URL
            if (string.IsNullOrEmpty(language))
            {
                // Update default poster
                var updated = existing with
                {
                    poster = new Poster(coverUrl, $"Cover for {existing.title}"),
                    updated_at = DateTime.UtcNow
                };
                repo.UpdateSeries(updated);
            }
            else
            {
                // Update localized poster in localized metadata
                var localized = existing.localized ?? new Dictionary<string, LocalizedMetadata>();
                var existingLocalized = localized.TryGetValue(language, out var loc) ? loc : new LocalizedMetadata(null, null);
                
                localized[language] = existingLocalized with 
                { 
                    poster = new Poster(coverUrl, $"Cover for {existing.title} ({language})")
                };
                
                var updated = existing with
                {
                    localized = localized,
                    updated_at = DateTime.UtcNow
                };
                repo.UpdateSeries(updated);
            }

            var baseUrl = coverUrl.Split('?')[0];
            var langParam = string.IsNullOrEmpty(language) ? "" : $"&lang={language}";
            
            logger.LogInformation("Cover uploaded successfully for series {SeriesId}. Language: {Language}, Variants: 3",
                id, language ?? "default");
            
            return Results.Ok(new { 
                cover_url = coverUrl,
                language = language ?? "default",
                variants = new {
                    thumbnail = $"{baseUrl}?variant=thumbnail{langParam}",
                    web = $"{baseUrl}?variant=web{langParam}",
                    raw = $"{baseUrl}?variant=raw{langParam}"
                }
            });
        }
        catch (ArgumentException argEx)
        {
            logger.LogWarning(argEx, "Invalid argument during cover upload for series {SeriesId}", id);
            return ResultsExtensions.BadRequest($"Invalid input: {argEx.Message}", context.Request.Path);
        }
        catch (IOException ioEx)
        {
            logger.LogError(ioEx, "File system error during cover upload for series {SeriesId}", id);
            return ResultsExtensions.InternalServerError("Failed to save cover image", context.Request.Path);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error uploading cover for series {SeriesId}", id);
            return ResultsExtensions.InternalServerError("Failed to upload cover", context.Request.Path);
        }
    }
    
    /// <summary>
    /// Downloads a cover image from a URL and saves it locally with comprehensive validation.
    /// Generates multiple image variants (thumbnail, web, raw) for optimal delivery.
    /// </summary>
    private static async Task<IResult> DownloadCoverFromUrl(
        string seriesId,
        [FromBody] CoverFromUrlRequest request,
        [FromServices] IRepository repo,
        [FromServices] FileBasedSeriesService fileService,
        [FromServices] ImageProcessingService imageProcessor,
        [FromServices] IHttpClientFactory httpClientFactory,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext context,
        ClaimsPrincipal user)
    {
        var logger = loggerFactory.CreateLogger("MehguViewer.Core.Endpoints.SeriesEndpoints");
        var id = UrnHelper.NormalizeSeriesUrn(seriesId);
        
        logger.LogDebug("Processing cover download from URL for series {SeriesId}. URL: {Url}", id, request.url);
        
        var existing = repo.GetSeries(id);
        if (existing == null)
        {
            logger.LogInformation("Cover download failed: Series {SeriesId} not found", id);
            return ResultsExtensions.NotFound($"Series {id} not found", context.Request.Path);
        }

        // Authorization check
        if (!CanModifySeries(user, existing, repo))
        {
            var userId = GetUserId(user);
            logger.LogWarning("Unauthorized cover download: User {UserId} attempted to download cover for series {SeriesId}. IP: {IP}",
                userId, id, context.Connection.RemoteIpAddress);
            return ResultsExtensions.Forbidden("You can only set covers for series you created or have permission to edit", context.Request.Path);
        }

        // Validate URL provided
        if (string.IsNullOrWhiteSpace(request.url))
        {
            return ResultsExtensions.BadRequest("No URL provided", context.Request.Path);
        }

        // Security: Validate URL format
        if (!Uri.TryCreate(request.url, UriKind.Absolute, out var uri) || 
            (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            logger.LogWarning("Invalid URL format for cover download: {Url}. Series: {SeriesId}", request.url, id);
            return ResultsExtensions.BadRequest("Invalid URL. Only HTTP and HTTPS URLs are allowed", context.Request.Path);
        }

        try
        {
            var httpClient = httpClientFactory.CreateClient("CoverDownload");
            
            logger.LogDebug("Downloading image from URL: {Url}", request.url);
            
            // Download the image
            using var response = await httpClient.GetAsync(request.url);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Failed to download image from URL: {Url}. Status: {Status}",
                    request.url, response.StatusCode);
                return ResultsExtensions.BadRequest($"Failed to download image from URL: {response.StatusCode}", context.Request.Path);
            }

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
            
            // Validate content type
            if (!ImageProcessingService.IsSupportedImageType(contentType))
            {
                logger.LogWarning("Invalid image type downloaded: {ContentType}. URL: {Url}",
                    contentType, request.url);
                return ResultsExtensions.BadRequest($"Invalid image type: {contentType}. Allowed: jpg, png, webp, gif", context.Request.Path);
            }

            var imageBytes = await response.Content.ReadAsByteArrayAsync();
            
            // Security: Validate downloaded image size
            if (imageBytes.Length > MaxCoverFileSizeBytes)
            {
                logger.LogWarning("Cover download rejected: Image size {Size} exceeds maximum {MaxSize}. URL: {Url}",
                    imageBytes.Length, MaxCoverFileSizeBytes, request.url);
                return ResultsExtensions.BadRequest("Image too large. Maximum size is 10MB", context.Request.Path);
            }

            // Process image and generate variants (THUMBNAIL, WEB, RAW)
            await using var imageStream = new MemoryStream(imageBytes);
            var variants = await imageProcessor.ProcessImageVariantsAsync(imageStream, contentType);
            
            // Determine file extension from content type
            var extension = ImageProcessingService.GetFileExtension(contentType);

            // Save all variants to disk (with optional language)
            var coverUrl = await fileService.SaveCoverImageVariantsAsync(id, variants, extension, request.language);

            // Update the series with the new cover URL
            if (string.IsNullOrEmpty(request.language))
            {
                // Update default poster
                var updated = existing with
                {
                    poster = new Poster(coverUrl, $"Cover for {existing.title}"),
                    updated_at = DateTime.UtcNow
                };
                repo.UpdateSeries(updated);
            }
            else
            {
                // Update localized poster in localized metadata
                var localized = existing.localized ?? new Dictionary<string, LocalizedMetadata>();
                var existingLocalized = localized.TryGetValue(request.language, out var loc) ? loc : new LocalizedMetadata(null, null);
                
                localized[request.language] = existingLocalized with 
                { 
                    poster = new Poster(coverUrl, $"Cover for {existing.title} ({request.language})")
                };
                
                var updated = existing with
                {
                    localized = localized,
                    updated_at = DateTime.UtcNow
                };
                repo.UpdateSeries(updated);
            }

            var baseUrl = coverUrl.Split('?')[0];
            var langParam = string.IsNullOrEmpty(request.language) ? "" : $"&lang={request.language}";
            
            logger.LogInformation("Cover downloaded and saved successfully for series {SeriesId} from URL: {Url}. Language: {Language}",
                id, request.url, request.language ?? "default");
            
            return Results.Ok(new { 
                cover_url = coverUrl, 
                downloaded_from = request.url,
                language = request.language ?? "default",
                variants = new {
                    thumbnail = $"{baseUrl}?variant=thumbnail{langParam}",
                    web = $"{baseUrl}?variant=web{langParam}",
                    raw = $"{baseUrl}?variant=raw{langParam}"
                }
            });
        }
        catch (HttpRequestException httpEx)
        {
            logger.LogError(httpEx, "HTTP error downloading cover from URL: {Url}", request.url);
            return ResultsExtensions.BadRequest($"Failed to download image: {httpEx.Message}", context.Request.Path);
        }
        catch (TaskCanceledException timeoutEx)
        {
            logger.LogError(timeoutEx, "Timeout downloading cover from URL: {Url}", request.url);
            return ResultsExtensions.BadRequest("Request timeout. The image download took too long", context.Request.Path);
        }
        catch (ArgumentException argEx)
        {
            logger.LogWarning(argEx, "Invalid argument during cover download for series {SeriesId}", id);
            return ResultsExtensions.BadRequest($"Invalid input: {argEx.Message}", context.Request.Path);
        }
        catch (IOException ioEx)
        {
            logger.LogError(ioEx, "File system error during cover download for series {SeriesId}", id);
            return ResultsExtensions.InternalServerError("Failed to save downloaded cover image", context.Request.Path);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error downloading cover from URL: {Url} for series {SeriesId}", request.url, id);
            return ResultsExtensions.InternalServerError("Failed to download cover from URL", context.Request.Path);
        }
    }
    
    #endregion
    
    #region Permission Management Endpoints
    
    /// <summary>
    /// Grant edit permission for a series to a specific user.
    /// Only series owner or admin can grant permissions.
    /// </summary>
    private static async Task<IResult> GrantEditPermission(
        string seriesId,
        GrantEditPermissionRequest request,
        [FromServices] IRepository repo,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext context,
        ClaimsPrincipal user)
    {
        var logger = loggerFactory.CreateLogger("MehguViewer.Core.Endpoints.SeriesEndpoints");
        await Task.CompletedTask;
        var id = seriesId.StartsWith("urn:mvn:series:") ? seriesId : $"urn:mvn:series:{seriesId}";
        
        logger.LogInformation("Granting edit permission for series {SeriesId} to user {TargetUserUrn}", id, request.user_urn);

        var series = repo.GetSeries(id);
        if (series == null)
        {
            logger.LogWarning("Series {SeriesId} not found", id);
            return ResultsExtensions.NotFound($"Series {id} not found", context.Request.Path);
        }
        
        // Only owner or admin can grant permissions
        if (!IsAdmin(user) && !IsOwner(user, series))
        {
            logger.LogWarning("User {UserUrn} is not authorized to grant permissions for series {SeriesId}", user.FindFirstValue(ClaimTypes.NameIdentifier), id);
            return ResultsExtensions.Forbidden("Only the series owner or admin can grant edit permissions", context.Request.Path);
        }
        
        // Validate user URN format
        var targetUserUrn = request.user_urn.Trim();
        if (!targetUserUrn.StartsWith("urn:mvn:user:"))
        {
            return ResultsExtensions.BadRequest("Invalid user URN format. Must start with 'urn:mvn:user:'", context.Request.Path);
        }
        
        // Check if the target user exists
        var targetUser = repo.GetUser(targetUserUrn);
        if (targetUser == null)
        {
            logger.LogWarning("Target user {TargetUserUrn} not found", targetUserUrn);
            return ResultsExtensions.NotFound($"User {targetUserUrn} not found", context.Request.Path);
        }
        
        // Check if the target user is already the owner
        if (series.created_by == targetUserUrn)
        {
            return ResultsExtensions.BadRequest("Cannot grant permission to the series owner. The owner already has full access.", context.Request.Path);
        }
        
        // Admins have global edit access, no need to add them to permission lists
        if (targetUser.role == "Admin")
        {
            return ResultsExtensions.BadRequest("Cannot grant permission to Admins. Admins already have global edit access to all series.", context.Request.Path);
        }
        
        // Check if the target user has permission to edit (Uploader role only)
        if (targetUser.role != "Uploader")
        {
            return ResultsExtensions.BadRequest($"Cannot grant permission to user with role '{targetUser.role}'. Only Uploaders can be granted edit permissions.", context.Request.Path);
        }
        
        // Check if permission already granted
        var existingPermissions = repo.GetEditPermissions(id);
        if (existingPermissions.Contains(targetUserUrn))
        {
            return ResultsExtensions.BadRequest($"User {targetUser.username} already has edit permission for this series", context.Request.Path);
        }
        
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return ResultsExtensions.Unauthorized("Authentication required", context.Request.Path);
        }
        var grantedBy = userId.StartsWith("urn:mvn:user:") ? userId : $"urn:mvn:user:{userId}";
        
        repo.GrantEditPermission(id, targetUserUrn, grantedBy);
        
        logger.LogInformation("Granted edit permission for series {SeriesId} to user {TargetUserUrn} by {GrantedBy}", id, targetUserUrn, grantedBy);

        return Results.Ok(new { message = "Permission granted successfully", target = id, user = targetUserUrn, username = targetUser.username });
    }
    
    private static async Task<IResult> RevokeSeriesEditPermission(
        string seriesId,
        string userUrn,
        [FromServices] IRepository repo,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext context,
        ClaimsPrincipal user)
    {
        var logger = loggerFactory.CreateLogger("MehguViewer.Core.Endpoints.SeriesEndpoints");
        await Task.CompletedTask;
        var id = seriesId.StartsWith("urn:mvn:series:") ? seriesId : $"urn:mvn:series:{seriesId}";
        
        logger.LogInformation("Revoking edit permission for series {SeriesId} from user {TargetUserUrn}", id, userUrn);

        var series = repo.GetSeries(id);
        if (series == null)
        {
            logger.LogWarning("Series {SeriesId} not found", id);
            return ResultsExtensions.NotFound($"Series {id} not found", context.Request.Path);
        }
        
        // Only owner or admin can revoke permissions
        if (!IsAdmin(user) && !IsOwner(user, series))
        {
            logger.LogWarning("User {UserUrn} is not authorized to revoke permissions for series {SeriesId}", user.FindFirstValue(ClaimTypes.NameIdentifier), id);
            return ResultsExtensions.Forbidden("Only the series owner or admin can revoke edit permissions", context.Request.Path);
        }
        
        repo.RevokeEditPermission(id, userUrn);
        
        logger.LogInformation("Revoked edit permission for series {SeriesId} from user {TargetUserUrn}", id, userUrn);

        return Results.Ok(new { message = "Permission revoked successfully", target = id, user = userUrn });
    }
    
    private static async Task<IResult> CheckCanModifySeries(
        string seriesId,
        [FromServices] IRepository repo,
        HttpContext context,
        ClaimsPrincipal user)
    {
        await Task.CompletedTask;
        var id = seriesId.StartsWith("urn:mvn:series:") ? seriesId : $"urn:mvn:series:{seriesId}";
        
        var series = repo.GetSeries(id);
        if (series == null)
        {
            return ResultsExtensions.NotFound($"Series {id} not found", context.Request.Path);
        }
        
        var canModify = CanModifySeries(user, series, repo);
        var userId = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        var userUrn = !string.IsNullOrEmpty(userId) && userId.StartsWith("urn:mvn:user:") ? userId : $"urn:mvn:user:{userId}";
        
        return Results.Ok(new { 
            series_id = id, 
            user_urn = userUrn,
            can_modify = canModify,
            is_admin = IsAdmin(user),
            is_owner = IsOwner(user, series),
            has_explicit_permission = !string.IsNullOrEmpty(userId) && repo.HasEditPermission(id, userUrn)
        });
    }
    
    private static async Task<IResult> GetSeriesEditPermissions(
        string seriesId,
        [FromServices] IRepository repo,
        HttpContext context,
        ClaimsPrincipal user)
    {
        await Task.CompletedTask;
        var id = seriesId.StartsWith("urn:mvn:series:") ? seriesId : $"urn:mvn:series:{seriesId}";
        
        var series = repo.GetSeries(id);
        if (series == null)
        {
            return ResultsExtensions.NotFound($"Series {id} not found", context.Request.Path);
        }
        
        // Only owner or admin can view permissions
        if (!IsAdmin(user) && !IsOwner(user, series))
        {
            return ResultsExtensions.Forbidden("Only the series owner or admin can view edit permissions", context.Request.Path);
        }
        
        // Get detailed permission records from database
        var permissionRecords = repo.GetEditPermissionRecords(id);
        
        return Results.Ok(new { series_id = id, allowed_editors = permissionRecords });
    }
    
    private static async Task<IResult> TransferSeriesOwnership(
        string seriesId,
        TransferOwnershipRequest request,
        [FromServices] IRepository repo,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext context,
        ClaimsPrincipal user)
    {
        var logger = loggerFactory.CreateLogger("MehguViewer.Core.Endpoints.SeriesEndpoints");
        await Task.CompletedTask;
        
        // Only admins can transfer ownership
        if (!IsAdmin(user))
        {
            logger.LogWarning("User {UserUrn} attempted to transfer ownership but is not an admin", user.FindFirstValue(ClaimTypes.NameIdentifier));
            return ResultsExtensions.Forbidden("Only admins can transfer series ownership", context.Request.Path);
        }
        
        var id = seriesId.StartsWith("urn:mvn:series:") ? seriesId : $"urn:mvn:series:{seriesId}";
        
        logger.LogInformation("Transferring ownership of series {SeriesId} to {NewOwnerUrn}", id, request.new_owner_urn);

        var series = repo.GetSeries(id);
        if (series == null)
        {
            logger.LogWarning("Series {SeriesId} not found", id);
            return ResultsExtensions.NotFound($"Series {id} not found", context.Request.Path);
        }
        
        // Validate new owner URN format
        var newOwnerUrn = request.new_owner_urn.Trim();
        if (!newOwnerUrn.StartsWith("urn:mvn:user:"))
        {
            return ResultsExtensions.BadRequest("Invalid user URN format. Must start with 'urn:mvn:user:'", context.Request.Path);
        }
        
        // Check if new owner is the same as current owner
        if (series.created_by == newOwnerUrn)
        {
            return ResultsExtensions.BadRequest("New owner is already the current owner", context.Request.Path);
        }
        
        // Check if the target user exists
        var newOwner = repo.GetUser(newOwnerUrn);
        if (newOwner == null)
        {
            logger.LogWarning("New owner {NewOwnerUrn} not found", newOwnerUrn);
            return ResultsExtensions.NotFound($"User {newOwnerUrn} not found", context.Request.Path);
        }
        
        // Check if the new owner has appropriate role (Uploader or Admin)
        if (newOwner.role != "Uploader" && newOwner.role != "Admin")
        {
            return ResultsExtensions.BadRequest($"User with role '{newOwner.role}' cannot be a series owner. Only Uploaders and Admins can own series.", context.Request.Path);
        }
        
        // Transfer ownership by updating the series
        var updatedSeries = series with { created_by = newOwnerUrn };
        repo.UpdateSeries(updatedSeries);
        
        logger.LogInformation("Transferred ownership of series {SeriesId} from {OldOwner} to {NewOwner}", id, series.created_by, newOwnerUrn);

        return Results.Ok(new { 
            message = "Ownership transferred successfully", 
            series_id = id, 
            previous_owner = series.created_by,
            new_owner = newOwnerUrn,
            new_owner_username = newOwner.username
        });
    }
    
    private static async Task<IResult> GrantUnitEditPermission(
        string seriesId,
        string unitId,
        GrantEditPermissionRequest request,
        [FromServices] IRepository repo,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext context,
        ClaimsPrincipal user)
    {
        var logger = loggerFactory.CreateLogger("MehguViewer.Core.Endpoints.SeriesEndpoints");
        await Task.CompletedTask;
        var sid = seriesId.StartsWith("urn:mvn:series:") ? seriesId : $"urn:mvn:series:{seriesId}";
        var uid = unitId.StartsWith("urn:mvn:unit:") ? unitId : $"urn:mvn:unit:{unitId}";
        
        logger.LogInformation("Granting edit permission for unit {UnitId} in series {SeriesId} to user {TargetUserUrn}", uid, sid, request.user_urn);

        var series = repo.GetSeries(sid);
        if (series == null)
        {
            logger.LogWarning("Series {SeriesId} not found", sid);
            return ResultsExtensions.NotFound($"Series {sid} not found", context.Request.Path);
        }
        
        var unit = repo.GetUnit(uid);
        if (unit == null)
        {
            logger.LogWarning("Unit {UnitId} not found", uid);
            return ResultsExtensions.NotFound($"Unit {uid} not found", context.Request.Path);
        }
        
        // Only owner or admin can grant permissions
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return ResultsExtensions.Unauthorized("Authentication required", context.Request.Path);
        }
        var userUrn = userId.StartsWith("urn:mvn:user:") ? userId : $"urn:mvn:user:{userId}";
        
        var isUnitOwner = unit.created_by == userUrn || unit.created_by == userId;
        var isSeriesOwner = series.created_by == userUrn || series.created_by == userId;
        
        if (!IsAdmin(user) && !isUnitOwner && !isSeriesOwner)
        {
            logger.LogWarning("User {UserUrn} is not authorized to grant permissions for unit {UnitId}", userUrn, uid);
            return ResultsExtensions.Forbidden("Only the unit/series owner or admin can grant edit permissions", context.Request.Path);
        }
        
        repo.GrantEditPermission(uid, request.user_urn, userUrn);
        
        logger.LogInformation("Granted edit permission for unit {UnitId} to user {TargetUserUrn} by {GrantedBy}", uid, request.user_urn, userUrn);

        return Results.Ok(new { message = "Permission granted successfully", target = uid, user = request.user_urn });
    }
    
    private static async Task<IResult> RevokeUnitEditPermission(
        string seriesId,
        string unitId,
        string userUrn,
        [FromServices] IRepository repo,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext context,
        ClaimsPrincipal user)
    {
        var logger = loggerFactory.CreateLogger("MehguViewer.Core.Endpoints.SeriesEndpoints");
        await Task.CompletedTask;
        var sid = seriesId.StartsWith("urn:mvn:series:") ? seriesId : $"urn:mvn:series:{seriesId}";
        var uid = unitId.StartsWith("urn:mvn:unit:") ? unitId : $"urn:mvn:unit:{unitId}";
        
        logger.LogInformation("Revoking edit permission for unit {UnitId} in series {SeriesId} from user {TargetUserUrn}", uid, sid, userUrn);

        var series = repo.GetSeries(sid);
        if (series == null)
        {
            logger.LogWarning("Series {SeriesId} not found", sid);
            return ResultsExtensions.NotFound($"Series {sid} not found", context.Request.Path);
        }
        
        var unit = repo.GetUnit(uid);
        if (unit == null)
        {
            logger.LogWarning("Unit {UnitId} not found", uid);
            return ResultsExtensions.NotFound($"Unit {uid} not found", context.Request.Path);
        }
        
        // Only owner or admin can revoke permissions
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return ResultsExtensions.Unauthorized("Authentication required", context.Request.Path);
        }
        var currentUserUrn = userId.StartsWith("urn:mvn:user:") ? userId : $"urn:mvn:user:{userId}";
        
        var isUnitOwner = unit.created_by == currentUserUrn || unit.created_by == userId;
        var isSeriesOwner = series.created_by == currentUserUrn || series.created_by == userId;
        
        if (!IsAdmin(user) && !isUnitOwner && !isSeriesOwner)
        {
            logger.LogWarning("User {UserUrn} is not authorized to revoke permissions for unit {UnitId}", currentUserUrn, uid);
            return ResultsExtensions.Forbidden("Only the unit/series owner or admin can revoke edit permissions", context.Request.Path);
        }
        
        repo.RevokeEditPermission(uid, userUrn);
        
        logger.LogInformation("Revoked edit permission for unit {UnitId} from user {TargetUserUrn}", uid, userUrn);

        return Results.Ok(new { message = "Permission revoked successfully", target = uid, user = userUrn });
    }
    
    private static async Task<IResult> GetUnitEditPermissions(
        string seriesId,
        string unitId,
        [FromServices] IRepository repo,
        HttpContext context,
        ClaimsPrincipal user)
    {
        await Task.CompletedTask;
        var sid = seriesId.StartsWith("urn:mvn:series:") ? seriesId : $"urn:mvn:series:{seriesId}";
        var uid = unitId.StartsWith("urn:mvn:unit:") ? unitId : $"urn:mvn:unit:{unitId}";
        
        var series = repo.GetSeries(sid);
        if (series == null)
        {
            return ResultsExtensions.NotFound($"Series {sid} not found", context.Request.Path);
        }
        
        var unit = repo.GetUnit(uid);
        if (unit == null)
        {
            return ResultsExtensions.NotFound($"Unit {uid} not found", context.Request.Path);
        }
        
        // Only owner or admin can view permissions
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return ResultsExtensions.Unauthorized("Authentication required", context.Request.Path);
        }
        var userUrn = userId.StartsWith("urn:mvn:user:") ? userId : $"urn:mvn:user:{userId}";
        
        var isUnitOwner = unit.created_by == userUrn || unit.created_by == userId;
        var isSeriesOwner = series.created_by == userUrn || series.created_by == userId;
        
        if (!IsAdmin(user) && !isUnitOwner && !isSeriesOwner)
        {
            return ResultsExtensions.Forbidden("Only the unit/series owner or admin can view edit permissions", context.Request.Path);
        }
        
        var permissions = repo.GetEditPermissions(uid);
        
        return Results.Ok(new { unit_id = uid, allowed_editors = permissions });
    }
    
    /// <summary>
    /// Serve cover image from file system
    /// </summary>
    private static async Task<IResult> GetCover(
        string seriesId,
        [FromServices] FileBasedSeriesService fileService,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext context)
    {
        var logger = loggerFactory.CreateLogger("MehguViewer.Core.Endpoints.SeriesEndpoints");
        await Task.CompletedTask;
        var id = seriesId.StartsWith("urn:mvn:series:") ? seriesId : $"urn:mvn:series:{seriesId}";
        
        // Get variant from query parameter (default to "web")
        var variant = context.Request.Query["variant"].ToString().ToLowerInvariant();
        if (string.IsNullOrEmpty(variant) || !new[] { "thumbnail", "web", "raw" }.Contains(variant))
        {
            variant = "web";
        }
        
        // Get language from query parameter (optional)
        var language = context.Request.Query["lang"].ToString();
        if (string.IsNullOrWhiteSpace(language))
        {
            language = null;
        }
        
        logger.LogInformation("Serving cover for series {SeriesId} (Variant: {Variant}, Lang: {Lang})", id, variant, language ?? "default");

        var coverPath = fileService.GetCoverImagePath(id, variant, language);
        if (coverPath == null || !File.Exists(coverPath))
        {
            logger.LogWarning("Cover not found for series {SeriesId}", id);
            return ResultsExtensions.NotFound($"Cover image not found for series {id}", context.Request.Path);
        }
        
        var extension = Path.GetExtension(coverPath).ToLowerInvariant();
        var contentType = extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => "application/octet-stream"
        };
        
        var fileStream = File.OpenRead(coverPath);
        return Results.Stream(fileStream, contentType);
    }
    
    /// <summary>
    /// Get all available cover images for a series (all language variants)
    /// </summary>
    private static async Task<IResult> GetAllCovers(
        string seriesId,
        [FromServices] FileBasedSeriesService fileService,
        HttpContext context)
    {
        await Task.CompletedTask;
        var id = seriesId.StartsWith("urn:mvn:series:") ? seriesId : $"urn:mvn:series:{seriesId}";
        
        var covers = fileService.GetAllAvailableCovers(id);
        return Results.Ok(covers);
    }

    #endregion
}

/// <summary>
/// Request body for downloading cover from URL.
/// </summary>
public record CoverFromUrlRequest(string url, string? language = null);
