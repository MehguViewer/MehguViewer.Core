using System.Text.Json.Serialization;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});

var app = builder.Build();

app.MapGet("/.well-known/mehgu-node", () => new NodeMetadata(
    "1.0.0",
    "MehguViewer Core",
    "A MehguViewer Core Node",
    "https://auth.mehgu.example.com",
    new NodeCapabilities(true, true, true),
    new NodeMaintainer("Admin", "admin@example.com")
));

app.MapGet("/api/v1/instance", () => new NodeManifest(
    "urn:mvn:node:example",
    "MehguViewer Core",
    "A MehguViewer Core Node",
    "1.0.0",
    "MehguViewer.Core (NativeAOT)",
    "admin@example.com",
    false,
    new NodeFeatures(true, false),
    null
));

app.MapGet("/api/v1/taxonomy", () => new TaxonomyData(
    new[] { "Action", "Adventure", "Comedy", "Drama", "Fantasy", "Slice of Life" },
    new[] { "Gore", "Sexual Violence", "Nudity" },
    new[] { "Manga", "Manhwa", "Manhua", "Novel", "OEL" },
    new[] { "Official", "Fan Group A", "Fan Group B" }
));

app.MapGet("/api/v1/search", (string? q, string? type, string[]? genres, string? status, string? sort, string? cursor) => new SearchResults(
    Array.Empty<object>(),
    new CursorPagination(null, false)
));

// Assets
app.MapGet("/api/v1/assets/{asset_urn}", (string asset_urn, string? variant, string? token) =>
{
    // Stub: Return 404 for now as we don't have real assets
    return Results.NotFound();
});

// Series
app.MapPost("/api/v1/series", (SeriesCreate request) =>
{
    var series = new Series(
        $"urn:mvn:series:{Guid.NewGuid()}",
        null,
        request.title,
        request.description,
        new Poster("https://example.com/poster.jpg", "Placeholder"),
        request.media_type,
        new Dictionary<string, string>(),
        request.reading_direction,
        request.duration_seconds
    );
    return Results.Created($"/api/v1/series/{series.id}", series);
});

app.MapGet("/api/v1/series", (string? cursor, int? limit, string? type) =>
{
    return new SeriesListResponse(
        Array.Empty<Series>(),
        new CursorPagination(null, false)
    );
});

app.MapGet("/api/v1/series/{seriesId}", (string seriesId) =>
{
    // Stub: Return a mock series
    return new Series(
        $"urn:mvn:series:{seriesId}",
        null,
        "Mock Series",
        "Description",
        new Poster("https://example.com/poster.jpg", "Placeholder"),
        "MANGA",
        new Dictionary<string, string>(),
        "RTL",
        null
    );
});

// Chapters (Upload)
app.MapPost("/api/v1/series/{seriesId}/chapters", (string seriesId, HttpRequest request) =>
{
    // Stub: Accept the job
    return Results.Accepted($"/api/v1/jobs/{Guid.NewGuid()}", new JobResponse(
        Guid.NewGuid().ToString(),
        "processing"
    ));
});

// Units
app.MapPost("/api/v1/series/{seriesId}/units", (string seriesId, UnitCreate request) =>
{
    var unit = new Unit(
        Guid.NewGuid().ToString(),
        $"urn:mvn:series:{seriesId}",
        request.unit_number,
        request.title ?? $"Chapter {request.unit_number}",
        DateTime.UtcNow
    );
    return Results.Created($"/api/v1/units/{unit.id}", unit);
});

app.MapGet("/api/v1/series/{seriesId}/units", (string seriesId, string? cursor) =>
{
    return new UnitListResponse(
        Array.Empty<Unit>(),
        new CursorPagination(null, false)
    );
});

// Pages
app.MapPost("/api/v1/units/{unitId}/pages", (string unitId, HttpRequest request) =>
{
    // Stub: Return created page
    return Results.Created("", new Page(
        1,
        $"urn:mvn:asset:{Guid.NewGuid()}",
        null
    ));
});

// Progress
app.MapPut("/api/v1/series/{seriesId}/progress", (string seriesId, ProgressUpdate request) =>
{
    return Results.NoContent();
});

app.MapGet("/api/v1/series/{seriesId}/progress", (string seriesId) =>
{
    return new ReadingProgress(
        $"urn:mvn:series:{seriesId}",
        Guid.NewGuid().ToString(),
        1,
        "reading",
        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
    );
});

// History & Library
app.MapGet("/api/v1/me/history", (int? limit, int? offset) =>
{
    return new HistoryListResponse(
        Array.Empty<ReadingProgress>(),
        new HistoryMeta(0, false)
    );
});

app.MapPost("/api/v1/me/history/batch", (HistoryBatchImport request) =>
{
    return Results.Accepted($"/api/v1/jobs/{Guid.NewGuid()}", new JobResponse(
        Guid.NewGuid().ToString(),
        "processing"
    ));
});

app.MapPost("/api/v1/me/progress", (ReadingProgress request) =>
{
    return Results.Ok(new { success = true });
});

app.MapGet("/api/v1/me/library", () =>
{
    return Array.Empty<ReadingProgress>();
});

// Comments
app.MapGet("/api/v1/comments", (string target_urn, int? depth, string? cursor) =>
{
    return new CommentListResponse(
        Array.Empty<Comment>(),
        new CursorPagination(null, false)
    );
});

