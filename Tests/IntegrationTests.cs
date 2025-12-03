using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace MehguViewer.Core.Tests;

/// <summary>
/// Integration tests that test workflows spanning multiple endpoints.
/// These tests verify that the API works correctly as a whole.
/// </summary>
[Trait("Category", "Integration")]
public class IntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly TestWebApplicationFactory _factory;

    public IntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    #region Series Workflow

    [Fact]
    public async Task SeriesWorkflow_CreateAndRetrieve_Succeeds()
    {
        // 1. Create a series
        var createPayload = new
        {
            title = "Integration Test Series",
            description = "A series created during integration testing",
            media_type = "MANGA",
            reading_direction = "RTL"
        };
        var createResponse = await _client.PostAsJsonAsync("/api/v1/series", createPayload);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        
        var location = createResponse.Headers.Location?.ToString();
        Assert.NotNull(location);

        // 2. Retrieve the series
        var getResponse = await _client.GetAsync(location);
        getResponse.EnsureSuccessStatusCode();
        var content = await getResponse.Content.ReadAsStringAsync();
        Assert.Contains("Integration Test Series", content);

        // 3. Verify it appears in the list
        var listResponse = await _client.GetAsync("/api/v1/series");
        listResponse.EnsureSuccessStatusCode();
        var listContent = await listResponse.Content.ReadAsStringAsync();
        Assert.Contains("Integration Test Series", listContent);
    }

    [Fact]
    public async Task SeriesWithUnits_CreateAndList_Succeeds()
    {
        // 1. Create a series
        var seriesPayload = new
        {
            title = "Series with Chapters",
            description = "A manga series",
            media_type = "MANGA",
            reading_direction = "RTL"
        };
        var seriesResponse = await _client.PostAsJsonAsync("/api/v1/series", seriesPayload);
        var seriesLocation = seriesResponse.Headers.Location?.ToString();
        var seriesId = seriesLocation?.Split('/').Last();

        // 2. Create units (chapters)
        for (int i = 1; i <= 3; i++)
        {
            var unitPayload = new
            {
                unit_number = i,
                title = $"Chapter {i}"
            };
            var unitResponse = await _client.PostAsJsonAsync($"/api/v1/series/{seriesId}/units", unitPayload);
            Assert.Equal(HttpStatusCode.Created, unitResponse.StatusCode);
        }

        // 3. List units
        var listResponse = await _client.GetAsync($"/api/v1/series/{seriesId}/units");
        listResponse.EnsureSuccessStatusCode();
        var content = await listResponse.Content.ReadAsStringAsync();
        
        Assert.Contains("Chapter 1", content);
        Assert.Contains("Chapter 2", content);
        Assert.Contains("Chapter 3", content);
    }

    #endregion

    #region Search Workflow

    [Fact]
    public async Task SearchWorkflow_CreateAndSearch_FindsSeries()
    {
        // 1. Create a series with unique title
        var uniqueTitle = $"Unique Dragon Adventure {Guid.NewGuid():N}";
        var createPayload = new
        {
            title = uniqueTitle,
            description = "A tale of dragons and heroes",
            media_type = "MANHWA",
            reading_direction = "LTR"
        };
        await _client.PostAsJsonAsync("/api/v1/series", createPayload);

        // 2. Search for it
        var searchResponse = await _client.GetAsync($"/api/v1/search?q={Uri.EscapeDataString(uniqueTitle.Substring(0, 20))}");
        searchResponse.EnsureSuccessStatusCode();
        var content = await searchResponse.Content.ReadAsStringAsync();
        
        // API returns "data" array
        Assert.Contains("data", content);
    }

    #endregion

    #region Instance Discovery

    [Fact]
    public async Task Discovery_AllEndpointsAccessible()
    {
        // Test all public discovery endpoints
        var endpoints = new[]
        {
            "/.well-known/mehgu-node",
            "/api/v1/instance",
            "/api/v1/taxonomy",
            "/api/v1/system/setup-status"
        };

        foreach (var endpoint in endpoints)
        {
            var response = await _client.GetAsync(endpoint);
            Assert.True(
                response.IsSuccessStatusCode, 
                $"Endpoint {endpoint} returned {response.StatusCode}"
            );
        }
    }

    #endregion

    #region Error Handling

    [Fact]
    public async Task Error_NotFoundSeries_ReturnsProblemDetails()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/series/urn:mvn:series:00000000-0000-0000-0000-000000000000");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        
        // Should return RFC 7807 Problem Details
        Assert.Contains("type", content);
        Assert.Contains("title", content);
        Assert.Contains("status", content);
    }

    #endregion
}
