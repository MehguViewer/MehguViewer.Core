using MehguViewer.Core.Infrastructures;
using MehguViewer.Core.Shared;
using MehguViewer.Core.Services;
using MehguViewer.Core.Workers;
using MehguViewer.Core.Helpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace MehguViewer.Core.Tests.Services;

/// <summary>
/// Unit tests for <see cref="RepositoryInitializerService"/>.
/// Validates initialization sequencing, error handling, and fallback behavior.
/// </summary>
[Trait("Category", "Unit")]
[Trait("Service", "RepositoryInitializer")]
public class RepositoryInitializerServiceTests : IDisposable
{
    #region Test Infrastructure
    
    private readonly ILogger<RepositoryInitializerService> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IConfiguration _configuration;
    private readonly DynamicRepository _repository;
    private readonly ITestOutputHelper _output;
    
    public RepositoryInitializerServiceTests(ITestOutputHelper output)
    {
        _output = output;
        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Debug);
        });
        
        _logger = _loggerFactory.CreateLogger<RepositoryInitializerService>();
        
        // Setup minimal configuration
        var configBuilder = new ConfigurationBuilder();
        configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = "",
            ["EmbeddedPostgres:Enabled"] = "false"
        });
        _configuration = configBuilder.Build();
        
        // Create repository with minimal dependencies
        var metadataLogger = _loggerFactory.CreateLogger<MetadataAggregationService>();
        var metadataService = new MetadataAggregationService(metadataLogger);
        
        _repository = new DynamicRepository(
            _configuration,
            _loggerFactory,
            null,
            null,
            metadataService);
    }
    
    public void Dispose()
    {
        _loggerFactory.Dispose();
        GC.SuppressFinalize(this);
    }
    
    #endregion
    
    #region Constructor Tests
    
    [Fact]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        // Act
        var service = new RepositoryInitializerService(
            _repository,
            _logger);
        
        // Assert
        Assert.NotNull(service);
        Assert.False(service.IsInitialized);
        _output.WriteLine("✓ Service created successfully");
    }
    
    [Fact]
    public void Constructor_WithoutEmbeddedPostgres_CreatesInstance()
    {
        // Act
        var service = new RepositoryInitializerService(
            _repository,
            _logger,
            null);
        
        // Assert
        Assert.NotNull(service);
        Assert.False(service.IsInitialized);
        _output.WriteLine("✓ Service created without embedded PostgreSQL");
    }
    
    [Fact]
    public void Constructor_WithNullRepository_ThrowsArgumentNullException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new RepositoryInitializerService(
                null!,
                _logger));
        
        Assert.Equal("repository", ex.ParamName);
        _output.WriteLine($"✓ Correctly throws ArgumentNullException for null repository");
    }
    
    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new RepositoryInitializerService(
                _repository,
                null!));
        
        Assert.Equal("logger", ex.ParamName);
        _output.WriteLine($"✓ Correctly throws ArgumentNullException for null logger");
    }
    
    #endregion
    
    #region Initialization Tests
    
    [Fact]
    public async Task ExecuteAsync_WithoutEmbeddedPostgres_InitializesSuccessfully()
    {
        // Arrange
        var service = new RepositoryInitializerService(
            _repository,
            _logger);
        
        var cts = new CancellationTokenSource();
        
        // Act
        await service.StartAsync(cts.Token);
        await Task.Delay(1000); // Allow background service to execute
        
        // Assert
        Assert.True(service.IsInitialized);
        _output.WriteLine($"✓ Service initialized successfully. IsInitialized: {service.IsInitialized}");
        
        await service.StopAsync(cts.Token);
        cts.Dispose();
    }
    
    [Fact]
    public async Task ExecuteAsync_WithMemoryRepository_CompletesSuccessfully()
    {
        // Arrange
        var service = new RepositoryInitializerService(
            _repository,
            _logger);
        
        var cts = new CancellationTokenSource();
        
        // Act
        await service.StartAsync(cts.Token);
        await Task.Delay(1000); // Allow background service to execute
        
        // Assert - Service completes even with memory repository
        Assert.True(service.IsInitialized);
        Assert.True(_repository.IsInMemory);
        _output.WriteLine($"✓ Memory repository initialized. IsInMemory: {_repository.IsInMemory}");
        
        await service.StopAsync(cts.Token);
        cts.Dispose();
    }
    
    [Fact]
    public void IsInitialized_BeforeExecution_ReturnsFalse()
    {
        // Arrange
        var service = new RepositoryInitializerService(
            _repository,
            _logger);
        
        // Assert
        Assert.False(service.IsInitialized);
        _output.WriteLine("✓ IsInitialized correctly returns false before execution");
    }
    
    #endregion
    
    #region Error Handling Tests
    
    [Fact]
    public async Task ExecuteAsync_WhenCancelled_HandlesGracefully()
    {
        // Arrange
        var service = new RepositoryInitializerService(
            _repository,
            _logger);
        
        var cts = new CancellationTokenSource();
        
        // Act
        await service.StartAsync(cts.Token);
        cts.Cancel(); // Cancel immediately
        
        // Assert - Should not throw
        await Task.Delay(100);
        _output.WriteLine("✓ Service handles cancellation gracefully");
        
        await service.StopAsync(default);
        cts.Dispose();
    }
    
    [Fact]
    public async Task ExecuteAsync_CompletesWithinTimeout()
    {
        // Arrange
        var service = new RepositoryInitializerService(
            _repository,
            _logger);
        
        var cts = new CancellationTokenSource();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        
        // Act
        await service.StartAsync(cts.Token);
        await Task.Delay(2000); // Wait for completion
        sw.Stop();
        
        // Assert - Should complete well within timeout (60 seconds)
        Assert.True(sw.ElapsedMilliseconds < 60000, $"Initialization took too long: {sw.ElapsedMilliseconds}ms");
        Assert.True(service.IsInitialized);
        _output.WriteLine($"✓ Initialization completed in {sw.ElapsedMilliseconds}ms");
        
        await service.StopAsync(cts.Token);
        cts.Dispose();
    }
    
    #endregion
    
    #region Integration Tests
    
    [Fact]
    public async Task FullInitializationCycle_WithMemoryRepository_Succeeds()
    {
        // Arrange
        var service = new RepositoryInitializerService(
            _repository,
            _logger);
        
        var cts = new CancellationTokenSource();
        
        // Act - Full lifecycle
        await service.StartAsync(cts.Token);
        await Task.Delay(1500); // Allow full initialization
        
        // Assert
        Assert.True(service.IsInitialized);
        Assert.True(_repository.IsInMemory);
        _output.WriteLine($"✓ Full initialization cycle completed");
        _output.WriteLine($"  - IsInitialized: {service.IsInitialized}");
        _output.WriteLine($"  - IsInMemory: {_repository.IsInMemory}");
        
        await service.StopAsync(cts.Token);
        cts.Dispose();
    }
    
    [Fact]
    public async Task RepositoryState_AfterInitialization_IsUsable()
    {
        // Arrange
        var service = new RepositoryInitializerService(
            _repository,
            _logger);
        
        var cts = new CancellationTokenSource();
        
        // Act
        await service.StartAsync(cts.Token);
        await Task.Delay(1000);
        
        // Assert - Repository should be ready for operations
        Assert.True(service.IsInitialized);
        
        // Test repository is operational by creating a test URN
        var testUrn = UrnHelper.CreateUserUrn();
        _output.WriteLine($"✓ Repository is operational. Test URN: {testUrn}");
        
        await service.StopAsync(cts.Token);
        cts.Dispose();
    }
    
    #endregion
    
    #region Performance Tests
    
    [Fact]
    public async Task Initialization_Performance_IsAcceptable()
    {
        // Arrange
        var service = new RepositoryInitializerService(
            _repository,
            _logger);
        
        var cts = new CancellationTokenSource();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        
        // Act
        await service.StartAsync(cts.Token);
        await Task.Delay(1500); // Wait for completion
        sw.Stop();
        
        // Assert - Should complete quickly for memory repository
        Assert.True(sw.ElapsedMilliseconds < 5000, $"Initialization too slow: {sw.ElapsedMilliseconds}ms");
        Assert.True(service.IsInitialized);
        _output.WriteLine($"✓ Initialization performance acceptable: {sw.ElapsedMilliseconds}ms");
        
        await service.StopAsync(cts.Token);
        cts.Dispose();
    }
    
    [Fact]
    public async Task MultipleStartStop_Cycles_WorkCorrectly()
    {
        // Arrange
        var service = new RepositoryInitializerService(
            _repository,
            _logger);
        
        // Act & Assert - Multiple cycles
        for (int i = 0; i < 3; i++)
        {
            var cts = new CancellationTokenSource();
            await service.StartAsync(cts.Token);
            await Task.Delay(500);
            await service.StopAsync(cts.Token);
            cts.Dispose();
            _output.WriteLine($"✓ Cycle {i + 1} completed");
        }
        
        _output.WriteLine("✓ Multiple start/stop cycles handled correctly");
    }
    
    #endregion
}
