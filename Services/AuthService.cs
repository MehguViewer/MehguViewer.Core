using MehguViewer.Core.Helpers;
using System.Diagnostics.CodeAnalysis;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using MehguViewer.Core.Shared;
using Microsoft.IdentityModel.Tokens;

namespace MehguViewer.Core.Services;

/// <summary>
/// Provides authentication services including JWT token generation, validation,
/// and password hashing/verification with support for legacy password migration.
/// </summary>
/// <remarks>
/// This service handles:
/// - RSA-based JWT token generation and validation
/// - Bcrypt password hashing with configurable work factor
/// - Legacy SHA256 password verification for migration
/// - Password strength validation
/// - Role-based scope assignment
/// </remarks>
public sealed class AuthService : IDisposable
{
    #region Constants

    /// <summary>JWT token issuer claim value.</summary>
    private const string Issuer = "https://auth.mehgu.example.com";
    
    /// <summary>JWT token audience claim value.</summary>
    private const string Audience = "mehgu-core";
    
    /// <summary>Token validity duration in hours.</summary>
    private const int TokenExpiryHours = 24;
    
    /// <summary>Bcrypt computational cost factor (2^12 iterations).</summary>
    private const int BcryptWorkFactor = 12;
    
    /// <summary>Minimum acceptable password length.</summary>
    private const int MinPasswordLength = 8;
    
    /// <summary>Maximum acceptable password length (prevents DoS attacks).</summary>
    private const int MaxPasswordLength = 128;
    
    /// <summary>Directory for storing cryptographic keys.</summary>
    private const string KeyDirectory = "keys";
    
    /// <summary>Filename for the JWT signing key.</summary>
    private const string KeyFileName = "jwt-signing-key.bin";
    
    /// <summary>RSA key size in bits.</summary>
    private const int RsaKeySize = 2048;
    
    /// <summary>Clock skew tolerance for token validation.</summary>
    private static readonly TimeSpan ClockSkewTolerance = TimeSpan.FromMinutes(1);

    #endregion

    #region Fields

    private readonly ILogger<AuthService> _logger;
    private readonly RSA _rsaKey;
    private bool _disposed;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthService"/> class.
    /// </summary>
    /// <param name="logger">Logger instance for diagnostic and security logging.</param>
    public AuthService(ILogger<AuthService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _rsaKey = GetOrCreateRsaKey();
    }

    #endregion

    #region Key Management

    /// <summary>
    /// Loads an existing RSA private key from disk or generates a new one if not found.
    /// </summary>
    /// <returns>An RSA instance containing the signing key.</returns>
    /// <remarks>
    /// Keys are stored in the 'keys' directory with restrictive permissions on Unix systems.
    /// If key loading fails due to corruption, a new key is automatically generated.
    /// </remarks>
    private RSA GetOrCreateRsaKey()
    {
        var rsa = RSA.Create(RsaKeySize);
        var keyFilePath = Path.Combine(KeyDirectory, KeyFileName);
        
        _logger.LogDebug("Initializing RSA key management. Checking for existing key at: {KeyPath}", keyFilePath);
        
        if (File.Exists(keyFilePath))
        {
            _logger.LogDebug("Existing key file found. Attempting to load...");
            if (TryLoadExistingKey(rsa, keyFilePath))
            {
                _logger.LogInformation("Successfully initialized AuthService with existing RSA key");
                return rsa;
            }
            
            _logger.LogWarning("Failed to load existing key. Will generate new key. Previous tokens will be invalidated.");
        }
        else
        {
            _logger.LogInformation("No existing RSA key found at {KeyPath}. Generating new key...", keyFilePath);
        }

        GenerateAndSaveKey(rsa, keyFilePath);
        _logger.LogInformation("AuthService initialized successfully with new RSA key");
        return rsa;
    }

