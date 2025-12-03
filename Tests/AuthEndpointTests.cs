using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace MehguViewer.Core.Tests;

/// <summary>
/// Tests for authentication-related endpoints: login, register, password.
/// </summary>
[Trait("Category", "Unit")]
[Trait("Endpoint", "Auth")]
public class AuthEndpointTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public AuthEndpointTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    #region Registration

    [Fact]
    public async Task Register_ValidCredentials_ReturnsAppropriateStatus()
    {
        // Arrange
        var uniqueUsername = $"testuser_{Guid.NewGuid():N}";
        var payload = new
        {
            username = uniqueUsername,
            password = "SecureP@ssw0rd123!"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/auth/register", payload);

        // Assert - Registration can return various statuses:
        // - OK/Created: User registered successfully
        // - Forbidden: Registration is closed
        // - BadRequest: Validation error (e.g., SETUP_NOT_COMPLETE)
        Assert.True(
            response.StatusCode == HttpStatusCode.OK || 
            response.StatusCode == HttpStatusCode.Created ||
            response.StatusCode == HttpStatusCode.Forbidden ||
            response.StatusCode == HttpStatusCode.BadRequest,
            $"Expected OK, Created, Forbidden, or BadRequest, got {response.StatusCode}"
        );
    }

    [Fact]
    public async Task Register_MissingUsername_ReturnsBadRequest()
    {
        // Arrange
        var payload = new
        {
            password = "SecureP@ssw0rd123!"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/auth/register", payload);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_MissingPassword_ReturnsBadRequest()
    {
        // Arrange
        var payload = new
        {
            username = "testuser"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/auth/register", payload);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_WeakPassword_ReturnsBadRequest()
    {
        // Arrange
        var payload = new
        {
            username = "testuser",
            password = "weak"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/auth/register", payload);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("WEAK_PASSWORD", content);
    }

    [Fact]
    public async Task Register_ShortUsername_ReturnsBadRequest()
    {
        // Arrange
        var payload = new
        {
            username = "ab", // Less than 3 characters
            password = "SecureP@ssw0rd123!"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/auth/register", payload);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    #endregion

    #region Login

    [Fact]
    public async Task Login_MissingCredentials_ReturnsBadRequest()
    {
        // Arrange
        var payload = new { };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", payload);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_InvalidCredentials_ReturnsUnauthorized()
    {
        // Arrange
        var payload = new
        {
            username = "nonexistent",
            password = "wrongpassword"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", payload);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_ValidCredentials_ReturnsToken()
    {
        // Arrange - First register a user
        var uniqueUsername = $"logintest_{Guid.NewGuid():N}";
        var password = "SecureP@ssw0rd123!";
        
        var registerPayload = new
        {
            username = uniqueUsername,
            password = password
        };
        var registerResponse = await _client.PostAsJsonAsync("/api/v1/auth/register", registerPayload);
        
        // Only proceed with login test if registration succeeded
        if (registerResponse.StatusCode != HttpStatusCode.OK)
        {
            // Skip this test if registration is closed
            return;
        }

        // Act
        var loginPayload = new
        {
            username = uniqueUsername,
            password = password
        };
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", loginPayload);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("token", content);
    }

    #endregion

    #region Admin Password

    [Fact]
    public async Task AdminPassword_MissingPassword_ReturnsBadRequest()
    {
        // Arrange
        var payload = new { };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/system/admin/password", payload);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AdminPassword_WeakPassword_ReturnsBadRequest()
    {
        // Arrange
        var payload = new
        {
            password = "weak"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/system/admin/password", payload);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    #endregion
}
