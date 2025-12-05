using MehguViewer.Shared.Models;
using MehguViewer.Core.Backend.Services;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace MehguViewer.Core.Backend.Endpoints;

public static class SeriesEndpoints
{
    /// <summary>
    /// Default federation reference for locally created content.
    /// TODO: Replace with actual federation reference from auth server.
    /// </summary>
    private const string DefaultFederationRef = "urn:mvn:node:local";

    /// <summary>
    /// Placeholder poster for newly created series.
    /// </summary>
    private static readonly Poster PlaceholderPoster = new("https://placehold.co/400x600?text=No+Cover", "Placeholder cover image");

    /// <summary>
    /// Check if user is admin (has mvn:admin scope)
    /// </summary>
    private static bool IsAdmin(ClaimsPrincipal user)
    {
        return user.HasClaim(c => c.Type == "scope" && c.Value.Contains("mvn:admin"));
    }

    /// <summary>
    /// Check if user is the owner of the series
    /// </summary>
    private static bool IsOwner(ClaimsPrincipal user, Series series)
    {
        // Token uses "sub" claim for user ID
        var userId = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        return !string.IsNullOrEmpty(userId) && series.created_by == userId;
    }

    /// <summary>
    /// Check if user can modify the series (is admin or owner)
    /// </summary>
    private static bool CanModifySeries(ClaimsPrincipal user, Series series)
    {
        return IsAdmin(user) || IsOwner(user, series);
    }

    public static void MapSeriesEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/search", SearchSeries);

        var group = app.MapGroup("/api/v1/series");

        // Create series - requires Admin or Uploader role
        group.MapPost("/", CreateSeries).RequireAuthorization("MvnIngestOrAdmin");
        group.MapGet("/", ListSeries);
        group.MapGet("/{seriesId}", GetSeries);
        group.MapPut("/{seriesId}", UpdateSeries).RequireAuthorization("MvnIngestOrAdmin");
        group.MapPatch("/{seriesId}", UpdateSeries).RequireAuthorization("MvnIngestOrAdmin");
        group.MapDelete("/{seriesId}", DeleteSeries).RequireAuthorization("MvnIngestOrAdmin");
        group.MapPost("/{seriesId}/units", CreateUnit).RequireAuthorization("MvnIngestOrAdmin");
        group.MapGet("/{seriesId}/units", ListUnits);
        group.MapPatch("/{seriesId}/units/{unitId}", UpdateUnit).RequireAuthorization("MvnIngestOrAdmin");
        group.MapDelete("/{seriesId}/units/{unitId}", DeleteUnit).RequireAuthorization("MvnIngestOrAdmin");
        group.MapGet("/{seriesId}/units/{unitId}/pages", GetUnitPages);
        group.MapPut("/{seriesId}/progress", UpdateProgress);
        group.MapGet("/{seriesId}/progress", GetProgress);
        
        // Cover upload
        group.MapPost("/{seriesId}/cover", UploadCover)
            .RequireAuthorization("MvnIngestOrAdmin")
            .DisableAntiforgery();
        
