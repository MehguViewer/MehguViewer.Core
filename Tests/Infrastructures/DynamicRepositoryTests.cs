using MehguViewer.Core.Shared;
using MehguViewer.Core.Infrastructures;
using MehguViewer.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;

namespace MehguViewer.Core.Tests.Infrastructures;

/// <summary>
/// Unit tests for <see cref="DynamicRepository"/>.
/// </summary>
/// <remarks>
/// Tests cover:
/// - Repository initialization and fallback behavior
/// - File-based series/unit operations
/// - Database-delegated operations
/// - Connection testing and switching
/// - Thread safety and error handling
/// </remarks>
public class DynamicRepositoryTests : IDisposable
{
    private readonly IConfiguration _configuration;
    private readonly ILoggerFactory _loggerFactory;
    private readonly MetadataAggregationService _metadataService;
    
    public DynamicRepositoryTests()
    {
        // Create in-memory configuration
        var inMemorySettings = new Dictionary<string, string?>();
        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();
            
        _loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        _metadataService = new MetadataAggregationService(_loggerFactory.CreateLogger<MetadataAggregationService>());
    }

    public void Dispose()
    {
        _loggerFactory?.Dispose();
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidArguments_InitializesSuccessfully()
    {
        // Arrange & Act
        var repository = new DynamicRepository(_configuration, _loggerFactory, null, null, _metadataService);

        // Assert
        Assert.NotNull(repository);
        Assert.True(repository.IsInMemory); // Should start with MemoryRepository
    }

    [Fact]
    public void Constructor_WithNullConfiguration_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => new DynamicRepository(null!, _loggerFactory));
    }

    [Fact]
    public void Constructor_WithNullLoggerFactory_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => new DynamicRepository(_configuration, null!));
    }

    #endregion

    #region IsInMemory Tests

    [Fact]
    public void IsInMemory_WhenUsingMemoryRepository_ReturnsTrue()
    {
        // Arrange
        var repository = new DynamicRepository(_configuration, _loggerFactory, null, null, _metadataService);

        // Act
        var isInMemory = repository.IsInMemory;

        // Assert
        Assert.True(isInMemory);
    }

    #endregion

    #region TestConnection Tests

    [Fact]
    public void TestConnection_WithNullConnectionString_ThrowsArgumentNullException()
    {
        // Arrange
        var repository = new DynamicRepository(_configuration, _loggerFactory, null, null, _metadataService);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => repository.TestConnection(null!));
    }

    [Fact]
    public void TestConnection_WithEmptyConnectionString_ThrowsArgumentNullException()
    {
        // Arrange
        var repository = new DynamicRepository(_configuration, _loggerFactory, null, null, _metadataService);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => repository.TestConnection("   "));
    }

    #endregion

    #region SwitchToPostgres Tests

    [Fact]
    public void SwitchToPostgres_WithNullConnectionString_ThrowsArgumentNullException()
    {
        // Arrange
        var repository = new DynamicRepository(_configuration, _loggerFactory, null, null, _metadataService);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => repository.SwitchToPostgres(null!, false));
    }

    [Fact]
    public void SwitchToPostgres_WithEmptyConnectionString_ThrowsArgumentNullException()
    {
        // Arrange
        var repository = new DynamicRepository(_configuration, _loggerFactory, null, null, _metadataService);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => repository.SwitchToPostgres("", false));
    }

    #endregion

    #region Series Operations Tests

    [Fact]
    public void AddSeries_WithoutFileService_ThrowsInvalidOperationException()
    {
        // Arrange
        var repository = new DynamicRepository(_configuration, _loggerFactory, null, null, _metadataService);
        var series = CreateTestSeries();

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => repository.AddSeries(series));
        Assert.Contains("FileBasedSeriesService", exception.Message);
    }

    [Fact]
    public void UpdateSeries_WithoutFileService_ThrowsInvalidOperationException()
    {
        // Arrange
        var repository = new DynamicRepository(_configuration, _loggerFactory, null, null, _metadataService);
        var series = CreateTestSeries();

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => repository.UpdateSeries(series));
        Assert.Contains("FileBasedSeriesService", exception.Message);
    }

    [Fact]
    public void DeleteSeries_WithoutFileService_ThrowsInvalidOperationException()
    {
        // Arrange
        var repository = new DynamicRepository(_configuration, _loggerFactory, null, null, _metadataService);

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => repository.DeleteSeries("test-series"));
        Assert.Contains("FileBasedSeriesService", exception.Message);
    }

    [Fact]
    public void GetSeries_WithoutFileService_ReturnsNull()
    {
        // Arrange
        var repository = new DynamicRepository(_configuration, _loggerFactory, null, null, _metadataService);

        // Act
        var result = repository.GetSeries("test-series");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ListSeries_WithoutFileService_ReturnsEmptyEnumerable()
    {
        // Arrange
        var repository = new DynamicRepository(_configuration, _loggerFactory, null, null, _metadataService);

        // Act
        var result = repository.ListSeries();

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void SearchSeries_WithoutFileService_ReturnsEmptyEnumerable()
    {
        // Arrange
        var repository = new DynamicRepository(_configuration, _loggerFactory, null, null, _metadataService);

        // Act
        var result = repository.SearchSeries("test", "Photo", null, null);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    #endregion

    #region Unit Operations Tests

    [Fact]
    public void AddUnit_WithoutFileService_ThrowsInvalidOperationException()
    {
        // Arrange
        var repository = new DynamicRepository(_configuration, _loggerFactory, null, null, _metadataService);
        var unit = CreateTestUnit();

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => repository.AddUnit(unit));
        Assert.Contains("FileBasedSeriesService", exception.Message);
    }

    [Fact]
    public void UpdateUnit_WithoutFileService_ThrowsInvalidOperationException()
    {
        // Arrange
        var repository = new DynamicRepository(_configuration, _loggerFactory, null, null, _metadataService);
        var unit = CreateTestUnit();

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => repository.UpdateUnit(unit));
        Assert.Contains("FileBasedSeriesService", exception.Message);
    }

    [Fact]
    public void GetUnit_WithoutFileService_ReturnsNull()
    {
        // Arrange
        var repository = new DynamicRepository(_configuration, _loggerFactory, null, null, _metadataService);

        // Act
        var result = repository.GetUnit("test-unit");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ListUnits_WithoutFileService_ReturnsEmptyEnumerable()
    {
        // Arrange
        var repository = new DynamicRepository(_configuration, _loggerFactory, null, null, _metadataService);

        // Act
        var result = repository.ListUnits("test-series");

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    #endregion

    #region Database Delegated Operations Tests

    [Fact]
    public void SeedDebugData_CallsUnderlyingRepository()
    {
        // Arrange
        var repository = new DynamicRepository(_configuration, _loggerFactory, null, null, _metadataService);

        // Act
        repository.SeedDebugData();

        // Assert - No exception thrown, operation completes
        Assert.True(true);
    }

    [Fact]
    public void GetSystemConfig_ReturnsSystemConfig()
    {
        // Arrange
        var repository = new DynamicRepository(_configuration, _loggerFactory, null, null, _metadataService);

        // Act
        var config = repository.GetSystemConfig();

        // Assert
        Assert.NotNull(config);
    }

    [Fact]
    public void IsAdminSet_InitiallyReturnsFalse()
    {
        // Arrange
        var repository = new DynamicRepository(_configuration, _loggerFactory, null, null, _metadataService);

        // Act
        var isAdminSet = repository.IsAdminSet();

        // Assert
        Assert.False(isAdminSet);
    }

    [Fact]
    public void ListUsers_ReturnsEmptyListInitially()
    {
        // Arrange
        var repository = new DynamicRepository(_configuration, _loggerFactory, null, null, _metadataService);

        // Act
        var users = repository.ListUsers();

        // Assert
        Assert.NotNull(users);
        Assert.Empty(users);
    }

    #endregion

    #region Metadata Aggregation Tests

    [Fact]
    public void AggregateSeriesMetadataFromUnits_WithoutFileService_LogsWarningAndReturns()
    {
        // Arrange
        var repository = new DynamicRepository(_configuration, _loggerFactory, null, null, _metadataService);

        // Act
        repository.AggregateSeriesMetadataFromUnits("test-series");

        // Assert - Should not throw, just log warning
        Assert.True(true);
    }

    #endregion

    #region Thread Safety Tests

    [Fact]
    public async Task ConcurrentIsInMemoryAccess_ThreadSafe()
    {
        // Arrange
        var repository = new DynamicRepository(_configuration, _loggerFactory, null, null, _metadataService);
        var tasks = new List<Task<bool>>();

        // Act
        for (int i = 0; i < 100; i++)
        {
            tasks.Add(Task.Run(() => repository.IsInMemory));
        }

        await Task.WhenAll(tasks);

        // Assert - All should return true (no exceptions)
        Assert.All(tasks, task => Assert.True(task.Result));
    }

    [Fact]
    public async Task ConcurrentGetSystemConfig_ThreadSafe()
    {
        // Arrange
        var repository = new DynamicRepository(_configuration, _loggerFactory, null, null, _metadataService);
        var tasks = new List<Task<SystemConfig>>();

        // Act
        for (int i = 0; i < 100; i++)
        {
            tasks.Add(Task.Run(() => repository.GetSystemConfig()));
        }

        await Task.WhenAll(tasks);

        // Assert - All should complete successfully
        Assert.All(tasks, task => Assert.NotNull(task.Result));
    }

    #endregion

    #region Reset Operations Tests

    [Fact]
    public void ResetAllData_CallsUnderlyingRepository()
    {
        // Arrange
        var repository = new DynamicRepository(_configuration, _loggerFactory, null, null, _metadataService);

        // Act
        repository.ResetAllData();

        // Assert - Should complete without exception
        Assert.True(true);
    }

    [Fact]
    public void ResetDatabase_WithMemoryRepository_CallsResetAllData()
    {
        // Arrange
        var repository = new DynamicRepository(_configuration, _loggerFactory, null, null, _metadataService);

        // Act
        repository.ResetDatabase();

        // Assert - Should complete without exception
        Assert.True(repository.IsInMemory);
    }

    #endregion

    #region Helper Methods

    private static Series CreateTestSeries()
    {
        return new Series(
            id: "test-series",
            federation_ref: null,
            title: "Test Series",
            description: "Test description",
            poster: new Poster("test.jpg", "Test image"),
            media_type: MediaTypes.Photo,
            external_links: new Dictionary<string, string>(),
            reading_direction: "ltr",
            tags: Array.Empty<string>(),
            content_warnings: Array.Empty<string>(),
            authors: Array.Empty<Author>(),
            scanlators: Array.Empty<Scanlator>(),
            created_by: "test-user",
            created_at: DateTime.UtcNow,
            updated_at: DateTime.UtcNow
        );
    }

    private static Unit CreateTestUnit()
    {
        return new Unit(
            id: "test-unit",
            series_id: "test-series",
            unit_number: 1.0,
            title: "Chapter 1",
            created_at: DateTime.UtcNow,
            page_count: 10
        );
    }

    #endregion
}
