using MehguViewer.Core.Backend.Services;
using Xunit;

namespace MehguViewer.Core.Tests;

/// <summary>
/// Unit tests for the AuthService.
/// Tests password hashing, validation, and token generation.
/// </summary>
[Trait("Category", "Unit")]
[Trait("Service", "Auth")]
public class AuthServiceTests
{
    #region Password Validation

    [Theory]
    [InlineData("SecureP@ss1", true)]
    [InlineData("MyPassword123", true)]
    [InlineData("ValidP4ssword!", true)]
    public void ValidatePasswordStrength_ValidPasswords_ReturnsValid(string password, bool expectedValid)
    {
        // Act
        var (isValid, _) = AuthService.ValidatePasswordStrength(password);

        // Assert
        Assert.Equal(expectedValid, isValid);
    }

    [Theory]
    [InlineData("short", "Password must be at least 8 characters")]
    [InlineData("", "Password is required")]
    [InlineData("alllowercase123", "Password must contain uppercase, lowercase, and a number")]
    [InlineData("ALLUPPERCASE123", "Password must contain uppercase, lowercase, and a number")]
    [InlineData("NoDigitsHere!", "Password must contain uppercase, lowercase, and a number")]
    public void ValidatePasswordStrength_InvalidPasswords_ReturnsError(string password, string expectedError)
    {
        // Act
        var (isValid, error) = AuthService.ValidatePasswordStrength(password);

        // Assert
        Assert.False(isValid);
        Assert.Contains(expectedError, error ?? "");
    }

    [Fact]
    public void ValidatePasswordStrength_TooLongPassword_ReturnsError()
    {
        // Arrange
        var password = new string('A', 129) + "a1"; // 131 chars

        // Act
        var (isValid, error) = AuthService.ValidatePasswordStrength(password);

        // Assert
        Assert.False(isValid);
        Assert.Contains("less than 128", error ?? "");
    }

    #endregion

    #region Password Hashing

    [Fact]
    public void HashPassword_ReturnsValidBcryptHash()
    {
        // Arrange
        var password = "TestPassword123";

        // Act
        var hash = AuthService.HashPassword(password);

        // Assert
        Assert.StartsWith("$2", hash); // BCrypt hash prefix
        Assert.NotEqual(password, hash);
    }

    [Fact]
    public void HashPassword_SamePasswordDifferentHashes()
    {
        // Arrange
        var password = "TestPassword123";

        // Act
        var hash1 = AuthService.HashPassword(password);
        var hash2 = AuthService.HashPassword(password);

        // Assert - Different salts produce different hashes
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void VerifyPassword_CorrectPassword_ReturnsTrue()
    {
        // Arrange
        var password = "TestPassword123";
        var hash = AuthService.HashPassword(password);

        // Act
        var result = AuthService.VerifyPassword(password, hash);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void VerifyPassword_WrongPassword_ReturnsFalse()
    {
        // Arrange
        var password = "TestPassword123";
        var wrongPassword = "WrongPassword123";
        var hash = AuthService.HashPassword(password);

        // Act
        var result = AuthService.VerifyPassword(wrongPassword, hash);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region Hash Migration

    [Fact]
    public void NeedsRehash_BcryptHash_ReturnsFalse()
    {
        // Arrange
        var bcryptHash = AuthService.HashPassword("SomePassword123");

        // Act
        var needsRehash = AuthService.NeedsRehash(bcryptHash);

        // Assert
        Assert.False(needsRehash);
    }

    [Fact]
    public void NeedsRehash_NonBcryptHash_ReturnsTrue()
    {
        // Arrange - A legacy SHA256 hash (base64 encoded)
        var legacyHash = "ABC123XYZ=";

        // Act
        var needsRehash = AuthService.NeedsRehash(legacyHash);

        // Assert
        Assert.True(needsRehash);
    }

    #endregion
}
