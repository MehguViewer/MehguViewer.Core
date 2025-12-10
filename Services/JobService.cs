using MehguViewer.Core.Helpers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using MehguViewer.Core.Shared;

namespace MehguViewer.Core.Services;

/// <summary>
/// Service for managing background jobs with progress tracking and queue management.
/// </summary>
/// <remarks>
/// <para><strong>Purpose:</strong> Provides thread-safe job lifecycle management with progress tracking
/// and queue-based processing for background operations.</para>
/// 
/// <para><strong>Core Capabilities:</strong></para>
/// <list type="bullet">
///   <item><description>Job lifecycle management (create, update, retrieve, cancel)</description></item>
///   <item><description>In-memory job queue for background processing with FIFO ordering</description></item>
///   <item><description>Progress tracking with percentage completion (0-100)</description></item>
///   <item><description>Concurrent-safe operations using ConcurrentDictionary and ConcurrentQueue</description></item>
///   <item><description>Automatic job expiration to prevent memory leaks</description></item>
///   <item><description>Comprehensive logging at appropriate levels</description></item>
/// </list>
/// 
/// <para><strong>Job Types:</strong></para>
/// <list type="bullet">
///   <item><description>INGEST: Content ingestion operations</description></item>
///   <item><description>METADATA_FETCH: External metadata retrieval</description></item>
///   <item><description>THUMBNAIL_GEN: Image thumbnail generation</description></item>
///   <item><description>VALIDATION: Content validation tasks</description></item>
/// </list>
/// 
/// <para><strong>Job States:</strong></para>
/// <list type="bullet">
///   <item><description>QUEUED: Job created and waiting for processing</description></item>
///   <item><description>PROCESSING: Job actively being executed</description></item>
///   <item><description>COMPLETED: Job finished successfully</description></item>
///   <item><description>FAILED: Job encountered an error</description></item>
///   <item><description>CANCELLED: Job terminated by user request</description></item>
/// </list>
/// 
/// <para><strong>Security Considerations:</strong></para>
/// <list type="bullet">
///   <item><description>Input validation on all job types and IDs</description></item>
///   <item><description>Bounded queue size to prevent resource exhaustion</description></item>
///   <item><description>Job expiration to prevent unbounded memory growth</description></item>
///   <item><description>No sensitive data in job objects (use URN references)</description></item>
/// </list>
/// 
/// <para><strong>Performance:</strong></para>
/// <list type="bullet">
///   <item><description>O(1) job lookup using ConcurrentDictionary</description></item>
///   <item><description>Lock-free queue operations for high throughput</description></item>
///   <item><description>Automatic cleanup of expired jobs via background timer</description></item>
/// </list>
/// 
/// <para><strong>Note:</strong> Uses in-memory storage for job state. For production systems requiring
/// persistence across restarts, consider implementing persistent storage or extending with database backing.</para>
/// </remarks>
public sealed class JobService : IDisposable
{
    #region Constants

    /// <summary>Default maximum number of jobs to return in list operations.</summary>
    private const int DefaultJobLimit = 20;
    
    /// <summary>Maximum allowed jobs in the system to prevent resource exhaustion.</summary>
    private const int MaxJobsLimit = 10000;
    
    /// <summary>Log progress updates at this percentage interval to avoid log spam.</summary>
    private const int ProgressLogInterval = 25;
    
    /// <summary>Maximum age for completed/failed jobs before automatic cleanup (24 hours).</summary>
    private static readonly TimeSpan JobExpirationTime = TimeSpan.FromHours(24);
    
