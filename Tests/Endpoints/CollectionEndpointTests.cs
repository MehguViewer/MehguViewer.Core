using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc;
using Xunit;
using MehguViewer.Core.Shared;

namespace MehguViewer.Core.Tests.Endpoints;

/// <summary>
/// Comprehensive test suite for collection management endpoints.
/// Tests CRUD operations, authorization, validation, and error handling.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Endpoint", "Collections")]
public class CollectionEndpointTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly HttpClient _adminClient;
    private readonly HttpClient _userClient;

    public CollectionEndpointTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _adminClient = factory.CreateAuthenticatedClient("Admin");
        _userClient = factory.CreateAuthenticatedClient("User");
    }

    #region Authorization Tests

    /// <summary>
    /// Tests that listing collections without authentication returns 401 Unauthorized.
    /// </summary>
    [Fact]
    public async Task ListCollections_WithoutAuth_ReturnsUnauthorized()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/collections");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Tests that creating a collection without authentication returns 401 Unauthorized.
    /// </summary>
    [Fact]
    public async Task CreateCollection_WithoutAuth_ReturnsUnauthorized()
    {
        // Arrange
        var payload = new { name = "Unauthorized Collection" };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/collections", payload);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    #endregion

    #region List Collections Tests

    /// <summary>
    /// Tests that authenticated users can list their collections.
    /// </summary>
    [Fact]
    public async Task ListCollections_AsAuthenticatedUser_ReturnsOk()
    {
        // Act
        var response = await _userClient.GetAsync("/api/v1/collections");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var collections = await response.Content.ReadFromJsonAsync<Collection[]>();
        Assert.NotNull(collections);
    }

    /// <summary>
    /// Tests that newly created user has empty collection list.
    /// </summary>
    [Fact]
    public async Task ListCollections_NewUser_ReturnsEmptyList()
    {
        // Arrange
        var newUserClient = _factory.CreateAuthenticatedClient("NewUser");

        // Act
        var response = await newUserClient.GetAsync("/api/v1/collections");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var collections = await response.Content.ReadFromJsonAsync<Collection[]>();
        Assert.NotNull(collections);
        Assert.Empty(collections);
    }

    #endregion

    #region Create Collection Tests

    /// <summary>
    /// Tests successful collection creation with valid name.
    /// </summary>
    [Fact]
    public async Task CreateCollection_WithValidName_ReturnsCreated()
    {
        // Arrange
        var payload = new { name = "My Reading List" };

        // Act
        var response = await _userClient.PostAsJsonAsync("/api/v1/collections", payload);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        
        var collection = await response.Content.ReadFromJsonAsync<Collection>();
        Assert.NotNull(collection);
        Assert.Equal("My Reading List", collection.name);
        Assert.NotNull(collection.id);
        Assert.False(collection.is_system);
        Assert.Empty(collection.items);
        
        // Verify Location header
        Assert.NotNull(response.Headers.Location);
        Assert.Contains($"/api/v1/collections/{collection.id}", response.Headers.Location.ToString());
    }

    /// <summary>
    /// Tests that collection creation with empty name returns 400 Bad Request.
    /// </summary>
    [Fact]
    public async Task CreateCollection_WithEmptyName_ReturnsBadRequest()
    {
        // Arrange
        var payload = new { name = "" };

        // Act
        var response = await _userClient.PostAsJsonAsync("/api/v1/collections", payload);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        
        var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problemDetails);
        Assert.Contains("required", problemDetails.Detail, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Tests that collection creation with whitespace-only name returns 400 Bad Request.
    /// </summary>
    [Fact]
    public async Task CreateCollection_WithWhitespaceName_ReturnsBadRequest()
    {
        // Arrange
        var payload = new { name = "   " };

        // Act
        var response = await _userClient.PostAsJsonAsync("/api/v1/collections", payload);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Tests that collection creation with excessively long name returns 400 Bad Request.
    /// </summary>
    [Fact]
    public async Task CreateCollection_WithTooLongName_ReturnsBadRequest()
    {
        // Arrange
        var longName = new string('A', 201); // Exceeds 200 character limit
        var payload = new { name = longName };

        // Act
        var response = await _userClient.PostAsJsonAsync("/api/v1/collections", payload);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        
        var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problemDetails);
        Assert.Contains("200", problemDetails.Detail);
    }

    /// <summary>
    /// Tests that collection name whitespace is trimmed properly.
    /// </summary>
    [Fact]
    public async Task CreateCollection_WithWhitespacePadding_TrimsName()
    {
        // Arrange
        var payload = new { name = "  Favorites  " };

        // Act
        var response = await _userClient.PostAsJsonAsync("/api/v1/collections", payload);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        
        var collection = await response.Content.ReadFromJsonAsync<Collection>();
        Assert.NotNull(collection);
        Assert.Equal("Favorites", collection.name);
    }

    /// <summary>
    /// Tests that multiple users can create collections with the same name (user isolation).
    /// </summary>
    [Fact]
    public async Task CreateCollection_SameNameDifferentUsers_BothSucceed()
    {
        // Arrange
        var payload = new { name = "My Favorites" };
        var user1Client = _factory.CreateAuthenticatedClient("User1");
        var user2Client = _factory.CreateAuthenticatedClient("User2");

        // Act
        var response1 = await user1Client.PostAsJsonAsync("/api/v1/collections", payload);
        var response2 = await user2Client.PostAsJsonAsync("/api/v1/collections", payload);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response1.StatusCode);
        Assert.Equal(HttpStatusCode.Created, response2.StatusCode);
        
        var collection1 = await response1.Content.ReadFromJsonAsync<Collection>();
        var collection2 = await response2.Content.ReadFromJsonAsync<Collection>();
        
        Assert.NotEqual(collection1!.id, collection2!.id);
        Assert.NotEqual(collection1.user_id, collection2.user_id);
    }

    #endregion

    #region Add Collection Item Tests

    /// <summary>
    /// Tests adding a valid URN to a collection.
    /// </summary>
    [Fact]
    public async Task AddCollectionItem_WithValidUrn_ReturnsOk()
    {
        // Arrange
        var createResponse = await _userClient.PostAsJsonAsync("/api/v1/collections", new { name = "Test Collection" });
        var collection = await createResponse.Content.ReadFromJsonAsync<Collection>();
        Assert.NotNull(collection);

        var payload = new { target_urn = "urn:mvn:series:123e4567-e89b-12d3-a456-426614174000" };

        // Act
        var response = await _userClient.PostAsJsonAsync($"/api/v1/collections/{collection.id}/items", payload);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await response.Content.ReadFromJsonAsync<dynamic>();
        Assert.NotNull(result);
    }

    /// <summary>
    /// Tests that adding an empty URN returns 400 Bad Request.
    /// </summary>
    [Fact]
    public async Task AddCollectionItem_WithEmptyUrn_ReturnsBadRequest()
    {
        // Arrange
        var createResponse = await _userClient.PostAsJsonAsync("/api/v1/collections", new { name = "Test Collection" });
        var collection = await createResponse.Content.ReadFromJsonAsync<Collection>();
        Assert.NotNull(collection);

        var payload = new { target_urn = "" };

        // Act
        var response = await _userClient.PostAsJsonAsync($"/api/v1/collections/{collection.id}/items", payload);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Tests that adding an invalid URN format returns 400 Bad Request.
    /// </summary>
    [Fact]
    public async Task AddCollectionItem_WithInvalidUrn_ReturnsBadRequest()
    {
        // Arrange
        var createResponse = await _userClient.PostAsJsonAsync("/api/v1/collections", new { name = "Test Collection" });
        var collection = await createResponse.Content.ReadFromJsonAsync<Collection>();
        Assert.NotNull(collection);

        var payload = new { target_urn = "invalid-urn-format" };

        // Act
        var response = await _userClient.PostAsJsonAsync($"/api/v1/collections/{collection.id}/items", payload);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        
        var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problemDetails);
        Assert.Contains("Invalid URN", problemDetails.Detail);
    }

    /// <summary>
    /// Tests that adding item to non-existent collection returns 404 Not Found.
    /// </summary>
    [Fact]
    public async Task AddCollectionItem_NonExistentCollection_ReturnsNotFound()
    {
        // Arrange
        var payload = new { target_urn = "urn:mvn:series:123e4567-e89b-12d3-a456-426614174000" };

        // Act
        var response = await _userClient.PostAsJsonAsync("/api/v1/collections/nonexistent-id/items", payload);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// Tests that users cannot add items to collections they don't own.
    /// </summary>
    [Fact]
    public async Task AddCollectionItem_OtherUsersCollection_ReturnsForbidden()
    {
        // Arrange - User1 creates collection
        var user1Client = _factory.CreateAuthenticatedClient("User1");
        var createResponse = await user1Client.PostAsJsonAsync("/api/v1/collections", new { name = "User1 Collection" });
        var collection = await createResponse.Content.ReadFromJsonAsync<Collection>();
        Assert.NotNull(collection);

        // Act - User2 tries to add item
        var user2Client = _factory.CreateAuthenticatedClient("User2");
        var payload = new { target_urn = "urn:mvn:series:123e4567-e89b-12d3-a456-426614174000" };
        var response = await user2Client.PostAsJsonAsync($"/api/v1/collections/{collection.id}/items", payload);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// Tests that adding duplicate URN doesn't create duplicates in collection.
    /// </summary>
    [Fact]
    public async Task AddCollectionItem_DuplicateUrn_DeduplicatesItems()
    {
        // Arrange
        var createResponse = await _userClient.PostAsJsonAsync("/api/v1/collections", new { name = "Test Collection" });
        var collection = await createResponse.Content.ReadFromJsonAsync<Collection>();
        Assert.NotNull(collection);

        var payload = new { target_urn = "urn:mvn:series:123e4567-e89b-12d3-a456-426614174000" };

        // Act - Add same item twice
        var response1 = await _userClient.PostAsJsonAsync($"/api/v1/collections/{collection.id}/items", payload);
        var response2 = await _userClient.PostAsJsonAsync($"/api/v1/collections/{collection.id}/items", payload);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        
        // Verify collection only has one item
        var listResponse = await _userClient.GetAsync("/api/v1/collections");
        var collections = await listResponse.Content.ReadFromJsonAsync<Collection[]>();
        var updatedCollection = collections!.First(c => c.id == collection.id);
        
        Assert.Single(updatedCollection.items);
    }

    #endregion

    #region Remove Collection Item Tests

    /// <summary>
    /// Tests removing an item from a collection.
    /// </summary>
    [Fact]
    public async Task RemoveCollectionItem_ExistingItem_ReturnsNoContent()
    {
        // Arrange - Create collection and add item
        var createResponse = await _userClient.PostAsJsonAsync("/api/v1/collections", new { name = "Test Collection" });
        var collection = await createResponse.Content.ReadFromJsonAsync<Collection>();
        Assert.NotNull(collection);

        var urn = "urn:mvn:series:123e4567-e89b-12d3-a456-426614174000";
        await _userClient.PostAsJsonAsync($"/api/v1/collections/{collection.id}/items", new { target_urn = urn });

        // Act - Remove item
        var response = await _userClient.DeleteAsync($"/api/v1/collections/{collection.id}/items/{urn}");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        
        // Verify item was removed
        var listResponse = await _userClient.GetAsync("/api/v1/collections");
        var collections = await listResponse.Content.ReadFromJsonAsync<Collection[]>();
        var updatedCollection = collections!.First(c => c.id == collection.id);
        
        Assert.Empty(updatedCollection.items);
    }

    /// <summary>
    /// Tests removing non-existent item returns 204 No Content (idempotent).
    /// </summary>
    [Fact]
    public async Task RemoveCollectionItem_NonExistentItem_ReturnsNoContent()
    {
        // Arrange
        var createResponse = await _userClient.PostAsJsonAsync("/api/v1/collections", new { name = "Test Collection" });
        var collection = await createResponse.Content.ReadFromJsonAsync<Collection>();
        Assert.NotNull(collection);

        var urn = "urn:mvn:series:123e4567-e89b-12d3-a456-426614174000";

        // Act - Remove item that doesn't exist
        var response = await _userClient.DeleteAsync($"/api/v1/collections/{collection.id}/items/{urn}");

        // Assert - Should be idempotent
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    /// <summary>
    /// Tests removing item from non-existent collection returns 404 Not Found.
    /// </summary>
    [Fact]
    public async Task RemoveCollectionItem_NonExistentCollection_ReturnsNotFound()
    {
        // Arrange
        var urn = "urn:mvn:series:123e4567-e89b-12d3-a456-426614174000";

        // Act
        var response = await _userClient.DeleteAsync($"/api/v1/collections/nonexistent-id/items/{urn}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// Tests that users cannot remove items from collections they don't own.
    /// </summary>
    [Fact]
    public async Task RemoveCollectionItem_OtherUsersCollection_ReturnsForbidden()
    {
        // Arrange - User1 creates collection
        var user1Client = _factory.CreateAuthenticatedClient("User1");
        var createResponse = await user1Client.PostAsJsonAsync("/api/v1/collections", new { name = "User1 Collection" });
        var collection = await createResponse.Content.ReadFromJsonAsync<Collection>();
        Assert.NotNull(collection);

        var urn = "urn:mvn:series:123e4567-e89b-12d3-a456-426614174000";

        // Act - User2 tries to remove item
        var user2Client = _factory.CreateAuthenticatedClient("User2");
        var response = await user2Client.DeleteAsync($"/api/v1/collections/{collection.id}/items/{urn}");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// Tests that removing item with invalid URN format returns 400 Bad Request.
    /// </summary>
    [Fact]
    public async Task RemoveCollectionItem_InvalidUrn_ReturnsBadRequest()
    {
        // Arrange
        var createResponse = await _userClient.PostAsJsonAsync("/api/v1/collections", new { name = "Test Collection" });
        var collection = await createResponse.Content.ReadFromJsonAsync<Collection>();
        Assert.NotNull(collection);

        // Act
        var response = await _userClient.DeleteAsync($"/api/v1/collections/{collection.id}/items/invalid-urn");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    #endregion

    #region Integration Tests

    /// <summary>
    /// Tests complete workflow: create collection, add items, list, remove items.
    /// </summary>
    [Fact]
    public async Task CollectionWorkflow_CreateAddListRemove_WorksCorrectly()
    {
        // Step 1: Create collection
        var createResponse = await _userClient.PostAsJsonAsync("/api/v1/collections", new { name = "My Manga List" });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var collection = await createResponse.Content.ReadFromJsonAsync<Collection>();
        Assert.NotNull(collection);

        // Step 2: Add multiple items
        var urns = new[]
        {
            "urn:mvn:series:111e4567-e89b-12d3-a456-426614174000",
            "urn:mvn:series:222e4567-e89b-12d3-a456-426614174000",
            "urn:mvn:series:333e4567-e89b-12d3-a456-426614174000"
        };

        foreach (var urn in urns)
        {
            var addResponse = await _userClient.PostAsJsonAsync(
                $"/api/v1/collections/{collection.id}/items", 
                new { target_urn = urn }
            );
            Assert.Equal(HttpStatusCode.OK, addResponse.StatusCode);
        }

        // Step 3: List collections and verify items
        var listResponse = await _userClient.GetAsync("/api/v1/collections");
        var collections = await listResponse.Content.ReadFromJsonAsync<Collection[]>();
        var updatedCollection = collections!.First(c => c.id == collection.id);
        Assert.Equal(3, updatedCollection.items.Length);

        // Step 4: Remove one item
        var removeResponse = await _userClient.DeleteAsync(
            $"/api/v1/collections/{collection.id}/items/{urns[1]}"
        );
        Assert.Equal(HttpStatusCode.NoContent, removeResponse.StatusCode);

        // Step 5: Verify item removed
        listResponse = await _userClient.GetAsync("/api/v1/collections");
        collections = await listResponse.Content.ReadFromJsonAsync<Collection[]>();
        updatedCollection = collections!.First(c => c.id == collection.id);
        Assert.Equal(2, updatedCollection.items.Length);
        Assert.DoesNotContain(urns[1], updatedCollection.items);
    }

    #endregion
}
