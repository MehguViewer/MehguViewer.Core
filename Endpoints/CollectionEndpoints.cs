using System.Security.Claims;
using MehguViewer.Core.Shared;
using MehguViewer.Core.Services;
using MehguViewer.Core.Workers;
using MehguViewer.Core.Helpers;
using MehguViewer.Core.Infrastructures;
using Microsoft.AspNetCore.Mvc;

#pragma warning disable CS1998 // Async method lacks 'await' operators

namespace MehguViewer.Core.Endpoints;

/// <summary>
/// API endpoints for managing user collections (reading lists, favorites, custom lists).
/// </summary>
/// <remarks>
/// <para><strong>Purpose:</strong></para>
/// Provides RESTful endpoints for organizing series and units into collections.
/// Collections are user-specific and support custom categorization of content.
/// 
/// <para><strong>Endpoints:</strong></para>
/// <list type="bullet">
///   <item><strong>GET /api/v1/collections</strong> - List all collections for authenticated user</item>
///   <item><strong>POST /api/v1/collections</strong> - Create new collection</item>
///   <item><strong>POST /api/v1/collections/{collectionId}/items</strong> - Add item to collection</item>
///   <item><strong>DELETE /api/v1/collections/{collectionId}/items/{urn}</strong> - Remove item from collection</item>
/// </list>
/// 
/// <para><strong>Security:</strong></para>
/// All endpoints require authentication. Users can only access/modify their own collections.
/// URN validation prevents injection attacks. Ownership checks enforce access control.
/// 
/// <para><strong>RFC 7807 Compliance:</strong></para>
/// Error responses follow Problem Details standard with appropriate status codes.
/// </remarks>
public static class CollectionEndpoints
{
    #region Constants

    /// <summary>Logger name for consistent telemetry categorization.</summary>
    private const string LoggerName = "MehguViewer.Core.Endpoints.CollectionEndpoints";

    /// <summary>Maximum collection name length to prevent abuse.</summary>
    private const int MaxCollectionNameLength = 200;

    /// <summary>Maximum items per collection to prevent performance issues.</summary>
    private const int MaxCollectionItems = 10000;

    #endregion

    #region Endpoint Registration

    /// <summary>
    /// Registers collection management endpoints with the application routing.
    /// </summary>
    /// <param name="app">The endpoint route builder.</param>
    public static void MapCollectionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/collections")
            .RequireAuthorization()
            .WithTags("Collections");

