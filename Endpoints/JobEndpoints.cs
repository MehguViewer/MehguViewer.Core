using MehguViewer.Core.Backend.Services;
using MehguViewer.Shared.Models;
using Microsoft.AspNetCore.Mvc;

namespace MehguViewer.Core.Backend.Endpoints;

public static class JobEndpoints
{
    public static void MapJobEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/jobs").RequireAuthorization();

        group.MapGet("/", GetAllJobs);
        group.MapGet("/{jobId}", GetJobStatus);
        group.MapPost("/{jobId}/cancel", CancelJob).RequireAuthorization("MvnAdmin");
        group.MapPost("/{jobId}/retry", RetryJob).RequireAuthorization("MvnAdmin");
    }

    private static async Task<IResult> GetAllJobs([FromServices] JobService jobService, [FromQuery] int limit = 20)
    {
        await Task.CompletedTask;
        var jobs = jobService.GetAllJobs(limit);
        return Results.Ok(new JobListResponse(jobs.ToArray()));
    }

    private static async Task<IResult> GetJobStatus(string jobId, [FromServices] JobService jobService)
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

    private static async Task<IResult> CancelJob(string jobId, [FromServices] JobService jobService)
    {
        await Task.CompletedTask;
        if (string.IsNullOrWhiteSpace(jobId)) return Results.BadRequest("Job ID is required");

        var job = jobService.GetJob(jobId);
        if (job == null)
        {
            return Results.NotFound();
        }

        if (job.status == "COMPLETED" || job.status == "FAILED" || job.status == "CANCELLED")
        {
            return Results.BadRequest("Cannot cancel a job that is already completed, failed, or cancelled");
        }

        jobService.UpdateJob(jobId, "CANCELLED", job.progress_percentage);
        return Results.Ok(new { message = "Job cancelled" });
    }

    private static async Task<IResult> RetryJob(string jobId, [FromServices] JobService jobService)
    {
        await Task.CompletedTask;
        if (string.IsNullOrWhiteSpace(jobId)) return Results.BadRequest("Job ID is required");

        var job = jobService.GetJob(jobId);
        if (job == null)
        {
            return Results.NotFound();
        }

        if (job.status != "FAILED" && job.status != "CANCELLED")
        {
            return Results.BadRequest("Can only retry failed or cancelled jobs");
        }

        // Create a new job with same type
        var newJob = jobService.CreateJob(job.type);
        
        return Results.Ok(newJob);
    }
}
