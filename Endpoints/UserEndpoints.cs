using System.Security.Claims;
using MehguViewer.Shared.Models;
using MehguViewer.Core.Backend.Services;
using Microsoft.AspNetCore.Mvc;

namespace MehguViewer.Core.Backend.Endpoints;

public static class UserEndpoints
{
    public static void MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/me").RequireAuthorization();

        // Profile endpoints
        group.MapGet("/", GetProfile);
        group.MapPatch("/password", ChangePassword);
        
        // Library & History
        group.MapGet("/library", GetLibrary);
        group.MapGet("/history", GetHistory);
        group.MapPost("/history/batch", BatchImportHistory);
        group.MapPost("/progress", UpdateProgress);
        group.MapDelete("/", DeleteAccount);
    }

    private static async Task<IResult> GetProfile(ClaimsPrincipal user, [FromServices] IRepository repo)
    {
        await Task.CompletedTask;
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

        var dbUser = repo.GetUser(userId);
        if (dbUser == null) return Results.NotFound();

        return Results.Ok(new UserProfileResponse(
            dbUser.id,
            dbUser.username,
            dbUser.role,
            dbUser.created_at
        ));
    }

    private static async Task<IResult> ChangePassword(
        [FromBody] ChangePasswordRequest request, 
        ClaimsPrincipal user, 
        [FromServices] IRepository repo)
    {
        await Task.CompletedTask;
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

        // Validate request
        if (string.IsNullOrWhiteSpace(request.current_password) || 
            string.IsNullOrWhiteSpace(request.new_password))
        {
            return Results.BadRequest(new Problem(
                "urn:mvn:error:validation",
                "Current password and new password are required",
                400,
                null,
                "/api/v1/me/password"
            ));
        }

        // Get user
        var dbUser = repo.GetUser(userId);
        if (dbUser == null) return Results.NotFound();

        // Verify current password
        if (!AuthService.VerifyPassword(request.current_password, dbUser.password_hash))
        {
            return Results.BadRequest(new Problem(
                "urn:mvn:error:invalid-password",
                "Current password is incorrect",
                400,
                null,
                "/api/v1/me/password"
            ));
        }

        // Validate new password strength
        var (isValid, error) = AuthService.ValidatePasswordStrength(request.new_password);
        if (!isValid)
        {
            return Results.BadRequest(new Problem(
                "urn:mvn:error:weak-password",
                error!,
                400,
                null,
                "/api/v1/me/password"
            ));
        }

        // Update password
        var newHash = AuthService.HashPassword(request.new_password);
        var updatedUser = dbUser with { password_hash = newHash };
        repo.UpdateUser(updatedUser);

        return Results.Ok(new { message = "Password changed successfully" });
    }

    private static async Task<IResult> DeleteAccount(ClaimsPrincipal user, [FromServices] IRepository repo)
    {
        await Task.CompletedTask;
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

        // 1. Anonymize Comments
        repo.AnonymizeUserContent(userId);

        // 2. Delete History
        repo.DeleteUserHistory(userId);

        // 3. Delete User
        repo.DeleteUser(userId);

        return Results.NoContent();
    }

    private static async Task<IResult> BatchImportHistory(
        [FromBody] HistoryBatchImport request, 
        ClaimsPrincipal user, 
        [FromServices] IRepository repo,
        [FromServices] JobService jobService)
    {
        await Task.CompletedTask;
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

        if (request.items == null || !request.items.Any())
            return Results.BadRequest("No items to import");

        // In a real implementation, this would be a background job
        // For now, we'll just process it synchronously or queue a job
        
        var job = jobService.CreateJob("IMPORT_HISTORY");
        
        // Simulate processing
        foreach (var item in request.items)
        {
            repo.UpdateProgress(userId, new ReadingProgress(
                item.series_urn,
                item.chapter_urn,
                1, // Default page
                "reading",
                new DateTimeOffset(item.read_at).ToUnixTimeMilliseconds()
            ));
        }
        
        jobService.UpdateJob(job.id, "COMPLETED", 100);

        return Results.Accepted($"/api/v1/jobs/{job.id}");
    }

    private static async Task<IResult> GetLibrary(ClaimsPrincipal user, [FromServices] IRepository repo)
    {
        await Task.CompletedTask;
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

        var library = repo.GetLibrary(userId);
        return Results.Ok(library);
    }

    private static async Task<IResult> GetHistory(ClaimsPrincipal user, [FromServices] IRepository repo)
    {
        await Task.CompletedTask;
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

        var history = repo.GetHistory(userId);
        return Results.Ok(new HistoryListResponse(history.ToArray(), new HistoryMeta(history.Count(), false)));
    }

    private static async Task<IResult> UpdateProgress(ClaimsPrincipal user, [FromBody] ReadingProgress progress, [FromServices] IRepository repo)
    {
        await Task.CompletedTask;
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

        // Basic validation
        if (string.IsNullOrEmpty(progress.series_urn)) return Results.BadRequest("series_urn is required");

        repo.UpdateProgress(userId, progress);
        return Results.Ok();
    }
}
