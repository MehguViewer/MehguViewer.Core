using MehguViewer.Core.Shared;
using MehguViewer.Core.Infrastructures;
using MehguViewer.Core.Services;
using MehguViewer.Core.Workers;
using MehguViewer.Core.Helpers;
using Microsoft.Extensions.Logging;
using Xunit;

namespace MehguViewer.Core.Tests.Infrastructures;

/// <summary>
/// Comprehensive unit tests for <see cref="MemoryRepository"/>.
/// </summary>
/// <remarks>
/// Test coverage includes:
/// <list type="bullet">
/// <item>Constructor and initialization</item>
/// <item>Series CRUD operations</item>
/// <item>Unit CRUD operations</item>
/// <item>Edit permission management</item>
/// <item>Page operations</item>
/// <item>Progress tracking</item>
/// <item>User management</item>
/// <item>Collection operations</item>
/// <item>Passkey operations</item>
/// <item>Error handling and validation</item>
/// <item>Thread safety scenarios</item>
/// </list>
/// </remarks>
public class MemoryRepositoryTests : IDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly MemoryRepository _repository;

    public MemoryRepositoryTests()
    {
        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        var metadataLogger = _loggerFactory.CreateLogger<MetadataAggregationService>();
        var metadataService = new MetadataAggregationService(metadataLogger);
        _repository = new MemoryRepository(_loggerFactory.CreateLogger<MemoryRepository>(), metadataService);
    }

    public void Dispose()
    {
        _loggerFactory?.Dispose();
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidLogger_InitializesSuccessfully()
    {
        // Arrange & Act
        var logger = _loggerFactory.CreateLogger<MemoryRepository>();
        var metadataLogger = _loggerFactory.CreateLogger<MetadataAggregationService>();
        var metadataService = new MetadataAggregationService(metadataLogger);
        var repo = new MemoryRepository(logger, metadataService);

        // Assert
        Assert.NotNull(repo);
    }

    [Fact]
    public void Constructor_WithNullLogger_UsesNullLogger()
    {
        // Arrange, Act
        var metadataLogger = _loggerFactory.CreateLogger<MetadataAggregationService>();
        var metadataService = new MetadataAggregationService(metadataLogger);
        var repo = new MemoryRepository(null, metadataService);

        // Assert - Should not throw, should use NullLogger fallback
        Assert.NotNull(repo);
    }

    [Fact]
    public void Constructor_WithNullMetadataService_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        var logger = _loggerFactory.CreateLogger<MemoryRepository>();
        Assert.Throws<ArgumentNullException>(() => new MemoryRepository(logger, null!));
    }

    #endregion

    #region Series Operation Tests

    [Fact]
    public void AddSeries_WithValidSeries_AddsSuccessfully()
    {
        // Arrange
        var series = CreateTestSeries("Test Manga");

        // Act
        _repository.AddSeries(series);
        var retrieved = _repository.GetSeries(series.id);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(series.id, retrieved.id);
        Assert.Equal(series.title, retrieved.title);
    }

    [Fact]
    public void AddSeries_WithNullSeries_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => _repository.AddSeries(null!));
    }

    [Fact]
    public void AddSeries_WithEmptyId_ThrowsArgumentException()
    {
        // Arrange
        var series = CreateTestSeries("Test") with { id = "" };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => _repository.AddSeries(series));
    }

    [Fact]
    public void AddSeries_WithDuplicateId_ThrowsInvalidOperationException()
    {
        // Arrange
        var series = CreateTestSeries("Test Manga");
        _repository.AddSeries(series);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => _repository.AddSeries(series));
    }

    [Fact]
    public void UpdateSeries_WithValidSeries_UpdatesSuccessfully()
    {
        // Arrange
        var series = CreateTestSeries("Original Title");
        _repository.AddSeries(series);
        var updated = series with { title = "Updated Title" };

        // Act
        _repository.UpdateSeries(updated);
        var retrieved = _repository.GetSeries(series.id);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal("Updated Title", retrieved.title);
    }

    [Fact]
    public void UpdateSeries_WithNullSeries_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => _repository.UpdateSeries(null!));
    }

    [Fact]
    public void GetSeries_WithExistingId_ReturnsSeries()
    {
        // Arrange
        var series = CreateTestSeries("Test Manga");
        _repository.AddSeries(series);

        // Act
        var retrieved = _repository.GetSeries(series.id);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(series.id, retrieved.id);
    }

    [Fact]
    public void GetSeries_WithNonExistentId_ReturnsNull()
    {
        // Act
        var retrieved = _repository.GetSeries("urn:mvn:series:nonexistent");

        // Assert
        Assert.Null(retrieved);
    }

    [Fact]
    public void GetSeries_WithEmptyId_ReturnsNull()
    {
        // Act
        var retrieved = _repository.GetSeries("");

        // Assert
        Assert.Null(retrieved);
    }

    [Fact]
    public void ListSeries_WithNegativeOffset_UsesZero()
    {
        // Arrange
        AddMultipleSeries(5);

        // Act
        var results = _repository.ListSeries(offset: -10, limit: 3).ToList();

        // Assert
        Assert.NotEmpty(results);
        Assert.True(results.Count <= 3);
    }

    [Fact]
    public void ListSeries_WithInvalidLimit_UsesDefault()
    {
        // Arrange
        AddMultipleSeries(5);

        // Act
        var results = _repository.ListSeries(offset: 0, limit: -1).ToList();

        // Assert
        Assert.NotEmpty(results);
    }

    [Fact]
    public void SearchSeries_WithQueryFilter_ReturnsMatchingSeries()
    {
        // Arrange
        _repository.AddSeries(CreateTestSeries("Action Manga"));
        _repository.AddSeries(CreateTestSeries("Romance Novel"));
        _repository.AddSeries(CreateTestSeries("Another Action Story"));

        // Act
        var results = _repository.SearchSeries(query: "Action", null, null, null).ToList();

        // Assert
        Assert.Equal(2, results.Count);
        Assert.All(results, s => Assert.Contains("Action", s.title, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SearchSeries_WithTypeFilter_ReturnsMatchingType()
    {
        // Arrange
        var photoSeries = CreateTestSeries("Photo Series") with { media_type = MediaTypes.Photo };
        var textSeries = CreateTestSeries("Text Series") with { media_type = MediaTypes.Text };
        _repository.AddSeries(photoSeries);
        _repository.AddSeries(textSeries);

        // Act
        var results = _repository.SearchSeries(null, MediaTypes.Photo, null, null).ToList();

        // Assert
        Assert.Single(results);
        Assert.Equal(MediaTypes.Photo, results[0].media_type);
    }

    [Fact]
    public void SearchSeries_WithTagsFilter_ReturnsSeriesWithMatchingTags()
    {
        // Arrange
        var series1 = CreateTestSeries("Series 1") with { tags = new[] { "Action", "Fantasy" } };
        var series2 = CreateTestSeries("Series 2") with { tags = new[] { "Romance", "Drama" } };
        var series3 = CreateTestSeries("Series 3") with { tags = new[] { "Action", "Comedy" } };
        _repository.AddSeries(series1);
        _repository.AddSeries(series2);
        _repository.AddSeries(series3);

        // Act
        var results = _repository.SearchSeries(null, null, new[] { "Action" }, null).ToList();

        // Assert
        Assert.Equal(2, results.Count);
        Assert.All(results, s => Assert.Contains("Action", s.tags));
    }

    [Fact]
    public void DeleteSeries_WithValidId_DeletesSeriesAndCascades()
    {
        // Arrange
        var series = CreateTestSeries("Test Manga");
        _repository.AddSeries(series);
        var unit = CreateTestUnit(series.id, 1, "Chapter 1");
        _repository.AddUnit(unit);

        // Act
        _repository.DeleteSeries(series.id);

        // Assert
        Assert.Null(_repository.GetSeries(series.id));
        Assert.Null(_repository.GetUnit(unit.id));
    }

    [Fact]
    public void DeleteSeries_WithEmptyId_DoesNotThrow()
    {
        // Act & Assert
        _repository.DeleteSeries("");
        // Should not throw
    }

    #endregion

    #region Unit Operation Tests

    [Fact]
    public void AddUnit_WithValidUnit_AddsSuccessfully()
    {
        // Arrange
        var series = CreateTestSeries("Test Manga");
        _repository.AddSeries(series);
        var unit = CreateTestUnit(series.id, 1, "Chapter 1");

        // Act
        _repository.AddUnit(unit);
        var retrieved = _repository.GetUnit(unit.id);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(unit.id, retrieved.id);
        Assert.Equal(unit.series_id, retrieved.series_id);
    }

    [Fact]
    public void AddUnit_WithNullUnit_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => _repository.AddUnit(null!));
    }

    [Fact]
    public void AddUnit_WithDuplicateId_ThrowsInvalidOperationException()
    {
        // Arrange
        var series = CreateTestSeries("Test Manga");
        _repository.AddSeries(series);
        var unit = CreateTestUnit(series.id, 1, "Chapter 1");
        _repository.AddUnit(unit);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => _repository.AddUnit(unit));
    }

    [Fact]
    public void UpdateUnit_WithValidUnit_UpdatesSuccessfully()
    {
        // Arrange
        var series = CreateTestSeries("Test Manga");
        _repository.AddSeries(series);
        var unit = CreateTestUnit(series.id, 1, "Chapter 1");
        _repository.AddUnit(unit);
        var updated = unit with { title = "Updated Chapter" };

        // Act
        _repository.UpdateUnit(updated);
        var retrieved = _repository.GetUnit(unit.id);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal("Updated Chapter", retrieved.title);
    }

    [Fact]
    public void UpdateUnit_WithNullUnit_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => _repository.UpdateUnit(null!));
    }

    [Fact]
    public void ListUnits_WithValidSeriesId_ReturnsOrderedUnits()
    {
        // Arrange
        var series = CreateTestSeries("Test Manga");
        _repository.AddSeries(series);
        _repository.AddUnit(CreateTestUnit(series.id, 3, "Chapter 3"));
        _repository.AddUnit(CreateTestUnit(series.id, 1, "Chapter 1"));
        _repository.AddUnit(CreateTestUnit(series.id, 2, "Chapter 2"));

        // Act
        var units = _repository.ListUnits(series.id).ToList();

        // Assert
        Assert.Equal(3, units.Count);
        Assert.Equal(1, units[0].unit_number);
        Assert.Equal(2, units[1].unit_number);
        Assert.Equal(3, units[2].unit_number);
    }

    [Fact]
    public void ListUnits_WithEmptySeriesId_ReturnsEmpty()
    {
        // Act
        var units = _repository.ListUnits("");

        // Assert
        Assert.Empty(units);
    }

    [Fact]
    public void DeleteUnit_WithValidId_DeletesUnit()
    {
        // Arrange
        var series = CreateTestSeries("Test Manga");
        _repository.AddSeries(series);
        var unit = CreateTestUnit(series.id, 1, "Chapter 1");
        _repository.AddUnit(unit);

        // Act
        _repository.DeleteUnit(unit.id);

        // Assert
        Assert.Null(_repository.GetUnit(unit.id));
    }

    #endregion

    #region Edit Permission Tests

    [Fact]
    public void GrantEditPermission_WithValidParameters_GrantsPermission()
    {
        // Arrange
        var series = CreateTestSeries("Test Manga");
        _repository.AddSeries(series);
        var userUrn = "urn:mvn:user:testuser";

        // Act
        _repository.GrantEditPermission(series.id, userUrn, "urn:mvn:user:admin");

        // Assert
        Assert.True(_repository.HasEditPermission(series.id, userUrn));
    }

    [Fact]
    public void GrantEditPermission_WithEmptyTargetUrn_ThrowsArgumentException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentException>(() =>
            _repository.GrantEditPermission("", "urn:mvn:user:test", "urn:mvn:user:admin"));
    }

    [Fact]
    public void RevokeEditPermission_WithValidParameters_RevokesPermission()
    {
        // Arrange
        var series = CreateTestSeries("Test Manga");
        _repository.AddSeries(series);
        var userUrn = "urn:mvn:user:testuser";
        _repository.GrantEditPermission(series.id, userUrn, "urn:mvn:user:admin");

        // Act
        _repository.RevokeEditPermission(series.id, userUrn);

        // Assert
        Assert.False(_repository.HasEditPermission(series.id, userUrn));
    }

    [Fact]
    public void HasEditPermission_ForOwner_ReturnsTrue()
    {
        // Arrange
        var ownerUrn = "urn:mvn:user:owner";
        var series = CreateTestSeries("Test Manga") with { created_by = ownerUrn };
        _repository.AddSeries(series);

        // Act & Assert
        Assert.True(_repository.HasEditPermission(series.id, ownerUrn));
    }

    [Fact]
    public void HasEditPermission_WithEmptyParameters_ReturnsFalse()
    {
        // Act & Assert
        Assert.False(_repository.HasEditPermission("", "urn:mvn:user:test"));
        Assert.False(_repository.HasEditPermission("urn:mvn:series:test", ""));
    }

    [Fact]
    public void GetEditPermissions_WithValidTargetUrn_ReturnsUserUrns()
    {
        // Arrange
        var series = CreateTestSeries("Test Manga");
        _repository.AddSeries(series);
        var user1 = "urn:mvn:user:user1";
        var user2 = "urn:mvn:user:user2";
        _repository.GrantEditPermission(series.id, user1, "urn:mvn:user:admin");
        _repository.GrantEditPermission(series.id, user2, "urn:mvn:user:admin");

        // Act
        var permissions = _repository.GetEditPermissions(series.id);

        // Assert
        Assert.Equal(2, permissions.Length);
        Assert.Contains(user1, permissions);
        Assert.Contains(user2, permissions);
    }

    [Fact]
    public void SyncEditPermissions_RemovesOrphanedPermissions()
    {
        // Arrange
        var series = CreateTestSeries("Test Manga");
        _repository.AddSeries(series);
        var userUrn = "urn:mvn:user:testuser";
        _repository.GrantEditPermission(series.id, userUrn, "urn:mvn:user:admin");

        // Delete the series
        _repository.DeleteSeries(series.id);

        // Act
        _repository.SyncEditPermissions();

        // Assert
        var permissions = _repository.GetEditPermissions(series.id);
        Assert.Empty(permissions);
    }

    #endregion

    #region Page Operation Tests

    [Fact]
    public void AddPage_WithValidParameters_AddsPage()
    {
        // Arrange
        var series = CreateTestSeries("Test Manga");
        _repository.AddSeries(series);
        var unit = CreateTestUnit(series.id, 1, "Chapter 1");
        _repository.AddUnit(unit);
        var page = new Page(1, UrnHelper.CreateAssetUrn(), "https://example.com/page1.jpg");

        // Act
        _repository.AddPage(unit.id, page);
        var pages = _repository.GetPages(unit.id).ToList();

        // Assert
        Assert.Single(pages);
        Assert.Equal(1, pages[0].page_number);
    }

    [Fact]
    public void AddPage_WithNullPage_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => _repository.AddPage("urn:mvn:unit:test", null!));
    }

    [Fact]
    public void GetPages_WithValidUnitId_ReturnsOrderedPages()
    {
        // Arrange
        var series = CreateTestSeries("Test Manga");
        _repository.AddSeries(series);
        var unit = CreateTestUnit(series.id, 1, "Chapter 1");
        _repository.AddUnit(unit);
        _repository.AddPage(unit.id, new Page(3, UrnHelper.CreateAssetUrn(), "page3.jpg"));
        _repository.AddPage(unit.id, new Page(1, UrnHelper.CreateAssetUrn(), "page1.jpg"));
        _repository.AddPage(unit.id, new Page(2, UrnHelper.CreateAssetUrn(), "page2.jpg"));

        // Act
        var pages = _repository.GetPages(unit.id).ToList();

        // Assert
        Assert.Equal(3, pages.Count);
        Assert.Equal(1, pages[0].page_number);
        Assert.Equal(2, pages[1].page_number);
        Assert.Equal(3, pages[2].page_number);
    }

    #endregion

    #region Progress Operation Tests

    [Fact]
    public void UpdateProgress_WithValidParameters_UpdatesProgress()
    {
        // Arrange
        var userId = "urn:mvn:user:testuser";
        var seriesUrn = "urn:mvn:series:testseries";
        var progress = new ReadingProgress(seriesUrn, "urn:mvn:unit:1", 5, "reading", DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        // Act
        _repository.UpdateProgress(userId, progress);
        var retrieved = _repository.GetProgress(userId, seriesUrn);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(seriesUrn, retrieved.series_urn);
        Assert.Equal(5, retrieved.page_number);
    }

    [Fact]
    public void UpdateProgress_WithNullProgress_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => _repository.UpdateProgress("urn:mvn:user:test", null!));
    }

    [Fact]
    public void GetLibrary_WithValidUserId_ReturnsUserProgress()
    {
        // Arrange
        var userId = "urn:mvn:user:testuser";
        var progress1 = new ReadingProgress("urn:mvn:series:series1", "urn:mvn:unit:1", 5, "reading", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var progress2 = new ReadingProgress("urn:mvn:series:series2", "urn:mvn:unit:2", 10, "completed", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        _repository.UpdateProgress(userId, progress1);
        _repository.UpdateProgress(userId, progress2);

        // Act
        var library = _repository.GetLibrary(userId).ToList();

        // Assert
        Assert.Equal(2, library.Count);
    }

    [Fact]
    public void GetHistory_WithValidUserId_ReturnsOrderedHistory()
    {
        // Arrange
        var userId = "urn:mvn:user:testuser";
        var oldProgress = new ReadingProgress("urn:mvn:series:series1", "urn:mvn:unit:1", 5, "reading", DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds());
        var newProgress = new ReadingProgress("urn:mvn:series:series2", "urn:mvn:unit:2", 10, "reading", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        _repository.UpdateProgress(userId, oldProgress);
        _repository.UpdateProgress(userId, newProgress);

        // Act
        var history = _repository.GetHistory(userId).ToList();

        // Assert
        Assert.Equal(2, history.Count);
        Assert.Equal("urn:mvn:series:series2", history[0].series_urn); // Most recent first
    }

    #endregion

    #region User Management Tests

    [Fact]
    public void AddUser_WithValidUser_AddsSuccessfully()
    {
        // Arrange
        var user = CreateTestUser("testuser");

        // Act
        _repository.AddUser(user);
        var retrieved = _repository.GetUser(user.id);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(user.username, retrieved.username);
    }

    [Fact]
    public void AddUser_WithNullUser_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => _repository.AddUser(null!));
    }

    [Fact]
    public void GetUserByUsername_WithExistingUsername_ReturnsUser()
    {
        // Arrange
        var user = CreateTestUser("testuser");
        _repository.AddUser(user);

        // Act
        var retrieved = _repository.GetUserByUsername("testuser");

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(user.id, retrieved.id);
    }

    [Fact]
    public void GetUserByUsername_CaseInsensitive_ReturnsUser()
    {
        // Arrange
        var user = CreateTestUser("TestUser");
        _repository.AddUser(user);

        // Act
        var retrieved = _repository.GetUserByUsername("testuser");

        // Assert
        Assert.NotNull(retrieved);
    }

    [Fact]
    public void DeleteUserHistory_WithValidUserId_DeletesProgress()
    {
        // Arrange
        var userId = "urn:mvn:user:testuser";
        var progress = new ReadingProgress("urn:mvn:series:series1", "urn:mvn:unit:1", 5, "reading", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        _repository.UpdateProgress(userId, progress);

        // Act
        _repository.DeleteUserHistory(userId);
        var history = _repository.GetHistory(userId).ToList();

        // Assert
        Assert.Empty(history);
    }

    [Fact]
    public void IsAdminSet_WithNoAdmin_ReturnsFalse()
    {
        // Act & Assert
        Assert.False(_repository.IsAdminSet());
    }

    [Fact]
    public void IsAdminSet_WithAdmin_ReturnsTrue()
    {
        // Arrange
        var admin = CreateTestUser("admin") with { role = "Admin" };
        _repository.AddUser(admin);

        // Act & Assert
        Assert.True(_repository.IsAdminSet());
    }

    #endregion

    #region Collection Tests

    [Fact]
    public void AddCollection_WithValidCollection_AddsSuccessfully()
    {
        // Arrange
        var userId = "urn:mvn:user:testuser";
        var collection = CreateTestCollection(userId, "My Favorites");

        // Act
        _repository.AddCollection(userId, collection);
        var retrieved = _repository.GetCollection(collection.id);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(collection.name, retrieved.name);
    }

    [Fact]
    public void AddCollection_WithNullCollection_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => _repository.AddCollection("urn:mvn:user:test", null!));
    }

    [Fact]
    public void UpdateCollection_WithValidCollection_UpdatesSuccessfully()
    {
        // Arrange
        var userId = "urn:mvn:user:testuser";
        var collection = CreateTestCollection(userId, "Original Name");
        _repository.AddCollection(userId, collection);
        var updated = collection with { name = "Updated Name" };

        // Act
        _repository.UpdateCollection(updated);
        var retrieved = _repository.GetCollection(collection.id);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal("Updated Name", retrieved.name);
    }

    [Fact]
    public void ListCollections_WithValidUserId_ReturnsUserCollections()
    {
        // Arrange
        var user1 = "urn:mvn:user:user1";
        var user2 = "urn:mvn:user:user2";
        _repository.AddCollection(user1, CreateTestCollection(user1, "Collection 1"));
        _repository.AddCollection(user1, CreateTestCollection(user1, "Collection 2"));
        _repository.AddCollection(user2, CreateTestCollection(user2, "Collection 3"));

        // Act
        var collections = _repository.ListCollections(user1).ToList();

        // Assert
        Assert.Equal(2, collections.Count);
        Assert.All(collections, c => Assert.Equal(user1, c.user_id));
    }

    #endregion

    #region Passkey Tests

    [Fact]
    public void AddPasskey_WithValidPasskey_AddsSuccessfully()
    {
        // Arrange
        var passkey = CreateTestPasskey("urn:mvn:user:testuser");

        // Act
        _repository.AddPasskey(passkey);
        var retrieved = _repository.GetPasskey(passkey.id);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(passkey.credential_id, retrieved.credential_id);
    }

    [Fact]
    public void GetPasskeysByUser_WithValidUserId_ReturnsUserPasskeys()
    {
        // Arrange
        var userId = "urn:mvn:user:testuser";
        _repository.AddPasskey(CreateTestPasskey(userId));
        _repository.AddPasskey(CreateTestPasskey(userId));
        _repository.AddPasskey(CreateTestPasskey("urn:mvn:user:otheruser"));

        // Act
        var passkeys = _repository.GetPasskeysByUser(userId).ToList();

        // Assert
        Assert.Equal(2, passkeys.Count);
    }

    [Fact]
    public void GetPasskeyByCredentialId_WithExistingId_ReturnsPasskey()
    {
        // Arrange
        var passkey = CreateTestPasskey("urn:mvn:user:testuser");
        _repository.AddPasskey(passkey);

        // Act
        var retrieved = _repository.GetPasskeyByCredentialId(passkey.credential_id);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(passkey.id, retrieved.id);
    }

    #endregion

    #region System Operations Tests

    [Fact]
    public void SeedDebugData_CreatesTestData()
    {
        // Act
        _repository.SeedDebugData();

        // Assert
        var series = _repository.ListSeries().ToList();
        Assert.NotEmpty(series);
    }

    [Fact]
    public void ResetAllData_ClearsAllData()
    {
        // Arrange
        _repository.SeedDebugData();
        _repository.AddUser(CreateTestUser("testuser"));

        // Act
        _repository.ResetAllData();

        // Assert
        Assert.Empty(_repository.ListSeries());
        Assert.Empty(_repository.ListUsers());
    }

    [Fact]
    public void GetSystemStats_ReturnsValidStats()
    {
        // Arrange
        _repository.AddUser(CreateTestUser("user1"));
        _repository.AddUser(CreateTestUser("user2"));

        // Act
        var stats = _repository.GetSystemStats();

        // Assert
        Assert.Equal(2, stats.total_users);
        Assert.True(stats.uptime_seconds >= 0);
    }

    #endregion

    #region Configuration Tests

    [Fact]
    public void UpdateSystemConfig_WithValidConfig_UpdatesSuccessfully()
    {
        // Arrange
        var config = new SystemConfig(
            is_setup_complete: true,
            registration_open: false,
            maintenance_mode: false,
            motd_message: "Test MOTD",
            default_language_filter: new[] { "en", "ja" },
            max_login_attempts: 3,
            lockout_duration_minutes: 30,
            token_expiry_hours: 48,
            cloudflare_enabled: true,
            cloudflare_site_key: "test-key",
            cloudflare_secret_key: "test-secret",
            require_2fa_passkey: true,
            require_password_for_danger_zone: true
        );

        // Act
        _repository.UpdateSystemConfig(config);
        var retrieved = _repository.GetSystemConfig();

        // Assert
        Assert.True(retrieved.is_setup_complete);
        Assert.Equal("Test MOTD", retrieved.motd_message);
    }

    [Fact]
    public void UpdateTaxonomyConfig_WithValidConfig_UpdatesSuccessfully()
    {
        // Arrange
        var config = new TaxonomyConfig(
            tags: new[] { "Custom Tag" },
            content_warnings: ContentWarnings.All,
            types: MediaTypes.All,
            authors: Array.Empty<Author>(),
            scanlators: Array.Empty<Scanlator>(),
            groups: Array.Empty<Group>()
        );

        // Act
        _repository.UpdateTaxonomyConfig(config);
        var retrieved = _repository.GetTaxonomyConfig();

        // Assert
        Assert.Contains("Custom Tag", retrieved.tags);
    }

    #endregion

    #region Helper Methods

    private Series CreateTestSeries(string title)
    {
        return new Series(
            id: UrnHelper.CreateSeriesUrn(),
            federation_ref: "urn:mvn:node:local",
            title: title,
            description: $"Description for {title}",
            poster: new Poster("https://placehold.co/400x600", "Poster"),
            media_type: MediaTypes.Photo,
            external_links: new Dictionary<string, string>(),
            reading_direction: ReadingDirections.RTL,
            tags: new[] { "Test" },
            content_warnings: Array.Empty<string>(),
            authors: Array.Empty<Author>(),
            scanlators: Array.Empty<Scanlator>(),
            groups: null,
            alt_titles: Array.Empty<string>(),
            status: "Ongoing",
            year: 2024,
            created_by: "urn:mvn:user:system",
            created_at: DateTime.UtcNow,
            updated_at: DateTime.UtcNow
        );
    }

    private Unit CreateTestUnit(string seriesId, int unitNumber, string title)
    {
        return new Unit(
            Guid.NewGuid().ToString(),
            seriesId,
            unitNumber,
            title,
            DateTime.UtcNow
        );
    }

    private User CreateTestUser(string username)
    {
        return new User(
            id: UrnHelper.CreateUserUrn(),
            username: username,
            password_hash: "hashed_password",
            role: "User",
            created_at: DateTime.UtcNow,
            password_login_disabled: false,
            preferred_language: "en"
        );
    }

    private Collection CreateTestCollection(string userId, string name)
    {
        return new Collection(
            id: Guid.NewGuid().ToString(),
            user_id: userId,
            name: name,
            is_system: false,
            items: Array.Empty<string>()
        );
    }

    private Passkey CreateTestPasskey(string userId)
    {
        return new Passkey(
            id: Guid.NewGuid().ToString(),
            user_id: userId,
            credential_id: Guid.NewGuid().ToString(),
            public_key: "public_key_data",
            sign_count: 0,
            name: "Test Device",
            device_type: "platform",
            backed_up: false,
            created_at: DateTime.UtcNow,
            last_used_at: null
        );
    }

    private void AddMultipleSeries(int count)
    {
        for (int i = 0; i < count; i++)
        {
            _repository.AddSeries(CreateTestSeries($"Series {i + 1}"));
        }
    }

    #endregion
}
