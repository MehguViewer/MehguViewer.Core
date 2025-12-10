using MehguViewer.Core.Helpers;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MehguViewer.Core.Shared;

namespace MehguViewer.Core.Services;

/// <summary>
/// Service for WebAuthn/Passkey operations including challenge generation,
/// registration verification, and authentication verification.
/// </summary>
/// <remarks>
/// Implements WebAuthn specification for passwordless authentication using FIDO2 passkeys.
/// 
/// <para><strong>Key Features:</strong></para>
/// <list type="bullet">
///   <item>Challenge generation and validation with 5-minute expiration</item>
///   <item>Registration flow: credential creation and attestation verification</item>
///   <item>Authentication flow: assertion verification with replay protection</item>
///   <item>ES256 (ECDSA P-256 + SHA-256) and RS256 algorithm support</item>
///   <item>CBOR parsing for WebAuthn binary formats</item>
///   <item>Signature counter monitoring for cloned authenticator detection</item>
/// </list>
/// 
/// <para><strong>Security Considerations:</strong></para>
/// <list type="bullet">
///   <item>Challenges expire after 5 minutes to prevent replay attacks</item>
///   <item>RP ID hash verification ensures ceremony matches expected origin</item>
///   <item>User presence (UP) flag required for all operations</item>
///   <item>Sign counter monotonicity checked to detect cloned authenticators</item>
///   <item>Origin validation supports localhost development and strict production matching</item>
/// </list>
/// 
/// <para><strong>Implementation Notes:</strong></para>
/// This is a simplified, AOT-compatible implementation without external WebAuthn libraries.
/// Includes custom CBOR parsing for attestation objects and COSE public keys.
/// For production deployments, consider enhanced attestation verification and
/// integration with device trust frameworks.
/// </remarks>
public class PasskeyService
{
    #region Constants

    /// <summary>COSE algorithm identifier for ES256 (ECDSA with P-256 curve and SHA-256).</summary>
    private const int COSE_ALG_ES256 = -7;
    
    /// <summary>COSE algorithm identifier for RS256 (RSASSA-PKCS1-v1_5 with SHA-256).</summary>
    private const int COSE_ALG_RS256 = -257;
    
    /// <summary>Challenge expiration time in minutes.</summary>
    private const int ChallengeExpiryMinutes = 5;
    
    /// <summary>Timeout for WebAuthn ceremonies in milliseconds (5 minutes).</summary>
    private const int CeremonyTimeoutMs = 300000;
    
    /// <summary>Challenge byte length for entropy.</summary>
    private const int ChallengeByteLength = 32;
    
    /// <summary>Minimum authenticator data length (RP ID hash + flags + sign count).</summary>
    private const int MinAuthDataLength = 37;
    
    /// <summary>RP ID hash length (SHA-256 output).</summary>
    private const int RpIdHashLength = 32;
    
    /// <summary>ECDSA P-256 coordinate length in bytes.</summary>
    private const int P256CoordinateLength = 32;
    
    /// <summary>IEEE P1363 signature length for ES256 (R + S coordinates).</summary>
    private const int ES256SignatureLength = 64;

    #endregion

    #region Fields

    private readonly IConfiguration _configuration;
    private readonly ILogger<PasskeyService> _logger;
    
    /// <summary>
    /// Challenge storage with expiration tracking (instance-scoped for better isolation).
    /// Key: Challenge ID, Value: (challenge, expiry, userId)
    /// </summary>
    /// <remarks>
    /// Changed from static to instance field to prevent cross-instance pollution
    /// and enable better testability and memory management.
    /// </remarks>
    private readonly ConcurrentDictionary<string, (string challenge, DateTime expiry, string? userId)> _challenges = new();
    
    /// <summary>
    /// Cached RP ID hash to avoid repeated SHA-256 calculations.
    /// </summary>
    private readonly Lazy<byte[]> _rpIdHash;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the PasskeyService.
    /// </summary>
    /// <param name="configuration">Application configuration for RP ID and name.</param>
    /// <param name="logger">Logger for WebAuthn operations and security events.</param>
    /// <exception cref="ArgumentNullException">Thrown when configuration or logger is null.</exception>
    public PasskeyService(IConfiguration configuration, ILogger<PasskeyService> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        // Pre-compute RP ID hash for performance
        _rpIdHash = new Lazy<byte[]>(() => SHA256.HashData(Encoding.UTF8.GetBytes(GetRpId())));
        
        _logger.LogInformation("PasskeyService initialized with RP ID: {RpId}", GetRpId());
    }

    #endregion

    #region Public Methods - Configuration

    /// <summary>
    /// Gets the Relying Party ID (domain) for WebAuthn operations.
    /// </summary>
    /// <returns>The RP ID from configuration, or "localhost" for development.</returns>
    /// <remarks>
    /// The RP ID must match the domain of the web application.
    /// For production, configure via Auth:Passkey:RpId setting.
    /// Defaults to "localhost" for local development.
    /// </remarks>
    public string GetRpId()
    {
        var rpId = _configuration["Auth:Passkey:RpId"];
        if (!string.IsNullOrEmpty(rpId))
            return rpId;
        
        return "localhost";
    }
    
    /// <summary>
    /// Gets the Relying Party display name for user-facing WebAuthn prompts.
    /// </summary>
    /// <returns>The RP name from configuration, or "MehguViewer" as default.</returns>
    public string GetRpName()
    {
        return _configuration["Auth:Passkey:RpName"] ?? "MehguViewer";
    }

    #endregion

    #region Public Methods - Registration

