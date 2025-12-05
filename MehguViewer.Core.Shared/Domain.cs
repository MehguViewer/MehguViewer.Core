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

// Media Types - Fixed values, not customizable
/// <summary>
/// Fixed media types for series classification.
/// </summary>
public static class MediaTypes
{
    public const string Photo = "Photo";   // Manga, Manhwa, Manhua, Comics
    public const string Text = "Text";     // Novels, Light Novels
    public const string Video = "Video";   // Anime, Donghua

    public static readonly string[] All = [Photo, Text, Video];

    public static bool IsValid(string? type) =>
        !string.IsNullOrEmpty(type) && All.Contains(type, StringComparer.OrdinalIgnoreCase);

    public static string? Normalize(string? type) =>
        string.IsNullOrEmpty(type) ? null : All.FirstOrDefault(t => t.Equals(type, StringComparison.OrdinalIgnoreCase));
}

// Content Warnings - Predefined list
/// <summary>
/// Predefined content warning values.
/// </summary>
public static class ContentWarnings
{
    public const string NSFW = "nsfw";
    public const string Gore = "gore";
    public const string Violence = "violence";
    public const string Language = "language";
    public const string Suggestive = "suggestive";

    public static readonly string[] All = [NSFW, Gore, Violence, Language, Suggestive];

    public static bool IsValid(string? warning) =>
        string.IsNullOrEmpty(warning) || All.Contains(warning, StringComparer.OrdinalIgnoreCase);

    public static string? Normalize(string? warning) =>
        string.IsNullOrEmpty(warning) ? null : All.FirstOrDefault(w => w.Equals(warning, StringComparison.OrdinalIgnoreCase));

    public static string[] NormalizeAll(string[]? warnings) =>
        warnings?.Select(Normalize).Where(w => w != null).Cast<string>().Distinct().ToArray() ?? [];
}

// Author
/// <summary>
/// Represents an Author or Artist who creates content.
/// </summary>
public record Author(
    string id,
    string name,
    string? role = null  // e.g., "Author", "Artist", "Author & Artist"
);

// Scanlator / Translation Group
/// <summary>
/// Represents a Scanlation or Translation group.
/// </summary>
public record Scanlator(
    string id,
    string name,
    ScanlatorRole role  // Translation, Scanlation, or Both
);

public enum ScanlatorRole
{
    Translation,
    Scanlation,
    Both
}

// Group / Scanlation Group
/// <summary>
/// Represents a Group that can be associated with series (e.g., fan groups, publishers).
/// </summary>
public record Group(
    string id,
    string name,
    string? description = null,
    string? website = null,
    string? discord = null
);

// Taxonomy & Search
/// <summary>
/// Auto-generated taxonomy data from public series.
/// Tags, authors, scanlators, and groups are aggregated from all public series.
/// Types are fixed (Photo, Text, Video).
/// Content warnings are predefined.
/// </summary>
public record TaxonomyData(
    string[] tags,
    string[] content_warnings,
    string[] types,
    Author[] authors,
    Scanlator[] scanlators,
    Group[] groups
);

/// <summary>
/// Stored taxonomy configuration - saved in database.
/// Note: types are now fixed (Photo, Text, Video) and cannot be customized.
/// Tags, authors, scanlators, and groups are auto-aggregated from series.
/// </summary>
public record TaxonomyConfig(
    string[] tags,
    string[] content_warnings,
    string[] types,
    Author[] authors,
    Scanlator[] scanlators,
    Group[] groups
);

/// <summary>
/// DTO for updating taxonomy - all fields nullable for partial updates.
/// Note: types field is ignored as media types are fixed.
/// </summary>
public record TaxonomyConfigUpdate(
    string[]? tags,
    string[]? content_warnings,
    string[]? types,
    Author[]? authors,
    Scanlator[]? scanlators,
    Group[]? groups
);

public record SearchResults(
    Series[] data,
    CursorPagination meta
);

public record CursorPagination(
    string? next_cursor,
    bool has_more
);

/// <summary>
/// Valid reading direction values for series content.
/// </summary>
public static class ReadingDirections
{
    public const string LTR = "LTR";       // Left-to-Right (Western comics, Manhwa)
    public const string RTL = "RTL";       // Right-to-Left (Manga)
    public const string WEBTOON = "WEBTOON"; // Vertical scroll (Webtoon, Manhua)

    public static readonly string[] All = [LTR, RTL, WEBTOON];

    public static bool IsValid(string? direction) =>
        string.IsNullOrEmpty(direction) || All.Contains(direction, StringComparer.OrdinalIgnoreCase);

    public static string? Normalize(string? direction) =>
        string.IsNullOrEmpty(direction) ? null : All.FirstOrDefault(d => d.Equals(direction, StringComparison.OrdinalIgnoreCase));
}

// Series
/// <summary>
/// DTO for creating a new Series. Only requires title and media_type.
/// Other fields can be filled in later on the details page.
/// </summary>
/// <param name="title">Required. The title of the series.</param>
/// <param name="media_type">Required. One of: Photo, Text, Video.</param>
/// <param name="description">Optional. Description of the series.</param>
/// <param name="reading_direction">Optional. One of: LTR, RTL, WEBTOON. Defaults to LTR if not specified.</param>
public record SeriesCreate(
    string title,
    string media_type,
    string? description = null,
    string? reading_direction = null
);

/// <summary>
/// DTO for updating a Series - all fields nullable for partial updates
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
    Dictionary<string, LocalizedMetadata>? localized = null
);

/// <summary>
/// Represents a Series in the MehguViewer system.
/// A Series is the top-level container that can represent Photo (Manga), Text (Novels), or Video (Anime) content.
/// This record is stored as metadata.json in the file system.
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
    string? created_by = null,
    DateTime? created_at = null,
    DateTime? updated_at = null,
    Dictionary<string, LocalizedMetadata>? localized = null
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
    string? content_folder = null  // Relative folder path for localized content (e.g., "en", "ja")
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
    string? title,
    string? language = null  // ISO 639-1 language code (e.g., "en", "ja")
);

/// <summary>
/// DTO for updating a Unit - all fields nullable for partial updates
/// </summary>
public record UnitUpdate(
    double? unit_number,
    string? title,
    string? language = null
);

/// <summary>
/// Represents a Unit (chapter/episode) in a series.
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
    string? folder_path = null // Relative folder path for this unit's content
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
