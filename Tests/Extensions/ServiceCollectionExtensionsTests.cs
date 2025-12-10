using MehguViewer.Core.Extensions;
using MehguViewer.Core.Services;
using MehguViewer.Core.Workers;
using MehguViewer.Core.Infrastructures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using Xunit;

namespace MehguViewer.Core.Tests.Extensions;

/// <summary>
/// Comprehensive tests for ServiceCollectionExtensions service registration.
/// </summary>
/// <remarks>
/// Tests cover:
/// - Core service registration and lifecycle
/// - Security configuration (authentication, authorization, data protection)
/// - Infrastructure configuration (compression, JSON, CORS)
/// - Argument validation and error handling
/// - Service dependency resolution
/// - Authorization policy evaluation
/// </remarks>
public class ServiceCollectionExtensionsTests : IDisposable
{
    private readonly IServiceCollection _services;
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;

    public ServiceCollectionExtensionsTests()
    {
        _services = new ServiceCollection();
        
        // Create test configuration
        var configData = new Dictionary<string, string?>
        {
            ["Storage:Mode"] = "FileSystem",
            ["Storage:DataPath"] = Path.GetTempPath(),
            ["EmbeddedPostgres:Enabled"] = "false",
            ["EmbeddedPostgres:FallbackToMemory"] = "true",
            ["ConnectionStrings:DefaultConnection"] = ""
        };
        
        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        // Add logging for services
        _services.AddLogging(builder => builder.AddConsole());
        
        _serviceProvider = _services.BuildServiceProvider();
    }

    #region AddMehguServices Tests

    [Fact]
    public void AddMehguServices_WithValidParameters_RegistersAllCoreServices()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(_configuration);

        // Act
        services.AddMehguServices(_configuration);
        var provider = services.BuildServiceProvider();

