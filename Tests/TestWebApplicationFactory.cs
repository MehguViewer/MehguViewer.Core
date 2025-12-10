using MehguViewer.Core.Services;
using MehguViewer.Core.Workers;
using MehguViewer.Core.Helpers;
using MehguViewer.Core.Infrastructures;
using MehguViewer.Core.Shared;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
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
    private AuthService? _authService;
    private DynamicRepository? _dynamicRepository;

    public MemoryRepository Repository => _repository ?? throw new InvalidOperationException("Repository not initialized");
    public AuthService AuthService => _authService ?? throw new InvalidOperationException("AuthService not initialized");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        
        builder.ConfigureServices(services =>
        {
            // Remove all hosted services that depend on database
            services.RemoveAll<IHostedService>();
            
            // Remove EmbeddedPostgresService and its factory registrations
            services.RemoveAll<EmbeddedPostgresService>();
            
            // Register a mock EmbeddedPostgresService for tests that need it
            var mockEmbeddedPg = new EmbeddedPostgresService(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<EmbeddedPostgresService>.Instance,
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["EmbeddedPostgres:Enabled"] = "false"
                }).Build());
            services.AddSingleton(mockEmbeddedPg);
            
            // Remove existing repository registrations
            services.RemoveAll<DynamicRepository>();
            services.RemoveAll<IRepository>();
            
            // Remove existing AuthService to ensure we use our singleton
            services.RemoveAll<AuthService>();
            
            // Remove existing MetadataAggregationService
            services.RemoveAll<MetadataAggregationService>();
            
            // Register MetadataAggregationService as singleton
            var metadataLogger = Microsoft.Extensions.Logging.Abstractions.NullLogger<MetadataAggregationService>.Instance;
            var metadataService = new MetadataAggregationService(metadataLogger);
            services.AddSingleton(metadataService);
            
            // Register MemoryRepository for testing as a singleton
            var repoLogger = Microsoft.Extensions.Logging.Abstractions.NullLogger<MemoryRepository>.Instance;
            _repository = new MemoryRepository(repoLogger, metadataService);
            services.AddSingleton<IRepository>(_repository);

            // Register DynamicRepository for system endpoint tests
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Repository:Type"] = "memory"
                })
                .Build();
            var loggerFactory = Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance;
            _dynamicRepository = new DynamicRepository(config, loggerFactory, 
                embeddedPostgres: null, 
                fileService: null, 
                metadataService: metadataService);
            services.AddSingleton(_dynamicRepository);

            // Register AuthService as singleton so we can use it to generate valid tokens
            var authLogger = Microsoft.Extensions.Logging.Abstractions.NullLogger<AuthService>.Instance;
            _authService = new AuthService(authLogger);
            services.AddSingleton(_authService);
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
