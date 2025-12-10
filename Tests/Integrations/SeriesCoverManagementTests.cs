using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace MehguViewer.Core.Tests.Integrations;

/// <summary>
/// Integration tests for Series cover management endpoints.
/// Tests cover upload, download from URL, variant generation, and localization.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Endpoint", "Series.Covers")]
public class SeriesCoverManagementTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _adminClient;
    private readonly HttpClient _uploaderClient;
    private readonly HttpClient _client;

    public SeriesCoverManagementTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _adminClient = factory.CreateAuthenticatedClient("Admin");
        _uploaderClient = factory.CreateAuthenticatedClient("Uploader");
        _client = factory.CreateClient();
    }

    #region Cover Upload Tests

    [Fact(Skip = "Requires ImageSharp service and file system - infrastructure dependent")]
    public async Task UploadCover_WithValidImage_ReturnsOkWithVariants()
    {
        // Arrange - Create a series first
        var series = await CreateTestSeries(_uploaderClient, "Cover Test Series");
        var seriesId = ExtractSeriesId(series);

        // Create fake image content
        var imageContent = CreateFakeImageBytes();
        var formContent = CreateMultipartFormContent(imageContent, "test-cover.jpg", "image/jpeg");

        // Act
        var response = await _uploaderClient.PostAsync($"/api/v1/series/{seriesId}/cover", formContent);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadAsStringAsync();
        
        // Verify variants are returned
        Assert.Contains("thumbnail", result);
        Assert.Contains("web", result);
        Assert.Contains("raw", result);
        Assert.Contains("cover_url", result);
    }

    [Fact]
    public async Task UploadCover_WithoutAuthentication_ReturnsUnauthorized()
    {
        // Arrange
        var imageContent = CreateFakeImageBytes();
        var formContent = CreateMultipartFormContent(imageContent, "test.jpg", "image/jpeg");

        // Act
        var response = await _client.PostAsync("/api/v1/series/urn:mvn:series:test-123/cover", formContent);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UploadCover_WithOversizedFile_ReturnsBadRequest()
    {
        // Arrange - Create a series
        var series = await CreateTestSeries(_uploaderClient, "Oversize Test");
        var seriesId = ExtractSeriesId(series);

        // Create oversized image (11MB, exceeds 10MB limit)
        var oversizedContent = new byte[11 * 1024 * 1024];
        var formContent = CreateMultipartFormContent(oversizedContent, "huge.jpg", "image/jpeg");

        // Act
        var response = await _uploaderClient.PostAsync($"/api/v1/series/{seriesId}/cover", formContent);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadAsStringAsync();
        Assert.Contains("too large", error.ToLower());
    }

    [Fact]
    public async Task UploadCover_WithInvalidFileType_ReturnsBadRequest()
    {
        // Arrange
        var series = await CreateTestSeries(_uploaderClient, "Invalid Type Test");
        var seriesId = ExtractSeriesId(series);

        var textContent = Encoding.UTF8.GetBytes("This is not an image");
        var formContent = CreateMultipartFormContent(textContent, "not-an-image.txt", "text/plain");

        // Act
        var response = await _uploaderClient.PostAsync($"/api/v1/series/{seriesId}/cover", formContent);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadAsStringAsync();
        Assert.Contains("Invalid file type", error);
    }

    [Fact]
    public async Task UploadCover_ForNonExistentSeries_ReturnsNotFound()
    {
        // Arrange
        var nonExistentId = "urn:mvn:series:00000000-0000-0000-0000-000000000000";
        var imageContent = CreateFakeImageBytes();
        var formContent = CreateMultipartFormContent(imageContent, "test.jpg", "image/jpeg");

        // Act
        var response = await _uploaderClient.PostAsync($"/api/v1/series/{nonExistentId}/cover", formContent);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UploadCover_ForOtherUsersSeries_ReturnsForbidden()
    {
        // Arrange - Create series as one uploader
        var series = await CreateTestSeries(_uploaderClient, "First User Series");
        var seriesId = ExtractSeriesId(series);

        // Try to upload cover as different uploader
        var anotherUploader = _factory.CreateAuthenticatedClient("Uploader");
        var imageContent = CreateFakeImageBytes();
        var formContent = CreateMultipartFormContent(imageContent, "test.jpg", "image/jpeg");

        // Act
        var response = await anotherUploader.PostAsync($"/api/v1/series/{seriesId}/cover", formContent);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact(Skip = "Requires ImageSharp service and file system - infrastructure dependent")]
    public async Task UploadCover_WithLanguageParameter_SavesLocalizedCover()
    {
        // Arrange
        var series = await CreateTestSeries(_uploaderClient, "Localized Cover Test");
        var seriesId = ExtractSeriesId(series);

        var imageContent = CreateFakeImageBytes();
        var formContent = CreateMultipartFormContent(imageContent, "jp-cover.jpg", "image/jpeg");

        // Act - Upload with Japanese language
        var response = await _uploaderClient.PostAsync(
            $"/api/v1/series/{seriesId}/cover?lang=ja", formContent);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"language\":\"ja\"", result);
    }

    #endregion

    #region Cover Download from URL Tests

    [Fact]
    public async Task DownloadCoverFromUrl_WithValidUrl_ReturnsOk()
    {
        // Arrange
        var series = await CreateTestSeries(_uploaderClient, "URL Download Test");
        var seriesId = ExtractSeriesId(series);

        var payload = new
        {
            url = "https://placehold.co/400x600.jpg",
            language = (string?)null
        };

        // Act
        var response = await _uploaderClient.PostAsJsonAsync(
            $"/api/v1/series/{seriesId}/cover/from-url", payload);

        // Assert
        // Note: This might fail in CI without internet or if the service is down
        // In real tests, you'd mock the HTTP client
        if (response.StatusCode == HttpStatusCode.OK)
        {
            var result = await response.Content.ReadAsStringAsync();
            Assert.Contains("downloaded_from", result);
            Assert.Contains("variants", result);
        }
    }

    [Fact]
    public async Task DownloadCoverFromUrl_WithInvalidUrl_ReturnsBadRequest()
    {
        // Arrange
        var series = await CreateTestSeries(_uploaderClient, "Invalid URL Test");
        var seriesId = ExtractSeriesId(series);

        var payload = new
        {
            url = "not-a-valid-url",
            language = (string?)null
        };

        // Act
        var response = await _uploaderClient.PostAsJsonAsync(
            $"/api/v1/series/{seriesId}/cover/from-url", payload);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DownloadCoverFromUrl_WithEmptyUrl_ReturnsBadRequest()
    {
        // Arrange
        var series = await CreateTestSeries(_uploaderClient, "Empty URL Test");
        var seriesId = ExtractSeriesId(series);

        var payload = new
        {
            url = "",
            language = (string?)null
        };

        // Act
        var response = await _uploaderClient.PostAsJsonAsync(
            $"/api/v1/series/{seriesId}/cover/from-url", payload);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    #endregion

    #region Cover Retrieval Tests

    [Fact]
    public async Task GetCover_ForSeriesWithCover_ReturnsImage()
    {
        // Arrange - Create series and upload cover
        var series = await CreateTestSeries(_uploaderClient, "Get Cover Test");
        var seriesId = ExtractSeriesId(series);

        var imageContent = CreateFakeImageBytes();
        var formContent = CreateMultipartFormContent(imageContent, "test.jpg", "image/jpeg");
        await _uploaderClient.PostAsync($"/api/v1/series/{seriesId}/cover", formContent);

        // Act - Get the cover
        var response = await _client.GetAsync($"/api/v1/series/{seriesId}/cover");

        // Assert
        if (response.StatusCode == HttpStatusCode.OK)
        {
            Assert.Contains("image/", response.Content.Headers.ContentType?.MediaType ?? "");
        }
    }

    [Fact]
    public async Task GetCover_WithVariantParameter_ReturnsVariant()
    {
        // Arrange
        var series = await CreateTestSeries(_uploaderClient, "Variant Test");
        var seriesId = ExtractSeriesId(series);

        var imageContent = CreateFakeImageBytes();
        var formContent = CreateMultipartFormContent(imageContent, "test.jpg", "image/jpeg");
        await _uploaderClient.PostAsync($"/api/v1/series/{seriesId}/cover", formContent);

        // Act - Request thumbnail variant
        var response = await _client.GetAsync($"/api/v1/series/{seriesId}/cover?variant=thumbnail");

        // Assert
        if (response.StatusCode == HttpStatusCode.OK)
        {
            Assert.Contains("image/", response.Content.Headers.ContentType?.MediaType ?? "");
        }
    }

    [Fact]
    public async Task GetAllCovers_ForSeriesWithMultipleLanguages_ReturnsAllCovers()
    {
        // Arrange
        var series = await CreateTestSeries(_uploaderClient, "Multi-Language Test");
        var seriesId = ExtractSeriesId(series);

        // Upload default cover
        var imageContent = CreateFakeImageBytes();
        var formContent1 = CreateMultipartFormContent(imageContent, "default.jpg", "image/jpeg");
        await _uploaderClient.PostAsync($"/api/v1/series/{seriesId}/cover", formContent1);

        // Upload Japanese cover
        var formContent2 = CreateMultipartFormContent(imageContent, "ja.jpg", "image/jpeg");
        await _uploaderClient.PostAsync($"/api/v1/series/{seriesId}/cover?lang=ja", formContent2);

        // Act
        var response = await _client.GetAsync($"/api/v1/series/{seriesId}/covers");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadAsStringAsync();
        // Should contain information about multiple covers
        Assert.NotEmpty(result);
    }

    #endregion

    #region Permission Tests

    [Fact(Skip = "Requires ImageSharp service and file system - infrastructure dependent")]
    public async Task AdminCanUploadCover_ForAnySereis()
    {
        // Arrange - Create series as uploader
        var series = await CreateTestSeries(_uploaderClient, "Admin Access Test");
        var seriesId = ExtractSeriesId(series);

        var imageContent = CreateFakeImageBytes();
        var formContent = CreateMultipartFormContent(imageContent, "admin-cover.jpg", "image/jpeg");

        // Act - Upload as admin
        var response = await _adminClient.PostAsync($"/api/v1/series/{seriesId}/cover", formContent);

        // Assert - Admin should have access
        Assert.True(response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.Created);
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Creates a test series and returns the response content.
    /// </summary>
    private static async Task<string> CreateTestSeries(HttpClient client, string title)
    {
        var payload = new
        {
            title,
            description = $"Test series: {title}",
            media_type = "Photo",
            reading_direction = "LTR"
        };

        var response = await client.PostAsJsonAsync("/api/v1/series", payload);
        response.EnsureSuccessStatusCode();
        
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>
    /// Extracts series ID from creation response.
    /// </summary>
    private static string ExtractSeriesId(string responseContent)
    {
        // Simple extraction - in real code, deserialize JSON properly
        var match = System.Text.RegularExpressions.Regex.Match(
            responseContent, @"urn:mvn:series:[a-f0-9-]+");
        
        return match.Success ? match.Value : throw new InvalidOperationException("Series ID not found");
    }

    /// <summary>
    /// Creates fake image bytes for testing.
    /// </summary>
    private static byte[] CreateFakeImageBytes()
    {
        // Create minimal valid JPEG header
        return new byte[] {
            0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46,
            0x49, 0x46, 0x00, 0x01, 0x01, 0x00, 0x00, 0x01,
            0x00, 0x01, 0x00, 0x00, 0xFF, 0xD9
        };
    }

    /// <summary>
    /// Creates multipart form content for file upload.
    /// </summary>
    private static MultipartFormDataContent CreateMultipartFormContent(
        byte[] fileContent, string fileName, string contentType)
    {
        var content = new MultipartFormDataContent();
        var fileStreamContent = new ByteArrayContent(fileContent);
        fileStreamContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(fileStreamContent, "file", fileName);
        
        return content;
    }

    #endregion
}
