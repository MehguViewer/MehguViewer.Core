using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using MehguViewer.Shared.Models;
using Microsoft.IdentityModel.Tokens;

namespace MehguViewer.Core.Backend.Services;

public class AuthService
{
    // In a real scenario, this would be loaded from a secure vault or config
    // For this standalone node, we use a fixed key to ensure tokens persist across restarts
    private const string SecretKey = "MehguViewer_Core_Node_Secret_Key_For_Development_Only_Do_Not_Use_In_Production_!@#";
    private const string Issuer = "https://auth.mehgu.example.com";
    private const string Audience = "mehgu-core";

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
                new Claim(ClaimTypes.Role, user.role) // Optional, but helpful for legacy
            }),
            Expires = DateTime.UtcNow.AddDays(7),
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
            ClockSkew = TimeSpan.Zero
        };
    }

    public static string HashPassword(string password)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
        return Convert.ToBase64String(bytes);
    }

    public static bool VerifyPassword(string password, string hash)
    {
        return HashPassword(password) == hash;
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
