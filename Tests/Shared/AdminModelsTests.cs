using System.ComponentModel.DataAnnotations;
using MehguViewer.Core.Shared;
using Xunit;

namespace Tests.Shared;

/// <summary>
/// Unit tests for AdminModels record validations and business logic.
/// Ensures data integrity and security of administrative operations.
/// </summary>
public class AdminModelsTests
{
    #region AdminPasswordRequest Tests

    [Fact]
    public void AdminPasswordRequest_ValidPassword_PassesValidation()
    {
        // Arrange
        var request = new AdminPasswordRequest("SecurePassword123!");

        // Act
        var results = ValidateModel(request);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void AdminPasswordRequest_EmptyPassword_FailsValidation()
    {
        // Arrange
        var request = new AdminPasswordRequest("");

        // Act
        var results = ValidateModel(request);

        // Assert
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.ErrorMessage!.Contains("Password is required") || r.ErrorMessage!.Contains("Password cannot be empty"));
    }

    #endregion

    #region DatabaseConfig Tests

    [Fact]
    public void DatabaseConfig_ValidConfiguration_PassesValidation()
    {
        // Arrange
        var config = new DatabaseConfig(
            host: "localhost",
            port: 5432,
            database: "mehguviewer",
            username: "admin",
            password: "SecurePass123"
        );

        // Act
        var results = ValidateModel(config);

        // Assert
        Assert.Empty(results);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    [InlineData(70000)]
    public void DatabaseConfig_InvalidPort_FailsValidation(int invalidPort)
    {
        // Arrange
        var config = new DatabaseConfig(
            host: "localhost",
            port: invalidPort,
            database: "mehguviewer",
            username: "admin",
            password: "SecurePass123"
        );

        // Act
        var results = ValidateModel(config);

        // Assert
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.ErrorMessage!.Contains("Port must be between 1 and 65535"));
    }

    [Fact]
    public void DatabaseConfig_MissingRequiredFields_FailsValidation()
    {
        // Arrange - using null coercion to test validation
        var config = new DatabaseConfig(
            host: "",
            port: 5432,
            database: "",
            username: "",
            password: ""
        );

        // Act
        var results = ValidateModel(config);

        // Assert
        Assert.NotEmpty(results);
    }

    #endregion

    #region StorageSettingsUpdate Tests

    [Fact]
    public void StorageSettingsUpdate_ValidSettings_PassesValidation()
    {
        // Arrange
        var update = new StorageSettingsUpdate(
            thumbnail_size: 200,
            web_size: 1920,
            jpeg_quality: 85
        );

        // Act
        var results = ValidateModel(update);

        // Assert
        Assert.Empty(results);
    }

