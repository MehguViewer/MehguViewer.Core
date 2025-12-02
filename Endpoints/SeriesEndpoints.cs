using MehguViewer.Core.Backend.Models;
using MehguViewer.Core.Backend.Services;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace MehguViewer.Core.Backend.Endpoints;

public static class SeriesEndpoints
{
    public static void MapSeriesEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/search", SearchSeries);

        var group = app.MapGroup("/api/v1/series");

        group.MapPost("/", CreateSeries);
        group.MapGet("/", ListSeries);
        group.MapGet("/{seriesId}", GetSeries);
        group.MapPost("/{seriesId}/units", CreateUnit);
        group.MapGet("/{seriesId}/units", ListUnits);
        group.MapGet("/{seriesId}/units/{unitId}/pages", GetUnitPages);
        group.MapPut("/{seriesId}/progress", UpdateProgress);
        group.MapGet("/{seriesId}/progress", GetProgress);
    }

    private static async Task<IResult> SearchSeries(
        [FromQuery] string? q, 
        [FromQuery] string? type, 
        [FromQuery] string[]? genres, 
        [FromQuery] string? status, 
        IRepository repo)
    {
        await Task.CompletedTask;
        var results = repo.SearchSeries(q, type, genres, status);
        return Results.Ok(new SearchResults(results.ToArray(), new CursorPagination(null, false)));
    }

    private static async Task<IResult> CreateSeries(SeriesCreate request, IRepository repo)
    {
        await Task.CompletedTask;
        if (string.IsNullOrWhiteSpace(request.title)) return Results.BadRequest("Title is required");
        if (string.IsNullOrWhiteSpace(request.media_type)) return Results.BadRequest("Media Type is required");

        var series = new Series(
            UrnHelper.CreateSeriesUrn(),
            null,
            request.title,
            request.description,
            new Poster("https://placehold.co/400x600", "Placeholder"),
            request.media_type,
            new Dictionary<string, string>(),
            request.reading_direction,
            request.duration_seconds
        );
        repo.AddSeries(series);
        return Results.Created($"/api/v1/series/{series.id}", series);
    }

    private static async Task<IResult> ListSeries(IRepository repo, string? cursor, int? limit, string? type)
    {
        await Task.CompletedTask;
        var series = repo.ListSeries();
        // Simple pagination stub
        return Results.Ok(new SeriesListResponse(series.ToArray(), new CursorPagination(null, false)));
    }

    private static async Task<IResult> GetSeries(string seriesId, IRepository repo, HttpContext context)
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

    private static async Task<IResult> CreateUnit(string seriesId, UnitCreate request, IRepository repo)
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

    private static async Task<IResult> ListUnits(string seriesId, IRepository repo)
    {
        await Task.CompletedTask;
        var id = seriesId.StartsWith("urn:mvn:series:") ? seriesId : $"urn:mvn:series:{seriesId}";
        var units = repo.ListUnits(id);
        return Results.Ok(new UnitListResponse(units.ToArray(), new CursorPagination(null, false)));
    }

    private static async Task<IResult> GetUnitPages(string seriesId, string unitId, IRepository repo)
    {
        await Task.CompletedTask;
        // Ideally check if unit belongs to series, but for now just get pages
        var pages = repo.GetPages(unitId);
        return Results.Ok(pages);
    }

    private static async Task<IResult> UpdateProgress(string seriesId, ProgressUpdate request, IRepository repo, System.Security.Claims.ClaimsPrincipal user)
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

    private static async Task<IResult> GetProgress(string seriesId, IRepository repo, HttpContext context, System.Security.Claims.ClaimsPrincipal user)
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
}
