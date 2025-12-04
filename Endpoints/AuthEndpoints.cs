using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json;
using MehguViewer.Shared.Models;
using MehguViewer.Core.Backend.Services;
using Microsoft.AspNetCore.Mvc;

namespace MehguViewer.Core.Backend.Endpoints;

/// <summary>
/// Authentication endpoints for login, registration, and user management.
/// 
/// ⚠️ TEMPORARY LOCAL AUTH:
/// These endpoints provide local authentication as a temporary solution.
/// When external auth server is integrated:
/// - /login and /register will be deprecated (handled by auth server)
/// - /provision will auto-create users from authenticated requests
/// - Token validation will use JWKS from the auth server
/// </summary>
public static partial class AuthEndpoints
{
    // Login attempt tracking for rate limiting
    private static readonly ConcurrentDictionary<string, LoginAttemptTracker> _loginAttempts = new();
    
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth");
        
        // Public endpoints (TEMPORARY: will be replaced by external auth server)
        group.MapPost("/login", Login);
        group.MapPost("/register", Register);
        group.MapGet("/config", GetAuthConfig);
        group.MapPost("/logout", Logout).RequireAuthorization();
        group.MapGet("/me", GetCurrentUser).RequireAuthorization();
        group.MapPatch("/me/password-login", TogglePasswordLogin).RequireAuthorization();
        
        // Passkey / WebAuthn endpoints
        group.MapPost("/passkey/register/options", GetPasskeyRegistrationOptions).RequireAuthorization();
        group.MapPost("/passkey/register", CompletePasskeyRegistration).RequireAuthorization();
        group.MapPost("/passkey/authenticate/options", GetPasskeyAuthenticationOptions);
        group.MapPost("/passkey/authenticate", AuthenticateWithPasskey);
        group.MapGet("/passkeys", GetUserPasskeys).RequireAuthorization();
        group.MapPatch("/passkeys/{id}", RenamePasskey).RequireAuthorization();
        group.MapDelete("/passkeys/{id}", DeletePasskey).RequireAuthorization();
        
        // User auto-provisioning from external auth server
        // This endpoint creates or returns a user based on the authenticated token claims
        // When called with a valid external auth token, it ensures the user exists locally
        group.MapPost("/provision", ProvisionUser).RequireAuthorization();
        
