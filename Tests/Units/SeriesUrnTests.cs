using System.Net;
using System.Net.Http.Json;
using MehguViewer.Core.Shared;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace MehguViewer.Core.Tests.Units;

[Trait("Category", "Unit")]
[Trait("Endpoint", "Series")]
public class SeriesUrnTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly HttpClient _adminClient;

    public SeriesUrnTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _adminClient = factory.CreateAuthenticatedClient("Admin");
    }

    [Fact]
    public async Task GetSeries_WithInvalidUrn_ReturnsBadRequest()
    {
        // Arrange
        var invalidUrn = "invalid-urn";

        // Act
        var response = await _client.GetAsync($"/api/v1/series/{invalidUrn}");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<Problem>();
        Assert.NotNull(problem);
        Assert.Equal("urn:mvn:error:bad-request", problem.type);
        Assert.Contains("Invalid Series URN", problem.detail);
    }

    [Fact]
    public async Task GetSeries_WithValidUrn_ReturnsOk()
    {
        // Arrange: Create a series first
        var payload = new
        {
            title = "Urn Test Series",
            media_type = "Photo",
            reading_direction = "LTR"
        };
        var createResponse = await _adminClient.PostAsJsonAsync("/api/v1/series", payload);
        createResponse.EnsureSuccessStatusCode();
        var series = await createResponse.Content.ReadFromJsonAsync<Series>();
        Assert.NotNull(series);
        Assert.StartsWith("urn:mvn:series:", series.id);

        // Act
        var response = await _client.GetAsync($"/api/v1/series/{series.id}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var fetchedSeries = await response.Content.ReadFromJsonAsync<Series>();
        Assert.NotNull(fetchedSeries);
        Assert.Equal(series.id, fetchedSeries.id);
    }

    [Fact]
    public async Task DeleteSeries_WithInvalidUrn_ReturnsBadRequest()
    {
        // Arrange
        var invalidUrn = "invalid-urn";

        // Act
        var response = await _adminClient.DeleteAsync($"/api/v1/series/{invalidUrn}");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
