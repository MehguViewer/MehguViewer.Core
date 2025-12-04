using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MehguViewer.Shared.Models;

namespace MehguViewer.Core.Backend.Services;

/// <summary>
/// Service for WebAuthn/Passkey operations including challenge generation,
/// registration verification, and authentication verification.
/// 
/// This is a simplified implementation for AOT compatibility.
/// Uses ES256 (ECDSA with P-256 and SHA-256) as the primary algorithm.
/// </summary>
public class PasskeyService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<PasskeyService> _logger;
    
    // Challenge storage with expiration (5 minutes)
    private static readonly ConcurrentDictionary<string, (string challenge, DateTime expiry, string? userId)> _challenges = new();
    
    // COSE algorithm identifiers
    private const int COSE_ALG_ES256 = -7;    // ECDSA w/ SHA-256
    private const int COSE_ALG_RS256 = -257;  // RSASSA-PKCS1-v1_5 w/ SHA-256
    
    public PasskeyService(IConfiguration configuration, ILogger<PasskeyService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }
    
    /// <summary>
    /// Get the Relying Party ID (domain) for WebAuthn operations.
    /// </summary>
    public string GetRpId()
    {
        // Get from configuration or derive from request host
        var rpId = _configuration["Auth:Passkey:RpId"];
        if (!string.IsNullOrEmpty(rpId))
            return rpId;
        
        // Default to localhost for development
        return "localhost";
    }
    
    /// <summary>
    /// Get the Relying Party name for display.
    /// </summary>
    public string GetRpName()
    {
        return _configuration["Auth:Passkey:RpName"] ?? "MehguViewer";
    }
    
    /// <summary>
    /// Generate registration options for a new passkey.
    /// </summary>
    public PasskeyRegistrationOptions GenerateRegistrationOptions(User user, IEnumerable<Passkey> existingPasskeys)
    {
        var challenge = GenerateChallenge();
        var challengeId = Guid.NewGuid().ToString();
        
        // Store challenge with user context (expires in 5 minutes)
        _challenges[challengeId] = (challenge, DateTime.UtcNow.AddMinutes(5), user.id);
        
        // Build exclude credentials list (existing passkeys for this user)
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
            timeout: 300000, // 5 minutes
            attestation: "none", // Don't require attestation for privacy
            authenticator_selection_resident_key: "preferred",
            authenticator_selection_user_verification: "preferred"
        );
    }
    
    /// <summary>
    /// Verify registration response and extract credential data.
    /// </summary>
    public (bool success, string? credentialId, string? publicKey, long signCount, bool backedUp, string? deviceType, string? error) 
        VerifyRegistration(PasskeyRegistrationRequest request, string expectedChallenge, string expectedUserId, string expectedOrigin)
    {
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
            
            // Verify RP ID hash
            var expectedRpIdHash = SHA256.HashData(Encoding.UTF8.GetBytes(GetRpId()));
            if (!authData.RpIdHash.SequenceEqual(expectedRpIdHash))
                return (false, null, null, 0, false, null, "RP ID hash mismatch");
            
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
            
            return (true, credentialId, publicKey, authData.SignCount, authData.BackedUp, deviceType, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Passkey registration verification failed");
            return (false, null, null, 0, false, null, "Verification failed: " + ex.Message);
        }
    }
    
    /// <summary>
    /// Generate authentication options for passkey login.
    /// </summary>
    public (PasskeyAuthenticationOptions options, string challengeId) GenerateAuthenticationOptions(IEnumerable<Passkey>? allowedPasskeys = null)
    {
        var challenge = GenerateChallenge();
        var challengeId = Guid.NewGuid().ToString();
        
        // Store challenge (expires in 5 minutes)
        _challenges[challengeId] = (challenge, DateTime.UtcNow.AddMinutes(5), null);
        
        PasskeyAllowCredential[]? allowCredentials = null;
        if (allowedPasskeys != null && allowedPasskeys.Any())
        {
            allowCredentials = allowedPasskeys
                .Select(p => new PasskeyAllowCredential("public-key", p.credential_id))
                .ToArray();
        }
        
        var options = new PasskeyAuthenticationOptions(
            challenge: challenge,
            timeout: 300000,
            rp_id: GetRpId(),
            allow_credentials: allowCredentials,
            user_verification: "preferred"
        );
        
        return (options, challengeId);
    }
    
    /// <summary>
    /// Verify authentication response.
    /// </summary>
    public (bool success, string? userId, long newSignCount, string? error) 
        VerifyAuthentication(PasskeyAuthenticationRequest request, Passkey passkey, string expectedChallenge, string expectedOrigin)
    {
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
            
            // Verify RP ID hash
            var expectedRpIdHash = SHA256.HashData(Encoding.UTF8.GetBytes(GetRpId()));
            if (!authData.RpIdHash.SequenceEqual(expectedRpIdHash))
                return (false, null, 0, "RP ID hash mismatch");
            
            // Verify user presence
            if (!authData.UserPresent)
                return (false, null, 0, "User presence not verified");
            
            // Verify signature counter (replay detection)
            if (authData.SignCount > 0 && passkey.sign_count > 0 && authData.SignCount <= passkey.sign_count)
            {
                _logger.LogWarning("Passkey signature counter did not increase. Possible cloned authenticator.");
                // Note: Some implementations may want to fail here, but we'll allow it with a warning
            }
            
            // Verify signature
            var signatureBase = CreateSignatureBase(authenticatorData, clientDataJson);
            var signature = Base64UrlDecode(request.response.signature);
            var publicKey = Base64UrlDecode(passkey.public_key);
            
            if (!VerifySignature(publicKey, signatureBase, signature))
                return (false, null, 0, "Signature verification failed");
            
            return (true, passkey.user_id, authData.SignCount, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Passkey authentication verification failed");
            return (false, null, 0, "Verification failed: " + ex.Message);
        }
    }
    
    /// <summary>
    /// Validate and consume a challenge.
    /// </summary>
    public (bool valid, string? challenge, string? userId) ValidateChallenge(string challengeId)
    {
        // Clean up expired challenges
        var now = DateTime.UtcNow;
        var expiredKeys = _challenges.Where(kvp => kvp.Value.expiry < now).Select(kvp => kvp.Key).ToList();
        foreach (var key in expiredKeys)
            _challenges.TryRemove(key, out _);
        
        // Validate and consume the challenge
        if (_challenges.TryRemove(challengeId, out var data))
        {
            if (data.expiry > now)
                return (true, data.challenge, data.userId);
        }
        
        return (false, null, null);
    }
    
    /// <summary>
    /// Store a challenge for later validation.
    /// </summary>
    public string StoreChallenge(string challenge, string? userId = null)
    {
        var challengeId = Guid.NewGuid().ToString();
        _challenges[challengeId] = (challenge, DateTime.UtcNow.AddMinutes(5), userId);
        return challengeId;
    }
    
    #region Private Helpers
    
    private static string GenerateChallenge()
    {
        var bytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Base64UrlEncode(bytes);
    }
    
    private static bool VerifyOrigin(string receivedOrigin, string expectedOrigin)
    {
        // Allow localhost variations for development
        if (expectedOrigin.Contains("localhost") || expectedOrigin.Contains("127.0.0.1"))
        {
            return receivedOrigin.Contains("localhost") || receivedOrigin.Contains("127.0.0.1");
        }
        
        // For production, compare origin strictly
        return receivedOrigin == expectedOrigin || receivedOrigin.StartsWith(expectedOrigin);
    }
    
    private static (AuthenticatorData? data, string? error) ParseAttestationObject(byte[] attestationObject)
    {
        try
        {
            // CBOR decode the attestation object
            // Format: { "fmt": string, "attStmt": {...}, "authData": bytes }
            var (authDataBytes, error) = ExtractAuthDataFromCbor(attestationObject);
            if (authDataBytes == null)
                return (null, error);
            
            return ParseAuthenticatorData(authDataBytes, includeCredentialData: true);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }
    
    private static (AuthenticatorData? data, string? error) ParseAuthenticatorData(byte[] authData, bool includeCredentialData = false)
    {
        try
        {
            if (authData.Length < 37)
                return (null, "Authenticator data too short");
            
            var result = new AuthenticatorData
            {
                RpIdHash = authData[..32]
            };
            
            var flags = authData[32];
            result.UserPresent = (flags & 0x01) != 0;
            result.UserVerified = (flags & 0x04) != 0;
            result.BackupEligible = (flags & 0x08) != 0;
            result.BackedUp = (flags & 0x10) != 0;
            var hasAttestedCredentialData = (flags & 0x40) != 0;
            
            result.SignCount = BitConverter.ToUInt32(authData.AsSpan(33, 4).ToArray().Reverse().ToArray(), 0);
            
            if (includeCredentialData && hasAttestedCredentialData && authData.Length > 37)
            {
                // Parse attested credential data
                // Format: AAGUID (16) || credIdLen (2) || credId (credIdLen) || credPubKey (CBOR)
                var offset = 37;
                
                // Skip AAGUID (16 bytes)
                offset += 16;
                
                // Read credential ID length (big-endian)
                var credIdLen = (authData[offset] << 8) | authData[offset + 1];
                offset += 2;
                
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
            return (null, ex.Message);
        }
    }
    
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
    
    // Legacy method - kept for compatibility but not used
    private static int SkipCborValue(byte[] data, int offset)
    {
        var firstByte = data[offset++];
        var majorType = (firstByte & 0xE0) >> 5;
        var additionalInfo = firstByte & 0x1F;
        
        int length = additionalInfo;
        if (additionalInfo == 24) length = data[offset++];
        else if (additionalInfo == 25) { length = (data[offset] << 8) | data[offset + 1]; offset += 2; }
        else if (additionalInfo == 26) { length = (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]; offset += 4; }
        
        switch (majorType)
        {
            case 0: // unsigned int
            case 1: // negative int
                return offset;
            case 2: // byte string
            case 3: // text string
                return offset + length;
            case 4: // array
                for (int i = 0; i < length; i++)
                    offset = SkipCborValue(data, offset);
                return offset;
            case 5: // map
                for (int i = 0; i < length * 2; i++)
                    offset = SkipCborValue(data, offset);
                return offset;
            default:
                return offset;
        }
    }
    
    private static byte[] CreateSignatureBase(byte[] authenticatorData, byte[] clientDataJson)
    {
        var clientDataHash = SHA256.HashData(clientDataJson);
        var signatureBase = new byte[authenticatorData.Length + clientDataHash.Length];
        authenticatorData.CopyTo(signatureBase, 0);
        clientDataHash.CopyTo(signatureBase, authenticatorData.Length);
        return signatureBase;
    }
    
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
    
    private static (int algorithm, byte[]? x, byte[]? y) ParseCosePublicKey(byte[] coseKey)
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
                // Skip other values
                offset = SkipCborValue(coseKey, offset - 1);
            }
        }
        
        return (algorithm, x, y);
    }
    
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
    
    public static string Base64UrlEncode(byte[] data)
    {
        return Convert.ToBase64String(data)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
    
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
    
    private class AuthenticatorData
    {
        public byte[] RpIdHash { get; set; } = Array.Empty<byte>();
        public bool UserPresent { get; set; }
        public bool UserVerified { get; set; }
        public bool BackupEligible { get; set; }
        public bool BackedUp { get; set; }
        public uint SignCount { get; set; }
        public byte[]? CredentialId { get; set; }
        public byte[]? PublicKey { get; set; }
    }
    
    internal class ClientData
    {
        public string type { get; set; } = "";
        public string challenge { get; set; } = "";
        public string origin { get; set; } = "";
        public bool? crossOrigin { get; set; }
    }
}

[JsonSerializable(typeof(PasskeyService.ClientData))]
internal partial class PasskeyJsonContext : JsonSerializerContext
{
}
