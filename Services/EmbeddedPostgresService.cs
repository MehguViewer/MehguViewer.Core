using MysticMind.PostgresEmbed;

namespace MehguViewer.Core.Backend.Services;

/// <summary>
/// Manages an embedded PostgreSQL server that runs alongside the application.
/// This allows MehguViewer.Core to run without requiring external PostgreSQL setup.
/// </summary>
public class EmbeddedPostgresService : IHostedService, IAsyncDisposable
{
    private readonly ILogger<EmbeddedPostgresService> _logger;
    private readonly IConfiguration _configuration;
    private PgServer? _pgServer;
    private readonly TaskCompletionSource<bool> _startupComplete = new();

    // Default configuration
    private const string DefaultPgVersion = "15.3.0";
    private const int DefaultPort = 6235;
    private const string DefaultUser = "postgres";
    private const string DefaultDatabase = "mehguviewer";
    
    public bool IsRunning => _pgServer != null;
    public int Port { get; private set; }
    public string ConnectionString { get; private set; } = string.Empty;
    public bool EmbeddedModeEnabled { get; private set; } = true;
    public bool StartupFailed { get; private set; } = false;

    public EmbeddedPostgresService(ILogger<EmbeddedPostgresService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public Task WaitForStartupAsync() => _startupComplete.Task;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var embeddedConfig = _configuration.GetSection("EmbeddedPostgres");
            var enabled = embeddedConfig.GetValue<bool>("Enabled", true);

            if (!enabled)
            {
                _logger.LogInformation("Embedded PostgreSQL is disabled. Using external connection string.");
                EmbeddedModeEnabled = false;
                _startupComplete.TrySetResult(true);
                return;
            }

            // Check if we already have a valid external connection
            var existingConnectionString = _configuration.GetConnectionString("DefaultConnection");
            if (!string.IsNullOrEmpty(existingConnectionString))
            {
                try
                {
                    // Try to connect to existing database
                    using var testConn = new Npgsql.NpgsqlConnection(existingConnectionString);
                    await testConn.OpenAsync(cancellationToken);
                    _logger.LogInformation("External PostgreSQL connection successful. Skipping embedded server.");
                    ConnectionString = existingConnectionString;
                    EmbeddedModeEnabled = false;
                    _startupComplete.TrySetResult(true);
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to connect to external PostgreSQL. Attempting embedded server...");
                }
            }

            // Start embedded PostgreSQL
            await StartEmbeddedServerAsync(embeddedConfig, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Embedded PostgreSQL startup failed. Application will use MemoryRepository as fallback.");
            StartupFailed = true;
            EmbeddedModeEnabled = false;
            _startupComplete.TrySetResult(false); // Signal completion but with failure
        }
    }

    private async Task StartEmbeddedServerAsync(IConfigurationSection config, CancellationToken cancellationToken)
    {
        var pgVersion = config.GetValue("Version", DefaultPgVersion);
        Port = config.GetValue("Port", DefaultPort);
        var dataDir = config.GetValue("DataDir", Path.Combine(Directory.GetCurrentDirectory(), "pg_data"));
        var instanceId = config.GetValue<Guid?>("InstanceId", null);

        _logger.LogInformation("Starting embedded PostgreSQL {Version} on port {Port}...", pgVersion, Port);
        _logger.LogInformation("Data directory: {DataDir}", dataDir);

        // Use a fixed instance ID so data persists between restarts
        var fixedInstanceId = instanceId ?? new Guid("00000000-0000-0000-0000-000000000001");

        // Configure PostgreSQL server parameters for trust authentication
        var serverParams = new Dictionary<string, string>
        {
            // Set password_encryption to scram-sha-256 for security when passwords are used
            { "password_encryption", "scram-sha-256" }
        };

        _pgServer = new PgServer(
            pgVersion: pgVersion!,
            pgUser: DefaultUser,
            dbDir: dataDir,
            instanceId: fixedInstanceId,
            port: Port,
            pgServerParams: serverParams,
            clearInstanceDirOnStop: false,  // Keep data between restarts
            clearWorkingDirOnStart: false,  // Don't clear on start
            addLocalUserAccessPermission: true, // Needed on Windows
            startupWaitTime: 120000  // 120 seconds startup wait (first run downloads binaries)
        );

        _logger.LogInformation("Downloading and starting PostgreSQL (this may take a few minutes on first run)...");
        await _pgServer.StartAsync();

        // Modify pg_hba.conf to use trust authentication for local connections
        await ConfigureTrustAuthenticationAsync(dataDir!, fixedInstanceId);

        // Build connection string - with trust auth, password is ignored but Npgsql requires one
        ConnectionString = $"Host=localhost;Port={Port};Database={DefaultDatabase};Username={DefaultUser};Password=postgres;Pooling=false";

        _logger.LogInformation("Embedded PostgreSQL started successfully on port {Port}", Port);

        // Ensure the database exists
        await EnsureDatabaseExistsAsync();

        _startupComplete.TrySetResult(true);
    }

