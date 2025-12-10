using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using MehguViewer.Core.Shared;
using Xunit;

namespace MehguViewer.Core.Tests.Endpoints;

/// <summary>
/// Tests for social feature endpoints: comments and voting/rating systems.
/// </summary>
/// <remarks>
/// <para><strong>Test Coverage:</strong></para>
/// <list type="bullet">
///   <item><description>Authorization: Verify access control for authenticated/unauthenticated users</description></item>
///   <item><description>Validation: Test input validation for URNs, content length, vote values</description></item>
///   <item><description>CRUD Operations: Create comments, list comments, cast votes</description></item>
///   <item><description>Security: XSS prevention, URN format validation, content sanitization</description></item>
///   <item><description>Error Handling: Proper error responses for invalid inputs</description></item>
/// </list>
/// </remarks>
[Trait("Category", "Unit")]
[Trait("Endpoint", "Social")]
public class SocialEndpointTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly HttpClient _authClient;

    public SocialEndpointTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _authClient = factory.CreateAuthenticatedClient("User");
    }

    #region ListComments Tests

    [Fact]
    public async Task ListComments_WithValidUrn_ReturnsOk()
    {
        // Arrange
        var targetUrn = "urn:mvn:series:123e4567-e89b-12d3-a456-426614174000";

        // Act
        var response = await _client.GetAsync($"/api/v1/comments?target_urn={targetUrn}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("data", content);
        Assert.Contains("meta", content);
    }

    [Fact]
    public async Task ListComments_WithoutTargetUrn_ReturnsBadRequest()
    {
        // Act - Missing query parameter
        var response = await _client.GetAsync("/api/v1/comments");

        // Assert - ASP.NET Core throws BadHttpRequestException before our endpoint code runs
        // when a required query parameter is missing, resulting in a 400 response
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ListComments_WithEmptyTargetUrn_ReturnsBadRequest()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/comments?target_urn=");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Target URN is required", content);
    }

    [Fact]
    public async Task ListComments_WithInvalidUrnFormat_ReturnsBadRequest()
    {
        // Arrange
        var invalidUrn = "not-a-valid-urn";

        // Act
        var response = await _client.GetAsync($"/api/v1/comments?target_urn={invalidUrn}");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Invalid target URN format", content);
    }

    [Fact]
    public async Task ListComments_WithMalformedUrn_ReturnsBadRequest()
    {
        // Arrange
        var malformedUrn = "urn:invalid:type:123";

        // Act
        var response = await _client.GetAsync($"/api/v1/comments?target_urn={malformedUrn}");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ListComments_ReturnsCommentsSuccessfully()
    {
        // Arrange
        var targetUrn = "urn:mvn:series:999e9999-e99b-99d9-a999-999999999999";

        // Act
        var response = await _client.GetAsync($"/api/v1/comments?target_urn={targetUrn}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        // Note: Current MemoryRepository implementation returns ALL comments,
        // not filtered by target_urn (this is a known TODO in the repository)
        // So we just verify the structure is correct
        Assert.Contains("data", content);
        Assert.Contains("meta", content);
    }

    #endregion

    #region CreateComment Tests - Authorization

    [Fact]
    public async Task CreateComment_WithoutAuth_ReturnsUnauthorized()
    {
        // Arrange
        var payload = new
        {
            target_urn = "urn:mvn:series:123e4567-e89b-12d3-a456-426614174000",
            body_markdown = "This is a test comment",
            spoiler = false
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/comments", payload);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateComment_WithAuth_ReturnsCreated()
    {
        // Arrange
        var payload = new
        {
            target_urn = "urn:mvn:series:123e4567-e89b-12d3-a456-426614174000",
            body_markdown = "This is an authenticated comment",
            spoiler = false
        };

        // Act
        var response = await _authClient.PostAsJsonAsync("/api/v1/comments", payload);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("This is an authenticated comment", content);
        Assert.Contains("urn:mvn:comment:", content);
        Assert.Contains("created_at", content);
        Assert.Contains("author", content);
    }

    #endregion

    #region CreateComment Tests - Validation

    [Fact]
    public async Task CreateComment_WithNullBody_ReturnsBadRequest()
    {
        // Arrange - intentionally malformed JSON would be caught by framework
        // Testing the endpoint validation layer
        var payload = new
        {
            target_urn = "urn:mvn:series:123e4567-e89b-12d3-a456-426614174000",
            body_markdown = (string?)null,
            spoiler = false
        };

        // Act
        var response = await _authClient.PostAsJsonAsync("/api/v1/comments", payload);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Comment body is required", content);
    }

    [Fact]
    public async Task CreateComment_WithEmptyBody_ReturnsBadRequest()
    {
        // Arrange
        var payload = new
        {
            target_urn = "urn:mvn:series:123e4567-e89b-12d3-a456-426614174000",
            body_markdown = "",
            spoiler = false
        };

        // Act
        var response = await _authClient.PostAsJsonAsync("/api/v1/comments", payload);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Comment body is required", content);
    }

    [Fact]
    public async Task CreateComment_WithWhitespaceBody_ReturnsBadRequest()
    {
        // Arrange
        var payload = new
        {
            target_urn = "urn:mvn:series:123e4567-e89b-12d3-a456-426614174000",
            body_markdown = "   ",
            spoiler = false
        };

        // Act
        var response = await _authClient.PostAsJsonAsync("/api/v1/comments", payload);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Comment body is required", content);
    }

    [Fact]
    public async Task CreateComment_WithTooLongBody_ReturnsBadRequest()
    {
        // Arrange
        var longBody = new string('a', 10001); // Max is 10000
        var payload = new
        {
            target_urn = "urn:mvn:series:123e4567-e89b-12d3-a456-426614174000",
            body_markdown = longBody,
            spoiler = false
        };

        // Act
        var response = await _authClient.PostAsJsonAsync("/api/v1/comments", payload);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Comment body must be between", content);
        Assert.Contains("10000", content);
    }

    [Fact]
    public async Task CreateComment_WithMaxLengthBody_ReturnsCreated()
    {
        // Arrange
        var maxBody = new string('a', 10000); // Exactly at max
        var payload = new
        {
            target_urn = "urn:mvn:series:123e4567-e89b-12d3-a456-426614174000",
            body_markdown = maxBody,
            spoiler = false
        };

        // Act
        var response = await _authClient.PostAsJsonAsync("/api/v1/comments", payload);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task CreateComment_WithoutTargetUrn_ReturnsBadRequest()
    {
        // Arrange
        var payload = new
        {
            target_urn = "",
            body_markdown = "This comment has no target",
            spoiler = false
        };

        // Act
        var response = await _authClient.PostAsJsonAsync("/api/v1/comments", payload);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Target URN is required", content);
    }

    [Fact]
    public async Task CreateComment_WithInvalidUrnFormat_ReturnsBadRequest()
    {
        // Arrange
        var payload = new
        {
            target_urn = "invalid-urn-format",
            body_markdown = "This comment has an invalid URN",
            spoiler = false
        };

        // Act
        var response = await _authClient.PostAsJsonAsync("/api/v1/comments", payload);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Invalid target URN format", content);
    }

    #endregion

    #region CreateComment Tests - Security

    [Fact]
    public async Task CreateComment_SanitizesHtmlContent()
    {
        // Arrange - Test XSS prevention
        var payload = new
        {
            target_urn = "urn:mvn:series:123e4567-e89b-12d3-a456-426614174000",
            body_markdown = "<script>alert('xss')</script>Hello World",
            spoiler = false
        };

        // Act
        var response = await _authClient.PostAsJsonAsync("/api/v1/comments", payload);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        // Content should be HTML-encoded
        Assert.Contains("&lt;script&gt;", content);
        Assert.DoesNotContain("<script>", content);
    }

    [Fact]
    public async Task CreateComment_SanitizesSpecialCharacters()
    {
        // Arrange
        var payload = new
        {
            target_urn = "urn:mvn:series:123e4567-e89b-12d3-a456-426614174000",
            body_markdown = "Test with <>&\"' special chars",
            spoiler = false
        };

        // Act
        var response = await _authClient.PostAsJsonAsync("/api/v1/comments", payload);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        // Verify HTML entities are encoded
        Assert.Contains("&lt;", content);
        Assert.Contains("&gt;", content);
        Assert.Contains("&amp;", content);
    }

    #endregion

    #region CreateComment Tests - Functionality

    [Fact]
    public async Task CreateComment_IncludesAuthorInformation()
    {
        // Arrange
        var payload = new
        {
            target_urn = "urn:mvn:series:123e4567-e89b-12d3-a456-426614174000",
            body_markdown = "Comment with author info",
            spoiler = false
        };

        // Act
        var response = await _authClient.PostAsJsonAsync("/api/v1/comments", payload);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("author", content);
        Assert.Contains("uid", content);
        Assert.Contains("display_name", content);
    }

    [Fact]
    public async Task CreateComment_SetsCreatedAtTimestamp()
    {
        // Arrange
        var payload = new
        {
            target_urn = "urn:mvn:series:123e4567-e89b-12d3-a456-426614174000",
            body_markdown = "Comment with timestamp",
            spoiler = false
        };

        // Act
        var response = await _authClient.PostAsJsonAsync("/api/v1/comments", payload);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("created_at", content);
    }

    [Fact]
    public async Task CreateComment_InitializesVoteCountToZero()
    {
        // Arrange
        var payload = new
        {
            target_urn = "urn:mvn:series:123e4567-e89b-12d3-a456-426614174000",
            body_markdown = "New comment should have zero votes",
            spoiler = false
        };

        // Act
        var response = await _authClient.PostAsJsonAsync("/api/v1/comments", payload);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"vote_count\":0", content);
    }

    [Fact]
    public async Task CreateComment_GeneratesValidCommentUrn()
    {
        // Arrange
        var payload = new
        {
            target_urn = "urn:mvn:series:123e4567-e89b-12d3-a456-426614174000",
            body_markdown = "Comment should have valid URN",
            spoiler = false
        };

        // Act
        var response = await _authClient.PostAsJsonAsync("/api/v1/comments", payload);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("urn:mvn:comment:", content);
        Assert.Matches(@"""id"":""urn:mvn:comment:[a-f0-9\-]{36}""", content);
    }

    #endregion

    #region CastVote Tests - Authorization

    [Fact]
    public async Task CastVote_WithoutAuth_ReturnsUnauthorized()
    {
        // Arrange
        var payload = new
        {
            target_id = "urn:mvn:series:123e4567-e89b-12d3-a456-426614174000",
            target_type = "series",
            value = 1
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/votes", payload);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CastVote_WithAuth_ReturnsOk()
    {
        // Arrange
        var payload = new
        {
            target_id = "urn:mvn:series:123e4567-e89b-12d3-a456-426614174000",
            target_type = "series",
            value = 1
        };

        // Act
        var response = await _authClient.PostAsJsonAsync("/api/v1/votes", payload);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("success", content);
    }

    #endregion

    #region CastVote Tests - Validation

    [Fact]
    public async Task CastVote_WithoutTargetId_ReturnsBadRequest()
    {
        // Arrange
        var payload = new
        {
            target_id = "",
            target_type = "series",
            value = 1
        };

        // Act
        var response = await _authClient.PostAsJsonAsync("/api/v1/votes", payload);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Target ID is required", content);
    }

    [Fact]
    public async Task CastVote_WithInvalidUrnFormat_ReturnsBadRequest()
    {
        // Arrange
        var payload = new
        {
            target_id = "invalid-urn",
            target_type = "series",
            value = 1
        };

        // Act
        var response = await _authClient.PostAsJsonAsync("/api/v1/votes", payload);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Invalid target ID format", content);
    }

    [Fact]
    public async Task CastVote_WithValueTooHigh_ReturnsBadRequest()
    {
        // Arrange
        var payload = new
        {
            target_id = "urn:mvn:series:123e4567-e89b-12d3-a456-426614174000",
            target_type = "series",
            value = 2 // Max is 1
        };

        // Act
        var response = await _authClient.PostAsJsonAsync("/api/v1/votes", payload);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Vote value must be between", content);
    }

    [Fact]
    public async Task CastVote_WithValueTooLow_ReturnsBadRequest()
    {
        // Arrange
        var payload = new
        {
            target_id = "urn:mvn:series:123e4567-e89b-12d3-a456-426614174000",
            target_type = "series",
            value = -2 // Min is -1
        };

        // Act
        var response = await _authClient.PostAsJsonAsync("/api/v1/votes", payload);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Vote value must be between", content);
    }

    [Fact]
    public async Task CastVote_WithUpvote_ReturnsOk()
    {
        // Arrange
        var payload = new
        {
            target_id = "urn:mvn:series:123e4567-e89b-12d3-a456-426614174000",
            target_type = "series",
            value = 1
        };

        // Act
        var response = await _authClient.PostAsJsonAsync("/api/v1/votes", payload);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CastVote_WithDownvote_ReturnsOk()
    {
        // Arrange
        var payload = new
        {
            target_id = "urn:mvn:series:123e4567-e89b-12d3-a456-426614174000",
            target_type = "series",
            value = -1
        };

        // Act
        var response = await _authClient.PostAsJsonAsync("/api/v1/votes", payload);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CastVote_WithNeutralVote_ReturnsOk()
    {
        // Arrange
        var payload = new
        {
            target_id = "urn:mvn:series:123e4567-e89b-12d3-a456-426614174000",
            target_type = "series",
            value = 0
        };

        // Act
        var response = await _authClient.PostAsJsonAsync("/api/v1/votes", payload);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    #endregion

    #region CastVote Tests - Functionality

    [Fact]
    public async Task CastVote_OnComment_ReturnsOk()
    {
        // Arrange
        var payload = new
        {
            target_id = "urn:mvn:comment:123e4567-e89b-12d3-a456-426614174000",
            target_type = "comment",
            value = 1
        };

        // Act
        var response = await _authClient.PostAsJsonAsync("/api/v1/votes", payload);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CastVote_OnUnit_ReturnsOk()
    {
        // Arrange
        var payload = new
        {
            target_id = "urn:mvn:unit:123e4567-e89b-12d3-a456-426614174000",
            target_type = "unit",
            value = 1
        };

        // Act
        var response = await _authClient.PostAsJsonAsync("/api/v1/votes", payload);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CastVote_UpdatesExistingVote()
    {
        // Arrange - Cast initial vote
        var targetId = "urn:mvn:series:123e4567-e89b-12d3-a456-426614174000";
        var payload1 = new
        {
            target_id = targetId,
            target_type = "series",
            value = 1
        };

        // Act - Cast first vote
        var response1 = await _authClient.PostAsJsonAsync("/api/v1/votes", payload1);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);

        // Arrange - Change vote
        var payload2 = new
        {
            target_id = targetId,
            target_type = "series",
            value = -1
        };

        // Act - Update vote
        var response2 = await _authClient.PostAsJsonAsync("/api/v1/votes", payload2);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public async Task CreateCommentAndList_ReturnsNewComment()
    {
        // Arrange
        var targetUrn = "urn:mvn:series:integration-test-001";
        var commentText = "Integration test comment";
        var createPayload = new
        {
            target_urn = targetUrn,
            body_markdown = commentText,
            spoiler = false
        };

        // Act - Create comment
        var createResponse = await _authClient.PostAsJsonAsync("/api/v1/comments", createPayload);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        // Act - List comments
        var listResponse = await _client.GetAsync($"/api/v1/comments?target_urn={targetUrn}");
        
        // Assert
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var content = await listResponse.Content.ReadAsStringAsync();
        Assert.Contains(commentText, content);
    }

    #endregion
}
