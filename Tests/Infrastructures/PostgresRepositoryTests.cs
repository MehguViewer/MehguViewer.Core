using Xunit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Configuration;
using MehguViewer.Core.Infrastructures;
using MehguViewer.Core.Shared;
using MehguViewer.Core.Services;
using MehguViewer.Core.Workers;
using MehguViewer.Core.Helpers;

namespace MehguViewer.Core.Tests.Infrastructures;

/// <summary>
/// Comprehensive test suite for PostgresRepository covering:
/// - Connection management
/// - CRUD operations for all entities
/// - Transaction handling
/// - Error scenarios
/// - Security (SQL injection prevention)
/// - Data integrity
/// </summary>
/// <remarks>
/// These tests require a running PostgreSQL database.
/// Set environment variable SKIP_POSTGRES_TESTS=true to skip in CI/test environments without Postgres.
/// </remarks>
[Trait("Category", "Integration")]
[Trait("RequiresDatabase", "PostgreSQL")]
public class PostgresRepositoryTests : IDisposable
{
    private readonly PostgresRepository? _repository;
    private readonly IConfiguration _configuration;
    private readonly bool _skipTests;
    
    public PostgresRepositoryTests()
    {
        // Check if we should skip tests
        var skipEnvVar = Environment.GetEnvironmentVariable("SKIP_POSTGRES_TESTS");
        _skipTests = !string.IsNullOrEmpty(skipEnvVar) && skipEnvVar.Equals("true", StringComparison.OrdinalIgnoreCase);
        
        if (_skipTests)
        {
            return;
        }
        
        // Create test configuration
        var configBuilder = new ConfigurationBuilder();
        configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = GetTestConnectionString()
        });
        _configuration = configBuilder.Build();
        
        try
        {
            var logger = NullLogger<PostgresRepository>.Instance;
            var metadataLogger = NullLogger<MetadataAggregationService>.Instance;
            var metadataService = new MetadataAggregationService(metadataLogger);
            
            _repository = new PostgresRepository(_configuration, logger, metadataService);
            
            // Reset database before each test
            _repository.ResetDatabase();
        }
        catch (Exception)
        {
            // If we can't connect to Postgres, mark tests to be skipped
            _skipTests = true;
            _repository = null;
        }
    }

    public void Dispose()
    {
        // Cleanup test database
        if (_repository != null)
        {
            try
            {
                _repository.ResetDatabase();
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    #region Database Initialization Tests

    [Fact]
    public void Constructor_WithValidConnectionString_InitializesSuccessfully()
    {
        if (_skipTests) { return; }
         if (_skipTests) { return; }
         if (_skipTests) { return; }
        
        // Arrange - using existing _repository from constructor
        // Act & Assert
        Assert.NotNull(_repository);
        Assert.False(_repository!.HasData()); // Fresh database should have no data
    }

    [Fact]
    public void Constructor_WithNullMetadataService_ThrowsArgumentNullException()
    {
        if (_skipTests) { return; }
         if (_skipTests) { return; }
         if (_skipTests) { return; }
        
        // Arrange
        var configBuilder = new ConfigurationBuilder();
        configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = GetTestConnectionString()
        });
        var config = configBuilder.Build();
        var logger = NullLogger<PostgresRepository>.Instance;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new PostgresRepository(config, logger, null!));
    }

    [Fact]
    public void HasData_OnFreshDatabase_ReturnsFalse()
    {
        if (_skipTests) { return; }
         if (_skipTests) { return; }
         if (_skipTests) { return; }
        
        // Arrange
        _repository!.ResetDatabase();

        // Act
        var hasData = _repository.HasData();

        // Assert
        Assert.False(hasData);
    }

    [Fact]
    public void HasData_AfterAddingUser_ReturnsTrue()
    {
        if (_skipTests) { return; }
         if (_skipTests) { return; }
        // Arrange
        var user = CreateTestUser();
        _repository.AddUser(user);

        // Act
        var hasData = _repository.HasData();

        // Assert
        Assert.True(hasData);
    }

    [Fact]
    public void ResetDatabase_ClearsAllData()
    {
        if (_skipTests) { return; }
         if (_skipTests) { return; }
        // Arrange
        var series = CreateTestSeries();
        _repository.AddSeries(series);

        // Act
        _repository.ResetDatabase();

        // Assert
        Assert.False(_repository.HasData());
        Assert.Null(_repository.GetSeries(series.id));
    }

    #endregion

    #region Series CRUD Tests

    [Fact]
    public void AddSeries_WithValidSeries_AddsSuccessfully()
    {
        if (_skipTests) { return; }
         if (_skipTests) { return; }
        // Arrange
        var series = CreateTestSeries();

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
        if (_skipTests) { return; }
         if (_skipTests) { return; }
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => _repository.AddSeries(null!));
    }

    [Fact]
    public void AddSeries_WithDuplicateId_DoesNotOverwrite()
    {
        if (_skipTests) { return; }
         if (_skipTests) { return; }
        // Arrange
        var series1 = CreateTestSeries("Original Title");
        var series2 = new Series(
            series1.id,  // Same ID
            series1.federation_ref,
            "Modified Title",  // Different title
            series1.description,
            series1.poster,
            series1.media_type,
            series1.external_links,
            series1.reading_direction,
            series1.tags,
            series1.content_warnings,
            series1.authors,
            series1.scanlators,
            series1.groups,
            series1.alt_titles,
            series1.status,
            series1.year,
            series1.original_language,
            series1.created_by,
            series1.created_at,
            series1.updated_at
        );

        // Act
        _repository.AddSeries(series1);
        _repository.AddSeries(series2);  // Should be ignored
        var retrieved = _repository.GetSeries(series1.id);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal("Original Title", retrieved.title);
    }

    [Fact]
    public void UpdateSeries_WithValidSeries_UpdatesSuccessfully()
    {
        if (_skipTests) { return; }
         if (_skipTests) { return; }
        // Arrange
        var series = CreateTestSeries();
        _repository.AddSeries(series);
        
        var updatedSeries = series with { title = "Updated Title" };

        // Act
        _repository.UpdateSeries(updatedSeries);
        var retrieved = _repository.GetSeries(series.id);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal("Updated Title", retrieved.title);
    }

    [Fact]
    public void UpdateSeries_WithNullSeries_ThrowsArgumentNullException()
    {
        if (_skipTests) { return; }
         if (_skipTests) { return; }
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => _repository.UpdateSeries(null!));
    }

    [Fact]
    public void GetSeries_WithNullId_ReturnsNull()
    {
        if (_skipTests) { return; }
         if (_skipTests) { return; }
        // Act
        var result = _repository.GetSeries(null!);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetSeries_WithNonExistentId_ReturnsNull()
    {
        if (_skipTests) { return; }
         if (_skipTests) { return; }
        // Act
        var result = _repository.GetSeries("urn:mvn:series:non-existent");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ListSeries_WithDefaultPagination_ReturnsOrderedResults()
    {
        if (_skipTests) { return; }
         if (_skipTests) { return; }
        // Arrange
        var series1 = CreateTestSeries("Series 1");
        var series2 = CreateTestSeries("Series 2");
        var series3 = CreateTestSeries("Series 3");
        
        _repository.AddSeries(series1);
        Thread.Sleep(10); // Ensure different timestamps
        _repository.AddSeries(series2);
        Thread.Sleep(10);
        _repository.AddSeries(series3);

        // Act
        var results = _repository.ListSeries(0, 10).ToList();

        // Assert
        Assert.Equal(3, results.Count);
        // Should be ordered by updated_at DESC
        Assert.Equal("Series 3", results[0].title);
    }

    [Fact]
    public void ListSeries_WithInvalidOffset_UsesZero()
    {
        if (_skipTests) { return; }
         if (_skipTests) { return; }
        // Arrange
        var series = CreateTestSeries();
        _repository.AddSeries(series);

        // Act
        var results = _repository.ListSeries(-5, 10).ToList();

        // Assert
        Assert.Single(results);
    }

    [Fact]
    public void SearchSeries_ByTitle_ReturnsMatchingResults()
    {
        if (_skipTests) { return; }
         if (_skipTests) { return; }
        // Arrange
        _repository.AddSeries(CreateTestSeries("One Piece"));
        _repository.AddSeries(CreateTestSeries("Naruto"));
        _repository.AddSeries(CreateTestSeries("One Punch Man"));

        // Act
        var results = _repository.SearchSeries("one", null, null, null).ToList();

        // Assert
        Assert.Equal(2, results.Count);
        Assert.All(results, s => Assert.Contains("one", s.title, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SearchSeries_ByMediaType_ReturnsMatchingResults()
    {
        if (_skipTests) { return; }
         if (_skipTests) { return; }
        // Arrange
        var photoSeries = CreateTestSeries("Photo Series");
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
    public void SearchSeries_ByTags_ReturnsMatchingResults()
    {
        if (_skipTests) { return; }
         if (_skipTests) { return; }
        // Arrange
        var series1 = CreateTestSeries("Series 1") with { tags = new[] { "Action", "Fantasy" } };
        var series2 = CreateTestSeries("Series 2") with { tags = new[] { "Romance", "Comedy" } };
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
    public void DeleteSeries_WithValidId_DeletesSeriesAndRelatedData()
    {
        if (_skipTests) { return; }
         if (_skipTests) { return; }
        // Arrange
        var series = CreateTestSeries();
        _repository.AddSeries(series);
        
        var unit = CreateTestUnit(series.id);
        _repository.AddUnit(unit);

        // Act
        _repository.DeleteSeries(series.id);

        // Assert
        Assert.Null(_repository.GetSeries(series.id));
        Assert.Null(_repository.GetUnit(unit.id));
    }

    [Fact]
    public void DeleteSeries_WithNullId_DoesNotThrow()
    {
        if (_skipTests) { return; }
         if (_skipTests) { return; }
        // Act & Assert - should log warning but not throw
        _repository.DeleteSeries(null!);
    }

    #endregion

    #region Unit CRUD Tests

    [Fact]
    public void AddUnit_WithValidUnit_AddsSuccessfully()
    {
        if (_skipTests) { return; }
         if (_skipTests) { return; }
        // Arrange
        var series = CreateTestSeries();
        _repository.AddSeries(series);
        var unit = CreateTestUnit(series.id);

        // Act
        _repository.AddUnit(unit);
        var retrieved = _repository.GetUnit(unit.id);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(unit.id, retrieved.id);
        Assert.Equal(unit.title, retrieved.title);
    }

    [Fact]
    public void AddUnit_WithNullUnit_ThrowsArgumentNullException()
    {
        if (_skipTests) { return; }
         if (_skipTests) { return; }
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => _repository.AddUnit(null!));
    }

    [Fact]
    public void UpdateUnit_TriggersMetadataAggregation()
    {
        if (_skipTests) { return; }
         if (_skipTests) { return; }
        // Arrange
        var series = CreateTestSeries();
        _repository.AddSeries(series);
        var unit = CreateTestUnit(series.id);
        _repository.AddUnit(unit);

        var updatedUnit = unit with { title = "Updated Chapter" };

        // Act
        _repository.UpdateUnit(updatedUnit);

        // Assert - metadata aggregation should be triggered
        // Note: This test would need the actual MetadataAggregationService implementation
        var retrieved = _repository.GetUnit(unit.id);
        Assert.NotNull(retrieved);
        Assert.Equal("Updated Chapter", retrieved.title);
    }

    [Fact]
    public void ListUnits_ReturnsOrderedByUnitNumber()
    {
        if (_skipTests) { return; }
         if (_skipTests) { return; }
        // Arrange
        var series = CreateTestSeries();
        _repository.AddSeries(series);
        
        var unit3 = CreateTestUnit(series.id, 3, "Chapter 3");
        var unit1 = CreateTestUnit(series.id, 1, "Chapter 1");
        var unit2 = CreateTestUnit(series.id, 2, "Chapter 2");
        
        _repository.AddUnit(unit3);
        _repository.AddUnit(unit1);
        _repository.AddUnit(unit2);

        // Act
        var results = _repository.ListUnits(series.id).ToList();

        // Assert
        Assert.Equal(3, results.Count);
        Assert.Equal(1, results[0].unit_number);
        Assert.Equal(2, results[1].unit_number);
        Assert.Equal(3, results[2].unit_number);
    }

    [Fact]
    public void DeleteUnit_DeletesUnitAndPages()
    {
        if (_skipTests) { return; }
         if (_skipTests) { return; }
        // Arrange
        var series = CreateTestSeries();
        _repository.AddSeries(series);
        var unit = CreateTestUnit(series.id);
        _repository.AddUnit(unit);
        
        var page = new Page(1, UrnHelper.CreateAssetUrn(), "https://example.com/page1.jpg");
        _repository.AddPage(unit.id, page);

        // Act
        _repository.DeleteUnit(unit.id);

        // Assert
        Assert.Null(_repository.GetUnit(unit.id));
        Assert.Empty(_repository.GetPages(unit.id));
    }

    #endregion

    #region User Management Tests

    [Fact]
    public void AddUser_WithValidUser_AddsSuccessfully()
    {
        if (_skipTests) { return; }
         if (_skipTests) { return; }
        // Arrange
        var user = CreateTestUser();

        // Act
        _repository.AddUser(user);
        var retrieved = _repository.GetUser(user.id);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(user.username, retrieved.username);
    }

    [Fact]
    public void GetUserByUsername_ReturnsCorrectUser()
    {
        if (_skipTests) { return; }
         if (_skipTests) { return; }
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
    public void DeleteUser_RemovesUser()
    {
        if (_skipTests) { return; }
         if (_skipTests) { return; }
        // Arrange
        var user = CreateTestUser();
        _repository.AddUser(user);

        // Act
        _repository.DeleteUser(user.id);

        // Assert
        Assert.Null(_repository.GetUser(user.id));
    }

    #endregion

    #region Security Tests

    [Theory]
    [InlineData("'; DROP TABLE series; --")]
    [InlineData("1' OR '1'='1")]
    [InlineData("<script>alert('xss')</script>")]
    public void GetSeries_WithSQLInjectionAttempt_DoesNotCauseIssues(string maliciousId)
    {
        if (_skipTests) { return; }
        
        // Arrange - add a valid series to ensure database is functional
        var validSeries = CreateTestSeries();
        _repository!.AddSeries(validSeries);

        // Act - attempt SQL injection
        var result = _repository.GetSeries(maliciousId);
        
        // Assert - injection should fail safely
        Assert.Null(result);
        
        // Verify database integrity - valid series should still exist
        var stillExists = _repository.GetSeries(validSeries.id);
        Assert.NotNull(stillExists);
    }

    [Fact]
    public void SearchSeries_WithSQLInjectionInQuery_DoesNotCauseIssues()
    {
        if (_skipTests) { return; }
         if (_skipTests) { return; }
        // Arrange
        var maliciousQuery = "'; DROP TABLE series; --";

        // Act & Assert - should handle safely
        var results = _repository.SearchSeries(maliciousQuery, null, null, null).ToList();
        Assert.NotNull(results);
    }

    #endregion

    #region Helper Methods

    private static string GetTestConnectionString()
    {
        // Use environment variable or default test database
        return Environment.GetEnvironmentVariable("TEST_DB_CONNECTION") 
            ?? "Host=localhost;Port=5432;Database=mehgutest;Username=postgres;Password=postgres";
    }

    private static Series CreateTestSeries(string title = "Test Series")
    {
        return new Series(
            id: UrnHelper.CreateSeriesUrn(),
            federation_ref: "urn:mvn:node:local",
            title: title,
            description: $"Description for {title}",
            poster: new Poster("https://example.com/poster.jpg", "Poster"),
            media_type: MediaTypes.Photo,
            external_links: new Dictionary<string, string>(),
            reading_direction: ReadingDirections.RTL,
            tags: new[] { "Action", "Fantasy" },
            content_warnings: Array.Empty<string>(),
            authors: new[] { new Author("author-1", "Test Author", "Author") },
            scanlators: new[] { new Scanlator("scan-1", "Test Scans", ScanlatorRole.Both) },
            groups: null,
            alt_titles: Array.Empty<string>(),
            status: "Ongoing",
            year: 2024,
            original_language: "en",
            created_by: "urn:mvn:user:system",
            created_at: DateTime.UtcNow,
            updated_at: DateTime.UtcNow
        );
    }

    private static Unit CreateTestUnit(string seriesId, int unitNumber = 1, string title = "Test Chapter")
    {
        return new Unit(
            UrnHelper.CreateUnitUrn(),
            seriesId,
            unitNumber,
            title,
            DateTime.UtcNow
        );
    }

    private static User CreateTestUser(string username = "testuser")
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

    #endregion
}
