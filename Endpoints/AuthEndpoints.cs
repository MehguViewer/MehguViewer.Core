using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using MehguViewer.Core.Shared;
using MehguViewer.Core.Services;
using MehguViewer.Core.Workers;
using MehguViewer.Core.Helpers;
using MehguViewer.Core.Infrastructures;
using Microsoft.AspNetCore.Mvc;

namespace MehguViewer.Core.Endpoints;

/// <summary>
/// Provides HTTP endpoints for authentication, authorization, and user management in the MehguViewer system.
/// </summary>
/// <remarks>
/// <para><strong>Authentication Management:</strong></para>
/// Handles user login, registration, JWT token generation, passkey (WebAuthn) authentication,
/// and user provisioning from external authentication providers.
/// 
/// <para><strong>Key Features:</strong></para>
/// <list type="bullet">
///   <item><description>Local username/password authentication with bcrypt password hashing</description></item>
///   <item><description>Passkey (WebAuthn) authentication for Admin and Uploader roles</description></item>
///   <item><description>JWT token generation and JWKS endpoint for token validation</description></item>
///   <item><description>User auto-provisioning from external auth servers</description></item>
///   <item><description>Cloudflare Turnstile integration for bot protection</description></item>
///   <item><description>Rate limiting and account lockout protection</description></item>
/// </list>
/// 
/// <para><strong>Security Features:</strong></para>
/// <list type="bullet">
///   <item><description>Rate limiting with configurable lockout duration</description></item>
///   <item><description>Input validation and sanitization (username format, password strength)</description></item>
///   <item><description>Cloudflare Turnstile challenge validation</description></item>
///   <item><description>JWT-based stateless authentication with JWKS support</description></item>
///   <item><description>Password rehashing for legacy password migration</description></item>
///   <item><description>Maintenance mode enforcement (Admin-only access)</description></item>
///   <item><description>Authorization scopes for endpoint protection</description></item>
/// </list>
/// 
/// <para><strong>Performance Optimizations:</strong></para>
/// <list type="bullet">
///   <item><description>Async/await pattern for non-blocking I/O operations</description></item>
///   <item><description>Request timing telemetry for performance monitoring</description></item>
///   <item><description>Concurrent dictionary for efficient rate limiting tracking</description></item>
///   <item><description>Efficient password verification with early exit on failure</description></item>
/// </list>
/// 
/// <para><strong>⚠️ TEMPORARY LOCAL AUTH:</strong></para>
/// These endpoints provide local authentication as a temporary solution.
/// When external auth server is integrated:
/// <list type="bullet">
///   <item><description>/login and /register will be deprecated (handled by auth server)</description></item>
///   <item><description>/provision will auto-create users from authenticated requests</description></item>
///   <item><description>Token validation will use JWKS from the auth server</description></item>
/// </list>
/// 
/// <para><strong>Future Enhancements:</strong></para>
/// <list type="bullet">
///   <item><description>Integration with external OAuth2/OIDC providers</description></item>
///   <item><description>Refresh token support for long-lived sessions</description></item>
///   <item><description>Token revocation/blacklist for enhanced security</description></item>
///   <item><description>Multi-factor authentication (TOTP, SMS)</description></item>
/// </list>
/// </remarks>
public static partial class AuthEndpoints
{
    #region Constants

    /// <summary>Performance threshold for slow request warnings (milliseconds).</summary>
    private const int SlowRequestThresholdMs = 2000;
    
    /// <summary>Minimum username length.</summary>
    private const int MinUsernameLength = 3;
    
    /// <summary>Maximum username length.</summary>
    private const int MaxUsernameLength = 32;
    
    /// <summary>Username validation pattern (alphanumeric and underscores only).</summary>
    private const string UsernamePattern = "^[a-zA-Z0-9_]+$";
    
    /// <summary>Compiled regex for username validation (performance optimization).</summary>
    private static readonly Regex UsernameRegex = new(UsernamePattern, RegexOptions.Compiled);
    
    /// <summary>
    /// Sanitizes username input by trimming and converting to lowercase.
    /// </summary>
    /// <param name="username">The username to sanitize.</param>
    /// <returns>Sanitized username or null if input is invalid.</returns>
    private static string? SanitizeUsername(string? username)
    {
        if (string.IsNullOrWhiteSpace(username))
            return null;
        
        var sanitized = username.Trim();
        
        // Prevent excessively long usernames (DoS protection)
        if (sanitized.Length > MaxUsernameLength)
            return null;
        
        return sanitized;
    }

    #endregion

    #region State Management

    /// <summary>Login attempt tracking for rate limiting (username and IP-based).</summary>
    private static readonly ConcurrentDictionary<string, LoginAttemptTracker> _loginAttempts = new();

    #endregion
    
    #region Endpoint Registration

    /// <summary>
    /// Registers authentication-related HTTP endpoints with the application's routing system.
    /// </summary>
    /// <param name="app">The endpoint route builder to register routes with.</param>
    /// <remarks>
    /// <para><strong>Public Endpoints (No Auth Required):</strong></para>
    /// <list type="bullet">
    ///   <item><description>POST /login - Authenticate with username/password</description></item>
    ///   <item><description>POST /register - Create new user account</description></item>
    ///   <item><description>GET /config - Retrieve public auth configuration</description></item>
    ///   <item><description>GET /.well-known/jwks.json - JWKS for JWT validation</description></item>
    ///   <item><description>POST /passkey/authenticate/options - Get passkey login challenge</description></item>
    ///   <item><description>POST /passkey/authenticate - Authenticate with passkey</description></item>
    /// </list>
    /// 
    /// <para><strong>Authenticated Endpoints:</strong></para>
    /// <list type="bullet">
    ///   <item><description>POST /logout - Invalidate current session (client-side)</description></item>
    ///   <item><description>GET /me - Retrieve current user profile</description></item>
    ///   <item><description>PATCH /me/password-login - Toggle password login</description></item>
    ///   <item><description>POST /provision - Auto-provision user from external auth</description></item>
    ///   <item><description>Passkey endpoints - Manage WebAuthn credentials</description></item>
    /// </list>
    /// 
    /// <para><strong>Admin Endpoints (MvnAdmin scope):</strong></para>
    /// <list type="bullet">
    ///   <item><description>GET /admin/auth - Retrieve full auth configuration</description></item>
    ///   <item><description>PUT/PATCH /admin/auth - Update auth configuration</description></item>
    /// </list>
    /// </remarks>
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth")
            .WithTags("Authentication")
            .WithDescription("User authentication and authorization endpoints");
        
        // Public endpoints (TEMPORARY: will be replaced by external auth server)
        group.MapPost("/login", Login)
            .WithName("Login")
            .WithSummary("Authenticate with username and password")
            .Produces<LoginResponse>(200)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(429)
            .ProducesProblem(503);
            
        group.MapPost("/register", Register)
            .WithName("Register")
            .WithSummary("Create a new user account")
            .Produces<LoginResponse>(200)
            .ProducesProblem(400)
            .ProducesProblem(403)
            .ProducesProblem(409);
            
        group.MapGet("/config", GetAuthConfig)
            .WithName("GetAuthConfig")
            .WithSummary("Retrieve public authentication configuration");
            
        group.MapGet("/.well-known/jwks.json", GetJwks)
            .WithName("GetJwks")
            .WithSummary("Retrieve JSON Web Key Set for JWT validation");
            
        group.MapPost("/logout", (Delegate)Logout)
            .RequireAuthorization()
            .WithName("Logout")
            .WithSummary("Logout current user (client-side token deletion)");
            
        group.MapGet("/me", GetCurrentUser)
            .RequireAuthorization()
            .WithName("GetCurrentUser")
            .WithSummary("Retrieve current user profile")
            .Produces<UserProfileResponse>(200)
            .ProducesProblem(401)
            .ProducesProblem(404);
            
        group.MapPatch("/me/password-login", TogglePasswordLogin)
            .RequireAuthorization()
            .WithName("TogglePasswordLogin")
            .WithSummary("Enable or disable password login for current user");
        