        // Cover from URL (downloads and saves locally)
        group.MapPost("/{seriesId}/cover/from-url", DownloadCoverFromUrl)
            .RequireAuthorization("MvnIngestOrAdmin");
    }

    private static async Task<IResult> SearchSeries(
        [FromQuery] string? q, 
        [FromQuery] string? type, 
        [FromQuery] string[]? tags, 
        [FromQuery] string? status, 
        [FromServices] IRepository repo)
    {
        await Task.CompletedTask;
        var results = repo.SearchSeries(q, type, tags, status);
        return Results.Ok(new SearchResults(results.ToArray(), new CursorPagination(null, false)));
    }

    private static async Task<IResult> CreateSeries(
        SeriesCreate request, 
        [FromServices] IRepository repo,
        HttpContext context,
        ClaimsPrincipal user)
    {
        await Task.CompletedTask;

        // Get the user ID from the authenticated user
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return ResultsExtensions.Unauthorized("Authentication required", context.Request.Path);
        }

        // Validate required fields
        if (string.IsNullOrWhiteSpace(request.title))
        {
            return ResultsExtensions.BadRequest("Title is required", context.Request.Path);
        }
        
        if (string.IsNullOrWhiteSpace(request.media_type))
        {
            return ResultsExtensions.BadRequest("Media type is required", context.Request.Path);
        }

        // Validate media_type against fixed types (Photo, Text, Video)
        var normalizedMediaType = MediaTypes.Normalize(request.media_type);
        if (normalizedMediaType == null)
        {
            return ResultsExtensions.BadRequest(
                $"Invalid media_type '{request.media_type}'. Valid types: {string.Join(", ", MediaTypes.All)}", 
                context.Request.Path);
        }

        // Validate reading_direction
        if (!ReadingDirections.IsValid(request.reading_direction))
        {
            return ResultsExtensions.BadRequest(
                $"Invalid reading_direction '{request.reading_direction}'. Valid values: {string.Join(", ", ReadingDirections.All)}", 
                context.Request.Path);
        }

        var normalizedDirection = ReadingDirections.Normalize(request.reading_direction) ?? ReadingDirections.LTR;

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
        
        repo.AddSeries(series);
        return Results.Created($"/api/v1/series/{series.id}", series);
    }

    private static async Task<IResult> ListSeries([FromServices] IRepository repo, string? cursor, int? limit, string? type)
    {
        await Task.CompletedTask;
        var series = repo.ListSeries();
        // Simple pagination stub
        return Results.Ok(new SeriesListResponse(series.ToArray(), new CursorPagination(null, false)));
    }

    private static async Task<IResult> GetSeries(string seriesId, [FromServices] IRepository repo, HttpContext context)
    {
        await Task.CompletedTask;
        // Handle both raw UUID and URN
        var id = seriesId.StartsWith("urn:mvn:series:") ? seriesId : $"urn:mvn:series:{seriesId}";
        
        var series = repo.GetSeries(id);
        if (series == null)
        {
            return ResultsExtensions.NotFound($"Series {id} not found", context.Request.Path);
        }
        return Results.Ok(series);
    }

    private static async Task<IResult> DeleteSeries(string seriesId, [FromServices] IRepository repo, HttpContext context, ClaimsPrincipal user)
    {
        await Task.CompletedTask;
        var id = seriesId.StartsWith("urn:mvn:series:") ? seriesId : $"urn:mvn:series:{seriesId}";
        
        var series = repo.GetSeries(id);
        if (series == null)
        {
            return ResultsExtensions.NotFound($"Series {id} not found", context.Request.Path);
        }
        
        // Admin can delete any series, uploader can only delete their own
        if (!CanModifySeries(user, series))
        {
            return ResultsExtensions.Forbidden("You can only delete series you created", context.Request.Path);
        }
        
        repo.DeleteSeries(id);
        return Results.NoContent();
    }

    private static async Task<IResult> UpdateSeries(string seriesId, SeriesUpdate request, [FromServices] IRepository repo, HttpContext context, ClaimsPrincipal user)
    {
        await Task.CompletedTask;
        var id = seriesId.StartsWith("urn:mvn:series:") ? seriesId : $"urn:mvn:series:{seriesId}";
        
        var existing = repo.GetSeries(id);
        if (existing == null)
        {
            return ResultsExtensions.NotFound($"Series {id} not found", context.Request.Path);
        }

        // Admin can update any series, uploader can only update their own
        if (!CanModifySeries(user, existing))
        {
            return ResultsExtensions.Forbidden("You can only edit series you created", context.Request.Path);
        }

        // Validate reading_direction if provided
        if (request.reading_direction != null && !ReadingDirections.IsValid(request.reading_direction))
        {
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
                return ResultsExtensions.BadRequest(
                    $"Invalid media_type '{request.media_type}'. Valid types: {string.Join(", ", MediaTypes.All)}", 
                    context.Request.Path);
            }
        }

        // Normalize content warnings if provided
        var normalizedWarnings = request.content_warnings != null
            ? ContentWarnings.NormalizeAll(request.content_warnings)
            : existing.content_warnings;

        // Normalize tags if provided
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
        
        repo.UpdateSeries(updated);
        return Results.Ok(updated);
    }

    private static async Task<IResult> CreateUnit(string seriesId, UnitCreate request, [FromServices] IRepository repo)
    {
        await Task.CompletedTask;
        var id = seriesId.StartsWith("urn:mvn:series:") ? seriesId : $"urn:mvn:series:{seriesId}";
        
        if (repo.GetSeries(id) == null) return Results.NotFound("Series not found");

        var unit = new Unit(
            Guid.NewGuid().ToString(),
            id,
            request.unit_number,
            request.title ?? $"Chapter {request.unit_number}",
            DateTime.UtcNow
        );
        repo.AddUnit(unit);
        return Results.Created($"/api/v1/units/{unit.id}", unit);
    }

    private static async Task<IResult> ListUnits(string seriesId, [FromServices] IRepository repo)
    {
        await Task.CompletedTask;
        var id = seriesId.StartsWith("urn:mvn:series:") ? seriesId : $"urn:mvn:series:{seriesId}";
        var units = repo.ListUnits(id);
        return Results.Ok(new UnitListResponse(units.ToArray(), new CursorPagination(null, false)));
    }

    private static async Task<IResult> UpdateUnit(string seriesId, string unitId, UnitUpdate request, [FromServices] IRepository repo, HttpContext context, ClaimsPrincipal user)
    {
        await Task.CompletedTask;
        
        // First check series ownership
        var sid = seriesId.StartsWith("urn:mvn:series:") ? seriesId : $"urn:mvn:series:{seriesId}";
        var series = repo.GetSeries(sid);
        if (series == null)
        {
            return ResultsExtensions.NotFound($"Series {sid} not found", context.Request.Path);
        }
        
        // Admin can update any series, uploader can only update their own
        if (!CanModifySeries(user, series))
        {
            return ResultsExtensions.Forbidden("You can only edit units in series you created", context.Request.Path);
        }
        
        var existing = repo.GetUnit(unitId);
        if (existing == null)
        {
            return ResultsExtensions.NotFound($"Unit {unitId} not found", context.Request.Path);
        }
        
        // Merge: only update fields that are explicitly provided (not null)
        var updated = new Unit(
            id: existing.id,
            series_id: existing.series_id,
            unit_number: request.unit_number ?? existing.unit_number,
            title: request.title ?? existing.title,
            created_at: existing.created_at
        );
        
        repo.UpdateUnit(updated);
        return Results.Ok(updated);
    }

    private static async Task<IResult> DeleteUnit(string seriesId, string unitId, [FromServices] IRepository repo, HttpContext context, ClaimsPrincipal user)
    {
        await Task.CompletedTask;
        
        // First check series ownership
        var sid = seriesId.StartsWith("urn:mvn:series:") ? seriesId : $"urn:mvn:series:{seriesId}";
        var series = repo.GetSeries(sid);
        if (series == null)
        {
            return ResultsExtensions.NotFound($"Series {sid} not found", context.Request.Path);
        }
        
        // Admin can delete any series, uploader can only delete their own
        if (!CanModifySeries(user, series))
        {
            return ResultsExtensions.Forbidden("You can only delete units in series you created", context.Request.Path);
        }
        
        var existing = repo.GetUnit(unitId);
        if (existing == null)
        {
            return ResultsExtensions.NotFound($"Unit {unitId} not found", context.Request.Path);
        }
        
        repo.DeleteUnit(unitId);
        return Results.NoContent();
    }

    private static async Task<IResult> GetUnitPages(string seriesId, string unitId, [FromServices] IRepository repo)
    {
        await Task.CompletedTask;
        // Ideally check if unit belongs to series, but for now just get pages
        var pages = repo.GetPages(unitId);
        return Results.Ok(pages);
    }

    private static async Task<IResult> UpdateProgress(string seriesId, ProgressUpdate request, [FromServices] IRepository repo, System.Security.Claims.ClaimsPrincipal user)
    {
        await Task.CompletedTask;
        var userId = user.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

        if (string.IsNullOrWhiteSpace(request.last_read_chapter_id)) return Results.BadRequest("Chapter ID is required");

        var id = seriesId.StartsWith("urn:mvn:series:") ? seriesId : $"urn:mvn:series:{seriesId}";
        var progress = new ReadingProgress(
            id,
            request.last_read_chapter_id,
            request.page_number,
            request.completed ? "completed" : "reading",
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        );
        repo.UpdateProgress(userId, progress);
        return Results.NoContent();
    }

    private static async Task<IResult> GetProgress(string seriesId, [FromServices] IRepository repo, HttpContext context, System.Security.Claims.ClaimsPrincipal user)
    {
        await Task.CompletedTask;
        var userId = user.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

        var id = seriesId.StartsWith("urn:mvn:series:") ? seriesId : $"urn:mvn:series:{seriesId}";
        var progress = repo.GetProgress(userId, id);
        if (progress == null)
        {
            return ResultsExtensions.NotFound("No progress found", context.Request.Path);
        }
        return Results.Ok(progress);
    }

    private static async Task<IResult> UploadCover(
        string seriesId, 
        IFormFile file,
        [FromServices] IRepository repo,
        [FromServices] IWebHostEnvironment env,
        HttpContext context,
        ClaimsPrincipal user)
    {
        var id = seriesId.StartsWith("urn:mvn:series:") ? seriesId : $"urn:mvn:series:{seriesId}";
        
        var existing = repo.GetSeries(id);
        if (existing == null)
        {
            return ResultsExtensions.NotFound($"Series {id} not found", context.Request.Path);
        }

        // Admin can update any series, uploader can only update their own
        if (!CanModifySeries(user, existing))
        {
            return ResultsExtensions.Forbidden("You can only upload covers for series you created", context.Request.Path);
        }

        if (file == null || file.Length == 0)
        {
            return ResultsExtensions.BadRequest("No file provided", context.Request.Path);
        }

        // Validate file type
        var allowedTypes = new[] { "image/jpeg", "image/png", "image/webp", "image/gif" };
        if (!allowedTypes.Contains(file.ContentType.ToLower()))
        {
            return ResultsExtensions.BadRequest("Invalid file type. Allowed: jpg, png, webp, gif", context.Request.Path);
        }

        // Max 10MB
        if (file.Length > 10 * 1024 * 1024)
        {
            return ResultsExtensions.BadRequest("File too large. Maximum size is 10MB", context.Request.Path);
        }

        try
        {
            // Get the series UUID from the URN
            var seriesUuid = id.Replace("urn:mvn:series:", "");
            
            // Create covers directory
            var coversDir = Path.Combine(env.ContentRootPath, "data", "covers");
            Directory.CreateDirectory(coversDir);

            // Determine file extension
            var extension = file.ContentType.ToLower() switch
            {
                "image/jpeg" => ".jpg",
                "image/png" => ".png",
                "image/webp" => ".webp",
                "image/gif" => ".gif",
                _ => ".jpg"
            };

            var fileName = $"{seriesUuid}{extension}";
            var filePath = Path.Combine(coversDir, fileName);

            // Delete any existing cover files for this series
            foreach (var existingFile in Directory.GetFiles(coversDir, $"{seriesUuid}.*"))
            {
                File.Delete(existingFile);
            }

            // Save the file
            await using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // Generate the URL for the cover
            var coverUrl = $"/covers/{fileName}";

            // Update the series with the new cover URL
            var updated = existing with
            {
                poster = new Poster(coverUrl, $"Cover for {existing.title}"),
                updated_at = DateTime.UtcNow
            };
            repo.UpdateSeries(updated);

            return Results.Ok(new { cover_url = coverUrl });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error uploading cover: {ex.Message}");
            return ResultsExtensions.InternalServerError("Failed to upload cover", context.Request.Path);
        }
    }
    
    /// <summary>
    /// Downloads a cover image from a URL and saves it locally.
    /// </summary>
    private static async Task<IResult> DownloadCoverFromUrl(
        string seriesId,
        [FromBody] CoverFromUrlRequest request,
        [FromServices] IRepository repo,
        [FromServices] IWebHostEnvironment env,
        [FromServices] IHttpClientFactory httpClientFactory,
        HttpContext context,
        ClaimsPrincipal user)
    {
        var id = seriesId.StartsWith("urn:mvn:series:") ? seriesId : $"urn:mvn:series:{seriesId}";
        
        var existing = repo.GetSeries(id);
        if (existing == null)
        {
            return ResultsExtensions.NotFound($"Series {id} not found", context.Request.Path);
        }

        // Admin can update any series, uploader can only update their own
        if (!CanModifySeries(user, existing))
        {
            return ResultsExtensions.Forbidden("You can only set covers for series you created", context.Request.Path);
        }

        if (string.IsNullOrWhiteSpace(request.url))
        {
            return ResultsExtensions.BadRequest("No URL provided", context.Request.Path);
        }

        try
        {
            var httpClient = httpClientFactory.CreateClient("CoverDownload");
            
            // Download the image
            using var response = await httpClient.GetAsync(request.url);
            if (!response.IsSuccessStatusCode)
            {
                return ResultsExtensions.BadRequest($"Failed to download image from URL: {response.StatusCode}", context.Request.Path);
            }

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
            
            // Validate content type
            var allowedTypes = new[] { "image/jpeg", "image/png", "image/webp", "image/gif" };
            if (!allowedTypes.Contains(contentType.ToLower()))
            {
                return ResultsExtensions.BadRequest($"Invalid image type: {contentType}. Allowed: jpg, png, webp, gif", context.Request.Path);
            }

            var imageBytes = await response.Content.ReadAsByteArrayAsync();
            
            // Max 10MB
            if (imageBytes.Length > 10 * 1024 * 1024)
            {
                return ResultsExtensions.BadRequest("Image too large. Maximum size is 10MB", context.Request.Path);
            }

            // Get the series UUID from the URN
            var seriesUuid = id.Replace("urn:mvn:series:", "");
            
            // Create covers directory
            var coversDir = Path.Combine(env.ContentRootPath, "data", "covers");
            Directory.CreateDirectory(coversDir);

            // Determine file extension
            var extension = contentType.ToLower() switch
            {
                "image/jpeg" => ".jpg",
                "image/png" => ".png",
                "image/webp" => ".webp",
                "image/gif" => ".gif",
                _ => ".jpg"
            };

            var fileName = $"{seriesUuid}{extension}";
            var filePath = Path.Combine(coversDir, fileName);

            // Delete any existing cover files for this series
            foreach (var existingFile in Directory.GetFiles(coversDir, $"{seriesUuid}.*"))
            {
                File.Delete(existingFile);
            }

            // Save the file
            await File.WriteAllBytesAsync(filePath, imageBytes);

            // Generate the local URL for the cover
            var coverUrl = $"/covers/{fileName}";

            // Update the series with the new local cover URL
            var updated = existing with
            {
                poster = new Poster(coverUrl, $"Cover for {existing.title}"),
                updated_at = DateTime.UtcNow
            };
            repo.UpdateSeries(updated);

            return Results.Ok(new { cover_url = coverUrl, downloaded_from = request.url });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error downloading cover from URL: {ex.Message}");
            return ResultsExtensions.InternalServerError("Failed to download cover from URL", context.Request.Path);
        }
    }
}

/// <summary>
/// Request body for downloading cover from URL.
/// </summary>
public record CoverFromUrlRequest(string url);
