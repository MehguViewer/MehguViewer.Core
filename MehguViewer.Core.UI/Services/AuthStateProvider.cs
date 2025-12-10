using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MehguViewer.Core.Shared;

namespace MehguViewer.Core.UI.Services;

/// <summary>
/// JWT-based authentication state provider for Blazor WebAssembly.
/// Implements stateless authentication using JWT tokens stored in browser localStorage.
/// Conforms to MehguViewer.Proto authentication specifications.
/// </summary>
/// <remarks>
/// Security Features:
/// - Token expiration validation
/// - Setup status verification
/// - Automatic token cleanup on expiration
/// - Secure claim parsing with validation
/// </remarks>
public class JwtAuthStateProvider : AuthenticationStateProvider
{
    #region Fields and Constants
    
    private readonly HttpClient _http;
    private readonly IJSRuntime _jsRuntime;
    private readonly ILogger<JwtAuthStateProvider> _logger;
    
    /// <summary>LocalStorage key for JWT token persistence.</summary>
    private const string TokenKey = "authToken";
    
    /// <summary>Clock skew tolerance for token expiration (5 minutes).</summary>
    private static readonly TimeSpan ClockSkewTolerance = TimeSpan.FromMinutes(5);
    
    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="JwtAuthStateProvider"/> class.
    /// </summary>
    /// <param name="http">HTTP client for API communication.</param>
    /// <param name="jsRuntime">JavaScript runtime for localStorage access.</param>
    /// <param name="logger">Logger for diagnostic information.</param>
    /// <exception cref="ArgumentNullException">Thrown when any parameter is null.</exception>
    public JwtAuthStateProvider(
        HttpClient http, 
        IJSRuntime jsRuntime,
        ILogger<JwtAuthStateProvider> logger)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _jsRuntime = jsRuntime ?? throw new ArgumentNullException(nameof(jsRuntime));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        _logger.LogDebug("JwtAuthStateProvider initialized");
    }
    
    #endregion

    #region Authentication State Management

    /// <summary>
    /// Retrieves the current authentication state by validating the stored JWT token.
    /// </summary>
    /// <returns>Authentication state containing user claims if valid, otherwise anonymous.</returns>
    /// <remarks>
    /// Performs the following validations:
    /// 1. System setup completion check
    /// 2. Token existence verification
    /// 3. Token expiration validation
    /// 4. Claim parsing and validation
    /// </remarks>
    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        _logger.LogTrace("Getting authentication state");
        
        // Step 1: Verify system setup is complete
        if (!await IsSetupCompleteAsync())
        {
            _logger.LogInformation("Setup not complete, returning anonymous state");
            await ClearAuthenticationAsync();
            return CreateAnonymousState();
        }
        
        // Step 2: Retrieve stored token
        var token = await GetTokenAsync();
        if (string.IsNullOrWhiteSpace(token))
        {
            _logger.LogDebug("No token found, returning anonymous state");
            return CreateAnonymousState();
        }

        // Step 3: Parse and validate token claims
        var claims = ParseClaimsFromJwt(token);
        if (!claims.Any())
        {
            _logger.LogWarning("Token parsing failed or returned no claims, clearing token");
            await ClearAuthenticationAsync();
            return CreateAnonymousState();
        }
        
        // Step 4: Validate token expiration
        if (IsTokenExpired(claims))
        {
            _logger.LogInformation("Token expired, clearing authentication");
            await ClearAuthenticationAsync();
            return CreateAnonymousState();
        }

        // Step 5: Set authorization header and return authenticated state
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        
        var userId = claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "unknown";
        _logger.LogDebug("User {UserId} authenticated successfully", userId);
        
        return CreateAuthenticatedState(claims);
    }
    
    #endregion

    #region Public Authentication Methods

    /// <summary>
    /// Performs user login with credentials and establishes authenticated session.
    /// </summary>
    /// <param name="username">User's username (required, non-empty).</param>
    /// <param name="password">User's plaintext password (required, non-empty).</param>
    /// <returns>True if login successful, false otherwise.</returns>
    /// <remarks>
    /// Password is sent as plaintext over HTTPS - server handles BCrypt validation.
    /// Token is stored in localStorage and HTTP authorization header is configured.
    /// </remarks>
    public async Task<bool> LoginAsync(string username, string password)
    {
        // Input validation
        if (string.IsNullOrWhiteSpace(username))
        {
            _logger.LogWarning("Login attempt with empty username");
            return false;
        }
        
        if (string.IsNullOrWhiteSpace(password))
        {
            _logger.LogWarning("Login attempt with empty password for user: {Username}", username);
            return false;
        }
        
        _logger.LogInformation("Attempting login for user: {Username}", username);
        
        try
        {
            // Send login request with plaintext password (HTTPS transport encryption)
            var request = new LoginRequest(username, password);
            var response = await _http.PostAsJsonAsync("api/v1/auth/login", request);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Login failed for user {Username} with status code: {StatusCode}", 
                    username, 
                    response.StatusCode);
                return false;
            }
            
            var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
            if (result?.token == null)
            {
                _logger.LogError("Login response missing token for user: {Username}", username);
                return false;
            }
            
            await MarkUserAsAuthenticatedAsync(result.token);
            _logger.LogInformation("User {Username} logged in successfully", username);
            return true;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error during login for user: {Username}", username);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during login for user: {Username}", username);
            return false;
        }
    }

    /// <summary>
    /// Marks the user as authenticated after successful login or token refresh.
    /// </summary>
    /// <param name="token">Valid JWT token (required, non-empty).</param>
    /// <exception cref="ArgumentException">Thrown when token is null or whitespace.</exception>
    public async Task MarkUserAsAuthenticatedAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            _logger.LogError("Attempted to mark user as authenticated with empty token");
            throw new ArgumentException("Token cannot be null or empty", nameof(token));
        }
        
        _logger.LogDebug("Marking user as authenticated");
        
        await SetTokenAsync(token);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        
        var claims = ParseClaimsFromJwt(token);
        var userId = claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "unknown";
        
        _logger.LogInformation("User {UserId} marked as authenticated", userId);
        
        NotifyAuthenticationStateChanged(Task.FromResult(CreateAuthenticatedState(claims)));
    }

    /// <summary>
    /// Logs out the current user and clears all authentication data.
    /// </summary>
    public async Task MarkUserAsLoggedOutAsync()
    {
        _logger.LogInformation("Logging out user");
        
        await ClearAuthenticationAsync();
        
        _logger.LogDebug("User logged out successfully");
        
        NotifyAuthenticationStateChanged(Task.FromResult(CreateAnonymousState()));
    }

    /// <summary>
    /// Retrieves the current JWT token from localStorage.
    /// </summary>
    /// <returns>JWT token if exists, otherwise null.</returns>
    public async Task<string?> GetTokenAsync()
    {
        try
        {
            var token = await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", TokenKey);
            _logger.LogTrace("Token retrieval {Result}", token != null ? "successful" : "returned null");
            return token;
        }
        catch (JSException ex)
        {
            _logger.LogError(ex, "JavaScript error retrieving token from localStorage");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error retrieving token");
            return null;
        }
    }
    
    #endregion

    #region Private Helper Methods

    /// <summary>
    /// Verifies that the MehguViewer system setup is complete.
    /// </summary>
    /// <returns>True if setup complete, false otherwise.</returns>
    private async Task<bool> IsSetupCompleteAsync()
    {
        try
        {
            var status = await _http.GetFromJsonAsync<SetupStatusResponse>("api/v1/system/setup-status");
            var isComplete = status?.is_setup_complete ?? false;
            
            _logger.LogTrace("Setup status check: {IsComplete}", isComplete);
            return isComplete;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to check setup status, assuming incomplete");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error checking setup status");
            return false;
        }
    }

    /// <summary>
    /// Validates whether the token has expired based on 'exp' claim.
    /// </summary>
    /// <param name="claims">Token claims to validate.</param>
    /// <returns>True if token is expired, false otherwise.</returns>
    private bool IsTokenExpired(IEnumerable<Claim> claims)
    {
        var expClaim = claims.FirstOrDefault(c => c.Type == "exp");
        if (expClaim == null)
        {
            _logger.LogWarning("Token missing 'exp' claim, treating as expired");
            return true;
        }
        
        if (!long.TryParse(expClaim.Value, out var exp))
        {
            _logger.LogWarning("Invalid 'exp' claim value: {ExpValue}, treating as expired", expClaim.Value);
            return true;
        }
        
        var expirationDate = DateTimeOffset.FromUnixTimeSeconds(exp);
        var isExpired = expirationDate.Add(ClockSkewTolerance) < DateTimeOffset.UtcNow;
        
        if (isExpired)
        {
            _logger.LogInformation("Token expired at {ExpirationDate}", expirationDate);
        }
        
        return isExpired;
    }

    /// <summary>
    /// Creates an anonymous (unauthenticated) authentication state.
    /// </summary>
    /// <returns>Anonymous authentication state.</returns>
    private static AuthenticationState CreateAnonymousState()
    {
        return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
    }

    /// <summary>
    /// Creates an authenticated state from the provided claims.
    /// </summary>
    /// <param name="claims">User claims from JWT token.</param>
    /// <returns>Authenticated state with user principal.</returns>
    private static AuthenticationState CreateAuthenticatedState(IEnumerable<Claim> claims)
    {
        var identity = new ClaimsIdentity(claims, "jwt");
        var user = new ClaimsPrincipal(identity);
        return new AuthenticationState(user);
    }

    /// <summary>
    /// Clears all authentication data (token and HTTP header).
    /// </summary>
    private async Task ClearAuthenticationAsync()
    {
        _logger.LogTrace("Clearing authentication data");
        await RemoveTokenAsync();
        _http.DefaultRequestHeaders.Authorization = null;
    }

    /// <summary>
    /// Stores JWT token in browser localStorage.
    /// </summary>
    /// <param name="token">JWT token to store.</param>
    private async Task SetTokenAsync(string token)
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync("localStorage.setItem", TokenKey, token);
            _logger.LogTrace("Token stored successfully");
        }
        catch (JSException ex)
        {
            _logger.LogError(ex, "JavaScript error storing token");
            throw;
        }
    }

    /// <summary>
    /// Removes JWT token from browser localStorage.
    /// </summary>
    private async Task RemoveTokenAsync()
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", TokenKey);
            _logger.LogTrace("Token removed successfully");
        }
        catch (JSException ex)
        {
            _logger.LogWarning(ex, "JavaScript error removing token (may not exist)");
        }
    }
    
    #endregion

    #region JWT Token Parsing

    /// <summary>
    /// Parses claims from a JWT token without external dependencies.
    /// </summary>
    /// <param name="jwt">JWT token string (header.payload.signature).</param>
    /// <returns>Collection of claims parsed from token payload.</returns>
    /// <remarks>
    /// Performs base64url decoding and JSON deserialization.
    /// Maps standard JWT claims to CLR claim types.
    /// Extracts role information from 'scope' claim.
    /// Returns empty collection on parse errors for security.
    /// </remarks>
    private IEnumerable<Claim> ParseClaimsFromJwt(string jwt)
    {
        var claims = new List<Claim>();
        
        if (string.IsNullOrWhiteSpace(jwt))
        {
            _logger.LogWarning("Attempted to parse null or empty JWT token");
            return claims;
        }
        
        try
        {
            // JWT format: header.payload.signature
            var parts = jwt.Split('.');
            if (parts.Length != 3)
            {
                _logger.LogWarning("Invalid JWT format: expected 3 parts, got {PartCount}", parts.Length);
                return claims;
            }
            
            var payload = parts[1];
            var jsonBytes = ParseBase64WithoutPadding(payload);
            var keyValuePairs = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonBytes);
            
            if (keyValuePairs == null)
            {
                _logger.LogWarning("JWT payload deserialization returned null");
                return claims;
            }

            // Parse each claim from the payload
            foreach (var kvp in keyValuePairs)
            {
                var claimType = MapJwtClaimToClrClaim(kvp.Key);
                
                if (kvp.Value is JsonElement element)
                {
                    ParseJsonElementClaim(claims, claimType, element);
                }
                else
                {
                    claims.Add(new Claim(claimType, kvp.Value?.ToString() ?? string.Empty));
                }
            }

            // Extract role from scope claim (MehguViewer convention)
            ExtractRoleFromScope(claims);
            
            _logger.LogDebug("Successfully parsed {ClaimCount} claims from JWT", claims.Count);
        }
        catch (FormatException ex)
        {
            _logger.LogError(ex, "Base64 decoding error while parsing JWT");
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON deserialization error while parsing JWT");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error parsing JWT token");
        }

        return claims;
    }

    /// <summary>
    /// Maps JWT claim names to CLR ClaimTypes constants.
    /// </summary>
    /// <param name="jwtClaimName">JWT claim name (e.g., "sub", "name").</param>
    /// <returns>Corresponding CLR claim type.</returns>
    private static string MapJwtClaimToClrClaim(string jwtClaimName)
    {
        return jwtClaimName switch
        {
            "sub" => ClaimTypes.NameIdentifier,
            "name" => ClaimTypes.Name,
            "role" => ClaimTypes.Role,
            "email" => ClaimTypes.Email,
            _ => jwtClaimName
        };
    }

    /// <summary>
    /// Parses a JSON element and adds corresponding claim(s).
    /// Handles both single values and arrays.
    /// </summary>
    /// <param name="claims">Claim collection to add to.</param>
    /// <param name="claimType">Type of the claim.</param>
    /// <param name="element">JSON element to parse.</param>
    private void ParseJsonElementClaim(List<Claim> claims, string claimType, JsonElement element)
    {
        try
        {
            if (element.ValueKind == JsonValueKind.Array)
            {
                // Handle array claims (e.g., multiple roles)
                foreach (var item in element.EnumerateArray())
                {
                    var value = item.GetString();
                    if (!string.IsNullOrEmpty(value))
                    {
                        claims.Add(new Claim(claimType, value));
                    }
                }
            }
            else if (element.ValueKind != JsonValueKind.Null)
            {
                // Handle single-value claims
                var rawText = element.GetRawText().Trim('"');
                if (!string.IsNullOrEmpty(rawText))
                {
                    claims.Add(new Claim(claimType, rawText));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error parsing claim {ClaimType} from JSON element", claimType);
        }
    }

    /// <summary>
    /// Extracts role information from the 'scope' claim.
    /// Follows MehguViewer scoping convention: mvn:admin, mvn:ingest, etc.
    /// </summary>
    /// <param name="claims">Claim collection to add role claim to.</param>
    private void ExtractRoleFromScope(List<Claim> claims)
    {
        var scopeClaim = claims.FirstOrDefault(c => c.Type == "scope");
        if (scopeClaim == null)
        {
            _logger.LogDebug("No 'scope' claim found, defaulting to User role");
            claims.Add(new Claim(ClaimTypes.Role, "User"));
            return;
        }

        var scopes = scopeClaim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        
        // Priority: Admin > Uploader > User
        if (scopes.Contains("mvn:admin"))
        {
            claims.Add(new Claim(ClaimTypes.Role, "Admin"));
            _logger.LogTrace("Extracted Admin role from scope");
        }
        else if (scopes.Contains("mvn:ingest"))
        {
            claims.Add(new Claim(ClaimTypes.Role, "Uploader"));
            _logger.LogTrace("Extracted Uploader role from scope");
        }
        else
        {
            claims.Add(new Claim(ClaimTypes.Role, "User"));
            _logger.LogTrace("No privileged scope found, assigned User role");
        }
    }

    /// <summary>
    /// Decodes base64url-encoded string (JWT payload format).
    /// Handles missing padding and URL-safe characters.
    /// </summary>
    /// <param name="base64">Base64url-encoded string.</param>
    /// <returns>Decoded byte array.</returns>
    /// <exception cref="FormatException">Thrown when base64 decoding fails.</exception>
    private static byte[] ParseBase64WithoutPadding(string base64)
    {
        // Add padding if necessary
        var paddingLength = base64.Length % 4;
        if (paddingLength > 0)
        {
            base64 += new string('=', 4 - paddingLength);
        }
        
        // Convert URL-safe base64 to standard base64
        base64 = base64.Replace('-', '+').Replace('_', '/');
        
        return Convert.FromBase64String(base64);
    }
    
    #endregion

    #region Internal Models
    
    /// <summary>
    /// Response model for system setup status endpoint.
    /// </summary>
    private record SetupStatusResponse(bool is_setup_complete);
    
    #endregion
}


/// <summary>
/// Extension methods for <see cref="ClaimsPrincipal"/> providing MehguViewer-specific authorization helpers.
/// Implements role and permission checking based on JWT scope claims.
/// </summary>
public static class AuthExtensions
{
    /// <summary>
    /// Determines if the current user has administrative privileges.
    /// </summary>
    /// <param name="user">The claims principal to check.</param>
    /// <returns>True if user is an admin, false otherwise.</returns>
    /// <remarks>
    /// Checks for either "Admin" role claim or "mvn:admin" scope.
    /// </remarks>
    public static bool IsAdmin(this ClaimsPrincipal user)
    {
        if (user == null || user.Identity?.IsAuthenticated != true)
        {
            return false;
        }
        
        return user.IsInRole("Admin") || 
               user.Claims.Any(c => c.Type == "scope" && c.Value.Contains("mvn:admin"));
    }

    /// <summary>
    /// Determines if the current user has content upload privileges.
    /// </summary>
    /// <param name="user">The claims principal to check.</param>
    /// <returns>True if user can upload content, false otherwise.</returns>
    /// <remarks>
    /// Admins automatically have upload privileges.
    /// Also checks for "Uploader" role or "mvn:ingest" scope.
    /// </remarks>
    public static bool CanUpload(this ClaimsPrincipal user)
    {
        if (user == null || user.Identity?.IsAuthenticated != true)
        {
            return false;
        }
        
        return user.IsAdmin() || 
               user.IsInRole("Uploader") ||
               user.Claims.Any(c => c.Type == "scope" && c.Value.Contains("mvn:ingest"));
    }

    /// <summary>
    /// Retrieves the user's unique identifier from claims.
    /// </summary>
    /// <param name="user">The claims principal to extract ID from.</param>
    /// <returns>User ID (URN format) if found, otherwise null.</returns>
    /// <remarks>
    /// Searches for standard NameIdentifier or "sub" claim.
    /// Returns URN in format: urn:mvn:user:{uuid}
    /// </remarks>
    public static string? GetUserId(this ClaimsPrincipal user)
    {
        if (user == null)
        {
            return null;
        }
        
        return user.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
               user.FindFirst("sub")?.Value;
    }

    /// <summary>
    /// Retrieves the user's display name from claims.
    /// </summary>
    /// <param name="user">The claims principal to extract name from.</param>
    /// <returns>Display name if found, otherwise "User" as default.</returns>
    /// <remarks>
    /// Searches for standard Name or "name" claim.
    /// Never returns null - provides safe default value.
    /// </remarks>
    public static string GetDisplayName(this ClaimsPrincipal user)
    {
        if (user == null || user.Identity?.IsAuthenticated != true)
        {
            return "User";
        }
        
        return user.FindFirst(ClaimTypes.Name)?.Value ??
               user.FindFirst("name")?.Value ??
               "User";
    }

    /// <summary>
    /// Retrieves all scopes granted to the user.
    /// </summary>
    /// <param name="user">The claims principal to extract scopes from.</param>
    /// <returns>Array of scope strings (e.g., ["mvn:admin", "mvn:ingest"]).</returns>
    public static string[] GetScopes(this ClaimsPrincipal user)
    {
        if (user == null || user.Identity?.IsAuthenticated != true)
        {
            return Array.Empty<string>();
        }
        
        var scopeClaim = user.FindFirst("scope")?.Value;
        if (string.IsNullOrWhiteSpace(scopeClaim))
        {
            return Array.Empty<string>();
        }
        
        return scopeClaim.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// Checks if the user has a specific scope.
    /// </summary>
    /// <param name="user">The claims principal to check.</param>
    /// <param name="scope">Scope to check for (e.g., "mvn:admin").</param>
    /// <returns>True if user has the scope, false otherwise.</returns>
    public static bool HasScope(this ClaimsPrincipal user, string scope)
    {
        if (user == null || string.IsNullOrWhiteSpace(scope))
        {
            return false;
        }
        
        return user.GetScopes().Contains(scope, StringComparer.OrdinalIgnoreCase);
    }
}
