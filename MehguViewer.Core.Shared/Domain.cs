using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace MehguViewer.Core.Shared;

#region Node Metadata

/// <summary>
/// Core metadata about a MehguViewer node.
/// Describes node capabilities, version, and maintainer information.
/// </summary>
/// <param name="version">Semantic version of the node software.</param>
/// <param name="node_name">Human-readable name of this node instance.</param>
/// <param name="description">Description of the node's purpose or content focus.</param>
/// <param name="auth_server">URL of the authentication server for this node.</param>
/// <param name="capabilities">Feature capabilities supported by this node.</param>
/// <param name="maintainer">Contact information for the node maintainer.</param>
public record NodeMetadata(
    [property: Required(ErrorMessage = "Version is required")]
    string version,
    
    [property: Required(ErrorMessage = "Node name is required")]
    string node_name,
    
    string description,
    
    [property: Required(ErrorMessage = "Auth server URL is required")]
    [Url(ErrorMessage = "Auth server must be a valid URL")]
    string auth_server,
    
    [property: Required(ErrorMessage = "Capabilities are required")]
    NodeCapabilities capabilities,
    
    [property: Required(ErrorMessage = "Maintainer information is required")]
    NodeMaintainer maintainer
);

/// <summary>
/// Feature capabilities supported by a node.
/// </summary>
/// <param name="search">Whether the node supports content search.</param>
/// <param name="streaming">Whether the node supports media streaming.</param>
/// <param name="download">Whether the node supports content downloads.</param>
public record NodeCapabilities(
    bool search,
    bool streaming,
    bool download
);

/// <summary>
/// Contact information for node maintainer.
/// </summary>
/// <param name="name">Maintainer's name or organization.</param>
/// <param name="email">Contact email address.</param>
public record NodeMaintainer(
    [property: Required(ErrorMessage = "Maintainer name is required")]
    string name,
    
    [property: Required(ErrorMessage = "Maintainer email is required")]
    [EmailAddress(ErrorMessage = "Must be a valid email address")]
    string email
);

/// <summary>
/// Public manifest for node discovery and federation.
/// Contains essential information for federation protocol.
/// </summary>
/// <param name="urn">Unique URN identifier for this node.</param>
/// <param name="name">Human-readable node name.</param>
/// <param name="description">Node description.</param>
/// <param name="version">Node software version.</param>
/// <param name="software">Software name (e.g., "MehguViewer.Core").</param>
/// <param name="maintainer">Maintainer contact information.</param>
/// <param name="registration_open">Whether new user registration is allowed.</param>
/// <param name="features">Feature flags for UI/client optimization.</param>
/// <param name="image_cdn_url">Optional CDN URL for image delivery.</param>
public record NodeManifest(
    [property: Required(ErrorMessage = "Node URN is required")]
    string urn,
    
    [property: Required(ErrorMessage = "Node name is required")]
    string name,
    
    string description,
    
    [property: Required(ErrorMessage = "Version is required")]
    string version,
    
    [property: Required(ErrorMessage = "Software name is required")]
    string software,
    
    [property: Required(ErrorMessage = "Maintainer is required")]
    string maintainer,
    
    bool registration_open,
    
    [property: Required(ErrorMessage = "Features are required")]
    NodeFeatures features,
    
    [Url(ErrorMessage = "CDN URL must be valid")]
    string? image_cdn_url
);

/// <summary>
/// Feature flags for client-side optimizations.
/// </summary>
/// <param name="ui_image_tiers_enabled">Whether UI should display image quality tiers.</param>
/// <param name="video_streaming_enabled">Whether video streaming is available.</param>
public record NodeFeatures(
    bool ui_image_tiers_enabled,
    bool video_streaming_enabled
);

#endregion

#region Media Types and Content Warnings

/// <summary>
/// Fixed media type constants for series classification.
/// These types are immutable and cannot be customized by users.
/// </summary>
/// <remarks>
/// - Photo: Visual media such as Manga, Manhwa, Manhua, Comics
/// - Text: Written content such as Novels, Light Novels
/// - Video: Video content such as Anime, Donghua
/// </remarks>
public static class MediaTypes
{
    /// <summary>Photo/Image media (Manga, Manhwa, Manhua, Comics)</summary>
    public const string Photo = "Photo";
    
