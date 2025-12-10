using MehguViewer.Core.Infrastructures;
using MehguViewer.Core.Services;
using MehguViewer.Core.Workers;
using MehguViewer.Core.Helpers;
using Xunit;

namespace MehguViewer.Core.Tests.Helpers;

/// <summary>
/// Unit tests for the UrnHelper service.
/// Tests URN creation, parsing, and validation according to MehguViewer specifications.
/// </summary>
[Trait("Category", "Unit")]
[Trait("Service", "UrnHelper")]
public class UrnHelperTests
{
    #region URN Creation Tests

    [Fact]
    public void CreateSeriesUrn_ReturnsValidFormat()
    {
        // Act
        var urn = UrnHelper.CreateSeriesUrn();

        // Assert
        Assert.StartsWith("urn:mvn:series:", urn);
        Assert.True(Guid.TryParse(urn.Replace("urn:mvn:series:", ""), out _));
    }

    [Fact]
    public void CreateUnitUrn_ReturnsValidFormat()
    {
        // Act
        var urn = UrnHelper.CreateUnitUrn();

        // Assert
        Assert.StartsWith("urn:mvn:unit:", urn);
        Assert.True(Guid.TryParse(urn.Replace("urn:mvn:unit:", ""), out _));
    }

    [Fact]
    public void CreateUserUrn_ReturnsValidFormat()
    {
        // Act
        var urn = UrnHelper.CreateUserUrn();

        // Assert
        Assert.StartsWith("urn:mvn:user:", urn);
        Assert.True(Guid.TryParse(urn.Replace("urn:mvn:user:", ""), out _));
    }

    [Fact]
    public void CreateAssetUrn_ReturnsValidFormat()
    {
        // Act
        var urn = UrnHelper.CreateAssetUrn();

        // Assert
        Assert.StartsWith("urn:mvn:asset:", urn);
        Assert.True(Guid.TryParse(urn.Replace("urn:mvn:asset:", ""), out _));
    }

    [Fact]
    public void CreateCommentUrn_ReturnsValidFormat()
    {
        // Act
        var urn = UrnHelper.CreateCommentUrn();

        // Assert
        Assert.StartsWith("urn:mvn:comment:", urn);
        Assert.True(Guid.TryParse(urn.Replace("urn:mvn:comment:", ""), out _));
    }
    
    [Fact]
    public void CreateCollectionUrn_ReturnsValidFormat()
    {
        // Act
        var urn = UrnHelper.CreateCollectionUrn();

        // Assert
        Assert.StartsWith("urn:mvn:collection:", urn);
        Assert.True(Guid.TryParse(urn.Replace("urn:mvn:collection:", ""), out _));
    }
    
    [Fact]
    public void CreateTagUrn_ReturnsValidFormat()
    {
        // Act
        var urn = UrnHelper.CreateTagUrn();

        // Assert
        Assert.StartsWith("urn:mvn:tag:", urn);
        Assert.True(Guid.TryParse(urn.Replace("urn:mvn:tag:", ""), out _));
    }

    [Fact]
    public void CreateErrorUrn_WithValidCode_ReturnsCorrectFormat()
    {
        // Arrange
        var errorCode = "not-found";

        // Act
        var urn = UrnHelper.CreateErrorUrn(errorCode);

        // Assert
        Assert.Equal("urn:mvn:error:not-found", urn);
    }
    
    [Fact]
    public void CreateErrorUrn_NormalizesToLowercase()
    {
        // Arrange
        var errorCode = "NOT-FOUND";

        // Act
        var urn = UrnHelper.CreateErrorUrn(errorCode);

        // Assert
        Assert.Equal("urn:mvn:error:not-found", urn);
    }
    