    [Theory]
    [InlineData(49)]   // Below minimum
    [InlineData(501)]  // Above maximum
    public void StorageSettingsUpdate_InvalidThumbnailSize_FailsValidation(int size)
    {
        // Arrange
        var update = new StorageSettingsUpdate(
            thumbnail_size: size,
            web_size: 1920,
            jpeg_quality: 85
        );

        // Act
        var results = ValidateModel(update);

        // Assert
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.ErrorMessage!.Contains("Thumbnail size must be between"));
    }

    [Theory]
    [InlineData(0)]    // Below minimum
    [InlineData(101)]  // Above maximum
    public void StorageSettingsUpdate_InvalidJpegQuality_FailsValidation(int quality)
    {
        // Arrange
        var update = new StorageSettingsUpdate(
            thumbnail_size: 200,
            web_size: 1920,
            jpeg_quality: quality
        );

        // Act
        var results = ValidateModel(update);

        // Assert
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.ErrorMessage!.Contains("JPEG quality must be between"));
    }

    #endregion

    #region AuthConfig Tests

    [Fact]
    public void AuthConfig_ValidConfiguration_PassesValidation()
    {
        // Arrange
        var config = new AuthConfig(
            registration_open: true,
            max_login_attempts: 5,
            lockout_duration_minutes: 15,
            token_expiry_hours: 24,
            cloudflare: new CloudflareConfig(
                enabled: true,
                turnstile_site_key: "test-site-key",
                turnstile_secret_key: "test-secret-key"
            ),
            require_2fa_passkey: false,
            require_password_for_danger_zone: true
        );

        // Act
        var results = ValidateModel(config);

        // Assert
        Assert.Empty(results);
    }

    [Theory]
    [InlineData(0)]    // Below minimum
    [InlineData(101)]  // Above maximum
    public void AuthConfig_InvalidMaxLoginAttempts_FailsValidation(int attempts)
    {
        // Arrange
        var config = new AuthConfig(
            registration_open: true,
            max_login_attempts: attempts,
            lockout_duration_minutes: 15,
            token_expiry_hours: 24,
            cloudflare: new CloudflareConfig(true, "site", "secret"),
            require_2fa_passkey: false,
            require_password_for_danger_zone: true
        );

        // Act
        var results = ValidateModel(config);

        // Assert
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.ErrorMessage!.Contains("Max login attempts"));
    }

    [Theory]
    [InlineData(0)]     // Below minimum
    [InlineData(1441)]  // Above maximum (24 hours)
    public void AuthConfig_InvalidLockoutDuration_FailsValidation(int minutes)
    {
        // Arrange
        var config = new AuthConfig(
            registration_open: true,
            max_login_attempts: 5,
            lockout_duration_minutes: minutes,
            token_expiry_hours: 24,
            cloudflare: new CloudflareConfig(true, "site", "secret"),
            require_2fa_passkey: false,
            require_password_for_danger_zone: true
        );

        // Act
        var results = ValidateModel(config);

        // Assert
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.ErrorMessage!.Contains("Lockout duration"));
    }

    #endregion

    #region LoginRequestWithCf Tests

    [Fact]
    public void LoginRequestWithCf_ValidRequest_PassesValidation()
    {
        // Arrange
        var request = new LoginRequestWithCf(
            username: "testuser",
            password: "SecurePass123",
            cf_turnstile_token: "valid-token"
        );

        // Act
        var results = ValidateModel(request);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void LoginRequestWithCf_EmptyUsername_FailsValidation()
    {
        // Arrange
        var request = new LoginRequestWithCf(
            username: "",
            password: "SecurePass123",
            cf_turnstile_token: null
        );

        // Act
        var results = ValidateModel(request);

        // Assert
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.ErrorMessage!.Contains("Username"));
    }

    #endregion

    #region RegisterRequestWithCf Tests

    [Fact]
    public void RegisterRequestWithCf_ValidRequest_PassesValidation()
    {
        // Arrange
        var request = new RegisterRequestWithCf(
            username: "newuser",
            password: "SecurePass123!",
            cf_turnstile_token: "valid-token"
        );

        // Act
        var results = ValidateModel(request);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void RegisterRequestWithCf_ShortUsername_FailsValidation()
    {
        // Arrange
        var request = new RegisterRequestWithCf(
            username: "ab",  // Too short
            password: "SecurePass123!",
            cf_turnstile_token: null
        );

        // Act
        var results = ValidateModel(request);

        // Assert
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.ErrorMessage!.Contains("Username must be at least 3 characters"));
    }

    [Fact]
    public void RegisterRequestWithCf_ShortPassword_FailsValidation()
    {
        // Arrange
        var request = new RegisterRequestWithCf(
            username: "newuser",
            password: "short",  // Too short
            cf_turnstile_token: null
        );

        // Act
        var results = ValidateModel(request);

        // Assert
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.ErrorMessage!.Contains("Password must be at least 8 characters"));
    }

    #endregion

    #region PasskeyVerificationData Tests

    [Fact]
    public void PasskeyVerificationData_ValidData_PassesValidation()
    {
        // Arrange
        var data = new PasskeyVerificationData(
            challenge_id: "challenge-123",
            id: "credential-id",
            raw_id: "raw-credential-id",
            response: new PasskeyAssertionResponseData(
                client_data_json: "eyJ0eXBlIjoid2ViYXV0aG4uZ2V0In0",
                authenticator_data: "SZYN5YgOjGh0NBcPZHZgW4_krrmihjLHmVzzuoMdl2M",
                signature: "MEUCIQDqOJZBqQZlBB4hzGbFJ8eQp_aQpKKL1H0DQeGkKwIgKQIgY5N",
                user_handle: "user-123"
            ),
            type: "public-key"
        );

        // Act
        var results = ValidateModel(data);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void PasskeyVerificationData_MissingRequiredFields_FailsValidation()
    {
        // Arrange - Create with empty strings to test validation
        var response = new PasskeyAssertionResponseData("", "", "", null);
        var data = new PasskeyVerificationData("", "", "", response, "");

        // Act
        var results = ValidateModel(data);

        // Assert
        Assert.NotEmpty(results);
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Validates a model using DataAnnotations validation.
    /// </summary>
    private static List<ValidationResult> ValidateModel(object model)
    {
        var context = new ValidationContext(model, null, null);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, context, results, validateAllProperties: true);
        return results;
    }

    #endregion
}
