using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using MehguViewer.Core.Services;
using MehguViewer.Core.Workers;
using MehguViewer.Core.Helpers;
using MehguViewer.Core.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;

namespace MehguViewer.Core.Tests.Services;

/// <summary>
/// Comprehensive test suite for PasskeyService.
/// Tests WebAuthn registration, authentication, challenge management, and security features.
/// </summary>
public class PasskeyServiceTests : IDisposable
{
    private readonly PasskeyService _service;
    private readonly IConfiguration _config;
    private readonly ILogger<PasskeyService> _logger;
    private readonly User _testUser;
    
    public PasskeyServiceTests()
    {
        // Create in-memory configuration
        var configData = new Dictionary<string, string?>
        {
            ["Auth:Passkey:RpId"] = "localhost",
            ["Auth:Passkey:RpName"] = "MehguViewer Test"
        };
        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();
        
        _logger = new TestLogger<PasskeyService>();
        
        _service = new PasskeyService(_config, _logger);
        
        _testUser = new User(
            id: "urn:mvn:user:test-123",
            username: "testuser",
            password_hash: "",
            role: "user",
            created_at: DateTime.UtcNow
        );
    }
    
    public void Dispose()
    {
        // Cleanup if needed
    }

    #region Constructor Tests
    
    [Fact]
    public void Constructor_WithNullConfiguration_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new PasskeyService(null!, _logger));
    }
    
    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new PasskeyService(_config, null!));
    }
    
    [Fact]
    public void Constructor_WithValidParameters_InitializesSuccessfully()
    {
        // Act
        var service = new PasskeyService(_config, _logger);
        
        // Assert
        Assert.NotNull(service);
        Assert.Equal("localhost", service.GetRpId());
        Assert.Equal("MehguViewer Test", service.GetRpName());
    }

    #endregion

    #region Configuration Tests
    
    [Fact]
    public void GetRpId_WithConfiguredValue_ReturnsConfiguredValue()
    {
        // Assert
        Assert.Equal("localhost", _service.GetRpId());
    }
    
    [Fact]
    public void GetRpId_WithoutConfiguration_ReturnsLocalhost()
    {
        // Arrange
        var emptyConfig = new ConfigurationBuilder().Build();
        var service = new PasskeyService(emptyConfig, _logger);
        
        // Act
        var rpId = service.GetRpId();
        
        // Assert
        Assert.Equal("localhost", rpId);
    }
    
    [Fact]
    public void GetRpName_WithConfiguredValue_ReturnsConfiguredValue()
    {
        // Assert
        Assert.Equal("MehguViewer Test", _service.GetRpName());
    }
    
    [Fact]
    public void GetRpName_WithoutConfiguration_ReturnsDefault()
    {
        // Arrange
        var emptyConfig = new ConfigurationBuilder().Build();
        var service = new PasskeyService(emptyConfig, _logger);
        
        // Act
        var rpName = service.GetRpName();
        
        // Assert
        Assert.Equal("MehguViewer", rpName);
    }

    #endregion

    #region Registration Options Tests
    
    [Fact]
    public void GenerateRegistrationOptions_WithNullUser_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            _service.GenerateRegistrationOptions(null!, Array.Empty<Passkey>()));
    }
    
    [Fact]
    public void GenerateRegistrationOptions_WithEmptyUserId_ThrowsArgumentException()
    {
        // Arrange
        var invalidUser = new User("", "test", "", "user", DateTime.UtcNow);
        
        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => 
            _service.GenerateRegistrationOptions(invalidUser, Array.Empty<Passkey>()));
        Assert.Contains("User ID", ex.Message);
    }
    
    [Fact]
    public void GenerateRegistrationOptions_WithEmptyUsername_ThrowsArgumentException()
    {
        // Arrange
        var invalidUser = new User("urn:mvn:user:123", "", "", "user", DateTime.UtcNow);
        
        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => 
            _service.GenerateRegistrationOptions(invalidUser, Array.Empty<Passkey>()));
        Assert.Contains("Username", ex.Message);
    }
    
    [Fact]
    public void GenerateRegistrationOptions_WithValidUser_ReturnsValidOptions()
    {
        // Act
        var options = _service.GenerateRegistrationOptions(_testUser, Array.Empty<Passkey>());
        
        // Assert
        Assert.NotNull(options);
        Assert.NotNull(options.challenge);
        Assert.NotEmpty(options.challenge);
        Assert.Equal("localhost", options.rp.id);
        Assert.Equal("MehguViewer Test", options.rp.name);
        Assert.Equal(_testUser.username, options.user.name);
        Assert.Equal(_testUser.username, options.user.display_name);
        Assert.Contains(options.pub_key_cred_params, p => p.alg == -7); // ES256
        Assert.Contains(options.pub_key_cred_params, p => p.alg == -257); // RS256
        Assert.Equal(300000, options.timeout);
        Assert.Equal("none", options.attestation);
    }
    
    [Fact]
    public void GenerateRegistrationOptions_CalledMultipleTimes_GeneratesUniqueChallenges()
    {
        // Act
        var options1 = _service.GenerateRegistrationOptions(_testUser, Array.Empty<Passkey>());
        var options2 = _service.GenerateRegistrationOptions(_testUser, Array.Empty<Passkey>());
        var options3 = _service.GenerateRegistrationOptions(_testUser, Array.Empty<Passkey>());
        
        // Assert
        Assert.NotEqual(options1.challenge, options2.challenge);
        Assert.NotEqual(options2.challenge, options3.challenge);
        Assert.NotEqual(options1.challenge, options3.challenge);
    }

    #endregion

    #region Authentication Options Tests
    
    [Fact]
    public void GenerateAuthenticationOptions_WithoutAllowedPasskeys_ReturnsValidOptions()
    {
        // Act
        var (options, challengeId) = _service.GenerateAuthenticationOptions();
        
        // Assert
        Assert.NotNull(options);
        Assert.NotNull(challengeId);
        Assert.NotEmpty(options.challenge);
        Assert.NotEmpty(challengeId);
        Assert.Equal("localhost", options.rp_id);
        Assert.Equal(300000, options.timeout);
        Assert.Equal("preferred", options.user_verification);
        Assert.Null(options.allow_credentials);
    }
    
    [Fact]
    public void GenerateAuthenticationOptions_WithAllowedPasskeys_IncludesThem()
    {
        // Arrange
        var allowedPasskeys = new[]
        {
            new Passkey(
                id: "urn:mvn:passkey:1",
                user_id: _testUser.id,
                credential_id: "cred-1",
                public_key: "key-1",
                sign_count: 0,
                name: "Test Key",
                device_type: "platform",
                backed_up: false,
                created_at: DateTime.UtcNow,
                last_used_at: null
            )
        };
        
        // Act
        var (options, challengeId) = _service.GenerateAuthenticationOptions(allowedPasskeys);
        
        // Assert
        Assert.NotNull(options.allow_credentials);
        Assert.Single(options.allow_credentials);
        Assert.Equal("cred-1", options.allow_credentials[0].id);
        Assert.Equal("public-key", options.allow_credentials[0].type);
    }

    #endregion

    #region Challenge Management Tests
    
    [Fact]
    public void StoreChallenge_WithValidChallenge_ReturnsValidId()
    {
        // Arrange
        var challenge = PasskeyService.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        
        // Act
        var challengeId = _service.StoreChallenge(challenge);
        
        // Assert
        Assert.NotNull(challengeId);
        Assert.NotEmpty(challengeId);
    }
    
    [Fact]
    public void ValidateChallenge_WithValidChallengeId_ReturnsTrue()
    {
        // Arrange
        var challenge = PasskeyService.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var challengeId = _service.StoreChallenge(challenge, _testUser.id);
        
        // Act
        var (valid, returnedChallenge, userId) = _service.ValidateChallenge(challengeId);
        
        // Assert
        Assert.True(valid);
        Assert.Equal(challenge, returnedChallenge);
        Assert.Equal(_testUser.id, userId);
    }
    
    [Fact]
    public void ValidateChallenge_WithInvalidChallengeId_ReturnsFalse()
    {
        // Act
        var (valid, challenge, userId) = _service.ValidateChallenge("invalid-id");
        
        // Assert
        Assert.False(valid);
        Assert.Null(challenge);
        Assert.Null(userId);
    }
    
    [Fact]
    public void ValidateChallenge_CalledTwiceWithSameId_ReturnsFalseSecondTime()
    {
        // Arrange
        var challenge = PasskeyService.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var challengeId = _service.StoreChallenge(challenge);
        
        // Act
        var (valid1, _, _) = _service.ValidateChallenge(challengeId);
        var (valid2, _, _) = _service.ValidateChallenge(challengeId);
        
        // Assert
        Assert.True(valid1);
        Assert.False(valid2); // Challenge consumed on first validation
    }

    #endregion

    #region Base64Url Encoding Tests
    
    [Fact]
    public void Base64UrlEncode_WithValidBytes_ReturnsValidString()
    {
        // Arrange
        var bytes = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
        
        // Act
        var encoded = PasskeyService.Base64UrlEncode(bytes);
        
        // Assert
        Assert.NotNull(encoded);
        Assert.NotEmpty(encoded);
        Assert.DoesNotContain("+", encoded);
        Assert.DoesNotContain("/", encoded);
        Assert.DoesNotContain("=", encoded);
    }
    
    [Fact]
    public void Base64UrlEncode_WithEmptyBytes_ReturnsEmptyString()
    {
        // Arrange
        var bytes = Array.Empty<byte>();
        
        // Act
        var encoded = PasskeyService.Base64UrlEncode(bytes);
        
        // Assert
        Assert.Equal(string.Empty, encoded);
    }
    
    [Fact]
    public void Base64UrlDecode_WithValidString_ReturnsOriginalBytes()
    {
        // Arrange
        var originalBytes = RandomNumberGenerator.GetBytes(32);
        var encoded = PasskeyService.Base64UrlEncode(originalBytes);
        
        // Act
        var decoded = PasskeyService.Base64UrlDecode(encoded);
        
        // Assert
        Assert.Equal(originalBytes, decoded);
    }
    
    [Fact]
    public void Base64UrlEncodeDecode_RoundTrip_PreservesData()
    {
        // Arrange
        var testData = new byte[256];
        for (int i = 0; i < 256; i++)
            testData[i] = (byte)i;
        
        // Act
        var encoded = PasskeyService.Base64UrlEncode(testData);
        var decoded = PasskeyService.Base64UrlDecode(encoded);
        
        // Assert
        Assert.Equal(testData, decoded);
    }

    #endregion

    #region Verification Tests
    
    [Fact]
    public void VerifyRegistration_WithNullRequest_ReturnsFalse()
    {
        // Act
        var result = _service.VerifyRegistration(
            null!, 
            "challenge", 
            "userId", 
            "https://localhost");
        
        // Assert
        Assert.False(result.success);
        Assert.Contains("cannot be null", result.error ?? "");
    }
    
    [Fact]
    public void VerifyRegistration_WithEmptyChallenge_ReturnsFalse()
    {
        // Arrange
        var request = CreateMockRegistrationRequest();
        
        // Act
        var result = _service.VerifyRegistration(
            request, 
            "", 
            "userId", 
            "https://localhost");
        
        // Assert
        Assert.False(result.success);
        Assert.Contains("challenge", result.error?.ToLower() ?? "");
    }
    
    [Fact]
    public void VerifyRegistration_WithEmptyOrigin_ReturnsFalse()
    {
        // Arrange
        var request = CreateMockRegistrationRequest();
        
        // Act
        var result = _service.VerifyRegistration(
            request, 
            "challenge", 
            "userId", 
            "");
        
        // Assert
        Assert.False(result.success);
        Assert.Contains("origin", result.error?.ToLower() ?? "");
    }
    
    [Fact]
    public void VerifyAuthentication_WithNullRequest_ReturnsFalse()
    {
        // Arrange
        var passkey = CreateMockPasskey();
        
        // Act
        var result = _service.VerifyAuthentication(
            null!, 
            passkey, 
            "challenge", 
            "https://localhost");
        
        // Assert
        Assert.False(result.success);
        Assert.Contains("cannot be null", result.error ?? "");
    }
    
    [Fact]
    public void VerifyAuthentication_WithNullPasskey_ReturnsFalse()
    {
        // Arrange
        var request = CreateMockAuthenticationRequest();
        
        // Act
        var result = _service.VerifyAuthentication(
            request, 
            null!, 
            "challenge", 
            "https://localhost");
        
        // Assert
        Assert.False(result.success);
        Assert.Contains("cannot be null", result.error ?? "");
    }

    #endregion

    #region Security Tests
    
    [Fact]
    public void ChallengeGeneration_ProducesHighEntropyValues()
    {
        // Arrange & Act
        var challenges = new HashSet<string>();
        for (int i = 0; i < 100; i++)
        {
            var (options, _) = _service.GenerateAuthenticationOptions();
            challenges.Add(options.challenge);
        }
        
        // Assert - All challenges should be unique
        Assert.Equal(100, challenges.Count);
        
        // Assert - All challenges should be at least 32 characters (256 bits base64url-encoded)
        Assert.All(challenges, c => Assert.True(c.Length >= 40));
    }
    
    [Fact]
    public void MultipleServiceInstances_DoNotShareChallenges()
    {
        // Arrange
        var service1 = new PasskeyService(_config, _logger);
        var service2 = new PasskeyService(_config, _logger);
        
        // Act
        var challenge1 = service1.StoreChallenge("challenge1", "user1");
        var challenge2 = service2.StoreChallenge("challenge2", "user2");
        
        // Assert - Challenges should be isolated to their instances
        var (valid1InService1, _, _) = service1.ValidateChallenge(challenge1);
        var (valid2InService1, _, _) = service1.ValidateChallenge(challenge2);
        var (valid1InService2, _, _) = service2.ValidateChallenge(challenge1);
        var (valid2InService2, _, _) = service2.ValidateChallenge(challenge2);
        
        Assert.True(valid1InService1); // Challenge 1 valid in service 1
        Assert.False(valid2InService1); // Challenge 2 not in service 1
        Assert.False(valid1InService2); // Challenge 1 not in service 2
        Assert.True(valid2InService2); // Challenge 2 valid in service 2
    }

    #endregion

    #region Helper Methods
    
    private PasskeyRegistrationRequest CreateMockRegistrationRequest()
    {
        return new PasskeyRegistrationRequest(
            id: "mock-id",
            raw_id: "mock-raw-id",
            response: new PasskeyAuthenticatorAttestationResponse(
                client_data_json: PasskeyService.Base64UrlEncode(Encoding.UTF8.GetBytes("{\"type\":\"webauthn.create\"}")),
                attestation_object: PasskeyService.Base64UrlEncode(new byte[100])
            ),
            type: "public-key",
            passkey_name: "Test Key"
        );
    }
    
    private PasskeyAuthenticationRequest CreateMockAuthenticationRequest()
    {
        return new PasskeyAuthenticationRequest(
            id: "mock-id",
            raw_id: "mock-raw-id",
            response: new PasskeyAuthenticatorAssertionResponse(
                client_data_json: PasskeyService.Base64UrlEncode(Encoding.UTF8.GetBytes("{\"type\":\"webauthn.get\"}")),
                authenticator_data: PasskeyService.Base64UrlEncode(new byte[100]),
                signature: PasskeyService.Base64UrlEncode(new byte[64]),
                user_handle: null
            ),
            type: "public-key"
        );
    }
    
    private Passkey CreateMockPasskey()
    {
        return new Passkey(
            id: "urn:mvn:passkey:mock-id",
            user_id: _testUser.id,
            credential_id: "mock-credential-id",
            public_key: PasskeyService.Base64UrlEncode(new byte[65]),
            sign_count: 0,
            name: "Test Key",
            device_type: "platform",
            backed_up: false,
            created_at: DateTime.UtcNow,
            last_used_at: null
        );
    }

    #endregion
    
    #region Test Logger Implementation
    
    /// <summary>
    /// Simple test logger that does nothing - for testing purposes only.
    /// </summary>
    private class TestLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
    
    #endregion
}