    /// <summary>
    /// Attempts to load an existing RSA key from the specified file path.
    /// </summary>
    /// <param name="rsa">The RSA instance to import the key into.</param>
    /// <param name="keyFilePath">The path to the key file.</param>
    /// <returns>True if the key was successfully loaded; otherwise, false.</returns>
    private bool TryLoadExistingKey(RSA rsa, string keyFilePath)
    {
        try 
        {
            _logger.LogTrace("Reading key file from disk...");
            
            // Security: Read file with FileOptions for better performance and security
            using var fileStream = new FileStream(
                keyFilePath, 
                FileMode.Open, 
                FileAccess.Read, 
                FileShare.None,
                bufferSize: 4096,
                FileOptions.SequentialScan);
            
            // Security: Validate key size before reading into memory
            if (fileStream.Length < 100 || fileStream.Length > 10000)
            {
                _logger.LogError("Key file size {Size} bytes is suspicious. Expected range: 100-10000 bytes", fileStream.Length);
                return false;
            }
            
            var keyBytes = new byte[fileStream.Length];
            var bytesRead = fileStream.Read(keyBytes, 0, keyBytes.Length);
            
            if (bytesRead != keyBytes.Length)
            {
                _logger.LogError("Failed to read complete key file. Expected {Expected} bytes, got {Actual} bytes", keyBytes.Length, bytesRead);
                return false;
            }
            
            _logger.LogTrace("Importing RSA private key ({Size} bytes)...", keyBytes.Length);
            rsa.ImportRSAPrivateKey(keyBytes, out var importedBytes);
            
            if (importedBytes != keyBytes.Length)
            {
                _logger.LogWarning("Key import consumed {BytesRead} bytes but file contains {TotalBytes} bytes", importedBytes, keyBytes.Length);
            }
            
            // Security: Clear sensitive data from memory
            Array.Clear(keyBytes, 0, keyBytes.Length);
            
            _logger.LogInformation("Successfully loaded existing RSA signing key from {Path} ({Size} bytes)", keyFilePath, fileStream.Length);
            return true;
        }
        catch (CryptographicException ex)
        {
            _logger.LogError(ex, "Cryptographic error reading RSA key from {Path}. Key may be corrupted or invalid format.", keyFilePath);
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Access denied reading RSA key from {Path}. Check file permissions.", keyFilePath);
            return false;
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "I/O error reading RSA key from {Path}.", keyFilePath);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error reading RSA key from {Path}", keyFilePath);
            return false;
        }
    }

    /// <summary>
    /// Generates a new RSA key and saves it to disk with appropriate permissions.
    /// </summary>
    /// <param name="rsa">The RSA instance to export the key from.</param>
    /// <param name="keyFilePath">The path where the key will be saved.</param>
    private void GenerateAndSaveKey(RSA rsa, string keyFilePath)
    {
        try
        {
            _logger.LogDebug("Creating key directory: {KeyDirectory}", KeyDirectory);
            Directory.CreateDirectory(KeyDirectory);
            
            _logger.LogTrace("Exporting RSA private key...");
            var privateKey = rsa.ExportRSAPrivateKey();
            _logger.LogDebug("Exported RSA private key ({Size} bytes)", privateKey.Length);
            
            _logger.LogTrace("Writing key to disk: {Path}", keyFilePath);
            File.WriteAllBytes(keyFilePath, privateKey);
            
            // Security: Set restrictive file permissions on Unix-like systems
            if (!OperatingSystem.IsWindows())
            {
                _logger.LogTrace("Setting Unix file permissions to 0600 (owner read/write only)");
                File.SetUnixFileMode(keyFilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                _logger.LogDebug("Applied restrictive permissions (0600) on key file for enhanced security");
            }
            else
            {
                _logger.LogDebug("Running on Windows - file permissions managed by NTFS ACLs");
            }
            
            _logger.LogInformation("Successfully generated and persisted new RSA signing key to {Path} ({Size} bytes)", keyFilePath, privateKey.Length);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogCritical(ex, "Access denied when attempting to persist RSA key to {Path}. Tokens will be invalidated on restart!", keyFilePath);
        }
        catch (IOException ex)
        {
            _logger.LogCritical(ex, "I/O error persisting RSA key to {Path}. Tokens will be invalidated on restart!", keyFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Unexpected error persisting RSA key to {Path}. Tokens will be invalidated on restart!", keyFilePath);
        }
    }

    #endregion

    #region Token Generation

    /// <summary>
    /// Generates a signed JWT token for the specified user with role-based claims and scopes.
    /// </summary>
    /// <param name="user">The user for whom to generate the token.</param>
    /// <returns>A signed JWT token string.</returns>
    /// <exception cref="ArgumentNullException">Thrown when user is null.</exception>
    /// <exception cref="ArgumentException">Thrown when user has invalid id or username.</exception>
    /// <remarks>
    /// The token includes:
    /// - Standard claims (sub, name, role)
    /// - OAuth 2.0 scopes based on user role
    /// - 24-hour expiration
    /// - RS256 signature algorithm
    /// </remarks>
    public string GenerateToken(User user)
    {
        ThrowIfDisposed();
        
        // Input validation with detailed logging
        if (user == null)
        {
            _logger.LogError("Token generation failed: user parameter is null");
            throw new ArgumentNullException(nameof(user));
        }

        if (string.IsNullOrWhiteSpace(user.id))
        {
            _logger.LogError("Token generation failed: user.id is null or whitespace");
            throw new ArgumentException("User must have valid id", nameof(user));
        }
        
        if (string.IsNullOrWhiteSpace(user.username))
        {
            _logger.LogError("Token generation failed for user {UserId}: username is null or whitespace", user.id);
            throw new ArgumentException("User must have valid username", nameof(user));
        }

        _logger.LogDebug("Generating JWT token for user {UserId} ({Username}) with role {Role}", user.id, user.username, user.role);

        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = new RsaSecurityKey(_rsaKey);
            var scopes = GetScopesForRole(user.role);
            var expiryTime = DateTime.UtcNow.AddHours(TokenExpiryHours);

            _logger.LogTrace("Creating token descriptor with issuer {Issuer}, audience {Audience}", Issuer, Audience);
            
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[]
                {
                    new Claim("sub", user.id),
                    new Claim("scope", scopes),
                    new Claim(ClaimTypes.Name, user.username),
                    new Claim(ClaimTypes.Role, user.role)
                }),
                Expires = expiryTime,
                Issuer = Issuer,
                Audience = Audience,
                SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256)
            };

            _logger.LogTrace("Creating and signing token...");
            var token = tokenHandler.CreateToken(tokenDescriptor);
            var tokenString = tokenHandler.WriteToken(token);
            
            _logger.LogInformation("Successfully generated JWT for user {UserId} ({Username}) with role {Role}", 
                user.id, user.username, user.role);
            _logger.LogDebug("Token details - Scopes: [{Scopes}], Expires: {ExpiryTime:u}, Length: {TokenLength} chars", 
                scopes, expiryTime, tokenString.Length);
            
            return tokenString;
        }
        catch (CryptographicException ex)
        {
            _logger.LogError(ex, "Cryptographic error generating JWT token for user {UserId}. RSA key may be invalid.", user.id);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error generating JWT token for user {UserId}", user.id);
            throw;
        }
    }

    /// <summary>
    /// Gets the token validation parameters for JWT verification.
    /// </summary>
    /// <returns>Configured token validation parameters.</returns>
    /// <remarks>
    /// Validates:
    /// - Signature using RSA public key
    /// - Issuer and audience claims
    /// - Token lifetime with 1-minute clock skew tolerance
    /// </remarks>
    public TokenValidationParameters GetValidationParameters()
    {
        ThrowIfDisposed();
        
        _logger.LogTrace("Creating token validation parameters for issuer {Issuer}, audience {Audience}", Issuer, Audience);
        
        var parameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new RsaSecurityKey(_rsaKey),
            ValidateIssuer = true,
            ValidIssuer = Issuer,
            ValidateAudience = true,
            ValidAudience = Audience,
            ValidateLifetime = true,
            ClockSkew = ClockSkewTolerance
        };
        
        _logger.LogDebug("Token validation parameters configured with {ClockSkew} clock skew tolerance", ClockSkewTolerance);
        return parameters;
    }

    /// <summary>
    /// Returns the JSON Web Key (JWK) representation of the public key for external verification.
    /// </summary>
    /// <returns>A JWK containing the public key parameters.</returns>
    /// <remarks>
    /// Used for external services that need to verify tokens without accessing the private key.
    /// </remarks>
    public JsonWebKey GetJwk()
    {
        ThrowIfDisposed();
        
        _logger.LogTrace("Exporting RSA public key parameters for JWK");
        
        try
        {
            var parameters = _rsaKey.ExportParameters(false);
            var jwk = new JsonWebKey
            {
                Kty = JsonWebAlgorithmsKeyTypes.RSA,
                Use = "sig",
                Kid = "mehgu-core-key-1", // TODO: Implement key rotation with dynamic kid
                E = Base64UrlEncoder.Encode(parameters.Exponent),
                N = Base64UrlEncoder.Encode(parameters.Modulus),
                Alg = SecurityAlgorithms.RsaSha256
            };
            
            _logger.LogDebug("Exported JWK with key ID {KeyId}, algorithm {Algorithm}", jwk.Kid, jwk.Alg);
            return jwk;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export JWK from RSA key");
            throw;
        }
    }

    #endregion

    #region Password Management

    /// <summary>
    /// Hashes a password using bcrypt with a secure work factor.
    /// </summary>
    /// <param name="password">The plaintext password to hash.</param>
    /// <returns>A bcrypt hash string.</returns>
    /// <exception cref="ArgumentException">Thrown when password is null or empty.</exception>
    /// <remarks>
    /// Uses bcrypt work factor of 12 for a balance between security and performance.
    /// Work factor determines the computational cost of hashing.
    /// </remarks>
    [UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode", Justification = "BCrypt.Net-Next is preserved via TrimmerRootAssembly")]
    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode", Justification = "BCrypt.Net-Next is preserved via TrimmerRootAssembly")]
    public string HashPassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            _logger.LogError("Password hashing failed: password is null or whitespace");
            throw new ArgumentException("Password cannot be null or empty", nameof(password));
        }

        _logger.LogTrace("Hashing password with bcrypt (work factor: {WorkFactor})", BcryptWorkFactor);

        try
        {
            var hash = BCrypt.Net.BCrypt.HashPassword(password, BcryptWorkFactor);
            _logger.LogDebug("Successfully hashed password with bcrypt work factor {WorkFactor} (hash length: {Length})", 
                BcryptWorkFactor, hash.Length);
            return hash;
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex, "Invalid argument when hashing password");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error hashing password with bcrypt");
            throw;
        }
    }

    /// <summary>
    /// Verifies a password against a stored hash, supporting both bcrypt and legacy SHA256 hashes.
    /// </summary>
    /// <param name="password">The plaintext password to verify.</param>
    /// <param name="hash">The stored password hash.</param>
    /// <returns>True if the password matches the hash; otherwise, false.</returns>
    /// <remarks>
    /// Automatically detects hash format:
    /// - Bcrypt hashes (starts with $2) use bcrypt verification
    /// - Legacy hashes use SHA256 comparison for backward compatibility
    /// </remarks>
    [UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode", Justification = "BCrypt.Net-Next is preserved via TrimmerRootAssembly")]
    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode", Justification = "BCrypt.Net-Next is preserved via TrimmerRootAssembly")]
    public bool VerifyPassword(string password, string hash)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            _logger.LogWarning("Password verification failed: password is null or whitespace");
            return false;
        }
        
        if (string.IsNullOrWhiteSpace(hash))
        {
            _logger.LogWarning("Password verification failed: hash is null or whitespace");
            return false;
        }

        _logger.LogTrace("Verifying password against stored hash (hash type: {HashType})", 
            hash.StartsWith("$2") ? "bcrypt" : "legacy");

        // Check if it's a bcrypt hash (starts with $2)
        if (hash.StartsWith("$2"))
        {
            return VerifyBcryptPassword(password, hash);
        }
        
        // Legacy SHA256 support for migration
        _logger.LogWarning("Verifying password using INSECURE legacy SHA256 hash. User should re-authenticate to upgrade to bcrypt.");
        return VerifyLegacySha256(password, hash);
    }

    /// <summary>
    /// Verifies a password against a bcrypt hash.
    /// </summary>
    /// <param name="password">The plaintext password.</param>
    /// <param name="hash">The bcrypt hash.</param>
    /// <returns>True if password matches; otherwise, false.</returns>
    /// <remarks>
    /// Uses constant-time comparison to prevent timing attacks.
    /// </remarks>
    private bool VerifyBcryptPassword(string password, string hash)
    {
        try
        {
            _logger.LogTrace("Performing bcrypt verification...");
            var isValid = BCrypt.Net.BCrypt.Verify(password, hash);
            
            // Security: Log result without revealing whether it passed or failed to prevent information leakage
            if (isValid)
            {
                _logger.LogDebug("Password verification completed successfully");
            }
            else
            {
                _logger.LogInformation("Password verification failed - invalid credentials provided");
            }
            
            return isValid;
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid bcrypt hash format provided for verification");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error verifying bcrypt hash - hash may be corrupted or invalid");
            return false;
        }
    }

    /// <summary>
    /// Verifies a password against a legacy SHA256 hash.
    /// </summary>
    /// <param name="password">The plaintext password.</param>
    /// <param name="hash">The SHA256 hash.</param>
    /// <returns>True if password matches; otherwise, false.</returns>
    /// <remarks>
    /// WARNING: SHA256 without salt is cryptographically weak for password storage.
    /// This method exists only for backward compatibility during migration.
    /// Users should be prompted to reset passwords to upgrade to bcrypt.
    /// </remarks>
    private bool VerifyLegacySha256(string password, string hash)
    {
        _logger.LogTrace("Performing legacy SHA256 verification (INSECURE)");
        
        try
        {
            using var sha256 = SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
            var computed = Convert.ToBase64String(bytes);
            var isValid = computed == hash;
            
            if (isValid)
            {
                _logger.LogWarning("Legacy SHA256 password verification succeeded. User should upgrade to bcrypt immediately.");
            }
            else
            {
                _logger.LogInformation("Legacy SHA256 password verification failed");
            }
            
            return isValid;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during legacy SHA256 verification");
            return false;
        }
    }

    /// <summary>
    /// Checks if a stored password hash needs to be upgraded to bcrypt.
    /// </summary>
    /// <param name="hash">The password hash to check.</param>
    /// <returns>True if the hash should be upgraded; otherwise, false.</returns>
    /// <remarks>
    /// Returns true for any non-bcrypt hash (e.g., legacy SHA256).
    /// </remarks>
    public bool NeedsRehash(string hash)
    {
        if (string.IsNullOrWhiteSpace(hash))
        {
            _logger.LogWarning("NeedsRehash called with null or empty hash");
            return true;
        }
        
        var needsRehash = !hash.StartsWith("$2");
        
        if (needsRehash)
        {
            _logger.LogDebug("Hash requires upgrade to bcrypt (current format: legacy)");
        }
        else
        {
            _logger.LogTrace("Hash is using bcrypt, no upgrade needed");
        }
        
        return needsRehash;
    }

    /// <summary>
    /// Validates password strength according to security requirements.
    /// </summary>
    /// <param name="password">The password to validate.</param>
    /// <returns>A tuple indicating validity and an error message if invalid.</returns>
    /// <remarks>
    /// Requirements:
    /// - Length: 8-128 characters
    /// - Must contain at least one uppercase letter
    /// - Must contain at least one lowercase letter
    /// - Must contain at least one digit
    /// </remarks>
    public (bool IsValid, string? Error) ValidatePasswordStrength(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            _logger.LogDebug("Password validation failed: password is empty");
            return (false, "Password is required");
        }
        
        if (password.Length < MinPasswordLength)
        {
            _logger.LogDebug("Password validation failed: too short ({Length} < {MinLength})", 
                password.Length, MinPasswordLength);
            return (false, $"Password must be at least {MinPasswordLength} characters");
        }
        
        if (password.Length > MaxPasswordLength)
        {
            _logger.LogDebug("Password validation failed: too long ({Length} > {MaxLength})", 
                password.Length, MaxPasswordLength);
            return (false, $"Password must be less than {MaxPasswordLength} characters");
        }
        
        // Check for character complexity requirements
        bool hasUpper = password.Any(char.IsUpper);
        bool hasLower = password.Any(char.IsLower);
        bool hasDigit = password.Any(char.IsDigit);
        
        if (!hasUpper || !hasLower || !hasDigit)
        {
            _logger.LogDebug("Password validation failed: insufficient complexity (Upper:{HasUpper}, Lower:{HasLower}, Digit:{HasDigit})", 
                hasUpper, hasLower, hasDigit);
            return (false, "Password must contain uppercase, lowercase, and a number");
        }
        
        _logger.LogDebug("Password validation succeeded");
        return (true, null);
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Gets OAuth 2.0 scopes based on user role.
    /// </summary>
    /// <param name="role">The user's role.</param>
    /// <returns>A space-separated string of scopes.</returns>
    /// <remarks>
    /// Scope hierarchy:
    /// - Admin: Full access including system administration
    /// - Uploader: Content creation and social features
    /// - User: Social features and content reading
    /// - Guest: Read-only access
    /// </remarks>
    private static string GetScopesForRole(string role)
    {
        const string baseScopes = "openid profile email mvn:read";
        
        return role.ToLowerInvariant() switch
        {
            "admin" => $"{baseScopes} mvn:social:write mvn:ingest mvn:admin",
            "uploader" => $"{baseScopes} mvn:social:write mvn:ingest",
            "user" => $"{baseScopes} mvn:social:write",
            _ => baseScopes // Guest/Basic - read-only
        };
    }

    #endregion

    #region Disposal

    /// <summary>
    /// Releases all resources used by the AuthService.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _logger.LogDebug("Disposing AuthService and releasing RSA cryptographic resources");
        _rsaKey?.Dispose();
        _logger.LogTrace("AuthService disposed successfully");
        
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Throws an ObjectDisposedException if this instance has been disposed.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Thrown when the object has been disposed.</exception>
    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            _logger.LogError("Attempted to use disposed AuthService instance");
            throw new ObjectDisposedException(nameof(AuthService));
        }
    }

    #endregion
}
