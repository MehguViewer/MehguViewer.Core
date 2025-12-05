using MehguViewer.Shared.Models;
using MehguViewer.Core.Backend.Services;
using Microsoft.AspNetCore.Mvc;

namespace MehguViewer.Core.Backend.Endpoints;

public static class SystemEndpoints
{
    public static void MapSystemEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/.well-known/mehgu-node", GetNodeMetadata);
        app.MapGet("/api/v1/instance", GetInstance);
        app.MapGet("/api/v1/taxonomy", GetTaxonomy);
        app.MapGet("/api/v1/system/setup-status", GetSetupStatus);
        app.MapGet("/api/v1/system/config", GetSystemConfig).RequireAuthorization("MvnRead");
        app.MapGet("/api/v1/admin/configuration", GetSystemConfig).RequireAuthorization("MvnAdmin");
        app.MapPatch("/api/v1/admin/configuration", PatchSystemConfig).RequireAuthorization("MvnAdmin");
        app.MapPut("/api/v1/system/config", UpdateSystemConfig).RequireAuthorization("MvnAdmin");
        app.MapPut("/api/v1/system/metadata", UpdateNodeMetadata).RequireAuthorization("MvnAdmin");
        app.MapPost("/api/v1/system/admin/password", CreateAdminUser);
        // Note: /api/v1/auth/login and /api/v1/auth/register are now in AuthEndpoints.cs
        app.MapPost("/api/v1/users", CreateUser).RequireAuthorization("MvnAdmin");
        app.MapGet("/api/v1/users", ListUsers).RequireAuthorization("MvnAdmin");
        app.MapPatch("/api/v1/users/{id}", UpdateUser).RequireAuthorization("MvnAdmin");
        app.MapDelete("/api/v1/users/{id}", DeleteUser).RequireAuthorization("MvnAdmin");
        app.MapGet("/api/v1/admin/stats", GetSystemStats).RequireAuthorization("MvnAdmin");
        app.MapGet("/api/v1/admin/storage", GetStorageStats).RequireAuthorization("MvnAdmin");
        app.MapPatch("/api/v1/admin/storage", UpdateStorageSettings).RequireAuthorization("MvnAdmin");
        app.MapPost("/api/v1/admin/storage/clear-cache", ClearCache).RequireAuthorization("MvnAdmin");
        app.MapPost("/api/v1/reports", CreateReport).RequireAuthorization("MvnSocial");
        
        // Logs endpoints
        app.MapGet("/api/v1/admin/logs", GetLogs).RequireAuthorization("MvnAdmin");
        app.MapDelete("/api/v1/admin/logs", ClearLogs).RequireAuthorization("MvnAdmin");
        
        // Database configuration endpoints - only during setup OR with admin auth
        app.MapPost("/api/v1/system/database/test", TestDatabaseConnection);
        app.MapPost("/api/v1/system/database/configure", ConfigureDatabase);
        app.MapGet("/api/v1/system/database/embedded-status", GetEmbeddedDatabaseStatus);
        app.MapPost("/api/v1/system/database/use-embedded", UseEmbeddedDatabase);
        
        // Admin-only database reconfiguration (after setup is complete)
        app.MapPost("/api/v1/admin/database/test", TestDatabaseConnection).RequireAuthorization("MvnAdmin");
        app.MapPost("/api/v1/admin/database/configure", ConfigureDatabase).RequireAuthorization("MvnAdmin");
        app.MapGet("/api/v1/admin/database/embedded-status", GetEmbeddedDatabaseStatus).RequireAuthorization("MvnAdmin");
        app.MapPost("/api/v1/admin/database/use-embedded", UseEmbeddedDatabase).RequireAuthorization("MvnAdmin");
        
        app.MapPost("/api/v1/admin/reset-data", ResetAllData).RequireAuthorization("MvnAdmin");
        app.MapPost("/api/v1/admin/reset-database", ResetDatabase).RequireAuthorization("MvnAdmin");
        
        // Taxonomy configuration endpoints
        app.MapGet("/api/v1/admin/taxonomy", GetTaxonomyConfig).RequireAuthorization("MvnAdmin");
        app.MapPut("/api/v1/admin/taxonomy", UpdateTaxonomyConfig).RequireAuthorization("MvnAdmin");
        app.MapPatch("/api/v1/admin/taxonomy", PatchTaxonomyConfig).RequireAuthorization("MvnAdmin");
        
