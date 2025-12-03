using System.Security.Claims;
using MehguViewer.Shared.Models;
using MehguViewer.Core.Backend.Services;
using Microsoft.AspNetCore.Mvc;

namespace MehguViewer.Core.Backend.Endpoints;

public static class CollectionEndpoints
{
    public static void MapCollectionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/collections").RequireAuthorization();

        group.MapGet("/", ListCollections);
        group.MapPost("/", CreateCollection);
        group.MapPost("/{collectionId}/items", AddCollectionItem);
        group.MapDelete("/{collectionId}/items/{urn}", RemoveCollectionItem);
    }

    private static async Task<IResult> ListCollections(ClaimsPrincipal user, IRepository repo)
    {
        await Task.CompletedTask;
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

        // Simulate async DB call
        var collections = repo.ListCollections(userId);
        return Results.Ok(collections);
    }

    private static async Task<IResult> CreateCollection(
        [FromBody] CollectionCreate request, 
        ClaimsPrincipal user, 
        IRepository repo)
    {
        await Task.CompletedTask;
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

        if (string.IsNullOrWhiteSpace(request.name))
            return Results.BadRequest("Collection name is required.");

        var collection = new Collection(
            Guid.NewGuid().ToString(),
            request.name,
            false,
            Array.Empty<string>()
        );
        repo.AddCollection(collection);
        return Results.Created($"/api/v1/collections/{collection.id}", collection);
    }

    private static async Task<IResult> AddCollectionItem(
        string collectionId, 
        [FromBody] CollectionItemAdd request, 
        ClaimsPrincipal user, 
        IRepository repo)
    {
        await Task.CompletedTask;
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

        if (string.IsNullOrWhiteSpace(request.target_urn))
            return Results.BadRequest("Target URN is required.");

        var collection = repo.GetCollection(collectionId);
        if (collection == null) return Results.NotFound();

        // In real app, check ownership
        
        var updatedItems = collection.items.Append(request.target_urn).Distinct().ToArray();
        var updatedCollection = collection with { items = updatedItems };
        repo.UpdateCollection(updatedCollection);
        
        return Results.Ok();
    }

    private static async Task<IResult> RemoveCollectionItem(
        string collectionId, 
        string urn, 
        ClaimsPrincipal user, 
        IRepository repo)
    {
        await Task.CompletedTask;
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

        var collection = repo.GetCollection(collectionId);
        if (collection == null) return Results.NotFound();

        // In real app, check ownership

        var updatedItems = collection.items.Where(i => i != urn).ToArray();
        var updatedCollection = collection with { items = updatedItems };
        repo.UpdateCollection(updatedCollection);

        return Results.NoContent();
    }
}
