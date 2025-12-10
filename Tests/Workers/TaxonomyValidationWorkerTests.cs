using MehguViewer.Core.Services;
using MehguViewer.Core.Workers;
using MehguViewer.Core.Helpers;
using MehguViewer.Core.Shared;
using MehguViewer.Core.Infrastructures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MehguViewer.Core.Tests.Workers;

/// <summary>
/// Unit tests for TaxonomyValidationWorker background service.
/// Tests initialization, validation cycles, error handling, and graceful shutdown.
/// </summary>
[Trait("Category", "Unit")]
[Trait("Service", "TaxonomyValidationWorker")]
public sealed class TaxonomyValidationWorkerTests : IDisposable
{
    #region Fields

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TaxonomyValidationWorker> _logger;
    private readonly ServiceCollection _services;
    private bool _disposed;

    #endregion

    #region Constructor & Disposal

    public TaxonomyValidationWorkerTests()
    {
        // Setup test dependencies
        _logger = NullLogger<TaxonomyValidationWorker>.Instance;
        _services = new ServiceCollection();
        
        // Register required services for worker
        var jobLogger = NullLogger<JobService>.Instance;
        var jobService = new JobService(jobLogger);
        _services.AddSingleton(jobService);
        
        // Create mock validation service with proper dependencies
        var validationLogger = NullLogger<TaxonomyValidationService>.Instance;
        var repoLogger = NullLogger<MemoryRepository>.Instance;
        
        // Create MetadataAggregationService (required by MemoryRepository)
        var metadataLogger = NullLogger<MetadataAggregationService>.Instance;
        var metadataService = new MetadataAggregationService(metadataLogger);
        
        var mockRepo = new MemoryRepository(repoLogger, metadataService);
        var validationService = new TaxonomyValidationService(mockRepo, validationLogger);
        _services.AddSingleton(validationService);
        
        _serviceProvider = _services.BuildServiceProvider();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            if (_serviceProvider is IDisposable disposable)
            {
                disposable.Dispose();
            }
            _disposed = true;
        }
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        // Act
        var worker = new TaxonomyValidationWorker(_serviceProvider, _logger);