        // Passkey / WebAuthn endpoints
        group.MapPost("/passkey/register/options", GetPasskeyRegistrationOptions)
            .RequireAuthorization()
            .WithName("GetPasskeyRegistrationOptions")
            .WithSummary("Get options for passkey registration");
            
        group.MapPost("/passkey/register", CompletePasskeyRegistration)
            .RequireAuthorization()
            .WithName("CompletePasskeyRegistration")
            .WithSummary("Complete passkey registration");
            
        group.MapPost("/passkey/authenticate/options", GetPasskeyAuthenticationOptions)
            .WithName("GetPasskeyAuthenticationOptions")
            .WithSummary("Get options for passkey authentication");
            
        group.MapPost("/passkey/authenticate", AuthenticateWithPasskey)
            .WithName("AuthenticateWithPasskey")
            .WithSummary("Authenticate using a passkey");
            
        group.MapGet("/passkeys", GetUserPasskeys)
            .RequireAuthorization()
            .WithName("GetUserPasskeys")
            .WithSummary("Retrieve all passkeys for current user");
            
        group.MapPatch("/passkeys/{id}", RenamePasskey)
            .RequireAuthorization()
            .WithName("RenamePasskey")
            .WithSummary("Rename a passkey");
            
        group.MapDelete("/passkeys/{id}", DeletePasskey)
            .RequireAuthorization()
            .WithName("DeletePasskey")
            .WithSummary("Delete a passkey");
        
        // User auto-provisioning from external auth server
        // This endpoint creates or returns a user based on the authenticated token claims
        // When called with a valid external auth token, it ensures the user exists locally
        group.MapPost("/provision", ProvisionUser)
            .RequireAuthorization()
            .WithName("ProvisionUser")
            .WithSummary("Auto-provision user from external authentication");
        
        // Admin-only auth config (support both PUT and PATCH for compatibility)
        app.MapGet("/api/v1/admin/auth", GetFullAuthConfig)
            .RequireAuthorization("MvnAdmin")
            .WithName("GetFullAuthConfig")
            .WithSummary("Retrieve full authentication configuration (Admin only)")
            .WithTags("Admin");
            
        app.MapPut("/api/v1/admin/auth", UpdateAuthConfig)
            .RequireAuthorization("MvnAdmin")
            .WithName("UpdateAuthConfig")
            .WithSummary("Update authentication configuration (Admin only)")
            .WithTags("Admin");
            
