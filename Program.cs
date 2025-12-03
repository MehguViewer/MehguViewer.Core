using System.Text.Json.Serialization;
using MehguViewer.Core.Backend.Endpoints;
using MehguViewer.Core.Backend.Middleware;
using MehguViewer.Shared.Models;
using MehguViewer.Core.Backend.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);

// Add Embedded PostgreSQL Service (starts first)
builder.Services.AddSingleton<EmbeddedPostgresService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<EmbeddedPostgresService>());

// Add Services
builder.Services.AddSingleton<DynamicRepository>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    var embeddedPg = sp.GetService<EmbeddedPostgresService>();
    return new DynamicRepository(config, loggerFactory, embeddedPg);
});
builder.Services.AddSingleton<IRepository>(sp => sp.GetRequiredService<DynamicRepository>());

// Repository initializer - runs after embedded postgres is ready
builder.Services.AddHostedService<RepositoryInitializerService>();

builder.Services.AddSingleton<JobService>();
builder.Services.AddHttpClient();
builder.Services.AddHostedService<IngestionWorker>();
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
});

// Configure Data Protection
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo("keys"))
    .SetApplicationName("MehguViewer");

// Configure JSON (AOT)
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});

// Configure Auth
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = AuthService.GetValidationParameters();
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/api/v1/assets"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("MvnRead", policy => policy.RequireClaim("scope", "mvn:read")); // Note: This is a simple check, real scope parsing might need to split string
    options.AddPolicy("MvnSocial", policy => policy.RequireAssertion(context => 
        context.User.HasClaim(c => c.Type == "scope" && c.Value.Contains("mvn:social:write"))));
    options.AddPolicy("MvnIngest", policy => policy.RequireAssertion(context => 
        context.User.HasClaim(c => c.Type == "scope" && c.Value.Contains("mvn:ingest"))));
    options.AddPolicy("MvnAdmin", policy => policy.RequireAssertion(context => 
        context.User.HasClaim(c => c.Type == "scope" && c.Value.Contains("mvn:admin"))));
});

// Configure CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    });
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseResponseCompression();
app.UseCors();

app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

// Network Policy Middleware
app.UseMiddleware<ServerTimingMiddleware>();
app.UseMiddleware<RateLimitingMiddleware>();

// Security Headers
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    // CSP is complex for Blazor WASM, skipping for now to avoid breakage
    await next();
});

app.UseAuthentication();
app.UseAuthorization();

// Map Endpoints
app.MapSystemEndpoints();
app.MapSeriesEndpoints();
app.MapUserEndpoints();
app.MapAssetEndpoints();
app.MapIngestEndpoints();
app.MapJobEndpoints();
app.MapSocialEndpoints();
app.MapCollectionEndpoints();
app.MapDebugEndpoints();

app.MapFallbackToFile("index.html");

app.Run();

[JsonSerializable(typeof(NodeManifest))]
[JsonSerializable(typeof(NodeMetadata))]
[JsonSerializable(typeof(TaxonomyData))]
[JsonSerializable(typeof(SearchResults))]
[JsonSerializable(typeof(Series))]
[JsonSerializable(typeof(SeriesCreate))]
[JsonSerializable(typeof(SeriesListResponse))]
[JsonSerializable(typeof(Unit))]
[JsonSerializable(typeof(UnitCreate))]
[JsonSerializable(typeof(UnitListResponse))]
[JsonSerializable(typeof(Page))]
[JsonSerializable(typeof(IEnumerable<Page>))]
[JsonSerializable(typeof(List<Page>))]
[JsonSerializable(typeof(JobResponse))]
[JsonSerializable(typeof(ProgressUpdate))]
[JsonSerializable(typeof(ReadingProgress))]
[JsonSerializable(typeof(HistoryListResponse))]
[JsonSerializable(typeof(HistoryBatchImport))]
[JsonSerializable(typeof(ReadingProgress[]))]
[JsonSerializable(typeof(CommentListResponse))]
[JsonSerializable(typeof(CommentCreate))]
[JsonSerializable(typeof(Comment))]
[JsonSerializable(typeof(Vote))]
[JsonSerializable(typeof(Collection[]))]
[JsonSerializable(typeof(Collection))]
[JsonSerializable(typeof(CollectionCreate))]
[JsonSerializable(typeof(CollectionItemAdd))]
[JsonSerializable(typeof(SystemConfig))]
[JsonSerializable(typeof(SystemStats))]
[JsonSerializable(typeof(Report))]
[JsonSerializable(typeof(Job))]
[JsonSerializable(typeof(Problem))]
[JsonSerializable(typeof(AdminPasswordRequest))]
[JsonSerializable(typeof(DatabaseConfig))]
[JsonSerializable(typeof(DatabaseSetupRequest))]
[JsonSerializable(typeof(DatabaseTestResponse))]
[JsonSerializable(typeof(SetupStatusResponse))]
[JsonSerializable(typeof(DebugResponse))]
[JsonSerializable(typeof(ResetRequest))]
[JsonSerializable(typeof(ResetResponse))]
[JsonSerializable(typeof(User))]
[JsonSerializable(typeof(UserCreate))]
[JsonSerializable(typeof(LoginRequest))]
[JsonSerializable(typeof(LoginResponse))]
[JsonSerializable(typeof(IEnumerable<User>))]
internal partial class AppJsonSerializerContext : JsonSerializerContext
{

}

public partial class Program { }
