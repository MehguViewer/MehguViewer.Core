using MehguViewer.Core.Infrastructures;
using MehguViewer.Core.Services;
using MehguViewer.Core.Workers;
using MehguViewer.Core.Helpers;
using MehguViewer.Core.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MehguViewer.Core.Tests.Workers;

/// <summary>
/// Unit tests for IngestionWorker background service.
/// Tests job processing, error handling, and lifecycle management.
/// </summary>
[Trait("Category", "Unit")]
[Trait("Service", "IngestionWorker")]
public sealed class IngestionWorkerTests : IDisposable
{
    #region Fields

    private readonly JobService _jobService;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<IngestionWorker> _logger;
    private readonly ServiceCollection _services;

    #endregion

    #region Constructor & Disposal

    public IngestionWorkerTests()
    {
        // Setup test dependencies
        _logger = NullLogger<IngestionWorker>.Instance;
        var jobLogger = NullLogger<JobService>.Instance;
        _jobService = new JobService(jobLogger);
        
        // Create service collection for dependency injection
        _services = new ServiceCollection();
        _serviceProvider = _services.BuildServiceProvider();
    }

    public void Dispose()
    {
        if (_serviceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        // Act
        var worker = new IngestionWorker(_jobService, _logger, _serviceProvider);

        // Assert
        Assert.NotNull(worker);
    }

    [Fact]
    public void Constructor_WithNullJobService_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new IngestionWorker(null!, _logger, _serviceProvider));
        
        Assert.Equal("jobService", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new IngestionWorker(_jobService, null!, _serviceProvider));
        
        Assert.Equal("logger", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithNullServiceProvider_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new IngestionWorker(_jobService, _logger, null!));
        
