using MehguViewer.Core.Backend.Models;
using MehguViewer.Core.Backend.Services;
using Microsoft.AspNetCore.Mvc;

namespace MehguViewer.Core.Backend.Endpoints;

public static class IngestEndpoints
{
    public static void MapIngestEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").RequireAuthorization("MvnIngest");

        // Bulk Upload
        group.MapPost("/series/{seriesId}/chapters", UploadChapterArchive).DisableAntiforgery();
        
        // Granular Upload
        group.MapPost("/units/{unitId}/pages", AddPageToUnit).DisableAntiforgery();
    }

    private static async Task<IResult> UploadChapterArchive(
        string seriesId, 
        IFormFile file, 
        [FromForm] string metadata, 
        [FromForm] string? parser_config,
        JobService jobService,
        IRepository repo)
    {
        await Task.CompletedTask;
        if (string.IsNullOrWhiteSpace(seriesId)) return Results.BadRequest("Series ID is required");
        
        // Normalize ID
        var id = seriesId.StartsWith("urn:mvn:series:") ? seriesId : $"urn:mvn:series:{seriesId}";
        if (repo.GetSeries(id) == null) return Results.NotFound("Series not found");

        if (file == null || file.Length == 0)
            return Results.BadRequest("No file uploaded");

        // In a real app, save file to temp storage
        // var tempPath = Path.GetTempFileName();
        // using var stream = System.IO.File.Create(tempPath);
        // await file.CopyToAsync(stream);

        var job = jobService.CreateJob("INGEST_ARCHIVE");
        
        // Store job metadata/file path in a real store so worker can access it
        
        return Results.Accepted($"/api/v1/jobs/{job.id}", new JobResponse(job.id, job.status));
    }

    private static async Task<IResult> AddPageToUnit(
        string unitId,
        [FromForm] IFormFile? file,
        [FromForm] string? url,
        [FromForm] int page_number,
        IRepository repo)
    {
        await Task.CompletedTask;
        if (string.IsNullOrWhiteSpace(unitId)) return Results.BadRequest("Unit ID is required");

        // Option A: Binary Upload
        if (file != null)
        {
            if (file.Length == 0) return Results.BadRequest("File is empty");
            
            // Save file, generate asset URN
            var assetUrn = UrnHelper.CreateAssetUrn();
            // In real app: Save stream to storage
            
            var page = new Page(page_number, assetUrn, null);
            repo.AddPage(unitId, page);
            return Results.Ok(page);
        }
        
        // Option B: Link
        if (!string.IsNullOrEmpty(url))
        {
            var page = new Page(page_number, null, url);
            repo.AddPage(unitId, page);
            return Results.Ok(page);
        }

        return Results.BadRequest("Either file or url must be provided");
    }
}
