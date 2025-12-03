using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using MehguViewer.Shared.Models;
using Microsoft.IdentityModel.Tokens;

namespace MehguViewer.Core.Backend.Services;

public class AuthService
{
    // JWT secret loaded from environment variable, or generated and persisted if not set
    private static readonly string SecretKey = GetOrCreateSecretKey();
    private const string Issuer = "https://auth.mehgu.example.com";
    private const string Audience = "mehgu-core";
    private const int TokenExpiryHours = 24; // Reduced from 7 days to 24 hours
    private const int BcryptWorkFactor = 12; // Cost factor for bcrypt

    private static string GetOrCreateSecretKey()
    {
        // First, try environment variable
        var envKey = Environment.GetEnvironmentVariable("MEHGU_JWT_SECRET");
        if (!string.IsNullOrEmpty(envKey) && envKey.Length >= 32)
        {
            return envKey;
        }

        // Try to load from file (for persistence across restarts)
        var keyFilePath = Path.Combine("keys", "jwt-secret.key");
        if (File.Exists(keyFilePath))
        {
            var fileKey = File.ReadAllText(keyFilePath).Trim();
            if (fileKey.Length >= 32)
            {
                return fileKey;
            }
        }

        // Generate a new secure key
        var newKey = GenerateSecureKey();
        
        // Persist it
        try
        {
            Directory.CreateDirectory("keys");
            File.WriteAllText(keyFilePath, newKey);
        }
        catch
        {
            // If we can't persist, log warning but continue
            Console.WriteLine("[AuthService] Warning: Could not persist JWT secret key. Tokens will be invalidated on restart.");
        }

        return newKey;
    }

    private static string GenerateSecureKey()
    {
        var bytes = new byte[64]; // 512 bits
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes);
    }

    public static string GenerateToken(User user)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.ASCII.GetBytes(SecretKey);

        var scopes = GetScopesForRole(user.role);

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim("sub", user.id),
                new Claim("iss", Issuer),
                new Claim("aud", Audience),
                new Claim("scope", scopes),
                new Claim(ClaimTypes.Name, user.username),
                new Claim(ClaimTypes.Role, user.role)
            }),
            Expires = DateTime.UtcNow.AddHours(TokenExpiryHours),
            Issuer = Issuer,
            Audience = Audience,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    public static TokenValidationParameters GetValidationParameters()
    {
        return new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(SecretKey)),
            ValidateIssuer = true,
            ValidIssuer = Issuer,
            ValidateAudience = true,
            ValidAudience = Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1) // Small clock skew tolerance
        };
    }

    /// <summary>
    /// Hash a password using bcrypt with a secure work factor.
    /// </summary>
    public static string HashPassword(string password)
    {
        return BCrypt.Net.BCrypt.HashPassword(password, BcryptWorkFactor);
    }

    /// <summary>
    /// Verify a password against a bcrypt hash.
    /// Also supports legacy SHA256 hashes for migration (will be rehashed on next login).
    /// </summary>
    public static bool VerifyPassword(string password, string hash)
    {
        // Check if it's a bcrypt hash (starts with $2)
        if (hash.StartsWith("$2"))
        {
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }
        
        // Legacy SHA256 support for migration
        return VerifyLegacySha256(password, hash);
    }

    /// <summary>
    /// Check if a hash needs to be upgraded to bcrypt.
    /// </summary>
    public static bool NeedsRehash(string hash)
    {
        // If it's not a bcrypt hash, it needs rehashing
        return !hash.StartsWith("$2");
    }

    private static bool VerifyLegacySha256(string password, string hash)
    {
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
        var computed = Convert.ToBase64String(bytes);
        return computed == hash;
    }

    /// <summary>
    /// Validate password strength.
    /// </summary>
    public static (bool IsValid, string? Error) ValidatePasswordStrength(string password)
    {
        if (string.IsNullOrEmpty(password))
            return (false, "Password is required");
        
        if (password.Length < 8)
            return (false, "Password must be at least 8 characters");
        
        if (password.Length > 128)
            return (false, "Password must be less than 128 characters");
        
        // Check for at least one uppercase, lowercase, and digit
        bool hasUpper = password.Any(char.IsUpper);
        bool hasLower = password.Any(char.IsLower);
        bool hasDigit = password.Any(char.IsDigit);
        
        if (!hasUpper || !hasLower || !hasDigit)
            return (false, "Password must contain uppercase, lowercase, and a number");
        
        return (true, null);
    }

    private static string GetScopesForRole(string role)
    {
        var baseScopes = "openid profile email mvn:read";
        
        return role.ToLower() switch
        {
            "admin" => $"{baseScopes} mvn:social:write mvn:ingest mvn:admin",
            "uploader" => $"{baseScopes} mvn:social:write mvn:ingest",
            "user" => $"{baseScopes} mvn:social:write",
            _ => baseScopes // Guest/Basic
        };
    }
}
