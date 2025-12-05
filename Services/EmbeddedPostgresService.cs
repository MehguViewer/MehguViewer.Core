using MysticMind.PostgresEmbed;
using System.Diagnostics;

namespace MehguViewer.Core.Backend.Services;

/// <summary>
/// Manages an embedded PostgreSQL server that runs alongside the application.
/// This allows MehguViewer.Core to run without requiring external PostgreSQL setup.
/// 
/// Configuration options in appsettings.json:
/// - EmbeddedPostgres:Enabled (bool) - Enable/disable embedded PostgreSQL
/// - EmbeddedPostgres:FallbackToMemory (bool) - Allow fallback to MemoryRepository if DB fails
/// - EmbeddedPostgres:Version (string) - PostgreSQL version (default: 15.3.0)
/// - EmbeddedPostgres:Port (int) - Port number (default: 6235)
/// - EmbeddedPostgres:DataDir (string) - Data directory path (default: pg_data)
/// </summary>
public class EmbeddedPostgresService : IHostedService, IAsyncDisposable
{
    private readonly ILogger<EmbeddedPostgresService> _logger;
    private readonly IConfiguration _configuration;
    private PgServer? _pgServer;
    private readonly TaskCompletionSource<bool> _startupComplete = new();
    private bool _isRunning = false;

    // Default configuration
    private const string DefaultPgVersion = "15.3.0";
    private const int DefaultPort = 6235;
    private const string DefaultUser = "postgres";
    private const string DefaultDatabase = "mehguviewer";
    
    public bool IsRunning => _isRunning;
    public int Port { get; private set; }
    public string ConnectionString { get; private set; } = string.Empty;
    public bool EmbeddedModeEnabled { get; private set; } = true;
    public bool StartupFailed { get; private set; } = false;
    public bool FallbackToMemoryAllowed { get; private set; } = true;

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
            FallbackToMemoryAllowed = embeddedConfig.GetValue<bool>("FallbackToMemory", true);

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
            HandleStartupFailure(ex);
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
        var instanceDir = Path.Combine(dataDir!, "pg_embed", fixedInstanceId.ToString());
        var pgDataDir = Path.Combine(instanceDir, "data");
        var binDir = Path.Combine(instanceDir, "bin");
        var initdbPath = Path.Combine(binDir, "initdb");

        // Check if PostgreSQL is already running on this port
        if (await TryConnectToExistingPostgres(Port))
        {
            _logger.LogInformation("PostgreSQL is already running on port {Port}. Using existing instance.", Port);
            ConnectionString = $"Host=localhost;Port={Port};Database={DefaultDatabase};Username={DefaultUser};Password=postgres;Pooling=false";
            _isRunning = true;
            await EnsureDatabaseExistsAsync();
            _startupComplete.TrySetResult(true);
            return;
        }

        // Check if binaries exist, if not, download them using MysticMind library
        if (!File.Exists(initdbPath))
        {
            _logger.LogInformation("PostgreSQL binaries not found. Downloading...");
            await DownloadPostgresBinariesAsync(dataDir!, pgVersion!, fixedInstanceId, cancellationToken);
        }

        // Ensure PostgreSQL binaries have execute permissions (important on macOS/Linux)
        await EnsureAllPostgresBinariesExecutableAsync(dataDir!, pgVersion!, cancellationToken);

        // Check if we need to initialize the data directory
        var needsInit = !Directory.Exists(pgDataDir) || !File.Exists(Path.Combine(pgDataDir, "PG_VERSION"));
        
        if (needsInit)
        {
            _logger.LogInformation("Initializing PostgreSQL data directory...");
            await InitializeDataDirectoryAsync(binDir, pgDataDir, cancellationToken);
        }

        // Start PostgreSQL using pg_ctl directly (more reliable than MysticMind library on macOS)
        _logger.LogInformation("Starting PostgreSQL server...");
        await StartPostgresDirectlyAsync(binDir, pgDataDir, Port, cancellationToken);

