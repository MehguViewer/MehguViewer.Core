using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using MehguViewer.Shared.Models;

namespace MehguViewer.Core.UI.Services;

/// <summary>
/// Custom authentication state provider for Blazor WebAssembly.
/// Manages JWT token storage and provides authentication state to the app.
/// </summary>
public class JwtAuthStateProvider : AuthenticationStateProvider
{
    private readonly HttpClient _http;
    private readonly IJSRuntime _jsRuntime;
    private const string TokenKey = "authToken";

    public JwtAuthStateProvider(HttpClient http, IJSRuntime jsRuntime)
    {
        _http = http;
        _jsRuntime = jsRuntime;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        // First, check if setup is complete
        try
        {
            var status = await _http.GetFromJsonAsync<SetupStatusResponse>("api/v1/system/setup-status");
            if (status?.is_setup_complete != true)
            {
                // Setup not complete - clear any stale tokens and return anonymous
                await RemoveTokenAsync();
                _http.DefaultRequestHeaders.Authorization = null;
                return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
            }
        }
        catch
        {
            // If we can't check setup status, assume not complete
            await RemoveTokenAsync();
            _http.DefaultRequestHeaders.Authorization = null;
            return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
        }
        
        var token = await GetTokenAsync();
        
        if (string.IsNullOrEmpty(token))
        {
            return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
        }

        // Parse JWT token
        var claims = ParseClaimsFromJwt(token);
        
        // Check if token is expired
        var expClaim = claims.FirstOrDefault(c => c.Type == "exp");
        if (expClaim != null && long.TryParse(expClaim.Value, out var exp))
        {
            var expDate = DateTimeOffset.FromUnixTimeSeconds(exp);
            if (expDate < DateTimeOffset.UtcNow)
            {
                // Token expired, clear it
                await RemoveTokenAsync();
                return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
            }
        }

        // Set the auth header for HTTP client
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        
        var identity = new ClaimsIdentity(claims, "jwt");
        var user = new ClaimsPrincipal(identity);
        
        return new AuthenticationState(user);
    }
    
    private record SetupStatusResponse(bool is_setup_complete);

    /// <summary>
    /// Perform login and mark user as authenticated.
    /// </summary>
    public async Task<bool> LoginAsync(string username, string password)
    {
        try
        {
            // Send plaintext password - server validates and verifies with BCrypt
            var request = new LoginRequest(username, password);
            var response = await _http.PostAsJsonAsync("api/v1/auth/login", request);
            
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
                if (result?.token != null)
                {
                    await MarkUserAsAuthenticatedAsync(result.token);
                    return true;
                }
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Mark user as authenticated after successful login.
    /// </summary>
    public async Task MarkUserAsAuthenticatedAsync(string token)
    {
        await SetTokenAsync(token);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        
        var claims = ParseClaimsFromJwt(token);
        var identity = new ClaimsIdentity(claims, "jwt");
        var user = new ClaimsPrincipal(identity);
        
        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(user)));
    }

    /// <summary>
    /// Mark user as logged out.
    /// </summary>
    public async Task MarkUserAsLoggedOutAsync()
    {
        await RemoveTokenAsync();
        _http.DefaultRequestHeaders.Authorization = null;
        
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());
        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(anonymous)));
    }

    /// <summary>
    /// Get the current JWT token.
    /// </summary>
    public async Task<string?> GetTokenAsync()
    {
        try
        {
            return await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", TokenKey);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Store JWT token in local storage.
    /// </summary>
    private async Task SetTokenAsync(string token)
    {
        await _jsRuntime.InvokeVoidAsync("localStorage.setItem", TokenKey, token);
    }

    /// <summary>
    /// Remove JWT token from local storage.
    /// </summary>
    private async Task RemoveTokenAsync()
    {
        await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", TokenKey);
    }

    /// <summary>
    /// Parse claims from JWT token without external libraries.
    /// </summary>
    private static IEnumerable<Claim> ParseClaimsFromJwt(string jwt)
    {
        var claims = new List<Claim>();
        
        try
        {
            var payload = jwt.Split('.')[1];
            var jsonBytes = ParseBase64WithoutPadding(payload);
            var keyValuePairs = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonBytes);
            
            if (keyValuePairs == null) return claims;

            foreach (var kvp in keyValuePairs)
            {
                var claimType = kvp.Key switch
                {
                    "sub" => ClaimTypes.NameIdentifier,
                    "name" => ClaimTypes.Name,
                    "role" => ClaimTypes.Role,
                    "email" => ClaimTypes.Email,
                    _ => kvp.Key
                };

                if (kvp.Value is JsonElement element)
                {
                    if (element.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in element.EnumerateArray())
                        {
                            claims.Add(new Claim(claimType, item.GetString() ?? ""));
                        }
                    }
                    else
                    {
                        claims.Add(new Claim(claimType, element.GetRawText().Trim('"')));
                    }
                }
                else
                {
                    claims.Add(new Claim(claimType, kvp.Value?.ToString() ?? ""));
                }
            }

            // Extract roles from scope if present
            var scopeClaim = claims.FirstOrDefault(c => c.Type == "scope");
            if (scopeClaim != null)
            {
                var scopes = scopeClaim.Value.Split(' ');
                if (scopes.Contains("mvn:admin"))
                {
                    claims.Add(new Claim(ClaimTypes.Role, "Admin"));
                }
                else if (scopes.Contains("mvn:ingest"))
                {
                    claims.Add(new Claim(ClaimTypes.Role, "Uploader"));
                }
                else
                {
                    claims.Add(new Claim(ClaimTypes.Role, "User"));
                }
            }
        }
        catch
        {
            // Return empty claims on parse error
        }

        return claims;
    }

    private static byte[] ParseBase64WithoutPadding(string base64)
    {
        switch (base64.Length % 4)
        {
            case 2: base64 += "=="; break;
            case 3: base64 += "="; break;
        }
        return Convert.FromBase64String(base64.Replace('-', '+').Replace('_', '/'));
    }
}

/// <summary>
/// Extension methods for authentication.
/// </summary>
public static class AuthExtensions
{
    /// <summary>
    /// Check if the current user is an admin.
    /// </summary>
    public static bool IsAdmin(this ClaimsPrincipal user)
    {
        return user.IsInRole("Admin") || 
               user.Claims.Any(c => c.Type == "scope" && c.Value.Contains("mvn:admin"));
    }

    /// <summary>
    /// Check if the current user can upload content.
    /// </summary>
    public static bool CanUpload(this ClaimsPrincipal user)
    {
        return user.IsAdmin() || 
               user.IsInRole("Uploader") ||
               user.Claims.Any(c => c.Type == "scope" && c.Value.Contains("mvn:ingest"));
    }

    /// <summary>
    /// Get the user's ID from claims.
    /// </summary>
    public static string? GetUserId(this ClaimsPrincipal user)
    {
        return user.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
               user.FindFirst("sub")?.Value;
    }

    /// <summary>
    /// Get the user's display name from claims.
    /// </summary>
    public static string GetDisplayName(this ClaimsPrincipal user)
    {
        return user.FindFirst(ClaimTypes.Name)?.Value ??
               user.FindFirst("name")?.Value ??
               "User";
    }
}
