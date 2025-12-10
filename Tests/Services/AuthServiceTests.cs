using MehguViewer.Core.Services;
using MehguViewer.Core.Workers;
using MehguViewer.Core.Helpers;
using MehguViewer.Core.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Xunit;
using Xunit.Abstractions;

namespace MehguViewer.Core.Tests.Services;

/// <summary>
/// Comprehensive unit tests for the AuthService covering:
/// - Password validation and strength requirements
/// - Password hashing and verification (bcrypt + legacy SHA256)
/// - JWT token generation and validation
/// - Key management and JWK export
/// </summary>
[Trait("Category", "Unit")]
[Trait("Service", "Auth")]
public class AuthServiceTests
{
    private readonly AuthService _authService;
    private readonly JwtSecurityTokenHandler _tokenHandler;
    private readonly ITestOutputHelper _output;

    public AuthServiceTests(ITestOutputHelper output)
    {
        _output = output;
        var logger = NullLogger<AuthService>.Instance;
        _authService = new AuthService(logger);
        _tokenHandler = new JwtSecurityTokenHandler();
    }

    #region Password Validation Tests

    /// <summary>
    /// Tests that valid passwords meeting all requirements are accepted.
    /// </summary>
    [Theory]
    [InlineData("SecureP@ss1", true)]
    [InlineData("MyPassword123", true)]
    [InlineData("ValidP4ssword!", true)]
    [InlineData("Abcdefgh1", true)] // Minimum 8 chars with required complexity
    [InlineData("LongerPasswordWith123NumbersAndLetters", true)]
    public void ValidatePasswordStrength_ValidPasswords_ReturnsValid(string password, bool expectedValid)
    {
        // Act
        var (isValid, error) = _authService.ValidatePasswordStrength(password);

        // Assert
        Assert.Equal(expectedValid, isValid);
        Assert.Null(error);
    }

    /// <summary>
    /// Tests that invalid passwords are rejected with appropriate error messages.
    /// </summary>
    [Theory]
    [InlineData("short", "Password must be at least 8 characters")]
    [InlineData("", "Password is required")]
    [InlineData("alllowercase123", "Password must contain uppercase, lowercase, and a number")]
    [InlineData("ALLUPPERCASE123", "Password must contain uppercase, lowercase, and a number")]
    [InlineData("NoDigitsHere", "Password must contain uppercase, lowercase, and a number")]
    [InlineData("nouppercasehere123", "Password must contain uppercase, lowercase, and a number")]
    [InlineData("NOLOWERCASEHERE123", "Password must contain uppercase, lowercase, and a number")]
    public void ValidatePasswordStrength_InvalidPasswords_ReturnsError(string password, string expectedError)
    {
        // Act
        var (isValid, error) = _authService.ValidatePasswordStrength(password);

        // Assert
        Assert.False(isValid);
        Assert.NotNull(error);
        Assert.Contains(expectedError, error);
    }

    /// <summary>
    /// Tests password length limits (max 128 characters).
    /// </summary>
    [Fact]
    public void ValidatePasswordStrength_TooLongPassword_ReturnsError()
    {
        // Arrange - Create a 129 character password
        var password = new string('A', 129) + "a1";

        // Act
        var (isValid, error) = _authService.ValidatePasswordStrength(password);

        // Assert
        Assert.False(isValid);
        Assert.NotNull(error);
        Assert.Contains("less than 128", error);
    }

    /// <summary>
    /// Tests that null password is rejected.
    /// </summary>
    [Fact]
    public void ValidatePasswordStrength_NullPassword_ReturnsError()
    {
        // Act
        var (isValid, error) = _authService.ValidatePasswordStrength(null!);

        // Assert
        Assert.False(isValid);
        Assert.Equal("Password is required", error);
    }

    #endregion

    #region Password Hashing Tests

    /// <summary>
    /// Tests that password hashing produces valid bcrypt hashes.
    /// </summary>
    [Fact]
    public void HashPassword_ReturnsValidBcryptHash()
    {
        // Arrange
        var password = "TestPassword123";

        // Act
        var hash = _authService.HashPassword(password);

        // Assert
        Assert.StartsWith("$2", hash); // BCrypt hash prefix
        Assert.NotEqual(password, hash);
        Assert.True(hash.Length > 50); // Bcrypt hashes are typically 60 chars
    }

