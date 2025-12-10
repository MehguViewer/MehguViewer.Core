using MehguViewer.Core.Shared;
using Xunit;

namespace Tests.Shared;

/// <summary>
/// Unit tests for Domain model constants, validators, and helper methods.
/// Tests business logic for media types, content warnings, and reading directions.
/// </summary>
public class DomainHelpersTests
{
    #region MediaTypes Tests

    [Theory]
    [InlineData("Photo")]
    [InlineData("Text")]
    [InlineData("Video")]
    public void MediaTypes_ValidType_IsValid(string type)
    {
        // Act
        var result = MediaTypes.IsValid(type);

        // Assert
        Assert.True(result);
    }

    [Theory]
    [InlineData("photo")]    // Lowercase
    [InlineData("PHOTO")]    // Uppercase
    [InlineData("pHoTo")]    // Mixed case
    public void MediaTypes_CaseInsensitive_IsValid(string type)
    {
        // Act
        var result = MediaTypes.IsValid(type);

        // Assert
        Assert.True(result);
    }

    [Theory]
    [InlineData("InvalidType")]
    [InlineData("")]
    [InlineData("Audio")]
    [InlineData(null)]
    public void MediaTypes_InvalidType_IsNotValid(string? type)
    {
        // Act
        var result = MediaTypes.IsValid(type);

        // Assert
        Assert.False(result);
    }

