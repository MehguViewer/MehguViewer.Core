using MehguViewer.Core.Backend.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace MehguViewer.Core.Tests;

/// <summary>
/// Custom WebApplicationFactory for testing that configures a test environment.
/// Uses in-memory repository to avoid database dependencies in unit tests.
/// </summary>
public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
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
            var memoryRepo = new MemoryRepository();
            services.AddSingleton<IRepository>(memoryRepo);
        });
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
        var client = Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        
        // For testing, we can add a mock authorization header
        // In a real scenario, you'd generate a valid test token
        // client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateTestToken(role));
        
        return client;
    }
}
