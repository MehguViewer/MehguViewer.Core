using System.Security.Claims;
using MehguViewer.Core.UI.Services;
using Xunit;

// Import AuthExtensions for extension methods
using static MehguViewer.Core.UI.Services.AuthExtensions;

namespace MehguViewer.Core.Tests.UI.Services;

/// <summary>
/// Unit tests for <see cref="AuthExtensions"/>.
/// Tests authorization helper methods for ClaimsPrincipal extensions.
/// Provides comprehensive coverage of role-based and scope-based authorization.
/// </summary>
[Trait("Category", "Unit")]
[Trait("Component", "AuthExtensions")]
public class AuthExtensionsTests
{
    #region IsAdmin Tests
    
    /// <summary>
    /// Tests that a user with Admin role is recognized as admin.
    /// </summary>
    [Fact]
    public void IsAdmin_WithAdminRole_ReturnsTrue()
    {
        // Arrange
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "urn:mvn:user:test-123"),
            new Claim(ClaimTypes.Role, "Admin")
        };
        var identity = new ClaimsIdentity(claims, "jwt");
        var user = new ClaimsPrincipal(identity);

        // Act
        var isAdmin = user.IsAdmin();

        // Assert
        Assert.True(isAdmin);
    }

    /// <summary>
    /// Tests that a user with mvn:admin scope is recognized as admin.
    /// </summary>
    [Fact]
    public void IsAdmin_WithAdminScope_ReturnsTrue()
    {
        // Arrange
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "urn:mvn:user:test-123"),
            new Claim("scope", "mvn:admin mvn:ingest")
        };
        var identity = new ClaimsIdentity(claims, "jwt");
        var user = new ClaimsPrincipal(identity);

        // Act
        var isAdmin = user.IsAdmin();

        // Assert
        Assert.True(isAdmin);
    }

    /// <summary>
    /// Tests that a regular user without admin privileges is not recognized as admin.
    /// </summary>
    [Fact]
    public void IsAdmin_WithoutAdminPrivileges_ReturnsFalse()
    {
        // Arrange
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "urn:mvn:user:test-123"),
            new Claim(ClaimTypes.Role, "User")
        };
        var identity = new ClaimsIdentity(claims, "jwt");
        var user = new ClaimsPrincipal(identity);

        // Act
        var isAdmin = user.IsAdmin();

        // Assert
        Assert.False(isAdmin);
    }

    /// <summary>
    /// Tests that an unauthenticated user is not recognized as admin.
    /// </summary>
    [Fact]
    public void IsAdmin_WithUnauthenticatedUser_ReturnsFalse()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity());

        // Act
        var isAdmin = user.IsAdmin();

        // Assert
        Assert.False(isAdmin);
    }

    #endregion

    #region CanUpload Tests

    /// <summary>
    /// Tests that an admin user can upload.
    /// </summary>
    [Fact]
    public void CanUpload_WithAdminRole_ReturnsTrue()
    {
        // Arrange
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "urn:mvn:user:test-123"),
            new Claim(ClaimTypes.Role, "Admin")
        };
        var identity = new ClaimsIdentity(claims, "jwt");
        var user = new ClaimsPrincipal(identity);

        // Act
        var canUpload = user.CanUpload();

        // Assert
        Assert.True(canUpload);
    }

    /// <summary>
    /// Tests that a user with Uploader role can upload.
    /// </summary>
    [Fact]
    public void CanUpload_WithUploaderRole_ReturnsTrue()
    {
        // Arrange
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "urn:mvn:user:test-123"),
            new Claim(ClaimTypes.Role, "Uploader")
        };
        var identity = new ClaimsIdentity(claims, "jwt");
        var user = new ClaimsPrincipal(identity);

        // Act
        var canUpload = user.CanUpload();

        // Assert
        Assert.True(canUpload);
    }

    /// <summary>
    /// Tests that a user with mvn:ingest scope can upload.
    /// </summary>
    [Fact]
    public void CanUpload_WithIngestScope_ReturnsTrue()
    {
        // Arrange
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "urn:mvn:user:test-123"),
            new Claim("scope", "mvn:ingest")
        };
        var identity = new ClaimsIdentity(claims, "jwt");
        var user = new ClaimsPrincipal(identity);

        // Act
        var canUpload = user.CanUpload();

        // Assert
        Assert.True(canUpload);
    }

    /// <summary>
    /// Tests that a regular user without upload privileges cannot upload.
    /// </summary>
    [Fact]
    public void CanUpload_WithoutUploadPrivileges_ReturnsFalse()
    {
        // Arrange
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "urn:mvn:user:test-123"),
            new Claim(ClaimTypes.Role, "User")
        };
        var identity = new ClaimsIdentity(claims, "jwt");
        var user = new ClaimsPrincipal(identity);

        // Act
        var canUpload = user.CanUpload();

        // Assert
        Assert.False(canUpload);
    }

    /// <summary>
    /// Tests that an unauthenticated user cannot upload.
    /// </summary>
    [Fact]
    public void CanUpload_WithUnauthenticatedUser_ReturnsFalse()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity());

        // Act
        var canUpload = user.CanUpload();

        // Assert
        Assert.False(canUpload);
    }

    #endregion

    #region GetUserId Tests

    /// <summary>
    /// Tests retrieval of user ID from NameIdentifier claim.
    /// </summary>
    [Fact]
    public void GetUserId_WithValidClaims_ReturnsUserId()
    {
        // Arrange
        var expectedUserId = "urn:mvn:user:test-123";
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, expectedUserId),
            new Claim(ClaimTypes.Name, "Test User")
        };
        var identity = new ClaimsIdentity(claims, "jwt");
        var user = new ClaimsPrincipal(identity);

        // Act
        var userId = user.GetUserId();

        // Assert
        Assert.Equal(expectedUserId, userId);
    }

    /// <summary>
    /// Tests retrieval of user ID from 'sub' claim fallback.
    /// </summary>
    [Fact]
    public void GetUserId_WithSubClaim_ReturnsUserId()
    {
        // Arrange
        var expectedUserId = "urn:mvn:user:test-456";
        var claims = new[]
        {
            new Claim("sub", expectedUserId),
            new Claim(ClaimTypes.Name, "Test User")
        };
        var identity = new ClaimsIdentity(claims, "jwt");
        var user = new ClaimsPrincipal(identity);

        // Act
        var userId = user.GetUserId();

        // Assert
        Assert.Equal(expectedUserId, userId);
    }

    /// <summary>
    /// Tests that GetUserId returns null when no ID claim exists.
    /// </summary>
    [Fact]
    public void GetUserId_WithoutIdClaim_ReturnsNull()
    {
        // Arrange
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, "Test User")
        };
        var identity = new ClaimsIdentity(claims, "jwt");
        var user = new ClaimsPrincipal(identity);

        // Act
        var userId = user.GetUserId();

        // Assert
        Assert.Null(userId);
    }

    #endregion

    #region GetDisplayName Tests

    /// <summary>
    /// Tests retrieval of display name from Name claim.
    /// </summary>
    [Fact]
    public void GetDisplayName_WithValidClaims_ReturnsDisplayName()
    {
        // Arrange
        var expectedName = "Test User";
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "urn:mvn:user:test-123"),
            new Claim(ClaimTypes.Name, expectedName)
        };
        var identity = new ClaimsIdentity(claims, "jwt");
        var user = new ClaimsPrincipal(identity);

        // Act
        var displayName = user.GetDisplayName();

        // Assert
        Assert.Equal(expectedName, displayName);
    }

    /// <summary>
    /// Tests retrieval of display name from 'name' claim fallback.
    /// </summary>
    [Fact]
    public void GetDisplayName_WithNameClaim_ReturnsDisplayName()
    {
        // Arrange
        var expectedName = "Another User";
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "urn:mvn:user:test-123"),
            new Claim("name", expectedName)
        };
        var identity = new ClaimsIdentity(claims, "jwt");
        var user = new ClaimsPrincipal(identity);

        // Act
        var displayName = user.GetDisplayName();

        // Assert
        Assert.Equal(expectedName, displayName);
    }

    /// <summary>
    /// Tests that GetDisplayName returns default value when no name claim exists.
    /// </summary>
    [Fact]
    public void GetDisplayName_WithoutNameClaim_ReturnsDefaultValue()
    {
        // Arrange
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "urn:mvn:user:test-123")
        };
        var identity = new ClaimsIdentity(claims, "jwt");
        var user = new ClaimsPrincipal(identity);

        // Act
        var displayName = user.GetDisplayName();

        // Assert
        Assert.Equal("User", displayName);
    }

    /// <summary>
    /// Tests that GetDisplayName returns default value for unauthenticated user.
    /// </summary>
    [Fact]
    public void GetDisplayName_WithUnauthenticatedUser_ReturnsDefaultValue()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity());

        // Act
        var displayName = user.GetDisplayName();

        // Assert
        Assert.Equal("User", displayName);
    }

    #endregion

    #region GetScopes Tests

    /// <summary>
    /// Tests retrieval of multiple scopes from scope claim.
    /// </summary>
    [Fact]
    public void GetScopes_WithMultipleScopes_ReturnsAllScopes()
    {
        // Arrange
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "urn:mvn:user:test-123"),
            new Claim("scope", "mvn:admin mvn:ingest mvn:read")
        };
        var identity = new ClaimsIdentity(claims, "jwt");
        var user = new ClaimsPrincipal(identity);

        // Act
        var scopes = user.GetScopes();

        // Assert
        Assert.Equal(3, scopes.Length);
        Assert.Contains("mvn:admin", scopes);
        Assert.Contains("mvn:ingest", scopes);
        Assert.Contains("mvn:read", scopes);
    }

    /// <summary>
    /// Tests retrieval of single scope.
    /// </summary>
    [Fact]
    public void GetScopes_WithSingleScope_ReturnsSingleScope()
    {
        // Arrange
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "urn:mvn:user:test-123"),
            new Claim("scope", "mvn:read")
        };
        var identity = new ClaimsIdentity(claims, "jwt");
        var user = new ClaimsPrincipal(identity);

        // Act
        var scopes = user.GetScopes();

        // Assert
        Assert.Single(scopes);
        Assert.Equal("mvn:read", scopes[0]);
    }

    /// <summary>
    /// Tests that GetScopes returns empty array when no scope claim exists.
    /// </summary>
    [Fact]
    public void GetScopes_WithoutScopeClaim_ReturnsEmptyArray()
    {
        // Arrange
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "urn:mvn:user:test-123")
        };
        var identity = new ClaimsIdentity(claims, "jwt");
        var user = new ClaimsPrincipal(identity);

        // Act
        var scopes = user.GetScopes();

        // Assert
        Assert.Empty(scopes);
    }

    /// <summary>
    /// Tests that GetScopes returns empty array for unauthenticated user.
    /// </summary>
    [Fact]
    public void GetScopes_WithUnauthenticatedUser_ReturnsEmptyArray()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity());

        // Act
        var scopes = user.GetScopes();

        // Assert
        Assert.Empty(scopes);
    }

    #endregion

    #region HasScope Tests

    /// <summary>
    /// Tests that HasScope returns true when user has the specified scope.
    /// </summary>
    [Fact]
    public void HasScope_WithMatchingScope_ReturnsTrue()
    {
        // Arrange
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "urn:mvn:user:test-123"),
            new Claim("scope", "mvn:admin mvn:ingest")
        };
        var identity = new ClaimsIdentity(claims, "jwt");
        var user = new ClaimsPrincipal(identity);

        // Act
        var hasAdminScope = user.HasScope("mvn:admin");
        var hasIngestScope = user.HasScope("mvn:ingest");

        // Assert
        Assert.True(hasAdminScope);
        Assert.True(hasIngestScope);
    }

    /// <summary>
    /// Tests that HasScope returns false when user does not have the specified scope.
    /// </summary>
    [Fact]
    public void HasScope_WithoutMatchingScope_ReturnsFalse()
    {
        // Arrange
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "urn:mvn:user:test-123"),
            new Claim("scope", "mvn:read")
        };
        var identity = new ClaimsIdentity(claims, "jwt");
        var user = new ClaimsPrincipal(identity);

        // Act
        var hasAdminScope = user.HasScope("mvn:admin");

        // Assert
        Assert.False(hasAdminScope);
    }

    /// <summary>
    /// Tests that HasScope is case-insensitive.
    /// </summary>
    [Fact]
    public void HasScope_IsCaseInsensitive()
    {
        // Arrange
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "urn:mvn:user:test-123"),
            new Claim("scope", "mvn:admin")
        };
        var identity = new ClaimsIdentity(claims, "jwt");
        var user = new ClaimsPrincipal(identity);

        // Act
        var hasScope = user.HasScope("MVN:ADMIN");

        // Assert
        Assert.True(hasScope);
    }

    /// <summary>
    /// Tests that HasScope returns false for empty scope parameter.
    /// </summary>
    [Fact]
    public void HasScope_WithEmptyScope_ReturnsFalse()
    {
        // Arrange
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "urn:mvn:user:test-123"),
            new Claim("scope", "mvn:admin")
        };
        var identity = new ClaimsIdentity(claims, "jwt");
        var user = new ClaimsPrincipal(identity);

        // Act
        var hasScope = user.HasScope("");

        // Assert
        Assert.False(hasScope);
    }

    #endregion
}
