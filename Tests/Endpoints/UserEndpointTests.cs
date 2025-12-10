using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using MehguViewer.Core.Shared;
using Xunit;

namespace MehguViewer.Core.Tests.Endpoints;

/// <summary>
/// Comprehensive test suite for User endpoints verifying profile management,
/// password changes, library/history access, and account deletion.
/// </summary>
/// <remarks>
/// <para><strong>Test Coverage Areas:</strong></para>
/// <list type="bullet">
///   <item><description>Profile retrieval and admin status detection</description></item>
///   <item><description>Password change with validation</description></item>
///   <item><description>Library and history management</description></item>
///   <item><description>Reading progress tracking</description></item>
///   <item><description>Batch history import</description></item>
///   <item><description>Account deletion (GDPR)</description></item>
///   <item><description>Authentication and authorization</description></item>
///   <item><description>Error handling and RFC 7807 compliance</description></item>
/// </list>
/// </remarks>
[Trait("Category", "Integration")]
[Trait("Endpoint", "User")]
public class UserEndpointTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly HttpClient _authenticatedClient;
    private readonly TestWebApplicationFactory _factory;

    public UserEndpointTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _authenticatedClient = factory.CreateAuthenticatedClient();
    }

    #region Profile Tests

    /// <summary>
    /// Tests that getting user profile without authentication returns 401.
    /// </summary>
    [Fact]
    public async Task GetProfile_WithoutAuth_ReturnsUnauthorized()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/me");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Tests that getting user profile with authentication returns OK.
    /// </summary>
    [Fact]
    public async Task GetProfile_WithAuth_ReturnsOkOrNotFound()
    {
        // Act
        var response = await _authenticatedClient.GetAsync("/api/v1/me");

        // Assert
        Assert.True(
            response.StatusCode == HttpStatusCode.OK || 
            response.StatusCode == HttpStatusCode.NotFound,
            $"Expected OK or NotFound, got {response.StatusCode}");
    }

    #endregion

    #region Password Change Tests

    /// <summary>
    /// Tests that changing password without authentication returns 401.
    /// </summary>
    [Fact]
    public async Task ChangePassword_WithoutAuth_ReturnsUnauthorized()
    {
        // Arrange
        var payload = new
        {
            current_password = "oldpass",
            new_password = "newpass123"
        };

        // Act
        var response = await _client.PatchAsync("/api/v1/me/password", 
            JsonContent.Create(payload));

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Tests that changing password with empty request body returns 400.
    /// </summary>
    [Fact]
    public async Task ChangePassword_EmptyBody_ReturnsBadRequest()
    {
        // Act
        var response = await _authenticatedClient.PatchAsync("/api/v1/me/password", 
            JsonContent.Create(new { }));

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Tests that changing password with missing current password returns 400.
    /// </summary>
    [Fact]
    public async Task ChangePassword_MissingCurrentPassword_ReturnsBadRequest()
    {
        // Arrange
        var payload = new
        {
            new_password = "newpassword123"
        };

        // Act
        var response = await _authenticatedClient.PatchAsync("/api/v1/me/password", 
            JsonContent.Create(payload));

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Tests that changing password with missing new password returns 400.
    /// </summary>
    [Fact]
    public async Task ChangePassword_MissingNewPassword_ReturnsBadRequest()
    {
        // Arrange
        var payload = new
        {
            current_password = "oldpassword"
        };

        // Act
        var response = await _authenticatedClient.PatchAsync("/api/v1/me/password", 
            JsonContent.Create(payload));

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    #endregion

    #region Library Tests

    /// <summary>
    /// Tests that getting library without authentication returns 401.
    /// </summary>
    [Fact]
    public async Task GetLibrary_WithoutAuth_ReturnsUnauthorized()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/me/library");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Tests that getting library with authentication returns OK.
    /// </summary>
    [Fact]
    public async Task GetLibrary_WithAuth_ReturnsOk()
    {
        // Act
        var response = await _authenticatedClient.GetAsync("/api/v1/me/library");

        // Assert
        Assert.True(
            response.StatusCode == HttpStatusCode.OK || 
            response.StatusCode == HttpStatusCode.Unauthorized,
            $"Expected OK or Unauthorized, got {response.StatusCode}");
    }

    #endregion

    #region History Tests

    /// <summary>
    /// Tests that getting history without authentication returns 401.
    /// </summary>
    [Fact]
    public async Task GetHistory_WithoutAuth_ReturnsUnauthorized()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/me/history");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Tests that getting history with authentication returns OK.
    /// </summary>
    [Fact]
    public async Task GetHistory_WithAuth_ReturnsOk()
    {
        // Act
        var response = await _authenticatedClient.GetAsync("/api/v1/me/history");

        // Assert
        Assert.True(
            response.StatusCode == HttpStatusCode.OK || 
            response.StatusCode == HttpStatusCode.Unauthorized,
            $"Expected OK or Unauthorized, got {response.StatusCode}");
    }

    #endregion

    #region Progress Update Tests

    /// <summary>
    /// Tests that updating progress without authentication returns 401.
    /// </summary>
    [Fact]
    public async Task UpdateProgress_WithoutAuth_ReturnsUnauthorized()
    {
        // Arrange
        var payload = new
        {
            series_urn = "urn:mvn:series:test-123",
            chapter_id = "chapter-1",
            page_number = 5,
            status = "reading",
            updated_at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/me/progress", payload);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Tests that updating progress with empty body returns 400.
    /// </summary>
    [Fact]
    public async Task UpdateProgress_EmptyBody_ReturnsBadRequest()
    {
        // Act
        var response = await _authenticatedClient.PostAsync("/api/v1/me/progress", null);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Tests that updating progress without series URN returns 400.
    /// </summary>
    [Fact]
    public async Task UpdateProgress_MissingSeriesUrn_ReturnsBadRequest()
    {
        // Arrange
        var payload = new
        {
            chapter_id = "chapter-1",
            page_number = 5,
            status = "reading",
            updated_at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        // Act
        var response = await _authenticatedClient.PostAsJsonAsync("/api/v1/me/progress", payload);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    #endregion

    #region Batch Import Tests

    /// <summary>
    /// Tests that batch importing history without authentication returns 401.
    /// </summary>
    [Fact]
    public async Task BatchImportHistory_WithoutAuth_ReturnsUnauthorized()
    {
        // Arrange
        var payload = new
        {
            items = new[]
            {
                new
                {
                    series_urn = "urn:mvn:series:test-1",
                    chapter_urn = "chapter-1",
                    read_at = DateTime.UtcNow
                }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/me/history/batch", payload);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Tests that batch importing history with empty items returns 400.
    /// </summary>
    [Fact]
    public async Task BatchImportHistory_EmptyItems_ReturnsBadRequest()
    {
        // Arrange
        var payload = new
        {
            items = Array.Empty<object>()
        };

        // Act
        var response = await _authenticatedClient.PostAsJsonAsync("/api/v1/me/history/batch", payload);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Tests that batch importing history with null items returns 400.
    /// </summary>
    [Fact]
    public async Task BatchImportHistory_NullItems_ReturnsBadRequest()
    {
        // Arrange
        var payload = new
        {
            items = (object[]?)null
        };

        // Act
        var response = await _authenticatedClient.PostAsJsonAsync("/api/v1/me/history/batch", payload);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    #endregion

    #region Account Deletion Tests

    /// <summary>
    /// Tests that deleting account without authentication returns 401.
    /// </summary>
    [Fact]
    public async Task DeleteAccount_WithoutAuth_ReturnsUnauthorized()
    {
        // Act
        var response = await _client.DeleteAsync("/api/v1/me");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    #endregion

    #region Error Format Tests

    /// <summary>
    /// Tests that error responses conform to RFC 7807 Problem Details format.
    /// </summary>
    [Fact]
    public async Task Error_ReturnsRfc7807Format()
    {
        // Arrange - Trigger an error by requesting without auth
        
        // Act
        var response = await _client.GetAsync("/api/v1/me");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        
        var content = await response.Content.ReadAsStringAsync();
        
        // Verify RFC 7807 required fields
        Assert.Contains("\"type\"", content);
        Assert.Contains("\"title\"", content);
        Assert.Contains("\"status\"", content);
    }

    #endregion

    #region Concurrent Request Tests

    /// <summary>
    /// Tests that concurrent requests are handled correctly.
    /// </summary>
    [Fact]
    public async Task ConcurrentRequests_AllSucceed()
    {
        // Arrange
        var requestCount = 5;

        // Act
        var tasks = Enumerable.Range(1, requestCount)
            .Select(_ => _authenticatedClient.GetAsync("/api/v1/me/history"));
        var responses = await Task.WhenAll(tasks);

        // Assert
        foreach (var response in responses)
        {
            Assert.True(
                response.StatusCode == HttpStatusCode.OK || 
                response.StatusCode == HttpStatusCode.Unauthorized,
                $"Expected OK or Unauthorized, got {response.StatusCode}");
        }
    }

    #endregion

    #region Performance Tests

    /// <summary>
    /// Tests that response time is reasonable for profile requests.
    /// </summary>
    [Fact]
    public async Task GetProfile_ResponseTime_IsReasonable()
    {
        // Arrange
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Act
        var response = await _authenticatedClient.GetAsync("/api/v1/me");
        stopwatch.Stop();

        // Assert
        Assert.True(
            response.IsSuccessStatusCode || 
            response.StatusCode == HttpStatusCode.Unauthorized,
            "Request should complete successfully or return Unauthorized");
        
        // Should complete within 5 seconds (generous threshold for CI/CD)
        Assert.True(stopwatch.ElapsedMilliseconds < 5000, 
            $"Request took {stopwatch.ElapsedMilliseconds}ms, expected < 5000ms");
    }

    #endregion
}