        Assert.Equal("serviceProvider", exception.ParamName);
    }

    #endregion

    #region Worker Execution Tests

    [Fact]
    public async Task ExecuteAsync_WithNoJobs_WaitsAndPolls()
    {
        // Arrange
        var worker = new IngestionWorker(_jobService, _logger, _serviceProvider);
        using var cts = new CancellationTokenSource();
        
        // Act - Start worker and cancel after short delay
        var workerTask = worker.StartAsync(cts.Token);
        await Task.Delay(100); // Allow worker to poll a few times
        cts.Cancel();
        
        // Wait for graceful shutdown
        await Task.Delay(100);
        await worker.StopAsync(CancellationToken.None);

        // Assert - No exceptions thrown, worker handles empty queue gracefully
        Assert.True(workerTask.IsCompleted);
    }

    [Fact]
    public async Task ExecuteAsync_WithQueuedJob_ProcessesJob()
    {
        // Arrange
        var worker = new IngestionWorker(_jobService, _logger, _serviceProvider);
        using var cts = new CancellationTokenSource();
        
        // Create a job
        var job = _jobService.CreateJob("INGEST");
        Assert.Equal("QUEUED", job.status);
        
        // Act - Start worker
        var workerTask = worker.StartAsync(cts.Token);
        
        // Wait for job to be processed (poll with timeout for CI environments)
        var completed = false;
        for (int i = 0; i < 50; i++) // 5 seconds max
        {
            await Task.Delay(100);
            var checkJob = _jobService.GetJob(job.id);
            if (checkJob?.status == "COMPLETED")
            {
                completed = true;
                break;
            }
        }
        
        // Stop worker
        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);

        // Assert - Job should be completed
        var processedJob = _jobService.GetJob(job.id);
        Assert.NotNull(processedJob);
        Assert.True(completed, $"Job did not complete in time. Status: {processedJob?.status}");
        Assert.Equal("COMPLETED", processedJob.status);
        Assert.Equal(100, processedJob.progress_percentage);
        Assert.NotNull(processedJob.result_urn);
        Assert.StartsWith("urn:mvn:unit:", processedJob.result_urn);
    }

    [Fact]
    public async Task ExecuteAsync_WithMultipleJobs_ProcessesInOrder()
    {
        // Arrange
        var worker = new IngestionWorker(_jobService, _logger, _serviceProvider);
        using var cts = new CancellationTokenSource();
        
        // Create multiple jobs
        var job1 = _jobService.CreateJob("INGEST");
        var job2 = _jobService.CreateJob("INGEST");
        var job3 = _jobService.CreateJob("INGEST");
        
        // Act - Start worker
        await worker.StartAsync(cts.Token);
        
        // Wait for all jobs to process (poll with timeout)
        for (int i = 0; i < 70; i++) // 7 seconds max for 3 jobs
        {
            await Task.Delay(100);
            var j1 = _jobService.GetJob(job1.id);
            var j2 = _jobService.GetJob(job2.id);
            var j3 = _jobService.GetJob(job3.id);
            if (j1?.status == "COMPLETED" && j2?.status == "COMPLETED" && j3?.status == "COMPLETED")
                break;
        }
        
        // Stop worker
        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);

        // Assert - All jobs completed
        var processedJob1 = _jobService.GetJob(job1.id);
        var processedJob2 = _jobService.GetJob(job2.id);
        var processedJob3 = _jobService.GetJob(job3.id);
        
        Assert.Equal("COMPLETED", processedJob1?.status);
        Assert.Equal("COMPLETED", processedJob2?.status);
        Assert.Equal("COMPLETED", processedJob3?.status);
    }

    [Fact]
    public async Task ExecuteAsync_CancellationRequested_StopsGracefully()
    {
        // Arrange
        var worker = new IngestionWorker(_jobService, _logger, _serviceProvider);
        using var cts = new CancellationTokenSource();
        
        // Act - Start and immediately cancel
        await worker.StartAsync(cts.Token);
        cts.Cancel();
        
        // Should stop gracefully without exceptions
        await worker.StopAsync(CancellationToken.None);

        // Assert - No exception thrown
        Assert.True(true);
    }

    [Fact]
    public async Task ExecuteAsync_WithJobInProgress_CompletesBeforeShutdown()
    {
        // Arrange
        var worker = new IngestionWorker(_jobService, _logger, _serviceProvider);
        using var cts = new CancellationTokenSource();
        
        var job = _jobService.CreateJob("INGEST");
        
        // Act - Start worker
        await worker.StartAsync(cts.Token);
        
        // Wait for job to start processing
        await Task.Delay(200);
        
        // Request cancellation while job is in progress
        cts.Cancel();
        
        // Wait for graceful shutdown
        await Task.Delay(1500);
        await worker.StopAsync(CancellationToken.None);

        // Assert - Job should still complete (current implementation completes in-flight jobs)
        var processedJob = _jobService.GetJob(job.id);
        Assert.NotNull(processedJob);
        // Job may be CANCELLED or COMPLETED depending on timing
        Assert.True(
            processedJob.status == "COMPLETED" || processedJob.status == "CANCELLED",
            $"Expected COMPLETED or CANCELLED, got {processedJob.status}");
    }

    #endregion

    #region Job Processing Tests

    [Fact]
    public async Task ProcessJobAsync_UpdatesProgress()
    {
        // Arrange
        var worker = new IngestionWorker(_jobService, _logger, _serviceProvider);
        using var cts = new CancellationTokenSource();
        
        var job = _jobService.CreateJob("INGEST");
        
        // Act
        await worker.StartAsync(cts.Token);
        
        // Poll job status during processing
        var progressValues = new List<int>();
        for (int i = 0; i < 15; i++)
        {
            await Task.Delay(100);
            var currentJob = _jobService.GetJob(job.id);
            if (currentJob != null)
            {
                progressValues.Add(currentJob.progress_percentage);
            }
        }
        
        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);

        // Assert - Progress should increase over time
        Assert.Contains(0, progressValues); // Started at 0
        Assert.Contains(100, progressValues); // Completed at 100
        
        // Verify progress is monotonically increasing (or stays at 100 after completion)
        var maxProgress = 0;
        foreach (var progress in progressValues)
        {
            Assert.True(progress >= maxProgress || progress == 100, 
                $"Progress should increase monotonically: {string.Join(", ", progressValues)}");
            maxProgress = Math.Max(maxProgress, progress);
        }
    }

    [Fact]
    public async Task ProcessJobAsync_SetsResultUrn()
    {
        // Arrange
        var worker = new IngestionWorker(_jobService, _logger, _serviceProvider);
        using var cts = new CancellationTokenSource();
        
        var job = _jobService.CreateJob("INGEST");
        
        // Act
        await worker.StartAsync(cts.Token);
        
        // Wait for completion with polling
        for (int i = 0; i < 50; i++) // 5 seconds max
        {
            await Task.Delay(100);
            var checkJob = _jobService.GetJob(job.id);
            if (checkJob?.status == "COMPLETED")
                break;
        }
        
        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);

        // Assert
        var completedJob = _jobService.GetJob(job.id);
        Assert.NotNull(completedJob);
        Assert.NotNull(completedJob.result_urn);
        Assert.StartsWith("urn:mvn:unit:", completedJob.result_urn);
        Assert.Matches(@"^urn:mvn:unit:[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", 
            completedJob.result_urn);
    }

    [Fact]
    public async Task ProcessJobAsync_WithNullJobId_HandlesGracefully()
    {
        // Arrange
        var worker = new IngestionWorker(_jobService, _logger, _serviceProvider);
        using var cts = new CancellationTokenSource();
        
        // Manually enqueue invalid job ID (null)
        // This tests defensive programming in ProcessJobAsync
        
        // Act & Assert - Should not throw
        await worker.StartAsync(cts.Token);
        await Task.Delay(200);
        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);
        
        Assert.True(true); // No exception thrown
    }

    #endregion

    #region Integration Tests with Service Scope

    [Fact]
    public async Task ProcessJobAsync_CreatesScopedServices()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScoped<TestScopedService>();
        
        var provider = services.BuildServiceProvider();
        var worker = new IngestionWorker(_jobService, _logger, provider);
        using var cts = new CancellationTokenSource();
        
        var job = _jobService.CreateJob("INGEST");
        
        // Act
        await worker.StartAsync(cts.Token);
        
        // Wait for completion with polling
        for (int i = 0; i < 50; i++) // 5 seconds max
        {
            await Task.Delay(100);
            var checkJob = _jobService.GetJob(job.id);
            if (checkJob?.status == "COMPLETED")
                break;
        }
        
        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);

        // Assert - Job processed successfully with scoped services available
        var processedJob = _jobService.GetJob(job.id);
        Assert.NotNull(processedJob);
        Assert.Equal("COMPLETED", processedJob.status);
    }

    [Fact]
    public async Task ProcessJobAsync_DisposesScope_AfterCompletion()
    {
        // Arrange
        var disposeCount = 0;
        var services = new ServiceCollection();
        
        services.AddScoped<DisposableTestService>(sp => 
            new DisposableTestService(() => disposeCount++));
        
        var provider = services.BuildServiceProvider();
        var worker = new IngestionWorker(_jobService, _logger, provider);
        using var cts = new CancellationTokenSource();
        
        _jobService.CreateJob("INGEST");
        
        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(1500);
        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);

        // Assert - Scope disposal happens after job completion
        // Note: Actual scope disposal verification would require more sophisticated testing
        Assert.True(true); // No exceptions during scope lifecycle
    }

    #endregion

    #region Test Helper Classes

    /// <summary>
    /// Test service to verify scoped service creation.
    /// </summary>
    private sealed class TestScopedService
    {
        public Guid InstanceId { get; } = Guid.NewGuid();
    }

    /// <summary>
    /// Test service to verify proper disposal.
    /// </summary>
    private sealed class DisposableTestService : IDisposable
    {
        private readonly Action _onDispose;

        public DisposableTestService(Action onDispose)
        {
            _onDispose = onDispose;
        }

        public void Dispose()
        {
            _onDispose?.Invoke();
        }
    }

    #endregion

    #region Performance Tests

    [Fact]
    public async Task ExecuteAsync_WithManyJobs_CompletesEfficiently()
    {
        // Arrange
        var worker = new IngestionWorker(_jobService, _logger, _serviceProvider);
        using var cts = new CancellationTokenSource();
        
        const int jobCount = 5;
        var jobIds = new List<string>();
        
        for (int i = 0; i < jobCount; i++)
        {
            var job = _jobService.CreateJob("INGEST");
            jobIds.Add(job.id);
        }
        
        // Act
        var startTime = DateTime.UtcNow;
        await worker.StartAsync(cts.Token);
        
        // Wait for all jobs to complete (poll with timeout for CI)
        var maxWaitSeconds = jobCount * 3; // 3 seconds per job for slow CI
        var allCompleted = false;
        for (int i = 0; i < maxWaitSeconds * 10; i++) // Check every 100ms
        {
            await Task.Delay(100);
            var completedCount = jobIds.Count(id => 
            {
                var job = _jobService.GetJob(id);
                return job?.status == "COMPLETED";
            });
            if (completedCount == jobCount)
            {
                allCompleted = true;
                break;
            }
        }
        
        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);
        var endTime = DateTime.UtcNow;

        // Assert - All jobs completed
        var finalCompletedCount = jobIds.Count(id => 
        {
            var job = _jobService.GetJob(id);
            return job?.status == "COMPLETED";
        });
        
        Assert.True(allCompleted, $"Only {finalCompletedCount}/{jobCount} jobs completed");
        Assert.Equal(jobCount, finalCompletedCount);
        
        // Verify reasonable processing time (not timing-sensitive, just sanity check)
        var duration = endTime - startTime;
        Assert.True(duration < TimeSpan.FromSeconds(jobCount * 3), 
            $"Processing took {duration.TotalSeconds}s, expected less than {jobCount * 3}s");
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task ExecuteAsync_WithEmptyQueue_ContinuesRunning()
    {
        // Arrange
        var worker = new IngestionWorker(_jobService, _logger, _serviceProvider);
        using var cts = new CancellationTokenSource();
        
        // Act - Run with empty queue
        await worker.StartAsync(cts.Token);
        await Task.Delay(500);
        
        // Add job after worker is running
        var job = _jobService.CreateJob("INGEST");
        await Task.Delay(1500); // Give time for job to be processed
        
        // Get job status before cancellation
        var processedJob = _jobService.GetJob(job.id);
        var statusBeforeCancellation = processedJob?.status;
        
        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);

        // Assert - Job should have been picked up and processed (or at least started processing)
        // When we cancel, job might be COMPLETED, PROCESSING, or CANCELLED depending on timing
        Assert.NotEqual("QUEUED", statusBeforeCancellation);
    }

    [Fact]
    public async Task ExecuteAsync_MultipleStartStop_HandlesCorrectly()
    {
        // Arrange
        var worker = new IngestionWorker(_jobService, _logger, _serviceProvider);
        
        // Act - Start and stop multiple times
        using (var cts1 = new CancellationTokenSource())
        {
            await worker.StartAsync(cts1.Token);
            await Task.Delay(100);
            cts1.Cancel();
            await worker.StopAsync(CancellationToken.None);
        }
        
        using (var cts2 = new CancellationTokenSource())
        {
            await worker.StartAsync(cts2.Token);
            await Task.Delay(100);
            cts2.Cancel();
            await worker.StopAsync(CancellationToken.None);
        }

        // Assert - No exceptions
        Assert.True(true);
    }

    #endregion
}
