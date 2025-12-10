using MehguViewer.Core.Services;
using MehguViewer.Core.Helpers;
using MehguViewer.Core.Shared;
using Microsoft.AspNetCore.Mvc;

namespace MehguViewer.Core.Endpoints;

/// <summary>
/// Provides HTTP endpoints for job management operations including listing, status checking,
/// cancellation, and retry functionality for background jobs.
/// </summary>
/// <remarks>
/// <para><strong>Purpose:</strong> Exposes RESTful API endpoints for job lifecycle management 
/// with role-based access control and comprehensive validation.</para>
/// 
/// <para><strong>Security:</strong></para>
/// <list type="bullet">
///   <item><description>All endpoints require authentication</description></item>
///   <item><description>Cancel and retry operations require MvnAdmin role</description></item>
///   <item><description>Input validation on all job ID parameters</description></item>
///   <item><description>Rate limiting applied via middleware</description></item>
/// </list>
/// 
/// <para><strong>Endpoints:</strong></para>
/// <list type="bullet">
///   <item><description>GET /api/v1/jobs - List jobs with optional limit</description></item>
///   <item><description>GET /api/v1/jobs/{jobId} - Get job status and details</description></item>
///   <item><description>POST /api/v1/jobs/{jobId}/cancel - Cancel a running job (admin)</description></item>
///   <item><description>POST /api/v1/jobs/{jobId}/retry - Retry a failed job (admin)</description></item>
/// </list>
/// 
/// <para><strong>Performance Considerations:</strong></para>
/// <list type="bullet">
///   <item><description>Limit parameter for pagination to prevent large responses</description></item>
///   <item><description>Efficient O(1) lookup for job retrieval by ID</description></item>
///   <item><description>Minimal validation overhead with early returns</description></item>
/// </list>
/// </remarks>
public static class JobEndpoints
{
    #region Constants

    /// <summary>Default maximum number of jobs to return in list operations.</summary>
    private const int DefaultJobLimit = 20;

    /// <summary>Maximum allowed jobs in a single list request to prevent resource exhaustion.</summary>
    private const int MaxJobLimit = 100;

    /// <summary>Minimum allowed limit value to ensure valid pagination.</summary>
    private const int MinJobLimit = 1;

    #endregion

    #region Endpoint Mapping

    /// <summary>
    /// Maps job-related HTTP endpoints to the application routing.
    /// </summary>
    /// <param name="app">The endpoint route builder.</param>
    public static void MapJobEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/jobs")
            .RequireAuthorization()
            .WithTags("Jobs");

