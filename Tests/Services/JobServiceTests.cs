using MehguViewer.Core.Services;
using MehguViewer.Core.Workers;
using MehguViewer.Core.Helpers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MehguViewer.Core.Tests.Services;

/// <summary>
/// Unit tests for JobService.
/// Tests job creation, updates, queue management, validation, and cleanup.
/// </summary>
[Trait("Category", "Unit")]
[Trait("Service", "JobService")]
public class JobServiceTests : IDisposable
{
    private readonly JobService _jobService;

    public JobServiceTests()
    {
        // Use NullLogger for tests to avoid log output during test runs
        var logger = NullLogger<JobService>.Instance;
        _jobService = new JobService(logger);
    }

    public void Dispose()
    {
        _jobService?.Dispose();
        GC.SuppressFinalize(this);
    }

    #region CreateJob

    [Fact]
    public void CreateJob_WithValidType_ReturnsJob()
    {
        // Arrange
        var jobType = "ingestion";

        // Act
        var job = _jobService.CreateJob(jobType);

        // Assert
        Assert.NotNull(job);
        Assert.Equal(jobType, job.type);
        Assert.Equal("QUEUED", job.status);
        Assert.Equal(0, job.progress_percentage);
        Assert.NotEmpty(job.id);
    }

    [Fact]
    public void CreateJob_WithNullType_ThrowsArgumentException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => _jobService.CreateJob(null!));
        Assert.Contains("Job type cannot be null or empty", exception.Message);
    }

    [Fact]
    public void CreateJob_WithEmptyType_ThrowsArgumentException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => _jobService.CreateJob(string.Empty));
        Assert.Contains("Job type cannot be null or empty", exception.Message);
    }

    [Fact]
    public void CreateJob_WithWhitespaceType_ThrowsArgumentException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => _jobService.CreateJob("   "));
        Assert.Contains("Job type cannot be null or empty", exception.Message);
    }

    [Fact]
    public void CreateJob_WithInvalidCharacters_ThrowsArgumentException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => _jobService.CreateJob("test@type!"));
        Assert.Contains("invalid characters", exception.Message);
    }

    [Fact]
    public void CreateJob_WithTooLongType_ThrowsArgumentException()
    {
        // Arrange
        var longType = new string('A', 101); // Exceeds MaxJobTypeLength of 100

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => _jobService.CreateJob(longType));
        Assert.Contains("cannot exceed", exception.Message);
    }

    [Fact]
    public void CreateJob_WithValidTypesVariations_Succeeds()
    {
        // Arrange & Act
        var job1 = _jobService.CreateJob("INGEST");
        var job2 = _jobService.CreateJob("metadata-fetch");
        var job3 = _jobService.CreateJob("thumbnail_gen");
        var job4 = _jobService.CreateJob("Test123-Type_99");

        // Assert
        Assert.NotNull(job1);
        Assert.NotNull(job2);
        Assert.NotNull(job3);
        Assert.NotNull(job4);
    }

    [Fact]
    public void CreateJob_CreatesUniqueJobs()
    {
        // Act
        var job1 = _jobService.CreateJob("type1");
        var job2 = _jobService.CreateJob("type2");
        var job3 = _jobService.CreateJob("type3");

        // Assert
        Assert.NotEqual(job1.id, job2.id);
        Assert.NotEqual(job2.id, job3.id);
        Assert.NotEqual(job1.id, job3.id);
    }

    #endregion

    #region GetJob

    [Fact]
    public void GetJob_ExistingJob_ReturnsJob()
    {
        // Arrange
        var createdJob = _jobService.CreateJob("test");

        // Act
        var retrievedJob = _jobService.GetJob(createdJob.id);

        // Assert
        Assert.NotNull(retrievedJob);
        Assert.Equal(createdJob.id, retrievedJob.id);
        Assert.Equal(createdJob.type, retrievedJob.type);
    }

    [Fact]
    public void GetJob_NonExistentJob_ReturnsNull()
    {
        // Act
        var job = _jobService.GetJob("non-existent-id");

        // Assert
        Assert.Null(job);
    }

    [Fact]
    public void GetJob_NullId_ReturnsNull()
    {
        // Act
        var job = _jobService.GetJob(null!);

        // Assert
        Assert.Null(job);
    }

    [Fact]
    public void GetJob_EmptyId_ReturnsNull()
    {
        // Act
        var job = _jobService.GetJob(string.Empty);

        // Assert
        Assert.Null(job);
    }

    #endregion

    #region UpdateJob

    [Fact]
    public void UpdateJob_ExistingJob_UpdatesStatus()
    {
        // Arrange
        var job = _jobService.CreateJob("test");

        // Act
        _jobService.UpdateJob(job.id, "PROCESSING", 50);
        var updatedJob = _jobService.GetJob(job.id);

        // Assert
        Assert.NotNull(updatedJob);
        Assert.Equal("PROCESSING", updatedJob.status);
        Assert.Equal(50, updatedJob.progress_percentage);
    }

    [Fact]
    public void UpdateJob_WithResult_UpdatesResultUrn()
    {
        // Arrange
        var job = _jobService.CreateJob("test");
        var resultUrn = "urn:mvn:series:12345";

        // Act
        _jobService.UpdateJob(job.id, "COMPLETED", 100, resultUrn);
        var updatedJob = _jobService.GetJob(job.id);

        // Assert
        Assert.NotNull(updatedJob);
        Assert.Equal("COMPLETED", updatedJob.status);
        Assert.Equal(resultUrn, updatedJob.result_urn);
    }

    [Fact]
    public void UpdateJob_WithError_UpdatesErrorDetails()
    {
        // Arrange
        var job = _jobService.CreateJob("test");
        var errorMessage = "Failed to process";

        // Act
        _jobService.UpdateJob(job.id, "FAILED", 75, null, errorMessage);
        var updatedJob = _jobService.GetJob(job.id);

        // Assert
        Assert.NotNull(updatedJob);
        Assert.Equal("FAILED", updatedJob.status);
        Assert.Equal(errorMessage, updatedJob.error_details);
    }

    [Fact]
    public void UpdateJob_NonExistentJob_DoesNotThrow()
    {
        // Act & Assert - Should not throw
        _jobService.UpdateJob("non-existent", "PROCESSING", 50);
    }

    [Fact]
    public void UpdateJob_WithInvalidStatus_ThrowsArgumentException()
    {
        // Arrange
        var job = _jobService.CreateJob("test");

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => 
            _jobService.UpdateJob(job.id, "INVALID_STATUS", 50));
        Assert.Contains("Status must be one of", exception.Message);
    }

    [Fact]
    public void UpdateJob_WithNegativeProgress_ThrowsArgumentException()
    {
        // Arrange
        var job = _jobService.CreateJob("test");

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => 
            _jobService.UpdateJob(job.id, "PROCESSING", -10));
        Assert.Contains("Progress must be between 0 and 100", exception.Message);
    }

    [Fact]
    public void UpdateJob_WithProgressOver100_ThrowsArgumentException()
    {
        // Arrange
        var job = _jobService.CreateJob("test");

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => 
            _jobService.UpdateJob(job.id, "PROCESSING", 150));
        Assert.Contains("Progress must be between 0 and 100", exception.Message);
    }

    [Fact]
    public void UpdateJob_WithNullStatus_ThrowsArgumentException()
    {
        // Arrange
        var job = _jobService.CreateJob("test");

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => 
            _jobService.UpdateJob(job.id, null!, 50));
        Assert.Contains("Status cannot be null or empty", exception.Message);
    }

    [Fact]
    public void UpdateJob_AllValidStatuses_Succeeds()
    {
        // Arrange
        var validStatuses = new[] { "QUEUED", "PROCESSING", "COMPLETED", "FAILED", "CANCELLED" };

        foreach (var status in validStatuses)
        {
            var job = _jobService.CreateJob("test");

            // Act
            _jobService.UpdateJob(job.id, status, 100);
            var updated = _jobService.GetJob(job.id);

            // Assert
            Assert.NotNull(updated);
            Assert.Equal(status, updated.status);
        }
    }

    #endregion

    #region CancelJob

    [Fact]
    public void CancelJob_QueuedJob_CancelsSuccessfully()
    {
        // Arrange
        var job = _jobService.CreateJob("test");
        Assert.Equal("QUEUED", job.status);

        // Act
        var result = _jobService.CancelJob(job.id);
        var cancelled = _jobService.GetJob(job.id);

        // Assert
        Assert.True(result);
        Assert.NotNull(cancelled);
        Assert.Equal("CANCELLED", cancelled.status);
        Assert.Contains("cancelled by user", cancelled.error_details);
    }

    [Fact]
    public void CancelJob_ProcessingJob_CancelsSuccessfully()
    {
        // Arrange
        var job = _jobService.CreateJob("test");
        _jobService.UpdateJob(job.id, "PROCESSING", 50);

        // Act
        var result = _jobService.CancelJob(job.id);
        var cancelled = _jobService.GetJob(job.id);

        // Assert
        Assert.True(result);
        Assert.NotNull(cancelled);
        Assert.Equal("CANCELLED", cancelled.status);
    }

    [Fact]
    public void CancelJob_CompletedJob_ReturnsFalse()
    {
        // Arrange
        var job = _jobService.CreateJob("test");
        _jobService.UpdateJob(job.id, "COMPLETED", 100, "urn:result");

        // Act
        var result = _jobService.CancelJob(job.id);
        var completed = _jobService.GetJob(job.id);

        // Assert
        Assert.False(result);
        Assert.NotNull(completed);
        Assert.Equal("COMPLETED", completed.status);
    }

    [Fact]
    public void CancelJob_FailedJob_ReturnsFalse()
    {
        // Arrange
        var job = _jobService.CreateJob("test");
        _jobService.UpdateJob(job.id, "FAILED", 75, null, "Error occurred");

        // Act
        var result = _jobService.CancelJob(job.id);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void CancelJob_AlreadyCancelledJob_ReturnsFalse()
    {
        // Arrange
        var job = _jobService.CreateJob("test");
        _jobService.CancelJob(job.id);

        // Act
        var result = _jobService.CancelJob(job.id);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void CancelJob_NonExistentJob_ReturnsFalse()
    {
        // Act
        var result = _jobService.CancelJob("non-existent");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void CancelJob_NullId_ReturnsFalse()
    {
        // Act
        var result = _jobService.CancelJob(null!);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region GetAllJobs

    [Fact]
    public void GetAllJobs_WithMultipleJobs_ReturnsInCorrectOrder()
    {
        // Arrange
        var job1 = _jobService.CreateJob("type1");
        var job2 = _jobService.CreateJob("type2");
        var job3 = _jobService.CreateJob("type3");

        _jobService.UpdateJob(job2.id, "PROCESSING", 50);

        // Act
        var jobs = _jobService.GetAllJobs(10).ToList();

        // Assert
        Assert.True(jobs.Count >= 3);
        // Processing jobs should come first
        Assert.Equal("PROCESSING", jobs[0].status);
    }

    [Fact]
    public void GetAllJobs_OrdersByStatusPriority_Correctly()
    {
        // Arrange
        var completed = _jobService.CreateJob("completed");
        _jobService.UpdateJob(completed.id, "COMPLETED", 100);

        var processing = _jobService.CreateJob("processing");
        _jobService.UpdateJob(processing.id, "PROCESSING", 50);

        var queued = _jobService.CreateJob("queued");

        var failed = _jobService.CreateJob("failed");
        _jobService.UpdateJob(failed.id, "FAILED", 80, null, "Error");

        var cancelled = _jobService.CreateJob("cancelled");
        _jobService.UpdateJob(cancelled.id, "CANCELLED", 30);

        // Act
        var jobs = _jobService.GetAllJobs(10).ToList();

        // Assert - Order should be PROCESSING > QUEUED > COMPLETED > FAILED > CANCELLED
        Assert.Equal("PROCESSING", jobs[0].status);
        Assert.Equal("QUEUED", jobs[1].status);
        Assert.Equal("COMPLETED", jobs[2].status);
        Assert.Equal("FAILED", jobs[3].status);
        Assert.Equal("CANCELLED", jobs[4].status);
    }

    [Fact]
    public void GetAllJobs_WithLimit_ReturnsCorrectCount()
    {
        // Arrange
        for (int i = 0; i < 30; i++)
        {
            _jobService.CreateJob($"type{i}");
        }

        // Act
        var jobs = _jobService.GetAllJobs(20).ToList();

        // Assert
        Assert.Equal(20, jobs.Count);
    }

    [Fact]
    public void GetAllJobs_NoJobs_ReturnsEmpty()
    {
        // Arrange
        using var emptyService = new JobService(NullLogger<JobService>.Instance);

        // Act
        var jobs = emptyService.GetAllJobs().ToList();

        // Assert
        Assert.Empty(jobs);
    }

    [Fact]
    public void GetAllJobs_WithInvalidLimit_UsesDefault()
    {
        // Arrange
        for (int i = 0; i < 5; i++)
        {
            _jobService.CreateJob($"type{i}");
        }

        // Act - negative limit should use default
        var jobs = _jobService.GetAllJobs(-1).ToList();

        // Assert
        Assert.Equal(5, jobs.Count); // All 5 jobs returned since less than default limit
    }

    #endregion

    #region GetQueueDepth

    [Fact]
    public void GetQueueDepth_EmptyQueue_ReturnsZero()
    {
        // Act
        var depth = _jobService.GetQueueDepth();

        // Assert
        Assert.Equal(0, depth);
    }

    [Fact]
    public void GetQueueDepth_AfterCreatingJobs_ReturnsCorrectCount()
    {
        // Arrange
        _jobService.CreateJob("job1");
        _jobService.CreateJob("job2");
        _jobService.CreateJob("job3");

        // Act
        var depth = _jobService.GetQueueDepth();

        // Assert
        Assert.Equal(3, depth);
    }

    [Fact]
    public void GetQueueDepth_AfterDequeue_DecreasesCorrectly()
    {
        // Arrange
        _jobService.CreateJob("job1");
        _jobService.CreateJob("job2");
        _jobService.TryDequeue(out _);

        // Act
        var depth = _jobService.GetQueueDepth();

        // Assert
        Assert.Equal(1, depth);
    }

    #endregion

    #region GetJobStatistics

    [Fact]
    public void GetJobStatistics_EmptyService_ReturnsZeros()
    {
        // Arrange
        using var emptyService = new JobService(NullLogger<JobService>.Instance);

        // Act
        var stats = emptyService.GetJobStatistics();

        // Assert
        Assert.Equal(0, stats["Total"]);
        Assert.Equal(0, stats["QueueDepth"]);
    }

    [Fact]
    public void GetJobStatistics_WithVariousJobs_ReturnsCorrectCounts()
    {
        // Arrange
        var job1 = _jobService.CreateJob("type1");
        var job2 = _jobService.CreateJob("type2");
        var job3 = _jobService.CreateJob("type3");
        var job4 = _jobService.CreateJob("type4");

        _jobService.UpdateJob(job1.id, "PROCESSING", 50);
        _jobService.UpdateJob(job2.id, "COMPLETED", 100);
        _jobService.UpdateJob(job3.id, "FAILED", 80, null, "Error");
        // job4 remains QUEUED

        // Act
        var stats = _jobService.GetJobStatistics();

        // Assert
        Assert.Equal(4, stats["Total"]);
        Assert.Equal(1, stats["PROCESSING"]);
        Assert.Equal(1, stats["COMPLETED"]);
        Assert.Equal(1, stats["FAILED"]);
        Assert.Equal(1, stats["QUEUED"]);
    }

    #endregion

    #region TryDequeue

    [Fact]
    public void TryDequeue_WithQueuedJobs_ReturnsJob()
    {
        // Arrange
        var job = _jobService.CreateJob("test");

        // Act
        var result = _jobService.TryDequeue(out var jobId);

        // Assert
        Assert.True(result);
        Assert.Equal(job.id, jobId);
    }

    [Fact]
    public void TryDequeue_EmptyQueue_ReturnsFalse()
    {
        // Arrange
        using var emptyService = new JobService(NullLogger<JobService>.Instance);

        // Act
        var result = emptyService.TryDequeue(out var jobId);

        // Assert
        Assert.False(result);
        Assert.Null(jobId);
    }

    [Fact]
    public void TryDequeue_PreservesOrder()
    {
        // Arrange
        var job1 = _jobService.CreateJob("first");
        var job2 = _jobService.CreateJob("second");
        var job3 = _jobService.CreateJob("third");

        // Act
        _jobService.TryDequeue(out var id1);
        _jobService.TryDequeue(out var id2);
        _jobService.TryDequeue(out var id3);

        // Assert
        Assert.Equal(job1.id, id1);
        Assert.Equal(job2.id, id2);
        Assert.Equal(job3.id, id3);
    }

    #endregion

    #region Edge Cases and Concurrency

    [Fact]
    public async Task ConcurrentJobCreation_AllSucceed()
    {
        // Arrange
        var tasks = new List<Task<MehguViewer.Core.Shared.Job>>();
        var jobCount = 100;

        // Act
        for (int i = 0; i < jobCount; i++)
        {
            var index = i;
            tasks.Add(Task.Run(() => _jobService.CreateJob($"concurrent-{index}")));
        }

        await Task.WhenAll(tasks.ToArray());

        // Assert
        Assert.Equal(jobCount, tasks.Count);
        Assert.All(tasks, t => Assert.NotNull(t.Result));
        
        var jobIds = tasks.Select(t => t.Result.id).ToList();
        Assert.Equal(jobCount, jobIds.Distinct().Count()); // All unique IDs
    }

    [Fact]
    public async Task ConcurrentJobUpdates_AllSucceed()
    {
        // Arrange
        var job = _jobService.CreateJob("concurrent-update");
        var updateTasks = new List<Task>();

        // Act - Multiple threads updating the same job
        for (int i = 0; i <= 100; i += 10)
        {
            var progress = i;
            updateTasks.Add(Task.Run(() => 
                _jobService.UpdateJob(job.id, "PROCESSING", progress)));
        }

        await Task.WhenAll(updateTasks.ToArray());

        // Assert - Job should exist and have a valid state
        var updated = _jobService.GetJob(job.id);
        Assert.NotNull(updated);
        Assert.Equal("PROCESSING", updated.status);
        Assert.InRange(updated.progress_percentage, 0, 100);
    }

    [Fact]
    public void MultipleDequeues_ReturnDifferentJobs()
    {
        // Arrange
        _jobService.CreateJob("job1");
        _jobService.CreateJob("job2");
        _jobService.CreateJob("job3");

        // Act
        var success1 = _jobService.TryDequeue(out var id1);
        var success2 = _jobService.TryDequeue(out var id2);
        var success3 = _jobService.TryDequeue(out var id3);

        // Assert
        Assert.True(success1);
        Assert.True(success2);
        Assert.True(success3);
        Assert.NotEqual(id1, id2);
        Assert.NotEqual(id2, id3);
        Assert.NotEqual(id1, id3);
    }

    [Fact]
    public void GetJob_WithWhitespaceId_ReturnsNull()
    {
        // Act
        var result = _jobService.GetJob("   ");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void UpdateJob_WithWhitespaceId_DoesNotThrow()
    {
        // Act & Assert
        _jobService.UpdateJob("   ", "PROCESSING", 50);
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public void Dispose_MultipleCalls_DoesNotThrow()
    {
        // Arrange
        using var service = new JobService(NullLogger<JobService>.Instance);
        service.CreateJob("test");

        // Act & Assert
        service.Dispose();
        service.Dispose(); // Should not throw
    }

    [Fact]
    public void Dispose_ClearsAllJobs()
    {
        // Arrange
        var service = new JobService(NullLogger<JobService>.Instance);
        service.CreateJob("test1");
        service.CreateJob("test2");
        service.CreateJob("test3");

        // Act
        service.Dispose();

        // Note: Cannot verify state after dispose as service should not be used
        // This test verifies no exceptions are thrown during disposal
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void CompleteJobWorkflow_Success()
    {
        // Arrange
        var jobType = "test-workflow";
        var resultUrn = "urn:mvn:series:result";

        // Act
        var job = _jobService.CreateJob(jobType);
        Assert.Equal("QUEUED", job.status);

        _jobService.UpdateJob(job.id, "PROCESSING", 0);
        var processing = _jobService.GetJob(job.id);
        Assert.Equal("PROCESSING", processing!.status);

        _jobService.UpdateJob(job.id, "PROCESSING", 50);
        var halfway = _jobService.GetJob(job.id);
        Assert.Equal(50, halfway!.progress_percentage);

        _jobService.UpdateJob(job.id, "COMPLETED", 100, resultUrn);
        var completed = _jobService.GetJob(job.id);

        // Assert
        Assert.Equal("COMPLETED", completed!.status);
        Assert.Equal(100, completed.progress_percentage);
        Assert.Equal(resultUrn, completed.result_urn);
        Assert.Null(completed.error_details);
    }

    [Fact]
    public void CompleteJobWorkflow_Failure()
    {
        // Arrange
        var jobType = "test-workflow";
        var errorMessage = "Processing failed unexpectedly";

        // Act
        var job = _jobService.CreateJob(jobType);
        _jobService.UpdateJob(job.id, "PROCESSING", 0);
        _jobService.UpdateJob(job.id, "PROCESSING", 75);
        _jobService.UpdateJob(job.id, "FAILED", 75, null, errorMessage);
        var failed = _jobService.GetJob(job.id);

        // Assert
        Assert.Equal("FAILED", failed!.status);
        Assert.Equal(75, failed.progress_percentage);
        Assert.Equal(errorMessage, failed.error_details);
        Assert.Null(failed.result_urn);
    }

    [Fact]
    public void CompleteJobWorkflow_WithCancellation()
    {
        // Arrange
        var job = _jobService.CreateJob("cancellable-workflow");

        // Act
        _jobService.UpdateJob(job.id, "PROCESSING", 25);
        var cancelResult = _jobService.CancelJob(job.id);
        var cancelled = _jobService.GetJob(job.id);

        // Assert
        Assert.True(cancelResult);
        Assert.Equal("CANCELLED", cancelled!.status);
        Assert.Contains("cancelled", cancelled.error_details!.ToLower());
    }

    [Fact]
    public void CompleteJobWorkflow_QueueProcessing()
    {
        // Arrange
        var job1 = _jobService.CreateJob("workflow1");
        var job2 = _jobService.CreateJob("workflow2");
        var job3 = _jobService.CreateJob("workflow3");

        // Act - Simulate worker processing queue
        _jobService.TryDequeue(out var firstJobId);
        _jobService.UpdateJob(firstJobId!, "PROCESSING", 0);
        _jobService.UpdateJob(firstJobId!, "PROCESSING", 100);
        _jobService.UpdateJob(firstJobId!, "COMPLETED", 100, "urn:result:1");

        _jobService.TryDequeue(out var secondJobId);
        _jobService.UpdateJob(secondJobId!, "PROCESSING", 50);
        _jobService.UpdateJob(secondJobId!, "FAILED", 50, null, "Error in processing");

        // Assert
        Assert.Equal(job1.id, firstJobId);
        Assert.Equal(job2.id, secondJobId);

        var completedJob = _jobService.GetJob(firstJobId!);
        Assert.Equal("COMPLETED", completedJob!.status);

        var failedJob = _jobService.GetJob(secondJobId!);
        Assert.Equal("FAILED", failedJob!.status);

        var stats = _jobService.GetJobStatistics();
        Assert.Equal(1, stats["COMPLETED"]);
        Assert.Equal(1, stats["FAILED"]);
        Assert.Equal(1, stats["QUEUED"]); // job3 still in queue
    }

    [Fact]
    public void CompleteJobWorkflow_WithStatistics()
    {
        // Arrange & Act
        var ingest = _jobService.CreateJob("INGEST");
        var metadata = _jobService.CreateJob("METADATA_FETCH");
        var thumbnail = _jobService.CreateJob("THUMBNAIL_GEN");

        _jobService.UpdateJob(ingest.id, "PROCESSING", 50);
        _jobService.UpdateJob(metadata.id, "COMPLETED", 100, "urn:series:123");
        _jobService.UpdateJob(thumbnail.id, "FAILED", 75, null, "Image processing error");

        // Assert
        var stats = _jobService.GetJobStatistics();
        Assert.Equal(3, stats["Total"]);
        Assert.Equal(1, stats["PROCESSING"]);
        Assert.Equal(1, stats["COMPLETED"]);
        Assert.Equal(1, stats["FAILED"]);
        Assert.Equal(3, stats["QueueDepth"]); // All 3 jobs remain in queue until explicitly dequeued

        var jobs = _jobService.GetAllJobs(10).ToList();
        Assert.Equal(3, jobs.Count);
        Assert.Equal("PROCESSING", jobs[0].status); // Highest priority
    }

    #endregion
}
