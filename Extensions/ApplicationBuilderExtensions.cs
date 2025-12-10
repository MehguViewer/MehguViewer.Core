using MehguViewer.Core.Middlewares;
using MehguViewer.Core.Services;
using MehguViewer.Core.Endpoints;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.StaticFiles;

namespace MehguViewer.Core.Extensions;

/// <summary>
/// Extension methods for configuring the application request pipeline and endpoint routing
/// in the MehguViewer Core application.
/// </summary>
/// <remarks>
/// <para><strong>Middleware Pipeline Order:</strong></para>
/// <list type="number">
///   <item><description>In-memory logging provider initialization</description></item>
///   <item><description>HTTPS redirection and HSTS (production only)</description></item>
///   <item><description>Response compression and CORS</description></item>
///   <item><description>Performance monitoring (ServerTiming, RateLimiting)</description></item>
///   <item><description>Static files (Blazor framework, wwwroot, covers)</description></item>
///   <item><description>Security headers for API routes</description></item>
///   <item><description>Authentication and Authorization</description></item>
/// </list>
/// 
/// <para><strong>Security Features:</strong></para>
/// <list type="bullet">
///   <item><description>HSTS and HTTPS redirection in production</description></item>
///   <item><description>Content Security Policy headers</description></item>
///   <item><description>JWT-based authentication with RFC 7807 error formatting</description></item>
///   <item><description>Rate limiting to prevent abuse</description></item>
///   <item><description>Proper MIME type configuration to prevent XSS</description></item>
/// </list>
/// </remarks>
public static class ApplicationBuilderExtensions
{
    #region Constants

    /// <summary>
    /// Default maximum age for immutable framework file caching (1 year in seconds).
    /// </summary>
    private const int FrameworkFileCacheMaxAge = 31536000;

    /// <summary>
    /// Default maximum age for cover image caching (24 hours in seconds).
    /// </summary>
    private const int CoverImageCacheMaxAge = 86400;

    /// <summary>
    /// Relative path to the covers directory within the content root.
    /// </summary>
    private const string CoversDirectoryPath = "data/covers";

    /// <summary>
    /// Request path prefix for serving cover images.
    /// </summary>
    private const string CoversRequestPath = "/covers";

    /// <summary>
    /// Request path prefix for API endpoints.
    /// </summary>
    private const string ApiRequestPath = "/api";

    /// <summary>
    /// Request path prefix for Blazor framework files.
    /// </summary>
    private const string FrameworkRequestPath = "/_framework";

    #endregion

    #region Middleware Configuration

    /// <summary>
    /// Configures the MehguViewer middleware pipeline with security, performance monitoring,
    /// static file serving, and authentication/authorization.
    /// </summary>
    /// <param name="app">The application builder instance.</param>
    /// <param name="env">The web hosting environment information.</param>
    /// <returns>The configured application builder for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when app or env is null.</exception>
    /// <remarks>
    /// This method orchestrates the middleware pipeline in a specific order to ensure
    /// proper request processing, security, and performance optimization.
    /// </remarks>
    public static IApplicationBuilder UseMehguMiddleware(this IApplicationBuilder app, IWebHostEnvironment env)
    {
        if (app == null)
            throw new ArgumentNullException(nameof(app));
        if (env == null)
            throw new ArgumentNullException(nameof(env));

        var logger = app.ApplicationServices.GetRequiredService<ILogger<IApplicationBuilder>>();
        
        logger.LogInformation("Configuring MehguViewer middleware pipeline for {Environment} environment", env.EnvironmentName);

        // Initialize in-memory logging provider for admin console
        ConfigureLoggingProvider(app, logger);

        // Configure production-specific middleware
        ConfigureProductionMiddleware(app, env, logger);

        // Configure performance and security middleware
        ConfigurePerformanceMiddleware(app, logger);
        
        // Configure WebOptimizer for asset optimization
        ConfigureWebOptimizer(app, logger);

        // Configure static file serving with security
        var contentTypeProvider = CreateContentTypeProvider();
        ConfigureBlazorStaticFiles(app, contentTypeProvider, logger);
        ConfigureWwwrootStaticFiles(app, contentTypeProvider, logger);
        ConfigureCoverImagesStaticFiles(app, env, contentTypeProvider, logger);

        // Configure security headers for API routes
        ConfigureApiSecurityHeaders(app, logger);

        // Configure authentication and authorization
        ConfigureAuthentication(app, logger);

        logger.LogInformation("MehguViewer middleware pipeline configuration completed successfully");

        return app;
    }

    #endregion

    #region Endpoint Mapping

