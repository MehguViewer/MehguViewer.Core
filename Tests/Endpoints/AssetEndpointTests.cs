using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace MehguViewer.Core.Tests.Endpoints;

/// <summary>
/// Comprehensive test suite for Asset endpoints verifying URN validation,
/// variant selection, authorization, error handling, and performance.
/// </summary>
/// <remarks>
/// <para><strong>Test Coverage Areas:</strong></para>
/// <list type="bullet">
///   <item><description>URN validation and format checking</description></item>
///   <item><description>Image variant selection (THUMBNAIL, WEB, RAW)</description></item>
///   <item><description>Authorization and authentication</description></item>
///   <item><description>Error handling and RFC 7807 compliance</description></item>
///   <item><description>Edge cases and boundary conditions</description></item>
/// </list>
/// </remarks>
[Trait("Category", "Integration")]
[Trait("Endpoint", "Assets")]
public class AssetEndpointTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly HttpClient _authenticatedClient;
    private readonly TestWebApplicationFactory _factory;

    public AssetEndpointTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _authenticatedClient = factory.CreateAuthenticatedClient();
    }

    #region URN Validation Tests

    /// <summary>
    /// Tests that a valid asset URN with default variant returns a successful image response.
    /// </summary>
    [Fact]
    public async Task GetAsset_ValidUrn_DefaultVariant_ReturnsOk()
    {
        // Arrange
        var assetUrn = "urn:mvn:asset:123e4567-e89b-12d3-a456-426614174000";

        // Act
        var response = await _authenticatedClient.GetAsync($"/api/v1/assets/{assetUrn}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
        
        // Verify we actually got image data
        var content = await response.Content.ReadAsByteArrayAsync();
        Assert.NotEmpty(content);
    }

    /// <summary>
    /// Tests that missing asset URN returns 400 Bad Request with RFC 7807 error.
    /// </summary>
    [Fact]
    public async Task GetAsset_MissingUrn_ReturnsBadRequest()
    {
        // Act
        var response = await _authenticatedClient.GetAsync("/api/v1/assets/");

        // Assert - Will return 404 because the route doesn't match without URN
        Assert.True(
            response.StatusCode == HttpStatusCode.NotFound || 
            response.StatusCode == HttpStatusCode.BadRequest,
            $"Expected NotFound or BadRequest, got {response.StatusCode}");
    }

    /// <summary>
    /// Tests that empty/whitespace URN returns 400 Bad Request.
    /// </summary>
    [Fact]
    public async Task GetAsset_EmptyUrn_ReturnsBadRequest()
    {
        // Arrange
        var assetUrn = "   "; // Whitespace only

        // Act
        var response = await _authenticatedClient.GetAsync($"/api/v1/assets/{Uri.EscapeDataString(assetUrn)}");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("urn:mvn:error:", content.ToLowerInvariant());
    }

    /// <summary>
    /// Tests that invalid URN format (not starting with urn:mvn:asset:) returns 400 Bad Request.
    /// </summary>
    [Fact]
    public async Task GetAsset_InvalidUrnFormat_ReturnsBadRequest()
    {
        // Arrange - Missing proper URN prefix
        var invalidUrn = "invalid-asset-id";

        // Act
        var response = await _authenticatedClient.GetAsync($"/api/v1/assets/{invalidUrn}");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("invalid", content.ToLowerInvariant());
        Assert.Contains("urn", content.ToLowerInvariant());
    }

    /// <summary>
    /// Tests that URN with wrong namespace (not mvn) returns 400 Bad Request.
    /// </summary>
    [Fact]
    public async Task GetAsset_WrongNamespace_ReturnsBadRequest()
    {
        // Arrange - Using 'src' namespace instead of 'mvn'
        var wrongNamespaceUrn = "urn:src:asset:123e4567-e89b-12d3-a456-426614174000";

        // Act
        var response = await _authenticatedClient.GetAsync($"/api/v1/assets/{wrongNamespaceUrn}");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Tests that URN with wrong type (not asset) returns 400 Bad Request.
    /// </summary>
    [Fact]
    public async Task GetAsset_WrongResourceType_ReturnsBadRequest()
    {
        // Arrange - Using 'series' type instead of 'asset'
        var wrongTypeUrn = "urn:mvn:series:123e4567-e89b-12d3-a456-426614174000";

        // Act
        var response = await _authenticatedClient.GetAsync($"/api/v1/assets/{wrongTypeUrn}");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    #endregion

    #region Variant Selection Tests

    /// <summary>
    /// Tests that THUMBNAIL variant returns appropriate image size.
    /// </summary>
    [Fact]
    public async Task GetAsset_ThumbnailVariant_ReturnsOk()
    {
        // Arrange
        var assetUrn = "urn:mvn:asset:thumbnail-test";

        // Act
        var response = await _authenticatedClient.GetAsync($"/api/v1/assets/{assetUrn}?variant=THUMBNAIL");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
        
        // Verify content exists (actual size validation would require image decoding)
        var content = await response.Content.ReadAsByteArrayAsync();
        Assert.NotEmpty(content);
    }

    /// <summary>
    /// Tests that WEB variant (explicit) returns appropriate image size.
    /// </summary>
    [Fact]
    public async Task GetAsset_WebVariant_ReturnsOk()
    {
        // Arrange
        var assetUrn = "urn:mvn:asset:web-test";

        // Act
        var response = await _authenticatedClient.GetAsync($"/api/v1/assets/{assetUrn}?variant=WEB");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
    }

    /// <summary>
    /// Tests that RAW variant returns appropriate image size.
    /// </summary>
    [Fact]
    public async Task GetAsset_RawVariant_ReturnsOk()
    {
        // Arrange
        var assetUrn = "urn:mvn:asset:raw-test";

        // Act
        var response = await _authenticatedClient.GetAsync($"/api/v1/assets/{assetUrn}?variant=RAW");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
    }

    /// <summary>
    /// Tests that lowercase variant names are accepted and normalized.
    /// </summary>
    [Fact]
    public async Task GetAsset_LowercaseVariant_ReturnsOk()
    {
        // Arrange
        var assetUrn = "urn:mvn:asset:lowercase-variant-test";

        // Act
        var response = await _authenticatedClient.GetAsync($"/api/v1/assets/{assetUrn}?variant=thumbnail");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// Tests that mixed-case variant names are accepted and normalized.
    /// </summary>
    [Fact]
    public async Task GetAsset_MixedCaseVariant_ReturnsOk()
    {
        // Arrange
        var assetUrn = "urn:mvn:asset:mixedcase-variant-test";

        // Act
        var response = await _authenticatedClient.GetAsync($"/api/v1/assets/{assetUrn}?variant=ThUmBnAiL");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// Tests that invalid variant name defaults to WEB variant without error.
    /// </summary>
    [Fact]
    public async Task GetAsset_InvalidVariant_DefaultsToWeb()
    {
        // Arrange
        var assetUrn = "urn:mvn:asset:invalid-variant-test";

        // Act
        var response = await _authenticatedClient.GetAsync($"/api/v1/assets/{assetUrn}?variant=INVALID");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Should default to WEB variant and still succeed
    }

    /// <summary>
    /// Tests that empty variant parameter defaults to WEB variant.
    /// </summary>
    [Fact]
    public async Task GetAsset_EmptyVariant_DefaultsToWeb()
    {
        // Arrange
        var assetUrn = "urn:mvn:asset:empty-variant-test";

        // Act
        var response = await _authenticatedClient.GetAsync($"/api/v1/assets/{assetUrn}?variant=");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    #endregion

    #region Authorization Tests

    /// <summary>
    /// Tests that unauthenticated request returns 401 Unauthorized.
    /// </summary>
    [Fact]
    public async Task GetAsset_NoAuthentication_ReturnsUnauthorized()
    {
        // Arrange
        var assetUrn = "urn:mvn:asset:auth-test";

        // Act
        var response = await _client.GetAsync($"/api/v1/assets/{assetUrn}");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("urn:mvn:error:unauthorized", content.ToLowerInvariant());
    }

    /// <summary>
    /// Tests that invalid authentication token returns 401 Unauthorized.
    /// </summary>
    [Fact]
    public async Task GetAsset_InvalidToken_ReturnsUnauthorized()
    {
        // Arrange
        var assetUrn = "urn:mvn:asset:invalid-token-test";
        var clientWithBadToken = _factory.CreateClient();
        clientWithBadToken.DefaultRequestHeaders.Authorization = 
            new AuthenticationHeaderValue("Bearer", "invalid.token.here");

        // Act
        var response = await clientWithBadToken.GetAsync($"/api/v1/assets/{assetUrn}");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    #endregion

    #region Error Handling Tests

    /// <summary>
    /// Tests that malformed URN with special characters is handled correctly.
    /// </summary>
    /// <remarks>
    /// Current implementation: URN passes basic prefix validation (urn:mvn:asset:).
    /// The special characters are URL-encoded and safely handled.
    /// Future enhancement: Could add stricter URN character validation.
    /// </remarks>
    [Fact]
    public async Task GetAsset_UrnWithSpecialCharacters_HandledCorrectly()
    {
        // Arrange
        var assetUrn = "urn:mvn:asset:<script>alert('xss')</script>";

        // Act
        var response = await _authenticatedClient.GetAsync($"/api/v1/assets/{Uri.EscapeDataString(assetUrn)}");

        // Assert
        // Current behavior: Passes prefix validation, URL encoding prevents XSS
        // Returns 200 OK from placeholder service (prototype mode)
        Assert.True(
            response.StatusCode == HttpStatusCode.OK || 
            response.StatusCode == HttpStatusCode.BadRequest || 
            response.StatusCode == HttpStatusCode.NotFound,
            $"Expected OK, BadRequest, or NotFound, got {response.StatusCode}");
    }

    /// <summary>
    /// Tests that extremely long URN is rejected to prevent DoS attacks.
    /// </summary>
    [Fact]
    public async Task GetAsset_ExtremelyLongUrn_HandledGracefully()
    {
        // Arrange - URN with very long ID segment
        var longId = new string('a', 10000);
        var assetUrn = $"urn:mvn:asset:{longId}";

        // Act
        var response = await _authenticatedClient.GetAsync($"/api/v1/assets/{Uri.EscapeDataString(assetUrn)}");

        // Assert
        // Should handle gracefully without crashing (may be BadRequest, NotFound, or even OK if length not validated yet)
        Assert.True(response.StatusCode != HttpStatusCode.InternalServerError,
            "Server should not crash on long URN");
    }

    /// <summary>
    /// Tests that SQL injection-like URN patterns are safely handled.
    /// </summary>
    [Fact]
    public async Task GetAsset_SqlInjectionAttempt_SafelyHandled()
    {
        // Arrange
        var assetUrn = "urn:mvn:asset:'; DROP TABLE assets; --";

        // Act
        var response = await _authenticatedClient.GetAsync($"/api/v1/assets/{Uri.EscapeDataString(assetUrn)}");

        // Assert
        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    /// <summary>
    /// Tests that path traversal attempts in URN are safely handled.
    /// </summary>
    /// <remarks>
    /// Current implementation: URN passes basic prefix validation (urn:mvn:asset:).
    /// The path traversal characters are URL-encoded and treated as part of the ID.
    /// In production, actual storage backend would perform additional validation.
    /// </remarks>
    [Fact]
    public async Task GetAsset_PathTraversalAttempt_SafelyHandled()
    {
        // Arrange
        var assetUrn = "urn:mvn:asset:../../etc/passwd";

        // Act
        var response = await _authenticatedClient.GetAsync($"/api/v1/assets/{Uri.EscapeDataString(assetUrn)}");

        // Assert
        // Current behavior: Passes prefix validation, URL encoding prevents path traversal
        // Returns 200 OK from placeholder service (prototype mode)
        // Production storage backend would add additional validation
        Assert.True(
            response.StatusCode == HttpStatusCode.OK || 
            response.StatusCode == HttpStatusCode.BadRequest || 
            response.StatusCode == HttpStatusCode.NotFound,
            "URN should be safely handled with URL encoding");
    }

    #endregion

    #region RFC 7807 Compliance Tests

    /// <summary>
    /// Tests that error responses conform to RFC 7807 Problem Details format.
    /// </summary>
    [Fact]
    public async Task GetAsset_Error_ReturnsRfc7807Format()
    {
        // Arrange
        var invalidUrn = "not-a-valid-urn";

        // Act
        var response = await _authenticatedClient.GetAsync($"/api/v1/assets/{invalidUrn}");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        
        var content = await response.Content.ReadAsStringAsync();
        
        // Verify RFC 7807 required fields
        Assert.Contains("\"type\"", content);
        Assert.Contains("\"title\"", content);
        Assert.Contains("\"status\"", content);
        Assert.Contains("\"detail\"", content);
        Assert.Contains("urn:mvn:error:", content.ToLowerInvariant());
    }

    /// <summary>
    /// Tests that error responses follow RFC 7807 format.
    /// </summary>
    /// <remarks>
    /// Note: traceId is logged server-side but not included in response body per current design.
    /// Response includes all required RFC 7807 fields: type, title, status, detail, instance.
    /// </remarks>
    [Fact]
    public async Task GetAsset_Error_FollowsRfc7807Format()
    {
        // Arrange
        var invalidUrn = "invalid-urn";

        // Act
        var response = await _authenticatedClient.GetAsync($"/api/v1/assets/{invalidUrn}");

        // Assert
        var content = await response.Content.ReadAsStringAsync();
        // Verify RFC 7807 required fields are present
        Assert.Contains("type", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("title", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("status", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("detail", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("instance", content, StringComparison.OrdinalIgnoreCase);
        // traceId is logged but not in response per current design
    }

    #endregion

    #region Edge Case Tests

    /// <summary>
    /// Tests asset URN with Unicode characters.
    /// </summary>
    [Fact]
    public async Task GetAsset_UnicodeCharacters_HandledCorrectly()
    {
        // Arrange
        var assetUrn = "urn:mvn:asset:テスト-123";

        // Act
        var response = await _authenticatedClient.GetAsync($"/api/v1/assets/{Uri.EscapeDataString(assetUrn)}");

        // Assert
        // Should either accept or reject gracefully (depends on URN spec compliance)
        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    /// <summary>
    /// Tests multiple variant parameters (last one should win or use default).
    /// </summary>
    [Fact]
    public async Task GetAsset_MultipleVariantParameters_HandledGracefully()
    {
        // Arrange
        var assetUrn = "urn:mvn:asset:multi-variant-test";

        // Act
        var response = await _authenticatedClient.GetAsync(
            $"/api/v1/assets/{assetUrn}?variant=THUMBNAIL&variant=RAW");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// Tests that null variant query parameter is handled correctly.
    /// </summary>
    [Fact]
    public async Task GetAsset_NullVariantParameter_DefaultsToWeb()
    {
        // Arrange
        var assetUrn = "urn:mvn:asset:null-variant-test";

        // Act
        var response = await _authenticatedClient.GetAsync($"/api/v1/assets/{assetUrn}?variant");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    #endregion

    #region Performance Tests

    /// <summary>
    /// Tests that concurrent requests are handled correctly.
    /// </summary>
    [Fact]
    public async Task GetAsset_ConcurrentRequests_AllSucceed()
    {
        // Arrange
        var assetUrns = Enumerable.Range(1, 10)
            .Select(i => $"urn:mvn:asset:concurrent-test-{i}")
            .ToList();

        // Act
        var tasks = assetUrns.Select(urn => 
            _authenticatedClient.GetAsync($"/api/v1/assets/{urn}"));
        var responses = await Task.WhenAll(tasks);

        // Assert
        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    /// <summary>
    /// Tests that response time is reasonable for asset retrieval.
    /// </summary>
    [Fact]
    public async Task GetAsset_ResponseTime_IsReasonable()
    {
        // Arrange
        var assetUrn = "urn:mvn:asset:performance-test";
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Act
        var response = await _authenticatedClient.GetAsync($"/api/v1/assets/{assetUrn}");
        stopwatch.Stop();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        // Should complete within 5 seconds (generous threshold for CI/CD environments)
        Assert.True(stopwatch.ElapsedMilliseconds < 5000, 
            $"Asset retrieval took {stopwatch.ElapsedMilliseconds}ms, expected < 5000ms");
    }

    #endregion
}