    private async Task EnsureDatabaseExistsAsync()
    {
        // Connect to 'postgres' database to create our target database
        // With trust authentication, password is ignored but Npgsql requires one
        var adminConnStr = $"Host=localhost;Port={Port};Database=postgres;Username={DefaultUser};Password=postgres;Pooling=false";
        
        await using var conn = new Npgsql.NpgsqlConnection(adminConnStr);
        await conn.OpenAsync();

        await using var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = $"SELECT 1 FROM pg_database WHERE datname = '{DefaultDatabase}'";
        var exists = await checkCmd.ExecuteScalarAsync() != null;

        if (!exists)
        {
            _logger.LogInformation("Creating database '{Database}'...", DefaultDatabase);
            await using var createCmd = conn.CreateCommand();
            createCmd.CommandText = $"CREATE DATABASE \"{DefaultDatabase}\"";
            await createCmd.ExecuteNonQueryAsync();
            _logger.LogInformation("Database '{Database}' created successfully", DefaultDatabase);
        }
    }

    private async Task ConfigureTrustAuthenticationAsync(string dataDir, Guid instanceId)
    {
        // The actual path structure is: dataDir/pg_embed/instanceId/data/pg_hba.conf
        var pgHbaPath = Path.Combine(dataDir, "pg_embed", instanceId.ToString(), "data", "pg_hba.conf");
        
        if (!File.Exists(pgHbaPath))
        {
            _logger.LogWarning("pg_hba.conf not found at {Path}. Authentication may fail.", pgHbaPath);
            return;
        }

        _logger.LogInformation("Checking trust authentication in {Path}", pgHbaPath);

        // Read the current config
        var content = await File.ReadAllTextAsync(pgHbaPath);

        // Check if already configured for trust
        if (content.Contains("host    all             all             127.0.0.1/32            trust"))
        {
            _logger.LogInformation("Trust authentication already configured");
            return;
        }

        // Replace scram-sha-256 or md5 with trust for local connections
        var newContent = content
            .Replace("host all all 127.0.0.1/32 scram-sha-256", "host all all 127.0.0.1/32 trust")
            .Replace("host all all ::1/128 scram-sha-256", "host all all ::1/128 trust")
            .Replace("host all all 127.0.0.1/32 md5", "host all all 127.0.0.1/32 trust")
            .Replace("host all all ::1/128 md5", "host all all ::1/128 trust")
            .Replace("local all all scram-sha-256", "local all all trust")
            .Replace("local all all md5", "local all all trust");

        // If no replacements were made, append trust entries
        if (newContent == content)
        {
            newContent += "\n# Added by MehguViewer for embedded PostgreSQL\n";
            newContent += "host all all 127.0.0.1/32 trust\n";
            newContent += "host all all ::1/128 trust\n";
        }

        await File.WriteAllTextAsync(pgHbaPath, newContent);
        _logger.LogInformation("Trust authentication configured successfully");

        // Reload PostgreSQL configuration by sending SIGHUP
        // For MysticMind.PostgresEmbed, we need to restart the server
        _logger.LogInformation("Restarting PostgreSQL to apply authentication changes...");
        await _pgServer!.StopAsync();
        await _pgServer.StartAsync();
        _logger.LogInformation("PostgreSQL restarted with new authentication configuration");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_pgServer != null)
        {
            _logger.LogInformation("Stopping embedded PostgreSQL server...");
            try
            {
                await _pgServer.StopAsync();
                _logger.LogInformation("Embedded PostgreSQL server stopped");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping embedded PostgreSQL server");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_pgServer != null)
        {
            await _pgServer.DisposeAsync();
            _pgServer = null;
        }
    }
}
