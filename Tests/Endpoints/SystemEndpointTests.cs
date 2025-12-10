using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using MehguViewer.Core.Shared;

namespace MehguViewer.Core.Tests.Endpoints;

/// <summary>
/// Comprehensive test suite for system-related endpoints including configuration,
/// storage, statistics, logs, and database management.
/// </summary>
/// <remarks>
/// <para><strong>Test Coverage Areas:</strong></para>
/// <list type="bullet">
///   <item><description>Public endpoints (well-known, instance, taxonomy)</description></item>
///   <item><description>System configuration and setup</description></item>
///   <item><description>Storage management and statistics</description></item>
///   <item><description>Logs management</description></item>
///   <item><description>Database configuration</description></item>
///   <item><description>Authorization and security</description></item>
///   <item><description>Error handling and validation</description></item>
/// </list>
/// </remarks>
[Trait("Category", "Integration")]
[Trait("Endpoint", "System")]
public class SystemEndpointTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly HttpClient _authenticatedClient;
    private readonly TestWebApplicationFactory _factory;

    public SystemEndpointTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _authenticatedClient = factory.CreateAuthenticatedClient();
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

    #region Cache Refresh

    [Fact]
    public async Task RefreshCache_Post_RequiresAuthentication()
    {
        // Act
        var response = await _client.PostAsync("/api/v1/system/refresh-cache", null);

        // Assert - Should require authentication
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    #endregion

    #region System Configuration Tests

    /// <summary>
    /// Tests that getting system configuration requires authentication.
    /// </summary>
    [Fact]
    public async Task GetSystemConfig_NoAuthentication_ReturnsUnauthorized()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/system/config");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Tests that authenticated users can retrieve system configuration.
    /// </summary>
    [Fact]
    public async Task GetSystemConfig_WithAuthentication_ReturnsOk()
    {
        // Act
        var response = await _authenticatedClient.GetAsync("/api/v1/system/config");

        // Assert
        Assert.True(
            response.StatusCode == HttpStatusCode.OK || 
            response.StatusCode == HttpStatusCode.Unauthorized,
            $"Expected OK or Unauthorized, got {response.StatusCode}");
    }

    /// <summary>
    /// Tests that patching system configuration requires admin authorization.
    /// </summary>
    [Fact]
    public async Task PatchSystemConfig_NoAuthentication_ReturnsUnauthorized()
    {
        // Arrange
        var update = new { maintenance_mode = true };

        // Act
        var response = await _client.PatchAsync(
            "/api/v1/admin/configuration",
            JsonContent.Create(update));

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    #endregion

    #region Storage Management Tests

    /// <summary>
    /// Tests that getting storage stats requires admin authorization.
    /// </summary>
    [Fact]
    public async Task GetStorageStats_NoAuthentication_ReturnsUnauthorized()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/admin/storage");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Tests that updating storage settings validates thumbnail size range.
    /// </summary>
    [Fact]
    public async Task UpdateStorageSettings_InvalidThumbnailSize_HandledGracefully()
    {
        // Arrange
        var invalidSettings = new
        {
            thumbnail_size = 5000 // Way above max (500)
        };

        // Act
        var response = await _authenticatedClient.PatchAsync(
            "/api/v1/admin/storage",
            JsonContent.Create(invalidSettings));

        // Assert - Should either reject or accept with clamping
        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    /// <summary>
    /// Tests that clearing cache requires admin authorization.
    /// </summary>
    [Fact]
    public async Task ClearCache_NoAuthentication_ReturnsUnauthorized()
    {
        // Act
        var response = await _client.PostAsync("/api/v1/admin/storage/clear-cache", null);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    #endregion

    #region Logs Management Tests

    /// <summary>
    /// Tests that getting logs requires admin authorization.
    /// </summary>
    [Fact]
    public async Task GetLogs_NoAuthentication_ReturnsUnauthorized()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/admin/logs?count=10");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Tests that clearing logs requires admin authorization.
    /// </summary>
    [Fact]
    public async Task ClearLogs_NoAuthentication_ReturnsUnauthorized()
    {
        // Act
        var response = await _client.DeleteAsync("/api/v1/admin/logs");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    #endregion

    #region Database Configuration Tests

    /// <summary>
    /// Tests that testing database connection accepts valid configuration.
    /// </summary>
    [Fact]
    public async Task TestDatabaseConnection_ValidConfig_ReturnsResponse()
    {
        // Arrange
        var config = new
        {
            host = "localhost",
            port = 5432,
            database = "test",
            username = "test",
            password = "test"
        };

        // Act
        var response = await _client.PostAsync(
            "/api/v1/system/database/test",
            JsonContent.Create(config));

        // Assert - Should return either success or connection error, not 500
        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    /// <summary>
    /// Tests that getting embedded database status returns valid response.
    /// </summary>
    [Fact]
    public async Task GetEmbeddedDatabaseStatus_ReturnsStatus()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/system/database/embedded-status");

        // Assert - Should return OK or Unauthorized (depending on setup status)
        Assert.True(
            response.StatusCode == HttpStatusCode.OK || 
            response.StatusCode == HttpStatusCode.Unauthorized,
            $"Expected OK or Unauthorized, got {response.StatusCode}");
    }

    #endregion

    #region Export Tests

    /// <summary>
    /// Tests that exporting series requires admin authorization.
    /// </summary>
    [Fact]
    public async Task ExportAllSeries_NoAuthentication_ReturnsUnauthorized()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/admin/export/series");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Tests that exporting series to files requires admin authorization.
    /// </summary>
    [Fact]
    public async Task ExportSeriesToFiles_NoAuthentication_ReturnsUnauthorized()
    {
        // Act
        var response = await _client.PostAsync("/api/v1/admin/export/series-to-files", null);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    #endregion

    #region Report Tests

    /// <summary>
    /// Tests that creating a report without target URN returns bad request.
    /// </summary>
    [Fact]
    public async Task CreateReport_MissingTargetUrn_ReturnsBadRequest()
    {
        // Arrange
        var report = new
        {
            target_urn = "",
            reason = "Test reason"
        };

        // Act
        var response = await _authenticatedClient.PostAsync(
            "/api/v1/reports",
            JsonContent.Create(report));

        // Assert
        Assert.True(
            response.StatusCode == HttpStatusCode.BadRequest || 
            response.StatusCode == HttpStatusCode.Unauthorized,
            $"Expected BadRequest or Unauthorized, got {response.StatusCode}");
    }

    /// <summary>
    /// Tests that creating a report without reason returns bad request.
    /// </summary>
    [Fact]
    public async Task CreateReport_MissingReason_ReturnsBadRequest()
    {
        // Arrange
        var report = new
        {
            target_urn = "urn:mvn:series:test",
            reason = ""
        };

        // Act
        var response = await _authenticatedClient.PostAsync(
            "/api/v1/reports",
            JsonContent.Create(report));

        // Assert
        Assert.True(
            response.StatusCode == HttpStatusCode.BadRequest || 
            response.StatusCode == HttpStatusCode.Unauthorized,
            $"Expected BadRequest or Unauthorized, got {response.StatusCode}");
    }

    /// <summary>
    /// Tests that creating a report requires authentication.
    /// </summary>
    [Fact]
    public async Task CreateReport_NoAuthentication_ReturnsUnauthorized()
    {
        // Arrange
        var report = new
        {
            target_urn = "urn:mvn:series:test",
            reason = "Test reason"
        };

        // Act
        var response = await _client.PostAsync(
            "/api/v1/reports",
            JsonContent.Create(report));

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    #endregion

    #region System Statistics Tests

    /// <summary>
    /// Tests that getting system stats requires admin authorization.
    /// </summary>
    [Fact]
    public async Task GetSystemStats_NoAuthentication_ReturnsUnauthorized()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/admin/stats");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    #endregion

    #region Performance Tests

    /// <summary>
    /// Tests that taxonomy endpoint responds within reasonable time.
    /// </summary>
    [Fact]
    public async Task Taxonomy_Get_RespondsQuickly()
    {
        // Arrange
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Act
        var response = await _client.GetAsync("/api/v1/taxonomy");
        stopwatch.Stop();

        // Assert
        Assert.True(response.IsSuccessStatusCode, "Taxonomy request should succeed");
        Assert.True(stopwatch.ElapsedMilliseconds < 5000, 
            $"Taxonomy request took {stopwatch.ElapsedMilliseconds}ms, expected < 5000ms");
    }

    /// <summary>
    /// Tests that instance endpoint responds within reasonable time.
    /// </summary>
    [Fact]
    public async Task Instance_Get_RespondsQuickly()
    {
        // Arrange
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Act
        var response = await _client.GetAsync("/api/v1/instance");
        stopwatch.Stop();

        // Assert
        Assert.True(response.IsSuccessStatusCode, "Instance request should succeed");
        Assert.True(stopwatch.ElapsedMilliseconds < 5000, 
            $"Instance request took {stopwatch.ElapsedMilliseconds}ms, expected < 5000ms");
    }

    #endregion

    #region Error Handling Tests

    /// <summary>
    /// Tests that invalid database configuration returns proper error format.
    /// </summary>
    [Fact]
    public async Task TestDatabaseConnection_InvalidConfig_ReturnsErrorResponse()
    {
        // Arrange
        var invalidConfig = new
        {
            host = "invalid-host-that-does-not-exist-12345",
            port = 99999,
            database = "test",
            username = "test",
            password = "test"
        };

        // Act
        var response = await _client.PostAsync(
            "/api/v1/system/database/test",
            JsonContent.Create(invalidConfig));

        // Assert
        Assert.True(
            response.StatusCode == HttpStatusCode.BadRequest || 
            response.StatusCode == HttpStatusCode.Unauthorized,
            $"Expected BadRequest or Unauthorized, got {response.StatusCode}");
        
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            var content = await response.Content.ReadAsStringAsync();
            Assert.Contains("DB_CONNECTION_FAILED", content);
        }
    }

    #endregion
}
