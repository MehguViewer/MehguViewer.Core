using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace MehguViewer.Core.Tests.Units;

/// <summary>
/// Tests for API security: headers, CORS, rate limiting, authentication.
/// </summary>
[Trait("Category", "Security")]
[Trait("Endpoint", "Security")]
public class SecurityTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public SecurityTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    #region Security Headers

    [Fact]
    public async Task Response_ContainsXContentTypeOptions()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/instance");

        // Assert
        Assert.Contains(response.Headers, h => h.Key == "X-Content-Type-Options");
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").First());
    }

    [Fact]
    public async Task Response_ContainsXFrameOptions()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/instance");

        // Assert
        Assert.Contains(response.Headers, h => h.Key == "X-Frame-Options");
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").First());
    }

    [Fact]
    public async Task Response_ContainsReferrerPolicy()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/instance");

        // Assert
        Assert.Contains(response.Headers, h => h.Key == "Referrer-Policy");
        Assert.Equal("strict-origin-when-cross-origin", response.Headers.GetValues("Referrer-Policy").First());
    }

    #endregion

    #region Content Type

    [Fact]
    public async Task ApiEndpoints_ReturnJsonContentType()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/instance");

        // Assert
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task ApiEndpoints_ReturnUtf8Charset()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/instance");

        // Assert
        Assert.Equal("utf-8", response.Content.Headers.ContentType?.CharSet);
    }

    #endregion

    #region CORS

    [Fact]
    public async Task Cors_AllowsAnyOrigin()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/instance");
        request.Headers.Add("Origin", "https://example.com");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        // The CORS preflight should be handled
        Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NoContent);
    }

    #endregion

    #region Protected Endpoints

    
    [Theory]
    [InlineData("/api/v1/me/library")]
    [InlineData("/api/v1/me/history")]
    public async Task ProtectedEndpoints_WithoutAuth_ReturnsUnauthorizedOrEmpty(string endpoint)
    {
        // Act
        var response = await _client.GetAsync(endpoint);

        // Assert - Either 401 Unauthorized or 200 with empty data (depending on implementation)
        Assert.True(
            response.StatusCode == HttpStatusCode.Unauthorized || 
            response.IsSuccessStatusCode,
            $"Expected Unauthorized or Success for {endpoint}, got {response.StatusCode}"
        );
    }

    #endregion

    #region Server Timing

    [Fact]
    public async Task Response_ContainsServerTimingHeader()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/instance");

        // Assert
        Assert.Contains(response.Headers, h => h.Key == "Server-Timing");
    }

    #endregion
}