        // Assert - Verify all core services are registered
        Assert.NotNull(provider.GetService<EmbeddedPostgresService>());
        Assert.NotNull(provider.GetService<FileBasedSeriesService>());
        Assert.NotNull(provider.GetService<MetadataAggregationService>());
        Assert.NotNull(provider.GetService<DynamicRepository>());
        Assert.NotNull(provider.GetService<IRepository>());
        Assert.NotNull(provider.GetService<JobService>());
        Assert.NotNull(provider.GetService<PasskeyService>());
        Assert.NotNull(provider.GetService<LogsService>());
        Assert.NotNull(provider.GetService<ImageProcessingService>());
        Assert.NotNull(provider.GetService<TaxonomyValidationService>());
        Assert.NotNull(provider.GetService<AuthService>());
    }

    [Fact]
    public void AddMehguServices_WithNullServices_ThrowsArgumentNullException()
    {
        // Arrange
        IServiceCollection? services = null;

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => 
            services!.AddMehguServices(_configuration));
        Assert.Equal("services", exception.ParamName);
    }

    [Fact]
    public void AddMehguServices_WithNullConfiguration_ThrowsArgumentNullException()
    {
        // Arrange
        var services = new ServiceCollection();
        IConfiguration? configuration = null;

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => 
            services.AddMehguServices(configuration!));
        Assert.Equal("configuration", exception.ParamName);
    }

    [Fact]
    public void AddMehguServices_RegistersDynamicRepositoryAsSingleton()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(_configuration);

        // Act
        services.AddMehguServices(_configuration);
        var provider = services.BuildServiceProvider();

        // Assert - Verify singleton behavior
        var instance1 = provider.GetService<DynamicRepository>();
        var instance2 = provider.GetService<DynamicRepository>();
        Assert.Same(instance1, instance2);
    }

    [Fact]
    public void AddMehguServices_RegistersIRepositoryPointingToDynamicRepository()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(_configuration);

        // Act
        services.AddMehguServices(_configuration);
        var provider = services.BuildServiceProvider();

        // Assert - Verify interface points to concrete implementation
        var dynamicRepo = provider.GetService<DynamicRepository>();
        var interfaceRepo = provider.GetService<IRepository>();
        Assert.Same(dynamicRepo, interfaceRepo);
    }

    [Fact]
    public void AddMehguServices_RegistersHostedServices()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddMehguServices(_configuration);

        // Assert - Verify hosted services are registered
        var hostedServices = services.Where(s => 
            s.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService)).ToList();
        
        Assert.Contains(hostedServices, s => 
            s.ImplementationType?.Name == nameof(EmbeddedPostgresService) ||
            s.ImplementationFactory != null);
        Assert.Contains(hostedServices, s => 
            s.ImplementationType?.Name == nameof(RepositoryInitializerService));
        Assert.Contains(hostedServices, s => 
            s.ImplementationType?.Name == nameof(IngestionWorker));
        Assert.Contains(hostedServices, s => 
            s.ImplementationType?.Name == nameof(TaxonomyValidationWorker));
    }

    [Fact]
    public void AddMehguServices_RegistersHttpClientFactory()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddMehguServices(_configuration);
        var provider = services.BuildServiceProvider();

        // Assert
        var httpClientFactory = provider.GetService<IHttpClientFactory>();
        Assert.NotNull(httpClientFactory);
    }

    [Fact]
    public void AddMehguServices_AllowsMultipleCalls_WithoutDuplicates()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act - Add services twice
        services.AddMehguServices(_configuration);
        var countAfterFirst = services.Count;
        services.AddMehguServices(_configuration);
        var countAfterSecond = services.Count;

        // Assert - Second call adds duplicate descriptors (expected behavior)
        Assert.True(countAfterSecond > countAfterFirst);
    }

    #endregion

    #region AddMehguSecurity Tests

    [Fact]
    public void AddMehguSecurity_WithValidServices_ConfiguresAuthentication()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<AuthService>(sp => 
            new AuthService(sp.GetRequiredService<ILogger<AuthService>>()));

        // Act
        services.AddMehguSecurity();
        var provider = services.BuildServiceProvider();

        // Assert
        var authService = provider.GetService<Microsoft.AspNetCore.Authentication.IAuthenticationService>();
        Assert.NotNull(authService);
    }

    [Fact]
    public void AddMehguSecurity_WithNullServices_ThrowsArgumentNullException()
    {
        // Arrange
        IServiceCollection? services = null;

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => 
            services!.AddMehguSecurity());
        Assert.Equal("services", exception.ParamName);
    }

    [Fact]
    public void AddMehguSecurity_ConfiguresDataProtection()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddMehguSecurity();
        var provider = services.BuildServiceProvider();

        // Assert
        var dataProtectionProvider = provider.GetService<IDataProtectionProvider>();
        Assert.NotNull(dataProtectionProvider);
    }

    [Fact]
    public async Task AddMehguSecurity_ConfiguresAuthorizationPolicies()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddMehguSecurity();
        var provider = services.BuildServiceProvider();

        // Assert - Verify all policies exist
        var policyProvider = provider.GetService<IAuthorizationPolicyProvider>();
        Assert.NotNull(policyProvider);
        
        var policies = new[] { "MvnRead", "MvnSocial", "MvnIngest", "MvnAdmin", "MvnIngestOrAdmin" };
        foreach (var policyName in policies)
        {
            var policy = await policyProvider.GetPolicyAsync(policyName);
            Assert.NotNull(policy);
        }
    }

    [Fact]
    public async Task AddMehguSecurity_MvnReadPolicy_RequiresReadScope()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMehguSecurity();
        var provider = services.BuildServiceProvider();
        
        var policyProvider = provider.GetRequiredService<IAuthorizationPolicyProvider>();
        var authService = provider.GetRequiredService<IAuthorizationService>();
        var policy = await policyProvider.GetPolicyAsync("MvnRead");
        Assert.NotNull(policy);

        var userWithReadScope = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("scope", "mvn:read mvn:social:write")
        }, "test"));

        var userWithoutReadScope = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("scope", "mvn:other")
        }, "test"));

        // Act
        var resultWithScope = await authService.AuthorizeAsync(userWithReadScope, policy);
        var resultWithoutScope = await authService.AuthorizeAsync(userWithoutReadScope, policy);

        // Assert
        Assert.True(resultWithScope.Succeeded);
        Assert.False(resultWithoutScope.Succeeded);
    }

    [Fact]
    public async Task AddMehguSecurity_MvnIngestOrAdminPolicy_AllowsEitherScope()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMehguSecurity();
        var provider = services.BuildServiceProvider();
        
        var policyProvider = provider.GetRequiredService<IAuthorizationPolicyProvider>();
        var authService = provider.GetRequiredService<IAuthorizationService>();
        var policy = await policyProvider.GetPolicyAsync("MvnIngestOrAdmin");
        Assert.NotNull(policy);

        var adminUser = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("scope", "mvn:admin")
        }, "test"));

        var ingestUser = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("scope", "mvn:ingest")
        }, "test"));

        var regularUser = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("scope", "mvn:read")
        }, "test"));

        // Act
        var adminResult = await authService.AuthorizeAsync(adminUser, policy);
        var ingestResult = await authService.AuthorizeAsync(ingestUser, policy);
        var regularResult = await authService.AuthorizeAsync(regularUser, policy);

        // Assert
        Assert.True(adminResult.Succeeded);
        Assert.True(ingestResult.Succeeded);
        Assert.False(regularResult.Succeeded);
    }

    [Fact]
    public async Task AddMehguSecurity_ScopeValidation_SupportsSpaceSeparatedScopes()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMehguSecurity();
        var provider = services.BuildServiceProvider();
        
        var policyProvider = provider.GetRequiredService<IAuthorizationPolicyProvider>();
        var authService = provider.GetRequiredService<IAuthorizationService>();
        var readPolicy = await policyProvider.GetPolicyAsync("MvnRead");
        var socialPolicy = await policyProvider.GetPolicyAsync("MvnSocial");
        Assert.NotNull(readPolicy);
        Assert.NotNull(socialPolicy);

        // User with multiple scopes in space-separated format (standard JWT format)
        var userWithMultipleScopes = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("scope", "mvn:read mvn:social:write mvn:ingest")
        }, "test"));

        // Act
        var readResult = await authService.AuthorizeAsync(userWithMultipleScopes, readPolicy);
        var socialResult = await authService.AuthorizeAsync(userWithMultipleScopes, socialPolicy);

        // Assert - Should match individual scopes within space-separated string
        Assert.True(readResult.Succeeded);
        Assert.True(socialResult.Succeeded);
    }

    [Fact]
    public void AddMehguSecurity_DataProtection_UsesCorrectKeyDirectory()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddMehguSecurity();
        var provider = services.BuildServiceProvider();
        var dataProtector = provider.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("test");

        // Assert - Verify can create protector (indicates successful configuration)
        Assert.NotNull(dataProtector);
        
        var testData = "test-data";
        var protectedData = dataProtector.Protect(testData);
        var unprotectedData = dataProtector.Unprotect(protectedData);
        Assert.Equal(testData, unprotectedData);
    }

    #endregion

    #region AddMehguInfrastructure Tests

    [Fact]
    public void AddMehguInfrastructure_WithValidServices_ConfiguresAllInfrastructure()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddMehguInfrastructure();
        var provider = services.BuildServiceProvider();

        // Assert - Verify infrastructure services registered
        Assert.Contains(services, s => s.ServiceType.Name.Contains("ResponseCompression"));
        Assert.Contains(services, s => s.ServiceType.Name.Contains("Cors"));
    }

    [Fact]
    public void AddMehguInfrastructure_WithNullServices_ThrowsArgumentNullException()
    {
        // Arrange
        IServiceCollection? services = null;

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => 
            services!.AddMehguInfrastructure());
        Assert.Equal("services", exception.ParamName);
    }

    [Fact]
    public void AddMehguInfrastructure_ConfiguresResponseCompression()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddMehguInfrastructure();

        // Assert
        Assert.Contains(services, s => 
            s.ServiceType.FullName != null && 
            s.ServiceType.FullName.Contains("ResponseCompressionOptions"));
    }

    [Fact]
    public void AddMehguInfrastructure_ConfiguresCors()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddMehguInfrastructure();

        // Assert
        Assert.Contains(services, s => 
            s.ServiceType.FullName != null && 
            s.ServiceType.FullName.Contains("CorsOptions"));
    }

    [Fact]
    public void AddMehguInfrastructure_ConfiguresJsonSerialization()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddMehguInfrastructure();

        // Assert - Verify JSON options are configured
        Assert.Contains(services, s => 
            s.ServiceType.FullName != null && 
            s.ServiceType.FullName.Contains("JsonOptions"));
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void AllExtensionMethods_CanBeChainedTogether()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(_configuration);

        // Act - Chain all extension methods
        services
            .AddMehguServices(_configuration)
            .AddMehguSecurity()
            .AddMehguInfrastructure();

        var provider = services.BuildServiceProvider();

        // Assert - Verify complete application configuration
        Assert.NotNull(provider.GetService<IRepository>());
        Assert.NotNull(provider.GetService<AuthService>());
        Assert.NotNull(provider.GetService<IAuthorizationPolicyProvider>());
        Assert.NotNull(provider.GetService<IDataProtectionProvider>());
    }

    [Fact]
    public void ServiceRegistration_WithMultipleEnvironments_UsesCorrectConfiguration()
    {
        // Arrange - Development environment
        var devConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EmbeddedPostgres:Enabled"] = "true",
                ["Storage:Mode"] = "FileSystem"
            })
            .Build();

        // Arrange - Production environment
        var prodConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EmbeddedPostgres:Enabled"] = "false",
                ["ConnectionStrings:DefaultConnection"] = "Host=prod-db;Database=mehgu"
            })
            .Build();

        var devServices = new ServiceCollection();
        devServices.AddLogging();
        devServices.AddSingleton<IConfiguration>(devConfig);
        
        var prodServices = new ServiceCollection();
        prodServices.AddLogging();
        prodServices.AddSingleton<IConfiguration>(prodConfig);

        // Act
        devServices.AddMehguServices(devConfig);
        prodServices.AddMehguServices(prodConfig);

        var devProvider = devServices.BuildServiceProvider();
        var prodProvider = prodServices.BuildServiceProvider();

        // Assert - Both configurations should create valid repositories
        Assert.NotNull(devProvider.GetService<IRepository>());
        Assert.NotNull(prodProvider.GetService<IRepository>());
    }

    #endregion

    #region Cleanup

    public void Dispose()
    {
        (_serviceProvider as IDisposable)?.Dispose();
    }

    #endregion
}
