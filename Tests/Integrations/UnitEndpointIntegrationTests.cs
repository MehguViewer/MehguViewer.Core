using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using MehguViewer.Core.Shared;

namespace MehguViewer.Core.Tests.Integrations;

/// <summary>
/// Integration tests for Unit endpoints with metadata inheritance and permission management.
/// </summary>
public class UnitEndpointIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly TestWebApplicationFactory _factory;

    public UnitEndpointIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [Fact]
    public async Task CreateUnit_InheritsMetadataFromSeries()
    {
        // Arrange - Create a series with metadata
        var seriesCreate = new SeriesCreate(
            title: "Test Manga",
            media_type: "Photo",
            description: "Test description"
        );
        
        var token = _factory.CreateTestUserAndGetToken("uploader1", "Uploader");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        
        var seriesResponse = await _client.PostAsJsonAsync("/api/v1/series", seriesCreate);
        seriesResponse.EnsureSuccessStatusCode();
        var series = await seriesResponse.Content.ReadFromJsonAsync<Series>();
        Assert.NotNull(series);
        
        // Update series with metadata
        var seriesUpdate = new SeriesUpdate(
            title: null,
            description: null,
            poster: null,
            media_type: null,
            external_links: null,
            reading_direction: null,
            tags: new[] { "Action", "Fantasy" },
            content_warnings: new[] { "violence" },
            authors: new[] { new Author("author-1", "Test Author", "Author") },
            scanlators: null,
            groups: null,
            alt_titles: null,
            status: null,
            year: null,
            localized: null
        );
        
        await _client.PatchAsJsonAsync($"/api/v1/series/{series.id}", seriesUpdate);

        // Act - Create a unit without explicit metadata
        var unitCreate = new UnitCreate(
            unit_number: 1,
            title: "Chapter 1",
            language: "en"
        );
        
        var unitResponse = await _client.PostAsJsonAsync($"/api/v1/series/{series.id}/units", unitCreate);
        
        // Assert
        unitResponse.EnsureSuccessStatusCode();
        var unit = await unitResponse.Content.ReadFromJsonAsync<Unit>();
        Assert.NotNull(unit);
        Assert.Equal(1, unit.unit_number);
        Assert.Equal("Chapter 1", unit.title);
        
        // Verify metadata inheritance
        Assert.NotNull(unit.tags);
        Assert.Contains("Action", unit.tags);
        Assert.Contains("Fantasy", unit.tags);
        Assert.NotNull(unit.content_warnings);
        Assert.Contains("violence", unit.content_warnings);
        Assert.NotNull(unit.authors);
        Assert.Contains(unit.authors, a => a.id == "author-1");
    }

    [Fact]
    public async Task UpdateUnit_TriggersSeriesMetadataAggregation()
    {
        // Arrange - Create series and unit
        var token = _factory.CreateTestUserAndGetToken("uploader1", "Uploader");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        
        var seriesCreate = new SeriesCreate(title: "Test Manga", media_type: "Photo");
        var seriesResponse = await _client.PostAsJsonAsync("/api/v1/series", seriesCreate);
        var series = await seriesResponse.Content.ReadFromJsonAsync<Series>();
        Assert.NotNull(series);
        
        var unitCreate = new UnitCreate(unit_number: 1, title: "Chapter 1");
        var unitResponse = await _client.PostAsJsonAsync($"/api/v1/series/{series.id}/units", unitCreate);
        var unit = await unitResponse.Content.ReadFromJsonAsync<Unit>();
        Assert.NotNull(unit);

        // Act - Update unit with new tags
        var unitUpdate = new UnitUpdate(
            unit_number: null,
            title: null,
            language: null,
            description: null,
            tags: new[] { "NewTag1", "NewTag2" },
            content_warnings: null,
            authors: null,
            localized: null
        );
        
        await _client.PatchAsJsonAsync($"/api/v1/series/{series.id}/units/{unit.id}", unitUpdate);

        // Assert - Check that series now has the new tags
        var updatedSeriesResponse = await _client.GetAsync($"/api/v1/series/{series.id}");
        var updatedSeries = await updatedSeriesResponse.Content.ReadFromJsonAsync<Series>();
        Assert.NotNull(updatedSeries);
        Assert.Contains("NewTag1", updatedSeries.tags);
        Assert.Contains("NewTag2", updatedSeries.tags);
    }

    [Fact]
    public async Task UpdateUnit_AggregatesMultipleScanlatorGroups_InLocalizedMetadata()
    {
        // Arrange - Create series
        var token = _factory.CreateTestUserAndGetToken("uploader1", "Uploader");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        
        var seriesCreate = new SeriesCreate(title: "Test Manga", media_type: "Photo");
        var seriesResponse = await _client.PostAsJsonAsync("/api/v1/series", seriesCreate);
        var series = await seriesResponse.Content.ReadFromJsonAsync<Series>();
        Assert.NotNull(series);

        // Create Chapter 1 and 2 with GroupA
        var unit1Create = new UnitCreate(
            unit_number: 1,
            title: "Chapter 1",
            language: "zh",
            localized: new Dictionary<string, UnitLocalizedMetadata>
            {
                ["en"] = new UnitLocalizedMetadata(
                    title: null,
                    scanlators: new[] { new Scanlator("group-c", "Group C", ScanlatorRole.Both) }
                )
            }
        );
        await _client.PostAsJsonAsync($"/api/v1/series/{series.id}/units", unit1Create);
        
        var unit2Create = new UnitCreate(
            unit_number: 2,
            title: "Chapter 2",
            language: "zh",
            localized: new Dictionary<string, UnitLocalizedMetadata>
            {
                ["zh"] = new UnitLocalizedMetadata(
                    title: null,
                    scanlators: new[] { new Scanlator("group-a", "Group A", ScanlatorRole.Both) }
                )
            }
        );
        await _client.PostAsJsonAsync($"/api/v1/series/{series.id}/units", unit2Create);

        // Act - Create Chapter 3 with GroupB
        var unit3Create = new UnitCreate(
            unit_number: 3,
            title: "Chapter 3",
            language: "zh",
            localized: new Dictionary<string, UnitLocalizedMetadata>
            {
                ["zh"] = new UnitLocalizedMetadata(
                    title: null,
                    scanlators: new[] { new Scanlator("group-b", "Group B", ScanlatorRole.Both) }
                )
            }
        );
        await _client.PostAsJsonAsync($"/api/v1/series/{series.id}/units", unit3Create);

        // Assert - Series should show both Group A and Group B for ZH
        var updatedSeriesResponse = await _client.GetAsync($"/api/v1/series/{series.id}");
        var updatedSeries = await updatedSeriesResponse.Content.ReadFromJsonAsync<Series>();
        Assert.NotNull(updatedSeries);
        Assert.NotNull(updatedSeries.localized);
        Assert.True(updatedSeries.localized.ContainsKey("zh"));
        
        var zhMeta = updatedSeries.localized["zh"];
        Assert.NotNull(zhMeta.scanlators);
        Assert.Equal(2, zhMeta.scanlators.Length);
        Assert.Contains(zhMeta.scanlators, s => s.id == "group-a");
        Assert.Contains(zhMeta.scanlators, s => s.id == "group-b");
    }

    [Fact]
    public async Task GrantEditPermission_AllowsOtherUploaderToEdit()
    {
        // Arrange - Create uploader2 first to get their URN
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var uploader2Token = _factory.CreateTestUserAndGetToken($"uploader2_{uniqueId}", "Uploader");
        var uploader2User = _factory.Repository.GetUserByUsername($"uploader2_{uniqueId}");
        Assert.NotNull(uploader2User);
        
        // Create series as uploader1
        var uploader1Token = _factory.CreateTestUserAndGetToken($"uploader1_{uniqueId}", "Uploader");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", uploader1Token);
        
        var seriesCreate = new SeriesCreate(title: "Test Manga", media_type: "Photo");
        var seriesResponse = await _client.PostAsJsonAsync("/api/v1/series", seriesCreate);
        var series = await seriesResponse.Content.ReadFromJsonAsync<Series>();
        Assert.NotNull(series);

        // Grant permission to uploader2 using their actual URN
        var grantRequest = new GrantEditPermissionRequest(uploader2User.id);
        await _client.PostAsJsonAsync($"/api/v1/series/{series.id}/permissions", grantRequest);

        // Act - Try to edit as uploader2
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", uploader2Token);
        
        var seriesUpdate = new SeriesUpdate(
            title: "Updated by Uploader2",
            description: null,
            poster: null,
            media_type: null,
            external_links: null,
            reading_direction: null
        );
        
        var updateResponse = await _client.PatchAsJsonAsync($"/api/v1/series/{series.id}", seriesUpdate);

        // Assert - Should succeed
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updatedSeries = await updateResponse.Content.ReadFromJsonAsync<Series>();
        Assert.NotNull(updatedSeries);
        Assert.Equal("Updated by Uploader2", updatedSeries.title);
    }

    [Fact]
    public async Task GetUnit_ReturnsCanEditFlag_ForAuthorizedUser()
    {
        // Arrange - Create series and unit
        var token = _factory.CreateTestUserAndGetToken("uploader1", "Uploader");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        
        var seriesCreate = new SeriesCreate(title: "Test Manga", media_type: "Photo");
        var seriesResponse = await _client.PostAsJsonAsync("/api/v1/series", seriesCreate);
        var series = await seriesResponse.Content.ReadFromJsonAsync<Series>();
        Assert.NotNull(series);
        
        var unitCreate = new UnitCreate(unit_number: 1, title: "Chapter 1");
        var unitResponse = await _client.PostAsJsonAsync($"/api/v1/series/{series.id}/units", unitCreate);
        var unit = await unitResponse.Content.ReadFromJsonAsync<Unit>();
        Assert.NotNull(unit);

        // Act - Get unit details
        var getResponse = await _client.GetAsync($"/api/v1/series/{series.id}/units/{unit.id}");

        // Assert
        getResponse.EnsureSuccessStatusCode();
        var content = await getResponse.Content.ReadAsStringAsync();
        Assert.Contains("can_edit", content);
        Assert.Contains("true", content);  // Should be true for creator
    }

    [Fact]
    public async Task UploaderCannotEdit_SeriesCreatedByOtherUploader_WithoutPermission()
    {
        // Arrange - Create series as uploader1
        var uploader1Token = _factory.CreateTestUserAndGetToken("uploader1", "Uploader");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", uploader1Token);
        
        var seriesCreate = new SeriesCreate(title: "Test Manga", media_type: "Photo");
        var seriesResponse = await _client.PostAsJsonAsync("/api/v1/series", seriesCreate);
        var series = await seriesResponse.Content.ReadFromJsonAsync<Series>();
        Assert.NotNull(series);

        // Act - Try to edit as uploader2 (without permission)
        var uploader2Token = _factory.CreateTestUserAndGetToken("uploader2", "Uploader");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", uploader2Token);
        
        var seriesUpdate = new SeriesUpdate(
            title: "Unauthorized Update",
            description: null,
            poster: null,
            media_type: null,
            external_links: null,
            reading_direction: null
        );
        
        var updateResponse = await _client.PatchAsJsonAsync($"/api/v1/series/{series.id}", seriesUpdate);

        // Assert - Should be forbidden
        Assert.Equal(HttpStatusCode.Forbidden, updateResponse.StatusCode);
    }
}