    /// <summary>Interval for running the cleanup timer (every 1 hour).</summary>
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);
    
    /// <summary>Maximum length for job type strings to prevent abuse.</summary>
    private const int MaxJobTypeLength = 100;
    
    /// <summary>Valid job type pattern (alphanumeric, underscore, hyphen).</summary>
    private static readonly Regex ValidJobTypePattern = new(@"^[a-zA-Z0-9_-]+$", RegexOptions.Compiled);

    #endregion

    #region Fields

    private readonly ConcurrentDictionary<string, Job> _jobs = new();
    private readonly ConcurrentDictionary<string, DateTime> _jobTimestamps = new();
    private readonly ConcurrentQueue<string> _queue = new();
    private readonly ILogger<JobService> _logger;
    private readonly Timer _cleanupTimer;
    private bool _disposed;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the JobService with automatic cleanup support.
    /// </summary>
    /// <param name="logger">Logger for job operations and diagnostics.</param>
    /// <exception cref="ArgumentNullException">Thrown when logger is null.</exception>
    public JobService(ILogger<JobService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        // Initialize cleanup timer to run periodically
        _cleanupTimer = new Timer(CleanupExpiredJobs, null, CleanupInterval, CleanupInterval);
        
        _logger.LogInformation("JobService initialized with automatic cleanup (interval: {CleanupInterval})", CleanupInterval);
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Creates a new job and adds it to the processing queue.
    /// </summary>
    /// <param name="type">The type of job to create (e.g., "INGEST", "METADATA_FETCH", "THUMBNAIL_GEN").</param>
    /// <returns>The newly created job with QUEUED status.</returns>
    /// <exception cref="ArgumentException">Thrown when type is null, empty, or invalid format.</exception>
    /// <exception cref="InvalidOperationException">Thrown when job creation fails or system limit reached.</exception>
    public Job CreateJob(string type)
    {
        // Validate input parameters
        if (string.IsNullOrWhiteSpace(type))
        {
            _logger.LogError("Job creation failed: Job type is null or whitespace");
            throw new ArgumentException("Job type cannot be null or empty", nameof(type));
        }

        // Validate job type format to prevent injection attacks
        if (type.Length > MaxJobTypeLength)
        {
            _logger.LogError("Job creation failed: Job type exceeds maximum length ({MaxLength}): {JobType}", 
                MaxJobTypeLength, type);
            throw new ArgumentException($"Job type cannot exceed {MaxJobTypeLength} characters", nameof(type));
        }

        if (!ValidJobTypePattern.IsMatch(type))
        {
            _logger.LogError("Job creation failed: Invalid job type format: {JobType}", type);
            throw new ArgumentException("Job type contains invalid characters. Only alphanumeric, underscore, and hyphen allowed", nameof(type));
        }

        // Check system limits to prevent resource exhaustion
        if (_jobs.Count >= MaxJobsLimit)
        {
            _logger.LogError("Job creation failed: System job limit reached ({MaxJobs}). Consider increasing cleanup frequency", 
                MaxJobsLimit);
            throw new InvalidOperationException($"Cannot create job: System limit of {MaxJobsLimit} jobs reached");
        }

        // Create job with unique identifier
        var jobId = Guid.NewGuid().ToString();
        var job = new Job(
            jobId,
            type,
            "QUEUED",
            0,
            null,
            null
        );
        
        // Add to collections with concurrent safety
        if (!_jobs.TryAdd(job.id, job))
        {
            _logger.LogError("Job creation failed: Unable to add job {JobId} - concurrent modification conflict", jobId);
            throw new InvalidOperationException("Failed to create job due to concurrent modification");
        }

        // Track creation timestamp for expiration management
        _jobTimestamps.TryAdd(job.id, DateTime.UtcNow);
        
        // Add to processing queue
        _queue.Enqueue(job.id);
        
        _logger.LogInformation("Created job {JobId} of type '{JobType}'. Queue depth: {QueueDepth}, Total jobs: {TotalJobs}", 
            job.id, type, _queue.Count, _jobs.Count);
        
        return job;
    }

    /// <summary>
    /// Retrieves a job by its unique identifier.
    /// </summary>
    /// <param name="id">The job identifier (GUID format).</param>
    /// <returns>The job if found; otherwise null.</returns>
    public Job? GetJob(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            _logger.LogWarning("GetJob called with null or empty ID");
            return null;
        }

        var found = _jobs.TryGetValue(id, out var job);
        
        if (!found)
        {
            _logger.LogDebug("Job not found: {JobId}", id);
            return null;
        }

        _logger.LogDebug("Retrieved job {JobId} (type: {JobType}, status: {Status}, progress: {Progress}%)", 
            job!.id, job.type, job.status, job.progress_percentage);
        
        return job;
    }

    /// <summary>
    /// Retrieves all jobs ordered by status priority (PROCESSING > QUEUED > COMPLETED > FAILED > CANCELLED).
    /// </summary>
    /// <param name="limit">Maximum number of jobs to return. Defaults to 20. Must be positive.</param>
    /// <returns>Collection of jobs ordered by processing priority and timestamp.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when limit is less than 1.</exception>
    public IEnumerable<Job> GetAllJobs(int limit = DefaultJobLimit)
    {
        if (limit < 1)
        {
            _logger.LogWarning("GetAllJobs called with invalid limit: {Limit}. Using default: {DefaultLimit}", 
                limit, DefaultJobLimit);
            limit = DefaultJobLimit;
        }

        var totalJobs = _jobs.Count;
        _logger.LogDebug("Retrieving jobs (limit: {Limit}, total in system: {Total})", limit, totalJobs);
        
        return _jobs.Values
            .OrderByDescending(j => GetStatusPriority(j.status))
            .ThenByDescending(j => _jobTimestamps.TryGetValue(j.id, out var timestamp) ? timestamp : DateTime.MinValue)
            .Take(limit);
    }

    /// <summary>
    /// Updates an existing job's status, progress, and optional result or error details.
    /// </summary>
    /// <param name="id">The job identifier.</param>
    /// <param name="status">New status (must be valid: QUEUED, PROCESSING, COMPLETED, FAILED, CANCELLED).</param>
    /// <param name="progress">Progress percentage (must be 0-100 inclusive).</param>
    /// <param name="resultUrn">Optional URN of the job result (for COMPLETED status).</param>
    /// <param name="error">Optional error details (for FAILED status).</param>
    /// <exception cref="ArgumentException">Thrown when status or progress is invalid.</exception>
    public void UpdateJob(string id, string status, int progress, string? resultUrn = null, string? error = null)
    {
        // Validate input parameters
        if (string.IsNullOrWhiteSpace(id))
        {
            _logger.LogWarning("UpdateJob called with null or empty ID");
            return;
        }

        if (string.IsNullOrWhiteSpace(status))
        {
            _logger.LogError("UpdateJob failed for job {JobId}: Status cannot be null or empty", id);
            throw new ArgumentException("Status cannot be null or empty", nameof(status));
        }

        if (progress < 0 || progress > 100)
        {
            _logger.LogError("UpdateJob failed for job {JobId}: Invalid progress value {Progress}. Must be 0-100", 
                id, progress);
            throw new ArgumentException("Progress must be between 0 and 100", nameof(progress));
        }

        // Validate status is one of the allowed values
        var validStatuses = new[] { "QUEUED", "PROCESSING", "COMPLETED", "FAILED", "CANCELLED" };
        if (!validStatuses.Contains(status))
        {
            _logger.LogError("UpdateJob failed for job {JobId}: Invalid status '{Status}'. Must be one of: {ValidStatuses}", 
                id, status, string.Join(", ", validStatuses));
            throw new ArgumentException($"Status must be one of: {string.Join(", ", validStatuses)}", nameof(status));
        }

        // Attempt to retrieve the job
        if (!_jobs.TryGetValue(id, out var job))
        {
            _logger.LogWarning("Attempted to update non-existent job: {JobId}", id);
            return;
        }

        // Create updated job record
        var updated = job with { 
            status = status, 
            progress_percentage = progress, 
            result_urn = resultUrn, 
            error_details = error 
        };
        
        // Attempt update with optimistic concurrency
        if (_jobs.TryUpdate(id, updated, job))
        {
            // Update timestamp if terminal state reached
            if (status == "COMPLETED" || status == "FAILED" || status == "CANCELLED")
            {
                _jobTimestamps.TryUpdate(id, DateTime.UtcNow, _jobTimestamps.TryGetValue(id, out var ts) ? ts : DateTime.UtcNow);
            }
            
            LogJobUpdate(id, job.type, status, progress, resultUrn, error);
        }
        else
        {
            _logger.LogWarning("Failed to update job {JobId} - concurrency conflict. Job may have been modified by another thread", id);
        }
    }

    /// <summary>
    /// Cancels a job if it's still in QUEUED or PROCESSING state.
    /// </summary>
    /// <param name="id">The job identifier to cancel.</param>
    /// <returns>True if the job was successfully cancelled; false if not found or already in terminal state.</returns>
    public bool CancelJob(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            _logger.LogWarning("CancelJob called with null or empty ID");
            return false;
        }

        if (!_jobs.TryGetValue(id, out var job))
        {
            _logger.LogWarning("Cannot cancel non-existent job: {JobId}", id);
            return false;
        }

        // Can only cancel jobs that aren't in terminal states
        if (job.status == "COMPLETED" || job.status == "FAILED" || job.status == "CANCELLED")
        {
            _logger.LogInformation("Cannot cancel job {JobId} - already in terminal state: {Status}", id, job.status);
            return false;
        }

        var cancelled = job with { 
            status = "CANCELLED", 
            error_details = "Job cancelled by user request" 
        };

        if (_jobs.TryUpdate(id, cancelled, job))
        {
            _jobTimestamps.TryUpdate(id, DateTime.UtcNow, _jobTimestamps.TryGetValue(id, out var ts) ? ts : DateTime.UtcNow);
            _logger.LogInformation("Cancelled job {JobId} (type: {JobType}, was at {Progress}% progress)", 
                id, job.type, job.progress_percentage);
            return true;
        }

        _logger.LogWarning("Failed to cancel job {JobId} - concurrent modification occurred", id);
        return false;
    }

    /// <summary>
    /// Attempts to dequeue the next job from the processing queue.
    /// </summary>
    /// <param name="jobId">Outputs the job ID if dequeue successful.</param>
    /// <returns>True if a job was dequeued; otherwise false.</returns>
    public bool TryDequeue(out string? jobId)
    {
        var result = _queue.TryDequeue(out jobId);
        
        if (result && jobId != null)
        {
            _logger.LogDebug("Dequeued job {JobId}. Remaining queue depth: {QueueDepth}", jobId, _queue.Count);
        }
        else
        {
            _logger.LogDebug("Queue is empty, no job to dequeue");
        }
        
        return result;
    }

    /// <summary>
    /// Gets the current queue depth (number of jobs waiting to be processed).
    /// </summary>
    /// <returns>Number of jobs in the queue.</returns>
    public int GetQueueDepth()
    {
        var depth = _queue.Count;
        _logger.LogDebug("Current queue depth: {QueueDepth}", depth);
        return depth;
    }

    /// <summary>
    /// Gets statistics about jobs in the system.
    /// </summary>
    /// <returns>Dictionary with job counts by status.</returns>
    public Dictionary<string, int> GetJobStatistics()
    {
        var stats = _jobs.Values
            .GroupBy(j => j.status)
            .ToDictionary(g => g.Key, g => g.Count());
        
        stats["Total"] = _jobs.Count;
        stats["QueueDepth"] = _queue.Count;
        
        _logger.LogDebug("Job statistics: {Statistics}", string.Join(", ", stats.Select(kvp => $"{kvp.Key}={kvp.Value}")));
        
        return stats;
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Determines the priority order for job status (higher = more important).
    /// </summary>
    /// <param name="status">Job status string.</param>
    /// <returns>Priority value for sorting.</returns>
    private static int GetStatusPriority(string status) => status switch
    {
        "PROCESSING" => 4,
        "QUEUED" => 3,
        "COMPLETED" => 2,
        "FAILED" => 1,
        "CANCELLED" => 0,
        _ => -1
    };

    /// <summary>
    /// Logs job update events with appropriate log levels based on status and progress.
    /// </summary>
    /// <param name="id">Job identifier.</param>
    /// <param name="type">Job type.</param>
    /// <param name="status">New job status.</param>
    /// <param name="progress">Progress percentage.</param>
    /// <param name="resultUrn">Result URN if applicable.</param>
    /// <param name="error">Error details if applicable.</param>
    private void LogJobUpdate(string id, string type, string status, int progress, string? resultUrn, string? error)
    {
        if (status == "FAILED" || !string.IsNullOrEmpty(error))
        {
            _logger.LogError("Job {JobId} (type: '{JobType}') FAILED at {Progress}%: {Error}", 
                id, type, progress, error ?? "Unknown error");
        }
        else if (status == "COMPLETED")
        {
            _logger.LogInformation("Job {JobId} (type: '{JobType}') COMPLETED successfully. Result: {ResultUrn}", 
                id, type, resultUrn ?? "none");
        }
        else if (status == "CANCELLED")
        {
            _logger.LogInformation("Job {JobId} (type: '{JobType}') CANCELLED at {Progress}%", 
                id, type, progress);
        }
        else if (status == "PROCESSING" && progress % ProgressLogInterval == 0)
        {
            _logger.LogInformation("Job {JobId} (type: '{JobType}') progress: {Progress}%", 
                id, type, progress);
        }
        else if (status == "PROCESSING")
        {
            _logger.LogDebug("Job {JobId} progress: {Progress}%", id, progress);
        }
        else if (status == "QUEUED")
        {
            _logger.LogDebug("Job {JobId} set to QUEUED status", id);
        }
    }

    /// <summary>
    /// Cleanup expired jobs to prevent unbounded memory growth.
    /// Automatically called by timer on regular intervals.
    /// </summary>
    /// <param name="state">Timer state (unused).</param>
    private void CleanupExpiredJobs(object? state)
    {
        try
        {
            var now = DateTime.UtcNow;
            var expiredJobs = _jobTimestamps
                .Where(kvp => now - kvp.Value > JobExpirationTime)
                .Select(kvp => kvp.Key)
                .ToList();

            if (!expiredJobs.Any())
            {
                _logger.LogDebug("Cleanup: No expired jobs found");
                return;
            }

            var removedCount = 0;
            foreach (var jobId in expiredJobs)
            {
                // Get timestamp before potential removal
                var jobAge = _jobTimestamps.TryGetValue(jobId, out var timestamp) 
                    ? now - timestamp 
                    : TimeSpan.Zero;

                // Only remove jobs in terminal states
                if (_jobs.TryGetValue(jobId, out var job))
                {
                    if (job.status == "COMPLETED" || job.status == "FAILED" || job.status == "CANCELLED")
                    {
                        if (_jobs.TryRemove(jobId, out _))
                        {
                            _jobTimestamps.TryRemove(jobId, out _);
                            removedCount++;
                            _logger.LogDebug("Removed expired job {JobId} (type: {JobType}, status: {Status}, age: {Age})", 
                                jobId, job.type, job.status, jobAge);
                        }
                    }
                }
            }

            if (removedCount > 0)
            {
                _logger.LogInformation("Cleanup completed: Removed {RemovedCount} expired jobs. Remaining jobs: {RemainingJobs}", 
                    removedCount, _jobs.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during job cleanup operation");
        }
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// Disposes resources used by the JobService.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _logger.LogInformation("Disposing JobService. Final statistics: Total jobs: {TotalJobs}, Queue depth: {QueueDepth}", 
            _jobs.Count, _queue.Count);

        _cleanupTimer?.Dispose();
        _jobs.Clear();
        _jobTimestamps.Clear();
        
        // Clear queue
        while (_queue.TryDequeue(out _)) { }

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    #endregion
}