    [Theory]
    [InlineData("photo", "Photo")]
    [InlineData("PHOTO", "Photo")]
    [InlineData("Photo", "Photo")]
    [InlineData("text", "Text")]
    [InlineData("video", "Video")]
    public void MediaTypes_Normalize_ReturnsCorrectCase(string input, string expected)
    {
        // Act
        var result = MediaTypes.Normalize(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("InvalidType")]
    [InlineData("")]
    [InlineData(null)]
    public void MediaTypes_NormalizeInvalid_ReturnsNull(string? input)
    {
        // Act
        var result = MediaTypes.Normalize(input);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void MediaTypes_All_ContainsAllTypes()
    {
        // Assert
        Assert.Equal(3, MediaTypes.All.Length);
        Assert.Contains("Photo", MediaTypes.All);
        Assert.Contains("Text", MediaTypes.All);
        Assert.Contains("Video", MediaTypes.All);
    }

    #endregion

    #region ContentWarnings Tests

    [Theory]
    [InlineData("nsfw")]
    [InlineData("gore")]
    [InlineData("violence")]
    [InlineData("language")]
    [InlineData("suggestive")]
    public void ContentWarnings_ValidWarning_IsValid(string warning)
    {
        // Act
        var result = ContentWarnings.IsValid(warning);

        // Assert
        Assert.True(result);
    }

    [Theory]
    [InlineData("NSFW")]     // Uppercase
    [InlineData("Gore")]     // Title case
    [InlineData("VIOLENCE")] // Uppercase
    public void ContentWarnings_CaseInsensitive_IsValid(string warning)
    {
        // Act
        var result = ContentWarnings.IsValid(warning);

        // Assert
        Assert.True(result);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("unknown")]
    public void ContentWarnings_InvalidWarning_IsNotValid(string? warning)
    {
        // Act
        var result = ContentWarnings.IsValid(warning);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ContentWarnings_EmptyString_IsValid()
    {
        // Act
        var result = ContentWarnings.IsValid("");

        // Assert
        Assert.True(result); // Empty string is treated same as null (not specified)
    }

    [Fact]
    public void ContentWarnings_Null_IsValid()
    {
        // Act
        var result = ContentWarnings.IsValid(null);

        // Assert
        Assert.True(result); // Null is considered valid (optional)
    }

    [Theory]
    [InlineData("nsfw", "nsfw")]
    [InlineData("NSFW", "nsfw")]
    [InlineData("Nsfw", "nsfw")]
    [InlineData("gore", "gore")]
    [InlineData("VIOLENCE", "violence")]
    public void ContentWarnings_Normalize_ReturnsCorrectCase(string input, string expected)
    {
        // Act
        var result = ContentWarnings.Normalize(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ContentWarnings_NormalizeAll_RemovesDuplicates()
    {
        // Arrange
        var warnings = new[] { "nsfw", "NSFW", "nsfw", "gore", "Gore" };

        // Act
        var result = ContentWarnings.NormalizeAll(warnings);

        // Assert
        Assert.Equal(2, result.Length);
        Assert.Contains("nsfw", result);
        Assert.Contains("gore", result);
    }

    [Fact]
    public void ContentWarnings_NormalizeAll_RemovesInvalid()
    {
        // Arrange
        var warnings = new[] { "nsfw", "invalid", "gore", "unknown" };

        // Act
        var result = ContentWarnings.NormalizeAll(warnings);

        // Assert
        Assert.Equal(2, result.Length);
        Assert.Contains("nsfw", result);
        Assert.Contains("gore", result);
        Assert.DoesNotContain("invalid", result);
    }

    [Fact]
    public void ContentWarnings_NormalizeAll_NullArray_ReturnsEmpty()
    {
        // Act
        var result = ContentWarnings.NormalizeAll(null);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ContentWarnings_All_ContainsAllWarnings()
    {
        // Assert
        Assert.Equal(5, ContentWarnings.All.Length);
        Assert.Contains("nsfw", ContentWarnings.All);
        Assert.Contains("gore", ContentWarnings.All);
        Assert.Contains("violence", ContentWarnings.All);
        Assert.Contains("language", ContentWarnings.All);
        Assert.Contains("suggestive", ContentWarnings.All);
    }

    #endregion

    #region ReadingDirections Tests

    [Theory]
    [InlineData("LTR")]
    [InlineData("RTL")]
    [InlineData("WEBTOON")]
    public void ReadingDirections_ValidDirection_IsValid(string direction)
    {
        // Act
        var result = ReadingDirections.IsValid(direction);

        // Assert
        Assert.True(result);
    }

    [Theory]
    [InlineData("ltr")]      // Lowercase
    [InlineData("rtl")]      // Lowercase
    [InlineData("webtoon")]  // Lowercase
    [InlineData("Ltr")]      // Mixed case
    public void ReadingDirections_CaseInsensitive_IsValid(string direction)
    {
        // Act
        var result = ReadingDirections.IsValid(direction);

        // Assert
        Assert.True(result);
    }

    [Theory]
    [InlineData("Invalid")]
    [InlineData("TopToBottom")]
    public void ReadingDirections_InvalidDirection_IsNotValid(string? direction)
    {
        // Act
        var result = ReadingDirections.IsValid(direction);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ReadingDirections_EmptyString_IsValid()
    {
        // Act
        var result = ReadingDirections.IsValid("");

        // Assert
        Assert.True(result); // Empty string is treated same as null (not specified)
    }

    [Fact]
    public void ReadingDirections_Null_IsValid()
    {
        // Act
        var result = ReadingDirections.IsValid(null);

        // Assert
        Assert.True(result); // Null is considered valid (optional)
    }

    [Theory]
    [InlineData("ltr", "LTR")]
    [InlineData("LTR", "LTR")]
    [InlineData("Ltr", "LTR")]
    [InlineData("rtl", "RTL")]
    [InlineData("webtoon", "WEBTOON")]
    [InlineData("WEBTOON", "WEBTOON")]
    public void ReadingDirections_Normalize_ReturnsCorrectCase(string input, string expected)
    {
        // Act
        var result = ReadingDirections.Normalize(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Invalid")]
    [InlineData("")]
    [InlineData(null)]
    public void ReadingDirections_NormalizeInvalid_ReturnsNull(string? input)
    {
        // Act
        var result = ReadingDirections.Normalize(input);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ReadingDirections_All_ContainsAllDirections()
    {
        // Assert
        Assert.Equal(3, ReadingDirections.All.Length);
        Assert.Contains("LTR", ReadingDirections.All);
        Assert.Contains("RTL", ReadingDirections.All);
        Assert.Contains("WEBTOON", ReadingDirections.All);
    }

    #endregion

    #region ScanlatorRole Tests

    [Fact]
    public void ScanlatorRole_AllValues_AreDefined()
    {
        // Arrange
        var expectedValues = new[] { ScanlatorRole.Translation, ScanlatorRole.Scanlation, ScanlatorRole.Both };

        // Act
        var actualValues = Enum.GetValues<ScanlatorRole>();

        // Assert
        Assert.Equal(3, actualValues.Length);
        foreach (var expected in expectedValues)
        {
            Assert.Contains(expected, actualValues);
        }
    }

    [Theory]
    [InlineData(ScanlatorRole.Translation)]
    [InlineData(ScanlatorRole.Scanlation)]
    [InlineData(ScanlatorRole.Both)]
    public void ScanlatorRole_ValidRole_CanBeUsed(ScanlatorRole role)
    {
        // Arrange & Act
        var scanlator = new Scanlator("id", "name", role);

        // Assert
        Assert.Equal(role, scanlator.role);
    }

    #endregion

    #region Record Validation Tests

    [Fact]
    public void Author_ValidData_CreatesSuccessfully()
    {
        // Arrange & Act
        var author = new Author("author-1", "John Doe", "Author");

        // Assert
        Assert.Equal("author-1", author.id);
        Assert.Equal("John Doe", author.name);
        Assert.Equal("Author", author.role);
    }

    [Fact]
    public void Author_NullRole_IsAllowed()
    {
        // Arrange & Act
        var author = new Author("author-1", "John Doe");

        // Assert
        Assert.Null(author.role);
    }

    [Fact]
    public void Scanlator_ValidData_CreatesSuccessfully()
    {
        // Arrange & Act
        var scanlator = new Scanlator("scan-1", "Group Name", ScanlatorRole.Both);

        // Assert
        Assert.Equal("scan-1", scanlator.id);
        Assert.Equal("Group Name", scanlator.name);
        Assert.Equal(ScanlatorRole.Both, scanlator.role);
    }

    [Fact]
    public void Group_ValidData_CreatesSuccessfully()
    {
        // Arrange & Act
        var group = new Group(
            "group-1",
            "Publisher Inc",
            "A publishing company",
            "https://publisher.com",
            "https://discord.gg/publisher"
        );

        // Assert
        Assert.Equal("group-1", group.id);
        Assert.Equal("Publisher Inc", group.name);
        Assert.Equal("A publishing company", group.description);
        Assert.Equal("https://publisher.com", group.website);
        Assert.Equal("https://discord.gg/publisher", group.discord);
    }

    [Fact]
    public void CursorPagination_HasMore_True()
    {
        // Arrange & Act
        var pagination = new CursorPagination("next-cursor-123", true);

        // Assert
        Assert.Equal("next-cursor-123", pagination.next_cursor);
        Assert.True(pagination.has_more);
    }

    [Fact]
    public void CursorPagination_NoMore_NullCursor()
    {
        // Arrange & Act
        var pagination = new CursorPagination(null, false);

        // Assert
        Assert.Null(pagination.next_cursor);
        Assert.False(pagination.has_more);
    }

    #endregion
}
