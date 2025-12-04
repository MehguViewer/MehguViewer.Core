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

/// <summary>
/// DTO for updating a Unit - all fields nullable for partial updates
/// </summary>
public record UnitUpdate(
    double? unit_number,
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
    bool password_login_disabled = false
);

public record UserCreate(
    string username,
    string password,
    string role
);

public record UserUpdate(
    string? role,
    string? password,
    bool? password_login_disabled = null
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
