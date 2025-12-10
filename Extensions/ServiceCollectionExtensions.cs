using MehguViewer.Core.Services;
using MehguViewer.Core.Workers;
using MehguViewer.Core.Helpers;
using MehguViewer.Core.Infrastructures;
using MehguViewer.Core.Shared;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;

namespace MehguViewer.Core.Extensions;

/// <summary>
/// Provides extension methods for configuring MehguViewer services in the dependency injection container.
/// </summary>
/// <remarks>
/// This class configures three main areas:
/// - Core application services (repositories, workers, business logic)
/// - Security services (authentication, authorization, data protection)
/// - Infrastructure services (compression, serialization, CORS)
/// </remarks>
public static class ServiceCollectionExtensions
{
    #region Constants

    /// <summary>Application name for data protection key isolation.</summary>
    private const string ApplicationName = "MehguViewer";
    
    /// <summary>Directory for persisting data protection keys.</summary>
    private const string KeysDirectory = "keys";
    
    /// <summary>Scope claim type for JWT authorization.</summary>
    private const string ScopeClaimType = "scope";
    
    /// <summary>Read access scope value.</summary>
    private const string ReadScope = "mvn:read";
    
    /// <summary>Social write access scope value.</summary>
    private const string SocialWriteScope = "mvn:social:write";
    
    /// <summary>Ingest access scope value.</summary>
    private const string IngestScope = "mvn:ingest";
    
    /// <summary>Admin access scope value.</summary>
    private const string AdminScope = "mvn:admin";
    
    /// <summary>Asset endpoint path prefix for query string token support.</summary>
    private const string AssetEndpointPath = "/api/v1/assets";

    #endregion

    #region Core Services Registration

    /// <summary>
    /// Registers all core MehguViewer application services.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configuration">Application configuration for service initialization.</param>
    /// <returns>The service collection for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when services or configuration is null.</exception>
    /// <remarks>
    /// Services are registered in dependency order:
    /// 1. Database infrastructure (Embedded PostgreSQL)
    /// 2. Core business services (File, Metadata, Repository)
    /// 3. Application services (Jobs, Auth, Images, Taxonomy)
    /// 4. Background workers (Ingestion, Validation)
    /// </remarks>
    public static IServiceCollection AddMehguServices(
        this IServiceCollection services, 
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var logger = CreateBootstrapLogger();
        
        try
        {
            logger.LogInformation("Registering MehguViewer core services");

            // Database Infrastructure - Register and start embedded PostgreSQL
            RegisterDatabaseServices(services, logger);

            // Core Business Services - File handling and metadata aggregation
            RegisterCoreServices(services, logger);

            // Repository Layer - Data access abstraction
            RegisterRepositoryServices(services, logger);

            // Application Services - Business logic and domain services
            RegisterApplicationServices(services, logger);

            // Background Workers - Async processing tasks
            RegisterBackgroundWorkers(services, logger);

            logger.LogInformation("Successfully registered {ServiceCount} MehguViewer core services", 
                GetServiceCount(services));

            return services;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to register MehguViewer core services");
            throw;
        }
    }

    /// <summary>
    /// Registers database infrastructure services including embedded PostgreSQL.
    /// </summary>
    private static void RegisterDatabaseServices(IServiceCollection services, ILogger logger)
    {
        logger.LogDebug("Registering database services");
        
        // Embedded PostgreSQL must be singleton and registered as hosted service
        services.AddSingleton<EmbeddedPostgresService>();
        services.AddHostedService(sp => sp.GetRequiredService<EmbeddedPostgresService>());
        
        logger.LogDebug("Registered EmbeddedPostgresService as singleton and hosted service");
    }

    /// <summary>
    /// Registers core business services for file and metadata operations.
    /// </summary>
    private static void RegisterCoreServices(IServiceCollection services, ILogger logger)
    {
        logger.LogDebug("Registering core business services");
        
        services.AddSingleton<FileBasedSeriesService>();
        services.AddSingleton<MetadataAggregationService>();
        
        logger.LogDebug("Registered FileBasedSeriesService and MetadataAggregationService");
    }

