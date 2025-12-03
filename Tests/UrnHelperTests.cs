using MehguViewer.Core.Backend.Services;
using Xunit;

namespace MehguViewer.Core.Tests;

/// <summary>
/// Unit tests for the UrnHelper service.
/// Tests URN creation and parsing according to MehguViewer specifications.
/// </summary>
[Trait("Category", "Unit")]
[Trait("Service", "UrnHelper")]
public class UrnHelperTests
{
    #region URN Creation

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
    public void CreateErrorUrn_WithCode_ReturnsCorrectFormat()
    {
        // Arrange
        var errorCode = "not-found";

        // Act
        var urn = UrnHelper.CreateErrorUrn(errorCode);

        // Assert
        Assert.Equal("urn:mvn:error:not-found", urn);
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

    #endregion

    #region URN Parsing

    [Fact]
    public void Parse_ValidMvnUrn_ReturnsParts()
    {
        // Arrange
        var urn = "urn:mvn:series:12345678-1234-1234-1234-123456789abc";

        // Act
        var parts = UrnHelper.Parse(urn);

        // Assert
        Assert.Equal("mvn", parts.Namespace);
        Assert.Equal("series", parts.Type);
        Assert.Equal("12345678-1234-1234-1234-123456789abc", parts.Id);
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
        Assert.Throws<ArgumentException>(() => UrnHelper.Parse(urn));
    }

    [Fact]
    public void Parse_NullUrn_ThrowsArgumentException()
    {
        // Arrange
        string urn = null!;

        // Act & Assert
        Assert.Throws<ArgumentException>(() => UrnHelper.Parse(urn));
    }

    [Fact]
    public void Parse_InvalidFormat_ThrowsArgumentException()
    {
        // Arrange
        var urn = "not-a-urn";

        // Act & Assert
        Assert.Throws<ArgumentException>(() => UrnHelper.Parse(urn));
    }

    [Fact]
    public void Parse_UnknownNamespace_ThrowsArgumentException()
    {
        // Arrange
        var urn = "urn:unknown:type:id";

        // Act & Assert
        Assert.Throws<ArgumentException>(() => UrnHelper.Parse(urn));
    }

    [Fact]
    public void Parse_MvnUrnWithMissingId_ThrowsArgumentException()
    {
        // Arrange
        var urn = "urn:mvn:series";

        // Act & Assert
        Assert.Throws<ArgumentException>(() => UrnHelper.Parse(urn));
    }

    #endregion
}