    /// <summary>
    /// Maps all MehguViewer API endpoints and configures the fallback route for the Blazor SPA.
    /// </summary>
    /// <param name="app">The web application instance.</param>
    /// <returns>The configured web application for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when app is null.</exception>
    /// <remarks>
    /// Endpoints are mapped in a logical order: system configuration first, then content,
    /// social features, and finally debugging endpoints.
    /// </remarks>
    public static WebApplication MapMehguEndpoints(this WebApplication app)
    {
        if (app == null)
            throw new ArgumentNullException(nameof(app));

        var logger = app.Services.GetRequiredService<ILogger<WebApplication>>();
        
        logger.LogInformation("Mapping MehguViewer API endpoints");

        try
        {
            // Initialize system-level configuration
            InitializeSystemConfiguration(app, logger);

            // Map all endpoint groups
            MapEndpointGroups(app, logger);

            // Configure SPA fallback
            app.MapFallbackToFile("index.html");

            logger.LogInformation("Successfully mapped all MehguViewer endpoints");
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Failed to map MehguViewer endpoints");
            throw;
        }

        return app;
    }

    #endregion

    #region Private Helper Methods - Middleware Configuration

    /// <summary>
    /// Configures the in-memory logging provider for admin log viewing.
    /// </summary>
    private static void ConfigureLoggingProvider(IApplicationBuilder app, ILogger logger)
    {
        logger.LogDebug("Initializing in-memory logging provider");

        try
        {
            var logsService = app.ApplicationServices.GetRequiredService<LogsService>();
            var loggerFactory = app.ApplicationServices.GetRequiredService<ILoggerFactory>();
            loggerFactory.AddProvider(new InMemoryLoggerProvider(logsService));

            logger.LogDebug("In-memory logging provider initialized successfully");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialize in-memory logging provider");
            throw;
        }
    }

    /// <summary>
    /// Configures production-specific middleware (HSTS and HTTPS redirection).
    /// </summary>
    private static void ConfigureProductionMiddleware(IApplicationBuilder app, IWebHostEnvironment env, ILogger logger)
    {
        if (!env.IsDevelopment())
        {
            logger.LogInformation("Enabling production security features (HSTS, HTTPS redirection)");
            app.UseHsts();
            app.UseHttpsRedirection();
        }
        else
        {
            logger.LogDebug("Development mode: Skipping HSTS and HTTPS redirection");
        }

        app.UseResponseCompression();
        app.UseCors();
        
        logger.LogDebug("Response compression and CORS enabled");
    }

    /// <summary>
    /// Configures performance monitoring middleware.
    /// </summary>
    private static void ConfigurePerformanceMiddleware(IApplicationBuilder app, ILogger logger)
    {
        logger.LogDebug("Configuring performance monitoring middleware");

        app.UseMiddleware<ServerTimingMiddleware>();
        app.UseMiddleware<RateLimitingMiddleware>();

        logger.LogDebug("Performance monitoring middleware configured (ServerTiming, RateLimiting)");
    }

    /// <summary>
    /// Configures WebOptimizer middleware for asset bundling and minification.
    /// </summary>
    private static void ConfigureWebOptimizer(IApplicationBuilder app, ILogger logger)
    {
        logger.LogDebug("Enabling WebOptimizer middleware");
        
        app.UseWebOptimizer();
        
        logger.LogDebug("WebOptimizer middleware enabled for CSS/JS optimization");
    }

    /// <summary>
    /// Creates and configures a content type provider with all necessary MIME type mappings.
    /// </summary>
    private static FileExtensionContentTypeProvider CreateContentTypeProvider()
    {
        var provider = new FileExtensionContentTypeProvider();
        
        // Blazor-specific MIME types
        provider.Mappings[".styles.css"] = "text/css";
        provider.Mappings[".wasm"] = "application/wasm";
        provider.Mappings[".dat"] = "application/octet-stream";
        provider.Mappings[".blat"] = "application/octet-stream";
        
        // Standard web MIME types
        provider.Mappings[".json"] = "application/json";
        provider.Mappings[".js"] = "application/javascript";

        return provider;
    }

    /// <summary>
    /// Configures static file serving for Blazor WebAssembly framework files.
    /// </summary>
    private static void ConfigureBlazorStaticFiles(IApplicationBuilder app, FileExtensionContentTypeProvider provider, ILogger logger)
    {
        logger.LogDebug("Configuring Blazor framework static files");

        // CRITICAL: Must be configured before security headers to prevent CSP blocking
        app.UseBlazorFrameworkFiles();

        logger.LogDebug("Blazor framework files configured successfully");
    }

