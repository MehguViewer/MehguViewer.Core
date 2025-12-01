using System.Text.Json.Serialization;
using MehguViewer.Core.Backend.Endpoints;
using MehguViewer.Core.Backend.Models;
using MehguViewer.Core.Backend.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);

// Add Services
builder.Services.AddSingleton<MemoryRepository>();
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
        options.Authority = "https://auth.mehgu.example.com"; // Replace with real auth server
        options.Audience = "mehgu-core";
        options.TokenValidationParameters.ValidateAudience = false; // For dev
        options.TokenValidationParameters.ValidateIssuer = false; // For dev
    });

builder.Services.AddAuthorization();

// Configure CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseResponseCompression();
app.UseCors();

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

// Fallback for Blazor
app.UseBlazorFrameworkFiles();
app.UseDefaultFiles();
app.UseStaticFiles();
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
internal partial class AppJsonSerializerContext : JsonSerializerContext
{

}

public partial class Program { }