        group.MapGet("/", ListCollections)
            .WithName("ListCollections")
            .WithSummary("Retrieve all collections for the authenticated user")
            .Produces<IEnumerable<Collection>>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized);

        group.MapPost("/", CreateCollection)
            .WithName("CreateCollection")
            .WithSummary("Create a new collection")
            .Produces<Collection>(StatusCodes.Status201Created)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized);

        group.MapPost("/{collectionId}/items", AddCollectionItem)
            .WithName("AddCollectionItem")
            .WithSummary("Add an item to a collection")
            .Produces(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        group.MapDelete("/{collectionId}/items/{urn}", RemoveCollectionItem)
            .WithName("RemoveCollectionItem")
            .WithSummary("Remove an item from a collection")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);
    }

    #endregion

    #region Endpoint Handlers

    /// <summary>
    /// Lists all collections owned by the authenticated user.
    /// </summary>
    /// <param name="user">The authenticated user's claims principal.</param>
    /// <param name="repo">Repository for data access.</param>
    /// <param name="loggerFactory">Factory for creating logger instances.</param>
    /// <param name="context">HTTP context for request metadata.</param>
    /// <returns>200 OK with collection list, or 401 Unauthorized.</returns>
    private static async Task<IResult> ListCollections(
        ClaimsPrincipal user, 
        [FromServices] IRepository repo,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext context)
    {
        var logger = loggerFactory.CreateLogger(LoggerName);
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);

        // Security: Validate user authentication
        if (string.IsNullOrEmpty(userId))
        {
            logger.LogWarning("Unauthenticated collection list request from {IpAddress}", context.Connection.RemoteIpAddress);
            return ResultsExtensions.Unauthorized("Authentication required", context.Request.Path);
        }

        logger.LogDebug("Listing collections for user {UserId} from {IpAddress}", userId, context.Connection.RemoteIpAddress);

        try
        {
            // Retrieve user's collections from repository
            var collections = repo.ListCollections(userId);
            var collectionList = collections.ToList();

            logger.LogInformation("Retrieved {Count} collections for user {UserId}", collectionList.Count, userId);

            return Results.Ok(collectionList);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to retrieve collections for user {UserId}", userId);
            return ResultsExtensions.InternalServerError(
                "Failed to retrieve collections",
                context.Request.Path,
                context.TraceIdentifier
            );
        }
    }

    /// <summary>
    /// Creates a new collection for the authenticated user.
    /// </summary>
    /// <param name="request">Collection creation request containing name.</param>
    /// <param name="user">The authenticated user's claims principal.</param>
    /// <param name="repo">Repository for data access.</param>
    /// <param name="loggerFactory">Factory for creating logger instances.</param>
    /// <param name="context">HTTP context for request metadata.</param>
    /// <returns>201 Created with new collection, 400 Bad Request for validation errors, or 401 Unauthorized.</returns>
    private static async Task<IResult> CreateCollection(
        [FromBody] CollectionCreate request, 
        ClaimsPrincipal user, 
        [FromServices] IRepository repo,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext context)
    {
        await Task.CompletedTask; // Satisfy async requirement
        var logger = loggerFactory.CreateLogger(LoggerName);
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);

        // Security: Validate user authentication
        if (string.IsNullOrEmpty(userId))
        {
            logger.LogWarning("Unauthenticated collection creation attempt from {IpAddress}", context.Connection.RemoteIpAddress);
            return ResultsExtensions.Unauthorized("Authentication required", context.Request.Path);
        }

        // Validation: Collection name is required
        if (string.IsNullOrWhiteSpace(request.name))
        {
            logger.LogWarning("User {UserId} attempted to create collection with empty name", userId);
            return ResultsExtensions.BadRequest("Collection name is required", context.Request.Path);
        }

        // Validation: Collection name length limit
        if (request.name.Length > MaxCollectionNameLength)
        {
            logger.LogWarning("User {UserId} attempted to create collection with name exceeding {MaxLength} characters", 
                userId, MaxCollectionNameLength);
            return ResultsExtensions.BadRequest(
                $"Collection name must not exceed {MaxCollectionNameLength} characters",
                context.Request.Path
            );
        }

        // Sanitization: Trim whitespace from name
        var sanitizedName = request.name.Trim();

        logger.LogDebug("Creating collection '{CollectionName}' for user {UserId}", sanitizedName, userId);

        try
        {
            // Create new collection with unique ID
            var collection = new Collection(
                id: Guid.NewGuid().ToString(),
                user_id: userId,
                name: sanitizedName,
                is_system: false,
                items: Array.Empty<string>()
            );

            // Persist to repository
            repo.AddCollection(userId, collection);
            
            logger.LogInformation("Created collection {CollectionId} '{CollectionName}' for user {UserId}", 
                collection.id, sanitizedName, userId);
            
            return Results.Created($"/api/v1/collections/{collection.id}", collection);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create collection '{CollectionName}' for user {UserId}", sanitizedName, userId);
            return ResultsExtensions.InternalServerError(
                "Failed to create collection",
                context.Request.Path,
                context.TraceIdentifier
            );
        }
    }

    /// <summary>
    /// Adds an item (series or unit URN) to an existing collection.
    /// </summary>
    /// <param name="collectionId">The collection identifier.</param>
    /// <param name="request">Request containing the target URN to add.</param>
    /// <param name="user">The authenticated user's claims principal.</param>
    /// <param name="repo">Repository for data access.</param>
    /// <param name="loggerFactory">Factory for creating logger instances.</param>
    /// <param name="context">HTTP context for request metadata.</param>
    /// <returns>200 OK on success, 400 Bad Request for invalid URN, 403 Forbidden if not owner, or 404 Not Found.</returns>
    private static async Task<IResult> AddCollectionItem(
        string collectionId, 
        [FromBody] CollectionItemAdd request, 
        ClaimsPrincipal user, 
        [FromServices] IRepository repo,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext context)
    {
        await Task.CompletedTask; // Satisfy async requirement
        var logger = loggerFactory.CreateLogger(LoggerName);
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);

        // Security: Validate user authentication
        if (string.IsNullOrEmpty(userId))
        {
            logger.LogWarning("Unauthenticated item addition attempt to collection {CollectionId}", collectionId);
            return ResultsExtensions.Unauthorized("Authentication required", context.Request.Path);
        }

        // Validation: Target URN is required
        if (string.IsNullOrWhiteSpace(request.target_urn))
        {
            logger.LogWarning("User {UserId} attempted to add empty URN to collection {CollectionId}", userId, collectionId);
            return ResultsExtensions.BadRequest("Target URN is required", context.Request.Path);
        }

        // Security: Validate URN format to prevent injection
        if (!IsValidMvnUrn(request.target_urn))
        {
            logger.LogWarning("User {UserId} attempted to add invalid URN '{Urn}' to collection {CollectionId}", 
                userId, request.target_urn, collectionId);
            return ResultsExtensions.BadRequest("Invalid URN format", context.Request.Path);
        }

        logger.LogDebug("Adding item {TargetUrn} to collection {CollectionId} for user {UserId}", 
            request.target_urn, collectionId, userId);

        try
        {
            // Retrieve collection
            var collection = repo.GetCollection(collectionId);
            if (collection == null)
            {
                logger.LogWarning("Collection {CollectionId} not found for user {UserId}", collectionId, userId);
                return ResultsExtensions.NotFound($"Collection {collectionId} not found", context.Request.Path);
            }

            // Security: Verify ownership
            if (collection.user_id != userId)
            {
                logger.LogWarning("User {UserId} attempted to modify collection {CollectionId} owned by {OwnerId}", 
                    userId, collectionId, collection.user_id);
                return ResultsExtensions.Forbidden("You do not have permission to modify this collection", context.Request.Path);
            }

            // Validation: Check collection size limit
            if (collection.items.Length >= MaxCollectionItems)
            {
                logger.LogWarning("Collection {CollectionId} has reached maximum item limit {MaxItems}", 
                    collectionId, MaxCollectionItems);
                return ResultsExtensions.BadRequest(
                    $"Collection has reached maximum item limit of {MaxCollectionItems}",
                    context.Request.Path
                );
            }
            
            // Add item (deduplicate to prevent duplicates)
            var updatedItems = collection.items.Append(request.target_urn).Distinct().ToArray();
            
            // Check if item was actually added (not a duplicate)
            var wasAdded = updatedItems.Length > collection.items.Length;
            
            var updatedCollection = collection with { items = updatedItems };
            repo.UpdateCollection(updatedCollection);
            
            if (wasAdded)
            {
                logger.LogInformation("Added item {TargetUrn} to collection {CollectionId} (total: {ItemCount})", 
                    request.target_urn, collectionId, updatedItems.Length);
            }
            else
            {
                logger.LogDebug("Item {TargetUrn} already exists in collection {CollectionId}", 
                    request.target_urn, collectionId);
            }
            
            return Results.Ok(new { added = wasAdded, item_count = updatedItems.Length });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to add item {TargetUrn} to collection {CollectionId}", 
                request.target_urn, collectionId);
            return ResultsExtensions.InternalServerError(
                "Failed to add item to collection",
                context.Request.Path,
                context.TraceIdentifier
            );
        }
    }

    /// <summary>
    /// Removes an item from a collection.
    /// </summary>
    /// <param name="collectionId">The collection identifier.</param>
    /// <param name="urn">The URN of the item to remove.</param>
    /// <param name="user">The authenticated user's claims principal.</param>
    /// <param name="repo">Repository for data access.</param>
    /// <param name="loggerFactory">Factory for creating logger instances.</param>
    /// <param name="context">HTTP context for request metadata.</param>
    /// <returns>204 No Content on success, 403 Forbidden if not owner, or 404 Not Found.</returns>
    private static async Task<IResult> RemoveCollectionItem(
        string collectionId, 
        string urn, 
        ClaimsPrincipal user, 
        [FromServices] IRepository repo,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext context)
    {
        await Task.CompletedTask; // Satisfy async requirement
        var logger = loggerFactory.CreateLogger(LoggerName);
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);

        // Security: Validate user authentication
        if (string.IsNullOrEmpty(userId))
        {
            logger.LogWarning("Unauthenticated item removal attempt from collection {CollectionId}", collectionId);
            return ResultsExtensions.Unauthorized("Authentication required", context.Request.Path);
        }

        // Security: Validate URN format to prevent injection
        if (!IsValidMvnUrn(urn))
        {
            logger.LogWarning("User {UserId} attempted to remove invalid URN '{Urn}' from collection {CollectionId}", 
                userId, urn, collectionId);
            return ResultsExtensions.BadRequest("Invalid URN format", context.Request.Path);
        }

        logger.LogDebug("Removing item {TargetUrn} from collection {CollectionId} for user {UserId}", 
            urn, collectionId, userId);

        try
        {
            // Retrieve collection
            var collection = repo.GetCollection(collectionId);
            if (collection == null)
            {
                logger.LogWarning("Collection {CollectionId} not found for user {UserId}", collectionId, userId);
                return ResultsExtensions.NotFound($"Collection {collectionId} not found", context.Request.Path);
            }

            // Security: Verify ownership
            if (collection.user_id != userId)
            {
                logger.LogWarning("User {UserId} attempted to modify collection {CollectionId} owned by {OwnerId}", 
                    userId, collectionId, collection.user_id);
                return ResultsExtensions.Forbidden("You do not have permission to modify this collection", context.Request.Path);
            }

            // Remove item from collection
            var originalCount = collection.items.Length;
            var updatedItems = collection.items.Where(i => i != urn).ToArray();
            var wasRemoved = updatedItems.Length < originalCount;
            
            if (wasRemoved)
            {
                var updatedCollection = collection with { items = updatedItems };
                repo.UpdateCollection(updatedCollection);

                logger.LogInformation("Removed item {TargetUrn} from collection {CollectionId} (remaining: {ItemCount})", 
                    urn, collectionId, updatedItems.Length);
            }
            else
            {
                logger.LogDebug("Item {TargetUrn} was not found in collection {CollectionId}", urn, collectionId);
            }

            return Results.NoContent();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to remove item {TargetUrn} from collection {CollectionId}", 
                urn, collectionId);
            return ResultsExtensions.InternalServerError(
                "Failed to remove item from collection",
                context.Request.Path,
                context.TraceIdentifier
            );
        }
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Validates that a URN follows the MehguViewer URN format (urn:mvn:{type}:{id}).
    /// </summary>
    /// <param name="urn">The URN to validate.</param>
    /// <returns>True if the URN format is valid; otherwise, false.</returns>
    private static bool IsValidMvnUrn(string? urn)
    {
        if (string.IsNullOrWhiteSpace(urn)) return false;
        if (urn.Length > 512) return false; // Security: Prevent DoS
        
        var parts = urn.Split(':');
        return parts.Length == 4 && 
               parts[0] == "urn" && 
               parts[1] == "mvn" && 
               !string.IsNullOrWhiteSpace(parts[2]) && 
               !string.IsNullOrWhiteSpace(parts[3]);
    }

    #endregion
}
