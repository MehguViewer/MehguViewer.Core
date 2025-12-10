using Xunit;
using MehguViewer.Core.Shared;
using MehguViewer.Core.Services;
using MehguViewer.Core.Workers;
using MehguViewer.Core.Helpers;
using MehguViewer.Core.Infrastructures;
using Microsoft.Extensions.Logging;

namespace MehguViewer.Core.Tests.Services;

/// <summary>
/// Extended test suite for TaxonomyValidationService focusing on edge cases,
/// error handling, null safety, and advanced scenarios.
/// </summary>
public class TaxonomyValidationServiceEdgeCaseTests
{
    private readonly MemoryRepository _repo;
    private readonly TaxonomyValidationService _service;

    public TaxonomyValidationServiceEdgeCaseTests()
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var repoLogger = loggerFactory.CreateLogger<MemoryRepository>();
        var metadataLogger = loggerFactory.CreateLogger<MetadataAggregationService>();
        var validationLogger = loggerFactory.CreateLogger<TaxonomyValidationService>();
        
        var metadataService = new MetadataAggregationService(metadataLogger);
        _repo = new MemoryRepository(repoLogger, metadataService);
        _service = new TaxonomyValidationService(_repo, validationLogger);

        // Setup default taxonomy config
        var defaultConfig = new TaxonomyConfig(
            tags: new[] { "action", "comedy", "drama", "fantasy", "romance" },
            content_warnings: new[] { "nsfw", "gore", "violence" },
            types: new[] { "Photo", "Text", "Video" },
            authors: new[]
            {
                new Author("author-1", "John Doe", "Author"),
                new Author("author-2", "Jane Smith", "Artist")
            },
            scanlators: new[]
            {
                new Scanlator("scanlator-1", "Group A", ScanlatorRole.Translation),
                new Scanlator("scanlator-2", "Group B", ScanlatorRole.Both)
            },
            groups: new[]
            {
                new Group("group-1", "Publisher A"),
                new Group("group-2", "Publisher B")
            }
        );