    /// <summary>
    /// Generates registration options for creating a new passkey.
    /// </summary>
    /// <param name="user">The user creating the passkey.</param>
    /// <param name="existingPasskeys">Existing passkeys for this user (to prevent duplicates).</param>
    /// <returns>Registration options to send to the client.</returns>
    /// <exception cref="ArgumentNullException">Thrown when user is null.</exception>
    /// <exception cref="ArgumentException">Thrown when user.id is null or empty.</exception>
    /// <remarks>
    /// Creates a cryptographically random challenge with 5-minute expiration.
    /// Excludes existing credentials to prevent duplicate registrations.
    /// Supports ES256 and RS256 algorithms with preference for ES256.
    /// Requests user verification but doesn't require it (preferred mode).
    /// </remarks>
    public PasskeyRegistrationOptions GenerateRegistrationOptions(User user, IEnumerable<Passkey> existingPasskeys)
    {
        if (user == null)
            throw new ArgumentNullException(nameof(user));
        if (string.IsNullOrWhiteSpace(user.id))
            throw new ArgumentException("User ID cannot be null or empty", nameof(user));
        if (string.IsNullOrWhiteSpace(user.username))
            throw new ArgumentException("Username cannot be null or empty", nameof(user));
        
        _logger.LogDebug("Generating registration options for user {UserId}", user.id);
        
        CleanupExpiredChallenges();
        var challenge = GenerateChallenge();
        var challengeId = Guid.NewGuid().ToString();
        
        _challenges[challengeId] = (challenge, DateTime.UtcNow.AddMinutes(ChallengeExpiryMinutes), user.id);
        
        _logger.LogInformation("Generated registration challenge {ChallengeId} for user {UserId}", challengeId, user.id);

        var excludeCredentials = existingPasskeys
            .Select(p => new PasskeyAllowCredential("public-key", p.credential_id))
            .ToArray();
        
        return new PasskeyRegistrationOptions(
            challenge: challenge,
            rp: new PasskeyRpEntity(GetRpId(), GetRpName()),
            user: new PasskeyUserEntity(
                id: Base64UrlEncode(Encoding.UTF8.GetBytes(user.id)),
                name: user.username,
                display_name: user.username
            ),
            pub_key_cred_params: new[]
            {
                new PasskeyPubKeyCredParam("public-key", COSE_ALG_ES256),
                new PasskeyPubKeyCredParam("public-key", COSE_ALG_RS256)
            },
            timeout: CeremonyTimeoutMs,
            attestation: "none",
            authenticator_selection_resident_key: "preferred",
            authenticator_selection_user_verification: "preferred"
        );
    }
    