        // Export/Import endpoints
        app.MapGet("/api/v1/admin/export/series", ExportAllSeries).RequireAuthorization("MvnAdmin");
        app.MapPost("/api/v1/admin/export/series-to-files", ExportSeriesToFiles).RequireAuthorization("MvnAdmin");
    }
    
    private static async Task<IResult> ExportAllSeries([FromServices] IRepository repo)
    {
        await Task.CompletedTask;
        var series = repo.ListSeries().ToArray();
        return Results.Ok(new ExportResponse(series.Length, series));
    }
    
    private static async Task<IResult> ExportSeriesToFiles([FromServices] FileBasedSeriesService fileService, [FromServices] IRepository repo)
    {
        var series = repo.ListSeries().ToList();
        var savedCount = 0;
        var errors = new List<string>();
        
        foreach (var s in series)
        {
            try
            {
                await fileService.SaveSeriesAsync(s);
                savedCount++;
            }
            catch (Exception ex)
            {
                errors.Add($"{s.id}: {ex.Message}");
            }
        }
        
        return Results.Ok(new ExportToFilesResponse(savedCount, series.Count, errors.ToArray()));
    }

    private static async Task<IResult> GetLogs([FromQuery] int count, [FromQuery] string? level, [FromServices] LogsService logsService)
    {
        await Task.CompletedTask;
        var logs = logsService.GetLogs(count > 0 ? count : 100, level);
        return Results.Ok(new LogsResponse(logs.ToArray(), logsService.GetLogCount()));
    }

    private static async Task<IResult> ClearLogs([FromServices] LogsService logsService)
    {
        await Task.CompletedTask;
        logsService.Clear();
        return Results.Ok(new ClearCacheResponse("Logs cleared"));
    }

    private static async Task<IResult> GetEmbeddedDatabaseStatus([FromServices] EmbeddedPostgresService embeddedPg, [FromServices] DynamicRepository repo, HttpContext context)
    {
        await Task.CompletedTask;
        
        // If setup is complete and user is not authenticated as admin, reject
        if (repo.GetSystemConfig().is_setup_complete && !context.User.IsInRole("Admin"))
        {
            return Results.Unauthorized();
        }
        
        // Check if the embedded database has existing data
        bool hasData = false;
        if (embeddedPg.IsRunning && !string.IsNullOrEmpty(embeddedPg.ConnectionString))
        {
            hasData = repo.TestConnection(embeddedPg.ConnectionString);
        }
        
        var status = new EmbeddedDatabaseStatus(
            available: embeddedPg.EmbeddedModeEnabled || !embeddedPg.StartupFailed,
            running: embeddedPg.IsRunning,
            enabled: embeddedPg.EmbeddedModeEnabled,
            port: embeddedPg.Port,
            has_data: hasData,
            version: "15.3.0",
            data_directory: embeddedPg.IsRunning ? "pg_data" : null,
            error_message: embeddedPg.StartupFailed ? "Embedded PostgreSQL failed to start" : null
        );
        return Results.Ok(status);
    }

    private static async Task<IResult> UseEmbeddedDatabase([FromBody] UseEmbeddedDatabaseRequest request, 
        [FromServices] EmbeddedPostgresService embeddedPg, [FromServices] DynamicRepository repo, HttpContext context)
    {
        await Task.CompletedTask;
        
        // If setup is complete and user is not authenticated as admin, reject
        if (repo.GetSystemConfig().is_setup_complete && !context.User.IsInRole("Admin"))
        {
            return Results.Unauthorized();
        }

        // Check if embedded is available and running
        if (embeddedPg.StartupFailed || !embeddedPg.IsRunning)
        {
            return Results.BadRequest(new Problem(
                "EMBEDDED_DB_UNAVAILABLE", 
                "Embedded PostgreSQL is not available", 
                400, 
                embeddedPg.StartupFailed ? "Embedded PostgreSQL failed to start" : "Embedded PostgreSQL is not running",
                "/api/v1/system/database/use-embedded"));
        }

        try
        {
            // Initialize the repository with embedded connection, optionally resetting data
            await repo.InitializeAsync(request.reset_data);
            return Results.Ok(new UseEmbeddedDatabaseResponse(
                request.reset_data ? "Using embedded PostgreSQL (data reset)" : "Using embedded PostgreSQL", 
                embeddedPg.ConnectionString));
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new Problem(
                "EMBEDDED_DB_INIT_FAILED", 
                "Failed to initialize embedded database", 
                400, 
                ex.Message,
                "/api/v1/system/database/use-embedded"));
        }
    }

    private static async Task<IResult> TestDatabaseConnection([FromBody] DatabaseConfig config, [FromServices] DynamicRepository repo, HttpContext context)
    {
        await Task.CompletedTask;
        
        // If setup is complete and user is not authenticated as admin, reject
        if (repo.GetSystemConfig().is_setup_complete && !context.User.IsInRole("Admin"))
        {
            return Results.Unauthorized();
        }
        
        var connString = $"Host={config.host};Port={config.port};Database={config.database};Username={config.username};Password={config.password}";
        try
        {
            bool hasData = repo.TestConnection(connString);
            return Results.Ok(new DatabaseTestResponse(hasData));
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new Problem("DB_CONNECTION_FAILED", "Failed to connect to database", 400, ex.Message, "/api/v1/system/database/test"));
        }
    }

    private static async Task<IResult> ConfigureDatabase([FromBody] DatabaseSetupRequest config, [FromServices] DynamicRepository repo, HttpContext context)
    {
        await Task.CompletedTask;
        
        // If setup is complete and user is not authenticated as admin, reject
        if (repo.GetSystemConfig().is_setup_complete && !context.User.IsInRole("Admin"))
        {
            return Results.Unauthorized();
        }
        
        var connString = $"Host={config.host};Port={config.port};Database={config.database};Username={config.username};Password={config.password}";
        try
        {
            repo.SwitchToPostgres(connString, config.reset);
            return Results.Ok();
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new Problem("DB_CONFIG_FAILED", "Failed to configure database", 400, ex.Message, "/api/v1/system/database/configure"));
        }
    }

    private static async Task<IResult> GetSetupStatus([FromServices] IRepository repo)
    {
        await Task.CompletedTask;
        return Results.Ok(new SetupStatusResponse(repo.GetSystemConfig().is_setup_complete));
    }

    private static async Task<IResult> GetSystemStats([FromServices] IRepository repo)
    {
        await Task.CompletedTask;
        return Results.Ok(repo.GetSystemStats());
    }

    // Storage settings (in-memory for now, could be persisted)
    private static int _thumbnailSize = 200;
    private static int _webSize = 1200;
    private static int _jpegQuality = 85;
    private static string _storagePath = "./storage";
    private static long _cacheBytes = 0;

    private static async Task<IResult> GetStorageStats([FromServices] IRepository repo)
    {
        await Task.CompletedTask;
        
        // Get asset count from series (approximate)
        var series = repo.ListSeries();
        var assetCount = series.Count() * 10; // Estimate 10 assets per series
        
        return Results.Ok(new StorageStatsResponse(assetCount, _cacheBytes, _storagePath, _thumbnailSize, _webSize, _jpegQuality));
    }

    private static async Task<IResult> UpdateStorageSettings(StorageSettingsUpdate request)
    {
        await Task.CompletedTask;
        
        if (request.thumbnail_size.HasValue && request.thumbnail_size >= 100 && request.thumbnail_size <= 500)
            _thumbnailSize = request.thumbnail_size.Value;
        if (request.web_size.HasValue && request.web_size >= 800 && request.web_size <= 2000)
            _webSize = request.web_size.Value;
        if (request.jpeg_quality.HasValue && request.jpeg_quality >= 50 && request.jpeg_quality <= 100)
            _jpegQuality = request.jpeg_quality.Value;
        
        return Results.Ok(new StorageStatsResponse(0, _cacheBytes, _storagePath, _thumbnailSize, _webSize, _jpegQuality));
    }

    private static async Task<IResult> ClearCache()
    {
        await Task.CompletedTask;
        _cacheBytes = 0;
        return Results.Ok(new ClearCacheResponse("Cache cleared"));
    }

    private static async Task<IResult> CreateReport([FromBody] Report report, [FromServices] IRepository repo)
    {
        await Task.CompletedTask;
        if (string.IsNullOrWhiteSpace(report.target_urn)) return Results.BadRequest("Target URN is required");
        if (string.IsNullOrWhiteSpace(report.reason)) return Results.BadRequest("Reason is required");

        repo.AddReport(report);
        return Results.Accepted();
    }

    private static async Task<IResult> GetSystemConfig([FromServices] IRepository repo)
    {
        await Task.CompletedTask;
        return Results.Ok(repo.GetSystemConfig());
    }

    private static async Task<IResult> PatchSystemConfig(SystemConfigUpdate update, [FromServices] IRepository repo)
    {
        await Task.CompletedTask;
        
        // Get current config from database
        var current = repo.GetSystemConfig();
        
        // Merge: only update fields that are explicitly provided (not null)
        var merged = new SystemConfig(
            is_setup_complete: update.is_setup_complete ?? current.is_setup_complete,
            registration_open: update.registration_open ?? current.registration_open,
            maintenance_mode: update.maintenance_mode ?? current.maintenance_mode,
            motd_message: update.motd_message ?? current.motd_message,
            default_language_filter: update.default_language_filter ?? current.default_language_filter,
            max_login_attempts: update.max_login_attempts ?? current.max_login_attempts,
            lockout_duration_minutes: update.lockout_duration_minutes ?? current.lockout_duration_minutes,
            token_expiry_hours: update.token_expiry_hours ?? current.token_expiry_hours,
            cloudflare_enabled: update.cloudflare_enabled ?? current.cloudflare_enabled,
            cloudflare_site_key: update.cloudflare_site_key ?? current.cloudflare_site_key,
            cloudflare_secret_key: update.cloudflare_secret_key ?? current.cloudflare_secret_key,
            require_2fa_passkey: update.require_2fa_passkey ?? current.require_2fa_passkey,
            require_password_for_danger_zone: update.require_password_for_danger_zone ?? current.require_password_for_danger_zone
        );
        
        repo.UpdateSystemConfig(merged);
        return Results.Ok(merged);
    }

    private static async Task<IResult> UpdateSystemConfig(SystemConfig config, [FromServices] IRepository repo)
    {
        await Task.CompletedTask;
        repo.UpdateSystemConfig(config);
        return Results.Ok(config);
    }

    private static async Task<IResult> CreateAdminUser(AdminPasswordRequest request, [FromServices] IRepository repo)
    {
        await Task.CompletedTask;
        if (string.IsNullOrWhiteSpace(request.password)) 
            return Results.BadRequest(new Problem("VALIDATION_ERROR", "Password is required", 400, null, "/api/v1/system/admin/password"));

        // Validate password strength
        var (isValid, error) = AuthService.ValidatePasswordStrength(request.password);
        if (!isValid)
            return Results.BadRequest(new Problem("WEAK_PASSWORD", error!, 400, null, "/api/v1/system/admin/password"));

        if (repo.IsAdminSet()) 
            return Results.Conflict(new Problem("ADMIN_EXISTS", "Admin user already exists", 409, null, "/api/v1/system/admin/password"));
        
        var user = new User(UrnHelper.CreateUserUrn(), "admin", AuthService.HashPassword(request.password), "Admin", DateTime.UtcNow);
        repo.AddUser(user);
        return Results.Ok();
    }

    private static async Task<IResult> CreateUser(UserCreate request, [FromServices] IRepository repo)
    {
        await Task.CompletedTask;
        if (string.IsNullOrWhiteSpace(request.username) || string.IsNullOrWhiteSpace(request.password))
            return Results.BadRequest(new Problem("VALIDATION_ERROR", "Username and password are required", 400, null, "/api/v1/users"));

        // Validate password strength
        var (isValid, error) = AuthService.ValidatePasswordStrength(request.password);
        if (!isValid)
            return Results.BadRequest(new Problem("WEAK_PASSWORD", error!, 400, null, "/api/v1/users"));

        if (repo.GetUserByUsername(request.username) != null) 
            return Results.Conflict(new Problem("USER_EXISTS", "A user with this username already exists", 409, null, "/api/v1/users"));
        
        var user = new User(UrnHelper.CreateUserUrn(), request.username, AuthService.HashPassword(request.password), request.role, DateTime.UtcNow);
        repo.AddUser(user);
        return Results.Ok(user);
    }

    private static async Task<IResult> ListUsers([FromServices] IRepository repo)
    {
        await Task.CompletedTask;
        return Results.Ok(repo.ListUsers());
    }

    private static async Task<IResult> UpdateUser(string id, UserUpdate request, [FromServices] IRepository repo, HttpContext context)
    {
        await Task.CompletedTask;
        
        var user = repo.GetUser(id);
        if (user == null)
            return Results.NotFound(new Problem("USER_NOT_FOUND", "User not found", 404, null, $"/api/v1/users/{id}"));

        // Check if trying to modify the first admin
        var currentUserId = context.User.FindFirst("sub")?.Value 
                           ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        
        if (user.role == "Admin" && IsFirstAdmin(user, repo) && currentUserId != user.id)
        {
            return Results.Json(new Problem(
                "urn:mvn:error:first-admin-protected",
                "The first admin account cannot be modified by other administrators.",
                403,
                "First Admin Protected",
                $"/api/v1/users/{id}"
            ), AppJsonSerializerContext.Default.Problem, statusCode: 403);
        }

        // Update role if provided
        var newRole = request.role ?? user.role;
        var newPasswordHash = user.password_hash;
        var newPasswordLoginDisabled = request.password_login_disabled ?? user.password_login_disabled;
        
        // Update password if provided
        if (!string.IsNullOrEmpty(request.password))
        {
            var (isValid, error) = AuthService.ValidatePasswordStrength(request.password);
            if (!isValid)
                return Results.BadRequest(new Problem("WEAK_PASSWORD", error!, 400, null, $"/api/v1/users/{id}"));
            
            newPasswordHash = AuthService.HashPassword(request.password);
        }
        
        var updatedUser = user with { 
            role = newRole, 
            password_hash = newPasswordHash,
            password_login_disabled = newPasswordLoginDisabled
        };
        repo.UpdateUser(updatedUser);
        
        return Results.Ok(updatedUser);
    }

    private static async Task<IResult> DeleteUser(string id, [FromServices] IRepository repo, HttpContext context)
    {
        await Task.CompletedTask;
        
        var user = repo.GetUser(id);
        if (user == null)
            return Results.NotFound(new Problem("USER_NOT_FOUND", "User not found", 404, null, $"/api/v1/users/{id}"));
        
        // Check if trying to delete the first admin
        var currentUserId = context.User.FindFirst("sub")?.Value 
                           ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        
        if (user.role == "Admin" && IsFirstAdmin(user, repo) && currentUserId != user.id)
        {
            return Results.Json(new Problem(
                "urn:mvn:error:first-admin-protected",
                "The first admin account cannot be deleted by other administrators.",
                403,
                "First Admin Protected",
                $"/api/v1/users/{id}"
            ), AppJsonSerializerContext.Default.Problem, statusCode: 403);
        }
        
        repo.DeleteUser(id);
        return Results.Ok();
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

    private static async Task<IResult> UpdateNodeMetadata(NodeMetadata metadata, [FromServices] IRepository repo)
    {
        await Task.CompletedTask;
        repo.UpdateNodeMetadata(metadata);
        return Results.Ok(metadata);
    }

    private static async Task<IResult> GetNodeMetadata([FromServices] IRepository repo)
    {
        await Task.CompletedTask;
        return Results.Ok(repo.GetNodeMetadata());
    }

    private static async Task<IResult> GetInstance()
    {
        await Task.CompletedTask;
        return Results.Ok(new NodeManifest(
            "urn:mvn:node:example",
            "MehguViewer Core",
            "A MehguViewer Core Node",
            "1.0.0",
            "MehguViewer.Core (NativeAOT)",
            "admin@example.com",
            false,
            new NodeFeatures(true, false),
            null
        ));
    }

    private static async Task<IResult> GetTaxonomy([FromServices] IRepository repo)
    {
        await Task.CompletedTask;
        
        // Auto-generate taxonomy from public series data
        var allSeries = repo.ListSeries();
        
        // Aggregate tags from all series
        var aggregatedTags = allSeries
            .SelectMany(s => s.tags)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t)
            .ToArray();
        
        // Aggregate authors from all series - deduplicate by name (case-insensitive), keep first ID
        var aggregatedAuthors = allSeries
            .SelectMany(s => s.authors)
            .GroupBy(a => a.name.ToLowerInvariant())
            .Select(g => g.First())
            .OrderBy(a => a.name)
            .ToArray();
        
        // Aggregate scanlators from all series - deduplicate by name (case-insensitive), keep first ID
        // Also include scanlators from localized metadata
        var seriesScanlators = allSeries.SelectMany(s => s.scanlators);
        var localizedScanlators = allSeries
            .Where(s => s.localized != null)
            .SelectMany(s => s.localized!.Values)
            .Where(lm => lm.scanlators != null)
            .SelectMany(lm => lm.scanlators!);
        
        var aggregatedScanlators = seriesScanlators
            .Concat(localizedScanlators)
            .GroupBy(s => s.name.ToLowerInvariant())
            .Select(g => g.First())
            .OrderBy(s => s.name)
            .ToArray();
        
        // Aggregate groups from all series - deduplicate by name (case-insensitive), keep first ID
        var aggregatedGroups = allSeries
            .Where(s => s.groups != null)
            .SelectMany(s => s.groups!)
            .GroupBy(g => g.name.ToLowerInvariant())
            .Select(g => g.First())
            .OrderBy(g => g.name)
            .ToArray();
        
        // Get stored config for fallback
        var config = repo.GetTaxonomyConfig();
        
        return Results.Ok(new TaxonomyData(
            tags: aggregatedTags.Length > 0 ? aggregatedTags : config.tags,
            content_warnings: ContentWarnings.All, // Always return all possible content warnings
            types: MediaTypes.All, // Fixed media types
            authors: aggregatedAuthors.Length > 0 ? aggregatedAuthors : config.authors,
            scanlators: aggregatedScanlators.Length > 0 ? aggregatedScanlators : config.scanlators,
            groups: aggregatedGroups.Length > 0 ? aggregatedGroups : config.groups
        ));
    }

    private static async Task<IResult> GetTaxonomyConfig([FromServices] IRepository repo)
    {
        await Task.CompletedTask;
        return Results.Ok(repo.GetTaxonomyConfig());
    }

    private static async Task<IResult> UpdateTaxonomyConfig([FromBody] TaxonomyConfig config, [FromServices] IRepository repo)
    {
        await Task.CompletedTask;
        repo.UpdateTaxonomyConfig(config);
        return Results.Ok(config);
    }

    private static async Task<IResult> PatchTaxonomyConfig([FromBody] TaxonomyConfigUpdate update, [FromServices] IRepository repo)
    {
        await Task.CompletedTask;
        
        // Get current config from database
        var current = repo.GetTaxonomyConfig();
        
        // Merge: only update fields that are explicitly provided (not null)
        var merged = new TaxonomyConfig(
            tags: update.tags ?? current.tags,
            content_warnings: update.content_warnings ?? current.content_warnings,
            types: update.types ?? current.types,
            authors: update.authors ?? current.authors,
            scanlators: update.scanlators ?? current.scanlators,
            groups: update.groups ?? current.groups
        );
        
        repo.UpdateTaxonomyConfig(merged);
        return Results.Ok(merged);
    }

    private static async Task<IResult> ResetAllData([FromBody] ResetRequest request, [FromServices] IRepository repo, HttpContext context, [FromServices] PasskeyService passkeyService)
    {
        await Task.CompletedTask;
        
        // Get current user from token
        var username = context.User.Identity?.Name;
        var userId = context.User.FindFirst("sub")?.Value 
                     ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(userId))
            return Results.Unauthorized();

        // Only first admin can access danger zone
        var currentUser = repo.GetUser(userId);
        if (currentUser == null || currentUser.role != "Admin" || !IsFirstAdmin(currentUser, repo))
        {
            return Results.Json(new Problem(
                "urn:mvn:error:forbidden",
                "Access Denied",
                403,
                "Only the first administrator can perform this action.",
                "/api/v1/admin/reset-data"
            ), AppJsonSerializerContext.Default.Problem, statusCode: 403);
        }

        // Validate authentication - either password or passkey
        bool authenticated = false;
        
        if (request.passkey != null)
        {
            // Validate passkey
            authenticated = await ValidatePasskeyVerification(request.passkey, userId, repo, passkeyService, context);
        }
        else if (!string.IsNullOrEmpty(request.password_hash))
        {
            // Validate password (legacy flow)
            var user = repo.ValidateUser(username, request.password_hash);
            authenticated = user != null && user.role == "Admin";
        }
        
        if (!authenticated)
            return Results.Unauthorized();

        try
        {
            repo.ResetAllData();
            return Results.Ok(new { message = "All data has been reset successfully" });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new Problem("RESET_FAILED", "Failed to reset data", 400, ex.Message, "/api/v1/admin/reset-data"));
        }
    }

    private static async Task<IResult> ResetDatabase([FromBody] ResetRequest request, [FromServices] DynamicRepository repo, HttpContext context, [FromServices] PasskeyService passkeyService)
    {
        await Task.CompletedTask;
        
        // Get current user from token
        var username = context.User.Identity?.Name;
        var userId = context.User.FindFirst("sub")?.Value 
                     ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(userId))
            return Results.Unauthorized();

        // Only first admin can access danger zone
        var currentUser = repo.GetUser(userId);
        if (currentUser == null || currentUser.role != "Admin" || !IsFirstAdmin(currentUser, repo))
        {
            return Results.Json(new Problem(
                "urn:mvn:error:forbidden",
                "Access Denied",
                403,
                "Only the first administrator can perform this action.",
                "/api/v1/admin/reset-database"
            ), AppJsonSerializerContext.Default.Problem, statusCode: 403);
        }

        // Validate authentication - either password or passkey
        bool authenticated = false;
        
        if (request.passkey != null)
        {
            // Validate passkey
            authenticated = await ValidatePasskeyVerification(request.passkey, userId, repo, passkeyService, context);
        }
        else if (!string.IsNullOrEmpty(request.password_hash))
        {
            // Validate password (legacy flow)
            var user = repo.ValidateUser(username, request.password_hash);
            authenticated = user != null && user.role == "Admin";
        }
        
        if (!authenticated)
            return Results.Unauthorized();

        try
        {
            repo.ResetDatabase();
            return Results.Ok(new { message = "Database has been reset successfully" });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new Problem("RESET_FAILED", "Failed to reset database", 400, ex.Message, "/api/v1/admin/reset-database"));
        }
    }

    private static async Task<bool> ValidatePasskeyVerification(
        PasskeyVerificationData passkeyData, 
        string userId, 
        IRepository repo,
        PasskeyService passkeyService,
        HttpContext context)
    {
        await Task.CompletedTask;
        
        try
        {
            // Validate and consume the stored challenge
            var (valid, storedChallenge, storedUserId) = passkeyService.ValidateChallenge(passkeyData.challenge_id);
            if (!valid || storedChallenge == null)
                return false;

            // Get user's passkeys
            var userPasskeys = repo.GetPasskeysByUser(userId);
            var matchingPasskey = userPasskeys.FirstOrDefault(p => 
            {
                var credId = Convert.ToBase64String(Base64UrlDecode(passkeyData.id));
                return p.credential_id == credId || p.credential_id == passkeyData.id;
            });

            if (matchingPasskey == null)
                return false;

            // Construct the authentication request
            var authRequest = new PasskeyAuthenticationRequest(
                id: passkeyData.id,
                raw_id: passkeyData.raw_id,
                response: new PasskeyAuthenticatorAssertionResponse(
                    passkeyData.response.client_data_json,
                    passkeyData.response.authenticator_data,
                    passkeyData.response.signature,
                    passkeyData.response.user_handle
                ),
                type: passkeyData.type
            );

            // Get the expected origin from the request
            var scheme = context.Request.Scheme;
            var host = context.Request.Host.ToString();
            var expectedOrigin = $"{scheme}://{host}";
            
            // Verify the assertion using the PasskeyService
            var (success, verifiedUserId, newSignCount, error) = passkeyService.VerifyAuthentication(
                authRequest,
                matchingPasskey,
                storedChallenge,
                expectedOrigin
            );

            if (success && newSignCount > matchingPasskey.sign_count)
            {
                // Update sign count
                var updatedPasskey = matchingPasskey with { sign_count = newSignCount };
                repo.UpdatePasskey(updatedPasskey);
            }

            return success;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Passkey verification error: {ex.Message}");
            return false;
        }
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var output = input.Replace('-', '+').Replace('_', '/');
        switch (output.Length % 4)
        {
            case 2: output += "=="; break;
            case 3: output += "="; break;
        }
        return Convert.FromBase64String(output);
    }
}
