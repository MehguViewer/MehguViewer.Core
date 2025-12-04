namespace MehguViewer.Shared.Models;

public record AdminPasswordRequest(string password);

public record DatabaseConfig(
    string host,
    int port,
    string database,
    string username,
    string password
);

public record DatabaseSetupRequest(
    string host,
    int port,
    string database,
    string username,
    string password,
    bool reset
);

public record DatabaseTestResponse(bool has_data);

public record EmbeddedDatabaseStatus(
    bool available,
    bool running,
    bool enabled,
    int port,
    bool has_data,
    string? version,
    string? data_directory,
    string? error_message
);

public record UseEmbeddedDatabaseRequest(bool reset_data = false);

public record UseEmbeddedDatabaseResponse(string message, string? connection_string = null);

public record SetupStatusResponse(bool is_setup_complete);

public record DebugResponse(string message);

public record ResetRequest(string? password_hash, PasskeyVerificationData? passkey = null);

public record PasskeyVerificationData(
    string challenge_id,
    string id,
    string raw_id,
    PasskeyAssertionResponseData response,
    string type
);

public record PasskeyAssertionResponseData(
    string client_data_json,
    string authenticator_data,
    string signature,
    string? user_handle
);

public record ResetResponse(string message);

public record StorageSettingsUpdate(int? thumbnail_size, int? web_size, int? jpeg_quality);

public record StorageStatsResponse(int asset_count, long cache_bytes, string storage_path, int thumbnail_size, int web_size, int jpeg_quality);

public record ClearCacheResponse(string message);

// Logs Models
public record LogEntry(
    DateTime timestamp,
    string level,
    string message,
    string? exception
);

public record LogsResponse(LogEntry[] logs, int total_count);

// Auth Configuration Models
public record AuthConfig(
    bool registration_open,
    int max_login_attempts,
    int lockout_duration_minutes,
    int token_expiry_hours,
    CloudflareConfig cloudflare,
    bool require_2fa_passkey,
    bool require_password_for_danger_zone
);

public record CloudflareConfig(
    bool enabled,
    string turnstile_site_key,
    string turnstile_secret_key
);

public record AuthConfigUpdate(
    bool? registration_open,
    int? max_login_attempts,
    int? lockout_duration_minutes,
    int? token_expiry_hours,
    CloudflareConfigUpdate? cloudflare,
    bool? require_2fa_passkey,
    bool? require_password_for_danger_zone
);

public record CloudflareConfigUpdate(
    bool? enabled,
    string? turnstile_site_key,
    string? turnstile_secret_key
);

// Login/Register request with optional Cloudflare token
public record LoginRequestWithCf(
    string username,
    string password,
    string? cf_turnstile_token
);

public record RegisterRequestWithCf(
    string username,
    string password,
    string? cf_turnstile_token
);

// Auth config response for UI
public record AuthConfigPublic(
    bool registration_open,
    bool cloudflare_enabled,
    string? turnstile_site_key
);