using Xunit;
using MehguViewer.Core.Shared;
using MehguViewer.Core.Services;
using MehguViewer.Core.Workers;
using MehguViewer.Core.Helpers;
using MehguViewer.Core.Infrastructures;
using Microsoft.Extensions.Logging;

namespace MehguViewer.Core.Tests.Services;

public class TaxonomyValidationServiceTests
{
    private readonly MemoryRepository _repo;
    private readonly TaxonomyValidationService _service;

    public TaxonomyValidationServiceTests()
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
            tags: new[] { "action", "comedy", "drama", "fantasy" },
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

    [Fact]
    public void ValidateTags_WithValidTags_ReturnsValid()
    {
        // Arrange
        var tags = new[] { "action", "comedy", "drama" };

        // Act
        var result = _service.ValidateTags(tags);

        // Assert
        Assert.True(result.IsValid);
        Assert.Equal(3, result.ValidTags.Length);
        Assert.Empty(result.InvalidTags);
        Assert.Empty(result.Suggestions);
    }

    [Fact]
    public void ValidateTags_WithInvalidTags_ReturnsInvalid()
    {
        // Arrange
        var tags = new[] { "action", "romance", "scifi" };

        // Act
        var result = _service.ValidateTags(tags);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.ValidTags);
        Assert.Equal("action", result.ValidTags[0]);
        Assert.Equal(2, result.InvalidTags.Length);
        Assert.Contains("romance", result.InvalidTags);
        Assert.Contains("scifi", result.InvalidTags);
    }

    [Fact]
    public void ValidateTags_CaseInsensitive_ReturnsValid()
    {
        // Arrange
        var tags = new[] { "ACTION", "CoMeDy", "DRAMA" };

        // Act
        var result = _service.ValidateTags(tags);

        // Assert
        Assert.True(result.IsValid);
        Assert.Equal(3, result.ValidTags.Length);
    }

    [Fact]
    public void ValidateTags_WithSimilarTag_ProvidesSuggestions()
    {
        // Arrange
        var tags = new[] { "actio", "comdy" }; // Similar to "action" and "comedy"

        // Act
        var result = _service.ValidateTags(tags);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("actio", result.InvalidTags);
        Assert.True(result.Suggestions.ContainsKey("actio"));
        Assert.Contains("action", result.Suggestions["actio"]);
    }

    [Fact]
    public void ValidateTags_WithEmptyArray_ReturnsValid()
    {
        // Arrange
        var tags = Array.Empty<string>();

        // Act
        var result = _service.ValidateTags(tags);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.ValidTags);
        Assert.Empty(result.InvalidTags);
    }

    [Fact]
    public void ValidateAuthors_WithValidAuthors_ReturnsValid()
    {
        // Arrange
        var authors = new[]
        {
            new Author("author-1", "John Doe", "Author"),
            new Author("author-2", "Jane Smith", "Artist")
        };

        // Act
        var result = _service.ValidateAuthors(authors);

        // Assert
        Assert.True(result.IsValid);
        Assert.Equal(2, result.ValidIds.Length);
        Assert.Empty(result.InvalidIds);
    }

    [Fact]
    public void ValidateAuthors_WithInvalidAuthors_ReturnsInvalid()
    {
        // Arrange
        var authors = new[]
        {
            new Author("author-1", "John Doe", "Author"),
            new Author("author-999", "Unknown Author", "Author")
        };

        // Act
        var result = _service.ValidateAuthors(authors);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.ValidIds);
        Assert.Single(result.InvalidIds);
        Assert.Contains("author-999", result.InvalidIds);
    }

    [Fact]
    public void ValidateScanlators_WithValidScanlators_ReturnsValid()
    {
        // Arrange
        var scanlators = new[]
        {
            new Scanlator("scanlator-1", "Group A", ScanlatorRole.Translation)
        };

        // Act
        var result = _service.ValidateScanlators(scanlators);

        // Assert
        Assert.True(result.IsValid);
        Assert.Single(result.ValidIds);
        Assert.Empty(result.InvalidIds);
    }

    [Fact]
    public void ValidateScanlators_WithInvalidScanlators_ReturnsInvalid()
    {
        // Arrange
        var scanlators = new[]
        {
            new Scanlator("scanlator-1", "Group A", ScanlatorRole.Translation),
            new Scanlator("scanlator-999", "Unknown Group", ScanlatorRole.Both)
        };

        // Act
        var result = _service.ValidateScanlators(scanlators);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.ValidIds);
        Assert.Single(result.InvalidIds);
        Assert.Contains("scanlator-999", result.InvalidIds);
    }

    [Fact]
    public void ValidateGroups_WithValidGroups_ReturnsValid()
    {
        // Arrange
        var groups = new[]
        {
            new Group("group-1", "Publisher A"),
            new Group("group-2", "Publisher B")
        };

        // Act
        var result = _service.ValidateGroups(groups);

        // Assert
        Assert.True(result.IsValid);
        Assert.Equal(2, result.ValidIds.Length);
        Assert.Empty(result.InvalidIds);
    }

    [Fact]
    public void ValidateSeries_WithValidData_ReturnsValid()
    {
        // Arrange
        var series = new Series(
            id: "urn:mvn:series:test",
            federation_ref: "urn:mvn:node:local",
            title: "Test Series",
            description: "Test",
            poster: new Poster("url", "alt"),
            media_type: "Photo",
            external_links: new Dictionary<string, string>(),
            reading_direction: "LTR",
            tags: new[] { "action", "comedy" },
            content_warnings: new[] { "violence" },
            authors: new[] { new Author("author-1", "John Doe", "Author") },
            scanlators: new[] { new Scanlator("scanlator-1", "Group A", ScanlatorRole.Translation) },
            groups: null,
            alt_titles: null,
            status: null,
            year: null,
            created_by: "urn:mvn:user:test",
            created_at: DateTime.UtcNow,
            updated_at: DateTime.UtcNow
        );

        // Act
        var result = _service.ValidateSeries(series);

        // Assert
        Assert.True(result.IsValid);
        Assert.True(result.Tags.IsValid);
        Assert.True(result.Authors.IsValid);
        Assert.True(result.Scanlators.IsValid);
    }

    [Fact]
    public void ValidateSeries_WithInvalidData_ReturnsInvalid()
    {
        // Arrange
        var series = new Series(
            id: "urn:mvn:series:test",
            federation_ref: "urn:mvn:node:local",
            title: "Test Series",
            description: "Test",
            poster: new Poster("url", "alt"),
            media_type: "Photo",
            external_links: new Dictionary<string, string>(),
            reading_direction: "LTR",
            tags: new[] { "action", "invalid-tag" },
            content_warnings: Array.Empty<string>(),
            authors: new[] { new Author("invalid-author", "Unknown", "Author") },
            scanlators: new[] { new Scanlator("invalid-scanlator", "Unknown Group", ScanlatorRole.Translation) },
            groups: null,
            alt_titles: null,
            status: null,
            year: null,
            created_by: "urn:mvn:user:test",
            created_at: DateTime.UtcNow,
            updated_at: DateTime.UtcNow
        );

        // Act
        var result = _service.ValidateSeries(series);

        // Assert
        Assert.False(result.IsValid);
        Assert.False(result.Tags.IsValid);
        Assert.False(result.Authors.IsValid);
        Assert.False(result.Scanlators.IsValid);
        Assert.Contains("invalid-tag", result.Tags.InvalidTags);
        Assert.Contains("invalid-author", result.Authors.InvalidIds);
        Assert.Contains("invalid-scanlator", result.Scanlators.InvalidIds);
    }

    [Fact]
    public void ValidateUnit_WithValidData_ReturnsValid()
    {
        // Arrange
        var unit = new Unit(
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
            localized: new Dictionary<string, UnitLocalizedMetadata>
            {
                ["en"] = new UnitLocalizedMetadata(
                    title: "Chapter 1",
                    scanlators: new[] { new Scanlator("scanlator-1", "Group A", ScanlatorRole.Translation) },
                    content_folder: null
                )
            },
            allowed_editors: null
        );

        // Act
        var result = _service.ValidateUnit(unit);

        // Assert
        Assert.True(result.IsValid);
        Assert.True(result.Tags.IsValid);
        Assert.True(result.Authors.IsValid);
        Assert.True(result.Scanlators.IsValid);
    }

    [Fact]
    public void ValidateUnit_WithInvalidData_ReturnsInvalid()
    {
        // Arrange
        var unit = new Unit(
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
            tags: new[] { "invalid-tag" },
            content_warnings: null,
            authors: new[] { new Author("invalid-author", "Unknown", "Author") },
            localized: new Dictionary<string, UnitLocalizedMetadata>
            {
                ["en"] = new UnitLocalizedMetadata(
                    title: "Chapter 1",
                    scanlators: new[] { new Scanlator("invalid-scanlator", "Unknown", ScanlatorRole.Translation) },
                    content_folder: null
                )
            },
            allowed_editors: null
        );

        // Act
        var result = _service.ValidateUnit(unit);

        // Assert
        Assert.False(result.IsValid);
        Assert.False(result.Tags.IsValid);
        Assert.False(result.Authors.IsValid);
        Assert.False(result.Scanlators.IsValid);
    }

    [Fact]
    public async Task RunFullValidationAsync_WithNoIssues_ReturnsCleanReport()
    {
        // Arrange - Add valid series to repository
        var series = new Series(
            id: "urn:mvn:series:test1",
            federation_ref: "urn:mvn:node:local",
            title: "Valid Series",
            description: "Test",
            poster: new Poster("url", "alt"),
            media_type: "Photo",
            external_links: new Dictionary<string, string>(),
            reading_direction: "LTR",
            tags: new[] { "action" },
            content_warnings: Array.Empty<string>(),
            authors: new[] { new Author("author-1", "John Doe", "Author") },
            scanlators: new[] { new Scanlator("scanlator-1", "Group A", ScanlatorRole.Translation) },
            groups: null,
            alt_titles: null,
            status: null,
            year: null,
            created_by: "urn:mvn:user:test",
            created_at: DateTime.UtcNow,
            updated_at: DateTime.UtcNow
        );

        _repo.AddSeries(series);

        // Act
        var report = await _service.RunFullValidationAsync();

        // Assert
        Assert.NotNull(report);
        Assert.Equal(1, report.TotalSeries);
        Assert.Empty(report.SeriesIssues);
        Assert.Empty(report.UnitIssues);
    }

    [Fact]
    public async Task RunFullValidationAsync_WithIssues_ReturnsIssuesInReport()
    {
        // Arrange - Add series with invalid data
        var series = new Series(
            id: "urn:mvn:series:test1",
            federation_ref: "urn:mvn:node:local",
            title: "Invalid Series",
            description: "Test",
            poster: new Poster("url", "alt"),
            media_type: "Photo",
            external_links: new Dictionary<string, string>(),
            reading_direction: "LTR",
            tags: new[] { "invalid-tag" },
            content_warnings: Array.Empty<string>(),
            authors: new[] { new Author("invalid-author", "Unknown", "Author") },
            scanlators: Array.Empty<Scanlator>(),
            groups: null,
            alt_titles: null,
            status: null,
            year: null,
            created_by: "urn:mvn:user:test",
            created_at: DateTime.UtcNow,
            updated_at: DateTime.UtcNow
        );

        _repo.AddSeries(series);

        // Act
        var report = await _service.RunFullValidationAsync();

        // Assert
        Assert.NotNull(report);
        Assert.Equal(1, report.TotalSeries);
        Assert.Single(report.SeriesIssues);
        Assert.Equal("urn:mvn:series:test1", report.SeriesIssues[0].EntityUrn);
        Assert.Contains("Invalid tags", report.SeriesIssues[0].Issues[0]);
    }

    [Fact]
    public void ValidateTags_WithEmptyTaxonomyConfig_TreatsAllTagsAsValid()
    {
        // Arrange - Set up empty taxonomy config
        var emptyConfig = new TaxonomyConfig(
            tags: Array.Empty<string>(),
            content_warnings: new[] { "nsfw" },
            types: new[] { "Photo" },
            authors: new[] { new Author("author-1", "John Doe", "Author") },
            scanlators: new[] { new Scanlator("scanlator-1", "Group A", ScanlatorRole.Translation) },
            groups: Array.Empty<Group>()
        );
        _repo.UpdateTaxonomyConfig(emptyConfig);

        var tags = new[] { "any-tag", "another-tag", "whatever" };

        // Act
        var result = _service.ValidateTags(tags);

        // Assert - All tags should be considered valid when taxonomy has no tags configured
        Assert.True(result.IsValid);
        Assert.Equal(3, result.ValidTags.Length);
        Assert.Empty(result.InvalidTags);
        Assert.Empty(result.Suggestions);
    }

    [Fact]
    public void ValidateAuthors_WithEmptyTaxonomyConfig_TreatsAllAuthorsAsValid()
    {
        // Arrange - Set up empty taxonomy config
        var emptyConfig = new TaxonomyConfig(
            tags: new[] { "action" },
            content_warnings: new[] { "nsfw" },
            types: new[] { "Photo" },
            authors: Array.Empty<Author>(),
            scanlators: new[] { new Scanlator("scanlator-1", "Group A", ScanlatorRole.Translation) },
            groups: Array.Empty<Group>()
        );
        _repo.UpdateTaxonomyConfig(emptyConfig);

        var authors = new[] 
        { 
            new Author("any-author", "Any Author", "Author"),
            new Author("another-author", "Another Author", "Artist")
        };

        // Act
        var result = _service.ValidateAuthors(authors);

        // Assert - All authors should be considered valid when taxonomy has no authors configured
        Assert.True(result.IsValid);
        Assert.Equal(2, result.ValidIds.Length);
        Assert.Empty(result.InvalidIds);
    }

    [Fact]
    public void ValidateScanlators_WithEmptyTaxonomyConfig_TreatsAllScanlatorsAsValid()
    {
        // Arrange - Set up empty taxonomy config
        var emptyConfig = new TaxonomyConfig(
            tags: new[] { "action" },
            content_warnings: new[] { "nsfw" },
            types: new[] { "Photo" },
            authors: new[] { new Author("author-1", "John Doe", "Author") },
            scanlators: Array.Empty<Scanlator>(),
            groups: Array.Empty<Group>()
        );
        _repo.UpdateTaxonomyConfig(emptyConfig);

        var scanlators = new[] 
        { 
            new Scanlator("any-scanlator", "Any Group", ScanlatorRole.Translation),
            new Scanlator("another-scanlator", "Another Group", ScanlatorRole.Both)
        };

        // Act
        var result = _service.ValidateScanlators(scanlators);

        // Assert - All scanlators should be considered valid when taxonomy has no scanlators configured
        Assert.True(result.IsValid);
        Assert.Equal(2, result.ValidIds.Length);
        Assert.Empty(result.InvalidIds);
    }

    [Fact]
    public void ValidateGroups_WithEmptyTaxonomyConfig_TreatsAllGroupsAsValid()
    {
        // Arrange - Set up empty taxonomy config
        var emptyConfig = new TaxonomyConfig(
            tags: new[] { "action" },
            content_warnings: new[] { "nsfw" },
            types: new[] { "Photo" },
            authors: new[] { new Author("author-1", "John Doe", "Author") },
            scanlators: new[] { new Scanlator("scanlator-1", "Group A", ScanlatorRole.Translation) },
            groups: Array.Empty<Group>()
        );
        _repo.UpdateTaxonomyConfig(emptyConfig);

        var groups = new[] 
        { 
            new Group("any-group", "Any Publisher"),
            new Group("another-group", "Another Publisher")
        };

        // Act
        var result = _service.ValidateGroups(groups);

        // Assert - All groups should be considered valid when taxonomy has no groups configured
        Assert.True(result.IsValid);
        Assert.Equal(2, result.ValidIds.Length);
        Assert.Empty(result.InvalidIds);
    }
}
