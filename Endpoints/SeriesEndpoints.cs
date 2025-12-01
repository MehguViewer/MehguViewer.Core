using MehguViewer.Core.Backend.Models;
using MehguViewer.Core.Backend.Services;
using Microsoft.AspNetCore.Mvc;

namespace MehguViewer.Core.Backend.Endpoints;

public static class SeriesEndpoints
{
    public static void MapSeriesEndpoints(this IEndpointRouteBuilder app)
    {
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

    private static IResult CreateSeries(SeriesCreate request, MemoryRepository repo)
    {
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

    private static IResult ListSeries(MemoryRepository repo, string? cursor, int? limit, string? type)
    {
        var series = repo.ListSeries();
        // Simple pagination stub
        return Results.Ok(new SeriesListResponse(series.ToArray(), new CursorPagination(null, false)));
    }

    private static IResult GetSeries(string seriesId, MemoryRepository repo, HttpContext context)
    {
        // Handle both raw UUID and URN
        var id = seriesId.StartsWith("urn:mvn:series:") ? seriesId : $"urn:mvn:series:{seriesId}";
        
        var series = repo.GetSeries(id);
        if (series == null)
        {
            return ResultsExtensions.NotFound($"Series {id} not found", context.Request.Path);
        }
        return Results.Ok(series);
    }

    private static IResult CreateUnit(string seriesId, UnitCreate request, MemoryRepository repo)
    {
        var id = seriesId.StartsWith("urn:mvn:series:") ? seriesId : $"urn:mvn:series:{seriesId}";
        
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

    private static IResult ListUnits(string seriesId, MemoryRepository repo)
    {
        var id = seriesId.StartsWith("urn:mvn:series:") ? seriesId : $"urn:mvn:series:{seriesId}";
        var units = repo.ListUnits(id);
        return Results.Ok(new UnitListResponse(units.ToArray(), new CursorPagination(null, false)));
    }

    private static IResult GetUnitPages(string seriesId, string unitId, MemoryRepository repo)
    {
        var pages = repo.GetPages(unitId);
        return Results.Ok(pages);
    }

    private static IResult UpdateProgress(string seriesId, ProgressUpdate request, MemoryRepository repo)
    {
        var id = seriesId.StartsWith("urn:mvn:series:") ? seriesId : $"urn:mvn:series:{seriesId}";
        var progress = new ReadingProgress(
            id,
            request.last_read_chapter_id,
            request.page_number,
            request.completed ? "completed" : "reading",
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        );
        repo.UpdateProgress(progress);
        return Results.NoContent();
    }

    private static IResult GetProgress(string seriesId, MemoryRepository repo, HttpContext context)
    {
        var id = seriesId.StartsWith("urn:mvn:series:") ? seriesId : $"urn:mvn:series:{seriesId}";
        var progress = repo.GetProgress(id);
        if (progress == null)
        {
            return ResultsExtensions.NotFound("No progress found", context.Request.Path);
        }
        return Results.Ok(progress);
    }
}
