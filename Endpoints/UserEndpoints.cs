using System.Security.Claims;
using MehguViewer.Core.Backend.Models;
using MehguViewer.Core.Backend.Services;
using Microsoft.AspNetCore.Mvc;

namespace MehguViewer.Core.Backend.Endpoints;

public static class UserEndpoints
{
    public static void MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/me").RequireAuthorization();

        group.MapGet("/library", GetLibrary);
        group.MapGet("/history", GetHistory);
        group.MapPost("/history/batch", BatchImportHistory);
        group.MapPost("/progress", UpdateProgress);
        group.MapDelete("/", DeleteAccount);
    }

    private static async Task<IResult> DeleteAccount(ClaimsPrincipal user, IRepository repo)
    {
        await Task.CompletedTask;
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

        // 1. Anonymize Comments
        repo.AnonymizeUserContent(userId);

        // 2. Delete History
        repo.DeleteUserHistory(userId);

        // 3. Delete User
        repo.DeleteUser(userId);

        return Results.NoContent();
    }

    private static async Task<IResult> BatchImportHistory(
        [FromBody] HistoryBatchImport request, 
        ClaimsPrincipal user, 
        IRepository repo,
        JobService jobService)
    {
        await Task.CompletedTask;
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

        if (request.items == null || !request.items.Any())
            return Results.BadRequest("No items to import");

        // In a real implementation, this would be a background job
        // For now, we'll just process it synchronously or queue a job
        
        var job = jobService.CreateJob("IMPORT_HISTORY");
        
        // Simulate processing
        foreach (var item in request.items)
        {
            repo.UpdateProgress(userId, new ReadingProgress(
                item.series_urn,
                item.chapter_urn,
                1, // Default page
                "reading",
                new DateTimeOffset(item.read_at).ToUnixTimeMilliseconds()
            ));
        }
        
        jobService.UpdateJob(job.id, "COMPLETED", 100);

        return Results.Accepted($"/api/v1/jobs/{job.id}");
    }

    private static async Task<IResult> GetLibrary(ClaimsPrincipal user, IRepository repo)
    {
        await Task.CompletedTask;
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

        var library = repo.GetLibrary(userId);
        return Results.Ok(library);
    }

    private static async Task<IResult> GetHistory(ClaimsPrincipal user, IRepository repo)
    {
        await Task.CompletedTask;
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

        var history = repo.GetHistory(userId);
        return Results.Ok(new HistoryListResponse(history.ToArray(), new HistoryMeta(history.Count(), false)));
    }

    private static async Task<IResult> UpdateProgress(ClaimsPrincipal user, [FromBody] ReadingProgress progress, IRepository repo)
    {
        await Task.CompletedTask;
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

        // Basic validation
        if (string.IsNullOrEmpty(progress.series_urn)) return Results.BadRequest("series_urn is required");

        repo.UpdateProgress(userId, progress);
        return Results.Ok();
    }
}
