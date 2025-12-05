using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace MehguViewer.Core.Tests;

/// <summary>
/// Tests for system-related endpoints: well-known, instance, taxonomy, etc.
/// </summary>
[Trait("Category", "Unit")]
[Trait("Endpoint", "System")]
public class SystemEndpointTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public SystemEndpointTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    #region Well-Known Endpoints

    [Fact]
    public async Task WellKnown_MehguNode_ReturnsNodeMetadata()
    {
        // Act
        var response = await _client.GetAsync("/.well-known/mehgu-node");

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("MehguViewer Core", content);
    }

    [Fact]
    public async Task WellKnown_MehguNode_ReturnsCorrectContentType()
    {
        // Act
        var response = await _client.GetAsync("/.well-known/mehgu-node");

        // Assert
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    #endregion

    #region Instance Endpoint

    [Fact]
    public async Task Instance_Get_ReturnsManifest()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/instance");

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        
        Assert.Contains("urn:mvn:node:example", content);
        Assert.Contains("MehguViewer Core", content);
    }

    [Fact]
    public async Task Instance_Get_ContainsRequiredFields()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/instance");
        var content = await response.Content.ReadAsStringAsync();

        // Assert - Verify all required manifest fields
        // API uses "urn" instead of "id"
        Assert.Contains("\"urn\"", content);
        Assert.Contains("\"name\"", content);
        Assert.Contains("\"version\"", content);
    }

    #endregion

    #region Taxonomy Endpoint

    [Fact]
    public async Task Taxonomy_Get_ReturnsGenres()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/taxonomy");

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        
        Assert.Contains("Action", content);
        Assert.Contains("Adventure", content);
        Assert.Contains("Comedy", content);
    }

    [Fact]
    public async Task Taxonomy_Get_ReturnsContentWarnings()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/taxonomy");
        var content = await response.Content.ReadAsStringAsync();

        // Assert - Fixed content warnings (lowercase): nsfw, gore
        Assert.Contains("nsfw", content);
        Assert.Contains("gore", content);
    }

    [Fact]
    public async Task Taxonomy_Get_ReturnsMediaTypes()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/taxonomy");
        var content = await response.Content.ReadAsStringAsync();

        // Assert - Fixed media types: Photo, Text, Video
        Assert.Contains("Photo", content);
        Assert.Contains("Text", content);
        Assert.Contains("Video", content);
    }

    #endregion

    #region Setup Status

    [Fact]
    public async Task SetupStatus_Get_ReturnsStatus()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/system/setup-status");

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("is_setup_complete", content);
    }

    #endregion
}
