using System.Net;
using System.Net.Http.Json;
using MehguViewer.Core.Shared;
using Xunit;

namespace MehguViewer.Core.Tests.Integrations;

[Trait("Category", "Integration")]
public class IntegrationTests_AuthCore : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public IntegrationTests_AuthCore(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task AuthFlow_WithValidToken_Succeeds()
    {
        // 1. Get a valid token
        var token = _factory.CreateTestUserAndGetToken("auth_test_user", "User");
        
        // 2. Call protected endpoint
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        
        var response = await _client.SendAsync(request);
        
        // 3. Verify success
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var profile = await response.Content.ReadFromJsonAsync<UserProfileResponse>();
        Assert.NotNull(profile);
        Assert.Equal("auth_test_user", profile.username);
    }

    [Fact]
    public async Task AuthFlow_WithInvalidToken_ReturnsProblemDetails()
    {
        // 1. Call protected endpoint with invalid token
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "invalid-token");
        
        var response = await _client.SendAsync(request);
        
        // 2. Verify 401 and ProblemDetails
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        
        var problem = await response.Content.ReadFromJsonAsync<Problem>();
        Assert.NotNull(problem);
        Assert.Equal("urn:mvn:error:unauthorized", problem.type);
        Assert.Equal(401, problem.status);
    }

    [Fact]
    public async Task JwksEndpoint_ReturnsKeys()
    {
        // 1. Call JWKS endpoint
        var response = await _client.GetAsync("/api/v1/auth/.well-known/jwks.json");
        
        // 2. Verify success
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("keys", content);
        Assert.Contains("RSA", content);
    }
}
