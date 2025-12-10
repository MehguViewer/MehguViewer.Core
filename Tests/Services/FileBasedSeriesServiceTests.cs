using System.Text.Json;
using MehguViewer.Core.Services;
using MehguViewer.Core.Workers;
using MehguViewer.Core.Helpers;
using MehguViewer.Core.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;

namespace MehguViewer.Core.Tests.Services;

/// <summary>
/// Comprehensive unit tests for FileBasedSeriesService.
/// Tests initialization, CRUD operations, caching, error handling, and security.
/// </summary>
public class FileBasedSeriesServiceTests : IDisposable
{
    private readonly string _testDataPath;
    private readonly IConfiguration _configuration;
    private readonly ILogger<FileBasedSeriesService> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly FileBasedSeriesService _service;

    public FileBasedSeriesServiceTests()
    {
        // Create unique test directory for each test run
        _testDataPath = Path.Combine(Path.GetTempPath(), $"mehgu_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDataPath);

        // Setup configuration
        var configSettings = new Dictionary<string, string?>
        {
            ["Storage:DataPath"] = _testDataPath
        };
        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configSettings)
            .Build();

        // Setup logger
        _loggerFactory = LoggerFactory.Create(builder => 
            builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        _logger = _loggerFactory.CreateLogger<FileBasedSeriesService>();

        // Create service instance
        _service = new FileBasedSeriesService(_configuration, _logger);
    }

    public void Dispose()
    {
        // Cleanup test directory
        if (Directory.Exists(_testDataPath))
        {
            try
            {
                Directory.Delete(_testDataPath, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
        _loggerFactory?.Dispose();
        GC.SuppressFinalize(this);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullConfiguration_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new FileBasedSeriesService(null!, _logger));
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new FileBasedSeriesService(_configuration, null!));
    }

    [Fact]
    public void Constructor_WithValidParameters_Succeeds()
    {
        // Act & Assert
        var service = new FileBasedSeriesService(_configuration, _logger);
        Assert.NotNull(service);
    }

    [Fact]
    public void Constructor_WithEmptyDataPath_UsesDefaultPath()
    {
        // Arrange
        var emptyConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        var logger = _loggerFactory.CreateLogger<FileBasedSeriesService>();

        // Act
        var service = new FileBasedSeriesService(emptyConfig, logger);

        // Assert
        Assert.NotNull(service);
        var basePath = service.GetSeriesBasePath();
        Assert.Contains("data", basePath);
    }

    #endregion

    #region Initialization Tests

    [Fact]
    public async Task InitializeAsync_CreatesSeriesDirectory()
    {
        // Act
        await _service.InitializeAsync();

        // Assert
        var seriesPath = _service.GetSeriesBasePath();
        Assert.True(Directory.Exists(seriesPath));
    }

    [Fact]
    public async Task InitializeAsync_CalledTwice_OnlyInitializesOnce()
    {
        // Act
        await _service.InitializeAsync();
        var firstSeriesCount = _service.ListSeries().Count();
        
        await _service.InitializeAsync();
        var secondSeriesCount = _service.ListSeries().Count();

        // Assert - counts should be the same, no duplicate loading
        Assert.Equal(firstSeriesCount, secondSeriesCount);
    }

    [Fact]
    public async Task InitializeAsync_LoadsExistingSeries()
    {
        // Arrange
        var testSeries = CreateTestSeries();
        await _service.InitializeAsync();
        await _service.SaveSeriesAsync(testSeries);

        // Create new service instance
        var newLogger = _loggerFactory.CreateLogger<FileBasedSeriesService>();
        var newService = new FileBasedSeriesService(_configuration, newLogger);

        // Act
        await newService.InitializeAsync();

        // Assert
        var loadedSeries = newService.GetSeries(testSeries.id);
        Assert.NotNull(loadedSeries);
        Assert.Equal(testSeries.title, loadedSeries.title);
    }

    [Fact]
    public async Task InitializeAsync_WithCorruptedMetadata_LogsErrorAndContinues()
    {
        // Arrange
        await _service.InitializeAsync();
        var seriesPath = _service.GetSeriesBasePath();
        var corruptedDir = Path.Combine(seriesPath, Guid.NewGuid().ToString());
        Directory.CreateDirectory(corruptedDir);
        await File.WriteAllTextAsync(Path.Combine(corruptedDir, "metadata.json"), "{ invalid json");

        // Create new service instance
        var newLogger = _loggerFactory.CreateLogger<FileBasedSeriesService>();
        var newService = new FileBasedSeriesService(_configuration, newLogger);

        // Act - should not throw, just log error
        await newService.InitializeAsync();

        // Assert - service should still be functional
        Assert.NotNull(newService);
    }

    #endregion

    #region Path Security Tests

    [Fact]
    public void GetSeriesPath_WithPathTraversal_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => _service.GetSeriesPath("../../../etc/passwd"));
    }

