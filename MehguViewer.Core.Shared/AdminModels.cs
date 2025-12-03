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

public record ResetRequest(string password_hash);

public record ResetResponse(string message);

public record StorageSettingsUpdate(int? thumbnail_size, int? web_size, int? jpeg_quality);

public record StorageStatsResponse(int asset_count, long cache_bytes, string storage_path, int thumbnail_size, int web_size, int jpeg_quality);

