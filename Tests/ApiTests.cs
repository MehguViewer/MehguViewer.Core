using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace MehguViewer.Core.Tests;

public class ApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Get_WellKnownNode_ReturnsOk()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/.well-known/mehgu-node");

        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("MehguViewer Core", content);
    }

    [Fact]
    public async Task Get_InstanceManifest_ReturnsOk()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/instance");

        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("urn:mvn:node:example", content);
    }

    [Fact]
    public async Task Get_Taxonomy_ReturnsOk()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/taxonomy");

        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Action", content);
    }

    /*
    [Fact]
    public async Task Get_Search_ReturnsOk()
    {
        var client = _factory.CreateClient();
        // Try without parameters first to ensure route exists
        var response = await client.GetAsync("/api/v1/search");

        response.EnsureSuccessStatusCode();
    }
    */

    [Fact]
    public async Task Post_Series_ReturnsCreated()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/series", new
        {
            title = "Test Series",
            description = "Test Description",
            media_type = "MANGA",
            reading_direction = "LTR"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Test Series", content);
    }

    [Fact]
    public async Task Get_Library_ReturnsOk()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/me/library");

        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        Assert.Equal("[]", content);
    }

    [Fact]
    public async Task Get_AdminConfig_ReturnsOk()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/admin/configuration");

        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Welcome to MehguViewer", content);
    }
}