    [Fact]
    public void GetSeriesPath_WithBackslash_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => _service.GetSeriesPath("test\\..\\backdoor"));
    }

    [Fact]
    public void GetSeriesPath_WithValidId_ReturnsNormalizedPath()
    {
        // Act
        var path = _service.GetSeriesPath("valid-uuid-123");

        // Assert
        Assert.NotNull(path);
        Assert.Contains("valid-uuid-123", path);
        Assert.DoesNotContain("..", path);
    }

    [Fact]
    public void GetSeriesPath_WithUrn_StripsPrefixCorrectly()
    {
        // Arrange
        var uuid = Guid.NewGuid().ToString();
        var urn = $"urn:mvn:series:{uuid}";

        // Act
        var path = _service.GetSeriesPath(urn);

        // Assert
        Assert.Contains(uuid, path);
        Assert.DoesNotContain("urn:mvn:series:", path);
    }

    [Fact]
    public void GetSeriesPath_WithNullId_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => _service.GetSeriesPath(null!));
    }

    [Fact]
    public void GetSeriesPath_WithEmptyId_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => _service.GetSeriesPath(""));
    }

    #endregion

    #region Series CRUD Tests

    [Fact]
    public async Task SaveSeriesAsync_WithValidSeries_SavesSuccessfully()
    {
        // Arrange
        await _service.InitializeAsync();
        var series = CreateTestSeries();

        // Act
        await _service.SaveSeriesAsync(series);

        // Assert
        var metadataPath = _service.GetMetadataPath(series.id);
        Assert.True(File.Exists(metadataPath));

        var savedSeries = _service.GetSeries(series.id);
        Assert.NotNull(savedSeries);
        Assert.Equal(series.title, savedSeries.title);
    }

    [Fact]
    public async Task SaveSeriesAsync_WithNullSeries_ThrowsArgumentNullException()
    {
        // Arrange
        await _service.InitializeAsync();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.SaveSeriesAsync(null!));
    }

    [Fact]
    public async Task SaveSeriesAsync_UpdatesCache()
    {
        // Arrange
        await _service.InitializeAsync();
        var series = CreateTestSeries();

        // Act
        await _service.SaveSeriesAsync(series);

        // Assert
        var cachedSeries = _service.GetSeries(series.id);
        Assert.NotNull(cachedSeries);
        Assert.Equal(series.id, cachedSeries.id);
    }

    [Fact]
    public async Task SaveSeriesAsync_UpdatesTaxonomyCache()
    {
        // Arrange
        await _service.InitializeAsync();
        var series = CreateTestSeries();

        // Act
        await _service.SaveSeriesAsync(series);

        // Assert
        var authors = _service.GetAllAuthors();
        Assert.Contains(authors, a => a.name == series.authors[0].name);
    }

    [Fact]
    public async Task GetSeries_WithValidId_ReturnsSeries()
    {
        // Arrange
        await _service.InitializeAsync();
        var series = CreateTestSeries();
        await _service.SaveSeriesAsync(series);

        // Act
        var result = _service.GetSeries(series.id);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(series.id, result.id);
        Assert.Equal(series.title, result.title);
    }

    [Fact]
    public async Task GetSeries_WithNonexistentId_ReturnsNull()
    {
        // Arrange
        await _service.InitializeAsync();

        // Act
        var result = _service.GetSeries("urn:mvn:series:nonexistent");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetSeries_WithNullId_ReturnsNull()
    {
        // Act
        var result = _service.GetSeries(null!);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ListSeries_ReturnsAllSeries()
    {
        // Arrange
        await _service.InitializeAsync();
        var series1 = CreateTestSeries();
        var series2 = CreateTestSeries();
        await _service.SaveSeriesAsync(series1);
        await _service.SaveSeriesAsync(series2);

        // Act
        var allSeries = _service.ListSeries().ToList();

        // Assert
        Assert.Contains(allSeries, s => s.id == series1.id);
        Assert.Contains(allSeries, s => s.id == series2.id);
        Assert.True(allSeries.Count >= 2);
    }

    [Fact]
    public async Task DeleteSeries_RemovesSeriesFromDiskAndCache()
    {
        // Arrange
        await _service.InitializeAsync();
        var series = CreateTestSeries();
        await _service.SaveSeriesAsync(series);

        // Act
        _service.DeleteSeries(series.id);

        // Assert
        var seriesPath = _service.GetSeriesPath(series.id);
        Assert.False(Directory.Exists(seriesPath));
        Assert.Null(_service.GetSeries(series.id));
    }

    [Fact]
    public void DeleteSeries_WithNullId_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => _service.DeleteSeries(null!));
    }

    [Fact]
    public async Task DeleteSeries_WithNonexistentSeries_DoesNotThrow()
    {
        // Arrange
        await _service.InitializeAsync();

        // Act & Assert (should not throw)
        _service.DeleteSeries("urn:mvn:series:nonexistent");
    }

    #endregion

    #region Search Tests

    [Fact]
    public async Task SearchSeries_WithTitleQuery_ReturnsMatchingSeries()
    {
        // Arrange
        await _service.InitializeAsync();
        var series1 = CreateTestSeries(title: "Test Manga One");
        var series2 = CreateTestSeries(title: "Another Series");
        await _service.SaveSeriesAsync(series1);
        await _service.SaveSeriesAsync(series2);

        // Act
        var results = _service.SearchSeries("Manga", null, null, null).ToList();

        // Assert
        Assert.Contains(results, s => s.id == series1.id);
        Assert.DoesNotContain(results, s => s.id == series2.id);
    }

    [Fact]
    public async Task SearchSeries_WithTypeFilter_ReturnsMatchingSeries()
    {
        // Arrange
        await _service.InitializeAsync();
        var photoSeries = CreateTestSeries(mediaType: "Photo");
        var videoSeries = CreateTestSeries(mediaType: "Video");
        await _service.SaveSeriesAsync(photoSeries);
        await _service.SaveSeriesAsync(videoSeries);

        // Act
        var results = _service.SearchSeries(null, "Photo", null, null).ToList();

        // Assert
        Assert.Contains(results, s => s.id == photoSeries.id);
        Assert.DoesNotContain(results, s => s.id == videoSeries.id);
    }

    [Fact]
    public async Task SearchSeries_WithTags_ReturnsMatchingSeries()
    {
        // Arrange
        await _service.InitializeAsync();
        var series1 = CreateTestSeries(tags: new[] { "action", "adventure" });
        var series2 = CreateTestSeries(tags: new[] { "romance", "comedy" });
        await _service.SaveSeriesAsync(series1);
        await _service.SaveSeriesAsync(series2);

        // Act
        var results = _service.SearchSeries(null, null, new[] { "action" }, null).ToList();

        // Assert
        Assert.Contains(results, s => s.id == series1.id);
        Assert.DoesNotContain(results, s => s.id == series2.id);
    }

    [Fact]
    public async Task SearchSeries_WithNoFilters_ReturnsAllSeries()
    {
        // Arrange
        await _service.InitializeAsync();
        var series1 = CreateTestSeries();
        var series2 = CreateTestSeries();
        await _service.SaveSeriesAsync(series1);
        await _service.SaveSeriesAsync(series2);

        // Act
        var results = _service.SearchSeries(null, null, null, null).ToList();

        // Assert
        Assert.Contains(results, s => s.id == series1.id);
        Assert.Contains(results, s => s.id == series2.id);
    }

    #endregion

    #region Cover Image Tests

    [Fact]
    public async Task SaveCoverImageAsync_WithValidStream_SavesImage()
    {
        // Arrange
        await _service.InitializeAsync();
        var series = CreateTestSeries();
        await _service.SaveSeriesAsync(series);

        using var imageStream = new MemoryStream(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }); // JPEG header

        // Act
        var url = await _service.SaveCoverImageAsync(series.id, imageStream, "cover.jpg");

        // Assert
        Assert.NotNull(url);
        Assert.Contains("/api/v1/series/", url);
        Assert.Contains("/cover", url);

        var coverPath = _service.GetCoverImagePath(series.id);
        Assert.NotNull(coverPath);
        Assert.True(File.Exists(coverPath));
    }

    [Fact]
    public async Task SaveCoverImageAsync_WithNullStream_ThrowsArgumentNullException()
    {
        // Arrange
        await _service.InitializeAsync();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _service.SaveCoverImageAsync("test-id", null!, "cover.jpg"));
    }

    [Fact]
    public async Task SaveCoverImageAsync_WithNullSeriesId_ThrowsArgumentException()
    {
        // Arrange
        await _service.InitializeAsync();
        using var stream = new MemoryStream();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.SaveCoverImageAsync(null!, stream, "cover.jpg"));
    }

    [Fact]
    public async Task SaveCoverImageVariantsAsync_WithMultipleVariants_SavesAll()
    {
        // Arrange
        await _service.InitializeAsync();
        var series = CreateTestSeries();
        await _service.SaveSeriesAsync(series);

        var variants = new Dictionary<string, byte[]>
        {
            ["thumbnail"] = new byte[] { 1, 2, 3 },
            ["web"] = new byte[] { 4, 5, 6 },
            ["raw"] = new byte[] { 7, 8, 9 }
        };

        // Act
        var url = await _service.SaveCoverImageVariantsAsync(series.id, variants, ".jpg");

        // Assert
        Assert.NotNull(url);
        Assert.Contains("variant=web", url);

        var thumbnailPath = _service.GetCoverImagePath(series.id, "thumbnail");
        var webPath = _service.GetCoverImagePath(series.id, "web");
        var rawPath = _service.GetCoverImagePath(series.id, "raw");

        Assert.NotNull(thumbnailPath);
        Assert.NotNull(webPath);
        Assert.NotNull(rawPath);
    }

    [Fact]
    public async Task SaveCoverImageVariantsAsync_WithLanguage_SavesLocalizedCover()
    {
        // Arrange
        await _service.InitializeAsync();
        var series = CreateTestSeries();
        await _service.SaveSeriesAsync(series);

        var variants = new Dictionary<string, byte[]>
        {
            ["web"] = new byte[] { 1, 2, 3, 4 }
        };

        // Act
        var url = await _service.SaveCoverImageVariantsAsync(series.id, variants, ".jpg", "ja");

        // Assert
        Assert.Contains("lang=ja", url);
        var coverPath = _service.GetCoverImagePath(series.id, "web", "ja");
        Assert.NotNull(coverPath);
    }

    #endregion

    #region Unit Tests

    [Fact]
    public async Task SaveUnitAsync_WithValidUnit_SavesSuccessfully()
    {
        // Arrange
        await _service.InitializeAsync();
        var series = CreateTestSeries();
        await _service.SaveSeriesAsync(series);

        var unit = CreateTestUnit(series.id, 1.0);

        // Act
        await _service.SaveUnitAsync(unit);

        // Assert
        var unitPath = _service.GetUnitPath(series.id, 1.0);
        var metadataPath = Path.Combine(unitPath, "metadata.json");
        Assert.True(File.Exists(metadataPath));

        var savedUnit = _service.GetUnit(unit.id);
        Assert.NotNull(savedUnit);
        Assert.Equal(unit.id, savedUnit.id);
    }

    [Fact]
    public async Task SaveUnitAsync_WithNullUnit_ThrowsArgumentNullException()
    {
        // Arrange
        await _service.InitializeAsync();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.SaveUnitAsync(null!));
    }

    [Fact]
    public async Task GetUnitsForSeries_ReturnsAllUnitsForSeries()
    {
        // Arrange
        await _service.InitializeAsync();
        var series = CreateTestSeries();
        await _service.SaveSeriesAsync(series);

        var unit1 = CreateTestUnit(series.id, 1.0);
        var unit2 = CreateTestUnit(series.id, 2.0);
        await _service.SaveUnitAsync(unit1);
        await _service.SaveUnitAsync(unit2);

        // Act
        var units = _service.GetUnitsForSeries(series.id).ToList();

        // Assert
        Assert.Equal(2, units.Count);
        Assert.Contains(units, u => u.id == unit1.id);
        Assert.Contains(units, u => u.id == unit2.id);
    }

    [Fact]
    public async Task DeleteUnit_RemovesUnitFromDiskAndCache()
    {
        // Arrange
        await _service.InitializeAsync();
        var series = CreateTestSeries();
        await _service.SaveSeriesAsync(series);

        var unit = CreateTestUnit(series.id, 1.0);
        await _service.SaveUnitAsync(unit);

        // Act
        _service.DeleteUnit(series.id, unit.id);

        // Assert
        var unitPath = _service.GetUnitPath(series.id, 1.0);
        Assert.False(Directory.Exists(unitPath));
        Assert.Null(_service.GetUnit(unit.id));
    }

    #endregion

    #region Cache Tests

    [Fact]
    public async Task RefreshCacheAsync_ReloadsAllData()
    {
        // Arrange
        await _service.InitializeAsync();
        var series = CreateTestSeries();
        await _service.SaveSeriesAsync(series);

        // Act
        await _service.RefreshCacheAsync();

        // Assert
        var reloadedSeries = _service.GetSeries(series.id);
        Assert.NotNull(reloadedSeries);
        Assert.Equal(series.title, reloadedSeries.title);
    }

    #endregion

    #region Taxonomy Tests

    [Fact]
    public async Task GetAllAuthors_ReturnsDistinctAuthors()
    {
        // Arrange
        await _service.InitializeAsync();
        var author = new Author("auth1", "John Doe", "Author");
        var series1 = CreateTestSeries(authors: new[] { author });
        var series2 = CreateTestSeries(authors: new[] { author });
        await _service.SaveSeriesAsync(series1);
        await _service.SaveSeriesAsync(series2);

        // Act
        var authors = _service.GetAllAuthors();

        // Assert
        Assert.Contains(authors, a => a.name == "John Doe");
        Assert.Equal(1, authors.Count(a => a.name == "John Doe")); // Should be deduplicated
    }

    [Fact]
    public async Task FindAuthorByName_WithExistingAuthor_ReturnsAuthor()
    {
        // Arrange
        await _service.InitializeAsync();
        var author = new Author("auth1", "Jane Smith", "Author");
        var series = CreateTestSeries(authors: new[] { author });
        await _service.SaveSeriesAsync(series);

        // Act
        var found = _service.FindAuthorByName("Jane Smith");

        // Assert
        Assert.NotNull(found);
        Assert.Equal("Jane Smith", found.name);
    }

    [Fact]
    public async Task FindAuthorByName_IsCaseInsensitive()
    {
        // Arrange
        await _service.InitializeAsync();
        var author = new Author("auth1", "TestAuthor", "Author");
        var series = CreateTestSeries(authors: new[] { author });
        await _service.SaveSeriesAsync(series);

        // Act
        var found = _service.FindAuthorByName("testauthor");

        // Assert
        Assert.NotNull(found);
        Assert.Equal("TestAuthor", found.name);
    }

    #endregion

    #region Helper Methods

    private Series CreateTestSeries(
        string? title = null,
        string? mediaType = null,
        string[]? tags = null,
        Author[]? authors = null)
    {
        var id = $"urn:mvn:series:{Guid.NewGuid()}";
        return new Series(
            id: id,
            federation_ref: null,
            title: title ?? "Test Series",
            description: "Test Description",
            poster: new Poster("/api/v1/series/test/cover", "Test Cover"),
            media_type: mediaType ?? "Photo",
            external_links: new Dictionary<string, string>(),
            reading_direction: "LTR",
            tags: tags ?? new[] { "test", "manga" },
            content_warnings: Array.Empty<string>(),
            authors: authors ?? new[] { new Author("auth1", "Test Author", "Author") },
            scanlators: new[] { new Scanlator("scan1", "Test Scanlator", ScanlatorRole.Both) },
            groups: null,
            alt_titles: null,
            status: "ongoing",
            year: 2024,
            original_language: "en",
            created_by: null,
            created_at: DateTime.UtcNow,
            updated_at: DateTime.UtcNow,
            localized: null,
            allowed_editors: null
        );
    }

    private Unit CreateTestUnit(string seriesId, double unitNumber)
    {
        var id = $"urn:mvn:unit:{Guid.NewGuid()}";
        return new Unit(
            id: id,
            series_id: seriesId,
            unit_number: unitNumber,
            title: $"Unit {unitNumber}",
            created_at: DateTime.UtcNow,
            created_by: null,
            language: "en",
            page_count: 0,
            folder_path: null,
            updated_at: DateTime.UtcNow,
            description: null,
            tags: null,
            content_warnings: null,
            authors: null,
            localized: null,
            allowed_editors: null
        );
    }

    #endregion
}