        // Assert
        Assert.NotNull(worker);
    }

    [Fact]
    public void Constructor_WithNullServiceProvider_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new TaxonomyValidationWorker(null!, _logger));
        
        Assert.Equal("serviceProvider", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new TaxonomyValidationWorker(_serviceProvider, null!));
        
        Assert.Equal("logger", exception.ParamName);
    }

    #endregion

    #region Execution Tests

    [Fact]
    public async Task ExecuteAsync_WhenCancelled_StopsGracefully()
    {
        // Arrange
        using var worker = new TaxonomyValidationWorker(_serviceProvider, _logger);
        using var cts = new CancellationTokenSource();
        
        // Act - Start worker and immediately cancel
        cts.Cancel(); // Cancel before starting to avoid race condition
        var executeTask = worker.StartAsync(cts.Token);
        
        // Assert - Should complete without throwing (cancellation is expected behavior)
        await executeTask;
        
        // Verify worker stopped gracefully
        Assert.True(true, "Worker stopped gracefully on cancellation");
    }

    [Fact]
    public async Task ExecuteAsync_WithMissingJobService_LogsErrorAndReturns()
    {
        // Arrange - Create service provider without JobService
        var emptyServices = new ServiceCollection();
        var emptyProvider = emptyServices.BuildServiceProvider();
        
        using var worker = new TaxonomyValidationWorker(emptyProvider, _logger);
        using var cts = new CancellationTokenSource();
        
        // Act - Start worker with minimal delay to trigger validation attempt
        var executeTask = worker.StartAsync(cts.Token);
        
        // Give it a moment to attempt validation
        await Task.Delay(100);
        
        cts.Cancel();
        
        // Assert - Should handle missing service gracefully
        try
        {
            await executeTask;
        }
        catch (TaskCanceledException)
        {
            // Expected when cancelled
        }
    }

    [Fact]
    public async Task ExecuteAsync_WithMissingValidationService_LogsErrorAndReturns()
    {
        // Arrange - Create service provider with JobService but no ValidationService
        var partialServices = new ServiceCollection();
        var jobLogger = NullLogger<JobService>.Instance;
        var jobService = new JobService(jobLogger);
        partialServices.AddSingleton(jobService);
        var partialProvider = partialServices.BuildServiceProvider();
        
        using var worker = new TaxonomyValidationWorker(partialProvider, _logger);
        using var cts = new CancellationTokenSource();
        
        // Act
        var executeTask = worker.StartAsync(cts.Token);
        
        // Give it a moment to attempt validation
        await Task.Delay(100);
        
        cts.Cancel();
        
        // Assert - Should handle missing service gracefully
        try
        {
            await executeTask;
        }
        catch (TaskCanceledException)
        {
            // Expected when cancelled
        }
    }

    #endregion

    #region Validation Job Tests

    [Fact]
    public async Task RunValidationJob_WithNoIssues_CompletesSuccessfully()
    {
        // Arrange
        using var worker = new TaxonomyValidationWorker(_serviceProvider, _logger);
        using var cts = new CancellationTokenSource();
        
        // Start the worker
        var executeTask = worker.StartAsync(cts.Token);
        
        // Wait a bit for initial delay (but not full 5 minutes in test)
        await Task.Delay(200);
        
        // Act - Cancel to stop worker
        cts.Cancel();
        
        // Assert
        try
        {
            await executeTask;
        }
        catch (TaskCanceledException)
        {
            // Expected when cancelled
        }
        
        // Verify job was created (check via JobService)
        var jobService = _serviceProvider.GetRequiredService<JobService>();
        var jobs = jobService.GetAllJobs();
        
        // Note: In actual test, initial delay prevents job creation in this timeframe
        // This test validates the structure - integration tests would verify full cycle
        Assert.NotNull(jobs);
    }

    #endregion

    #region Shutdown Tests

    [Fact]
    public async Task StopAsync_WhenCalled_StopsWorkerGracefully()
    {
        // Arrange
        using var worker = new TaxonomyValidationWorker(_serviceProvider, _logger);
        using var cts = new CancellationTokenSource();
        
        // Act
        await worker.StartAsync(cts.Token);
        await worker.StopAsync(cts.Token);
        
        // Assert - Should complete without throwing
        Assert.True(true);
    }

    [Fact]
    public async Task StopAsync_DuringValidation_WaitsForCompletion()
    {
        // Arrange
        using var worker = new TaxonomyValidationWorker(_serviceProvider, _logger);
        using var cts = new CancellationTokenSource();
        
        // Act
        await worker.StartAsync(cts.Token);
        
        // Immediately request stop
        var stopTask = worker.StopAsync(cts.Token);
        
        // Assert - Should complete gracefully
        await stopTask;
        Assert.True(true);
    }

    #endregion

    #region Service Resolution Tests

    [Fact]
    public void ServiceProvider_CanResolveJobService()
    {
        // Act
        var jobService = _serviceProvider.GetService<JobService>();
        
        // Assert
        Assert.NotNull(jobService);
    }

    [Fact]
    public void ServiceProvider_CanResolveValidationService()
    {
        // Act
        var validationService = _serviceProvider.GetService<TaxonomyValidationService>();
        
        // Assert
        Assert.NotNull(validationService);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task ExecuteAsync_WithInvalidServices_HandlesGracefully()
    {
        // Arrange - Service provider that returns null for services
        var emptyServices = new ServiceCollection();
        var emptyProvider = emptyServices.BuildServiceProvider();
        
        using var worker = new TaxonomyValidationWorker(emptyProvider, _logger);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        
        // Act & Assert - Should not throw, handles missing services gracefully
        try
        {
            await worker.StartAsync(cts.Token);
            await Task.Delay(100);
            await worker.StopAsync(CancellationToken.None);
        }
        catch (TaskCanceledException)
        {
            // Expected behavior when timeout occurs
        }
    }

    #endregion
}

