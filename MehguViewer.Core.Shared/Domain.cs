using System.Text.Json.Serialization;

namespace MehguViewer.Shared.Models;

// Node
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

// Taxonomy & Search
public record TaxonomyData(
    string[] genres,
    string[] content_warnings,
    string[] types,
    string[] scanlators
);

public record SearchResults(
    Series[] data,
    CursorPagination meta
);

public record CursorPagination(
    string? next_cursor,
    bool has_more
);

// Series
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

// Unit
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

// Page
public record Page(
    int page_number,
    string? asset_urn,
    string? url
);

public record PageCreate(
    int page_number,
    string? url
);

// Job
public record JobResponse(
    string job_id,
    string status
);

public record Job(
    string id,
    string type,
    string status,
    int progress_percentage,
    string? result_urn,
    string? error_details
);

public record JobListResponse(
    Job[] data
);

// Progress
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

// Social
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

// Collection
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

// System
public record SystemConfig(
    bool is_setup_complete,
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

// Auth & Users
public record User(
    string id,
    string username,
    string password_hash,
    string role,
    DateTime created_at
);

public record UserCreate(
    string username,
    string password,
    string role
);

public record UserUpdate(
    string? role,
    string? password
);

public record LoginRequest(
    string username,
    string password
);

public record LoginResponse(
    string token,
    string username,
    string role
);