    /// <summary>Text media (Novels, Light Novels)</summary>
    public const string Text = "Text";
    
    /// <summary>Video media (Anime, Donghua)</summary>
    public const string Video = "Video";

    /// <summary>All valid media types</summary>
    public static readonly string[] All = [Photo, Text, Video];

    /// <summary>
    /// Validates if a media type string is valid.
    /// </summary>
    /// <param name="type">The media type to validate.</param>
    /// <returns>True if the type is valid; false otherwise.</returns>
    public static bool IsValid(string? type) =>
        !string.IsNullOrEmpty(type) && All.Contains(type, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Normalizes a media type to its canonical casing.
    /// </summary>
    /// <param name="type">The media type to normalize.</param>
    /// <returns>The normalized media type, or null if invalid.</returns>
    public static string? Normalize(string? type) =>
        string.IsNullOrEmpty(type) ? null : All.FirstOrDefault(t => t.Equals(type, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Predefined content warning constants.
/// These warnings are used to tag content with age-appropriate advisories.
/// </summary>
/// <remarks>
/// Content warnings help users make informed decisions about content they consume.
/// These are system-defined and cannot be customized to ensure consistency.
/// </remarks>
public static class ContentWarnings
{
    /// <summary>Not Safe For Work - explicit sexual content</summary>
    public const string NSFW = "nsfw";
    
    /// <summary>Graphic violence or gore</summary>
    public const string Gore = "gore";
    
    /// <summary>Violence or combat</summary>
    public const string Violence = "violence";
    
    /// <summary>Strong language or profanity</summary>
    public const string Language = "language";
    
    /// <summary>Suggestive or sexual themes</summary>
    public const string Suggestive = "suggestive";

    /// <summary>All valid content warnings</summary>
    public static readonly string[] All = [NSFW, Gore, Violence, Language, Suggestive];

    /// <summary>
    /// Validates if a content warning is valid.
    /// </summary>
    /// <param name="warning">The warning to validate.</param>
    /// <returns>True if valid or null/empty; false otherwise.</returns>
    public static bool IsValid(string? warning) =>
        string.IsNullOrEmpty(warning) || All.Contains(warning, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Normalizes a content warning to its canonical casing.
    /// </summary>
    /// <param name="warning">The warning to normalize.</param>
    /// <returns>The normalized warning, or null if invalid.</returns>
    public static string? Normalize(string? warning) =>
        string.IsNullOrEmpty(warning) ? null : All.FirstOrDefault(w => w.Equals(warning, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Normalizes an array of content warnings, removing duplicates and invalid values.
    /// </summary>
    /// <param name="warnings">Array of warnings to normalize.</param>
    /// <returns>Normalized, deduplicated array of valid warnings.</returns>
    public static string[] NormalizeAll(string[]? warnings) =>
        warnings?.Select(Normalize).Where(w => w != null).Cast<string>().Distinct().ToArray() ?? [];
}

#endregion

#region Content Creators

/// <summary>
/// Represents an author or artist who creates content.
/// </summary>
/// <param name="id">Unique identifier for the author.</param>
/// <param name="name">Display name of the author.</param>
/// <param name="role">Optional role description (e.g., "Author", "Artist", "Author & Artist").</param>
public record Author(
    [property: Required(ErrorMessage = "Author ID is required")]
    string id,
    
    [property: Required(ErrorMessage = "Author name is required")]
    [property: MinLength(1, ErrorMessage = "Author name cannot be empty")]
    string name,
    
    string? role = null
);

/// <summary>
/// Represents a scanlation or translation group.
/// </summary>
/// <param name="id">Unique identifier for the scanlator.</param>
/// <param name="name">Display name of the scanlation/translation group.</param>
/// <param name="role">Type of work performed (Translation, Scanlation, or Both).</param>
public record Scanlator(
    [property: Required(ErrorMessage = "Scanlator ID is required")]
    string id,
    
    [property: Required(ErrorMessage = "Scanlator name is required")]
    [property: MinLength(1, ErrorMessage = "Scanlator name cannot be empty")]
    string name,
    
    ScanlatorRole role
);

/// <summary>
/// Types of work performed by a scanlation group.
/// </summary>
public enum ScanlatorRole
{
    /// <summary>Text translation only</summary>
    Translation,
    
    /// <summary>Image editing/typesetting only</summary>
    Scanlation,
    
    /// <summary>Both translation and scanlation</summary>
    Both
}

/// <summary>
/// Represents a group that can be associated with series (e.g., fan groups, publishers).
/// </summary>
/// <param name="id">Unique identifier for the group.</param>
/// <param name="name">Display name of the group.</param>
/// <param name="description">Optional description of the group.</param>
/// <param name="website">Optional official website URL.</param>
/// <param name="discord">Optional Discord server invite link.</param>
public record Group(
    [property: Required(ErrorMessage = "Group ID is required")]
    string id,
    
    [property: Required(ErrorMessage = "Group name is required")]
    [property: MinLength(1, ErrorMessage = "Group name cannot be empty")]
    string name,
    
    string? description = null,
    
    [Url(ErrorMessage = "Website must be a valid URL")]
    string? website = null,
    
    [Url(ErrorMessage = "Discord must be a valid URL")]
    string? discord = null
);

#endregion

#region Taxonomy and Search

/// <summary>
/// Auto-generated taxonomy data aggregated from public series.
/// Tags, authors, scanlators, and groups are automatically extracted from all public series.
/// Types are fixed (Photo, Text, Video). Content warnings are predefined.
/// </summary>
/// <remarks>
/// This data is regenerated periodically to reflect the current content catalog.
/// Used for search filters, autocomplete, and content discovery.
/// </remarks>
/// <param name="tags">All unique tags from public series.</param>
/// <param name="content_warnings">All content warnings in use.</param>
/// <param name="types">Fixed media types (Photo, Text, Video).</param>
/// <param name="authors">All unique authors from public series.</param>
/// <param name="scanlators">All unique scanlators from public series.</param>
/// <param name="groups">All unique groups from public series.</param>
public record TaxonomyData(
    string[] tags,
    string[] content_warnings,
    string[] types,
    Author[] authors,
    Scanlator[] scanlators,
    Group[] groups
);

/// <summary>
/// Stored taxonomy configuration in the database.
/// Note: Media types are now fixed (Photo, Text, Video) and cannot be customized.
/// Tags, authors, scanlators, and groups are auto-aggregated from series but can be manually curated.
/// </summary>
/// <param name="tags">Curated list of valid tags.</param>
/// <param name="content_warnings">Predefined content warnings.</param>
/// <param name="types">Fixed media types.</param>
/// <param name="authors">Curated author list.</param>
/// <param name="scanlators">Curated scanlator list.</param>
/// <param name="groups">Curated group list.</param>
public record TaxonomyConfig(
    string[] tags,
    string[] content_warnings,
    string[] types,
    Author[] authors,
    Scanlator[] scanlators,
    Group[] groups
);

/// <summary>
/// DTO for updating taxonomy configuration.
/// All fields are optional for partial updates.
/// Note: The 'types' field is ignored as media types are fixed.
/// </summary>
/// <param name="tags">Updated tag list.</param>
/// <param name="content_warnings">Updated content warning list.</param>
/// <param name="types">Ignored - media types are fixed.</param>
/// <param name="authors">Updated author list.</param>
/// <param name="scanlators">Updated scanlator list.</param>
/// <param name="groups">Updated group list.</param>
public record TaxonomyConfigUpdate(
    string[]? tags,
    string[]? content_warnings,
    string[]? types,  // Ignored - types are fixed
    Author[]? authors,
    Scanlator[]? scanlators,
    Group[]? groups
);

/// <summary>
/// Search results with cursor-based pagination.
/// </summary>
/// <param name="data">Array of series matching the search criteria.</param>
/// <param name="meta">Pagination metadata.</param>
public record SearchResults(
    Series[] data,
    CursorPagination meta
);

/// <summary>
/// Cursor-based pagination metadata.
/// More efficient than offset pagination for large datasets.
/// </summary>
/// <param name="next_cursor">Opaque cursor for the next page, null if no more pages.</param>
/// <param name="has_more">Whether there are more results available.</param>
public record CursorPagination(
    string? next_cursor,
    bool has_more
);

#endregion

#region Reading Directions

/// <summary>
/// Valid reading direction constants for content display.
/// Determines how content pages should be rendered and navigated.
/// </summary>
/// <remarks>
/// - LTR: Left-to-Right (Western comics, Manhwa)
/// - RTL: Right-to-Left (Japanese Manga)
/// - WEBTOON: Vertical scroll (Korean Webtoons, Chinese Manhua)
/// </remarks>
public static class ReadingDirections
{
    /// <summary>Left-to-Right reading (Western comics, Manhwa)</summary>
    public const string LTR = "LTR";
    
    /// <summary>Right-to-Left reading (Japanese Manga)</summary>
    public const string RTL = "RTL";
    
    /// <summary>Vertical scroll reading (Webtoons, Manhua)</summary>
    public const string WEBTOON = "WEBTOON";

    /// <summary>All valid reading directions</summary>
    public static readonly string[] All = [LTR, RTL, WEBTOON];

    /// <summary>
    /// Validates if a reading direction is valid.
    /// </summary>
    /// <param name="direction">The reading direction to validate.</param>
    /// <returns>True if valid or null/empty; false otherwise.</returns>
    public static bool IsValid(string? direction) =>
        string.IsNullOrEmpty(direction) || All.Contains(direction, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Normalizes a reading direction to its canonical casing.
    /// </summary>
    /// <param name="direction">The reading direction to normalize.</param>
    /// <returns>The normalized direction, or null if invalid.</returns>
    public static string? Normalize(string? direction) =>
        string.IsNullOrEmpty(direction) ? null : All.FirstOrDefault(d => d.Equals(direction, StringComparison.OrdinalIgnoreCase));
}

#endregion

#region Series Models

/// <summary>
/// DTO for creating a new series.
/// Only title and media_type are required; other fields can be added later via the details page.
/// </summary>
/// <param name="title">Required. The title of the series.</param>
/// <param name="media_type">Required. Must be one of: Photo, Text, Video.</param>
/// <param name="description">Optional. Description of the series content.</param>
/// <param name="reading_direction">Optional. One of: LTR, RTL, WEBTOON. Defaults to LTR if not specified.</param>
/// <param name="original_language">Optional. ISO 639-1 language code (e.g., "ja", "en", "ko").</param>
public record SeriesCreate(
    [property: Required(ErrorMessage = "Title is required")]
    [property: MinLength(1, ErrorMessage = "Title cannot be empty")]
    [property: MaxLength(500, ErrorMessage = "Title cannot exceed 500 characters")]
    string title,
    
    [property: Required(ErrorMessage = "Media type is required")]
    string media_type,
    
    [property: MaxLength(5000, ErrorMessage = "Description cannot exceed 5000 characters")]
    string? description = null,
    
    string? reading_direction = null,
    
    [property: StringLength(2, MinimumLength = 2, ErrorMessage = "Language code must be 2 characters (ISO 639-1)")]
    [property: RegularExpression("^[a-z]{2}$", ErrorMessage = "Language code must be lowercase ISO 639-1 format")]
    string? original_language = null
);

/// <summary>
/// DTO for updating a Series - all fields nullable for partial updates.
/// Note: Tags, scanlators, etc. can be aggregated from units automatically.
/// Manual updates will override auto-aggregation until next unit update.
/// </summary>
public record SeriesUpdate(
    string? title,
    string? description,
    Poster? poster,
    string? media_type,
    Dictionary<string, string>? external_links,
    string? reading_direction,
    string[]? tags = null,
    string[]? content_warnings = null,
    Author[]? authors = null,
    Scanlator[]? scanlators = null,
    Group[]? groups = null,
    string[]? alt_titles = null,
    string? status = null,
    int? year = null,
    Dictionary<string, LocalizedMetadata>? localized = null,
    string? original_language = null
);

/// <summary>
/// Represents a Series in the MehguViewer system.
/// A Series is the top-level container that can represent Photo (Manga), Text (Novels), or Video (Anime) content.
/// This record is stored as metadata.json in the file system.
/// Metadata is automatically aggregated from child units - if units have different scanlators/tags/etc,
/// the series will show all unique values.
/// </summary>
public record Series(
    string id,
    string? federation_ref,
    string title,
    string description,
    Poster poster,
    string media_type,
    Dictionary<string, string> external_links,
    string? reading_direction,
    string[] tags,
    string[] content_warnings,
    Author[] authors,
    Scanlator[] scanlators,
    Group[]? groups = null,
    string[]? alt_titles = null,
    string? status = null,
    int? year = null,
    string? original_language = null,  // ISO 639-1 language code (e.g., "ja", "en", "ko")
    string? created_by = null,
    DateTime? created_at = null,
    DateTime? updated_at = null,
    Dictionary<string, LocalizedMetadata>? localized = null,
    string[]? allowed_editors = null  // DEPRECATED: Edit permissions are now managed in the database edit_permissions table. This field is kept for backwards compatibility but is not actively used.
);

/// <summary>
/// Localized metadata for a series in a specific language.
/// The key in the dictionary should be an ISO 639-1 language code (e.g., "en", "ja", "ko").
/// Scanlators are specific to each language version as different groups translate to different languages.
/// </summary>
public record LocalizedMetadata(
    string? title,
    string? description,
    string[]? alt_titles = null,
    Scanlator[]? scanlators = null,
    string? content_folder = null,  // Relative folder path for localized content (e.g., "en", "ja")
    Poster? poster = null  // Localized cover image for this language
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
/// <summary>
/// DTO for creating a new Unit with optional metadata inheritance from parent series.
/// If metadata fields are not provided, they will be inherited from the parent series.
/// </summary>
public record UnitCreate(
    double unit_number,
    string? title = null,
    string? language = null,  // ISO 639-1 language code (e.g., "en", "ja")
    string? description = null,
    string[]? tags = null,
    string[]? content_warnings = null,
    Author[]? authors = null,
    Dictionary<string, UnitLocalizedMetadata>? localized = null
);

/// <summary>
/// DTO for updating a Unit - all fields nullable for partial updates.
/// When unit metadata is updated, parent series metadata will be aggregated.
/// </summary>
public record UnitUpdate(
    double? unit_number = null,
    string? title = null,
    string? language = null,
    string? description = null,
    string[]? tags = null,
    string[]? content_warnings = null,
    Author[]? authors = null,
    Dictionary<string, UnitLocalizedMetadata>? localized = null
);

/// <summary>
/// Represents a Unit (chapter/episode/season) in a series.
/// Units inherit metadata from their parent series by default.
/// When unit metadata differs from series, the series will aggregate all unique values.
/// 
/// Example: 
/// - Series has ZH scanlation by "Group A"
/// - Chapter 1-2 use "Group A"
/// - Chapter 3 uses "Group B"
/// - Series metadata will be updated to show both "Group A" and "Group B" for ZH
/// 
/// Stored as metadata.json in the unit folder.
/// Structure: data/series/{series-id}/units/{unit-number}/metadata.json
///            data/series/{series-id}/units/{unit-number}/pages/001.png, 002.png, etc.
/// For localized content:
///            data/series/{series-id}/units/{unit-number}/lang/{lang-code}/pages/...
/// </summary>
public record Unit(
    string id,
    string series_id,
    double unit_number,
    string title,
    DateTime created_at,
    string? created_by = null,
    string? language = null,  // Primary language of this unit
    int page_count = 0,       // Number of pages/files in this unit
    string? folder_path = null, // Relative folder path for this unit's content
    DateTime? updated_at = null,
    string? description = null,
    string[]? tags = null,
    string[]? content_warnings = null,
    Author[]? authors = null,
    Dictionary<string, UnitLocalizedMetadata>? localized = null,
    string[]? allowed_editors = null  // User URNs who have permission to edit this unit (owner always has access)
);

/// <summary>
/// Localized metadata for a unit in a specific language.
/// Similar to series LocalizedMetadata but specific to units.
/// Scanlators can differ per unit for the same language.
/// </summary>
public record UnitLocalizedMetadata(
    string? title = null,
    Scanlator[]? scanlators = null,
    string? content_folder = null  // Relative folder path for localized content
);

public record UnitListResponse(
    Unit[] data,
    CursorPagination meta
);

/// <summary>
/// Permission settings for series/unit editing.
/// Allows owner to grant edit permissions to other uploaders.
/// </summary>
public record EditPermission(
    string target_urn,      // Series or Unit URN
    string user_urn,        // User URN to grant permission to
    DateTime granted_at,
    string granted_by       // Owner who granted the permission
);

/// <summary>
/// DTO for granting edit permission.
/// </summary>
public record GrantEditPermissionRequest(
    string user_urn  // User URN to grant permission to
);

/// <summary>
/// DTO for revoking edit permission.
/// </summary>
public record RevokeEditPermissionRequest(
    string user_urn  // User URN to revoke permission from
);

/// <summary>
/// DTO for transferring series ownership.
/// </summary>
public record TransferOwnershipRequest(
    string new_owner_urn  // URN of the user who will become the new owner
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
    string user_id,
    string name,
    bool is_system,
    string[] items
);

/// <summary>
/// DTO for updating a Collection - all fields nullable for partial updates
/// </summary>
public record CollectionUpdate(
    string? name,
    string[]? items
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
    string[] default_language_filter,
    // Auth settings (stored in database)
    int max_login_attempts,
    int lockout_duration_minutes,
    int token_expiry_hours,
    // Cloudflare Turnstile settings
    bool cloudflare_enabled,
    string cloudflare_site_key,
    string cloudflare_secret_key,
    // Passkey settings
    bool require_2fa_passkey,
    bool require_password_for_danger_zone
);

/// <summary>
/// DTO for PATCH operations on SystemConfig - all fields nullable for partial updates
/// </summary>
public record SystemConfigUpdate(
    bool? is_setup_complete,
    bool? registration_open,
    bool? maintenance_mode,
    string? motd_message,
    string[]? default_language_filter,
    int? max_login_attempts,
    int? lockout_duration_minutes,
    int? token_expiry_hours,
    bool? cloudflare_enabled,
    string? cloudflare_site_key,
    string? cloudflare_secret_key,
    bool? require_2fa_passkey,
    bool? require_password_for_danger_zone
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
    DateTime created_at,
    bool password_login_disabled = false,
    string preferred_language = "en"
);

public record UserCreate(
    string username,
    string password,
    string role
);

public record UserUpdate(
    string? role,
    string? password,
    bool? password_login_disabled = null,
    string? preferred_language = null
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

/// <summary>
/// Response from the user provisioning endpoint.
/// Used when auto-creating users from external authentication.
/// </summary>
public record ProvisionResponse(
    string id,
    string username,
    string role,
    bool was_created,
    string message
);

/// <summary>
/// User profile response (excludes sensitive data like password hash).
/// </summary>
public record UserProfileResponse(
    string id,
    string username,
    string role,
    DateTime created_at,
    bool password_login_disabled = false,
    bool is_first_admin = false
);

/// <summary>
/// Request to change user's own password.
/// </summary>
public record ChangePasswordRequest(
    string current_password,
    string new_password
);

/// <summary>
/// Request to toggle password login for the current user.
/// </summary>
public record TogglePasswordLoginRequest(
    bool disable
);

/// <summary>
/// Response after toggling password login.
/// </summary>
public record TogglePasswordLoginResponse(
    bool password_login_disabled,
    string message
);

// Passkey / WebAuthn Support
/// <summary>
/// Represents a registered passkey credential for a user.
/// </summary>
public record Passkey(
    string id,
    string user_id,
    string credential_id,      // Base64URL-encoded credential ID from authenticator
    string public_key,         // Base64URL-encoded COSE public key
    long sign_count,           // Signature counter for replay detection
    string name,               // User-friendly name for this passkey
    string? device_type,       // Platform authenticator type (platform/cross-platform)
    bool backed_up,            // Whether passkey is backed up (synced)
    DateTime created_at,
    DateTime? last_used_at
);

/// <summary>
/// Response containing passkey info (excludes sensitive data).
/// </summary>
public record PasskeyInfo(
    string id,
    string name,
    string? device_type,
    bool backed_up,
    DateTime created_at,
    DateTime? last_used_at
);

/// <summary>
/// Request to start passkey registration - returns challenge and options.
/// </summary>
public record PasskeyRegistrationOptionsRequest(
    string? passkey_name  // Optional friendly name for the passkey
);

/// <summary>
/// Response with WebAuthn registration options.
/// </summary>
public record PasskeyRegistrationOptions(
    string challenge,                 // Base64URL-encoded challenge
    PasskeyRpEntity rp,               // Relying Party info
    PasskeyUserEntity user,           // User info for registration
    PasskeyPubKeyCredParam[] pub_key_cred_params,
    long timeout,
    string attestation,               // "none" for privacy
    string authenticator_selection_resident_key,
    string authenticator_selection_user_verification
);

public record PasskeyRpEntity(
    string id,
    string name
);

public record PasskeyUserEntity(
    string id,      // Base64URL-encoded user ID
    string name,
    string display_name
);

public record PasskeyPubKeyCredParam(
    string type,    // "public-key"
    int alg         // COSE algorithm identifier (-7 for ES256, -257 for RS256)
);

/// <summary>
/// Request to complete passkey registration with authenticator response.
/// </summary>
public record PasskeyRegistrationRequest(
    string id,                        // Credential ID from authenticator
    string raw_id,                    // Raw credential ID (Base64URL)
    PasskeyAuthenticatorAttestationResponse response,
    string type,                      // "public-key"
    string? passkey_name              // User-friendly name
);

public record PasskeyAuthenticatorAttestationResponse(
    string client_data_json,          // Base64URL-encoded
    string attestation_object         // Base64URL-encoded
);

/// <summary>
/// Request to start passkey authentication.
/// </summary>
public record PasskeyAuthenticationOptionsRequest(
    string? username                  // Optional - for usernameless auth leave null
);

/// <summary>
/// Response with WebAuthn authentication options.
/// </summary>
public record PasskeyAuthenticationOptions(
    string challenge,                 // Base64URL-encoded challenge
    long timeout,
    string rp_id,
    PasskeyAllowCredential[]? allow_credentials,  // Null for discoverable credentials
    string user_verification
);

public record PasskeyAllowCredential(
    string type,    // "public-key"
    string id       // Base64URL credential ID
);

/// <summary>
/// Request to complete passkey authentication.
/// </summary>
public record PasskeyAuthenticationRequest(
    string id,                        // Credential ID
    string raw_id,                    // Raw credential ID (Base64URL)
    PasskeyAuthenticatorAssertionResponse response,
    string type                       // "public-key"
);

public record PasskeyAuthenticatorAssertionResponse(
    string client_data_json,          // Base64URL-encoded
    string authenticator_data,        // Base64URL-encoded  
    string signature,                 // Base64URL-encoded
    string? user_handle               // Base64URL-encoded user ID (for discoverable credentials)
);

/// <summary>
/// Request to rename a passkey.
/// </summary>
public record PasskeyRenameRequest(
    string name
);

/// <summary>
/// Information about available cover variants for a specific language
/// </summary>
public record CoverInfo(
    string? Language,
    string LanguageName,
    string[] Variants
);

#endregion

