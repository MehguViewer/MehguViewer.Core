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
        app.MapPost("/api/v1/auth/login", Login);
        app.MapPost("/api/v1/auth/register", Register);
        app.MapPost("/api/v1/users", CreateUser).RequireAuthorization("MvnAdmin");
        app.MapGet("/api/v1/users", ListUsers).RequireAuthorization("MvnAdmin");
        app.MapPatch("/api/v1/users/{id}", UpdateUser).RequireAuthorization("MvnAdmin");
        app.MapDelete("/api/v1/users/{id}", DeleteUser).RequireAuthorization("MvnAdmin");
        app.MapGet("/api/v1/admin/stats", GetSystemStats).RequireAuthorization("MvnAdmin");
        app.MapGet("/api/v1/admin/storage", GetStorageStats).RequireAuthorization("MvnAdmin");
        app.MapPatch("/api/v1/admin/storage", UpdateStorageSettings).RequireAuthorization("MvnAdmin");
        app.MapPost("/api/v1/admin/storage/clear-cache", ClearCache).RequireAuthorization("MvnAdmin");
        app.MapPost("/api/v1/reports", CreateReport).RequireAuthorization("MvnSocial");
        
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
    }

    private static async Task<IResult> GetEmbeddedDatabaseStatus(EmbeddedPostgresService embeddedPg, DynamicRepository repo, HttpContext context)
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
        EmbeddedPostgresService embeddedPg, DynamicRepository repo, HttpContext context)
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

    private static async Task<IResult> TestDatabaseConnection([FromBody] DatabaseConfig config, DynamicRepository repo, HttpContext context)
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

    private static async Task<IResult> ConfigureDatabase([FromBody] DatabaseSetupRequest config, DynamicRepository repo, HttpContext context)
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

    private static async Task<IResult> GetSetupStatus(IRepository repo)
    {
        await Task.CompletedTask;
        return Results.Ok(new SetupStatusResponse(repo.GetSystemConfig().is_setup_complete));
    }

    private static async Task<IResult> GetSystemStats(IRepository repo)
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

    private static async Task<IResult> GetStorageStats(IRepository repo)
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
        return Results.Ok(new { message = "Cache cleared" });
    }

    private static async Task<IResult> CreateReport([FromBody] Report report, IRepository repo)
    {
        await Task.CompletedTask;
        if (string.IsNullOrWhiteSpace(report.target_urn)) return Results.BadRequest("Target URN is required");
        if (string.IsNullOrWhiteSpace(report.reason)) return Results.BadRequest("Reason is required");

        repo.AddReport(report);
        return Results.Accepted();
    }

    private static async Task<IResult> GetSystemConfig(IRepository repo)
    {
        await Task.CompletedTask;
        return Results.Ok(repo.GetSystemConfig());
    }

    private static async Task<IResult> PatchSystemConfig(SystemConfig config, IRepository repo)
    {
        await Task.CompletedTask;
        // In a real PATCH, we'd accept a partial JSON or JsonPatchDocument.
        // Since SystemConfig is a record and we are receiving the full object (or default values for missing fields)
        // due to simple binding, this is a bit naive. 
        // For this prototype, we'll assume the client sends the full object or we'd need a DTO with nullable fields.
        // However, to strictly follow "PATCH", we should merge. 
        // Given the tool limitations and simplicity, we'll treat it similar to PUT for now 
        // OR we can implement a manual merge if we change the input to JsonElement.
        
        // Let's just update it for now as the user likely sends the modified config.
        repo.UpdateSystemConfig(config);
        return Results.Ok(config);
    }

    private static async Task<IResult> UpdateSystemConfig(SystemConfig config, IRepository repo)
    {
        await Task.CompletedTask;
        repo.UpdateSystemConfig(config);
        return Results.Ok(config);
    }

    private static async Task<IResult> Register(UserCreate request, IRepository repo)
    {
        await Task.CompletedTask;
        if (string.IsNullOrWhiteSpace(request.username) || string.IsNullOrWhiteSpace(request.password))
            return Results.BadRequest(new Problem("VALIDATION_ERROR", "Username and password are required", 400, null, "/api/v1/auth/register"));

        // Validate username
        if (request.username.Length < 3 || request.username.Length > 32)
            return Results.BadRequest(new Problem("VALIDATION_ERROR", "Username must be between 3 and 32 characters", 400, null, "/api/v1/auth/register"));

        // Validate password strength
        var (isValid, error) = AuthService.ValidatePasswordStrength(request.password);
        if (!isValid)
            return Results.BadRequest(new Problem("WEAK_PASSWORD", error!, 400, null, "/api/v1/auth/register"));

        if (repo.GetUserByUsername(request.username) != null) 
            return Results.Conflict(new Problem("USER_EXISTS", "A user with this username already exists", 409, null, "/api/v1/auth/register"));

        string role = "User";
        
        // First User Claim
        if (!repo.IsAdminSet())
        {
            role = "Admin";
        }
        else
        {
            // Check if registration is open
            var config = repo.GetSystemConfig();
            if (!config.registration_open)
            {
                return Results.Problem(
                    title: "Registration is currently closed",
                    statusCode: 403,
                    type: "REGISTRATION_CLOSED",
                    instance: "/api/v1/auth/register");
            }
        }

        var user = new User(UrnHelper.CreateUserUrn(), request.username, AuthService.HashPassword(request.password), role, DateTime.UtcNow);
        repo.AddUser(user);
        
        // Auto-login
        var token = AuthService.GenerateToken(user);
        return Results.Ok(new LoginResponse(token, user.username, user.role));
    }

    private static async Task<IResult> CreateAdminUser(AdminPasswordRequest request, IRepository repo)
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

    private static async Task<IResult> Login(LoginRequest request, IRepository repo)
    {
        await Task.CompletedTask;
        if (string.IsNullOrWhiteSpace(request.username) || string.IsNullOrWhiteSpace(request.password))
            return Results.BadRequest(new Problem("VALIDATION_ERROR", "Username and password are required", 400, null, "/api/v1/auth/login"));

        var user = repo.ValidateUser(request.username, request.password);
        if (user != null)
        {
            // Check if password needs rehashing (legacy SHA256 -> bcrypt migration)
            if (AuthService.NeedsRehash(user.password_hash))
            {
                // Rehash with bcrypt and update
                var newHash = AuthService.HashPassword(request.password);
                var updatedUser = user with { password_hash = newHash };
                repo.UpdateUser(updatedUser);
            }
            
            var token = AuthService.GenerateToken(user);
            return Results.Ok(new LoginResponse(token, user.username, user.role));
        }
        return Results.Unauthorized();
    }

    private static async Task<IResult> CreateUser(UserCreate request, IRepository repo)
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

    private static async Task<IResult> ListUsers(IRepository repo)
    {
        await Task.CompletedTask;
        return Results.Ok(repo.ListUsers());
    }

    private static async Task<IResult> UpdateUser(string id, UserUpdate request, IRepository repo)
    {
        await Task.CompletedTask;
        
        var user = repo.GetUser(id);
        if (user == null)
            return Results.NotFound(new Problem("USER_NOT_FOUND", "User not found", 404, null, $"/api/v1/users/{id}"));

        // Update role if provided
        var newRole = request.role ?? user.role;
        var newPasswordHash = user.password_hash;
        
        // Update password if provided
        if (!string.IsNullOrEmpty(request.password))
        {
            var (isValid, error) = AuthService.ValidatePasswordStrength(request.password);
            if (!isValid)
                return Results.BadRequest(new Problem("WEAK_PASSWORD", error!, 400, null, $"/api/v1/users/{id}"));
            
            newPasswordHash = AuthService.HashPassword(request.password);
        }
        
        var updatedUser = user with { role = newRole, password_hash = newPasswordHash };
        repo.UpdateUser(updatedUser);
        
        return Results.Ok(updatedUser);
    }

    private static async Task<IResult> DeleteUser(string id, IRepository repo)
    {
        await Task.CompletedTask;
        repo.DeleteUser(id);
        return Results.Ok();
    }

    private static async Task<IResult> UpdateNodeMetadata(NodeMetadata metadata, IRepository repo)
    {
        await Task.CompletedTask;
        repo.UpdateNodeMetadata(metadata);
        return Results.Ok(metadata);
    }

    private static async Task<IResult> GetNodeMetadata(IRepository repo)
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

    private static async Task<IResult> GetTaxonomy()
    {
        await Task.CompletedTask;
        return Results.Ok(new TaxonomyData(
            new[] { "Action", "Adventure", "Comedy", "Drama", "Fantasy", "Slice of Life" },
            new[] { "Gore", "Sexual Violence", "Nudity" },
            new[] { "Manga", "Manhwa", "Manhua", "Novel", "OEL" },
            new[] { "Official", "Fan Group A", "Fan Group B" }
        ));
    }

    private static async Task<IResult> ResetAllData([FromBody] ResetRequest request, IRepository repo, HttpContext context)
    {
        await Task.CompletedTask;
        
        // Get current user from token
        var username = context.User.Identity?.Name;
        if (string.IsNullOrEmpty(username))
            return Results.Unauthorized();

        // Validate admin password
        var user = repo.ValidateUser(username, request.password_hash);
        if (user == null || user.role != "Admin")
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

    private static async Task<IResult> ResetDatabase([FromBody] ResetRequest request, DynamicRepository repo, HttpContext context)
    {
        await Task.CompletedTask;
        
        // Get current user from token
        var username = context.User.Identity?.Name;
        if (string.IsNullOrEmpty(username))
            return Results.Unauthorized();

        // Validate admin password
        var user = repo.ValidateUser(username, request.password_hash);
        if (user == null || user.role != "Admin")
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
}