    /// <summary>
    /// Registers repository services with dynamic backend selection.
    /// </summary>
    private static void RegisterRepositoryServices(IServiceCollection services, ILogger logger)
    {
        logger.LogDebug("Registering repository services with dynamic backend");
        
        // DynamicRepository requires explicit factory to resolve dependencies
        services.AddSingleton<DynamicRepository>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var embeddedPg = sp.GetService<EmbeddedPostgresService>();
            var fileService = sp.GetRequiredService<FileBasedSeriesService>();
            var metadataService = sp.GetRequiredService<MetadataAggregationService>();
            
            return new DynamicRepository(config, loggerFactory, embeddedPg, fileService, metadataService);
        });
        
        // Register interface pointing to concrete implementation
        services.AddSingleton<IRepository>(sp => sp.GetRequiredService<DynamicRepository>());

        // Repository initializer ensures schema setup after database is ready
        services.AddHostedService<RepositoryInitializerService>();
        
        logger.LogDebug("Registered DynamicRepository with IRepository interface and initializer");
    }

    /// <summary>
    /// Registers application-level services for business logic.
    /// </summary>
    private static void RegisterApplicationServices(IServiceCollection services, ILogger logger)
    {
        logger.LogDebug("Registering application services");
        
        // Job management and scheduling
        services.AddSingleton<JobService>();
        
        // Passkey authentication support
        services.AddSingleton<PasskeyService>();
        
        // In-memory logging aggregation
        services.AddSingleton<LogsService>();
        services.AddSingleton<ILoggerProvider, InMemoryLoggerProvider>();
        
        // Image processing and optimization
        services.AddSingleton<ImageProcessingService>();
        
        // Taxonomy validation and enforcement
        services.AddSingleton<TaxonomyValidationService>();
        
        // JWT authentication and authorization
        services.AddSingleton<AuthService>();
        
        // HTTP client factory for external API calls
        services.AddHttpClient();
        
        logger.LogDebug("Registered 7 application services and HTTP client factory");
    }

    /// <summary>
    /// Registers background worker services for async processing.
    /// </summary>
    private static void RegisterBackgroundWorkers(IServiceCollection services, ILogger logger)
    {
        logger.LogDebug("Registering background workers");
        
        services.AddHostedService<IngestionWorker>();
        services.AddHostedService<TaxonomyValidationWorker>();
        
        logger.LogDebug("Registered IngestionWorker and TaxonomyValidationWorker");
    }

    #endregion

    #region Security Services Registration

    /// <summary>
    /// Registers authentication, authorization, and data protection services.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The service collection for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when services is null.</exception>
    /// <remarks>
    /// Configures:
    /// - Data Protection with file-based key persistence
    /// - JWT Bearer authentication with query string support for assets
    /// - Role-based authorization policies with scope validation
    /// </remarks>
    public static IServiceCollection AddMehguSecurity(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var logger = CreateBootstrapLogger();
        
        try
        {
            logger.LogInformation("Configuring MehguViewer security services");

            // Data Protection - Encrypts sensitive data and manages keys
            ConfigureDataProtection(services, logger);

            // Authentication - JWT Bearer token validation
            ConfigureAuthentication(services, logger);

            // Authorization - Role and scope-based access control
            ConfigureAuthorization(services, logger);

            logger.LogInformation("Successfully configured security services");
            
            return services;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to configure security services");
            throw;
        }
    }

    /// <summary>
    /// Configures ASP.NET Core Data Protection with persistent key storage.
    /// </summary>
    private static void ConfigureDataProtection(IServiceCollection services, ILogger logger)
    {
        logger.LogDebug("Configuring data protection with key directory: {KeyDirectory}", KeysDirectory);
        
        var keyDirectory = new DirectoryInfo(KeysDirectory);
        
        services.AddDataProtection()
            .PersistKeysToFileSystem(keyDirectory)
            .SetApplicationName(ApplicationName);
        
        logger.LogDebug("Data protection configured successfully");
    }

    /// <summary>
    /// Configures JWT Bearer authentication with custom token retrieval.
    /// </summary>
    private static void ConfigureAuthentication(IServiceCollection services, ILogger logger)
    {
        logger.LogDebug("Configuring JWT Bearer authentication");
        
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<AuthService>((options, authService) =>
            {
                // Configure token validation parameters from AuthService
                options.TokenValidationParameters = authService.GetValidationParameters();
                
                // Allow token in query string for asset endpoints (image/video streaming)
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        // Security: Only allow query string tokens for asset endpoints
                        // This enables <img src="/api/v1/assets/...?token=xxx"> scenarios
                        var accessToken = context.Request.Query["token"];
                        var path = context.HttpContext.Request.Path;
                        
                        if (!string.IsNullOrEmpty(accessToken) && 
                            path.StartsWithSegments(AssetEndpointPath))
                        {
                            context.Token = accessToken;
                            
                            var requestId = context.HttpContext.TraceIdentifier;
                            logger.LogDebug("Accepting query string token for asset request {RequestId}", requestId);
                        }
                        
                        return Task.CompletedTask;
                    },
                    OnAuthenticationFailed = context =>
                    {
                        logger.LogWarning("JWT authentication failed for {Path}: {Error}",
                            context.HttpContext.Request.Path,
                            context.Exception.Message);
                        return Task.CompletedTask;
                    }
                };
            });
        
        logger.LogDebug("JWT Bearer authentication configured with query string token support");
    }

    /// <summary>
    /// Configures authorization policies based on JWT scope claims.
    /// </summary>
    private static void ConfigureAuthorization(IServiceCollection services, ILogger logger)
    {
        logger.LogDebug("Configuring authorization policies");
        
        services.AddAuthorization(options =>
        {
            // Read access - Required for viewing content
            options.AddPolicy("MvnRead", policy => 
                policy.RequireAssertion(context => 
                    HasScope(context.User, ReadScope)));
            
            // Social write access - Required for comments, votes, collections
            options.AddPolicy("MvnSocial", policy => 
                policy.RequireAssertion(context => 
                    HasScope(context.User, SocialWriteScope)));
            
            // Ingest access - Required for uploading content
            options.AddPolicy("MvnIngest", policy => 
                policy.RequireAssertion(context => 
                    HasScope(context.User, IngestScope)));
            
            // Admin access - Full system control
            options.AddPolicy("MvnAdmin", policy => 
                policy.RequireAssertion(context => 
                    HasScope(context.User, AdminScope)));
            
            // Combined policy - Allows both admin and uploader access
            options.AddPolicy("MvnIngestOrAdmin", policy => 
                policy.RequireAssertion(context => 
                    HasScope(context.User, AdminScope) || 
                    HasScope(context.User, IngestScope)));
        });
        
        logger.LogDebug("Configured 5 authorization policies: Read, Social, Ingest, Admin, IngestOrAdmin");
    }

    /// <summary>
    /// Checks if a user has a specific scope in their JWT claims.
    /// </summary>
    /// <param name="user">The claims principal to check.</param>
    /// <param name="scope">The scope value to look for.</param>
    /// <returns>True if the user has the scope, otherwise false.</returns>
    /// <remarks>
    /// JWT scope claims are space-separated strings (e.g., "mvn:read mvn:social:write").
    /// This method uses Contains() to match individual scopes within the claim value.
    /// </remarks>
    private static bool HasScope(System.Security.Claims.ClaimsPrincipal user, string scope)
    {
        return user.HasClaim(c => c.Type == ScopeClaimType && c.Value.Contains(scope));
    }

    #endregion

    #region Infrastructure Services Registration

    /// <summary>
    /// Registers infrastructure services for HTTP, JSON serialization, and CORS.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The service collection for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when services is null.</exception>
    /// <remarks>
    /// Configures:
    /// - Response compression for bandwidth optimization
    /// - JSON serialization with AOT-compatible source generation
    /// - CORS with permissive default policy (should be restricted in production)
    /// </remarks>
    public static IServiceCollection AddMehguInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var logger = CreateBootstrapLogger();
        
        try
        {
            logger.LogInformation("Configuring MehguViewer infrastructure services");

            // Response Compression - Reduces bandwidth usage
            ConfigureCompression(services, logger);

            // JSON Serialization - AOT-compatible source generation
            ConfigureJsonSerialization(services, logger);

            // CORS - Cross-Origin Resource Sharing
            ConfigureCors(services, logger);
            
            // WebOptimizer - CSS/JS bundling and minification
            ConfigureWebOptimizer(services, logger);

            logger.LogInformation("Successfully configured infrastructure services");
            
            return services;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to configure infrastructure services");
            throw;
        }
    }

    /// <summary>
    /// Configures HTTP response compression.
    /// </summary>
    private static void ConfigureCompression(IServiceCollection services, ILogger logger)
    {
        logger.LogDebug("Configuring response compression");
        
        services.AddResponseCompression(options =>
        {
            // Enable compression for HTTPS responses (disabled by default for BREACH attack mitigation)
            // Safe for MehguViewer as we don't reflect user input in compressed responses
            options.EnableForHttps = true;
        });
        
        logger.LogDebug("Response compression configured with HTTPS support");
    }

    /// <summary>
    /// Configures JSON serialization with source generation for AOT compatibility.
    /// </summary>
    private static void ConfigureJsonSerialization(IServiceCollection services, ILogger logger)
    {
        logger.LogDebug("Configuring JSON serialization with source generation");
        
        services.ConfigureHttpJsonOptions(options =>
        {
            // Insert AppJsonSerializerContext for AOT-compiled JSON serialization
            // Context is defined in Program.cs with [JsonSerializable] attributes
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
        });
        
        logger.LogDebug("JSON serialization configured with AppJsonSerializerContext");
    }

    /// <summary>
    /// Configures Cross-Origin Resource Sharing (CORS) policies.
    /// </summary>
    private static void ConfigureCors(IServiceCollection services, ILogger logger)
    {
        logger.LogDebug("Configuring CORS policies");
        
        services.AddCors(options =>
        {
            // Default policy allows all origins - SECURITY: Restrict in production
            options.AddDefaultPolicy(policy =>
            {
                policy.AllowAnyOrigin()
                      .AllowAnyMethod()
                      .AllowAnyHeader();
            });
        });
        
        logger.LogWarning("CORS configured with permissive policy (AllowAnyOrigin). " +
                         "Consider restricting origins in production environments");
    }

    /// <summary>
    /// Configures WebOptimizer for CSS and JavaScript bundling and minification.
    /// </summary>
    private static void ConfigureWebOptimizer(IServiceCollection services, ILogger logger)
    {
        logger.LogDebug("Configuring WebOptimizer for asset bundling and minification");
        
        services.AddWebOptimizer(pipeline =>
        {
            // Bundle and minify CSS files from wwwroot/css
            pipeline.AddCssBundle("/css/bundle.css", "css/**/*.css");
            
            // Minify individual JavaScript files
            pipeline.MinifyJsFiles("js/**/*.js");
            
            // Minify individual CSS files  
            pipeline.MinifyCssFiles("css/**/*.css");
            
            logger.LogDebug("WebOptimizer configured: CSS bundling and minification enabled");
        });
        
        logger.LogDebug("WebOptimizer pipeline configured successfully");
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Creates a bootstrap logger for use during service registration.
    /// </summary>
    /// <returns>A logger instance for ServiceCollectionExtensions.</returns>
    private static ILogger CreateBootstrapLogger()
    {
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });
        
        return loggerFactory.CreateLogger(typeof(ServiceCollectionExtensions));
    }

    /// <summary>
    /// Gets an approximate count of registered services for logging purposes.
    /// </summary>
    /// <param name="services">The service collection to count.</param>
    /// <returns>The number of service descriptors in the collection.</returns>
    private static int GetServiceCount(IServiceCollection services)
    {
        return services.Count;
    }

    #endregion
}