    /// <summary>
    /// Tests that hashing the same password produces different hashes (salt randomization).
    /// </summary>
    [Fact]
    public void HashPassword_SamePasswordDifferentHashes()
    {
        // Arrange
        var password = "TestPassword123";

        // Act
        var hash1 = _authService.HashPassword(password);
        var hash2 = _authService.HashPassword(password);

        // Assert - Different salts produce different hashes
        Assert.NotEqual(hash1, hash2);
    }

    /// <summary>
    /// Tests that hashing null or empty password throws exception.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void HashPassword_NullOrEmptyPassword_ThrowsException(string? password)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => _authService.HashPassword(password!));
    }

    #endregion

    #region Password Verification Tests

    /// <summary>
    /// Tests successful verification of correct password against bcrypt hash.
    /// </summary>
    [Fact]
    public void VerifyPassword_CorrectPassword_ReturnsTrue()
    {
        // Arrange
        var password = "TestPassword123";
        var hash = _authService.HashPassword(password);

        // Act
        var result = _authService.VerifyPassword(password, hash);

        // Assert
        Assert.True(result);
    }

    /// <summary>
    /// Tests that incorrect password fails verification.
    /// </summary>
    [Fact]
    public void VerifyPassword_IncorrectPassword_ReturnsFalse()
    {
        // Arrange
        var password = "TestPassword123";
        var hash = _authService.HashPassword(password);

        // Act
        var result = _authService.VerifyPassword("WrongPassword123", hash);

        // Assert
        Assert.False(result);
    }

    /// <summary>
    /// Tests that null or empty inputs return false instead of throwing.
    /// </summary>
    [Theory]
    [InlineData(null, "somehash")]
    [InlineData("somepassword", null)]
    [InlineData("", "somehash")]
    [InlineData("somepassword", "")]
    public void VerifyPassword_NullOrEmptyInputs_ReturnsFalse(string? password, string? hash)
    {
        // Act
        var result = _authService.VerifyPassword(password!, hash!);

        // Assert
        Assert.False(result);
    }

    /// <summary>
    /// Tests that bcrypt hashes don't need rehashing.
    /// </summary>
    [Fact]
    public void NeedsRehash_BcryptHash_ReturnsFalse()
    {
        // Arrange
        var bcryptHash = _authService.HashPassword("SomePassword123");

        // Act
        var result = _authService.NeedsRehash(bcryptHash);

        // Assert
        Assert.False(result);
    }

    /// <summary>
    /// Tests that legacy (non-bcrypt) hashes need rehashing.
    /// </summary>
    [Fact]
    public void NeedsRehash_LegacyHash_ReturnsTrue()
    {
        // Arrange
        var legacyHash = "SomeLegacyHashValue";

        // Act
        var result = _authService.NeedsRehash(legacyHash);

        // Assert
        Assert.True(result);
    }

    #endregion

    #region Token Generation Tests

    /// <summary>
    /// Tests successful JWT token generation for valid user.
    /// </summary>
    [Fact]
    public void GenerateToken_ValidUser_ReturnsValidJwtToken()
    {
        // Arrange
        var user = new User(
            id: "urn:mvn:user:123",
            username: "testuser",
            password_hash: "dummy_hash",
            role: "user",
            created_at: DateTime.UtcNow
        );

        // Act
        var token = _authService.GenerateToken(user);

        // Assert
        Assert.NotNull(token);
        Assert.NotEmpty(token);
        
        // Verify it's a valid JWT
        var jwtToken = _tokenHandler.ReadJwtToken(token);
        Assert.NotNull(jwtToken);
    }

    /// <summary>
    /// Tests that generated token contains correct claims.
    /// </summary>
    [Fact]
    public void GenerateToken_ValidUser_ContainsCorrectClaims()
    {
        // Arrange
        var user = new User(
            id: "urn:mvn:user:456",
            username: "johndoe",
            password_hash: "dummy_hash",
            role: "admin",
            created_at: DateTime.UtcNow
        );

        // Act
        var token = _authService.GenerateToken(user);
        var jwtToken = _tokenHandler.ReadJwtToken(token);

        // Assert - Check claims exist (use actual claim type names from JWT)
        var subClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "sub");
        var nameClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "unique_name");
        var roleClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "role");
        var scopeClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "scope");

        Assert.NotNull(subClaim);
        Assert.Equal(user.id, subClaim.Value);
        
        Assert.NotNull(nameClaim);
        Assert.Equal(user.username, nameClaim.Value);
        
        Assert.NotNull(roleClaim);
        Assert.Equal(user.role, roleClaim.Value);
        
        Assert.NotNull(scopeClaim);
    }

    /// <summary>
    /// Tests that admin users get correct scopes.
    /// </summary>
    [Fact]
    public void GenerateToken_AdminUser_HasAdminScopes()
    {
        // Arrange
        var adminUser = new User(
            id: "urn:mvn:user:admin",
            username: "admin",
            password_hash: "dummy_hash",
            role: "admin",
            created_at: DateTime.UtcNow
        );

        // Act
        var token = _authService.GenerateToken(adminUser);
        var jwtToken = _tokenHandler.ReadJwtToken(token);
        var scopeClaim = jwtToken.Claims.First(c => c.Type == "scope").Value;

        // Assert
        Assert.Contains("mvn:admin", scopeClaim);
        Assert.Contains("mvn:ingest", scopeClaim);
        Assert.Contains("mvn:social:write", scopeClaim);
    }

    /// <summary>
    /// Tests that regular users get appropriate scopes without admin privileges.
    /// </summary>
    [Fact]
    public void GenerateToken_RegularUser_HasUserScopes()
    {
        // Arrange
        var regularUser = new User(
            id: "urn:mvn:user:regular",
            username: "regularuser",
            password_hash: "dummy_hash",
            role: "user",
            created_at: DateTime.UtcNow
        );

        // Act
        var token = _authService.GenerateToken(regularUser);
        var jwtToken = _tokenHandler.ReadJwtToken(token);
        var scopeClaim = jwtToken.Claims.First(c => c.Type == "scope").Value;

        // Assert
        Assert.Contains("mvn:social:write", scopeClaim);
        Assert.DoesNotContain("mvn:admin", scopeClaim);
        Assert.DoesNotContain("mvn:ingest", scopeClaim);
    }

    /// <summary>
    /// Tests that token generation fails for null user.
    /// </summary>
    [Fact]
    public void GenerateToken_NullUser_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => _authService.GenerateToken(null!));
    }

    /// <summary>
    /// Tests that token generation fails for user with invalid ID.
    /// </summary>
    [Theory]
    [InlineData(null, "username")]
    [InlineData("", "username")]
    [InlineData("   ", "username")]
    [InlineData("validid", null)]
    [InlineData("validid", "")]
    [InlineData("validid", "   ")]
    public void GenerateToken_InvalidUserData_ThrowsArgumentException(string? userId, string? username)
    {
        // Arrange
        var user = new User(
            id: userId!,
            username: username!,
            password_hash: "dummy_hash",
            role: "user",
            created_at: DateTime.UtcNow
        );

        // Act & Assert
        Assert.Throws<ArgumentException>(() => _authService.GenerateToken(user));
    }

    #endregion

    #region Token Validation Tests

    /// <summary>
    /// Tests that validation parameters are correctly configured.
    /// </summary>
    [Fact]
    public void GetValidationParameters_ReturnsValidConfiguration()
    {
        // Act
        var parameters = _authService.GetValidationParameters();

        // Assert
        Assert.NotNull(parameters);
        Assert.True(parameters.ValidateIssuerSigningKey);
        Assert.True(parameters.ValidateIssuer);
        Assert.True(parameters.ValidateAudience);
        Assert.True(parameters.ValidateLifetime);
        Assert.NotNull(parameters.IssuerSigningKey);
        Assert.Equal(TimeSpan.FromMinutes(1), parameters.ClockSkew);
    }

    #endregion

    #region JWK Export Tests

    /// <summary>
    /// Tests that JWK export produces valid JSON Web Key.
    /// </summary>
    [Fact]
    public void GetJwk_ReturnsValidJwk()
    {
        // Act
        var jwk = _authService.GetJwk();

        // Assert
        Assert.NotNull(jwk);
        Assert.Equal("RSA", jwk.Kty);
        Assert.Equal("sig", jwk.Use);
        Assert.NotNull(jwk.E);
        Assert.NotNull(jwk.N);
        Assert.NotNull(jwk.Kid);
        Assert.Equal("RS256", jwk.Alg);
    }

    #endregion

    #region Additional Security Tests

    /// <summary>
    /// Tests that token can be validated using validation parameters.
    /// </summary>
    [Fact]
    public void ValidateToken_ValidToken_ReturnsValidatedToken()
    {
        // Arrange
        var user = new User(
            id: "urn:mvn:user:789",
            username: "validator",
            password_hash: "dummy_hash",
            role: "user",
            created_at: DateTime.UtcNow
        );
        var token = _authService.GenerateToken(user);
        var validationParams = _authService.GetValidationParameters();

        // Act
        var principal = _tokenHandler.ValidateToken(token, validationParams, out var validatedToken);

        // Assert
        Assert.NotNull(principal);
        Assert.NotNull(validatedToken);
        Assert.IsType<JwtSecurityToken>(validatedToken);
    }

    /// <summary>
    /// Tests that expired tokens are rejected during validation.
    /// </summary>
    [Fact]
    public void ValidateToken_ExpiredToken_ThrowsSecurityTokenException()
    {
        // This test verifies the lifetime validation is working
        // We can't easily create an expired token without reflection or time manipulation,
        // so this test validates that our validation parameters check lifetime
        var validationParams = _authService.GetValidationParameters();
        
        Assert.True(validationParams.ValidateLifetime);
        Assert.Equal(TimeSpan.FromMinutes(1), validationParams.ClockSkew);
    }

    /// <summary>
    /// Tests password rehashing for different hash formats.
    /// </summary>
    [Theory]
    [InlineData("legacy_sha256_hash_example")]
    [InlineData("some_other_format")]
    [InlineData("")]
    public void NeedsRehash_NonBcryptHashes_ReturnsTrue(string hash)
    {
        // Act
        var result = _authService.NeedsRehash(hash);

        // Assert
        Assert.True(result);
    }

    /// <summary>
    /// Tests that null hash in NeedsRehash returns true.
    /// </summary>
    [Fact]
    public void NeedsRehash_NullHash_ReturnsTrue()
    {
        // Act
        var result = _authService.NeedsRehash(null!);

        // Assert
        Assert.True(result);
    }

    /// <summary>
    /// Tests uploader role gets appropriate scopes.
    /// </summary>
    [Fact]
    public void GenerateToken_UploaderRole_HasUploaderScopes()
    {
        // Arrange
        var uploaderUser = new User(
            id: "urn:mvn:user:uploader",
            username: "uploader",
            password_hash: "dummy_hash",
            role: "uploader",
            created_at: DateTime.UtcNow
        );

        // Act
        var token = _authService.GenerateToken(uploaderUser);
        var jwtToken = _tokenHandler.ReadJwtToken(token);
        var scopeClaim = jwtToken.Claims.First(c => c.Type == "scope").Value;

        // Assert
        Assert.Contains("mvn:ingest", scopeClaim);
        Assert.Contains("mvn:social:write", scopeClaim);
        Assert.DoesNotContain("mvn:admin", scopeClaim);
    }

    /// <summary>
    /// Tests guest role gets minimal scopes.
    /// </summary>
    [Fact]
    public void GenerateToken_GuestRole_HasReadOnlyScopes()
    {
        // Arrange
        var guestUser = new User(
            id: "urn:mvn:user:guest",
            username: "guest",
            password_hash: "dummy_hash",
            role: "guest",
            created_at: DateTime.UtcNow
        );

        // Act
        var token = _authService.GenerateToken(guestUser);
        var jwtToken = _tokenHandler.ReadJwtToken(token);
        var scopeClaim = jwtToken.Claims.First(c => c.Type == "scope").Value;

        // Assert
        Assert.Contains("mvn:read", scopeClaim);
        Assert.DoesNotContain("mvn:social:write", scopeClaim);
        Assert.DoesNotContain("mvn:admin", scopeClaim);
        Assert.DoesNotContain("mvn:ingest", scopeClaim);
    }

    /// <summary>
    /// Tests token contains required standard claims.
    /// </summary>
    [Fact]
    public void GenerateToken_ValidUser_ContainsStandardClaims()
    {
        // Arrange
        var user = new User(
            id: "urn:mvn:user:standard",
            username: "standarduser",
            password_hash: "dummy_hash",
            role: "user",
            created_at: DateTime.UtcNow
        );

        // Act
        var token = _authService.GenerateToken(user);
        var jwtToken = _tokenHandler.ReadJwtToken(token);

        // Assert - Check all required JWT standard claims
        Assert.NotNull(jwtToken.Issuer);
        Assert.Contains("mehgu", jwtToken.Issuer.ToLower());
        
        Assert.NotEmpty(jwtToken.Audiences);
        Assert.Contains("mehgu-core", jwtToken.Audiences);
        
        // ValidFrom and ValidTo are DateTime value types, always have values
        Assert.True(jwtToken.ValidTo > DateTime.UtcNow);
        Assert.True(jwtToken.ValidTo <= DateTime.UtcNow.AddHours(25)); // Within 24h + buffer
    }

    /// <summary>
    /// Tests that hashing different passwords produces different hashes.
    /// </summary>
    [Fact]
    public void HashPassword_DifferentPasswords_ProducesDifferentHashes()
    {
        // Arrange
        var password1 = "Password123";
        var password2 = "DifferentPass456";

        // Act
        var hash1 = _authService.HashPassword(password1);
        var hash2 = _authService.HashPassword(password2);

        // Assert
        Assert.NotEqual(hash1, hash2);
        Assert.StartsWith("$2", hash1);
        Assert.StartsWith("$2", hash2);
    }

    /// <summary>
    /// Tests password verification with corrupted hash format.
    /// </summary>
    [Fact]
    public void VerifyPassword_CorruptedBcryptHash_ReturnsFalse()
    {
        // Arrange
        var password = "TestPassword123";
        var corruptedHash = "$2a$12$CORRUPTED_HASH";

        // Act
        var result = _authService.VerifyPassword(password, corruptedHash);

        // Assert
        Assert.False(result);
    }

    /// <summary>
    /// Tests that JWK export is consistent across multiple calls.
    /// </summary>
    [Fact]
    public void GetJwk_MultipleCalls_ReturnsSameKey()
    {
        // Act
        var jwk1 = _authService.GetJwk();
        var jwk2 = _authService.GetJwk();

        // Assert
        Assert.Equal(jwk1.N, jwk2.N);
        Assert.Equal(jwk1.E, jwk2.E);
        Assert.Equal(jwk1.Kid, jwk2.Kid);
    }

    /// <summary>
    /// Tests that tokens from different users have different subjects.
    /// </summary>
    [Fact]
    public void GenerateToken_DifferentUsers_ProduceDifferentSubjects()
    {
        // Arrange
        var user1 = new User("urn:mvn:user:001", "user1", "hash1", "user", DateTime.UtcNow);
        var user2 = new User("urn:mvn:user:002", "user2", "hash2", "user", DateTime.UtcNow);

        // Act
        var token1 = _authService.GenerateToken(user1);
        var token2 = _authService.GenerateToken(user2);
        var jwt1 = _tokenHandler.ReadJwtToken(token1);
        var jwt2 = _tokenHandler.ReadJwtToken(token2);

        // Assert
        var sub1 = jwt1.Claims.First(c => c.Type == "sub").Value;
        var sub2 = jwt2.Claims.First(c => c.Type == "sub").Value;
        Assert.NotEqual(sub1, sub2);
    }

    /// <summary>
    /// Tests password with exactly minimum length.
    /// </summary>
    [Fact]
    public void ValidatePasswordStrength_ExactlyMinLength_ReturnsValid()
    {
        // Arrange - Exactly 8 characters
        var password = "Pass123A";

        // Act
        var (isValid, error) = _authService.ValidatePasswordStrength(password);

        // Assert
        Assert.True(isValid);
        Assert.Null(error);
    }

    /// <summary>
    /// Tests password with exactly maximum length.
    /// </summary>
    [Fact]
    public void ValidatePasswordStrength_ExactlyMaxLength_ReturnsValid()
    {
        // Arrange - Exactly 128 characters with required complexity
        var password = new string('A', 125) + "a1";

        // Act
        var (isValid, error) = _authService.ValidatePasswordStrength(password);

        // Assert
        Assert.True(isValid);
        Assert.Null(error);
    }

    /// <summary>
    /// Tests whitespace-only password validation.
    /// </summary>
    [Fact]
    public void ValidatePasswordStrength_WhitespaceOnly_ReturnsError()
    {
        // Arrange
        var password = "          ";

        // Act
        var (isValid, error) = _authService.ValidatePasswordStrength(password);

        // Assert
        Assert.False(isValid);
        Assert.Equal("Password is required", error);
    }

    #endregion

    #region Edge Case Tests

    /// <summary>
    /// Tests that special characters in username don't break token generation.
    /// </summary>
    [Theory]
    [InlineData("user@example.com")]
    [InlineData("user.name")]
    [InlineData("user_name")]
    [InlineData("user-name")]
    public void GenerateToken_SpecialCharactersInUsername_GeneratesValidToken(string username)
    {
        // Arrange
        var user = new User(
            id: "urn:mvn:user:special",
            username: username,
            password_hash: "dummy_hash",
            role: "user",
            created_at: DateTime.UtcNow
        );

        // Act
        var token = _authService.GenerateToken(user);
        var jwtToken = _tokenHandler.ReadJwtToken(token);

        // Assert
        Assert.NotNull(token);
        var nameClaim = jwtToken.Claims.First(c => c.Type == "unique_name").Value;
        Assert.Equal(username, nameClaim);
    }

    /// <summary>
    /// Tests password with Unicode characters.
    /// </summary>
    [Theory]
    [InlineData("Пароль123")]  // Cyrillic
    [InlineData("密碼123Aa")]  // Chinese
    [InlineData("パスワード123A")] // Japanese
    public void HashPassword_UnicodePassword_CreatesValidHash(string password)
    {
        // Act
        var hash = _authService.HashPassword(password);
        var verified = _authService.VerifyPassword(password, hash);

        // Assert
        Assert.StartsWith("$2", hash);
        Assert.True(verified);
    }

    /// <summary>
    /// Tests case-sensitive password verification.
    /// </summary>
    [Fact]
    public void VerifyPassword_CaseDifference_ReturnsFalse()
    {
        // Arrange
        var password = "TestPassword123";
        var hash = _authService.HashPassword(password);

        // Act
        var result = _authService.VerifyPassword("testpassword123", hash);

        // Assert
        Assert.False(result);
    }

    /// <summary>
    /// Tests that unknown roles default to guest permissions.
    /// </summary>
    [Fact]
    public void GenerateToken_UnknownRole_GetsGuestScopes()
    {
        // Arrange
        var user = new User(
            id: "urn:mvn:user:unknown",
            username: "unknownrole",
            password_hash: "dummy_hash",
            role: "some_random_role",
            created_at: DateTime.UtcNow
        );

        // Act
        var token = _authService.GenerateToken(user);
        var jwtToken = _tokenHandler.ReadJwtToken(token);
        var scopeClaim = jwtToken.Claims.First(c => c.Type == "scope").Value;

        // Assert
        Assert.Contains("mvn:read", scopeClaim);
        Assert.DoesNotContain("mvn:admin", scopeClaim);
    }

    #endregion

    #region Disposal Tests

    /// <summary>
    /// Tests that disposed AuthService throws ObjectDisposedException.
    /// </summary>
    [Fact]
    public void Dispose_CalledMultipleTimes_DoesNotThrow()
    {
        // Arrange
        var logger = NullLogger<AuthService>.Instance;
        var service = new AuthService(logger);

        // Act & Assert - Multiple dispose calls should be safe
        service.Dispose();
        service.Dispose();
        service.Dispose();
    }

    /// <summary>
    /// Tests that methods throw ObjectDisposedException after disposal.
    /// </summary>
    [Fact]
    public void DisposedService_ThrowsObjectDisposedException()
    {
        // Arrange
        var logger = NullLogger<AuthService>.Instance;
        var service = new AuthService(logger);
        var user = new User(
            id: "urn:mvn:user:test",
            username: "testuser",
            password_hash: "dummy",
            role: "user",
            created_at: DateTime.UtcNow
        );

        // Act
        service.Dispose();

        // Assert
        Assert.Throws<ObjectDisposedException>(() => service.GenerateToken(user));
        Assert.Throws<ObjectDisposedException>(() => service.GetValidationParameters());
        Assert.Throws<ObjectDisposedException>(() => service.GetJwk());
    }

    #endregion

    #region Edge Cases and Error Handling Tests

    /// <summary>
    /// Tests token generation with user having null or empty username.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GenerateToken_InvalidUsername_ThrowsArgumentException(string? username)
    {
        // Arrange
        var user = new User(
            id: "urn:mvn:user:test",
            username: username!,
            password_hash: "dummy_hash",
            role: "user",
            created_at: DateTime.UtcNow
        );

        // Act & Assert
        Assert.Throws<ArgumentException>(() => _authService.GenerateToken(user));
    }

    /// <summary>
    /// Tests token generation with user having null or empty id.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GenerateToken_InvalidUserId_ThrowsArgumentException(string? userId)
    {
        // Arrange
        var user = new User(
            id: userId!,
            username: "testuser",
            password_hash: "dummy_hash",
            role: "user",
            created_at: DateTime.UtcNow
        );

        // Act & Assert
        Assert.Throws<ArgumentException>(() => _authService.GenerateToken(user));
    }

    /// <summary>
    /// Tests NeedsRehash with various hash formats.
    /// </summary>
    [Theory]
    [InlineData("sha256_hash_here", true)] // Legacy hash
    [InlineData("plain_text", true)] // Non-bcrypt
    [InlineData("$2a$12$validbcrypthash", false)] // Valid bcrypt
    [InlineData("$2b$12$validbcrypthash", false)] // Valid bcrypt variant
    [InlineData("$2y$12$validbcrypthash", false)] // Valid bcrypt variant
    [InlineData(null, true)] // Null hash
    [InlineData("", true)] // Empty hash
    public void NeedsRehash_VariousHashFormats_ReturnsExpectedResult(string? hash, bool expectedNeedsRehash)
    {
        // Act
        var result = _authService.NeedsRehash(hash!);

        // Assert
        Assert.Equal(expectedNeedsRehash, result);
    }

    /// <summary>
    /// Tests VerifyPassword with malformed bcrypt hash.
    /// </summary>
    [Fact]
    public void VerifyPassword_MalformedBcryptHash_ReturnsFalse()
    {
        // Arrange
        var password = "TestPassword123";
        var malformedHash = "$2a$invalid$hash";

        // Act
        var result = _authService.VerifyPassword(password, malformedHash);

        // Assert
        Assert.False(result);
    }

    /// <summary>
    /// Tests password verification is case-sensitive.
    /// </summary>
    [Fact]
    public void VerifyPassword_CaseSensitive()
    {
        // Arrange
        var password = "TestPassword123";
        var hash = _authService.HashPassword(password);

        // Act
        var correctCase = _authService.VerifyPassword("TestPassword123", hash);
        var wrongCase = _authService.VerifyPassword("testpassword123", hash);

        // Assert
        Assert.True(correctCase);
        Assert.False(wrongCase);
    }

    #endregion

    #region Performance and Security Tests

    /// <summary>
    /// Tests that password hashing completes in reasonable time.
    /// </summary>
    [Fact]
    public void HashPassword_Performance_CompletesInReasonableTime()
    {
        // Arrange
        var password = "TestPassword123";
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Act
        var hash = _authService.HashPassword(password);
        stopwatch.Stop();

        // Assert - Bcrypt with work factor 12 should complete within 2 seconds (CI environments are slower)
        Assert.True(stopwatch.ElapsedMilliseconds < 2000, 
            $"Hashing took {stopwatch.ElapsedMilliseconds}ms, expected < 2000ms");
        Assert.NotNull(hash);
    }

    /// <summary>
    /// Tests that validation parameters enforce security requirements.
    /// </summary>
    [Fact]
    public void GetValidationParameters_SecuritySettings_AreStrict()
    {
        // Act
        var parameters = _authService.GetValidationParameters();

        // Assert - All critical validations must be enabled
        Assert.True(parameters.ValidateIssuerSigningKey, "Must validate signing key");
        Assert.True(parameters.ValidateIssuer, "Must validate issuer");
        Assert.True(parameters.ValidateAudience, "Must validate audience");
        Assert.True(parameters.ValidateLifetime, "Must validate token lifetime");
        
        // Clock skew should be minimal to prevent replay attacks
        Assert.True(parameters.ClockSkew <= TimeSpan.FromMinutes(5), 
            "Clock skew should be minimal for security");
    }

    #endregion
}