        _repo.UpdateTaxonomyConfig(defaultConfig);
    }

    #region Tag Validation Edge Cases

    [Fact]
    public void ValidateTags_WithNullArray_ReturnsValid()
    {
        // Act
        var result = _service.ValidateTags(null);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.ValidTags);
        Assert.Empty(result.InvalidTags);
        Assert.Empty(result.Suggestions);
    }

    [Fact]
    public void ValidateTags_WithWhitespaceOnlyTags_IgnoresThem()
    {
        // Arrange
        var tags = new[] { "action", "  ", "\t", "\n", "comedy" };

        // Act
        var result = _service.ValidateTags(tags);

        // Assert
        Assert.True(result.IsValid);
        Assert.Equal(2, result.ValidTags.Length); // Only "action" and "comedy"
        Assert.Contains("action", result.ValidTags);
        Assert.Contains("comedy", result.ValidTags);
    }

    [Fact]
    public void ValidateTags_WithMixedCaseAndWhitespace_NormalizesCorrectly()
    {
        // Arrange
        var tags = new[] { "  ACTION  ", "CoMeDy  ", "  DRAmA" };

        // Act
        var result = _service.ValidateTags(tags);

        // Assert
        Assert.True(result.IsValid);
        Assert.Equal(3, result.ValidTags.Length);
    }

    [Fact]
    public void ValidateTags_WithDuplicateTags_ValidatesAll()
    {
        // Arrange
        var tags = new[] { "action", "action", "comedy", "action" };

        // Act
        var result = _service.ValidateTags(tags);

        // Assert
        Assert.True(result.IsValid);
        Assert.Equal(4, result.ValidTags.Length); // All instances counted
    }

    [Fact]
    public void ValidateTags_WithVeryLongTag_HandlesGracefully()
    {
        // Arrange
        var veryLongTag = new string('a', 1000);
        var tags = new[] { "action", veryLongTag };

        // Act
        var result = _service.ValidateTags(tags);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.ValidTags);
        Assert.Contains(veryLongTag, result.InvalidTags);
    }

    [Fact]
    public void ValidateTags_WithSpecialCharacters_ValidatesCorrectly()
    {
        // Arrange
        var tags = new[] { "action", "tag-with-dashes", "tag_with_underscores", "tag.with.dots" };

        // Act
        var result = _service.ValidateTags(tags);

        // Assert
        Assert.Single(result.ValidTags); // Only "action" is valid
        Assert.Equal(3, result.InvalidTags.Length);
    }

    [Fact]
    public void ValidateTags_WithUnicodeCharacters_HandlesCorrectly()
    {
        // Arrange
        var tags = new[] { "action", "アクション", "动作", "🎬" };

        // Act
        var result = _service.ValidateTags(tags);

        // Assert
        Assert.Single(result.ValidTags); // Only "action" is valid
        Assert.Equal(3, result.InvalidTags.Length);
    }

    [Fact]
    public void ValidateTags_WithSimilarTagsNearEditDistance_ProvidesSuggestions()
    {
        // Arrange - Tags with 1-3 character differences
        var tags = new[] { "actio", "actin", "ction" };

        // Act
        var result = _service.ValidateTags(tags);

        // Assert
        Assert.False(result.IsValid);
        Assert.True(result.Suggestions.ContainsKey("actio"));
        Assert.True(result.Suggestions.ContainsKey("actin"));
    }

    [Fact]
    public void ValidateTags_WithTagsBeyondEditDistance_NoSuggestions()
    {
        // Arrange - Tags with >3 character differences
        var tags = new[] { "xyz", "completely-different" };

        // Act
        var result = _service.ValidateTags(tags);

        // Assert
        Assert.False(result.IsValid);
        Assert.False(result.Suggestions.ContainsKey("xyz"));
        Assert.False(result.Suggestions.ContainsKey("completely-different"));
    }

    #endregion

    #region Entity Validation Edge Cases

    [Fact]
    public void ValidateAuthors_WithNullArray_ReturnsValid()
    {
        // Act
        var result = _service.ValidateAuthors(null);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.ValidIds);
        Assert.Empty(result.InvalidIds);
    }

    [Fact]
    public void ValidateAuthors_WithEmptyIdAuthor_MarksAsInvalid()
    {
        // Arrange
        var authors = new[]
        {
            new Author("", "Empty ID Author", "Author"),
            new Author("author-1", "Valid Author", "Author")
        };

        // Act
        var result = _service.ValidateAuthors(authors);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.ValidIds);
        Assert.Single(result.InvalidIds);
    }

    [Fact]
    public void ValidateAuthors_WithWhitespaceOnlyId_MarksAsInvalid()
    {
        // Arrange
        var authors = new[]
        {
            new Author("   ", "Whitespace ID", "Author"),
            new Author("author-1", "Valid Author", "Author")
        };

        // Act
        var result = _service.ValidateAuthors(authors);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.ValidIds);
        Assert.Single(result.InvalidIds);
    }

    [Fact]
    public void ValidateScanlators_WithNullArray_ReturnsValid()
    {
        // Act
        var result = _service.ValidateScanlators(null);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.ValidIds);
        Assert.Empty(result.InvalidIds);
    }

    [Fact]
    public void ValidateScanlators_WithEmptyIdScanlator_MarksAsInvalid()
    {
        // Arrange
        var scanlators = new[]
        {
            new Scanlator("", "Empty ID Group", ScanlatorRole.Translation),
            new Scanlator("scanlator-1", "Valid Group", ScanlatorRole.Translation)
        };

        // Act
        var result = _service.ValidateScanlators(scanlators);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.ValidIds);
        Assert.Single(result.InvalidIds);
    }

    [Fact]
    public void ValidateGroups_WithNullArray_ReturnsValid()
    {
        // Act
        var result = _service.ValidateGroups(null);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.ValidIds);
        Assert.Empty(result.InvalidIds);
    }

    [Fact]
    public void ValidateGroups_WithEmptyIdGroup_MarksAsInvalid()
    {
        // Arrange
        var groups = new[]
        {
            new Group("", "Empty ID Publisher"),
            new Group("group-1", "Valid Publisher")
        };

        // Act
        var result = _service.ValidateGroups(groups);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.ValidIds);
        Assert.Single(result.InvalidIds);
    }

    #endregion

    #region Series Validation Edge Cases

    [Fact]
    public void ValidateSeries_WithNullSeries_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => _service.ValidateSeries(null!));
        Assert.Equal("series", exception.ParamName);
    }

    [Fact]
    public void ValidateSeries_WithNullTagsArray_ReturnsValid()
    {
        // Arrange
        var series = CreateTestSeries(tags: null);

        // Act
        var result = _service.ValidateSeries(series);

        // Assert
        Assert.True(result.Tags.IsValid);
    }

    [Fact]
    public void ValidateSeries_WithNullAuthorsArray_ReturnsValid()
    {
        // Arrange
        var series = CreateTestSeries(authors: null);

        // Act
        var result = _service.ValidateSeries(series);

        // Assert
        Assert.True(result.Authors.IsValid);
    }

    [Fact]
    public void ValidateSeries_WithNullScanlatorsArray_ReturnsValid()
    {
        // Arrange
        var series = CreateTestSeries(scanlators: null);

        // Act
        var result = _service.ValidateSeries(series);

        // Assert
        Assert.True(result.Scanlators.IsValid);
    }

    [Fact]
    public void ValidateSeries_WithNullGroupsArray_ReturnsValid()
    {
        // Arrange
        var series = CreateTestSeries(groups: null);

        // Act
        var result = _service.ValidateSeries(series);

        // Assert
        Assert.True(result.Groups.IsValid);
    }

    [Fact]
    public void ValidateSeries_WithAllEmptyArrays_ReturnsValid()
    {
        // Arrange
        var series = CreateTestSeries(
            tags: Array.Empty<string>(),
            authors: Array.Empty<Author>(),
            scanlators: Array.Empty<Scanlator>(),
            groups: Array.Empty<Group>()
        );

        // Act
        var result = _service.ValidateSeries(series);

        // Assert
        Assert.True(result.IsValid);
    }

    #endregion

    #region Unit Validation Edge Cases

    [Fact]
    public void ValidateUnit_WithNullUnit_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => _service.ValidateUnit(null!));
        Assert.Equal("unit", exception.ParamName);
    }

    [Fact]
    public void ValidateUnit_WithNullLocalizedDictionary_ReturnsValid()
    {
        // Arrange
        var unit = CreateTestUnit(localized: null);

        // Act
        var result = _service.ValidateUnit(unit);

        // Assert
        Assert.True(result.Scanlators.IsValid);
    }

    [Fact]
    public void ValidateUnit_WithEmptyLocalizedDictionary_ReturnsValid()
    {
        // Arrange
        var unit = CreateTestUnit(localized: new Dictionary<string, UnitLocalizedMetadata>());

        // Act
        var result = _service.ValidateUnit(unit);

        // Assert
        Assert.True(result.Scanlators.IsValid);
    }

    [Fact]
    public void ValidateUnit_WithMultipleLanguagesWithScanlators_ValidatesAll()
    {
        // Arrange
        var unit = CreateTestUnit(localized: new Dictionary<string, UnitLocalizedMetadata>
        {
            ["en"] = new UnitLocalizedMetadata(
                title: "Chapter 1",
                scanlators: new[] { new Scanlator("scanlator-1", "Group A", ScanlatorRole.Translation) },
                content_folder: null
            ),
            ["es"] = new UnitLocalizedMetadata(
                title: "Capítulo 1",
                scanlators: new[] { new Scanlator("scanlator-2", "Group B", ScanlatorRole.Translation) },
                content_folder: null
            )
        });

        // Act
        var result = _service.ValidateUnit(unit);

        // Assert
        Assert.True(result.IsValid);
        Assert.Equal(2, result.Scanlators.ValidIds.Length);
    }

    [Fact]
    public void ValidateUnit_WithLocalizedMetadataButNullScanlators_ReturnsValid()
    {
        // Arrange
        var unit = CreateTestUnit(localized: new Dictionary<string, UnitLocalizedMetadata>
        {
            ["en"] = new UnitLocalizedMetadata(
                title: "Chapter 1",
                scanlators: null,
                content_folder: null
            )
        });

        // Act
        var result = _service.ValidateUnit(unit);

        // Assert
        Assert.True(result.Scanlators.IsValid);
    }

    #endregion

    #region Full Validation Edge Cases

    [Fact]
    public async Task RunFullValidationAsync_WithEmptyLibrary_ReturnsEmptyReport()
    {
        // Arrange - Repository is already empty

        // Act
        var report = await _service.RunFullValidationAsync();

        // Assert
        Assert.NotNull(report);
        Assert.Equal(0, report.TotalSeries);
        Assert.Equal(0, report.TotalUnits);
        Assert.Empty(report.SeriesIssues);
        Assert.Empty(report.UnitIssues);
    }

    [Fact]
    public async Task RunFullValidationAsync_CalledTwiceQuickly_UsesCacheOnSecondCall()
    {
        // Arrange
        var series = CreateTestSeries();
        _repo.AddSeries(series);

        // Act
        var report1 = await _service.RunFullValidationAsync();
        var report2 = await _service.RunFullValidationAsync();

        // Assert
        Assert.NotNull(report1);
        Assert.NotNull(report2);
        Assert.Equal(1, report1.TotalSeries);
        Assert.Equal(0, report2.TotalSeries); // Cached result
        Assert.Contains("Cached", report2.Summary);
    }

    [Fact]
    public async Task RunFullValidationAsync_WithMixedValidAndInvalidSeries_ReportsOnlyInvalid()
    {
        // Arrange
        var validSeries = CreateTestSeries(id: "urn:mvn:series:valid");
        var invalidSeries = CreateTestSeries(
            id: "urn:mvn:series:invalid",
            tags: new[] { "invalid-tag" }
        );
        
        _repo.AddSeries(validSeries);
        _repo.AddSeries(invalidSeries);

        // Act
        var report = await _service.RunFullValidationAsync();

        // Assert
        Assert.Equal(2, report.TotalSeries);
        Assert.Single(report.SeriesIssues);
        Assert.Equal("urn:mvn:series:invalid", report.SeriesIssues[0].EntityUrn);
    }

    #endregion

    #region Performance Tests

    [Fact]
    public void ValidateTags_WithLargeArrayOfTags_CompletesInReasonableTime()
    {
        // Arrange
        var tags = Enumerable.Range(1, 1000).Select(i => "action").ToArray();

        // Act
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = _service.ValidateTags(tags);
        stopwatch.Stop();

        // Assert
        Assert.True(result.IsValid);
        Assert.True(stopwatch.ElapsedMilliseconds < 1000, "Should complete in less than 1 second");
    }

    [Fact]
    public void LevenshteinDistance_WithLargeStrings_CompletesReasonably()
    {
        // Arrange - Create tags that will trigger fuzzy matching
        var longTag = new string('a', 100) + "xyz";
        var tags = new[] { longTag };

        // Act
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = _service.ValidateTags(tags);
        stopwatch.Stop();

        // Assert
        Assert.False(result.IsValid);
        Assert.True(stopwatch.ElapsedMilliseconds < 5000, "Should complete in less than 5 seconds");
    }

    #endregion

    #region Helper Methods

    private Series CreateTestSeries(
        string? id = null,
        string[]? tags = null,
        Author[]? authors = null,
        Scanlator[]? scanlators = null,
        Group[]? groups = null)
    {
        return new Series(
            id: id ?? "urn:mvn:series:test",
            federation_ref: "urn:mvn:node:local",
            title: "Test Series",
            description: "Test",
            poster: new Poster("url", "alt"),
            media_type: "Photo",
            external_links: new Dictionary<string, string>(),
            reading_direction: "LTR",
            tags: tags ?? new[] { "action" },
            content_warnings: Array.Empty<string>(),
            authors: authors ?? new[] { new Author("author-1", "John Doe", "Author") },
            scanlators: scanlators ?? new[] { new Scanlator("scanlator-1", "Group A", ScanlatorRole.Translation) },
            groups: groups,
            alt_titles: null,
            status: null,
            year: null,
            created_by: "urn:mvn:user:test",
            created_at: DateTime.UtcNow,
            updated_at: DateTime.UtcNow
        );
    }

    private Unit CreateTestUnit(
        Dictionary<string, UnitLocalizedMetadata>? localized = null)
    {
        return new Unit(
            id: "urn:mvn:unit:test",
            series_id: "urn:mvn:series:test",
            unit_number: 1,
            title: "Chapter 1",
            created_at: DateTime.UtcNow,
            created_by: "urn:mvn:user:test",
            language: "en",
            page_count: 20,
            folder_path: null,
            updated_at: DateTime.UtcNow,
            description: "Test chapter",
            tags: new[] { "action" },
            content_warnings: null,
            authors: new[] { new Author("author-1", "John Doe", "Author") },
            localized: localized ?? new Dictionary<string, UnitLocalizedMetadata>
            {
                ["en"] = new UnitLocalizedMetadata(
                    title: "Chapter 1",
                    scanlators: new[] { new Scanlator("scanlator-1", "Group A", ScanlatorRole.Translation) },
                    content_folder: null
                )
            },
            allowed_editors: null
        );
    }

    #endregion
}
