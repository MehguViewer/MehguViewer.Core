using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace MehguViewer.Core.Tests;

/// <summary>
/// Tests for user-related endpoints: library, history, progress.
/// </summary>
[Trait("Category", "Unit")]
[Trait("Endpoint", "User")]
public class UserEndpointTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public UserEndpointTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    #region Library

    [Fact]
    public async Task Library_WithoutAuth_ReturnsUnauthorized()
    {
        // Without authentication, library should return 401
        var response = await _client.GetAsync("/api/v1/me/library");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    #endregion

    #region History

    [Fact]
    public async Task History_WithoutAuth_ReturnsUnauthorized()
    {
        // Without authentication, history should return 401
        var response = await _client.GetAsync("/api/v1/me/history");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    #endregion

    #region Admin Configuration

    [Fact]
    public async Task AdminConfig_WithoutAuth_ReturnsUnauthorized()
    {
        // Admin config endpoint requires authentication
        var response = await _client.GetAsync("/api/v1/admin/configuration");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    #endregion
}
