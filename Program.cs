using System.Text.Json.Serialization;
using MehguViewer.Core.Backend.Endpoints;
using MehguViewer.Core.Backend.Middleware;
using MehguViewer.Shared.Models;
using MehguViewer.Core.Backend.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);

// Configure default URLs if not specified
if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
{
    builder.WebHost.UseUrls("http://localhost:6230");
}

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
builder.Services.AddSingleton<PasskeyService>();
builder.Services.AddSingleton<LogsService>();
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
                // Allow token to be passed via query string for asset endpoints
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
    options.AddPolicy("MvnRead", policy => policy.RequireClaim("scope", "mvn:read"));
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

// Add in-memory log provider for admin logs viewing
var logsService = app.Services.GetRequiredService<LogsService>();
var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
loggerFactory.AddProvider(new InMemoryLoggerProvider(logsService));

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}
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
app.MapAuthEndpoints();
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
[JsonSerializable(typeof(JobListResponse))]
[JsonSerializable(typeof(Job))]
[JsonSerializable(typeof(Job[]))]
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
[JsonSerializable(typeof(CollectionUpdate))]
[JsonSerializable(typeof(CollectionItemAdd))]
[JsonSerializable(typeof(SystemConfig))]
[JsonSerializable(typeof(SystemConfigUpdate))]
[JsonSerializable(typeof(SystemStats))]
[JsonSerializable(typeof(StorageStatsResponse))]
[JsonSerializable(typeof(StorageSettingsUpdate))]
[JsonSerializable(typeof(Report))]
[JsonSerializable(typeof(User))]
[JsonSerializable(typeof(User[]))]
[JsonSerializable(typeof(UserCreate))]
[JsonSerializable(typeof(UserUpdate))]
[JsonSerializable(typeof(SeriesUpdate))]
[JsonSerializable(typeof(UnitUpdate))]
[JsonSerializable(typeof(LoginRequest))]
[JsonSerializable(typeof(LoginResponse))]
[JsonSerializable(typeof(Problem))]
[JsonSerializable(typeof(AdminPasswordRequest))]
[JsonSerializable(typeof(DatabaseConfig))]
[JsonSerializable(typeof(DatabaseSetupRequest))]
[JsonSerializable(typeof(DatabaseTestResponse))]
[JsonSerializable(typeof(EmbeddedDatabaseStatus))]
[JsonSerializable(typeof(UseEmbeddedDatabaseRequest))]
[JsonSerializable(typeof(UseEmbeddedDatabaseResponse))]
[JsonSerializable(typeof(SetupStatusResponse))]
[JsonSerializable(typeof(DebugResponse))]
[JsonSerializable(typeof(ResetRequest))]
[JsonSerializable(typeof(ResetResponse))]
[JsonSerializable(typeof(ClearCacheResponse))]
[JsonSerializable(typeof(IEnumerable<User>))]
[JsonSerializable(typeof(AuthConfig))]
[JsonSerializable(typeof(CloudflareConfig))]
[JsonSerializable(typeof(AuthConfigUpdate))]
[JsonSerializable(typeof(CloudflareConfigUpdate))]
[JsonSerializable(typeof(LoginRequestWithCf))]
[JsonSerializable(typeof(RegisterRequestWithCf))]
[JsonSerializable(typeof(AuthConfigPublic))]
[JsonSerializable(typeof(ChangePasswordRequest))]
[JsonSerializable(typeof(UserProfileResponse))]
[JsonSerializable(typeof(Passkey))]
[JsonSerializable(typeof(Passkey[]))]
[JsonSerializable(typeof(PasskeyInfo))]
[JsonSerializable(typeof(PasskeyInfo[]))]
[JsonSerializable(typeof(PasskeyRegistrationOptionsRequest))]
[JsonSerializable(typeof(PasskeyRegistrationOptions))]
[JsonSerializable(typeof(PasskeyRpEntity))]
[JsonSerializable(typeof(PasskeyUserEntity))]
[JsonSerializable(typeof(PasskeyPubKeyCredParam))]
[JsonSerializable(typeof(PasskeyPubKeyCredParam[]))]
[JsonSerializable(typeof(PasskeyRegistrationRequest))]
[JsonSerializable(typeof(PasskeyAuthenticatorAttestationResponse))]
[JsonSerializable(typeof(PasskeyAuthenticationOptionsRequest))]
[JsonSerializable(typeof(PasskeyAuthenticationOptions))]
[JsonSerializable(typeof(PasskeyAllowCredential))]
[JsonSerializable(typeof(PasskeyAllowCredential[]))]
[JsonSerializable(typeof(PasskeyAuthenticationRequest))]
[JsonSerializable(typeof(PasskeyAuthenticatorAssertionResponse))]
[JsonSerializable(typeof(PasskeyRenameRequest))]
[JsonSerializable(typeof(TogglePasswordLoginRequest))]
[JsonSerializable(typeof(TogglePasswordLoginResponse))]
[JsonSerializable(typeof(ResetRequest))]
[JsonSerializable(typeof(PasskeyVerificationData))]
[JsonSerializable(typeof(PasskeyAssertionResponseData))]
[JsonSerializable(typeof(LogEntry))]
[JsonSerializable(typeof(LogEntry[]))]
[JsonSerializable(typeof(LogsResponse))]
internal partial class AppJsonSerializerContext : JsonSerializerContext
{

}

public partial class Program { }