        // Build connection string
        ConnectionString = $"Host=localhost;Port={Port};Database={DefaultDatabase};Username={DefaultUser};Password=postgres;Pooling=false";
        _isRunning = true;

        _logger.LogInformation("Embedded PostgreSQL started successfully on port {Port}", Port);

        // Ensure the database exists
        await EnsureDatabaseExistsAsync();

        _startupComplete.TrySetResult(true);
    }

    private async Task DownloadPostgresBinariesAsync(string dataDir, string pgVersion, Guid instanceId, CancellationToken cancellationToken)
    {
        // Use MysticMind library just to download binaries (not to start the server)
        var serverParams = new Dictionary<string, string>
        {
            { "password_encryption", "scram-sha-256" }
        };

        // Create a temporary PgServer just to trigger binary download
        var tempServer = new PgServer(
            pgVersion: pgVersion,
            pgUser: DefaultUser,
            dbDir: dataDir,
            instanceId: instanceId,
            port: Port,
            pgServerParams: serverParams,
            clearInstanceDirOnStop: false,
            clearWorkingDirOnStart: false,
            addLocalUserAccessPermission: true,
            startupWaitTime: 300000  // 5 minutes for download
        );

        _logger.LogInformation("Downloading PostgreSQL binaries (this may take a few minutes on first run)...");
        
        try
        {
            // Start will download binaries and try to init/start - we'll let it timeout or fail
            // but binaries should be downloaded
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromMinutes(5));
            
            await tempServer.StartAsync();
            
            // If it actually started, stop it - we'll start it ourselves
            try { await tempServer.StopAsync(); } catch { }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("MysticMind startup timed out, but binaries should be downloaded. Continuing...");
        }
        catch (Exception ex)
        {
            // Check if binaries were downloaded despite the error
            var binDir = Path.Combine(dataDir, "pg_embed", instanceId.ToString(), "bin");
            if (File.Exists(Path.Combine(binDir, "initdb")))
            {
                _logger.LogWarning(ex, "MysticMind had an error, but binaries are present. Continuing...");
            }
            else
            {
                throw new Exception($"Failed to download PostgreSQL binaries: {ex.Message}", ex);
            }
        }
    }

    private async Task<bool> TryConnectToExistingPostgres(int port)
    {
        try
        {
            var testConnStr = $"Host=localhost;Port={port};Database=postgres;Username={DefaultUser};Password=postgres;Pooling=false;Timeout=3";
            await using var conn = new Npgsql.NpgsqlConnection(testConnStr);
            await conn.OpenAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task InitializeDataDirectoryAsync(string binDir, string dataDir, CancellationToken cancellationToken)
    {
        var initdbPath = Path.Combine(binDir, "initdb");
        
        var psi = new ProcessStartInfo
        {
            FileName = initdbPath,
            Arguments = $"-D \"{dataDir}\" -U {DefaultUser}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
            throw new Exception("Failed to start initdb process");

        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            _logger.LogError("initdb failed with exit code {ExitCode}: {Error}", process.ExitCode, error);
            throw new Exception($"initdb failed: {error}");
        }

        _logger.LogInformation("PostgreSQL data directory initialized successfully");
    }

    private async Task StartPostgresDirectlyAsync(string binDir, string dataDir, int port, CancellationToken cancellationToken)
    {
        var pgCtlPath = Path.Combine(binDir, "pg_ctl");
        var logFile = Path.Combine(dataDir, "postgresql.log");

        var psi = new ProcessStartInfo
        {
            FileName = pgCtlPath,
            Arguments = $"-D \"{dataDir}\" -o \"-p {port}\" -l \"{logFile}\" start",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
            throw new Exception("Failed to start pg_ctl process");

        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            _logger.LogError("pg_ctl start failed with exit code {ExitCode}: {Error}\nOutput: {Output}", process.ExitCode, error, output);
            
            // Read the log file for more details
            if (File.Exists(logFile))
            {
                var logContent = await File.ReadAllTextAsync(logFile, cancellationToken);
                _logger.LogError("PostgreSQL log: {Log}", logContent);
            }
            
            throw new Exception($"pg_ctl start failed: {error}");
        }

        _logger.LogInformation("PostgreSQL server started: {Output}", output.Trim());
        
        // Wait a moment for the server to be ready
        await Task.Delay(1000, cancellationToken);
        
        // Verify we can connect
        for (int i = 0; i < 10; i++)
        {
            if (await TryConnectToExistingPostgres(port))
            {
                _logger.LogInformation("PostgreSQL is accepting connections");
                return;
            }
            await Task.Delay(500, cancellationToken);
        }
        
        throw new Exception("PostgreSQL started but is not accepting connections");
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

    private void HandleStartupFailure(Exception ex)
    {
        StartupFailed = true;
        EmbeddedModeEnabled = false;
        
        _logger.LogError(ex, "Embedded PostgreSQL startup failed with exception: {Message}", ex.Message);
        if (ex.InnerException != null)
        {
            _logger.LogError("Inner exception: {InnerMessage}", ex.InnerException.Message);
        }
        
        if (FallbackToMemoryAllowed)
        {
            _logger.LogWarning(ex, 
                "Embedded PostgreSQL startup failed. FallbackToMemory is enabled - using MemoryRepository (data will NOT persist!)");
            _startupComplete.TrySetResult(false); // Signal completion but with failure
        }
        else
        {
            _logger.LogError(ex, 
                "Embedded PostgreSQL startup failed. FallbackToMemory is disabled. " +
                "Set 'EmbeddedPostgres:FallbackToMemory' to true in appsettings.json to allow memory fallback.");
            _startupComplete.TrySetException(ex); // Propagate the exception
        }
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

    private async Task EnsureAllPostgresBinariesExecutableAsync(string dataDir, string pgVersion, CancellationToken cancellationToken)
    {
        try
        {
            // Focus on the bin directory where the actual executables are
            var binDir = Path.Combine(dataDir, "pg_embed", "00000000-0000-0000-0000-000000000001", "bin");

            if (!Directory.Exists(binDir))
            {
                _logger.LogWarning("PostgreSQL bin directory not found: {BinDir}", binDir);
                return;
            }

            var binaries = Directory.GetFiles(binDir);
            
            foreach (var binary in binaries)
            {
                var fileName = Path.GetFileName(binary);
                
                // On Unix systems, executable files typically don't have extensions
                // On Windows, look for .exe and .dll files
                var isExecutable = OperatingSystem.IsWindows()
                    ? fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || 
                      fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                    : !fileName.Contains('.'); // Unix: files without extensions are executables

                if (isExecutable)
                {
                    try
                    {
                        if (!OperatingSystem.IsWindows())
                        {
                            // Use chmod +x to set execute permissions
                            var process = Process.Start(new ProcessStartInfo
                            {
                                FileName = "chmod",
                                Arguments = $"+x \"{binary}\"",
                                UseShellExecute = false,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                CreateNoWindow = true
                            });

                            if (process != null)
                            {
                                await process.WaitForExitAsync(cancellationToken);
                                if (process.ExitCode != 0)
                                {
                                    var error = await process.StandardError.ReadToEndAsync();
                                    _logger.LogWarning("Failed to set execute permissions on {File}: {Error}", binary, error);
                                }
                                else
                                {
                                    _logger.LogDebug("Set execute permissions on {File}", binary);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to set execute permissions on {File}", binary);
                    }
                }
            }

            _logger.LogInformation("PostgreSQL binary permissions fixed for {Count} files in {BinDir}", binaries.Length, binDir);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to ensure PostgreSQL binary permissions");
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
