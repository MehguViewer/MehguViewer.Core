using MehguViewer.Core.Backend.Services;
using Microsoft.AspNetCore.Mvc;

namespace MehguViewer.Core.Backend.Endpoints;

public static class JobEndpoints
{
    public static void MapJobEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/jobs").RequireAuthorization();

        group.MapGet("/{jobId}", GetJobStatus);
    }

    private static async Task<IResult> GetJobStatus(string jobId, JobService jobService)
    {
        await Task.CompletedTask;
        if (string.IsNullOrWhiteSpace(jobId)) return Results.BadRequest("Job ID is required");

        var job = jobService.GetJob(jobId);
        if (job == null)
        {
            return Results.NotFound();
        }
        return Results.Ok(job);
    }
}
