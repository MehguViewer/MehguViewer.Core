using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace MehguViewer.Core.Tests;

/// <summary>
/// Tests for series-related endpoints: CRUD operations, search, units.
/// </summary>
[Trait("Category", "Unit")]
[Trait("Endpoint", "Series")]
public class SeriesEndpointTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public SeriesEndpointTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    #region Create Series

    [Fact]
    public async Task CreateSeries_ValidPayload_ReturnsCreated()
    {
        // Arrange
        var payload = new
        {
            title = "Test Series",
            description = "A test series description",
            media_type = "MANGA",
            reading_direction = "LTR"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/series", payload);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Test Series", content);
        Assert.Contains("urn:mvn:series:", content);
    }

    [Fact]
    public async Task CreateSeries_WithDescription_IncludesDescriptionInResponse()
    {
        // Arrange
        var payload = new
        {
            title = "Described Series",
            description = "This is a detailed description of the series",
            media_type = "MANHWA",
            reading_direction = "RTL"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/series", payload);
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Contains("This is a detailed description", content);
    }

    [Fact]
    public async Task CreateSeries_ReturnsLocationHeader()
    {
        // Arrange
        var payload = new
        {
            title = "Location Test Series",
            description = "Test",
            media_type = "MANGA",
            reading_direction = "LTR"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/series", payload);

        // Assert
        Assert.NotNull(response.Headers.Location);
        Assert.StartsWith("/api/v1/series/", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task CreateSeries_MissingTitle_ReturnsBadRequest()
    {
        // Arrange
        var payload = new
        {
            description = "Missing title",
            media_type = "MANGA",
            reading_direction = "LTR"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/series", payload);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateSeries_MissingMediaType_ReturnsBadRequest()
    {
        // Arrange
        var payload = new
        {
            title = "Missing Media Type",
            description = "Test"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/series", payload);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    #endregion

    #region List Series

    [Fact]
    public async Task ListSeries_ReturnsOk()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/series");

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        // API returns "data" array with pagination
        Assert.Contains("data", content);
    }

    [Fact]
    public async Task ListSeries_WithPagination_ReturnsValidStructure()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/series?limit=10");

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("data", content);
    }

    #endregion

    #region Get Series

    [Fact]
    public async Task GetSeries_NonExistent_ReturnsNotFound()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/series/urn:mvn:series:00000000-0000-0000-0000-000000000000");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetSeries_AfterCreate_ReturnsSeries()
    {
        // Arrange - Create a series first
        var createPayload = new
        {
            title = "Retrievable Series",
            description = "A series we can retrieve",
            media_type = "MANGA",
            reading_direction = "LTR"
        };
        var createResponse = await _client.PostAsJsonAsync("/api/v1/series", createPayload);
        var locationHeader = createResponse.Headers.Location?.ToString();

        // Act
        var response = await _client.GetAsync(locationHeader);

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Retrievable Series", content);
    }

    #endregion

    #region Search

    [Fact]
    public async Task Search_EmptyQuery_ReturnsOk()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/search");

        // Assert
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Search_WithQuery_ReturnsSearchResults()
    {
        // Arrange - Create a series first
        var createPayload = new
        {
            title = "Searchable Dragon Story",
            description = "A story about dragons",
            media_type = "MANGA",
            reading_direction = "LTR"
        };
        await _client.PostAsJsonAsync("/api/v1/series", createPayload);

        // Act
        var response = await _client.GetAsync("/api/v1/search?q=Dragon");

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        // API returns "data" array
        Assert.Contains("data", content);
    }

    [Fact]
    public async Task Search_WithTypeFilter_ReturnsFilteredResults()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/search?type=MANGA");

        // Assert
        response.EnsureSuccessStatusCode();
    }

    #endregion

    #region Units

    [Fact]
    public async Task CreateUnit_ForExistingSeries_ReturnsCreated()
    {
        // Arrange - Create a series first
        var seriesPayload = new
        {
            title = "Series With Units",
            description = "A series for unit tests",
            media_type = "MANGA",
            reading_direction = "LTR"
        };
        var seriesResponse = await _client.PostAsJsonAsync("/api/v1/series", seriesPayload);
        var seriesLocation = seriesResponse.Headers.Location?.ToString();
        var seriesId = seriesLocation?.Split('/').Last();

        var unitPayload = new
        {
            unit_number = 1,
            title = "Chapter 1: The Beginning"
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/v1/series/{seriesId}/units", unitPayload);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task ListUnits_ForSeries_ReturnsOk()
    {
        // Arrange - Create a series first
        var seriesPayload = new
        {
            title = "Series For Listing Units",
            description = "A series",
            media_type = "MANGA",
            reading_direction = "LTR"
        };
        var seriesResponse = await _client.PostAsJsonAsync("/api/v1/series", seriesPayload);
        var seriesLocation = seriesResponse.Headers.Location?.ToString();
        var seriesId = seriesLocation?.Split('/').Last();

        // Act
        var response = await _client.GetAsync($"/api/v1/series/{seriesId}/units");

        // Assert
        response.EnsureSuccessStatusCode();
    }

    #endregion
}