    [Fact]
    public void CreateErrorUrn_WithNullCode_ThrowsArgumentException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => UrnHelper.CreateErrorUrn(null!));
        Assert.Contains("Error code cannot be null", ex.Message);
    }
    
    [Fact]
    public void CreateErrorUrn_WithEmptyCode_ThrowsArgumentException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => UrnHelper.CreateErrorUrn(""));
        Assert.Contains("Error code cannot be null", ex.Message);
    }
    
    [Fact]
    public void CreateErrorUrn_WithInvalidCharacters_ThrowsArgumentException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => UrnHelper.CreateErrorUrn("invalid code!"));
        Assert.Contains("invalid characters", ex.Message);
    }
    
    [Fact]
    public void CreateErrorUrn_WithExcessiveLength_ThrowsArgumentException()
    {
        // Arrange
        var longCode = new string('a', 300);

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => UrnHelper.CreateErrorUrn(longCode));
        Assert.Contains("maximum length", ex.Message);
    }
    
    [Fact]
    public void CreateSourceUrn_WithValidParameters_ReturnsCorrectFormat()
    {
        // Arrange
        var source = "mangadex";
        var id = "abc123";

        // Act
        var urn = UrnHelper.CreateSourceUrn(source, id);

        // Assert
        Assert.Equal("urn:src:mangadex:abc123", urn);
    }
    
    [Fact]
    public void CreateSourceUrn_NormalizesSourceToLowercase()
    {
        // Arrange
        var source = "MangaDex";
        var id = "abc123";

        // Act
        var urn = UrnHelper.CreateSourceUrn(source, id);

        // Assert
        Assert.Equal("urn:src:mangadex:abc123", urn);
    }
    
    [Fact]
    public void CreateSourceUrn_WithNullSource_ThrowsArgumentException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => UrnHelper.CreateSourceUrn(null!, "id"));
        Assert.Contains("Source cannot be null", ex.Message);
    }
    
    [Fact]
    public void CreateSourceUrn_WithNullId_ThrowsArgumentException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => UrnHelper.CreateSourceUrn("source", null!));
        Assert.Contains("ID cannot be null", ex.Message);
    }
    
    [Fact]
    public void CreateSourceUrn_WithInvalidSourceCharacters_ThrowsArgumentException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => UrnHelper.CreateSourceUrn("invalid source!", "id"));
        Assert.Contains("invalid characters", ex.Message);
    }
    
    [Fact]
    public void CreateSourceUrn_WithExcessiveLength_ThrowsArgumentException()
    {
        // Arrange
        var longId = new string('a', 500);

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => UrnHelper.CreateSourceUrn("source", longId));
        Assert.Contains("maximum", ex.Message);
    }

    [Fact]
    public void CreateUrns_AreUnique()
    {
        // Act
        var urn1 = UrnHelper.CreateSeriesUrn();
        var urn2 = UrnHelper.CreateSeriesUrn();
        var urn3 = UrnHelper.CreateSeriesUrn();

        // Assert
        Assert.NotEqual(urn1, urn2);
        Assert.NotEqual(urn2, urn3);
        Assert.NotEqual(urn1, urn3);
    }
    
    [Fact]
    public void CreateUrns_UsesConsistentGuidFormat()
    {
        // Act
        var urn = UrnHelper.CreateSeriesUrn();
        var guidPart = urn.Replace("urn:mvn:series:", "");

        // Assert - Should be lowercase with hyphens (format "D")
        Assert.Matches("^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$", guidPart);
    }

    #endregion

    #region URN Validation Tests
    
    [Fact]
    public void IsValidSeriesUrn_WithValidUrn_ReturnsTrue()
    {
        // Arrange
        var urn = UrnHelper.CreateSeriesUrn();

        // Act & Assert
        Assert.True(UrnHelper.IsValidSeriesUrn(urn));
    }
    
    [Fact]
    public void IsValidSeriesUrn_WithInvalidType_ReturnsFalse()
    {
        // Arrange
        var urn = UrnHelper.CreateUserUrn();

        // Act & Assert
        Assert.False(UrnHelper.IsValidSeriesUrn(urn));
    }
    
    [Fact]
    public void IsValidSeriesUrn_WithNull_ReturnsFalse()
    {
        // Act & Assert
        Assert.False(UrnHelper.IsValidSeriesUrn(null));
    }
    
    [Fact]
    public void IsValidSeriesUrn_WithEmpty_ReturnsFalse()
    {
        // Act & Assert
        Assert.False(UrnHelper.IsValidSeriesUrn(""));
    }
    
    [Fact]
    public void IsValidSeriesUrn_IsCaseInsensitive()
    {
        // Arrange
        var urn = "urn:mvn:SERIES:123";

        // Act & Assert
        Assert.True(UrnHelper.IsValidSeriesUrn(urn));
    }
    
    [Fact]
    public void IsValidUnitUrn_WithValidUrn_ReturnsTrue()
    {
        // Arrange
        var urn = UrnHelper.CreateUnitUrn();

        // Act & Assert
        Assert.True(UrnHelper.IsValidUnitUrn(urn));
    }
    
    [Fact]
    public void IsValidUserUrn_WithValidUrn_ReturnsTrue()
    {
        // Arrange
        var urn = UrnHelper.CreateUserUrn();

        // Act & Assert
        Assert.True(UrnHelper.IsValidUserUrn(urn));
    }
    
    [Fact]
    public void IsValidAssetUrn_WithValidUrn_ReturnsTrue()
    {
        // Arrange
        var urn = UrnHelper.CreateAssetUrn();

        // Act & Assert
        Assert.True(UrnHelper.IsValidAssetUrn(urn));
    }
    
    [Fact]
    public void IsValidCollectionUrn_WithValidUrn_ReturnsTrue()
    {
        // Arrange
        var urn = UrnHelper.CreateCollectionUrn();

        // Act & Assert
        Assert.True(UrnHelper.IsValidCollectionUrn(urn));
    }
    
    [Fact]
    public void IsValid_WithValidMvnUrn_ReturnsTrue()
    {
        // Arrange
        var urn = UrnHelper.CreateSeriesUrn();

        // Act & Assert
        Assert.True(UrnHelper.IsValid(urn));
    }
    
    [Fact]
    public void IsValid_WithValidSourceUrn_ReturnsTrue()
    {
        // Arrange
        var urn = UrnHelper.CreateSourceUrn("mangadex", "abc123");

        // Act & Assert
        Assert.True(UrnHelper.IsValid(urn));
    }
    
    [Fact]
    public void IsValid_WithInvalidUrn_ReturnsFalse()
    {
        // Act & Assert
        Assert.False(UrnHelper.IsValid("not-a-urn"));
    }
    
    [Fact]
    public void IsValid_WithNull_ReturnsFalse()
    {
        // Act & Assert
        Assert.False(UrnHelper.IsValid(null));
    }
    
    [Fact]
    public void IsValid_WithExcessiveLength_ReturnsFalse()
    {
        // Arrange
        var longUrn = "urn:mvn:series:" + new string('a', 600);

        // Act & Assert
        Assert.False(UrnHelper.IsValid(longUrn));
    }

    #endregion

    #region URN Parsing Tests

    [Fact]
    public void Parse_ValidMvnUrn_ReturnsParts()
    {
        // Arrange
        var urn = "urn:mvn:series:12345678-1234-1234-1234-123456789abc";

        // Act
        var parts = UrnHelper.Parse(urn);

        // Assert
        // Assert
        Assert.Equal("mvn", parts.Namespace);
        Assert.Equal("series", parts.Type);
        Assert.Equal("12345678-1234-1234-1234-123456789abc", parts.Id);
    }
    
    [Fact]
    public void Parse_MvnUrn_NormalizesTypeToLowercase()
    {
        // Arrange
        var urn = "urn:mvn:SERIES:123";

        // Act
        var parts = UrnHelper.Parse(urn);

        // Assert
        Assert.Equal("series", parts.Type);
    }

    [Fact]
    public void Parse_ValidSourceUrn_ReturnsParts()
    {
        // Arrange
        var urn = "urn:src:mangadex:abc123";

        // Act
        var parts = UrnHelper.Parse(urn);

        // Assert
        Assert.Equal("src", parts.Namespace);
        Assert.Equal("mangadex", parts.Type);
        Assert.Equal("abc123", parts.Id);
    }

    [Fact]
    public void Parse_SourceUrnWithColonInId_HandlesCorrectly()
    {
        // Arrange - Source ID might contain colons
        var urn = "urn:src:external:item:with:colons";

        // Act
        var parts = UrnHelper.Parse(urn);

        // Assert
        Assert.Equal("src", parts.Namespace);
        Assert.Equal("external", parts.Type);
        Assert.Equal("item:with:colons", parts.Id);
    }

    [Fact]
    public void Parse_EmptyUrn_ThrowsArgumentException()
    {
        // Arrange
        var urn = "";

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => UrnHelper.Parse(urn));
        Assert.Contains("URN cannot be null", ex.Message);
    }

    [Fact]
    public void Parse_NullUrn_ThrowsArgumentException()
    {
        // Arrange
        string urn = null!;

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => UrnHelper.Parse(urn));
        Assert.Contains("URN cannot be null", ex.Message);
    }
    
    [Fact]
    public void Parse_ExcessivelyLongUrn_ThrowsArgumentException()
    {
        // Arrange
        var urn = "urn:mvn:series:" + new string('a', 600);

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => UrnHelper.Parse(urn));
        Assert.Contains("maximum length", ex.Message);
    }

    [Fact]
    public void Parse_InvalidFormat_ThrowsArgumentException()
    {
        // Arrange
        var urn = "not-a-urn";

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => UrnHelper.Parse(urn));
        Assert.Contains("Invalid URN format", ex.Message);
    }

    [Fact]
    public void Parse_UnknownNamespace_ThrowsArgumentException()
    {
        // Arrange
        var urn = "urn:unknown:type:id";

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => UrnHelper.Parse(urn));
        Assert.Contains("Unknown URN namespace", ex.Message);
    }

    [Fact]
    public void Parse_MvnUrnWithMissingId_ThrowsArgumentException()
    {
        // Arrange
        var urn = "urn:mvn:series";

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => UrnHelper.Parse(urn));
        Assert.Contains("Invalid MehguViewer URN", ex.Message);
    }
    
    [Fact]
    public void Parse_MvnUrnWithEmptyId_ThrowsArgumentException()
    {
        // Arrange
        var urn = "urn:mvn:series:";

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => UrnHelper.Parse(urn));
        Assert.Contains("ID cannot be empty", ex.Message);
    }
    
    [Fact]
    public void Parse_MvnUrnWithInvalidType_ThrowsArgumentException()
    {
        // Arrange
        var urn = "urn:mvn:invalidtype:123";

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => UrnHelper.Parse(urn));
        Assert.Contains("Unknown type", ex.Message);
    }
    
    [Fact]
    public void Parse_MvnUrnWithExcessiveIdLength_ThrowsArgumentException()
    {
        // Arrange
        var longId = new string('a', 300);
        var urn = $"urn:mvn:series:{longId}";

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => UrnHelper.Parse(urn));
        Assert.Contains("maximum length", ex.Message);
    }
    
    [Fact]
    public void Parse_SourceUrnWithInvalidSourceCharacters_ThrowsArgumentException()
    {
        // Arrange
        var urn = "urn:src:invalid source!:id";

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => UrnHelper.Parse(urn));
        Assert.Contains("invalid characters", ex.Message);
    }
    
    [Fact]
    public void Parse_SourceUrnWithEmptyId_ThrowsArgumentException()
    {
        // Arrange
        var urn = "urn:src:source:";

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => UrnHelper.Parse(urn));
        Assert.Contains("ID cannot be empty", ex.Message);
    }
    
    [Fact]
    public void TryParse_WithValidUrn_ReturnsTrue()
    {
        // Arrange
        var urn = UrnHelper.CreateSeriesUrn();

        // Act
        var result = UrnHelper.TryParse(urn, out var parts);

        // Assert
        Assert.True(result);
        Assert.NotNull(parts);
        Assert.Equal("mvn", parts.Namespace);
        Assert.Equal("series", parts.Type);
    }
    
    [Fact]
    public void TryParse_WithInvalidUrn_ReturnsFalse()
    {
        // Arrange
        var urn = "not-a-urn";

        // Act
        var result = UrnHelper.TryParse(urn, out var parts);

        // Assert
        Assert.False(result);
        Assert.Null(parts);
    }
    
    [Fact]
    public void TryParse_WithNull_ReturnsFalse()
    {
        // Act
        var result = UrnHelper.TryParse(null, out var parts);

        // Assert
        Assert.False(result);
        Assert.Null(parts);
    }

    #endregion
    
    #region URN Extraction Tests
    
    [Fact]
    public void ExtractId_WithValidUrn_ReturnsId()
    {
        // Arrange
        var urn = "urn:mvn:series:12345";

        // Act
        var id = UrnHelper.ExtractId(urn);

        // Assert
        Assert.Equal("12345", id);
    }
    
    [Fact]
    public void ExtractId_WithColonInId_ReturnsFullId()
    {
        // Arrange
        var urn = "urn:src:source:id:with:colons";

        // Act
        var id = UrnHelper.ExtractId(urn);

        // Assert
        Assert.Equal("id:with:colons", id);
    }
    
    [Fact]
    public void ExtractId_WithInvalidUrn_ThrowsArgumentException()
    {
        // Arrange
        var urn = "not-a-urn";

        // Act & Assert
        Assert.Throws<ArgumentException>(() => UrnHelper.ExtractId(urn));
    }
    
    [Fact]
    public void TryExtractId_WithValidUrn_ReturnsTrue()
    {
        // Arrange
        var urn = "urn:mvn:series:12345";

        // Act
        var result = UrnHelper.TryExtractId(urn, out var id);

        // Assert
        Assert.True(result);
        Assert.Equal("12345", id);
    }
    
    [Fact]
    public void TryExtractId_WithInvalidUrn_ReturnsFalse()
    {
        // Arrange
        var urn = "not-a-urn";

        // Act
        var result = UrnHelper.TryExtractId(urn, out var id);

        // Assert
        Assert.False(result);
        Assert.Null(id);
    }
    
    [Fact]
    public void TryExtractId_WithNull_ReturnsFalse()
    {
        // Act
        var result = UrnHelper.TryExtractId(null, out var id);

        // Assert
        Assert.False(result);
        Assert.Null(id);
    }
    
    [Fact]
    public void ExtractType_WithValidMvnUrn_ReturnsType()
    {
        // Arrange
        var urn = "urn:mvn:series:12345";

        // Act
        var type = UrnHelper.ExtractType(urn);

        // Assert
        Assert.Equal("series", type);
    }
    
    [Fact]
    public void ExtractType_WithSourceUrn_ThrowsArgumentException()
    {
        // Arrange
        var urn = "urn:src:source:id";

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => UrnHelper.ExtractType(urn));
        Assert.Contains("non-MehguViewer URN", ex.Message);
    }
    
    [Fact]
    public void ExtractType_WithInvalidUrn_ThrowsArgumentException()
    {
        // Arrange
        var urn = "not-a-urn";

        // Act & Assert
        Assert.Throws<ArgumentException>(() => UrnHelper.ExtractType(urn));
    }
    
    [Fact]
    public void TryExtractType_WithValidMvnUrn_ReturnsTrue()
    {
        // Arrange
        var urn = "urn:mvn:series:12345";

        // Act
        var result = UrnHelper.TryExtractType(urn, out var type);

        // Assert
        Assert.True(result);
        Assert.Equal("series", type);
    }
    
    [Fact]
    public void TryExtractType_WithSourceUrn_ReturnsFalse()
    {
        // Arrange
        var urn = "urn:src:source:id";

        // Act
        var result = UrnHelper.TryExtractType(urn, out var type);

        // Assert
        Assert.False(result);
        Assert.Null(type);
    }
    
    [Fact]
    public void TryExtractType_WithInvalidUrn_ReturnsFalse()
    {
        // Arrange
        var urn = "not-a-urn";

        // Act
        var result = UrnHelper.TryExtractType(urn, out var type);

        // Assert
        Assert.False(result);
        Assert.Null(type);
    }
    
    [Fact]
    public void TryExtractType_WithNull_ReturnsFalse()
    {
        // Act
        var result = UrnHelper.TryExtractType(null, out var type);

        // Assert
        Assert.False(result);
        Assert.Null(type);
    }

    #endregion
    
    #region Security and Edge Case Tests
    
    [Fact]
    public void Parse_WithWhitespaceUrn_ThrowsArgumentException()
    {
        // Arrange
        var urn = "   ";

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => UrnHelper.Parse(urn));
        Assert.Contains("URN cannot be null", ex.Message);
    }
    
    [Fact]
    public void CreateErrorUrn_WithUnderscoresAndHyphens_Succeeds()
    {
        // Arrange
        var code = "error_code-123";

        // Act
        var urn = UrnHelper.CreateErrorUrn(code);

        // Assert
        Assert.Equal("urn:mvn:error:error_code-123", urn);
    }
    
    [Fact]
    public void CreateSourceUrn_WithUnderscoresAndHyphens_Succeeds()
    {
        // Arrange
        var source = "manga_dex-v2";
        var id = "test_id-123";

        // Act
        var urn = UrnHelper.CreateSourceUrn(source, id);

        // Assert
        Assert.StartsWith("urn:src:manga_dex-v2:", urn);
    }
    
    [Theory]
    [InlineData("urn:mvn:series:123")]
    [InlineData("URN:MVN:SERIES:123")]
    [InlineData("urn:MVN:series:123")]
    public void Parse_IsCaseInsensitive(string urn)
    {
        // Act
        var parts = UrnHelper.Parse(urn);

        // Assert
        Assert.Equal("mvn", parts.Namespace);
        Assert.Equal("series", parts.Type);
        Assert.Equal("123", parts.Id);
    }
    
    [Theory]
    [InlineData("series")]
    [InlineData("unit")]
    [InlineData("user")]
    [InlineData("asset")]
    [InlineData("comment")]
    [InlineData("collection")]
    [InlineData("tag")]
    [InlineData("annotation")]
    [InlineData("session")]
    [InlineData("error")]
    public void Parse_AcceptsAllValidMvnTypes(string type)
    {
        // Arrange
        var urn = $"urn:mvn:{type}:test-id";

        // Act
        var parts = UrnHelper.Parse(urn);

        // Assert
        Assert.Equal(type, parts.Type);
    }
    
    [Fact]
    public void CreateUrns_PerformanceTest_CreatesThousandsQuickly()
    {
        // Act - Create 10,000 URNs
        var urns = new List<string>();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        for (int i = 0; i < 10000; i++)
        {
            urns.Add(UrnHelper.CreateSeriesUrn());
        }
        
        stopwatch.Stop();

        // Assert - Should complete in reasonable time (< 1 second)
        Assert.True(stopwatch.ElapsedMilliseconds < 1000, 
            $"Creating 10,000 URNs took {stopwatch.ElapsedMilliseconds}ms");
        Assert.Equal(10000, urns.Distinct().Count()); // All should be unique
    }

    #endregion
}

