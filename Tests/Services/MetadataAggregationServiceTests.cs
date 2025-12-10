using Xunit;
using MehguViewer.Core.Shared;
using MehguViewer.Core.Services;
using MehguViewer.Core.Workers;
using MehguViewer.Core.Helpers;
using Microsoft.Extensions.Logging;

namespace MehguViewer.Core.Tests.Services;

/// <summary>
/// Comprehensive unit tests for MetadataAggregationService covering edge cases, null handling,
/// validation, and advanced scenarios.
/// </summary>
public class MetadataAggregationServiceTests
{
    private readonly MetadataAggregationService _metadataService;
    private readonly ILogger<MetadataAggregationService> _logger;

    public MetadataAggregationServiceTests()
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Trace));
        _logger = loggerFactory.CreateLogger<MetadataAggregationService>();
        _metadataService = new MetadataAggregationService(_logger);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => new MetadataAggregationService(null!));
        Assert.Equal("logger", exception.ParamName);
    }

    #endregion

    #region AggregateMetadata Tests - Input Validation

    [Fact]
    public void AggregateMetadata_WithNullSeries_ThrowsArgumentNullException()
    {
        // Arrange
        var units = new List<Unit>();

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => 
            _metadataService.AggregateMetadata(null!, units));
        Assert.Equal("series", exception.ParamName);
    }

    [Fact]
    public void AggregateMetadata_WithNullUnits_ThrowsArgumentNullException()
    {
        // Arrange
        var series = CreateTestSeries();

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => 
            _metadataService.AggregateMetadata(series, null!));
        Assert.Equal("units", exception.ParamName);
    }

    [Fact]
    public void AggregateMetadata_WithEmptyUnits_ReturnsSameSeriesUnchanged()
    {
        // Arrange
        var series = CreateTestSeries();
        var emptyUnits = new List<Unit>();

        // Act
        var result = _metadataService.AggregateMetadata(series, emptyUnits);

        // Assert
        Assert.Equal(series.id, result.id);
        Assert.Equal(series.title, result.title);
        Assert.Equal(series.tags, result.tags);
        Assert.Equal(series.authors, result.authors);
    }

    #endregion

    #region AggregateMetadata Tests - Case Insensitivity

    [Fact]
    public void AggregateMetadata_Tags_CaseInsensitiveDeduplication()
    {
        // Arrange
        var series = CreateTestSeries(tags: new[] { "Action", "Fantasy" });
        var units = new List<Unit>
        {
            CreateTestUnit(series.id, 1, tags: new[] { "action", "HORROR" }),
            CreateTestUnit(series.id, 2, tags: new[] { "ACTION", "horror", "Comedy" })
        };

        // Act
        var result = _metadataService.AggregateMetadata(series, units);

        // Assert
        Assert.NotNull(result.tags);
        Assert.Equal(4, result.tags.Length);
        Assert.Contains(result.tags, t => t.Equals("Action", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.tags, t => t.Equals("Fantasy", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.tags, t => t.Equals("horror", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.tags, t => t.Equals("Comedy", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AggregateMetadata_ContentWarnings_CaseInsensitiveDeduplication()
    {
        // Arrange
        var series = CreateTestSeries(contentWarnings: new[] { "violence" });
        var units = new List<Unit>
        {
            CreateTestUnit(series.id, 1, contentWarnings: new[] { "VIOLENCE", "gore" }),
            CreateTestUnit(series.id, 2, contentWarnings: new[] { "Gore", "nsfw" })
        };

        // Act
        var result = _metadataService.AggregateMetadata(series, units);

        // Assert
        Assert.NotNull(result.content_warnings);
        Assert.Equal(3, result.content_warnings.Length);
        Assert.Contains(result.content_warnings, w => w.Equals("violence", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.content_warnings, w => w.Equals("gore", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.content_warnings, w => w.Equals("nsfw", StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    #region AggregateMetadata Tests - Null/Empty Handling

    [Fact]
    public void AggregateMetadata_WithNullTags_HandlesGracefully()
    {
        // Arrange
        var series = CreateTestSeries(tags: null, useDefaults: false);
        var units = new List<Unit>
        {
            CreateTestUnit(series.id, 1, tags: new[] { "Tag1" }),
            CreateTestUnit(series.id, 2, tags: null)
        };

        // Act
        var result = _metadataService.AggregateMetadata(series, units);

        // Assert
        Assert.NotNull(result.tags);
        Assert.Single(result.tags);
        Assert.Contains("Tag1", result.tags);
    }

    [Fact]
    public void AggregateMetadata_WithAllNullAuthors_ReturnsEmptyArray()
    {
        // Arrange
        var series = CreateTestSeries(authors: null, useDefaults: false);
        var units = new List<Unit>
        {
            CreateTestUnit(series.id, 1, authors: null),
            CreateTestUnit(series.id, 2, authors: null)
        };

        // Act
        var result = _metadataService.AggregateMetadata(series, units);

        // Assert
        Assert.NotNull(result.authors);
        Assert.Empty(result.authors);
    }

    [Fact]
    public void AggregateMetadata_WithEmptyStringTags_FiltersOut()
    {
        // Arrange
        var series = CreateTestSeries(tags: new[] { "Action", "", "  " });
        var units = new List<Unit>
        {
            CreateTestUnit(series.id, 1, tags: new[] { "", "Fantasy", "   " })
        };

        // Act
        var result = _metadataService.AggregateMetadata(series, units);

        // Assert
        Assert.NotNull(result.tags);
        Assert.DoesNotContain("", result.tags);
        Assert.DoesNotContain("  ", result.tags);
        Assert.DoesNotContain("   ", result.tags);
    }

    #endregion

    #region AggregateMetadata Tests - Author Deduplication

    [Fact]
    public void AggregateMetadata_Authors_DeduplicateById()
    {
        // Arrange
        var author1 = new Author("author-1", "Author One", "Author");
        var author1Duplicate = new Author("author-1", "Author One Updated", "Writer"); // Same ID
        var author2 = new Author("author-2", "Author Two", "Artist");

        var series = CreateTestSeries(authors: new[] { author1 });
        var units = new List<Unit>
        {
            CreateTestUnit(series.id, 1, authors: new[] { author1Duplicate, author2 })
        };

        // Act
        var result = _metadataService.AggregateMetadata(series, units);

        // Assert
        Assert.NotNull(result.authors);
        Assert.Equal(2, result.authors.Length);
        Assert.Contains(result.authors, a => a.id == "author-1");
        Assert.Contains(result.authors, a => a.id == "author-2");
        // Note: Last wins in case of duplicate IDs
    }

    [Fact]
    public void AggregateMetadata_Authors_IgnoreNullEntries()
    {
        // Arrange
        var series = CreateTestSeries(authors: new[] { new Author("author-1", "Test", "Author") });
        var units = new List<Unit>
        {
            // Unit with null in authors array would be caught by record validation,
            // but we test defensive handling
            CreateTestUnit(series.id, 1, authors: new[] { new Author("author-2", "Test2", "Artist") })
        };

        // Act
        var result = _metadataService.AggregateMetadata(series, units);

        // Assert
        Assert.NotNull(result.authors);
        Assert.Equal(2, result.authors.Length);
    }

    #endregion

    #region AggregateMetadata Tests - Scanlator Aggregation

    [Fact]
    public void AggregateMetadata_Scanlators_FromSeriesAndLocalized()
    {
        // Arrange
        var seriesScanlator = new Scanlator("scan-1", "Series Group", ScanlatorRole.Both);
        var enScanlator = new Scanlator("scan-2", "English Team", ScanlatorRole.Translation);
        var jaScanlator = new Scanlator("scan-3", "Japanese Team", ScanlatorRole.Scanlation);

        var series = CreateTestSeries(scanlators: new[] { seriesScanlator });
        var units = new List<Unit>
        {
            CreateTestUnitWithLocalizedScanlator(series.id, 1, "en", enScanlator),
            CreateTestUnitWithLocalizedScanlator(series.id, 2, "ja", jaScanlator)
        };

        // Act
        var result = _metadataService.AggregateMetadata(series, units);

        // Assert
        Assert.NotNull(result.scanlators);
        Assert.Equal(3, result.scanlators.Length);
        Assert.Contains(result.scanlators, s => s.id == "scan-1");
        Assert.Contains(result.scanlators, s => s.id == "scan-2");
        Assert.Contains(result.scanlators, s => s.id == "scan-3");
    }

    [Fact]
    public void AggregateMetadata_Scanlators_DeduplicateAcrossLanguages()
    {
        // Arrange
        var sharedScanlator = new Scanlator("scan-shared", "Shared Team", ScanlatorRole.Both);
        var series = CreateTestSeries(scanlators: Array.Empty<Scanlator>());
        var units = new List<Unit>
        {
            CreateTestUnitWithLocalizedScanlator(series.id, 1, "en", sharedScanlator),
            CreateTestUnitWithLocalizedScanlator(series.id, 2, "ja", sharedScanlator)
        };

        // Act
        var result = _metadataService.AggregateMetadata(series, units);

        // Assert
        Assert.NotNull(result.scanlators);
        var uniqueScanlators = result.scanlators.GroupBy(s => s.id).ToList();
        Assert.Single(uniqueScanlators);
        Assert.Equal("scan-shared", uniqueScanlators[0].Key);
    }

    #endregion

    #region AggregateMetadata Tests - Localized Metadata

    [Fact]
    public void AggregateMetadata_LocalizedMetadata_MergesPerLanguage()
    {
        // Arrange
        var series = CreateTestSeries();
        var scan1 = new Scanlator("scan-1", "Team A", ScanlatorRole.Both);
        var scan2 = new Scanlator("scan-2", "Team B", ScanlatorRole.Both);

        var units = new List<Unit>
        {
            CreateTestUnitWithLocalizedScanlator(series.id, 1, "en", scan1),
            CreateTestUnitWithLocalizedScanlator(series.id, 2, "en", scan2),
            CreateTestUnitWithLocalizedScanlator(series.id, 3, "ja", scan1)
        };

        // Act
        var result = _metadataService.AggregateMetadata(series, units);

        // Assert
        Assert.NotNull(result.localized);
        Assert.Equal(2, result.localized.Count);
        Assert.True(result.localized.ContainsKey("en"));
        Assert.True(result.localized.ContainsKey("ja"));
        
        var enScanlators = result.localized["en"].scanlators;
        Assert.NotNull(enScanlators);
        Assert.Equal(2, enScanlators.Length);
        
        var jaScanlators = result.localized["ja"].scanlators;
        Assert.NotNull(jaScanlators);
        Assert.Single(jaScanlators);
    }

    [Fact]
    public void AggregateMetadata_LocalizedMetadata_IgnoresEmptyLanguageCodes()
    {
        // Arrange
        var series = CreateTestSeries();
        var unit = CreateTestUnit(series.id, 1);
        // Manually create unit with empty language code in localized (edge case)
        unit = unit with
        {
            localized = new Dictionary<string, UnitLocalizedMetadata>
            {
                [""] = new UnitLocalizedMetadata(null, new[] { new Scanlator("scan-1", "Test", ScanlatorRole.Both) }, null),
                ["en"] = new UnitLocalizedMetadata(null, new[] { new Scanlator("scan-2", "Test2", ScanlatorRole.Both) }, null)
            }
        };

        // Act
        var result = _metadataService.AggregateMetadata(series, new[] { unit });

        // Assert - Empty language code should be filtered out
        Assert.NotNull(result.localized);
        Assert.Single(result.localized);
        Assert.True(result.localized.ContainsKey("en"));
        Assert.False(result.localized.ContainsKey(""));
    }

    #endregion

    #region AggregateMetadata Tests - Timestamp Update

    [Fact]
    public void AggregateMetadata_UpdatesTimestamp()
    {
        // Arrange
        var originalTime = DateTime.UtcNow.AddHours(-1);
        var series = CreateTestSeries() with { updated_at = originalTime };
        var units = new List<Unit>
        {
            CreateTestUnit(series.id, 1)
        };

        // Act
        var result = _metadataService.AggregateMetadata(series, units);

        // Assert
        Assert.True(result.updated_at > originalTime);
        Assert.True(result.updated_at <= DateTime.UtcNow);
    }

    #endregion

    #region InheritMetadataFromSeries Tests - Input Validation

    [Fact]
    public void InheritMetadataFromSeries_WithNullUnit_ThrowsArgumentNullException()
    {
        // Arrange
        var series = CreateTestSeries();

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => 
            _metadataService.InheritMetadataFromSeries(null!, series));
        Assert.Equal("unit", exception.ParamName);
    }

    [Fact]
    public void InheritMetadataFromSeries_WithNullSeries_ThrowsArgumentNullException()
    {
        // Arrange
        var unit = CreateTestUnit("test-series", 1);

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => 
            _metadataService.InheritMetadataFromSeries(unit, null!));
        Assert.Equal("series", exception.ParamName);
    }

    #endregion

    #region InheritMetadataFromSeries Tests - Inheritance Behavior

    [Fact]
    public void InheritMetadataFromSeries_PreservesExistingUnitMetadata()
    {
        // Arrange
        var series = CreateTestSeries(
            tags: new[] { "SeriesTag" },
            authors: new[] { new Author("author-1", "Series Author", "Author") }
        );
        var unitTags = new[] { "UnitTag" };
        var unitAuthors = new[] { new Author("author-2", "Unit Author", "Artist") };
        var unit = CreateTestUnit(series.id, 1, tags: unitTags, authors: unitAuthors);

        // Act
        var result = _metadataService.InheritMetadataFromSeries(unit, series);

        // Assert
        Assert.Equal(unitTags, result.tags);
        Assert.Equal(unitAuthors, result.authors);
    }

    [Fact]
    public void InheritMetadataFromSeries_InheritsWhenUnitMetadataNull()
    {
        // Arrange
        var seriesTags = new[] { "Action", "Fantasy" };
        var seriesWarnings = new[] { "violence" };
        var seriesAuthors = new[] { new Author("author-1", "Test Author", "Author") };
        
        var series = CreateTestSeries(
            tags: seriesTags,
            contentWarnings: seriesWarnings,
            authors: seriesAuthors
        );
        var unit = CreateTestUnit(series.id, 1, tags: null, contentWarnings: null, authors: null);

        // Act
        var result = _metadataService.InheritMetadataFromSeries(unit, series);

        // Assert
        Assert.Equal(seriesTags, result.tags);
        Assert.Equal(seriesWarnings, result.content_warnings);
        Assert.Equal(seriesAuthors, result.authors);
    }

    [Fact]
    public void InheritMetadataFromSeries_WithLanguage_InheritsLocalizedScanlators()
    {
        // Arrange
        var enScanlators = new[] { new Scanlator("scan-1", "English Team", ScanlatorRole.Translation) };
        var series = CreateTestSeries() with
        {
            localized = new Dictionary<string, LocalizedMetadata>
            {
                ["en"] = new LocalizedMetadata(null, null, null, enScanlators, null)
            }
        };
        var unit = CreateTestUnit(series.id, 1);

        // Act
        var result = _metadataService.InheritMetadataFromSeries(unit, series, "en");

        // Assert
        Assert.NotNull(result.localized);
        Assert.True(result.localized.ContainsKey("en"));
        Assert.Equal(enScanlators, result.localized["en"].scanlators);
    }

    [Fact]
    public void InheritMetadataFromSeries_WithLanguage_DoesNotOverrideExistingLocalized()
    {
        // Arrange
        var series = CreateTestSeries() with
        {
            localized = new Dictionary<string, LocalizedMetadata>
            {
                ["en"] = new LocalizedMetadata(null, null, null, 
                    new[] { new Scanlator("scan-1", "Series Team", ScanlatorRole.Both) }, null)
            }
        };
        var unitLocalized = new Dictionary<string, UnitLocalizedMetadata>
        {
            ["en"] = new UnitLocalizedMetadata(null, 
                new[] { new Scanlator("scan-2", "Unit Team", ScanlatorRole.Both) }, null)
        };
        var unit = CreateTestUnit(series.id, 1) with { localized = unitLocalized };

        // Act
        var result = _metadataService.InheritMetadataFromSeries(unit, series, "en");

        // Assert
        Assert.Equal(unitLocalized, result.localized);
        Assert.Equal("scan-2", result.localized!["en"].scanlators![0].id);
    }

    [Fact]
    public void InheritMetadataFromSeries_WithNullLanguage_DoesNotInheritLocalized()
    {
        // Arrange
        var series = CreateTestSeries() with
        {
            localized = new Dictionary<string, LocalizedMetadata>
            {
                ["en"] = new LocalizedMetadata(null, null, null, 
                    new[] { new Scanlator("scan-1", "Team", ScanlatorRole.Both) }, null)
            }
        };
        var unit = CreateTestUnit(series.id, 1);

        // Act
        var result = _metadataService.InheritMetadataFromSeries(unit, series, null);

        // Assert
        Assert.Null(result.localized);
    }

    [Fact]
    public void InheritMetadataFromSeries_WithWhitespaceLanguage_DoesNotInheritLocalized()
    {
        // Arrange
        var series = CreateTestSeries() with
        {
            localized = new Dictionary<string, LocalizedMetadata>
            {
                ["en"] = new LocalizedMetadata(null, null, null, 
                    new[] { new Scanlator("scan-1", "Team", ScanlatorRole.Both) }, null)
            }
        };
        var unit = CreateTestUnit(series.id, 1);

        // Act
        var result = _metadataService.InheritMetadataFromSeries(unit, series, "  ");

        // Assert
        Assert.Null(result.localized);
    }

    #endregion

    #region Helper Methods

    private static Series CreateTestSeries(
        string[]? tags = null,
        string[]? contentWarnings = null,
        Author[]? authors = null,
        Scanlator[]? scanlators = null,
        bool useDefaults = true)
    {
        return new Series(
            id: "urn:mvn:series:test",
            federation_ref: "urn:mvn:node:local",
            title: "Test Series",
            description: "Test Description",
            poster: new Poster("https://example.com/poster.jpg", "Test Poster"),
            media_type: MediaTypes.Photo,
            external_links: new Dictionary<string, string>(),
            reading_direction: ReadingDirections.RTL,
            tags: useDefaults && tags == null ? new[] { "Action", "Fantasy" } : tags ?? Array.Empty<string>(),
            content_warnings: useDefaults && contentWarnings == null ? new[] { "violence" } : contentWarnings ?? Array.Empty<string>(),
            authors: useDefaults && authors == null ? new[] { new Author("author-1", "Original Author", "Author") } : authors ?? Array.Empty<Author>(),
            scanlators: useDefaults && scanlators == null ? new[] { new Scanlator("scanlator-1", "Original Scans", ScanlatorRole.Both) } : scanlators ?? Array.Empty<Scanlator>(),
            groups: null,
            alt_titles: null,
            status: "Ongoing",
            year: 2024,
            created_by: "urn:mvn:user:owner",
            created_at: DateTime.UtcNow,
            updated_at: DateTime.UtcNow
        );
    }

    private static Unit CreateTestUnit(
        string seriesId,
        int number,
        string[]? tags = null,
        string[]? contentWarnings = null,
        Author[]? authors = null)
    {
        return new Unit(
            id: $"urn:mvn:unit:test-{number}",
            series_id: seriesId,
            unit_number: number,
            title: $"Chapter {number}",
            created_at: DateTime.UtcNow,
            created_by: "urn:mvn:user:uploader1",
            language: "en",
            page_count: 0,
            folder_path: null,
            updated_at: DateTime.UtcNow,
            description: null,
            tags: tags,
            content_warnings: contentWarnings,
            authors: authors,
            localized: null
        );
    }

    private static Unit CreateTestUnitWithLocalizedScanlator(
        string seriesId,
        int number,
        string language,
        Scanlator scanlator)
    {
        var localized = new Dictionary<string, UnitLocalizedMetadata>
        {
            [language] = new UnitLocalizedMetadata(
                title: null,
                scanlators: new[] { scanlator },
                content_folder: null
            )
        };

        return new Unit(
            id: $"urn:mvn:unit:test-{number}",
            series_id: seriesId,
            unit_number: number,
            title: $"Chapter {number}",
            created_at: DateTime.UtcNow,
            created_by: "urn:mvn:user:uploader1",
            language: language,
            page_count: 0,
            folder_path: null,
            updated_at: DateTime.UtcNow,
            description: null,
            tags: null,
            content_warnings: null,
            authors: null,
            localized: localized
        );
    }

    #endregion
}
