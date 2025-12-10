using Xunit;
using MehguViewer.Core.Shared;
using MehguViewer.Core.Services;
using MehguViewer.Core.Workers;
using MehguViewer.Core.Helpers;
using Microsoft.Extensions.Logging;

namespace MehguViewer.Core.Tests.Units;

/// <summary>
/// Tests for Unit creation, metadata inheritance, and aggregation.
/// </summary>
public class UnitMetadataTests
{
    private readonly MetadataAggregationService _metadataService;

    public UnitMetadataTests()
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _metadataService = new MetadataAggregationService(loggerFactory.CreateLogger<MetadataAggregationService>());
    }
    [Fact]
    public void Unit_InheritsMetadataFromSeries_WhenNotExplicitlyProvided()
    {
        // Arrange
        var series = CreateTestSeries();
        var unit = new Unit(
            id: "urn:mvn:unit:test-1",
            series_id: series.id,
            unit_number: 1,
            title: "Chapter 1",
            created_at: DateTime.UtcNow,
            created_by: "urn:mvn:user:uploader1",
            language: "en",
            page_count: 0,
            folder_path: null,
            updated_at: DateTime.UtcNow,
            description: null,
            tags: null,  // Should inherit from series
            content_warnings: null,  // Should inherit from series
            authors: null,  // Should inherit from series
            localized: null
        );

        // Act
        var inheritedUnit = _metadataService.InheritMetadataFromSeries(unit, series, "en");

        // Assert
        Assert.NotNull(inheritedUnit.tags);
        Assert.Equal(series.tags, inheritedUnit.tags);
        Assert.NotNull(inheritedUnit.content_warnings);
        Assert.Equal(series.content_warnings, inheritedUnit.content_warnings);
        Assert.NotNull(inheritedUnit.authors);
        Assert.Equal(series.authors, inheritedUnit.authors);
    }

    [Fact]
    public void Unit_PreservesExplicitMetadata_WhenProvided()
    {
        // Arrange
        var series = CreateTestSeries();
        var customTags = new[] { "Custom Tag" };
        var unit = new Unit(
            id: "urn:mvn:unit:test-1",
            series_id: series.id,
            unit_number: 1,
            title: "Chapter 1",
            created_at: DateTime.UtcNow,
            created_by: "urn:mvn:user:uploader1",
            language: "en",
            page_count: 0,
            folder_path: null,
            updated_at: DateTime.UtcNow,
            description: null,
            tags: customTags,  // Explicit tags
            content_warnings: null,
            authors: null,
            localized: null
        );

        // Act
        var inheritedUnit = _metadataService.InheritMetadataFromSeries(unit, series, "en");

        // Assert - Custom tags should be preserved
        Assert.Equal(customTags, inheritedUnit.tags);
    }

    [Fact]
    public void Series_AggregatesTagsFromAllUnits()
    {
        // Arrange
        var series = CreateTestSeries();
        var units = new List<Unit>
        {
            CreateTestUnit(series.id, 1, tags: new[] { "Tag1", "Tag2" }),
            CreateTestUnit(series.id, 2, tags: new[] { "Tag2", "Tag3" }),
            CreateTestUnit(series.id, 3, tags: new[] { "Tag3", "Tag4" })
        };

        // Act
        var aggregated = _metadataService.AggregateMetadata(series, units);

        // Assert
        Assert.Contains("Tag1", aggregated.tags);
        Assert.Contains("Tag2", aggregated.tags);
        Assert.Contains("Tag3", aggregated.tags);
        Assert.Contains("Tag4", aggregated.tags);
        Assert.Contains("Action", aggregated.tags);  // Original series tag
        Assert.Contains("Fantasy", aggregated.tags);  // Original series tag
    }

    [Fact]
    public void Series_AggregatesScanlators_FromLocalizedMetadata()
    {
        // Arrange
        var series = CreateTestSeries();
        var units = new List<Unit>
        {
            CreateTestUnitWithLocalizedScanlator(series.id, 1, "zh", "GroupA"),
            CreateTestUnitWithLocalizedScanlator(series.id, 2, "zh", "GroupA"),
            CreateTestUnitWithLocalizedScanlator(series.id, 3, "zh", "GroupB")  // Different group
        };

        // Act
        var aggregated = _metadataService.AggregateMetadata(series, units);

        // Assert
        Assert.NotNull(aggregated.localized);
        Assert.True(aggregated.localized.ContainsKey("zh"));
        var zhMeta = aggregated.localized["zh"];
        Assert.NotNull(zhMeta.scanlators);
        Assert.Equal(2, zhMeta.scanlators.Length);  // Both GroupA and GroupB
        Assert.Contains(zhMeta.scanlators, s => s.id == "scanlator-GroupA");
        Assert.Contains(zhMeta.scanlators, s => s.id == "scanlator-GroupB");
    }

    [Fact]
    public void Series_AggregatesContentWarnings_FromAllUnits()
    {
        // Arrange
        var series = new Series(
            id: "urn:mvn:series:test",
            federation_ref: "urn:mvn:node:local",
            title: "Test Series",
            description: "Test",
            poster: new Poster("url", "alt"),
            media_type: MediaTypes.Photo,
            external_links: new Dictionary<string, string>(),
            reading_direction: ReadingDirections.RTL,
            tags: [],
            content_warnings: new[] { "violence" },  // Original warning
            authors: [],
            scanlators: [],
            groups: null,
            alt_titles: null,
            status: null,
            year: null,
            created_by: "urn:mvn:user:owner",
            created_at: DateTime.UtcNow,
            updated_at: DateTime.UtcNow
        );
        
        var units = new List<Unit>
        {
            CreateTestUnit(series.id, 1, contentWarnings: new[] { "gore" }),
            CreateTestUnit(series.id, 2, contentWarnings: new[] { "nsfw" })
        };

        // Act
        var aggregated = _metadataService.AggregateMetadata(series, units);

        // Assert
        Assert.Contains("violence", aggregated.content_warnings);  // Original
        Assert.Contains("gore", aggregated.content_warnings);      // From unit 1
        Assert.Contains("nsfw", aggregated.content_warnings);      // From unit 2
    }

    [Fact]
    public void Series_AggregatesAuthors_FromAllUnits()
    {
        // Arrange
        var series = CreateTestSeries();
        var units = new List<Unit>
        {
            CreateTestUnit(series.id, 1, authors: new[] { new Author("author-2", "Author 2", "Artist") }),
            CreateTestUnit(series.id, 2, authors: new[] { new Author("author-3", "Author 3", "Author") })
        };

        // Act
        var aggregated = _metadataService.AggregateMetadata(series, units);

        // Assert
        Assert.Contains(aggregated.authors, a => a.id == "author-1");  // Original
        Assert.Contains(aggregated.authors, a => a.id == "author-2");  // From unit 1
        Assert.Contains(aggregated.authors, a => a.id == "author-3");  // From unit 2
    }

    // Helper methods
    private static Series CreateTestSeries()
    {
        return new Series(
            id: "urn:mvn:series:test",
            federation_ref: "urn:mvn:node:local",
            title: "Test Series",
            description: "Test",
            poster: new Poster("url", "alt"),
            media_type: MediaTypes.Photo,
            external_links: new Dictionary<string, string>(),
            reading_direction: ReadingDirections.RTL,
            tags: new[] { "Action", "Fantasy" },
            content_warnings: new[] { "violence" },
            authors: new[] { new Author("author-1", "Original Author", "Author") },
            scanlators: new[] { new Scanlator("scanlator-1", "Original Scans", ScanlatorRole.Both) },
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
        string groupName)
    {
        var localized = new Dictionary<string, UnitLocalizedMetadata>
        {
            [language] = new UnitLocalizedMetadata(
                title: null,
                scanlators: new[] { new Scanlator($"scanlator-{groupName}", groupName, ScanlatorRole.Both) },
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
}
