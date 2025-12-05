using MehguViewer.Core.Backend.Services;
using MehguViewer.Shared.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using System.Net.Http.Headers;
using Xunit;

namespace MehguViewer.Core.Tests;

/// <summary>
/// Custom WebApplicationFactory for testing that configures a test environment.
/// Uses in-memory repository to avoid database dependencies in unit tests.
/// </summary>
public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private MemoryRepository? _repository;

    public MemoryRepository Repository => _repository ?? throw new InvalidOperationException("Repository not initialized");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        
        builder.ConfigureServices(services =>
        {
            // Remove all hosted services that depend on database
            services.RemoveAll<IHostedService>();
            
            // Remove EmbeddedPostgresService and its factory registrations
            services.RemoveAll<EmbeddedPostgresService>();
            
            // Remove existing repository registrations
            services.RemoveAll<DynamicRepository>();
            services.RemoveAll<IRepository>();
            
            // Register MemoryRepository for testing as a singleton
            _repository = new MemoryRepository();
            services.AddSingleton<IRepository>(_repository);
        });
    }

    /// <summary>
    /// Creates a test user and returns their JWT token.
    /// </summary>
    public string CreateTestUserAndGetToken(string username, string role)
    {
        var userId = UrnHelper.CreateUserUrn();
        var user = new User(
            id: userId,
            username: username,
            password_hash: AuthService.HashPassword("Test123!"),
            role: role,
            created_at: DateTime.UtcNow
        );
        Repository.AddUser(user);
        return AuthService.GenerateToken(user);
    }

    /// <summary>
    /// Creates an authenticated HttpClient with the given role.
    /// </summary>
    public HttpClient CreateAuthenticatedClient(string role = "User")
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        
        var token = CreateTestUserAndGetToken($"test_{role.ToLower()}_{Guid.NewGuid():N}", role);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        
        return client;
    }
}

/// <summary>
/// Collection definition for tests that share the same WebApplicationFactory instance.
/// </summary>
[CollectionDefinition("API Tests")]
public class ApiTestCollection : ICollectionFixture<TestWebApplicationFactory>
{
}

/// <summary>
/// Base class for API tests providing common functionality.
/// </summary>
public abstract class ApiTestBase : IClassFixture<TestWebApplicationFactory>
{
    protected readonly HttpClient Client;
    protected readonly TestWebApplicationFactory Factory;

    protected ApiTestBase(TestWebApplicationFactory factory)
    {
        Factory = factory;
        Client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    /// <summary>
    /// Creates an authenticated client with a JWT token.
    /// </summary>
    protected HttpClient CreateAuthenticatedClient(string role = "User")
    {
        return Factory.CreateAuthenticatedClient(role);
    }
}