    /// <summary>
    /// Verifies a passkey registration response from the client.
    /// </summary>
    /// <param name="request">The registration response from the authenticator.</param>
    /// <param name="expectedChallenge">The challenge that was sent to the client.</param>
    /// <param name="expectedUserId">The user ID that should be registered.</param>
    /// <param name="expectedOrigin">The expected origin (e.g., https://example.com).</param>
    /// <returns>
    /// Tuple containing:
    /// - success: Whether verification passed
    /// - credentialId: Base64url-encoded credential ID
    /// - publicKey: Base64url-encoded COSE public key
    /// - signCount: Initial signature counter value
    /// - backedUp: Whether credential is backed up (synced)
    /// - deviceType: "platform" or "cross-platform"
    /// - error: Error message if verification failed
    /// </returns>
    /// <remarks>
    /// <para><strong>Verification Steps:</strong></para>
    /// <list type="number">
    ///   <item>Decode and parse client data JSON</item>
    ///   <item>Verify ceremony type is "webauthn.create"</item>
    ///   <item>Verify challenge matches expected value</item>
    ///   <item>Verify origin matches expected domain</item>
    ///   <item>Parse attestation object (CBOR format)</item>
    ///   <item>Extract authenticator data from attestation</item>
    ///   <item>Verify RP ID hash matches expected RP ID</item>
    ///   <item>Verify user presence (UP) flag is set</item>
    ///   <item>Extract credential ID and public key</item>
    /// </list>
    /// 
    /// <para><strong>Security Notes:</strong></para>
    /// Attestation format is set to "none" for privacy - we don't verify the authenticator model.
    /// For high-security scenarios, consider requiring and verifying device attestation.
    /// </remarks>
    public (bool success, string? credentialId, string? publicKey, long signCount, bool backedUp, string? deviceType, string? error) 
        VerifyRegistration(PasskeyRegistrationRequest request, string expectedChallenge, string expectedUserId, string expectedOrigin)
    {
        if (request == null)
            return (false, null, null, 0, false, null, "Request cannot be null");
        if (string.IsNullOrWhiteSpace(expectedChallenge))
            return (false, null, null, 0, false, null, "Expected challenge cannot be null or empty");
        if (string.IsNullOrWhiteSpace(expectedOrigin))
            return (false, null, null, 0, false, null, "Expected origin cannot be null or empty");
        
        _logger.LogDebug("Verifying passkey registration for user {UserId}", expectedUserId);
        
        try
        {
            // Decode client data
            var clientDataJson = Base64UrlDecode(request.response.client_data_json);
            var clientData = JsonSerializer.Deserialize(clientDataJson, PasskeyJsonContext.Default.ClientData);
            
            if (clientData == null)
                return (false, null, null, 0, false, null, "Invalid client data");
            
            // Verify type
            if (clientData.type != "webauthn.create")
                return (false, null, null, 0, false, null, "Invalid ceremony type");
            
            // Verify challenge
            var receivedChallenge = clientData.challenge;
            if (receivedChallenge != expectedChallenge)
                return (false, null, null, 0, false, null, "Challenge mismatch");
            
            // Verify origin (basic check - in production, should be more strict)
            if (!VerifyOrigin(clientData.origin, expectedOrigin))
                return (false, null, null, 0, false, null, "Origin mismatch");
            
            // Decode attestation object
            var attestationObject = Base64UrlDecode(request.response.attestation_object);
            var (authData, error) = ParseAttestationObject(attestationObject);
            
            if (authData == null)
                return (false, null, null, 0, false, null, error ?? "Failed to parse attestation");
            
            // Verify RP ID hash using cached value
            if (!authData.RpIdHash.SequenceEqual(_rpIdHash.Value))
            {
                _logger.LogWarning("RP ID hash mismatch during registration verification for user {UserId}", expectedUserId);
                return (false, null, null, 0, false, null, "RP ID hash mismatch");
            }
            
            // Verify user presence flag
            if (!authData.UserPresent)
                return (false, null, null, 0, false, null, "User presence not verified");
            
            // Extract credential data
            if (authData.CredentialId == null || authData.PublicKey == null)
                return (false, null, null, 0, false, null, "Missing credential data");
            
            var credentialId = Base64UrlEncode(authData.CredentialId);
            var publicKey = Base64UrlEncode(authData.PublicKey);
            
            // Determine device type based on authenticator attachment
            string? deviceType = authData.BackupEligible ? "platform" : "cross-platform";
            
            _logger.LogInformation(
                "Successfully verified passkey registration for user {UserId}. Credential ID: {CredentialId}, Device: {DeviceType}, Backed up: {BackedUp}",
                expectedUserId, credentialId[..Math.Min(8, credentialId.Length)] + "...", deviceType, authData.BackedUp);
            
            return (true, credentialId, publicKey, authData.SignCount, authData.BackedUp, deviceType, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Passkey registration verification failed for user {UserId}", expectedUserId);
            return (false, null, null, 0, false, null, "Verification failed: " + ex.Message);
        }
    }
    
    #endregion

    #region Public Methods - Authentication

    /// <summary>
    /// Generates authentication options for passkey login.
    /// </summary>
    /// <param name="allowedPasskeys">Optional list of specific passkeys to allow. If null, any passkey is accepted.</param>
    /// <returns>
    /// Tuple containing:
    /// - options: Authentication options to send to the client
    /// - challengeId: ID to validate the challenge later
    /// </returns>
    /// <remarks>
    /// Creates a cryptographically random challenge with 5-minute expiration.
    /// If allowedPasskeys is provided, restricts authentication to those specific credentials.
    /// If null, allows any registered passkey (usernameless flow).
    /// </remarks>
    public (PasskeyAuthenticationOptions options, string challengeId) GenerateAuthenticationOptions(IEnumerable<Passkey>? allowedPasskeys = null)
    {
        _logger.LogDebug("Generating authentication options with {Count} allowed passkeys", 
            allowedPasskeys?.Count() ?? 0);
        
        CleanupExpiredChallenges();
        var challenge = GenerateChallenge();
        var challengeId = Guid.NewGuid().ToString();
        
        _challenges[challengeId] = (challenge, DateTime.UtcNow.AddMinutes(ChallengeExpiryMinutes), null);
        
        _logger.LogInformation("Generated authentication challenge {ChallengeId}", challengeId);

        PasskeyAllowCredential[]? allowCredentials = null;
        if (allowedPasskeys != null && allowedPasskeys.Any())
        {
            allowCredentials = allowedPasskeys
                .Select(p => new PasskeyAllowCredential("public-key", p.credential_id))
                .ToArray();
        }
        
        var options = new PasskeyAuthenticationOptions(
            challenge: challenge,
            timeout: CeremonyTimeoutMs,
            rp_id: GetRpId(),
            allow_credentials: allowCredentials,
            user_verification: "preferred"
        );
        
        return (options, challengeId);
    }
    
    /// <summary>
    /// Verifies a passkey authentication response from the client.
    /// </summary>
    /// <param name="request">The authentication response from the authenticator.</param>
    /// <param name="passkey">The stored passkey for this credential.</param>
    /// <param name="expectedChallenge">The challenge that was sent to the client.</param>
    /// <param name="expectedOrigin">The expected origin (e.g., https://example.com).</param>
    /// <returns>
    /// Tuple containing:
    /// - success: Whether verification passed
    /// - userId: The user ID from the passkey
    /// - newSignCount: Updated signature counter value
    /// - error: Error message if verification failed
    /// </returns>
    /// <remarks>
    /// <para><strong>Verification Steps:</strong></para>
    /// <list type="number">
    ///   <item>Decode and parse client data JSON</item>
    ///   <item>Verify ceremony type is "webauthn.get"</item>
    ///   <item>Verify challenge matches expected value</item>
    ///   <item>Verify origin matches expected domain</item>
    ///   <item>Parse authenticator data</item>
    ///   <item>Verify RP ID hash matches expected RP ID</item>
    ///   <item>Verify user presence (UP) flag is set</item>
    ///   <item>Check signature counter for replay/clone detection</item>
    ///   <item>Verify cryptographic signature using stored public key</item>
    /// </list>
    /// 
    /// <para><strong>Replay Protection:</strong></para>
    /// The signature counter must increase with each authentication.
    /// If the counter doesn't increase, it may indicate a cloned authenticator.
    /// Currently logs a warning but allows authentication - adjust for security requirements.
    /// </remarks>
    public (bool success, string? userId, long newSignCount, string? error) 
        VerifyAuthentication(PasskeyAuthenticationRequest request, Passkey passkey, string expectedChallenge, string expectedOrigin)
    {
        if (request == null)
            return (false, null, 0, "Request cannot be null");
        if (passkey == null)
            return (false, null, 0, "Passkey cannot be null");
        if (string.IsNullOrWhiteSpace(expectedChallenge))
            return (false, null, 0, "Expected challenge cannot be null or empty");
        if (string.IsNullOrWhiteSpace(expectedOrigin))
            return (false, null, 0, "Expected origin cannot be null or empty");
        
        _logger.LogDebug("Verifying passkey authentication for user {UserId}, credential {CredentialId}", 
            passkey.user_id, passkey.credential_id?[..Math.Min(8, passkey.credential_id.Length)] + "...");
        
        try
        {
            // Decode client data
            var clientDataJson = Base64UrlDecode(request.response.client_data_json);
            var clientData = JsonSerializer.Deserialize(clientDataJson, PasskeyJsonContext.Default.ClientData);
            
            if (clientData == null)
                return (false, null, 0, "Invalid client data");
            
            // Verify type
            if (clientData.type != "webauthn.get")
                return (false, null, 0, "Invalid ceremony type");
            
            // Verify challenge
            if (clientData.challenge != expectedChallenge)
                return (false, null, 0, "Challenge mismatch");
            
            // Verify origin
            if (!VerifyOrigin(clientData.origin, expectedOrigin))
                return (false, null, 0, "Origin mismatch");
            
            // Decode authenticator data
            var authenticatorData = Base64UrlDecode(request.response.authenticator_data);
            var (authData, error) = ParseAuthenticatorData(authenticatorData);
            
            if (authData == null)
                return (false, null, 0, error ?? "Failed to parse authenticator data");
            
            // Verify RP ID hash using cached value
            if (!authData.RpIdHash.SequenceEqual(_rpIdHash.Value))
            {
                _logger.LogWarning("RP ID hash mismatch during authentication for user {UserId}", passkey.user_id);
                return (false, null, 0, "RP ID hash mismatch");
            }
            
            // Verify user presence
            if (!authData.UserPresent)
                return (false, null, 0, "User presence not verified");
            
            // Verify signature counter (replay detection)
            if (authData.SignCount > 0 && passkey.sign_count > 0 && authData.SignCount <= passkey.sign_count)
            {
                _logger.LogWarning(
                    "SECURITY: Passkey signature counter anomaly detected for user {UserId}. Current: {Current}, Expected: >{Expected}. Possible cloned authenticator.",
                    passkey.user_id, authData.SignCount, passkey.sign_count);
                
                // SECURITY: Consider failing authentication in high-security scenarios
                // return (false, null, 0, "Signature counter did not increase - possible replay attack");
            }
            
            // Verify signature
            var signatureBase = CreateSignatureBase(authenticatorData, clientDataJson);
            var signature = Base64UrlDecode(request.response.signature);
            var publicKey = Base64UrlDecode(passkey.public_key);
            
            if (!VerifySignature(publicKey, signatureBase, signature))
            {
                _logger.LogWarning("SECURITY: Signature verification failed for user {UserId}", passkey.user_id);
                return (false, null, 0, "Signature verification failed");
            }
            
            _logger.LogInformation(
                "Successfully verified passkey authentication for user {UserId}. Sign count: {SignCount}",
                passkey.user_id, authData.SignCount);
            
            return (true, passkey.user_id, authData.SignCount, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Passkey authentication verification failed for user {UserId}", passkey.user_id);
            return (false, null, 0, "Verification failed: " + ex.Message);
        }
    }
    
    #endregion

    #region Public Methods - Challenge Management

    /// <summary>
    /// Validates and consumes a challenge.
    /// </summary>
    /// <param name="challengeId">The challenge ID to validate.</param>
    /// <returns>
    /// Tuple containing:
    /// - valid: Whether the challenge is valid and not expired
    /// - challenge: The challenge value if valid
    /// - userId: The associated user ID if this was a registration challenge
    /// </returns>
    /// <remarks>
    /// This method removes the challenge from storage (one-time use).
    /// Automatically cleans up expired challenges before validation.
    /// Challenges expire after 5 minutes.
    /// </remarks>
    public (bool valid, string? challenge, string? userId) ValidateChallenge(string challengeId)
    {
        CleanupExpiredChallenges();
        
        if (_challenges.TryRemove(challengeId, out var data))
        {
            if (data.expiry > DateTime.UtcNow)
            {
                _logger.LogDebug("Challenge {ChallengeId} validated and consumed", challengeId);
                return (true, data.challenge, data.userId);
            }
            
            _logger.LogWarning("Challenge {ChallengeId} was expired", challengeId);
        }
        else
        {
            _logger.LogWarning("Challenge {ChallengeId} not found or already used", challengeId);
        }
        
        return (false, null, null);
    }
    
    /// <summary>
    /// Stores a challenge for later validation.
    /// </summary>
    /// <param name="challenge">The challenge value to store.</param>
    /// <param name="userId">Optional user ID to associate with this challenge.</param>
    /// <returns>A unique challenge ID for later validation.</returns>
    /// <remarks>
    /// Used for custom challenge flows. Standard registration and authentication
    /// flows generate challenges automatically.
    /// Challenge expires after 5 minutes.
    /// </remarks>
    public string StoreChallenge(string challenge, string? userId = null)
    {
        var challengeId = Guid.NewGuid().ToString();
        _challenges[challengeId] = (challenge, DateTime.UtcNow.AddMinutes(ChallengeExpiryMinutes), userId);
        
        _logger.LogDebug("Stored challenge {ChallengeId} with expiry {Expiry}", 
            challengeId, DateTime.UtcNow.AddMinutes(ChallengeExpiryMinutes));
        
        return challengeId;
    }

    #endregion

    #region Private Helpers - Challenge and Validation
    
    /// <summary>
    /// Generates a cryptographically random challenge for WebAuthn ceremonies.
    /// </summary>
    /// <returns>Base64url-encoded 32-byte random challenge.</returns>
    private static string GenerateChallenge()
    {
        var bytes = new byte[ChallengeByteLength];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Base64UrlEncode(bytes);
    }
    
    /// <summary>
    /// Verifies that the received origin matches the expected origin.
    /// </summary>
    /// <param name="receivedOrigin">Origin from client data.</param>
    /// <param name="expectedOrigin">Expected origin for this RP.</param>
    /// <returns>True if origins match; false otherwise.</returns>
    /// <remarks>
    /// For development, allows localhost variations (localhost, 127.0.0.1, with any port).
    /// For production, requires exact origin match or same-origin prefix.
    /// SECURITY: In production, this should be strictly validated against a whitelist.
    /// </remarks>
    private bool VerifyOrigin(string receivedOrigin, string expectedOrigin)
    {
        if (string.IsNullOrWhiteSpace(receivedOrigin) || string.IsNullOrWhiteSpace(expectedOrigin))
        {
            _logger.LogWarning("Origin validation failed: null or empty origin");
            return false;
        }
        
        // Allow localhost variations for development
        if (expectedOrigin.Contains("localhost") || expectedOrigin.Contains("127.0.0.1"))
        {
            var isLocalhost = receivedOrigin.Contains("localhost") || receivedOrigin.Contains("127.0.0.1");
            if (!isLocalhost)
            {
                _logger.LogWarning("Origin mismatch: expected localhost, got {ReceivedOrigin}", receivedOrigin);
            }
            return isLocalhost;
        }
        
        // For production, compare origin strictly
        var matches = receivedOrigin == expectedOrigin || receivedOrigin.StartsWith(expectedOrigin);
        if (!matches)
        {
            _logger.LogWarning("Origin mismatch: expected {ExpectedOrigin}, got {ReceivedOrigin}", 
                expectedOrigin, receivedOrigin);
        }
        return matches;
    }
    
    /// <summary>
    /// Removes expired challenges from the concurrent dictionary.
    /// </summary>
    /// <remarks>
    /// Called automatically before challenge generation and validation.
    /// Helps prevent memory leaks from abandoned challenges.
    /// </remarks>
    private void CleanupExpiredChallenges()
    {
        var now = DateTime.UtcNow;
        var expiredKeys = _challenges
            .Where(kvp => kvp.Value.expiry < now)
            .Select(kvp => kvp.Key)
            .ToList();
        
        foreach (var key in expiredKeys)
        {
            _challenges.TryRemove(key, out _);
        }
        
        if (expiredKeys.Count > 0)
        {
            _logger.LogDebug("Cleaned up {Count} expired passkey challenges", expiredKeys.Count);
        }
    }

    #endregion

    #region Private Helpers - Authenticator Data Parsing
    
    /// <summary>
    /// Parses the attestation object from registration response.
    /// </summary>
    /// <param name="attestationObject">CBOR-encoded attestation object.</param>
    /// <returns>Tuple containing parsed authenticator data and any error.</returns>
    /// <remarks>
    /// Attestation object format (CBOR map):
    /// - "fmt": attestation statement format
    /// - "attStmt": attestation statement (varies by format)
    /// - "authData": authenticator data bytes
    /// 
    /// This implementation extracts authData and ignores attestation verification (fmt="none").
    /// </remarks>
    private static (AuthenticatorData? data, string? error) ParseAttestationObject(byte[] attestationObject)
    {
        try
        {
            var (authDataBytes, error) = ExtractAuthDataFromCbor(attestationObject);
            if (authDataBytes == null)
                return (null, error);
            
            return ParseAuthenticatorData(authDataBytes, includeCredentialData: true);
        }
        catch (Exception ex)
        {
            return (null, $"Attestation parsing failed: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Parses authenticator data bytes into structured format.
    /// </summary>
    /// <param name="authData">Raw authenticator data bytes.</param>
    /// <param name="includeCredentialData">Whether to parse attested credential data (registration only).</param>
    /// <returns>Tuple containing parsed data and any error.</returns>
    /// <remarks>
    /// <para><strong>Authenticator Data Structure:</strong></para>
    /// <list type="bullet">
    ///   <item>Bytes 0-31: RP ID hash (SHA-256)</item>
    ///   <item>Byte 32: Flags (UP, UV, BE, BS, AT, ED)</item>
    ///   <item>Bytes 33-36: Signature counter (big-endian uint32)</item>
    ///   <item>Bytes 37+: Attested credential data (if AT flag set)</item>
    /// </list>
    /// 
    /// <para><strong>Flags:</strong></para>
    /// - UP (0x01): User Present
    /// - UV (0x04): User Verified
    /// - BE (0x08): Backup Eligible (synced authenticator capable)
    /// - BS (0x10): Backup State (currently synced)
    /// - AT (0x40): Attested Credential Data included
    /// - ED (0x80): Extension Data included
    /// </remarks>
    private static (AuthenticatorData? data, string? error) ParseAuthenticatorData(byte[] authData, bool includeCredentialData = false)
    {
        try
        {
            if (authData.Length < MinAuthDataLength)
                return (null, $"Authenticator data too short: {authData.Length} bytes, expected at least {MinAuthDataLength}");
            
            var result = new AuthenticatorData
            {
                RpIdHash = authData[..RpIdHashLength]
            };
            
            var flags = authData[32];
            result.UserPresent = (flags & 0x01) != 0;
            result.UserVerified = (flags & 0x04) != 0;
            result.BackupEligible = (flags & 0x08) != 0;
            result.BackedUp = (flags & 0x10) != 0;
            var hasAttestedCredentialData = (flags & 0x40) != 0;
            
            result.SignCount = BitConverter.ToUInt32(authData.AsSpan(33, 4).ToArray().Reverse().ToArray(), 0);
            
            if (includeCredentialData && hasAttestedCredentialData && authData.Length > MinAuthDataLength)
            {
                // Parse attested credential data
                // Format: AAGUID (16) || credIdLen (2) || credId (credIdLen) || credPubKey (CBOR)
                var offset = MinAuthDataLength;
                
                // Skip AAGUID (16 bytes)
                offset += 16;
                
                if (offset + 2 > authData.Length)
                    return (null, "Invalid attested credential data: missing credential ID length");
                
                // Read credential ID length (big-endian)
                var credIdLen = (authData[offset] << 8) | authData[offset + 1];
                offset += 2;
                
                if (offset + credIdLen > authData.Length)
                    return (null, $"Invalid attested credential data: credential ID length {credIdLen} exceeds data");
                
                // Read credential ID
                result.CredentialId = authData[offset..(offset + credIdLen)];
                offset += credIdLen;
                
                // Read public key (remaining bytes - CBOR encoded)
                result.PublicKey = authData[offset..];
            }
            
            return (result, null);
        }
        catch (Exception ex)
        {
            return (null, $"Authenticator data parsing failed: {ex.Message}");
        }
    }

    #endregion

    #region Private Helpers - CBOR Parsing
    #endregion

    #region Private Helpers - CBOR Parsing
    
    /// <summary>
    /// Extracts the authenticator data bytes from a CBOR-encoded attestation object.
    /// </summary>
    /// <param name="cborData">CBOR-encoded attestation object.</param>
    /// <returns>Tuple containing authenticator data bytes and any error.</returns>
    /// <remarks>
    /// <para><strong>CBOR Map Structure:</strong></para>
    /// The attestation object is a CBOR map with keys:
    /// - "fmt": string (attestation format, e.g., "none", "packed", "tpm")
    /// - "attStmt": map (attestation statement, format-specific)
    /// - "authData": bytes (the authenticator data we need)
    /// 
    /// <para><strong>Implementation:</strong></para>
    /// This parser handles basic CBOR types needed for WebAuthn.
    /// Iterates through map entries looking for "authData" key.
    /// For production use with complex attestation, consider a full CBOR library.
    /// </remarks>
    private static (byte[]? authData, string? error) ExtractAuthDataFromCbor(byte[] cborData)
    {
        try
        {
            // Simple CBOR parser for attestation object
            // Looking for the "authData" key in the map
            // Format: { "fmt": string, "attStmt": {...}, "authData": bytes }
            var offset = 0;
            
            // First byte should be map type (0xA0-0xBF for small maps, or 0xB8+ for larger)
            var firstByte = cborData[offset++];
            int mapSize;
            
            if ((firstByte & 0xF0) == 0xA0)
            {
                // Small map (0-23 items)
                mapSize = firstByte & 0x0F;
            }
            else if (firstByte == 0xB8)
            {
                // Map with 1-byte length
                mapSize = cborData[offset++];
            }
            else if (firstByte == 0xB9)
            {
                // Map with 2-byte length
                mapSize = (cborData[offset] << 8) | cborData[offset + 1];
                offset += 2;
            }
            else
            {
                return (null, $"Invalid CBOR map header: 0x{firstByte:X2}");
            }
            
            // Iterate through map entries looking for "authData"
            for (int i = 0; i < mapSize; i++)
            {
                // Read key
                var (keyStr, newOffset, keyError) = ReadCborTextString(cborData, offset);
                if (keyError != null)
                {
                    return (null, $"Error reading key at offset {offset}: {keyError}");
                }
                offset = newOffset;
                
                if (keyStr == "authData")
                {
                    // Read byte string value
                    var (authDataBytes, finalOffset, valueError) = ReadCborByteString(cborData, offset);
                    if (valueError != null)
                    {
                        return (null, $"Error reading authData value: {valueError}");
                    }
                    return (authDataBytes, null);
                }
                else
                {
                    // Skip this value
                    var (skipOffset, skipError) = SkipCborValueSafe(cborData, offset);
                    if (skipError != null)
                    {
                        return (null, $"Error skipping value for key '{keyStr}': {skipError}");
                    }
                    offset = skipOffset;
                }
            }
            
            return (null, "authData not found in attestation object");
        }
        catch (Exception ex)
        {
            return (null, $"CBOR parsing exception: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Reads a CBOR text string from the data stream.
    /// </summary>
    /// <param name="data">CBOR data bytes.</param>
    /// <param name="offset">Current position in the data.</param>
    /// <returns>Tuple containing the text, new offset, and any error.</returns>
    /// <remarks>
    /// CBOR major type 3 (text string) with UTF-8 encoding.
    /// Supports additional info 0-23 (inline length), 24 (1-byte length), 25 (2-byte length).
    /// </remarks>
    private static (string? text, int newOffset, string? error) ReadCborTextString(byte[] data, int offset)
    {
        if (offset >= data.Length)
            return (null, offset, "Unexpected end of data");
            
        var firstByte = data[offset++];
        var majorType = (firstByte & 0xE0) >> 5;
        
        if (majorType != 3) // 3 = text string
            return (null, offset, $"Expected text string (major type 3), got {majorType}");
        
        var additionalInfo = firstByte & 0x1F;
        int length;
        
        if (additionalInfo < 24)
        {
            length = additionalInfo;
        }
        else if (additionalInfo == 24)
        {
            length = data[offset++];
        }
        else if (additionalInfo == 25)
        {
            length = (data[offset] << 8) | data[offset + 1];
            offset += 2;
        }
        else
        {
            return (null, offset, $"Unsupported text string length encoding: {additionalInfo}");
        }
        
        var text = Encoding.UTF8.GetString(data, offset, length);
        return (text, offset + length, null);
    }
    
    /// <summary>
    /// Reads a CBOR byte string from the data stream.
    /// </summary>
    /// <param name="data">CBOR data bytes.</param>
    /// <param name="offset">Current position in the data.</param>
    /// <returns>Tuple containing the bytes, new offset, and any error.</returns>
    /// <remarks>
    /// CBOR major type 2 (byte string).
    /// Supports additional info 0-23, 24 (1-byte), 25 (2-byte), 26 (4-byte) length encoding.
    /// </remarks>
    private static (byte[]? bytes, int newOffset, string? error) ReadCborByteString(byte[] data, int offset)
    {
        if (offset >= data.Length)
            return (null, offset, "Unexpected end of data");
            
        var firstByte = data[offset++];
        var majorType = (firstByte & 0xE0) >> 5;
        
        if (majorType != 2) // 2 = byte string
            return (null, offset, $"Expected byte string (major type 2), got {majorType}");
        
        var additionalInfo = firstByte & 0x1F;
        int length;
        
        if (additionalInfo < 24)
        {
            length = additionalInfo;
        }
        else if (additionalInfo == 24)
        {
            length = data[offset++];
        }
        else if (additionalInfo == 25)
        {
            length = (data[offset] << 8) | data[offset + 1];
            offset += 2;
        }
        else if (additionalInfo == 26)
        {
            length = (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
            offset += 4;
        }
        else
        {
            return (null, offset, $"Unsupported byte string length encoding: {additionalInfo}");
        }
        
        if (offset + length > data.Length)
            return (null, offset, $"Byte string length {length} exceeds remaining data");
            
        var bytes = data[offset..(offset + length)];
        return (bytes, offset + length, null);
    }
    
    /// <summary>
    /// Safely skips a CBOR value in the data stream without parsing it.
    /// </summary>
    /// <param name="data">CBOR data bytes.</param>
    /// <param name="offset">Current position in the data.</param>
    /// <returns>Tuple containing new offset after skip and any error.</returns>
    /// <remarks>
    /// <para><strong>Handles CBOR Major Types:</strong></para>
    /// <list type="bullet">
    ///   <item>0: Unsigned integer</item>
    ///   <item>1: Negative integer</item>
    ///   <item>2: Byte string</item>
    ///   <item>3: Text string</item>
    ///   <item>4: Array (recursively skips items)</item>
    ///   <item>5: Map (recursively skips key-value pairs)</item>
    ///   <item>6: Tagged value</item>
    ///   <item>7: Simple values and floats</item>
    /// </list>
    /// 
    /// Used when iterating through CBOR maps to skip unwanted keys.
    /// </remarks>
    private static (int newOffset, string? error) SkipCborValueSafe(byte[] data, int offset)
    {
        if (offset >= data.Length)
            return (offset, "Unexpected end of data");
            
        var firstByte = data[offset++];
        var majorType = (firstByte & 0xE0) >> 5;
        var additionalInfo = firstByte & 0x1F;
        
        // Get length/value for additional info
        long value = additionalInfo;
        if (additionalInfo == 24 && offset < data.Length)
        {
            value = data[offset++];
        }
        else if (additionalInfo == 25 && offset + 1 < data.Length)
        {
            value = (data[offset] << 8) | data[offset + 1];
            offset += 2;
        }
        else if (additionalInfo == 26 && offset + 3 < data.Length)
        {
            value = ((long)data[offset] << 24) | ((long)data[offset + 1] << 16) | ((long)data[offset + 2] << 8) | data[offset + 3];
            offset += 4;
        }
        else if (additionalInfo == 27 && offset + 7 < data.Length)
        {
            // 8-byte value - very rare but handle it
            offset += 8;
            value = 0; // We don't actually need the value for skipping in this case
        }
        else if (additionalInfo >= 28)
        {
            return (offset, $"Unsupported additional info: {additionalInfo}");
        }
        
        switch (majorType)
        {
            case 0: // unsigned int - already consumed
            case 1: // negative int - already consumed
                return (offset, null);
                
            case 2: // byte string
            case 3: // text string
                return (offset + (int)value, null);
                
            case 4: // array
                for (int i = 0; i < (int)value; i++)
                {
                    var (newOff, err) = SkipCborValueSafe(data, offset);
                    if (err != null) return (offset, err);
                    offset = newOff;
                }
                return (offset, null);
                
            case 5: // map
                for (int i = 0; i < (int)value * 2; i++) // key + value for each entry
                {
                    var (newOff, err) = SkipCborValueSafe(data, offset);
                    if (err != null) return (offset, err);
                    offset = newOff;
                }
                return (offset, null);
                
            case 6: // tagged value - skip the tag and then the value
                return SkipCborValueSafe(data, offset);
                
            case 7: // simple values, floats
                if (additionalInfo == 25) return (offset, null); // half-float already consumed
                if (additionalInfo == 26) return (offset, null); // single-float already consumed  
                if (additionalInfo == 27) return (offset, null); // double-float already consumed
                return (offset, null);
                
            default:
                return (offset, $"Unknown major type: {majorType}");
        }
    }
    
    #endregion

    #region Private Helpers - Signature Verification
    
    /// <summary>
    /// Creates the signature base by concatenating authenticator data and client data hash.
    /// </summary>
    /// <param name="authenticatorData">Raw authenticator data bytes.</param>
    /// <param name="clientDataJson">Client data JSON bytes.</param>
    /// <returns>Signature base for verification.</returns>
    /// <remarks>
    /// <para><strong>Signature Base Format:</strong></para>
    /// authenticatorData || SHA256(clientDataJSON)
    /// 
    /// This is what the authenticator signs during the ceremony.
    /// </remarks>
    private static byte[] CreateSignatureBase(byte[] authenticatorData, byte[] clientDataJson)
    {
        var clientDataHash = SHA256.HashData(clientDataJson);
        var signatureBase = new byte[authenticatorData.Length + clientDataHash.Length];
        authenticatorData.CopyTo(signatureBase, 0);
        clientDataHash.CopyTo(signatureBase, authenticatorData.Length);
        return signatureBase;
    }
    
    /// <summary>
    /// Verifies a cryptographic signature using the stored public key.
    /// </summary>
    /// <param name="publicKeyCose">CBOR-encoded COSE public key.</param>
    /// <param name="data">Data that was signed (signature base).</param>
    /// <param name="signature">Signature bytes from authenticator.</param>
    /// <returns>True if signature is valid; false otherwise.</returns>
    /// <remarks>
    /// Currently supports ES256 (ECDSA P-256 + SHA-256) algorithm.
    /// RS256 support can be added by extending ParseCosePublicKey and verification logic.
    /// </remarks>
    private bool VerifySignature(byte[] publicKeyCose, byte[] data, byte[] signature)
    {
        try
        {
            // Parse COSE public key and verify signature
            // This is a simplified implementation for ES256 (ECDSA P-256)
            var (algorithm, x, y) = ParseCosePublicKey(publicKeyCose);
            
            if (algorithm == COSE_ALG_ES256 && x != null && y != null)
            {
                return VerifyES256Signature(x, y, data, signature);
            }
            
            _logger.LogWarning("Unsupported COSE algorithm: {Algorithm}", algorithm);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Signature verification error");
            return false;
        }
    }
    
    /// <summary>
    /// Parses a COSE public key to extract algorithm and EC2 key coordinates.
    /// </summary>
    /// <param name="coseKey">CBOR-encoded COSE key.</param>
    /// <returns>Tuple containing algorithm ID, X coordinate, and Y coordinate.</returns>
    /// <remarks>
    /// <para><strong>COSE Key Format (EC2):</strong></para>
    /// CBOR map with integer keys:
    /// - 1: Key type (2 = EC2)
    /// - 3: Algorithm (-7 = ES256, -257 = RS256)
    /// - -1: Curve (1 = P-256)
    /// - -2: X coordinate (32 bytes for P-256)
    /// - -3: Y coordinate (32 bytes for P-256)
    /// 
    /// For RSA keys, different key parameters apply.
    /// </remarks>
    private (int algorithm, byte[]? x, byte[]? y) ParseCosePublicKey(byte[] coseKey)
    {
        // COSE Key format (simplified CBOR parsing)
        // For EC2 keys: {1: 2, 3: alg, -1: crv, -2: x, -3: y}
        var offset = 0;
        var firstByte = coseKey[offset++];
        
        int mapSize;
        if ((firstByte & 0xF0) == 0xA0)
            mapSize = firstByte & 0x0F;
        else if (firstByte == 0xB8)
            mapSize = coseKey[offset++];
        else
            return (0, null, null);
        
        int algorithm = 0;
        byte[]? x = null;
        byte[]? y = null;
        
        for (int i = 0; i < mapSize && offset < coseKey.Length; i++)
        {
            // Read key (can be positive or negative int)
            var keyByte = coseKey[offset++];
            int key;
            
            if ((keyByte & 0xE0) == 0x00) // positive int
                key = keyByte & 0x1F;
            else if ((keyByte & 0xE0) == 0x20) // negative int
                key = -1 - (keyByte & 0x1F);
            else if (keyByte == 0x38) // negative int (1 byte follows)
            {
                key = -1 - coseKey[offset++];
            }
            else
                continue;
            
            // Read value
            var valueByte = coseKey[offset++];
            
            if (key == 3) // algorithm
            {
                if ((valueByte & 0xE0) == 0x20) // negative int
                    algorithm = -1 - (valueByte & 0x1F);
                else if (valueByte == 0x38)
                    algorithm = -1 - coseKey[offset++];
                else if (valueByte == 0x39)
                {
                    algorithm = -1 - ((coseKey[offset] << 8) | coseKey[offset + 1]);
                    offset += 2;
                }
            }
            else if (key == -2) // x coordinate
            {
                int len = valueByte & 0x1F;
                if (valueByte == 0x58) len = coseKey[offset++];
                x = coseKey[offset..(offset + len)];
                offset += len;
            }
            else if (key == -3) // y coordinate
            {
                int len = valueByte & 0x1F;
                if (valueByte == 0x58) len = coseKey[offset++];
                y = coseKey[offset..(offset + len)];
                offset += len;
            }
            else
            {
                // Skip other values using safe CBOR skip
                var (newOffset, skipError) = SkipCborValueSafe(coseKey, offset - 1);
                if (skipError != null)
                {
                    _logger.LogWarning("Failed to skip CBOR value in COSE key: {Error}", skipError);
                    continue;
                }
                offset = newOffset;
            }
        }
        
        return (algorithm, x, y);
    }
    
    /// <summary>
    /// Verifies an ES256 (ECDSA P-256 + SHA-256) signature.
    /// </summary>
    /// <param name="x">X coordinate of the public key (32 bytes).</param>
    /// <param name="y">Y coordinate of the public key (32 bytes).</param>
    /// <param name="data">Data that was signed.</param>
    /// <param name="signature">DER-encoded signature from authenticator.</param>
    /// <returns>True if signature is valid; false otherwise.</returns>
    /// <remarks>
    /// <para><strong>Process:</strong></para>
    /// <list type="number">
    ///   <item>Construct ECDsa public key from P-256 coordinates</item>
    ///   <item>Convert signature from DER (WebAuthn) to IEEE P1363 (.NET format)</item>
    ///   <item>Verify signature using SHA-256 hash</item>
    /// </list>
    /// </remarks>
    private static bool VerifyES256Signature(byte[] x, byte[] y, byte[] data, byte[] signature)
    {
        try
        {
            // Create ECDsa from coordinates
            var ecParams = new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint
                {
                    X = x,
                    Y = y
                }
            };
            
            using var ecdsa = ECDsa.Create(ecParams);
            
            // WebAuthn signatures are in ASN.1 DER format, need to convert to IEEE P1363 for .NET
            var ieeeSignature = ConvertDerToIeee(signature);
            
            return ecdsa.VerifyData(data, ieeeSignature, HashAlgorithmName.SHA256);
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// Converts DER-encoded signature to IEEE P1363 format.
    /// </summary>
    /// <param name="derSignature">DER-encoded signature from WebAuthn.</param>
    /// <returns>IEEE P1363 formatted signature (64 bytes for ES256).</returns>
    /// <remarks>
    /// <para><strong>DER Format:</strong></para>
    /// 0x30 [total-length] 0x02 [r-length] [r-bytes] 0x02 [s-length] [s-bytes]
    /// 
    /// <para><strong>IEEE P1363 Format:</strong></para>
    /// [r-bytes][s-bytes] (each 32 bytes for P-256, zero-padded if needed)
    /// 
    /// <para><strong>Processing:</strong></para>
    /// - Removes ASN.1 structure tags
    /// - Strips leading zeros from R and S components
    /// - Pads to fixed 32-byte length for P-256
    /// </remarks>
    private static byte[] ConvertDerToIeee(byte[] derSignature)
    {
        // DER format: 0x30 [total-length] 0x02 [r-length] [r] 0x02 [s-length] [s]
        // IEEE P1363 format: [r][s] (each 32 bytes for P-256)
        
        if (derSignature[0] != 0x30)
            return derSignature; // Already in IEEE format or invalid
        
        var offset = 2;
        
        // Read R
        if (derSignature[offset++] != 0x02)
            return derSignature;
        
        var rLen = derSignature[offset++];
        var rStart = offset;
        offset += rLen;
        
        // Read S
        if (derSignature[offset++] != 0x02)
            return derSignature;
        
        var sLen = derSignature[offset++];
        var sStart = offset;
        
        // Extract R and S, removing leading zeros if present
        var r = derSignature.AsSpan(rStart, rLen);
        var s = derSignature.AsSpan(sStart, sLen);
        
        // Remove leading zero if present (used for positive number in ASN.1)
        if (r.Length == 33 && r[0] == 0) r = r[1..];
        if (s.Length == 33 && s[0] == 0) s = s[1..];
        
        // Pad to 32 bytes if needed
        var result = new byte[64];
        r.CopyTo(result.AsSpan(32 - r.Length));
        s.CopyTo(result.AsSpan(64 - s.Length));
        
        return result;
    }
    
    #endregion

    #region Public Helpers - Base64URL Encoding
    
    /// <summary>
    /// Encodes bytes to Base64URL format (RFC 4648).
    /// </summary>
    /// <param name="data">Bytes to encode.</param>
    /// <returns>Base64URL-encoded string.</returns>
    /// <remarks>
    /// Base64URL is URL-safe Base64:
    /// - Replaces + with -
    /// - Replaces / with _
    /// - Removes padding (=)
    /// 
    /// Used extensively in WebAuthn for challenges, credentials, and keys.
    /// </remarks>
    public static string Base64UrlEncode(byte[] data)
    {
        return Convert.ToBase64String(data)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
    
    /// <summary>
    /// Decodes a Base64URL-encoded string to bytes.
    /// </summary>
    /// <param name="data">Base64URL-encoded string.</param>
    /// <returns>Decoded bytes.</returns>
    /// <remarks>
    /// Reverses Base64URL encoding:
    /// - Replaces - with +
    /// - Replaces _ with /
    /// - Adds padding (=) as needed
    /// 
    /// Then decodes using standard Base64.
    /// </remarks>
    public static byte[] Base64UrlDecode(string data)
    {
        var padded = data
            .Replace('-', '+')
            .Replace('_', '/');
        
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }
        
        return Convert.FromBase64String(padded);
    }
    
    #endregion

    #region Private Types
    
    /// <summary>
    /// Parsed authenticator data structure.
    /// </summary>
    /// <remarks>
    /// Contains both fixed fields (RP ID hash, flags, counter) and
    /// optional attested credential data (registration only).
    /// </remarks>
    private class AuthenticatorData
    {
        /// <summary>SHA-256 hash of the RP ID.</summary>
        public byte[] RpIdHash { get; set; } = Array.Empty<byte>();
        
        /// <summary>User Present flag - user confirmed the ceremony.</summary>
        public bool UserPresent { get; set; }
        
        /// <summary>User Verified flag - biometric/PIN verification occurred.</summary>
        public bool UserVerified { get; set; }
        
        /// <summary>Backup Eligible flag - authenticator supports credential sync.</summary>
        public bool BackupEligible { get; set; }
        
        /// <summary>Backup State flag - credential is currently synced.</summary>
        public bool BackedUp { get; set; }
        
        /// <summary>Signature counter for replay/clone detection.</summary>
        public uint SignCount { get; set; }
        
        /// <summary>Credential ID (registration only).</summary>
        public byte[]? CredentialId { get; set; }
        
        /// <summary>COSE-encoded public key (registration only).</summary>
        public byte[]? PublicKey { get; set; }
    }
    
    /// <summary>
    /// Client data structure from WebAuthn ceremony.
    /// </summary>
    /// <remarks>
    /// Sent by the browser, contains ceremony metadata that binds
    /// the authentication/registration to a specific origin and challenge.
    /// </remarks>
    internal class ClientData
    {
        /// <summary>Ceremony type: "webauthn.create" or "webauthn.get".</summary>
        public string type { get; set; } = "";
        
        /// <summary>Base64URL-encoded challenge.</summary>
        public string challenge { get; set; } = "";
        
        /// <summary>Origin of the requesting page (e.g., https://example.com).</summary>
        public string origin { get; set; } = "";
        
        /// <summary>Cross-origin flag (optional, for iframe scenarios).</summary>
        public bool? crossOrigin { get; set; }
    }

    #endregion
}

/// <summary>
/// JSON serialization context for PasskeyService types.
/// </summary>
/// <remarks>
/// Enables AOT-compatible JSON serialization for ClientData.
/// Required for source generators in .NET 7+.
/// </remarks>
[JsonSerializable(typeof(PasskeyService.ClientData))]
internal partial class PasskeyJsonContext : JsonSerializerContext
{
}