        app.MapPatch("/api/v1/admin/auth", UpdateAuthConfig)
            .RequireAuthorization("MvnAdmin")
            .WithName("PatchAuthConfig")
            .WithSummary("Partially update authentication configuration (Admin only)")
            .WithTags("Admin");
    }

    #endregion

    #region Endpoint Handlers - Public

    /// <summary>
    /// Retrieves the JSON Web Key Set (JWKS) for JWT token validation.
    /// </summary>
    /// <param name="authService">Authentication service providing JWK data (injected).</param>
    /// <returns>JWKS response containing public keys for JWT signature verification.</returns>
    /// <remarks>
    /// <para><strong>Purpose:</strong></para>
    /// Provides the public key(s) used to verify JWT signatures. External services and
    /// clients can use this endpoint to validate tokens issued by this auth server.
    /// 
    /// <para><strong>RFC Compliance:</strong></para>
    /// Follows RFC 7517 (JSON Web Key) specification for key representation.
    /// 
    /// <para><strong>Security:</strong></para>
    /// Only exposes public keys - private keys remain secure on the server.
    /// </remarks>
    private static IResult GetJwks(
        [FromServices] AuthService authService,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var logger = loggerFactory.CreateLogger("MehguViewer.Core.Endpoints.AuthEndpoints");
        var traceId = context.TraceIdentifier;
        
        try
        {
            logger.LogDebug("JWKS requested (TraceId: {TraceId})", traceId);
            
            var jwk = authService.GetJwk();
            
            stopwatch.Stop();
            logger.LogInformation(
                "JWKS retrieved successfully in {Duration}ms (TraceId: {TraceId})",
                stopwatch.ElapsedMilliseconds, traceId);
            
            return Results.Ok(new { keys = new[] { jwk } });
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.LogError(ex,
                "Failed to retrieve JWKS in {Duration}ms (TraceId: {TraceId})",
                stopwatch.ElapsedMilliseconds, traceId);
            return ResultsExtensions.InternalServerError(
                "Failed to retrieve JSON Web Key Set",
                context.Request.Path,
                traceId);
        }
    }

    #endregion

    #region Endpoint Handlers - Login

    /// <summary>
    /// Authenticates a user with username and password credentials.
    /// </summary>
    /// <param name="request">Login request containing username, password, and optional Turnstile token.</param>
    /// <param name="repo">Repository instance for data access (injected).</param>
    /// <param name="config">Configuration instance for auth settings (injected).</param>
    /// <param name="context">Current HTTP context for request/response details.</param>
    /// <param name="httpClientFactory">HTTP client factory for Turnstile validation (injected).</param>
    /// <param name="authService">Authentication service for password verification (injected).</param>
    /// <param name="loggerFactory">Logger factory for diagnostic logging (injected).</param>
    /// <returns>
    /// Login response with JWT token on success, or RFC 7807 Problem Details on error.
    /// </returns>
    /// <remarks>
    /// <para><strong>Login Flow:</strong></para>
    /// <list type="number">
    ///   <item><description>Validate input (username and password required)</description></item>
    ///   <item><description>Check account lockout status (rate limiting)</description></item>
    ///   <item><description>Validate Cloudflare Turnstile if enabled</description></item>
    ///   <item><description>Verify credentials (username + password)</description></item>
    ///   <item><description>Check password login enabled status</description></item>
    ///   <item><description>Check maintenance mode (Admin bypass)</description></item>
    ///   <item><description>Rehash password if using legacy algorithm</description></item>
    ///   <item><description>Generate and return JWT token</description></item>
    /// </list>
    /// 
    /// <para><strong>Security Features:</strong></para>
    /// <list type="bullet">
    ///   <item><description>Rate limiting: Username-based and IP-based lockout</description></item>
    ///   <item><description>Generic error messages to prevent user enumeration</description></item>
    ///   <item><description>Password verification with timing-safe comparison</description></item>
    ///   <item><description>Automatic password rehashing for algorithm upgrades</description></item>
    ///   <item><description>Maintenance mode enforcement (Admin-only access)</description></item>
    ///   <item><description>Optional Cloudflare Turnstile bot protection</description></item>
    /// </list>
    /// 
    /// <para><strong>Rate Limiting:</strong></para>
    /// Failed login attempts are tracked per username and per IP address.
    /// After exceeding the configured threshold, the account is temporarily locked.
    /// Lockout duration is configurable via Auth:LockoutDurationMinutes.
    /// 
    /// <para><strong>Performance:</strong></para>
    /// Requests taking longer than 2000ms trigger warning logs for monitoring.
    /// Password verification is optimized with early exit on user not found.
    /// </remarks>
    private static async Task<IResult> Login(
        [FromBody] LoginRequestWithCf request, 
        [FromServices] IRepository repo, 
        [FromServices] IConfiguration config,
        HttpContext context,
        [FromServices] IHttpClientFactory httpClientFactory,
        [FromServices] AuthService authService,
        [FromServices] ILoggerFactory loggerFactory)
    {
        var stopwatch = Stopwatch.StartNew();
        var logger = loggerFactory.CreateLogger("MehguViewer.Core.Endpoints.AuthEndpoints");
        var requestPath = context.Request.Path;
        var traceId = context.TraceIdentifier;
        
        // Sanitize username input
        var sanitizedUsername = SanitizeUsername(request.username);
        
        logger.LogDebug(
            "Login attempt: Username={Username}, HasPassword={HasPassword}, TraceId={TraceId}",
            sanitizedUsername ?? "<null>", 
            !string.IsNullOrWhiteSpace(request.password),
            traceId);
        
        // === Phase 1: Input Validation ===
        if (string.IsNullOrWhiteSpace(sanitizedUsername) || string.IsNullOrWhiteSpace(request.password))
        {
            logger.LogWarning(
                "Login rejected: Missing or invalid credentials (Username: {UsernameProvided}, Password: {PasswordProvided}) (TraceId: {TraceId})",
                !string.IsNullOrWhiteSpace(request.username),
                !string.IsNullOrWhiteSpace(request.password),
                traceId);
            return ResultsExtensions.BadRequest(
                "Username and password are required",
                requestPath,
                traceId);
        }
        
        // === Phase 2: Rate Limiting ===
        var clientIp = GetClientIp(context);
        
        if (IsAccountLocked(sanitizedUsername, clientIp, config))
        {
            var lockoutMinutes = config.GetValue("Auth:LockoutDurationMinutes", 15);
            logger.LogWarning(
                "Login blocked: Account locked for '{Username}' from {Ip} (TraceId: {TraceId})",
                sanitizedUsername, clientIp, traceId);
            return Results.Problem(
                title: "Account temporarily locked",
                statusCode: 429,
                type: "urn:mvn:error:account-locked",
                detail: $"Too many failed login attempts. Please try again in {lockoutMinutes} minutes.",
                instance: requestPath
            );
        }
        
        // === Phase 3: Cloudflare Turnstile Validation ===
        var cfEnabled = config.GetValue("Auth:Cloudflare:Enabled", false);
        if (cfEnabled)
        {
            logger.LogDebug(
                "Validating Cloudflare Turnstile for '{Username}' (TraceId: {TraceId})",
                sanitizedUsername, traceId);
                
            var cfResult = await ValidateTurnstileToken(request.cf_turnstile_token, clientIp, config, httpClientFactory, logger);
            if (!cfResult.success)
            {
                logger.LogWarning(
                    "Login rejected: Cloudflare challenge failed for '{Username}' from {Ip}: {Error} (TraceId: {TraceId})",
                    sanitizedUsername, clientIp, cfResult.error, traceId);
                return Results.BadRequest(new Problem(
                    "urn:mvn:error:cf-challenge-failed",
                    "Cloudflare challenge failed",
                    400,
                    cfResult.error,
                    requestPath
                ));
            }
            
            logger.LogDebug(
                "Cloudflare Turnstile validated successfully for '{Username}' (TraceId: {TraceId})",
                sanitizedUsername, traceId);
        }
        
        // === Phase 4: Credential Validation ===
        var user = repo.GetUserByUsername(sanitizedUsername);
        if (user != null && !authService.VerifyPassword(request.password, user.password_hash))
        {
            // Password incorrect - set user to null for failed attempt handling
            user = null;
            logger.LogDebug(
                "Password verification failed for '{Username}' (TraceId: {TraceId})",
                sanitizedUsername, traceId);
        }

        if (user != null)
        {
            // === Phase 5: Account Status Checks ===
            
            // Check if user has disabled password login
            if (user.password_login_disabled)
            {
                logger.LogInformation(
                    "Login rejected: Password login disabled for '{Username}' (TraceId: {TraceId})",
                    request.username, traceId);
                return Results.Json(new Problem(
                    "urn:mvn:error:password-login-disabled",
                    "Password Login Disabled",
                    403,
                    "Password login is disabled for this account. Please use passkey authentication.",
                    requestPath
                ), AppJsonSerializerContext.Default.Problem, statusCode: 403);
            }
            
            // Check maintenance mode - only Admin can login during maintenance
            var systemConfig = repo.GetSystemConfig();
            if (systemConfig.maintenance_mode && user.role != "Admin")
            {
                logger.LogInformation(
                    "Login rejected: Maintenance mode for '{Username}' with role '{Role}' (TraceId: {TraceId})",
                    request.username, user.role, traceId);
                var problem = new Problem(
                    "urn:mvn:error:maintenance-mode",
                    "The system is currently in maintenance mode. Only administrators can log in.",
                    503,
                    "Maintenance Mode",
                    requestPath
                );
                return Results.Json(problem, AppJsonSerializerContext.Default.Problem, statusCode: 503);
            }
            
            // === Phase 6: Success Path ===
            
            // Clear login attempts on success
            ClearLoginAttempts(sanitizedUsername, clientIp);
            logger.LogDebug(
                "Cleared login attempts for '{Username}' (TraceId: {TraceId})",
                sanitizedUsername, traceId);
            
            // Check if password needs rehashing (legacy SHA256 -> bcrypt migration)
            if (authService.NeedsRehash(user.password_hash))
            {
                logger.LogInformation(
                    "Rehashing password for '{Username}' (legacy algorithm detected) (TraceId: {TraceId})",
                    sanitizedUsername, traceId);
                var newHash = authService.HashPassword(request.password);
                var updatedUser = user with { password_hash = newHash };
                repo.UpdateUser(updatedUser);
            }
            
            // Generate JWT token
            var token = authService.GenerateToken(user);
            
            stopwatch.Stop();
            var elapsedMs = stopwatch.ElapsedMilliseconds;
            
            logger.LogInformation(
                "User '{Username}' (Role: {Role}) logged in successfully from {Ip} in {Duration}ms (TraceId: {TraceId})",
                sanitizedUsername, user.role, clientIp, elapsedMs, traceId);
            
            if (elapsedMs > SlowRequestThresholdMs)
            {
                logger.LogWarning(
                    "Slow login request: {Duration}ms for '{Username}' (TraceId: {TraceId})",
                    elapsedMs, sanitizedUsername, traceId);
            }
            
            return Results.Ok(new LoginResponse(token, user.username, user.role));
        }
        
        // === Phase 7: Failed Attempt Handling ===
        
        // Record failed attempt
        RecordFailedAttempt(sanitizedUsername, clientIp);
        
        stopwatch.Stop();
        var failedElapsedMs = stopwatch.ElapsedMilliseconds;
        
        logger.LogWarning(
            "Failed login attempt for '{Username}' from {Ip} in {Duration}ms (TraceId: {TraceId})",
            sanitizedUsername, clientIp, failedElapsedMs, traceId);
        
        // Return generic error (don't reveal if user exists - security best practice)
        return ResultsExtensions.Unauthorized(
            "Invalid username or password",
            requestPath,
            traceId);
    }

    #endregion

    #region Endpoint Handlers - Registration

    /// <summary>
    /// Registers a new user account with username and password authentication.
    /// </summary>
    /// <param name="request">Registration request containing username, password, and optional Turnstile token.</param>
    /// <param name="repo">Repository instance for data access (injected).</param>
    /// <param name="config">Configuration instance for auth settings (injected).</param>
    /// <param name="context">Current HTTP context for request/response details.</param>
    /// <param name="httpClientFactory">HTTP client factory for Turnstile validation (injected).</param>
    /// <param name="authService">Authentication service for password hashing/validation (injected).</param>
    /// <param name="loggerFactory">Logger factory for diagnostic logging (injected).</param>
    /// <returns>
    /// Login response with JWT token on success, or RFC 7807 Problem Details on error.
    /// </returns>
    /// <remarks>
    /// <para><strong>Registration Flow:</strong></para>
    /// <list type="number">
    ///   <item><description>Validate input (username and password required)</description></item>
    ///   <item><description>Validate username format (3-32 chars, alphanumeric + underscores)</description></item>
    ///   <item><description>Validate password strength (via AuthService)</description></item>
    ///   <item><description>Check if username already exists</description></item>
    ///   <item><description>Determine role (first user = Admin, others = User)</description></item>
    ///   <item><description>Validate Turnstile token if Cloudflare is enabled</description></item>
    ///   <item><description>Create user with hashed password</description></item>
    ///   <item><description>Auto-login and return JWT token</description></item>
    /// </list>
    /// 
    /// <para><strong>First User Setup:</strong></para>
    /// The first registered user automatically receives Admin role for system initialization.
    /// Subsequent registrations create User (reader) accounts.
    /// 
    /// <para><strong>Security Validations:</strong></para>
    /// <list type="bullet">
    ///   <item><description>Username: 3-32 characters, alphanumeric + underscores only</description></item>
    ///   <item><description>Password: Strength validation via AuthService</description></item>
    ///   <item><description>Cloudflare Turnstile: Bot protection if enabled</description></item>
    ///   <item><description>Registration gate: Can be disabled in configuration</description></item>
    /// </list>
    /// 
    /// <para><strong>Performance:</strong></para>
    /// Requests taking longer than 2000ms trigger warning logs for monitoring.
    /// </remarks>
    private static async Task<IResult> Register(
        [FromBody] RegisterRequestWithCf request, 
        [FromServices] IRepository repo, 
        [FromServices] IConfiguration config,
        HttpContext context,
        [FromServices] IHttpClientFactory httpClientFactory,
        [FromServices] AuthService authService,
        [FromServices] ILoggerFactory loggerFactory)
    {
        var stopwatch = Stopwatch.StartNew();
        var logger = loggerFactory.CreateLogger("MehguViewer.Core.Endpoints.AuthEndpoints");
        var requestPath = context.Request.Path;
        var traceId = context.TraceIdentifier;
        var clientIp = GetClientIp(context);
        
        // Sanitize username input
        var sanitizedUsername = SanitizeUsername(request.username);
        
        logger.LogDebug(
            "Registration attempt: Username={Username}, HasPassword={HasPassword}, ClientIp={Ip}, TraceId={TraceId}",
            sanitizedUsername ?? "<null>", 
            !string.IsNullOrWhiteSpace(request.password),
            clientIp,
            traceId);

        // === Phase 1: Input Validation ===
        if (string.IsNullOrWhiteSpace(sanitizedUsername) || string.IsNullOrWhiteSpace(request.password))
        {
            logger.LogWarning(
                "Registration rejected: Missing or invalid credentials (Username: {UsernameProvided}, Password: {PasswordProvided}) (TraceId: {TraceId})",
                !string.IsNullOrWhiteSpace(request.username),
                !string.IsNullOrWhiteSpace(request.password),
                traceId);
            return ResultsExtensions.BadRequest("Username and password are required", requestPath, traceId);
        }
        
        // Validate username length
        if (sanitizedUsername.Length < MinUsernameLength || sanitizedUsername.Length > MaxUsernameLength)
        {
            logger.LogWarning(
                "Registration rejected: Invalid username length {Length} for '{Username}' (TraceId: {TraceId})",
                sanitizedUsername.Length, sanitizedUsername, traceId);
            return ResultsExtensions.BadRequest(
                $"Username must be between {MinUsernameLength} and {MaxUsernameLength} characters",
                requestPath,
                traceId);
        }
        
        // Validate username format (alphanumeric, underscores only)
        if (!UsernameRegex.IsMatch(sanitizedUsername))
        {
            logger.LogWarning(
                "Registration rejected: Invalid username format '{Username}' (TraceId: {TraceId})",
                sanitizedUsername, traceId);
            return ResultsExtensions.BadRequest(
                "Username can only contain letters, numbers, and underscores",
                requestPath,
                traceId);
        }
        
        // Validate password strength
        var (isValid, error) = authService.ValidatePasswordStrength(request.password);
        if (!isValid)
        {
            logger.LogWarning(
                "Registration rejected: Weak password for '{Username}': {Error} (TraceId: {TraceId})",
                sanitizedUsername, error, traceId);
            return Results.BadRequest(new Problem(
                "urn:mvn:error:weak-password", 
                error!, 
                400, 
                null, 
                requestPath
            ));
        }
        
        // === Phase 2: Uniqueness Check ===
        if (repo.GetUserByUsername(sanitizedUsername) != null)
        {
            logger.LogWarning(
                "Registration rejected: Username '{Username}' already exists from {Ip} (TraceId: {TraceId})",
                sanitizedUsername, clientIp, traceId);
            return Results.Conflict(new Problem(
                "urn:mvn:error:user-exists", 
                "A user with this username already exists", 
                409, 
                null, 
                requestPath
            ));
        }
        
        // === Phase 3: Role Determination ===
        string role = "User";
        
        // First user becomes Admin
        if (!repo.IsAdminSet())
        {
            role = "Admin";
            logger.LogInformation(
                "First user registration - assigning Admin role to '{Username}' from {Ip} (TraceId: {TraceId})",
                sanitizedUsername, clientIp, traceId);
        }
        else
        {
            // Check if registration is open
            var registrationOpen = config.GetValue("Auth:RegistrationOpen", true);
            if (!registrationOpen)
            {
                logger.LogWarning(
                    "Registration rejected: Registration closed for '{Username}' from {Ip} (TraceId: {TraceId})",
                    sanitizedUsername, clientIp, traceId);
                return Results.Problem(
                    title: "Registration is currently closed",
                    statusCode: 403,
                    type: "urn:mvn:error:registration-closed",
                    instance: requestPath
                );
            }
            
            // === Phase 4: Cloudflare Turnstile Validation ===
            var cfEnabled = config.GetValue("Auth:Cloudflare:Enabled", false);
            if (cfEnabled)
            {
                logger.LogDebug(
                    "Validating Cloudflare Turnstile for '{Username}' (TraceId: {TraceId})",
                    sanitizedUsername, traceId);
                    
                var cfResult = await ValidateTurnstileToken(request.cf_turnstile_token, clientIp, config, httpClientFactory, logger);
                if (!cfResult.success)
                {
                    logger.LogWarning(
                        "Registration rejected: Cloudflare challenge failed for '{Username}' from {Ip}: {Error} (TraceId: {TraceId})",
                        sanitizedUsername, clientIp, cfResult.error, traceId);
                    return Results.BadRequest(new Problem(
                        "urn:mvn:error:cf-challenge-failed",
                        "Cloudflare challenge failed",
                        400,
                        cfResult.error,
                        requestPath
                    ));
                }
                
                logger.LogDebug(
                    "Cloudflare Turnstile validated successfully for '{Username}' (TraceId: {TraceId})",
                    sanitizedUsername, traceId);
            }
        }
        
        // === Phase 5: User Creation ===
        try
        {
            logger.LogDebug(
                "Creating user '{Username}' with role '{Role}' (TraceId: {TraceId})",
                sanitizedUsername, role, traceId);
                
            var user = new User(
                UrnHelper.CreateUserUrn(), 
                sanitizedUsername, 
                authService.HashPassword(request.password), 
                role, 
                DateTime.UtcNow
            );
            repo.AddUser(user);
            
            stopwatch.Stop();
            var elapsedMs = stopwatch.ElapsedMilliseconds;
            
            logger.LogInformation(
                "User '{Username}' registered successfully with role '{Role}' from {Ip} in {Duration}ms (TraceId: {TraceId})",
                sanitizedUsername, role, clientIp, elapsedMs, traceId);
            
            if (elapsedMs > SlowRequestThresholdMs)
            {
                logger.LogWarning(
                    "Slow registration request: {Duration}ms for '{Username}' (TraceId: {TraceId})",
                    elapsedMs, sanitizedUsername, traceId);
            }
            
            // Auto-login after registration
            var token = authService.GenerateToken(user);
            return Results.Ok(new LoginResponse(token, user.username, user.role));
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.LogError(ex,
                "Registration failed for '{Username}' from {Ip}: {Error} in {Duration}ms (TraceId: {TraceId})",
                sanitizedUsername, clientIp, ex.Message, stopwatch.ElapsedMilliseconds, traceId);
            return ResultsExtensions.InternalServerError(
                "An error occurred during registration",
                requestPath,
                traceId);
        }
    }

    #endregion

    #region Endpoint Handlers - User Management

    /// <summary>
    /// Logs out the current user by invalidating their session (client-side only).
    /// </summary>
    /// <param name="context">Current HTTP context for request/response details.</param>
    /// <returns>
    /// Success message confirming logout.
    /// </returns>
    /// <remarks>
    /// <para><strong>Stateless JWT Limitation:</strong></para>
    /// Since JWTs are stateless, true server-side invalidation is not possible without
    /// maintaining a token blacklist. This endpoint returns success, but the actual
    /// logout occurs client-side by deleting the token from storage.
    /// 
    /// <para><strong>Production Considerations:</strong></para>
    /// For enhanced security in production:
    /// <list type="bullet">
    ///   <item><description>Implement token blacklist (Redis/database)</description></item>
    ///   <item><description>Use short-lived access tokens with refresh tokens</description></item>
    ///   <item><description>Implement token revocation endpoints</description></item>
    /// </list>
    /// 
    /// <para><strong>Client Responsibility:</strong></para>
    /// Clients must delete the JWT token from local storage, session storage, or cookies
    /// to complete the logout process.
    /// </remarks>
    private static async Task<IResult> Logout(HttpContext context, [FromServices] ILoggerFactory loggerFactory)
    {
        await Task.CompletedTask;
        
        var logger = loggerFactory.CreateLogger("MehguViewer.Core.Endpoints.AuthEndpoints");
        var traceId = context.TraceIdentifier;
        var userId = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value 
                     ?? context.User.FindFirst("sub")?.Value;
        var username = context.User.Identity?.Name;
        var clientIp = GetClientIp(context);
        
        logger.LogInformation(
            "User '{Username}' (ID: {UserId}) logged out from {Ip} (TraceId: {TraceId})",
            username ?? "Unknown", userId ?? "Unknown", clientIp, traceId);
        
        // JWT tokens are stateless - cannot invalidate server-side without blacklist
        // Client must delete the token from storage to complete logout
        // Future: Implement token blacklist or use short-lived tokens with refresh tokens
        return Results.Ok(new { message = "Logged out successfully" });
    }

    /// <summary>
    /// Retrieves the profile information for the currently authenticated user.
    /// </summary>
    /// <param name="context">Current HTTP context containing user claims.</param>
    /// <param name="repo">Repository instance for data access (injected).</param>
    /// <returns>
    /// User profile response with public information, or RFC 7807 Problem Details on error.
    /// </returns>
    /// <remarks>
    /// <para><strong>Authentication Required:</strong></para>
    /// This endpoint requires a valid JWT token. The user ID is extracted from the
    /// "sub" (subject) claim or NameIdentifier claim.
    /// 
    /// <para><strong>Response Data:</strong></para>
    /// Returns user profile without sensitive data:
    /// <list type="bullet">
    ///   <item><description>User ID (URN)</description></item>
    ///   <item><description>Username</description></item>
    ///   <item><description>Role (Admin, Uploader, User)</description></item>
    ///   <item><description>Account creation timestamp</description></item>
    ///   <item><description>Password login status</description></item>
    ///   <item><description>First admin flag</description></item>
    /// </list>
    /// 
    /// <para><strong>Security:</strong></para>
    /// Password hash is never included in the response.
    /// 
    /// <para><strong>First Admin Detection:</strong></para>
    /// The is_first_admin flag indicates if this admin account was the first created,
    /// which may grant special privileges in the UI.
    /// </remarks>
    private static async Task<IResult> GetCurrentUser(
        HttpContext context, 
        [FromServices] IRepository repo,
        [FromServices] ILoggerFactory loggerFactory)
    {
        await Task.CompletedTask;
        
        var logger = loggerFactory.CreateLogger("MehguViewer.Core.Endpoints.AuthEndpoints");
        var traceId = context.TraceIdentifier;
        var requestPath = context.Request.Path;
        
        // Extract user ID from JWT claims (try both standard claim types)
        var userId = context.User.FindFirst("sub")?.Value 
            ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        
        if (string.IsNullOrEmpty(userId))
        {
            logger.LogWarning(
                "GetCurrentUser failed: User ID not found in claims (TraceId: {TraceId})",
                traceId);
            return Results.Unauthorized();
        }
        
        logger.LogDebug(
            "Retrieving profile for user {UserId} (TraceId: {TraceId})",
            userId, traceId);
        
        var user = repo.GetUser(userId);
        if (user == null)
        {
            logger.LogWarning(
                "GetCurrentUser failed: User {UserId} not found in database (TraceId: {TraceId})",
                userId, traceId);
            return Results.NotFound(new Problem(
                "urn:mvn:error:not-found",
                "User not found",
                404,
                null,
                "/api/v1/auth/me"
            ));
        }
        
        logger.LogDebug(
            "Profile retrieved successfully for '{Username}' (Role: {Role}) (TraceId: {TraceId})",
            user.username, user.role, traceId);
        
        // Return user info without password hash (security)
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
    /// Toggles password-based login for the currently authenticated user.
    /// </summary>
    /// <param name="request">Request containing the desired password login state.</param>
    /// <param name="context">Current HTTP context containing user claims.</param>
    /// <param name="repo">Repository instance for data access (injected).</param>
    /// <returns>
    /// Response with updated status and confirmation message, or RFC 7807 Problem Details on error.
    /// </returns>
    /// <remarks>
    /// <para><strong>Security Requirement:</strong></para>
    /// User must have at least one passkey registered to disable password login.
    /// This prevents account lockout - users must maintain an alternative authentication method.
    /// 
    /// <para><strong>Use Cases:</strong></para>
    /// <list type="bullet">
    ///   <item><description><strong>Enable Passkey-Only:</strong> Disable password login for enhanced security</description></item>
    ///   <item><description><strong>Re-enable Passwords:</strong> Restore password login if needed</description></item>
    /// </list>
    /// 
    /// <para><strong>Validation:</strong></para>
    /// When disabling password login (request.disable = true):
    /// <list type="bullet">
    ///   <item><description>Checks for at least one registered passkey</description></item>
    ///   <item><description>Returns 400 Bad Request if no passkeys exist</description></item>
    ///   <item><description>Prevents accidental account lockout</description></item>
    /// </list>
    /// 
    /// <para><strong>Authentication Required:</strong></para>
    /// User must be authenticated with a valid JWT token.
    /// </remarks>
    private static async Task<IResult> TogglePasswordLogin(
        [FromBody] TogglePasswordLoginRequest request,
        HttpContext context, 
        [FromServices] IRepository repo)
    {
        await Task.CompletedTask;
        
        // Extract user ID from JWT claims
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
        
        // Security: Require at least one passkey before disabling password login
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
        
        // Update user password login status
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
    /// Auto-provisions a reader (User role) account from external authentication.
    /// </summary>
    /// <param name="context">Current HTTP context containing external auth claims.</param>
    /// <param name="repo">Repository instance for data access (injected).</param>
    /// <returns>
    /// Provisioning response indicating if user was created or already existed.
    /// </returns>
    /// <remarks>
    /// <para><strong>Purpose:</strong></para>
    /// Called after external authentication (OAuth2/OIDC) to ensure the authenticated user
    /// has a local reader account in the system. Creates account on first login.
    /// 
    /// <para><strong>⚠️ IMPORTANT - Role Restrictions:</strong></para>
    /// Only "User" (reader) accounts are auto-provisioned from external auth.
    /// Admin and Uploader accounts are managed locally through the admin panel.
    /// This ensures privileged access is controlled by core server administrators.
    /// 
    /// <para><strong>Username Resolution:</strong></para>
    /// Username is determined from claims in priority order:
    /// <list type="number">
    ///   <item><description>ClaimTypes.Name or "preferred_username" claim</description></item>
    ///   <item><description>Email local part (before @) if email claim exists</description></item>
    ///   <item><description>Generated username from user ID: user_{id_first_8_chars}</description></item>
    /// </list>
    /// If username conflicts occur, a random 4-character suffix is appended.
    /// 
    /// <para><strong>Claims Processing:</strong></para>
    /// Extracts user information from JWT claims:
    /// <list type="bullet">
    ///   <item><description><strong>Required:</strong> "sub" (subject) claim - used as user ID</description></item>
    ///   <item><description><strong>Optional:</strong> name, preferred_username, email claims</description></item>
    ///   <item><description><strong>Ignored:</strong> scope claim (roles not imported)</description></item>
    /// </list>
    /// 
    /// <para><strong>Security Design:</strong></para>
    /// <list type="bullet">
    ///   <item><description>External user IDs are stored directly (not URNs)</description></item>
    ///   <item><description>No password hash for externally authenticated users</description></item>
    ///   <item><description>Role escalation requires admin intervention</description></item>
    ///   <item><description>Existing users are not modified (roles preserved)</description></item>
    /// </list>
    /// 
    /// <para><strong>Usage Flow:</strong></para>
    /// <list type="number">
    ///   <item><description>User authenticates with external provider</description></item>
    ///   <item><description>External auth validates and issues JWT</description></item>
    ///   <item><description>Client calls /provision with external JWT</description></item>
    ///   <item><description>System creates or retrieves local user record</description></item>
    ///   <item><description>User can access content with reader permissions</description></item>
    /// </list>
    /// </remarks>
    private static async Task<IResult> ProvisionUser(HttpContext context, [FromServices] IRepository repo)
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

    /// <summary>
    /// Retrieves public authentication configuration for client display and validation.
    /// </summary>
    /// <param name="repo">Repository instance for accessing system configuration (injected).</param>
    /// <returns>
    /// Public authentication configuration (registration status, Cloudflare settings).
    /// </returns>
    /// <remarks>
    /// <para><strong>Public Information:</strong></para>
    /// This endpoint is publicly accessible (no authentication required) and returns
    /// only non-sensitive configuration data needed by clients:
    /// <list type="bullet">
    ///   <item><description>Registration open/closed status</description></item>
    ///   <item><description>Cloudflare Turnstile enabled status</description></item>
    ///   <item><description>Cloudflare site key (public, safe to expose)</description></item>
    /// </list>
    /// 
    /// <para><strong>Security:</strong></para>
    /// Secret keys and internal configuration are NOT exposed.
    /// Site key is conditionally included only when Cloudflare is enabled.
    /// 
    /// <para><strong>Use Cases:</strong></para>
    /// <list type="bullet">
    ///   <item><description>Client determines if registration form should be shown</description></item>
    ///   <item><description>Client loads Cloudflare Turnstile widget if enabled</description></item>
    ///   <item><description>Client configures authentication UI based on settings</description></item>
    /// </list>
    /// </remarks>
    private static async Task<IResult> GetAuthConfig([FromServices] IRepository repo)
    {
        await Task.CompletedTask;
        
        var config = repo.GetSystemConfig();
        return Results.Ok(new AuthConfigPublic(
            config.registration_open, 
            config.cloudflare_enabled, 
            config.cloudflare_enabled ? config.cloudflare_site_key : null
        ));
    }

    /// <summary>
    /// Retrieves complete authentication configuration for administrative management.
    /// </summary>
    /// <param name="repo">Repository instance for accessing system configuration (injected).</param>
    /// <returns>
    /// Full authentication configuration including all settings (secret key masked).
    /// </returns>
    /// <remarks>
    /// <para><strong>Admin-Only Access:</strong></para>
    /// Requires MvnAdmin scope. Returns complete auth configuration for management:
    /// <list type="bullet">
    ///   <item><description>Registration open status</description></item>
    ///   <item><description>Login attempt limits and lockout duration</description></item>
    ///   <item><description>JWT token expiry settings</description></item>
    ///   <item><description>Cloudflare Turnstile configuration</description></item>
    ///   <item><description>2FA and danger zone password requirements</description></item>
    /// </list>
    /// 
    /// <para><strong>Security Masking:</strong></para>
    /// Cloudflare secret key is masked with "********" if configured.
    /// This prevents accidental exposure while showing that a key is set.
    /// 
    /// <para><strong>Use Case:</strong></para>
    /// Admin panel displays current configuration and allows administrators
    /// to modify auth settings through the UpdateAuthConfig endpoint.
    /// </remarks>
    private static async Task<IResult> GetFullAuthConfig([FromServices] IRepository repo)
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

    /// <summary>
    /// Updates authentication configuration settings (admin-only operation).
    /// </summary>
    /// <param name="update">Partial configuration update with optional fields.</param>
    /// <param name="repo">Repository instance for updating system configuration (injected).</param>
    /// <returns>
    /// 204 No Content on successful update.
    /// </returns>
    /// <remarks>
    /// <para><strong>Admin-Only Access:</strong></para>
    /// Requires MvnAdmin scope. Allows modification of all authentication settings.
    /// 
    /// <para><strong>Partial Updates:</strong></para>
    /// All fields in the request are optional. Only provided fields are updated:
    /// <list type="bullet">
    ///   <item><description>registration_open - Enable/disable user registration</description></item>
    ///   <item><description>max_login_attempts - Failed attempts before lockout</description></item>
    ///   <item><description>lockout_duration_minutes - Account lockout duration</description></item>
    ///   <item><description>token_expiry_hours - JWT token lifetime</description></item>
    ///   <item><description>cloudflare - Turnstile configuration (enabled, site key, secret key)</description></item>
    ///   <item><description>require_2fa_passkey - Enforce passkey for privileged accounts</description></item>
    ///   <item><description>require_password_for_danger_zone - Password confirmation for sensitive operations</description></item>
    /// </list>
    /// 
    /// <para><strong>Secret Key Handling:</strong></para>
    /// Cloudflare secret key is only updated if:
    /// <list type="bullet">
    ///   <item><description>A new value is provided</description></item>
    ///   <item><description>The value is not the mask string "********"</description></item>
    /// </list>
    /// This allows updating other Cloudflare settings without changing the secret.
    /// 
    /// <para><strong>HTTP Methods:</strong></para>
    /// Supports both PUT and PATCH for flexibility. Both perform partial updates.
    /// 
    /// <para><strong>Impact:</strong></para>
    /// Changes take effect immediately for new authentication requests.
    /// Existing sessions/tokens are not affected until they expire.
    /// </remarks>
    private static async Task<IResult> UpdateAuthConfig([FromBody] AuthConfigUpdate update, [FromServices] IRepository repo)
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
                ? update.cloudflare!.turnstile_secret_key 
                : currentConfig.cloudflare_secret_key,
            require_2fa_passkey = update.require_2fa_passkey ?? currentConfig.require_2fa_passkey,
            require_password_for_danger_zone = update.require_password_for_danger_zone ?? currentConfig.require_password_for_danger_zone
        };
        
        repo.UpdateSystemConfig(newConfig);
        
        return Results.NoContent();
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Validates if a string is a properly formatted IP address (IPv4 or IPv6).
    /// </summary>
    /// <param name="ipAddress">The IP address string to validate.</param>
    /// <returns>True if valid IP address, false otherwise.</returns>
    private static bool IsValidIpAddress(string? ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
            return false;
        
        return System.Net.IPAddress.TryParse(ipAddress, out _);
    }

    /// <summary>
    /// Extracts the client's IP address from the HTTP request context.
    /// </summary>
    /// <param name="context">The current HTTP context containing request headers.</param>
    /// <returns>
    /// The client's IP address as a string. Returns "unknown" if unable to determine.
    /// </returns>
    /// <remarks>
    /// <para><strong>Priority Order:</strong></para>
    /// <list type="number">
    ///   <item><description>CF-Connecting-IP header (Cloudflare CDN)</description></item>
    ///   <item><description>X-Forwarded-For header first entry (reverse proxies)</description></item>
    ///   <item><description>RemoteIpAddress from connection (direct connection)</description></item>
    /// </list>
    /// 
    /// <para><strong>Security Note:</strong></para>
    /// When behind Cloudflare, CF-Connecting-IP is the most reliable source as it cannot
    /// be spoofed by the client. X-Forwarded-For can be manipulated if not behind a trusted proxy.
    /// All IP addresses are validated to prevent injection attacks.
    /// </remarks>
    private static string GetClientIp(HttpContext context)
    {
        // Check for CF-Connecting-IP header (Cloudflare) - most trustworthy behind CF
        var cfIp = context.Request.Headers["CF-Connecting-IP"].FirstOrDefault();
        if (!string.IsNullOrEmpty(cfIp))
        {
            var trimmedCfIp = cfIp.Trim();
            if (IsValidIpAddress(trimmedCfIp))
                return trimmedCfIp;
        }
        
        // Check for X-Forwarded-For header (reverse proxies) - use first IP (original client)
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            // Take the first IP in the list (original client IP)
            var firstIp = forwardedFor.Split(',')[0].Trim();
            if (IsValidIpAddress(firstIp))
                return firstIp;
        }
        
        // Fall back to remote IP address from direct connection
        var remoteIp = context.Connection.RemoteIpAddress?.ToString();
        return IsValidIpAddress(remoteIp) ? remoteIp! : "unknown";
    }

    /// <summary>
    /// Determines if an account is currently locked due to excessive failed login attempts.
    /// </summary>
    /// <param name="username">The username to check for lockout status.</param>
    /// <param name="clientIp">The client IP address to check for rate limiting.</param>
    /// <param name="config">Configuration instance containing lockout settings.</param>
    /// <returns>
    /// True if the account is locked (either by username or IP), false otherwise.
    /// </returns>
    /// <remarks>
    /// <para><strong>Lockout Strategy:</strong></para>
    /// Implements dual-layer rate limiting:
    /// <list type="bullet">
    ///   <item><description><strong>Username-based:</strong> Locks after N failed attempts (default: 5)</description></item>
    ///   <item><description><strong>IP-based:</strong> Locks after 10x failed attempts (default: 50) to prevent distributed attacks</description></item>
    /// </list>
    /// 
    /// <para><strong>Configuration:</strong></para>
    /// <list type="bullet">
    ///   <item><description>Auth:MaxLoginAttempts - Max attempts before lockout (default: 5)</description></item>
    ///   <item><description>Auth:LockoutDurationMinutes - Duration of lockout (default: 15)</description></item>
    /// </list>
    /// 
    /// <para><strong>Security Design:</strong></para>
    /// IP-based limiting is more lenient (10x threshold) to avoid blocking legitimate users
    /// behind shared IP addresses (corporate networks, NAT, etc.) while still providing
    /// protection against distributed brute-force attacks.
    /// 
    /// <para><strong>Lockout Expiration:</strong></para>
    /// Lockouts automatically expire after the configured duration. The lockout timer
    /// resets with each failed attempt (sliding window).
    /// </remarks>
    private static bool IsAccountLocked(string username, string clientIp, IConfiguration config)
    {
        var maxAttempts = config.GetValue("Auth:MaxLoginAttempts", 5);
        var lockoutMinutes = config.GetValue("Auth:LockoutDurationMinutes", 15);
        var lockoutDuration = TimeSpan.FromMinutes(lockoutMinutes);
        
        // Validate inputs to prevent empty key lookups
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(clientIp))
            return false;
        
        // Check by username (case-insensitive)
        var usernameKey = $"user:{username.ToLowerInvariant()}";
        if (_loginAttempts.TryGetValue(usernameKey, out var userTracker))
        {
            var timeSinceLastAttempt = DateTime.UtcNow - userTracker.LastAttempt;
            if (userTracker.AttemptCount >= maxAttempts && timeSinceLastAttempt < lockoutDuration)
            {
                return true; // Account locked by username
            }
            // Clean up expired entries to prevent memory bloat
            else if (timeSinceLastAttempt >= lockoutDuration.Add(TimeSpan.FromHours(1)))
            {
                _loginAttempts.TryRemove(usernameKey, out _);
            }
        }
        
        // Check by IP (more lenient - 10x the attempts to avoid blocking shared IPs)
        var ipKey = $"ip:{clientIp}";
        if (_loginAttempts.TryGetValue(ipKey, out var ipTracker))
        {
            var timeSinceLastAttempt = DateTime.UtcNow - ipTracker.LastAttempt;
            if (ipTracker.AttemptCount >= maxAttempts * 10 && timeSinceLastAttempt < lockoutDuration)
            {
                return true; // Account locked by IP
            }
            // Clean up expired entries to prevent memory bloat
            else if (timeSinceLastAttempt >= lockoutDuration.Add(TimeSpan.FromHours(1)))
            {
                _loginAttempts.TryRemove(ipKey, out _);
            }
        }
        
        return false; // Not locked
    }

    /// <summary>
    /// Records a failed login attempt for both username and IP address tracking.
    /// </summary>
    /// <param name="username">The username that failed authentication.</param>
    /// <param name="clientIp">The client IP address where the attempt originated.</param>
    /// <remarks>
    /// <para><strong>Tracking Strategy:</strong></para>
    /// Maintains separate counters for:
    /// <list type="bullet">
    ///   <item><description>Username-based tracking (case-insensitive)</description></item>
    ///   <item><description>IP-based tracking</description></item>
    /// </list>
    /// 
    /// <para><strong>Concurrent Safety:</strong></para>
    /// Uses ConcurrentDictionary.AddOrUpdate for thread-safe counter updates.
    /// Multiple simultaneous login attempts are handled correctly.
    /// 
    /// <para><strong>Timestamp Update:</strong></para>
    /// Each failed attempt updates the LastAttempt timestamp, implementing a
    /// sliding window lockout mechanism (timer resets with each failure).
    /// </remarks>
    private static void RecordFailedAttempt(string username, string clientIp)
    {
        // Validate inputs to prevent invalid key creation
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(clientIp))
            return;
        
        var usernameKey = $"user:{username.ToLowerInvariant()}";
        var ipKey = $"ip:{clientIp}";
        var now = DateTime.UtcNow;
        
        // Update username-based tracking (thread-safe)
        _loginAttempts.AddOrUpdate(usernameKey, 
            _ => new LoginAttemptTracker { AttemptCount = 1, LastAttempt = now },
            (_, tracker) => 
            {
                tracker.AttemptCount++;
                tracker.LastAttempt = now; // Sliding window
                return tracker;
            });
        
        // Update IP-based tracking (thread-safe)
        _loginAttempts.AddOrUpdate(ipKey,
            _ => new LoginAttemptTracker { AttemptCount = 1, LastAttempt = now },
            (_, tracker) =>
            {
                tracker.AttemptCount++;
                tracker.LastAttempt = now; // Sliding window
                return tracker;
            });
    }

    /// <summary>
    /// Clears failed login attempt tracking for a username upon successful authentication.
    /// </summary>
    /// <param name="username">The username to clear failed attempts for.</param>
    /// <param name="clientIp">The client IP address (currently unused but kept for future use).</param>
    /// <remarks>
    /// <para><strong>Behavior:</strong></para>
    /// Only clears username-based tracking. IP-based tracking is NOT cleared to maintain
    /// protection against distributed attacks from the same IP using different usernames.
    /// 
    /// <para><strong>Thread Safety:</strong></para>
    /// Uses ConcurrentDictionary.TryRemove for thread-safe removal.
    /// </remarks>
    private static void ClearLoginAttempts(string username, string clientIp)
    {
        // Validate input
        if (string.IsNullOrWhiteSpace(username))
            return;
        
        var usernameKey = $"user:{username.ToLowerInvariant()}";
        _loginAttempts.TryRemove(usernameKey, out _);
        // Note: IP tracking intentionally NOT cleared to maintain distributed attack protection
    }

    /// <summary>
    /// Validates a Cloudflare Turnstile challenge token to prevent bot attacks.
    /// </summary>
    /// <param name="token">The Turnstile response token from the client.</param>
    /// <param name="clientIp">The client IP address for validation.</param>
    /// <param name="config">Configuration instance containing Turnstile secret key.</param>
    /// <param name="httpClientFactory">HTTP client factory for making verification request.</param>
    /// <returns>
    /// A tuple containing success status and optional error message.
    /// </returns>
    /// <remarks>
    /// <para><strong>Cloudflare Turnstile Integration:</strong></para>
    /// Validates the challenge token by calling Cloudflare's siteverify endpoint.
    /// This provides bot protection similar to reCAPTCHA but with better privacy.
    /// 
    /// <para><strong>Configuration:</strong></para>
    /// Requires Auth:Cloudflare:TurnstileSecretKey in configuration.
    /// If secret key is missing but CF is enabled, validation passes (fail-open for misconfiguration).
    /// 
    /// <para><strong>Error Handling:</strong></para>
    /// On network errors or Cloudflare API failures, validation FAILS (fail-closed)
    /// to maintain security. Logs should be monitored for configuration issues.
    /// 
    /// <para><strong>Security:</strong></para>
    /// The secret key is never exposed to the client and is only used server-side.
    /// Token validation includes client IP to prevent token replay attacks.
    /// </remarks>
    private static async Task<(bool success, string? error)> ValidateTurnstileToken(
        string? token, 
        string clientIp, 
        IConfiguration config,
        IHttpClientFactory httpClientFactory,
        ILogger? logger = null)
    {
        if (string.IsNullOrEmpty(token))
        {
            logger?.LogWarning("Cloudflare Turnstile validation failed: Token is missing");
            return (false, "Cloudflare challenge token is required");
        }
        
        var secretKey = config.GetValue<string>("Auth:Cloudflare:TurnstileSecretKey");
        if (string.IsNullOrEmpty(secretKey))
        {
            logger?.LogWarning(
                "Cloudflare Turnstile enabled but TurnstileSecretKey not configured - failing open");
            // Misconfiguration: CF enabled but no secret key - fail-open to avoid breaking auth
            return (true, null);
        }
        
        try
        {
            logger?.LogDebug("Validating Cloudflare Turnstile token for IP {ClientIp}", clientIp);
            
            var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10); // Prevent hanging on slow CF API
            
            var formContent = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("secret", secretKey),
                new KeyValuePair<string, string>("response", token),
                new KeyValuePair<string, string>("remoteip", clientIp)
            });
            
            var response = await client.PostAsync(
                "https://challenges.cloudflare.com/turnstile/v0/siteverify", 
                formContent);
            
            if (!response.IsSuccessStatusCode)
            {
                logger?.LogWarning(
                    "Cloudflare Turnstile API returned {StatusCode}", response.StatusCode);
                return (false, $"Turnstile API error: {response.StatusCode}");
            }
            
            var responseBody = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize(responseBody, TurnstileJsonContext.Default.TurnstileResponse);
            
            if (result?.success == true)
            {
                logger?.LogDebug("Cloudflare Turnstile validation successful for IP {ClientIp}", clientIp);
                return (true, null); // Challenge passed
            }
            
            // Challenge failed - provide error details
            var errorCodes = result?.error_codes != null 
                ? string.Join(", ", result.error_codes) 
                : "Unknown error";
            
            logger?.LogWarning(
                "Cloudflare Turnstile validation failed for IP {ClientIp}: {Errors}",
                clientIp, errorCodes);
            
            return (false, $"Turnstile validation failed: {errorCodes}");
        }
        catch (TaskCanceledException ex)
        {
            logger?.LogError(ex, "Cloudflare Turnstile validation timed out");
            return (false, "Cloudflare challenge validation timed out");
        }
        catch (HttpRequestException ex)
        {
            logger?.LogError(ex, "Network error validating Cloudflare Turnstile");
            return (false, $"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Unexpected error validating Cloudflare Turnstile");
            // Network error or Cloudflare API failure - fail-closed for security
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
        [FromServices] IRepository repo,
        [FromServices] PasskeyService passkeyService)
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
        [FromServices] IRepository repo,
        [FromServices] PasskeyService passkeyService)
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
        [FromServices] IRepository repo,
        [FromServices] PasskeyService passkeyService)
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
        [FromServices] IRepository repo,
        [FromServices] PasskeyService passkeyService,
        [FromServices] IConfiguration config,
        [FromServices] IHttpClientFactory httpClientFactory,
        [FromServices] AuthService authService)
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
        var token = authService.GenerateToken(user);
        
        return Results.Ok(new LoginResponse(token, user.username, user.role));
    }

    /// <summary>
    /// Get all passkeys for the current user.
    /// </summary>
    private static async Task<IResult> GetUserPasskeys(
        HttpContext context,
        [FromServices] IRepository repo)
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
        [FromServices] IRepository repo)
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
        [FromServices] IRepository repo)
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

    /// <summary>
    /// Extracts the origin (scheme + host) from the HTTP request for WebAuthn validation.
    /// </summary>
    /// <param name="context">The current HTTP context containing request information.</param>
    /// <returns>
    /// Origin string in format "scheme://host" (e.g., "https://example.com").
    /// </returns>
    /// <remarks>
    /// <para><strong>WebAuthn Requirement:</strong></para>
    /// WebAuthn/Passkey authentication requires validating that the credential was
    /// created and used on the same origin. This method constructs the expected origin
    /// from the current request.
    /// 
    /// <para><strong>Security:</strong></para>
    /// Origin validation prevents credentials from being used on phishing sites.
    /// The origin must match exactly between registration and authentication.
    /// </remarks>
    private static string GetOriginFromRequest(HttpContext context)
    {
        var scheme = context.Request.Scheme; // http or https
        var host = context.Request.Host.ToString(); // domain:port
        return $"{scheme}://{host}";
    }

    #endregion

    #region Internal Data Structures

    /// <summary>
    /// Tracks failed login attempts for rate limiting purposes.
    /// </summary>
    /// <remarks>
    /// Used in conjunction with ConcurrentDictionary to maintain thread-safe
    /// counters for username-based and IP-based rate limiting.
    /// </remarks>
    private class LoginAttemptTracker
    {
        /// <summary>Number of failed login attempts.</summary>
        public int AttemptCount { get; set; }
        
        /// <summary>Timestamp of the most recent failed attempt (for lockout expiration).</summary>
        public DateTime LastAttempt { get; set; }
    }

    /// <summary>
    /// Response structure from Cloudflare Turnstile siteverify API.
    /// </summary>
    /// <remarks>
    /// Represents the JSON response from:
    /// https://challenges.cloudflare.com/turnstile/v0/siteverify
    /// </remarks>
    private class TurnstileResponse
    {
        /// <summary>True if the challenge was successfully validated.</summary>
        public bool success { get; set; }
        
        /// <summary>Array of error codes if validation failed.</summary>
        public string[]? error_codes { get; set; }
        
        /// <summary>Timestamp when the challenge was solved.</summary>
        public string? challenge_ts { get; set; }
        
        /// <summary>Hostname where the challenge was completed.</summary>
        public string? hostname { get; set; }
    }
    
    /// <summary>
    /// JSON serialization context for Turnstile response deserialization.
    /// </summary>
    /// <remarks>
    /// Uses System.Text.Json source generators for improved performance.
    /// </remarks>
    [System.Text.Json.Serialization.JsonSerializable(typeof(TurnstileResponse))]
    private partial class TurnstileJsonContext : System.Text.Json.Serialization.JsonSerializerContext
    {
    }

    #endregion

    #region Helper Methods - User Utilities

    /// <summary>
    /// Determines if a user is the first administrator account created in the system.
    /// </summary>
    /// <param name="user">The user to check.</param>
    /// <param name="repo">Repository instance for querying all admin users.</param>
    /// <returns>
    /// True if this user is the first admin (earliest created_at), false otherwise.
    /// </returns>
    /// <remarks>
    /// <para><strong>Purpose:</strong></para>
    /// The first admin account may have special privileges or protections in the UI,
    /// such as prevention of accidental deletion or role changes.
    /// 
    /// <para><strong>Logic:</strong></para>
    /// <list type="number">
    ///   <item><description>Returns false immediately if user is not an Admin</description></item>
    ///   <item><description>Retrieves all admin users from repository</description></item>
    ///   <item><description>Orders by created_at timestamp (ascending)</description></item>
    ///   <item><description>Compares user ID with first admin's ID</description></item>
    /// </list>
    /// 
    /// <para><strong>Performance Note:</strong></para>
    /// This method queries all admin users. In systems with many admins, consider caching
    /// or using a more efficient query.
    /// </remarks>
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

    #endregion
}