        // Admin-only auth config (support both PUT and PATCH for compatibility)
        app.MapGet("/api/v1/admin/auth", GetFullAuthConfig).RequireAuthorization("MvnAdmin");
        app.MapPut("/api/v1/admin/auth", UpdateAuthConfig).RequireAuthorization("MvnAdmin");
        app.MapPatch("/api/v1/admin/auth", UpdateAuthConfig).RequireAuthorization("MvnAdmin");
    }

    private static async Task<IResult> Login(
        [FromBody] LoginRequestWithCf request, 
        IRepository repo, 
        IConfiguration config,
        HttpContext context,
        IHttpClientFactory httpClientFactory)
    {
        await Task.CompletedTask;
        
        // Validate input first (before any other checks)
        if (string.IsNullOrWhiteSpace(request.username) || string.IsNullOrWhiteSpace(request.password))
        {
            return Results.BadRequest(new Problem(
                "urn:mvn:error:validation", 
                "Username and password are required", 
                400, 
                null, 
                "/api/v1/auth/login"
            ));
        }
        
        // Get client IP for rate limiting
        var clientIp = GetClientIp(context);
        
        // Check if account is locked
        if (IsAccountLocked(request.username, clientIp, config))
        {
            var lockoutMinutes = config.GetValue("Auth:LockoutDurationMinutes", 15);
            return Results.Problem(
                title: "Account temporarily locked",
                statusCode: 429,
                type: "urn:mvn:error:account-locked",
                detail: $"Too many failed login attempts. Please try again in {lockoutMinutes} minutes.",
                instance: "/api/v1/auth/login"
            );
        }
        
        // Validate Cloudflare Turnstile if enabled
        var cfEnabled = config.GetValue("Auth:Cloudflare:Enabled", false);
        if (cfEnabled)
        {
            var cfResult = await ValidateTurnstileToken(request.cf_turnstile_token, clientIp, config, httpClientFactory);
            if (!cfResult.success)
            {
                return Results.BadRequest(new Problem(
                    "urn:mvn:error:cf-challenge-failed",
                    "Cloudflare challenge failed",
                    400,
                    cfResult.error,
                    "/api/v1/auth/login"
                ));
            }
        }
        
        // Validate credentials
        var user = repo.ValidateUser(request.username, request.password);
        if (user != null)
        {
            // Check if user has disabled password login
            if (user.password_login_disabled)
            {
                return Results.Json(new Problem(
                    "urn:mvn:error:password-login-disabled",
                    "Password Login Disabled",
                    403,
                    "Password login is disabled for this account. Please use passkey authentication.",
                    "/api/v1/auth/login"
                ), AppJsonSerializerContext.Default.Problem, statusCode: 403);
            }
            
            // Check maintenance mode - only Admin can login during maintenance
            var systemConfig = repo.GetSystemConfig();
            if (systemConfig.maintenance_mode && user.role != "Admin")
            {
                var problem = new Problem(
                    "urn:mvn:error:maintenance-mode",
                    "The system is currently in maintenance mode. Only administrators can log in.",
                    503,
                    "Maintenance Mode",
                    "/api/v1/auth/login"
                );
                return Results.Json(problem, AppJsonSerializerContext.Default.Problem, statusCode: 503);
            }
            
            // Clear login attempts on success
            ClearLoginAttempts(request.username, clientIp);
            
            // Check if password needs rehashing (legacy SHA256 -> bcrypt migration)
            if (AuthService.NeedsRehash(user.password_hash))
            {
                var newHash = AuthService.HashPassword(request.password);
                var updatedUser = user with { password_hash = newHash };
                repo.UpdateUser(updatedUser);
            }
            
            var token = AuthService.GenerateToken(user);
            return Results.Ok(new LoginResponse(token, user.username, user.role));
        }
        
        // Record failed attempt
        RecordFailedAttempt(request.username, clientIp);
        
        // Return generic error (don't reveal if user exists)
        return Results.Unauthorized();
    }

    private static async Task<IResult> Register(
        [FromBody] RegisterRequestWithCf request, 
        IRepository repo, 
        IConfiguration config,
        HttpContext context,
        IHttpClientFactory httpClientFactory)
    {
        await Task.CompletedTask;
        
        var clientIp = GetClientIp(context);
        
        // Validate input
        if (string.IsNullOrWhiteSpace(request.username) || string.IsNullOrWhiteSpace(request.password))
        {
            return Results.BadRequest(new Problem(
                "urn:mvn:error:validation", 
                "Username and password are required", 
                400, 
                null, 
                "/api/v1/auth/register"
            ));
        }
        
        // Validate username
        if (request.username.Length < 3 || request.username.Length > 32)
        {
            return Results.BadRequest(new Problem(
                "urn:mvn:error:validation", 
                "Username must be between 3 and 32 characters", 
                400, 
                null, 
                "/api/v1/auth/register"
            ));
        }
        
        // Validate username format (alphanumeric, underscores only)
        if (!System.Text.RegularExpressions.Regex.IsMatch(request.username, "^[a-zA-Z0-9_]+$"))
        {
            return Results.BadRequest(new Problem(
                "urn:mvn:error:validation", 
                "Username can only contain letters, numbers, and underscores", 
                400, 
                null, 
                "/api/v1/auth/register"
            ));
        }
        
        // Validate password strength
        var (isValid, error) = AuthService.ValidatePasswordStrength(request.password);
        if (!isValid)
        {
            return Results.BadRequest(new Problem(
                "urn:mvn:error:weak-password", 
                error!, 
                400, 
                null, 
                "/api/v1/auth/register"
            ));
        }
        
        // Check if user already exists
        if (repo.GetUserByUsername(request.username) != null)
        {
            return Results.Conflict(new Problem(
                "urn:mvn:error:user-exists", 
                "A user with this username already exists", 
                409, 
                null, 
                "/api/v1/auth/register"
            ));
        }
        
        // Determine role
        string role = "User";
        
        // First user becomes Admin
        if (!repo.IsAdminSet())
        {
            role = "Admin";
        }
        else
        {
            // Check if registration is open
            var registrationOpen = config.GetValue("Auth:RegistrationOpen", true);
            if (!registrationOpen)
            {
                return Results.Problem(
                    title: "Registration is currently closed",
                    statusCode: 403,
                    type: "urn:mvn:error:registration-closed",
                    instance: "/api/v1/auth/register"
                );
            }
            
            // Validate Cloudflare Turnstile if enabled
            var cfEnabled = config.GetValue("Auth:Cloudflare:Enabled", false);
            if (cfEnabled)
            {
                var cfResult = await ValidateTurnstileToken(request.cf_turnstile_token, clientIp, config, httpClientFactory);
                if (!cfResult.success)
                {
                    return Results.BadRequest(new Problem(
                        "urn:mvn:error:cf-challenge-failed",
                        "Cloudflare challenge failed",
                        400,
                        cfResult.error,
                        "/api/v1/auth/register"
                    ));
                }
            }
        }
        
        // Create user
        var user = new User(
            UrnHelper.CreateUserUrn(), 
            request.username, 
            AuthService.HashPassword(request.password), 
            role, 
            DateTime.UtcNow
        );
        repo.AddUser(user);
        
        // Auto-login after registration
        var token = AuthService.GenerateToken(user);
        return Results.Ok(new LoginResponse(token, user.username, user.role));
    }

    private static async Task<IResult> Logout(HttpContext context)
    {
        await Task.CompletedTask;
        // JWT tokens are stateless, so we can't really invalidate them server-side
        // Client should delete the token from storage
        // In a production system, you'd want a token blacklist or use short-lived tokens with refresh tokens
        return Results.Ok(new { message = "Logged out successfully" });
    }

    private static async Task<IResult> GetCurrentUser(HttpContext context, IRepository repo)
    {
        await Task.CompletedTask;
        
        // Try both claim types - JWT might map "sub" to NameIdentifier
        var userId = context.User.FindFirst("sub")?.Value 
            ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Results.Unauthorized();
        }
        
        var user = repo.GetUser(userId);
        if (user == null)
        {
            return Results.NotFound(new Problem(
                "urn:mvn:error:not-found",
                "User not found",
                404,
                null,
                "/api/v1/auth/me"
            ));
        }
        
        // Return user info without password hash
        var isFirstAdmin = user.role == "Admin" && IsFirstAdmin(user, repo);
        return Results.Ok(new UserProfileResponse(
            id: user.id,
            username: user.username,
            role: user.role,
            created_at: user.created_at,
            password_login_disabled: user.password_login_disabled,
            is_first_admin: isFirstAdmin
        ));
    }

    /// <summary>
    /// Toggle password login for the current user.
    /// User must have at least one passkey registered to disable password login.
    /// </summary>
    private static async Task<IResult> TogglePasswordLogin(
        [FromBody] TogglePasswordLoginRequest request,
        HttpContext context, 
        IRepository repo)
    {
        await Task.CompletedTask;
        
        var userId = context.User.FindFirst("sub")?.Value 
                     ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        
        if (string.IsNullOrEmpty(userId))
        {
            return Results.Unauthorized();
        }
        
        var user = repo.GetUser(userId);
        if (user == null)
        {
            return Results.NotFound(new Problem(
                "urn:mvn:error:not-found",
                "User not found",
                404,
                null,
                "/api/v1/auth/me/password-login"
            ));
        }
        
        // To disable password login, user must have at least one passkey
        if (request.disable)
        {
            var passkeys = repo.GetPasskeysByUser(userId);
            if (!passkeys.Any())
            {
                return Results.BadRequest(new Problem(
                    "urn:mvn:error:no-passkey",
                    "You must have at least one passkey registered to disable password login.",
                    400,
                    "No passkey registered",
                    "/api/v1/auth/me/password-login"
                ));
            }
        }
        
        var updatedUser = user with { password_login_disabled = request.disable };
        repo.UpdateUser(updatedUser);
        
        return Results.Ok(new TogglePasswordLoginResponse(
            password_login_disabled: request.disable,
            message: request.disable 
                ? "Password login has been disabled. You can only log in with a passkey." 
                : "Password login has been enabled."
        ));
    }

    /// <summary>
    /// Auto-provision a reader (User role) from external authentication.
    /// 
    /// This endpoint is called when a user authenticates via an external auth server.
    /// It creates a local user record if one doesn't exist, or returns the existing user.
    /// 
    /// IMPORTANT: Only "User" (reader) accounts are auto-provisioned from external auth.
    /// Admin and Uploader accounts are managed locally through the admin panel.
    /// This ensures privileged access is controlled by the core server administrators.
    /// 
    /// Usage: Call this endpoint after validating an external auth token to ensure
    /// the user has a local reader account.
    /// </summary>
    private static async Task<IResult> ProvisionUser(HttpContext context, IRepository repo)
    {
        await Task.CompletedTask;
        
        // Get user info from token claims (set by external auth or local JWT)
        // Try both claim types - JWT might map "sub" to NameIdentifier
        var userId = context.User.FindFirst("sub")?.Value 
            ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var username = context.User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value 
                    ?? context.User.FindFirst("preferred_username")?.Value
                    ?? context.User.FindFirst("name")?.Value;
        var scopes = context.User.FindFirst("scope")?.Value ?? "";
        var email = context.User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
                 ?? context.User.FindFirst("email")?.Value;
        
        if (string.IsNullOrEmpty(userId))
        {
            return Results.BadRequest(new Problem(
                "urn:mvn:error:validation",
                "Token missing 'sub' claim",
                400,
                null,
                "/api/v1/auth/provision"
            ));
        }
        
        // Determine username - use provided or generate from email/id
        var effectiveUsername = username;
        if (string.IsNullOrEmpty(effectiveUsername))
        {
            effectiveUsername = !string.IsNullOrEmpty(email) 
                ? email.Split('@')[0] 
                : $"user_{userId[..8]}";
        }
        
        // Check if user already exists (by external ID stored in user.id)
        var existingUser = repo.GetUser(userId);
        if (existingUser != null)
        {
            // User exists - return existing user info
            // Note: Roles are NOT updated from external auth scopes
            // Admin/Uploader roles are managed locally through the admin panel
            return Results.Ok(new ProvisionResponse(
                existingUser.id,
                existingUser.username,
                existingUser.role,
                false, // was_created
                "User already exists"
            ));
        }
        
        // Check if username is taken by a different user
        var userByUsername = repo.GetUserByUsername(effectiveUsername);
        if (userByUsername != null)
        {
            // Append random suffix to make username unique
            effectiveUsername = $"{effectiveUsername}_{Guid.NewGuid().ToString()[..4]}";
        }
        
        // Always create as "User" (reader) role
        // Admin and Uploader accounts are managed locally through the admin panel
        // This ensures privileged access is controlled by core server administrators
        const string role = "User";
        
        // Create new user with external auth ID
        var newUser = new User(
            userId, // Use external auth ID as user ID
            effectiveUsername,
            "", // No password hash for externally authenticated users
            role,
            DateTime.UtcNow
        );
        repo.AddUser(newUser);
        
        return Results.Ok(new ProvisionResponse(
            newUser.id,
            newUser.username,
            newUser.role,
            true, // was_created
            "Reader account provisioned successfully"
        ));
    }

    private static async Task<IResult> GetAuthConfig(IRepository repo)
    {
        await Task.CompletedTask;
        
        var config = repo.GetSystemConfig();
        return Results.Ok(new AuthConfigPublic(
            config.registration_open, 
            config.cloudflare_enabled, 
            config.cloudflare_enabled ? config.cloudflare_site_key : null
        ));
    }

    private static async Task<IResult> GetFullAuthConfig(IRepository repo)
    {
        await Task.CompletedTask;
        
        var config = repo.GetSystemConfig();
        var authConfig = new AuthConfig(
            config.registration_open,
            config.max_login_attempts,
            config.lockout_duration_minutes,
            config.token_expiry_hours,
            new CloudflareConfig(
                config.cloudflare_enabled,
                config.cloudflare_site_key,
                // Don't expose secret key in response
                string.IsNullOrEmpty(config.cloudflare_secret_key) ? "" : "********"
            ),
            config.require_2fa_passkey,
            config.require_password_for_danger_zone
        );
        
        return Results.Ok(authConfig);
    }

    private static async Task<IResult> UpdateAuthConfig([FromBody] AuthConfigUpdate update, IRepository repo)
    {
        await Task.CompletedTask;
        
        var currentConfig = repo.GetSystemConfig();
        
        // Create updated config with new auth values
        var newConfig = currentConfig with
        {
            registration_open = update.registration_open ?? currentConfig.registration_open,
            max_login_attempts = update.max_login_attempts ?? currentConfig.max_login_attempts,
            lockout_duration_minutes = update.lockout_duration_minutes ?? currentConfig.lockout_duration_minutes,
            token_expiry_hours = update.token_expiry_hours ?? currentConfig.token_expiry_hours,
            cloudflare_enabled = update.cloudflare?.enabled ?? currentConfig.cloudflare_enabled,
            cloudflare_site_key = update.cloudflare?.turnstile_site_key ?? currentConfig.cloudflare_site_key,
            // Only update secret key if a new one is provided (not masked)
            cloudflare_secret_key = (!string.IsNullOrEmpty(update.cloudflare?.turnstile_secret_key) && update.cloudflare?.turnstile_secret_key != "********") 
                ? update.cloudflare.turnstile_secret_key 
                : currentConfig.cloudflare_secret_key,
            require_2fa_passkey = update.require_2fa_passkey ?? currentConfig.require_2fa_passkey,
            require_password_for_danger_zone = update.require_password_for_danger_zone ?? currentConfig.require_password_for_danger_zone
        };
        
        repo.UpdateSystemConfig(newConfig);
        
        return Results.NoContent();
    }

    #region Helpers

    private static string GetClientIp(HttpContext context)
    {
        // Check for CF-Connecting-IP header (Cloudflare)
        var cfIp = context.Request.Headers["CF-Connecting-IP"].FirstOrDefault();
        if (!string.IsNullOrEmpty(cfIp)) return cfIp;
        
        // Check for X-Forwarded-For header (reverse proxies)
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            // Take the first IP in the list (original client)
            return forwardedFor.Split(',')[0].Trim();
        }
        
        // Fall back to remote IP
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private static bool IsAccountLocked(string username, string clientIp, IConfiguration config)
    {
        var maxAttempts = config.GetValue("Auth:MaxLoginAttempts", 5);
        var lockoutMinutes = config.GetValue("Auth:LockoutDurationMinutes", 15);
        var lockoutDuration = TimeSpan.FromMinutes(lockoutMinutes);
        
        // Check by username
        var usernameKey = $"user:{username.ToLowerInvariant()}";
        if (_loginAttempts.TryGetValue(usernameKey, out var userTracker))
        {
            if (userTracker.AttemptCount >= maxAttempts && 
                (DateTime.UtcNow - userTracker.LastAttempt) < lockoutDuration)
            {
                return true;
            }
        }
        
        // Check by IP (more lenient - 10x the attempts)
        var ipKey = $"ip:{clientIp}";
        if (_loginAttempts.TryGetValue(ipKey, out var ipTracker))
        {
            if (ipTracker.AttemptCount >= maxAttempts * 10 && 
                (DateTime.UtcNow - ipTracker.LastAttempt) < lockoutDuration)
            {
                return true;
            }
        }
        
        return false;
    }

    private static void RecordFailedAttempt(string username, string clientIp)
    {
        var usernameKey = $"user:{username.ToLowerInvariant()}";
        var ipKey = $"ip:{clientIp}";
        
        _loginAttempts.AddOrUpdate(usernameKey, 
            _ => new LoginAttemptTracker { AttemptCount = 1, LastAttempt = DateTime.UtcNow },
            (_, tracker) => 
            {
                tracker.AttemptCount++;
                tracker.LastAttempt = DateTime.UtcNow;
                return tracker;
            });
        
        _loginAttempts.AddOrUpdate(ipKey,
            _ => new LoginAttemptTracker { AttemptCount = 1, LastAttempt = DateTime.UtcNow },
            (_, tracker) =>
            {
                tracker.AttemptCount++;
                tracker.LastAttempt = DateTime.UtcNow;
                return tracker;
            });
    }

    private static void ClearLoginAttempts(string username, string clientIp)
    {
        var usernameKey = $"user:{username.ToLowerInvariant()}";
        _loginAttempts.TryRemove(usernameKey, out _);
    }

    private static async Task<(bool success, string? error)> ValidateTurnstileToken(
        string? token, 
        string clientIp, 
        IConfiguration config,
        IHttpClientFactory httpClientFactory)
    {
        if (string.IsNullOrEmpty(token))
        {
            return (false, "Cloudflare challenge token is required");
        }
        
        var secretKey = config.GetValue<string>("Auth:Cloudflare:TurnstileSecretKey");
        if (string.IsNullOrEmpty(secretKey))
        {
            // If no secret key configured but CF is enabled, allow (misconfiguration)
            return (true, null);
        }
        
        try
        {
            var client = httpClientFactory.CreateClient();
            var formContent = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("secret", secretKey),
                new KeyValuePair<string, string>("response", token),
                new KeyValuePair<string, string>("remoteip", clientIp)
            });
            
            var response = await client.PostAsync("https://challenges.cloudflare.com/turnstile/v0/siteverify", formContent);
            var responseBody = await response.Content.ReadAsStringAsync();
            
            var result = JsonSerializer.Deserialize(responseBody, TurnstileJsonContext.Default.TurnstileResponse);
            if (result?.success == true)
            {
                return (true, null);
            }
            
            var errorCodes = result?.error_codes != null ? string.Join(", ", result.error_codes) : "Unknown error";
            return (false, $"Turnstile validation failed: {errorCodes}");
        }
        catch (Exception ex)
        {
            // On network error, optionally allow the request (fail-open) or deny (fail-closed)
            // For security, we fail-closed
            return (false, $"Failed to validate Cloudflare challenge: {ex.Message}");
        }
    }

    #endregion

    #region Passkey / WebAuthn Endpoints

    /// <summary>
    /// Get registration options for creating a new passkey.
    /// Requires authentication - user must be logged in to register a passkey.
    /// </summary>
    private static async Task<IResult> GetPasskeyRegistrationOptions(
        [FromBody] PasskeyRegistrationOptionsRequest request,
        HttpContext context,
        IRepository repo,
        PasskeyService passkeyService)
    {
        await Task.CompletedTask;
        
        // Try both claim types - JWT might map "sub" to NameIdentifier
        var userId = context.User.FindFirst("sub")?.Value 
            ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Results.Unauthorized();
        
        var user = repo.GetUser(userId);
        if (user == null)
            return Results.Unauthorized();
        
        // Only allow Admin and Uploader roles to register passkeys
        if (user.role != "Admin" && user.role != "Uploader")
        {
            return Results.Problem(
                title: "Passkeys not available",
                statusCode: 403,
                detail: "Passkeys are only available for Admin and Uploader accounts."
            );
        }
        
        var existingPasskeys = repo.GetPasskeysByUser(userId);
        var options = passkeyService.GenerateRegistrationOptions(user, existingPasskeys);
        
        // Store challenge ID in response header for later verification
        var challengeId = passkeyService.StoreChallenge(options.challenge, userId);
        context.Response.Headers["X-Passkey-Challenge-Id"] = challengeId;
        
        return Results.Ok(options);
    }

    /// <summary>
    /// Complete passkey registration with authenticator response.
    /// </summary>
    private static async Task<IResult> CompletePasskeyRegistration(
        [FromBody] PasskeyRegistrationRequest request,
        [FromHeader(Name = "X-Passkey-Challenge-Id")] string? challengeId,
        HttpContext context,
        IRepository repo,
        PasskeyService passkeyService)
    {
        await Task.CompletedTask;
        
        // Try both claim types - JWT might map "sub" to NameIdentifier
        var userId = context.User.FindFirst("sub")?.Value 
            ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Results.Unauthorized();
        
        if (string.IsNullOrEmpty(challengeId))
        {
            return Results.BadRequest(new Problem(
                "urn:mvn:error:passkey:missing-challenge",
                "Missing challenge ID",
                400,
                "X-Passkey-Challenge-Id header is required",
                "/api/v1/auth/passkey/register"
            ));
        }
        
        // Validate challenge
        var (valid, challenge, storedUserId) = passkeyService.ValidateChallenge(challengeId);
        if (!valid || challenge == null || storedUserId != userId)
        {
            return Results.BadRequest(new Problem(
                "urn:mvn:error:passkey:invalid-challenge",
                "Invalid or expired challenge",
                400,
                null,
                "/api/v1/auth/passkey/register"
            ));
        }
        
        // Get expected origin from request
        var origin = GetOriginFromRequest(context);
        
        // Verify registration
        var (success, credentialId, publicKey, signCount, backedUp, deviceType, error) = 
            passkeyService.VerifyRegistration(request, challenge, userId, origin);
        
        if (!success || credentialId == null || publicKey == null)
        {
            return Results.BadRequest(new Problem(
                "urn:mvn:error:passkey:registration-failed",
                "Passkey registration failed",
                400,
                error,
                "/api/v1/auth/passkey/register"
            ));
        }
        
        // Check if credential already exists
        if (repo.GetPasskeyByCredentialId(credentialId) != null)
        {
            return Results.Conflict(new Problem(
                "urn:mvn:error:passkey:already-registered",
                "This passkey is already registered",
                409,
                null,
                "/api/v1/auth/passkey/register"
            ));
        }
        
        // Create passkey record
        var passkey = new Passkey(
            id: Guid.NewGuid().ToString(),
            user_id: userId,
            credential_id: credentialId,
            public_key: publicKey,
            sign_count: signCount,
            name: request.passkey_name ?? "Passkey",
            device_type: deviceType,
            backed_up: backedUp,
            created_at: DateTime.UtcNow,
            last_used_at: null
        );
        
        repo.AddPasskey(passkey);
        
        return Results.Ok(new PasskeyInfo(
            passkey.id,
            passkey.name,
            passkey.device_type,
            passkey.backed_up,
            passkey.created_at,
            passkey.last_used_at
        ));
    }

    /// <summary>
    /// Get authentication options for passkey login.
    /// </summary>
    private static async Task<IResult> GetPasskeyAuthenticationOptions(
        [FromBody] PasskeyAuthenticationOptionsRequest? request,
        HttpContext context,
        IRepository repo,
        PasskeyService passkeyService)
    {
        await Task.CompletedTask;
        
        IEnumerable<Passkey>? allowedPasskeys = null;
        
        // If username provided, get passkeys for that user
        if (!string.IsNullOrEmpty(request?.username))
        {
            var user = repo.GetUserByUsername(request.username);
            if (user != null)
            {
                allowedPasskeys = repo.GetPasskeysByUser(user.id);
                if (!allowedPasskeys.Any())
                {
                    return Results.NotFound(new Problem(
                        "urn:mvn:error:passkey:no-passkeys",
                        "No passkeys registered",
                        404,
                        "This account has no passkeys registered.",
                        "/api/v1/auth/passkey/authenticate/options"
                    ));
                }
            }
        }
        
        var (options, challengeId) = passkeyService.GenerateAuthenticationOptions(allowedPasskeys);
        
        // Store challenge ID in response header
        context.Response.Headers["X-Passkey-Challenge-Id"] = challengeId;
        
        return Results.Ok(options);
    }

    /// <summary>
    /// Authenticate with a passkey.
    /// </summary>
    private static async Task<IResult> AuthenticateWithPasskey(
        [FromBody] PasskeyAuthenticationRequest request,
        [FromHeader(Name = "X-Passkey-Challenge-Id")] string? challengeId,
        HttpContext context,
        IRepository repo,
        PasskeyService passkeyService,
        IConfiguration config,
        IHttpClientFactory httpClientFactory)
    {
        await Task.CompletedTask;
        
        if (string.IsNullOrEmpty(challengeId))
        {
            return Results.BadRequest(new Problem(
                "urn:mvn:error:passkey:missing-challenge",
                "Missing challenge ID",
                400,
                "X-Passkey-Challenge-Id header is required",
                "/api/v1/auth/passkey/authenticate"
            ));
        }
        
        // Validate challenge
        var (valid, challenge, _) = passkeyService.ValidateChallenge(challengeId);
        if (!valid || challenge == null)
        {
            return Results.BadRequest(new Problem(
                "urn:mvn:error:passkey:invalid-challenge",
                "Invalid or expired challenge",
                400,
                null,
                "/api/v1/auth/passkey/authenticate"
            ));
        }
        
        // Find passkey by credential ID
        var passkey = repo.GetPasskeyByCredentialId(request.raw_id);
        if (passkey == null)
        {
            return Results.Unauthorized();
        }
        
        // Get user
        var user = repo.GetUser(passkey.user_id);
        if (user == null)
        {
            return Results.Unauthorized();
        }
        
        // Check maintenance mode - only Admin can login during maintenance
        var systemConfig = repo.GetSystemConfig();
        if (systemConfig.maintenance_mode && user.role != "Admin")
        {
            var problem = new Problem(
                "urn:mvn:error:maintenance-mode",
                "The system is currently in maintenance mode. Only administrators can log in.",
                503,
                "Maintenance Mode",
                "/api/v1/auth/passkey/authenticate"
            );
            return Results.Json(problem, AppJsonSerializerContext.Default.Problem, statusCode: 503);
        }
        
        // Only allow Admin and Uploader to authenticate with passkey
        if (user.role != "Admin" && user.role != "Uploader")
        {
            return Results.Unauthorized();
        }
        
        // Get expected origin from request
        var origin = GetOriginFromRequest(context);
        
        // Verify authentication
        var (success, userId, newSignCount, error) = 
            passkeyService.VerifyAuthentication(request, passkey, challenge, origin);
        
        if (!success)
        {
            return Results.Unauthorized();
        }
        
        // Update sign count and last used timestamp
        var updatedPasskey = passkey with 
        { 
            sign_count = newSignCount,
            last_used_at = DateTime.UtcNow
        };
        repo.UpdatePasskey(updatedPasskey);
        
        // Generate JWT token
        var token = AuthService.GenerateToken(user);
        
        return Results.Ok(new LoginResponse(token, user.username, user.role));
    }

    /// <summary>
    /// Get all passkeys for the current user.
    /// </summary>
    private static async Task<IResult> GetUserPasskeys(
        HttpContext context,
        IRepository repo)
    {
        await Task.CompletedTask;
        
        // Try both claim types - JWT might map "sub" to NameIdentifier
        var userId = context.User.FindFirst("sub")?.Value 
            ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Results.Unauthorized();
        
        var passkeys = repo.GetPasskeysByUser(userId)
            .Select(p => new PasskeyInfo(
                p.id,
                p.name,
                p.device_type,
                p.backed_up,
                p.created_at,
                p.last_used_at
            ))
            .ToArray();
        
        return Results.Ok(passkeys);
    }

    /// <summary>
    /// Rename a passkey.
    /// </summary>
    private static async Task<IResult> RenamePasskey(
        string id,
        [FromBody] PasskeyRenameRequest request,
        HttpContext context,
        IRepository repo)
    {
        await Task.CompletedTask;
        
        // Try both claim types - JWT might map "sub" to NameIdentifier
        var userId = context.User.FindFirst("sub")?.Value 
            ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Results.Unauthorized();
        
        var passkey = repo.GetPasskey(id);
        if (passkey == null || passkey.user_id != userId)
            return Results.NotFound();
        
        if (string.IsNullOrWhiteSpace(request.name))
        {
            return Results.BadRequest(new Problem(
                "urn:mvn:error:validation",
                "Name is required",
                400,
                null,
                $"/api/v1/auth/passkeys/{id}"
            ));
        }
        
        var updatedPasskey = passkey with { name = request.name.Trim() };
        repo.UpdatePasskey(updatedPasskey);
        
        return Results.Ok(new PasskeyInfo(
            updatedPasskey.id,
            updatedPasskey.name,
            updatedPasskey.device_type,
            updatedPasskey.backed_up,
            updatedPasskey.created_at,
            updatedPasskey.last_used_at
        ));
    }

    /// <summary>
    /// Delete a passkey.
    /// </summary>
    private static async Task<IResult> DeletePasskey(
        string id,
        HttpContext context,
        IRepository repo)
    {
        await Task.CompletedTask;
        
        // Try both claim types - JWT might map "sub" to NameIdentifier
        var userId = context.User.FindFirst("sub")?.Value 
            ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Results.Unauthorized();
        
        var passkey = repo.GetPasskey(id);
        if (passkey == null || passkey.user_id != userId)
            return Results.NotFound();
        
        repo.DeletePasskey(id);
        
        return Results.NoContent();
    }

    private static string GetOriginFromRequest(HttpContext context)
    {
        var scheme = context.Request.Scheme;
        var host = context.Request.Host.ToString();
        return $"{scheme}://{host}";
    }

    #endregion

    private class LoginAttemptTracker
    {
        public int AttemptCount { get; set; }
        public DateTime LastAttempt { get; set; }
    }

    private class TurnstileResponse
    {
        public bool success { get; set; }
        public string[]? error_codes { get; set; }
        public string? challenge_ts { get; set; }
        public string? hostname { get; set; }
    }
    
    [System.Text.Json.Serialization.JsonSerializable(typeof(TurnstileResponse))]
    private partial class TurnstileJsonContext : System.Text.Json.Serialization.JsonSerializerContext
    {
    }

    /// <summary>
    /// Check if a user is the first admin (earliest created_at among admins).
    /// </summary>
    private static bool IsFirstAdmin(User user, IRepository repo)
    {
        if (user.role != "Admin")
            return false;
        
        var allAdmins = repo.ListUsers().Where(u => u.role == "Admin").ToList();
        if (!allAdmins.Any())
            return false;
        
        var firstAdmin = allAdmins.OrderBy(a => a.created_at).First();
        return firstAdmin.id == user.id;
    }
}
