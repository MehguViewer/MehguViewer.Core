using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace MehguViewer.Core.Tests.Endpoints;

/// <summary>
/// Quick smoke tests for critical API endpoints.
/// These tests verify that the basic API functionality works.
/// For detailed tests, see the specific endpoint test files.
/// </summary>
[Trait("Category", "Smoke")]
public class ApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly HttpClient _adminClient;
    private readonly TestWebApplicationFactory _factory;

    public ApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _adminClient = factory.CreateAuthenticatedClient("Admin");
    }

    [Fact]
    [Trait("Priority", "Critical")]
    public async Task Smoke_WellKnownEndpoint_IsAccessible()
    {
        var response = await _client.GetAsync("/.well-known/mehgu-node");
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("MehguViewer Core", content);
    }

    [Fact]
    [Trait("Priority", "Critical")]
    public async Task Smoke_InstanceManifest_IsAccessible()
    {
        var response = await _client.GetAsync("/api/v1/instance");
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("urn:mvn:node:example", content);
    }

    [Fact]
    [Trait("Priority", "Critical")]
    public async Task Smoke_Taxonomy_IsAccessible()
    {
        var response = await _client.GetAsync("/api/v1/taxonomy");
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Action", content);
    }

    [Fact]
    [Trait("Priority", "High")]
    public async Task Smoke_CreateSeries_Works()
    {
        // Creating series requires Admin or Uploader role
        var response = await _adminClient.PostAsJsonAsync("/api/v1/series", new
        {
            title = "Smoke Test Series",
            description = "Created during smoke testing",
            media_type = "Photo",
            reading_direction = "LTR"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    [Trait("Priority", "High")]
    public async Task Smoke_Library_RequiresAuth()
    {
        // Library endpoint requires authentication
        var response = await _client.GetAsync("/api/v1/me/library");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    [Trait("Priority", "High")]
    public async Task Smoke_AdminConfig_RequiresAuth()
    {
        // Admin config endpoint requires authentication
        var response = await _client.GetAsync("/api/v1/admin/configuration");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    [Trait("Priority", "Critical")]
    public async Task Smoke_Search_IsAccessible()
    {
        var response = await _client.GetAsync("/api/v1/search");
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    [Trait("Priority", "Critical")]
    public async Task Smoke_SetupStatus_IsAccessible()
    {
        var response = await _client.GetAsync("/api/v1/system/setup-status");
        response.EnsureSuccessStatusCode();
    }
}