        group.MapGet("/", GetAllJobs)
            .WithName("GetAllJobs")
            .WithSummary("Retrieve a list of jobs")
            .WithDescription("Returns a paginated list of jobs ordered by priority (processing > queued > completed > failed > cancelled)")
            .Produces<JobListResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest);

        group.MapGet("/{jobId}", GetJobStatus)
            .WithName("GetJobStatus")
            .WithSummary("Get job status by ID")
            .WithDescription("Retrieves detailed information about a specific job including progress and results")
            .Produces<Job>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        group.MapPost("/{jobId}/cancel", CancelJob)
            .RequireAuthorization("MvnAdmin")
            .WithName("CancelJob")
            .WithSummary("Cancel a running job (Admin only)")
            .WithDescription("Cancels a job that is currently queued or processing")
            .Produces(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden);

        group.MapPost("/{jobId}/retry", RetryJob)
            .RequireAuthorization("MvnAdmin")
            .WithName("RetryJob")
            .WithSummary("Retry a failed or cancelled job (Admin only)")
            .WithDescription("Creates a new job instance for a previously failed or cancelled job")
            .Produces<Job>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden);
    }

    #endregion

    #region Endpoint Handlers

    /// <summary>
    /// Retrieves a paginated list of jobs ordered by processing priority.
    /// </summary>
    /// <param name="jobService">Service for job management operations.</param>
    /// <param name="loggerFactory">Factory for creating loggers.</param>
    /// <param name="context">HTTP context for the current request.</param>
    /// <param name="limit">Maximum number of jobs to return (1-100). Defaults to 20.</param>
    /// <returns>HTTP 200 with job list, or HTTP 400 if limit is invalid.</returns>
    private static async Task<IResult> GetAllJobs(
        [FromServices] JobService jobService, 
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext context,
        [FromQuery] int limit = DefaultJobLimit)
    {
        var logger = loggerFactory.CreateLogger("MehguViewer.Core.Endpoints.JobEndpoints");
        await Task.CompletedTask;

        // Validate limit parameter to prevent abuse and ensure valid pagination
        if (limit < MinJobLimit)
        {
            logger.LogWarning("GetAllJobs: Invalid limit {Limit} (minimum: {MinLimit}). Client IP: {ClientIP}",
                limit, MinJobLimit, context.Connection.RemoteIpAddress);
            return ResultsExtensions.BadRequest(
                $"Limit must be at least {MinJobLimit}", 
                context.Request.Path);
        }

        if (limit > MaxJobLimit)
        {
            logger.LogWarning("GetAllJobs: Limit {Limit} exceeds maximum {MaxLimit}, capping to maximum. Client IP: {ClientIP}",
                limit, MaxJobLimit, context.Connection.RemoteIpAddress);
            limit = MaxJobLimit;
        }

        logger.LogInformation("Retrieving jobs list (Limit: {Limit}, User: {User})", 
            limit, context.User.Identity?.Name ?? "Unknown");
        
        var jobs = jobService.GetAllJobs(limit);
        var jobArray = jobs.ToArray();
        
        logger.LogDebug("Retrieved {JobCount} jobs", jobArray.Length);
        
        return Results.Ok(new JobListResponse(jobArray));
    }

    /// <summary>
    /// Retrieves detailed information about a specific job.
    /// </summary>
    /// <param name="jobId">The unique identifier of the job.</param>
    /// <param name="jobService">Service for job management operations.</param>
    /// <param name="loggerFactory">Factory for creating loggers.</param>
    /// <param name="context">HTTP context for the current request.</param>
    /// <returns>HTTP 200 with job details, HTTP 400 if ID is invalid, or HTTP 404 if job not found.</returns>
    private static async Task<IResult> GetJobStatus(
        string jobId, 
        [FromServices] JobService jobService,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext context)
    {
        var logger = loggerFactory.CreateLogger("MehguViewer.Core.Endpoints.JobEndpoints");
        await Task.CompletedTask;

        // Validate job ID parameter
        if (string.IsNullOrWhiteSpace(jobId))
        {
            logger.LogWarning("GetJobStatus: Job ID is null or empty. Client IP: {ClientIP}",
                context.Connection.RemoteIpAddress);
            return ResultsExtensions.BadRequest("Job ID is required", context.Request.Path);
        }

        logger.LogDebug("Retrieving job status for {JobId} (User: {User})", 
            jobId, context.User.Identity?.Name ?? "Unknown");

        // Retrieve job from service
        var job = jobService.GetJob(jobId);
        if (job == null)
        {
            logger.LogWarning("Job {JobId} not found (User: {User})", 
                jobId, context.User.Identity?.Name ?? "Unknown");
            return ResultsExtensions.NotFound($"Job {jobId} not found", context.Request.Path);
        }

        logger.LogInformation("Retrieved job {JobId} (Type: {JobType}, Status: {Status}, Progress: {Progress}%)",
            jobId, job.type, job.status, job.progress_percentage);
        
        return Results.Ok(job);
    }

    /// <summary>
    /// Cancels a job that is currently queued or processing.
    /// </summary>
    /// <param name="jobId">The unique identifier of the job to cancel.</param>
    /// <param name="jobService">Service for job management operations.</param>
    /// <param name="loggerFactory">Factory for creating loggers.</param>
    /// <param name="context">HTTP context for the current request.</param>
    /// <returns>HTTP 200 if cancelled, HTTP 400 if already terminal state, or HTTP 404 if job not found.</returns>
    private static async Task<IResult> CancelJob(
        string jobId, 
        [FromServices] JobService jobService,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext context)
    {
        var logger = loggerFactory.CreateLogger("MehguViewer.Core.Endpoints.JobEndpoints");
        await Task.CompletedTask;

        // Validate job ID parameter
        if (string.IsNullOrWhiteSpace(jobId))
        {
            logger.LogWarning("CancelJob: Job ID is null or empty. Admin: {Admin}, Client IP: {ClientIP}",
                context.User.Identity?.Name ?? "Unknown", context.Connection.RemoteIpAddress);
            return ResultsExtensions.BadRequest("Job ID is required", context.Request.Path);
        }

        logger.LogInformation("Attempting to cancel job {JobId} (Admin: {Admin})", 
            jobId, context.User.Identity?.Name ?? "Unknown");

        // Retrieve job to validate existence and current state
        var job = jobService.GetJob(jobId);
        if (job == null)
        {
            logger.LogWarning("CancelJob: Job {JobId} not found (Admin: {Admin})", 
                jobId, context.User.Identity?.Name ?? "Unknown");
            return ResultsExtensions.NotFound($"Job {jobId} not found", context.Request.Path);
        }

        // Validate job is in a cancellable state
        var terminalStates = new[] { "COMPLETED", "FAILED", "CANCELLED" };
        if (terminalStates.Contains(job.status))
        {
            logger.LogWarning("CancelJob: Cannot cancel job {JobId} with status {Status} (Admin: {Admin})",
                jobId, job.status, context.User.Identity?.Name ?? "Unknown");
            return ResultsExtensions.BadRequest(
                $"Cannot cancel a job that is already {job.status.ToLower()}", 
                context.Request.Path);
        }

        // Perform cancellation
        try
        {
            jobService.UpdateJob(jobId, "CANCELLED", job.progress_percentage);
            
            logger.LogInformation("Successfully cancelled job {JobId} (Type: {JobType}, Previous Status: {PreviousStatus}, Admin: {Admin})",
                jobId, job.type, job.status, context.User.Identity?.Name ?? "Unknown");
            
            return Results.Ok(new { 
                message = "Job cancelled successfully",
                job_id = jobId,
                previous_status = job.status
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to cancel job {JobId} (Admin: {Admin})", 
                jobId, context.User.Identity?.Name ?? "Unknown");
            throw;
        }
    }

    /// <summary>
    /// Creates a new job to retry a previously failed or cancelled job.
    /// </summary>
    /// <param name="jobId">The unique identifier of the failed/cancelled job to retry.</param>
    /// <param name="jobService">Service for job management operations.</param>
    /// <param name="loggerFactory">Factory for creating loggers.</param>
    /// <param name="context">HTTP context for the current request.</param>
    /// <returns>HTTP 200 with new job details, HTTP 400 if not retryable, or HTTP 404 if job not found.</returns>
    private static async Task<IResult> RetryJob(
        string jobId, 
        [FromServices] JobService jobService,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext context)
    {
        var logger = loggerFactory.CreateLogger("MehguViewer.Core.Endpoints.JobEndpoints");
        await Task.CompletedTask;

        // Validate job ID parameter
        if (string.IsNullOrWhiteSpace(jobId))
        {
            logger.LogWarning("RetryJob: Job ID is null or empty. Admin: {Admin}, Client IP: {ClientIP}",
                context.User.Identity?.Name ?? "Unknown", context.Connection.RemoteIpAddress);
            return ResultsExtensions.BadRequest("Job ID is required", context.Request.Path);
        }

        logger.LogInformation("Attempting to retry job {JobId} (Admin: {Admin})", 
            jobId, context.User.Identity?.Name ?? "Unknown");

        // Retrieve job to validate existence and current state
        var job = jobService.GetJob(jobId);
        if (job == null)
        {
            logger.LogWarning("RetryJob: Job {JobId} not found (Admin: {Admin})", 
                jobId, context.User.Identity?.Name ?? "Unknown");
            return ResultsExtensions.NotFound($"Job {jobId} not found", context.Request.Path);
        }

        // Validate job is in a retryable state
        var retryableStates = new[] { "FAILED", "CANCELLED" };
        if (!retryableStates.Contains(job.status))
        {
            logger.LogWarning("RetryJob: Cannot retry job {JobId} with status {Status} (Admin: {Admin})",
                jobId, job.status, context.User.Identity?.Name ?? "Unknown");
            return ResultsExtensions.BadRequest(
                $"Can only retry jobs with status FAILED or CANCELLED. Current status: {job.status}", 
                context.Request.Path);
        }

        // Create new job instance for retry
        try
        {
            var newJob = jobService.CreateJob(job.type);
            
            logger.LogInformation("Successfully created retry job {NewJobId} for original job {OriginalJobId} (Type: {JobType}, Original Status: {OriginalStatus}, Admin: {Admin})",
                newJob.id, jobId, job.type, job.status, context.User.Identity?.Name ?? "Unknown");
            
            return Results.Ok(newJob);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create retry job for {JobId} (Type: {JobType}, Admin: {Admin})", 
                jobId, job.type, context.User.Identity?.Name ?? "Unknown");
            throw;
        }
    }

    #endregion
}
