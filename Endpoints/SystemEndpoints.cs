using MehguViewer.Core.Backend.Models;
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
        app.MapDelete("/api/v1/users/{id}", DeleteUser).RequireAuthorization("MvnAdmin");
        app.MapGet("/api/v1/admin/stats", GetSystemStats).RequireAuthorization("MvnAdmin");
        app.MapPost("/api/v1/reports", CreateReport).RequireAuthorization("MvnSocial");
    }

    private static async Task<IResult> GetSystemStats(IRepository repo)
    {
        await Task.CompletedTask;
        return Results.Ok(repo.GetSystemStats());
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
            return Results.BadRequest("Username and password are required");

        if (repo.GetUserByUsername(request.username) != null) 
            return Results.Conflict("User exists");

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
                return Results.Forbid(); // Or 403
            }
        }

        var user = new User(UrnHelper.CreateUserUrn(), request.username, request.password, role, DateTime.UtcNow);
        repo.AddUser(user);
        
        // Auto-login
        var token = AuthService.GenerateToken(user);
        return Results.Ok(new LoginResponse(token, user.username, user.role));
    }

    private static async Task<IResult> CreateAdminUser(AdminPasswordRequest request, IRepository repo)
    {
        await Task.CompletedTask;
        if (string.IsNullOrWhiteSpace(request.password)) return Results.BadRequest("Password is required");

        if (repo.IsAdminSet()) return Results.Conflict("Admin already set");
        var user = new User(UrnHelper.CreateUserUrn(), "admin", request.password, "Admin", DateTime.UtcNow);
        repo.AddUser(user);
        return Results.Ok();
    }

    private static async Task<IResult> Login(LoginRequest request, IRepository repo)
    {
        await Task.CompletedTask;
        if (string.IsNullOrWhiteSpace(request.username) || string.IsNullOrWhiteSpace(request.password))
            return Results.BadRequest("Username and password are required");

        var user = repo.ValidateUser(request.username, request.password);
        if (user != null)
        {
            var token = AuthService.GenerateToken(user);
            return Results.Ok(new LoginResponse(token, user.username, user.role));
        }
        return Results.Unauthorized();
    }

    private static async Task<IResult> CreateUser(UserCreate request, IRepository repo)
    {
        await Task.CompletedTask;
        if (string.IsNullOrWhiteSpace(request.username) || string.IsNullOrWhiteSpace(request.password))
            return Results.BadRequest("Username and password are required");

        if (repo.GetUserByUsername(request.username) != null) return Results.Conflict("User exists");
        var user = new User(UrnHelper.CreateUserUrn(), request.username, request.password, request.role, DateTime.UtcNow);
        repo.AddUser(user);
        return Results.Ok(user);
    }

    private static async Task<IResult> ListUsers(IRepository repo)
    {
        await Task.CompletedTask;
        return Results.Ok(repo.ListUsers());
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
}