app.MapPost("/api/v1/comments", (CommentCreate request) =>
{
    return Results.Created("", new Comment(
        Guid.NewGuid().ToString(),
        request.body_markdown,
        new AuthorSnapshot("urn:mvn:user:mock", "Mock User", "https://example.com/avatar.jpg", "user"),
        DateTime.UtcNow,
        0
    ));
});

// Votes
app.MapPost("/api/v1/votes", (Vote request) =>
{
    return Results.Ok();
});

// Collections
app.MapGet("/api/v1/collections", () =>
{
    return Array.Empty<Collection>();
});

app.MapPost("/api/v1/collections", (CollectionCreate request) =>
{
    return Results.Created("", new Collection(
        Guid.NewGuid().ToString(),
        request.name,
        false,
        Array.Empty<string>()
    ));
});

app.MapPost("/api/v1/collections/{collection_id}/items", (string collection_id, CollectionItemAdd request) =>
{
    return Results.Ok();
});

app.MapDelete("/api/v1/collections/{collection_id}/items/{urn}", (string collection_id, string urn) =>
{
    return Results.NoContent();
});

// Admin & System
app.MapGet("/api/v1/admin/configuration", () =>
{
    return new SystemConfig(true, false, "Welcome to MehguViewer", new[] { "en", "jp" });
});

app.MapPatch("/api/v1/admin/configuration", (SystemConfig request) =>
{
    return Results.Ok(request);
});

app.MapGet("/api/v1/admin/stats", () =>
{
    return new SystemStats(100, 1024L * 1024 * 1024 * 500, 3600);
});

app.MapPost("/api/v1/reports", (Report request) =>
{
    return Results.Accepted();
});

app.MapGet("/api/v1/jobs/{job_id}", (string job_id) =>
{
    return new Job(
        job_id,
        "INGEST_CHAPTER",
        "PROCESSING",
        50,
        null,
        null
    );
});

app.UseBlazorFrameworkFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program { }

public record NodeMetadata(
    string version,
    string node_name,
    string description,
    string auth_server,
    NodeCapabilities capabilities,
    NodeMaintainer maintainer
);

public record NodeCapabilities(
    bool search,
    bool streaming,
    bool download
);

public record NodeMaintainer(
    string name,
    string email
);

public record NodeManifest(
    string urn,
    string name,
    string description,
    string version,
    string software,
    string maintainer,
    bool registration_open,
    NodeFeatures features,
    string? image_cdn_url
);

public record NodeFeatures(
    bool ui_image_tiers_enabled,
    bool video_streaming_enabled
);

public record TaxonomyData(
    string[] genres,
    string[] content_warnings,
    string[] types,
    string[] scanlators
);

public record SearchResults(
    object[] data,
    CursorPagination meta
);

public record CursorPagination(
    string? next_cursor,
    bool has_more
);

// Series DTOs
public record SeriesCreate(
    string title,
    string description,
    string media_type,
    string? reading_direction,
    int? duration_seconds
);

public record Series(
    string id,
    string? federation_ref,
    string title,
    string description,
    Poster poster,
    string media_type,
    Dictionary<string, string> external_links,
    string? reading_direction,
    int? duration_seconds
);

public record Poster(
    string url,
    string alt_text
);

public record SeriesListResponse(
    Series[] data,
    CursorPagination meta
);

// Unit DTOs
public record UnitCreate(
    double unit_number,
    string? title
);

public record Unit(
    string id,
    string series_id,
    double unit_number,
    string title,
    DateTime created_at
);

public record UnitListResponse(
    Unit[] data,
    CursorPagination meta
);

// Page DTOs
public record Page(
    int page_number,
    string? asset_urn,
    string? url
);

// Job DTOs
public record JobResponse(
    string job_id,
    string status
);

// Progress DTOs
public record ProgressUpdate(
    string last_read_chapter_id,
    int page_number,
    bool completed
);

public record ReadingProgress(
    string series_urn,
    string chapter_id,
    int page_number,
    string status,
    long updated_at
);

public record HistoryListResponse(
    ReadingProgress[] data,
    HistoryMeta meta
);

public record HistoryMeta(
    int total,
    bool has_more
);

public record HistoryBatchImport(
    HistoryImportItem[] items
);

public record HistoryImportItem(
    string series_urn,
    string chapter_urn,
    DateTime read_at
);

// Social DTOs
public record Comment(
    string id,
    string content,
    AuthorSnapshot author,
    DateTime created_at,
    int vote_count
);

public record CommentCreate(
    string target_urn,
    string body_markdown,
    bool spoiler
);

public record AuthorSnapshot(
    string uid,
    string display_name,
    string avatar_url,
    string role_badge
);

public record CommentListResponse(
    Comment[] data,
    CursorPagination meta
);

public record Vote(
    string target_id,
    string target_type,
    int value
);

// Collection DTOs
public record Collection(
    string id,
    string name,
    bool is_system,
    string[] items
);

public record CollectionCreate(
    string name
);

public record CollectionItemAdd(
    string target_urn
);

// System DTOs
public record SystemConfig(
    bool registration_open,
    bool maintenance_mode,
    string motd_message,
    string[] default_language_filter
);

public record SystemStats(
    int total_users,
    long total_storage_bytes,
    int uptime_seconds
);

public record Report(
    string target_urn,
    string reason,
    string severity
);

public record Job(
    string id,
    string type,
    string status,
    int progress_percentage,
    string? result_urn,
    string? error_details
);

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
internal partial class AppJsonSerializerContext : JsonSerializerContext
{

}

public partial class Program { }
