using System.Security.Claims;
using MehguViewer.Shared.Models;
using MehguViewer.Core.Backend.Services;
using Microsoft.AspNetCore.Mvc;

namespace MehguViewer.Core.Backend.Endpoints;

public static class SocialEndpoints
{
    public static void MapSocialEndpoints(this IEndpointRouteBuilder app)
    {
        var comments = app.MapGroup("/api/v1/comments");
        comments.MapGet("/", ListComments);
        comments.MapPost("/", CreateComment).RequireAuthorization("MvnSocial");

        var votes = app.MapGroup("/api/v1/votes").RequireAuthorization("MvnSocial");
        votes.MapPost("/", CastVote);
    }

    private static async Task<IResult> ListComments(
        [FromQuery] string target_urn, 
        [FromQuery] int? depth, 
        [FromQuery] string? cursor, 
        IRepository repo)
    {
        await Task.CompletedTask;
        if (string.IsNullOrWhiteSpace(target_urn)) return Results.BadRequest("Target URN is required");

        var comments = repo.GetComments(target_urn);
        // Filter by target_urn would happen here in real DB
        return Results.Ok(new CommentListResponse(comments.ToArray(), new CursorPagination(null, false)));
    }

    private static async Task<IResult> CreateComment(
        [FromBody] CommentCreate request, 
        ClaimsPrincipal user, 
        IRepository repo)
    {
        await Task.CompletedTask;
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        var username = user.FindFirstValue(ClaimTypes.Name);
        
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

        if (string.IsNullOrWhiteSpace(request.target_urn)) return Results.BadRequest("Target URN is required");
        if (string.IsNullOrWhiteSpace(request.body_markdown)) return Results.BadRequest("Comment body is required");

        var comment = new Comment(
            Guid.NewGuid().ToString(),
            request.body_markdown,
            new AuthorSnapshot(userId, username ?? "User", "", "user"),
            DateTime.UtcNow,
            0
        );
        repo.AddComment(comment);
        return Results.Created($"/api/v1/comments?target_urn={request.target_urn}", comment);
    }

    private static async Task<IResult> CastVote(
        [FromBody] Vote vote, 
        ClaimsPrincipal user, 
        IRepository repo)
    {
        await Task.CompletedTask;
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

        if (string.IsNullOrWhiteSpace(vote.target_id)) return Results.BadRequest("Target ID is required");

        repo.AddVote(userId, vote);
        return Results.Ok();
    }
}
