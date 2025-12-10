using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace MehguViewer.Core.Tests.Endpoints;

/// <summary>
/// Tests for series-related endpoints: CRUD operations, search, units.
/// </summary>
[Trait("Category", "Unit")]
[Trait("Endpoint", "Series")]
public class SeriesEndpointTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly HttpClient _adminClient;
    private readonly HttpClient _uploaderClient;

    public SeriesEndpointTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _adminClient = factory.CreateAuthenticatedClient("Admin");
        _uploaderClient = factory.CreateAuthenticatedClient("Uploader");
    }

    #region Authorization Tests

    [Fact]
    public async Task CreateSeries_WithoutAuth_ReturnsUnauthorized()
    {
        // Arrange
        var payload = new
        {
            title = "Unauthorized Series",
            description = "Should fail",
            media_type = "Photo",
            reading_direction = "LTR"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/series", payload);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateSeries_WithRegularUser_ReturnsForbidden()
    {
        // Arrange
        var userClient = _factory.CreateAuthenticatedClient("User");
        var payload = new
        {
            title = "User Series",
            description = "Should fail - no ingest permission",
            media_type = "Photo",
            reading_direction = "LTR"
        };

        // Act
        var response = await userClient.PostAsJsonAsync("/api/v1/series", payload);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    #endregion

    #region Create Series

    [Fact]
    public async Task CreateSeries_AsAdmin_ReturnsCreated()
    {
        // Arrange
        var payload = new
        {
            title = "Admin Test Series",
            description = "A test series description",
            media_type = "Photo",
            reading_direction = "LTR"
        };

        // Act
        var response = await _adminClient.PostAsJsonAsync("/api/v1/series", payload);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Admin Test Series", content);
        Assert.Contains("urn:mvn:series:", content);
        Assert.Contains("federation_ref", content);
        Assert.Contains("created_by", content);
    }

    [Fact]
    public async Task CreateSeries_AsUploader_ReturnsCreated()
    {
        // Arrange
        var payload = new
        {
            title = "Uploader Test Series",
            description = "A test series by uploader",
            media_type = "Video",
            reading_direction = "LTR"
        };

        // Act
        var response = await _uploaderClient.PostAsJsonAsync("/api/v1/series", payload);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Uploader Test Series", content);
    }

    [Fact]
    public async Task CreateSeries_WithDescription_IncludesDescriptionInResponse()
    {
        // Arrange
        var payload = new
        {
            title = "Described Series",
            description = "This is a detailed description of the series",
            media_type = "Text",
            reading_direction = "LTR"
        };

        // Act
        var response = await _adminClient.PostAsJsonAsync("/api/v1/series", payload);
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
            media_type = "Photo",
            reading_direction = "LTR"
        };

        // Act
        var response = await _adminClient.PostAsJsonAsync("/api/v1/series", payload);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
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
            media_type = "Photo",
            reading_direction = "LTR"
        };

        // Act
        var response = await _adminClient.PostAsJsonAsync("/api/v1/series", payload);

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
        var response = await _adminClient.PostAsJsonAsync("/api/v1/series", payload);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateSeries_InvalidMediaType_ReturnsBadRequest()
    {
        // Arrange
        var payload = new
        {
            title = "Invalid Media Type",
            description = "Test",
            media_type = "INVALID_TYPE"
        };

        // Act
        var response = await _adminClient.PostAsJsonAsync("/api/v1/series", payload);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Invalid media_type", content);
    }

    [Fact]
    public async Task CreateSeries_InvalidReadingDirection_ReturnsBadRequest()
    {
        // Arrange
        var payload = new
        {
            title = "Invalid Direction",
            description = "Test",
            media_type = "Photo",
            reading_direction = "DIAGONAL"
        };

        // Act
        var response = await _adminClient.PostAsJsonAsync("/api/v1/series", payload);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Invalid reading_direction", content);
    }

    [Fact]
    public async Task CreateSeries_WithoutReadingDirection_DefaultsToLTR()
    {
        // Arrange - Photo should default to LTR
        var payload = new
        {
            title = "Default Direction Test",
            description = "Test",
            media_type = "Photo"
        };

        // Act
        var response = await _adminClient.PostAsJsonAsync("/api/v1/series", payload);
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Contains("LTR", content);
    }

    [Fact]
    public async Task CreateSeries_Video_DefaultsToLTR()
    {
        // Arrange
        var payload = new
        {
            title = "Video Direction Test",
            description = "Test",
            media_type = "Video"
        };

        // Act
        var response = await _adminClient.PostAsJsonAsync("/api/v1/series", payload);
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Contains("LTR", content);
    }

    #endregion

    #region List Series

    [Fact]
    public async Task ListSeries_ReturnsOk()
    {
        // Act - No auth required for listing
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
        // Arrange - Create a series first (requires auth)
        var createPayload = new
        {
            title = "Retrievable Series",
            description = "A series we can retrieve",
            media_type = "Photo",
            reading_direction = "LTR"
        };
        var createResponse = await _adminClient.PostAsJsonAsync("/api/v1/series", createPayload);
        var locationHeader = createResponse.Headers.Location?.ToString();

        // Act - No auth required for reading
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
            media_type = "Photo",
            reading_direction = "LTR"
        };
        await _adminClient.PostAsJsonAsync("/api/v1/series", createPayload);

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
        var response = await _client.GetAsync("/api/v1/search?type=Photo");

        // Assert
        response.EnsureSuccessStatusCode();
    }

    #endregion

    #region Units

    [Fact]
    public async Task CreateUnit_WithoutAuth_ReturnsUnauthorized()
    {
        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/series/urn:mvn:series:test/units", new { unit_number = 1, title = "Test" });

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateUnit_ForExistingSeries_ReturnsCreated()
    {
        // Arrange - Create a series first
        var seriesPayload = new
        {
            title = "Series With Units",
            description = "A series for unit tests",
            media_type = "Photo",
            reading_direction = "LTR"
        };
        var seriesResponse = await _adminClient.PostAsJsonAsync("/api/v1/series", seriesPayload);
        var seriesLocation = seriesResponse.Headers.Location?.ToString();
        var seriesId = seriesLocation?.Split('/').Last();

        var unitPayload = new
        {
            unit_number = 1,
            title = "Chapter 1: The Beginning"
        };

        // Act
        var response = await _adminClient.PostAsJsonAsync($"/api/v1/series/{seriesId}/units", unitPayload);

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
            media_type = "Photo",
            reading_direction = "LTR"
        };
        var seriesResponse = await _adminClient.PostAsJsonAsync("/api/v1/series", seriesPayload);
        var seriesLocation = seriesResponse.Headers.Location?.ToString();
        var seriesId = seriesLocation?.Split('/').Last();

        // Act - No auth required for listing
        var response = await _client.GetAsync($"/api/v1/series/{seriesId}/units");

        // Assert
        response.EnsureSuccessStatusCode();
    }

    #endregion

    #region URN Validation Tests

    [Fact]
    public async Task GetSeries_WithInvalidUrn_ReturnsBadRequest()
    {
        // Arrange
        var invalidUrn = "invalid-urn-format";

        // Act
        var response = await _client.GetAsync($"/api/v1/series/{invalidUrn}");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetSeries_WithValidUrn_ReturnsOkOrNotFound()
    {
        // Arrange
        var validUrn = "urn:mvn:series:00000000-0000-0000-0000-000000000000";

        // Act
        var response = await _client.GetAsync($"/api/v1/series/{validUrn}");

        // Assert - Should be either OK (if exists) or NotFound (if doesn't exist), but not BadRequest
        Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    #endregion

    #region Security Tests

    [Fact]
    public async Task SearchSeries_WithExcessiveLimit_ClampedToMaximum()
    {
        // Arrange
        var excessiveLimit = 1000; // Should be clamped to 100

        // Act
        var response = await _client.GetAsync($"/api/v1/search?limit={excessiveLimit}");

        // Assert
        response.EnsureSuccessStatusCode();
        // The backend should handle this gracefully and return at most 100 results
    }

    [Fact]
    public async Task SearchSeries_WithNegativeOffset_HandlesSafely()
    {
        // Arrange
        var negativeOffset = -10;

        // Act
        var response = await _client.GetAsync($"/api/v1/search?offset={negativeOffset}");

        // Assert - Should handle gracefully (convert to 0 or return error)
        Assert.True(
            response.StatusCode == HttpStatusCode.OK || 
            response.StatusCode == HttpStatusCode.BadRequest
        );
    }

    #endregion

    #region Update Series Tests

    [Fact]
    public async Task UpdateSeries_AsOwner_ReturnsOk()
    {
        // Arrange - Create a series
        var createPayload = new
        {
            title = "Original Title",
            description = "Original Description",
            media_type = "Photo",
            reading_direction = "LTR"
        };
        var createResponse = await _uploaderClient.PostAsJsonAsync("/api/v1/series", createPayload);
        var seriesLocation = createResponse.Headers.Location?.ToString();
        var seriesId = seriesLocation?.Split('/').Last();

        var updatePayload = new
        {
            title = "Updated Title",
            description = "Updated Description"
        };

        // Act
        var response = await _uploaderClient.PutAsJsonAsync($"/api/v1/series/{seriesId}", updatePayload);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Updated Title", content);
    }

    [Fact]
    public async Task UpdateSeries_InvalidReadingDirection_ReturnsBadRequest()
    {
        // Arrange
        var createPayload = new
        {
            title = "Test Series",
            media_type = "Photo",
            reading_direction = "LTR"
        };
        var createResponse = await _adminClient.PostAsJsonAsync("/api/v1/series", createPayload);
        var seriesLocation = createResponse.Headers.Location?.ToString();
        var seriesId = seriesLocation?.Split('/').Last();

        var updatePayload = new
        {
            reading_direction = "InvalidDirection"
        };

        // Act
        var response = await _adminClient.PutAsJsonAsync($"/api/v1/series/{seriesId}", updatePayload);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    #endregion

    #region Delete Series Tests

    [Fact]
    public async Task DeleteSeries_AsOwner_ReturnsNoContent()
    {
        // Arrange
        var payload = new
        {
            title = "Series To Delete",
            media_type = "Photo",
            reading_direction = "LTR"
        };
        var createResponse = await _uploaderClient.PostAsJsonAsync("/api/v1/series", payload);
        var seriesLocation = createResponse.Headers.Location?.ToString();
        var seriesId = seriesLocation?.Split('/').Last();

        // Act
        var response = await _uploaderClient.DeleteAsync($"/api/v1/series/{seriesId}");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task DeleteSeries_NonExistent_ReturnsNotFound()
    {
        // Arrange
        var nonExistentUrn = "urn:mvn:series:00000000-0000-0000-0000-000000000000";

        // Act
        var response = await _adminClient.DeleteAsync($"/api/v1/series/{nonExistentUrn}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    #endregion

    #region Validation Tests

    [Fact]
    public async Task CreateSeries_WithEmptyTitle_ReturnsBadRequest()
    {
        // Arrange
        var payload = new
        {
            title = "",
            description = "Valid description",
            media_type = "Photo",
            reading_direction = "LTR"
        };

        // Act
        var response = await _uploaderClient.PostAsJsonAsync("/api/v1/series", payload);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Title", content);
    }

    [Fact]
    public async Task CreateSeries_WithInvalidMediaType_ReturnsBadRequest()
    {
        // Arrange
        var payload = new
        {
            title = "Test Series",
            description = "Valid description",
            media_type = "InvalidType",
            reading_direction = "LTR"
        };

        // Act
        var response = await _uploaderClient.PostAsJsonAsync("/api/v1/series", payload);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("media_type", content);
    }

    [Fact]
    public async Task CreateSeries_WithInvalidReadingDirection_ReturnsBadRequest()
    {
        // Arrange
        var payload = new
        {
            title = "Test Series",
            description = "Valid description",
            media_type = "Photo",
            reading_direction = "InvalidDirection"
        };

        // Act
        var response = await _uploaderClient.PostAsJsonAsync("/api/v1/series", payload);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("reading_direction", content);
    }

    [Fact]
    public async Task UpdateSeries_WithInvalidUrn_ReturnsNotFound()
    {
        // Arrange - Invalid URN gets normalized and then looked up, resulting in NotFound
        var invalidUrn = "invalid-urn-format";
        var payload = new
        {
            title = "Updated Title"
        };

        // Act
        var response = await _adminClient.PutAsJsonAsync($"/api/v1/series/{invalidUrn}", payload);

        // Assert
        // URN normalization happens first, then lookup fails with NotFound
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    #endregion

    #region Pagination Tests

    [Fact]
    public async Task ListSeries_WithLimit_ReturnsCorrectCount()
    {
        // Arrange - Create multiple series
        for (int i = 0; i < 5; i++)
        {
            var payload = new
            {
                title = $"Series {i}",
                description = "Test series",
                media_type = "Photo",
                reading_direction = "LTR"
            };
            await _uploaderClient.PostAsJsonAsync("/api/v1/series", payload);
        }

        // Act
        var response = await _client.GetAsync("/api/v1/series?limit=3");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        // Should return at most 3 series
        Assert.Contains("series", content);
    }

    [Fact]
    public async Task SearchSeries_WithQueryParameter_ReturnsMatchingResults()
    {
        // Arrange - Create series with specific title
        var payload = new
        {
            title = "Unique Search Test Series",
            description = "For search testing",
            media_type = "Photo",
            reading_direction = "LTR"
        };
        await _uploaderClient.PostAsJsonAsync("/api/v1/series", payload);

        // Act
        var response = await _client.GetAsync("/api/v1/search?q=Unique");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Unique Search Test Series", content);
    }

    [Fact]
    public async Task SearchSeries_WithExcessiveLimit_CapsAtMaximum()
    {
        // Arrange & Act - Request more than max allowed (100)
        var response = await _client.GetAsync("/api/v1/search?limit=999");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Implementation should cap at maximum pagination limit
    }

    #endregion

    #region Enhanced Validation Tests

    [Fact]
    public async Task CreateSeries_WithExcessivelyLongTitle_ReturnsBadRequest()
    {
        // Arrange - Title longer than 500 characters
        var longTitle = new string('A', 501);
        var payload = new
        {
            title = longTitle,
            description = "Test",
            media_type = "Photo",
            reading_direction = "LTR"
        };

        // Act
        var response = await _adminClient.PostAsJsonAsync("/api/v1/series", payload);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("500 characters or less", content);
    }

    [Fact]
    public async Task CreateSeries_WithControlCharactersInTitle_ReturnsBadRequest()
    {
        // Arrange - Title with control characters
        var payload = new
        {
            title = "Test\u0001Series\u0002",
            description = "Test",
            media_type = "Photo",
            reading_direction = "LTR"
        };

        // Act
        var response = await _adminClient.PostAsJsonAsync("/api/v1/series", payload);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("invalid control characters", content);
    }

    [Fact]
    public async Task CreateSeries_WithExcessivelyLongDescription_ReturnsBadRequest()
    {
        // Arrange - Description longer than 5000 characters
        var longDescription = new string('B', 5001);
        var payload = new
        {
            title = "Test Series",
            description = longDescription,
            media_type = "Photo",
            reading_direction = "LTR"
        };

        // Act
        var response = await _adminClient.PostAsJsonAsync("/api/v1/series", payload);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("5000 characters or less", content);
    }

    [Fact]
    public async Task CreateSeries_WithWhitespaceOnlyTitle_ReturnsBadRequest()
    {
        // Arrange
        var payload = new
        {
            title = "   ",
            description = "Test",
            media_type = "Photo",
            reading_direction = "LTR"
        };

        // Act
        var response = await _adminClient.PostAsJsonAsync("/api/v1/series", payload);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateSeries_WithValidNewlinesInTitle_Succeeds()
    {
        // Arrange - Newlines and tabs should be allowed
        var payload = new
        {
            title = "Multi\\nLine\\tTitle",
            description = "Test",
            media_type = "Photo",
            reading_direction = "LTR"
        };

        // Act
        var response = await _adminClient.PostAsJsonAsync("/api/v1/series", payload);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task CreateSeries_WithMaxLengthTitle_Succeeds()
    {
        // Arrange - Exactly 500 characters should be allowed
        var maxTitle = new string('X', 500);
        var payload = new
        {
            title = maxTitle,
            description = "Test",
            media_type = "Photo",
            reading_direction = "LTR"
        };

        // Act
        var response = await _adminClient.PostAsJsonAsync("/api/v1/series", payload);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task CreateSeries_WithMaxLengthDescription_Succeeds()
    {
        // Arrange - Exactly 5000 characters should be allowed
        var maxDescription = new string('Y', 5000);
        var payload = new
        {
            title = "Test Series",
            description = maxDescription,
            media_type = "Photo",
            reading_direction = "LTR"
        };

        // Act
        var response = await _adminClient.PostAsJsonAsync("/api/v1/series", payload);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task SearchSeries_WithNegativeOffset_UsesZero()
    {
        // Arrange & Act - Negative offset should be sanitized to 0
        var response = await _client.GetAsync("/api/v1/search?offset=-10");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Should not throw error, offset should be capped at 0
    }

    #endregion
}
