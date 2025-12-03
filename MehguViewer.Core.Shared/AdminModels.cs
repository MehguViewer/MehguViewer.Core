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

public record SetupStatusResponse(bool is_setup_complete);

public record DebugResponse(string message);

public record ResetRequest(string password_hash);

public record ResetResponse(string message);