    /// <summary>
    /// Configures static file serving for wwwroot with proper MIME types and caching.
    /// </summary>
    private static void ConfigureWwwrootStaticFiles(IApplicationBuilder app, FileExtensionContentTypeProvider provider, ILogger logger)
    {
        logger.LogDebug("Configuring wwwroot static files with caching and security headers");

        app.UseStaticFiles(new StaticFileOptions
        {
            ContentTypeProvider = provider,
            OnPrepareResponse = ctx =>
            {
                if (ctx.Context.Request.Path.StartsWithSegments(FrameworkRequestPath))
                {
                    // Aggressive caching for immutable framework files
                    ctx.Context.Response.Headers["Cache-Control"] = 
                        $"public, max-age={FrameworkFileCacheMaxAge}, immutable";
                }
                else
                {
                    // Security headers for other static files
                    ctx.Context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                }
            }
        });

        logger.LogDebug("Wwwroot static files configured with proper MIME types and caching");
    }

    /// <summary>
    /// Configures static file serving for cover images from the data/covers directory.
    /// </summary>
    private static void ConfigureCoverImagesStaticFiles(
        IApplicationBuilder app, 
        IWebHostEnvironment env, 
        FileExtensionContentTypeProvider provider, 
        ILogger logger)
    {
        var coversPath = Path.Combine(env.ContentRootPath, CoversDirectoryPath);

        try
        {
            // Ensure covers directory exists
            if (!Directory.Exists(coversPath))
            {
                logger.LogInformation("Creating covers directory at {Path}", coversPath);
                Directory.CreateDirectory(coversPath);
            }

            logger.LogDebug("Configuring cover images static files from {Path}", coversPath);

            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(coversPath),
                RequestPath = CoversRequestPath,
                ContentTypeProvider = provider,
                OnPrepareResponse = ctx =>
                {
                    ctx.Context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                    ctx.Context.Response.Headers["Cache-Control"] = $"public, max-age={CoverImageCacheMaxAge}";
                }
            });

            logger.LogDebug("Cover images static files configured successfully");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to configure cover images static files at {Path}", coversPath);
            throw;
        }
    }

    /// <summary>
    /// Configures security headers for API endpoints.
    /// </summary>
    private static void ConfigureApiSecurityHeaders(IApplicationBuilder app, ILogger logger)
    {
        logger.LogDebug("Configuring security headers for API endpoints");

        app.Use(async (context, next) =>
        {
            // Only apply to API routes, static files are already handled
            if (!context.Response.HasStarted && 
                context.Request.Path.StartsWithSegments(ApiRequestPath))
            {
                context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                context.Response.Headers["X-Frame-Options"] = "DENY";
                context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            }
            await next();
        });

        logger.LogDebug("API security headers configured successfully");
    }

    /// <summary>
    /// Configures authentication and authorization middleware with RFC 7807 error formatting.
    /// </summary>
    private static void ConfigureAuthentication(IApplicationBuilder app, ILogger logger)
    {
        logger.LogDebug("Configuring authentication and authorization middleware");

        app.UseAuthentication();
        app.UseMiddleware<JwtMiddleware>(); // Handles 401/403 with RFC 7807
        app.UseAuthorization();

        logger.LogDebug("Authentication and authorization configured successfully");
    }

    #endregion

    #region Private Helper Methods - Endpoint Mapping

    /// <summary>
    /// Initializes system-level configuration before mapping endpoints.
    /// </summary>
    private static void InitializeSystemConfiguration(WebApplication app, ILogger logger)
    {
        logger.LogDebug("Initializing system configuration");

        try
        {
            SystemEndpoints.InitializeStorageSettings(app.Configuration);
            logger.LogDebug("Storage settings initialized successfully");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialize storage settings");
            throw;
        }
    }

    /// <summary>
    /// Maps all endpoint groups in the correct order.
    /// </summary>
    private static void MapEndpointGroups(WebApplication app, ILogger logger)
    {
        logger.LogDebug("Mapping endpoint groups");

        // System and configuration endpoints
        app.MapAuthEndpoints();
        app.MapSystemEndpoints();

        // Content management endpoints
        app.MapSeriesEndpoints();
        app.MapAssetEndpoints();
        app.MapIngestEndpoints();

        // User and social features
        app.MapUserEndpoints();
        app.MapSocialEndpoints();
        app.MapCollectionEndpoints();

        // Background jobs and debugging
        app.MapJobEndpoints();
        app.MapDebugEndpoints();

        logger.LogDebug("All endpoint groups mapped successfully");
    }

    #endregion
}
