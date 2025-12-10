using MehguViewer.Core.Helpers;
using MysticMind.PostgresEmbed;
using Npgsql;
using System.Diagnostics;
using System.Security;

namespace MehguViewer.Core.Services;

/// <summary>
/// Manages an embedded PostgreSQL server for self-contained deployment.
/// Supports both embedded mode (auto-download binaries) and external PostgreSQL connections.
/// </summary>
/// <remarks>
/// <para><strong>Lifecycle:</strong></para>
/// <list type="number">
/// <item>StartAsync: Checks configuration and attempts external connection first</item>
/// <item>Falls back to embedded mode: Downloads binaries via MysticMind.PostgresEmbed</item>
/// <item>Initializes data directory with initdb if needed</item>
/// <item>Starts server via pg_ctl with trust authentication</item>
/// <item>Creates default database if not exists</item>
/// <item>StopAsync: Gracefully shuts down via pg_ctl (fast mode)</item>
/// </list>
/// <para><strong>Platform Support:</strong></para>
/// <list type="bullet">
/// <item>Windows: Binaries used as-is (.exe/.dll)</item>
/// <item>macOS/Linux: Sets execute permissions (chmod +x) on all binaries</item>
/// </list>
/// <para><strong>Configuration (appsettings.json):</strong></para>
/// <code>
/// "EmbeddedPostgres": {
///   "Enabled": true,
///   "FallbackToMemory": false,
///   "Version": "15.3.0",
///   "Port": 6235,
///   "DataDir": "pg_data",
///   "InstanceId": "00000000-0000-0000-0000-000000000001"
/// }
/// </code>
/// <para><strong>Fallback Strategy:</strong></para>
/// <list type="number">
/// <item>Try external PostgreSQL on configured port</item>
/// <item>If fails and Enabled=true, download and start embedded server</item>
/// <item>If embedded fails and FallbackToMemory=true, allow MemoryRepository</item>
/// <item>If FallbackToMemory=false, propagate exception and fail startup</item>
/// </list>
/// </remarks>
public class EmbeddedPostgresService : IHostedService, IAsyncDisposable
{
    #region Constants
    
    /// <summary>Default PostgreSQL version to download if not specified in configuration.</summary>
    private const string DefaultPgVersion = "17.2.0";
    
    /// <summary>Default port for PostgreSQL server if not specified in configuration.</summary>
    private const int DefaultPort = 6235;
    
    /// <summary>Default PostgreSQL superuser name for embedded instances.</summary>
    private const string DefaultUser = "postgres";
    
    /// <summary>Default database name to create for MehguViewer application.</summary>
    private const string DefaultDatabase = "mehguviewer";
    
    /// <summary>Default instance ID for embedded PostgreSQL (used in directory structure).</summary>
    private const string DefaultInstanceId = "00000000-0000-0000-0000-000000000001";
    
    /// <summary>Timeout for downloading PostgreSQL binaries (5 minutes).</summary>
    private static readonly TimeSpan BinaryDownloadTimeout = TimeSpan.FromMinutes(5);
    
    /// <summary>Delay after server startup to ensure readiness (1 second).</summary>
    private static readonly TimeSpan ServerReadyDelay = TimeSpan.FromSeconds(1);
    
    /// <summary>Timeout for external PostgreSQL connection test (3 seconds).</summary>
    private const int ConnectionTestTimeout = 3;
    
    /// <summary>Maximum retry attempts for connection verification after server startup.</summary>
    private const int MaxConnectionRetries = 10;
    
    /// <summary>Delay between connection retry attempts (500 milliseconds).</summary>
    private static readonly TimeSpan ConnectionRetryDelay = TimeSpan.FromMilliseconds(500);
    
    /// <summary>Timeout for process execution (30 seconds).</summary>
    private static readonly TimeSpan ProcessExecutionTimeout = TimeSpan.FromSeconds(30);
    
    /// <summary>Maximum log file size to read for error diagnostics (10KB).</summary>
    private const int MaxLogFileSizeToRead = 10 * 1024;
    
    #endregion
    
    #region Fields
    
    private readonly ILogger<EmbeddedPostgresService> _logger;
    private readonly IConfiguration _configuration;
    
    /// <summary>TaskCompletionSource for coordinating repository initialization.</summary>
    private readonly TaskCompletionSource<bool> _startupComplete = new();
    
    private bool _isRunning;
    private bool _isDisposed;
    private string? _binDir;
    private string? _dataDir;
    private Process? _postgresProcess = null!;
    
    #endregion
    
    #region Constructor
    
    public EmbeddedPostgresService(ILogger<EmbeddedPostgresService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }
    
    #endregion
    
    #region Public Properties
    
    /// <summary>Indicates whether the PostgreSQL server is currently running.</summary>
    public bool IsRunning => _isRunning;
    
    /// <summary>The port on which the PostgreSQL server is listening.</summary>
    public int Port { get; private set; }
    
    /// <summary>Connection string for the MehguViewer database.</summary>
    public string ConnectionString { get; private set; } = string.Empty;
    
    /// <summary>Indicates whether embedded mode is active (vs external PostgreSQL).</summary>
    public bool EmbeddedModeEnabled { get; private set; } = true;
    
    /// <summary>Indicates whether startup failed (used for fallback logic).</summary>
    public bool StartupFailed { get; private set; } = false;
    
    /// <summary>Indicates whether memory repository fallback is allowed on startup failure.</summary>
    public bool FallbackToMemoryAllowed { get; private set; } = true;
    
    #endregion
    
    #region Public Methods
    
    /// <summary>
    /// Waits for the PostgreSQL startup process to complete.
    /// </summary>
    /// <returns>Task that completes when startup is finished (success or failure).</returns>
    public Task WaitForStartupAsync() => _startupComplete.Task;

    /// <summary>
    /// Starts the PostgreSQL service when the application starts.
    /// Attempts external connection first, then falls back to embedded mode if enabled.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for startup operation.</param>
    /// <returns>Task representing the asynchronous operation.</returns>
    /// <remarks>
    /// <para><strong>Startup Flow:</strong></para>
    /// <list type="number">
    /// <item>Check if embedded mode is disabled in configuration</item>
    /// <item>Test external PostgreSQL connection from connection string</item>
    /// <item>If external connection succeeds, use it and skip embedded mode</item>
    /// <item>If external fails and Enabled=true, start embedded server</item>
    /// <item>Signal completion via _startupComplete TaskCompletionSource</item>
    /// </list>
    /// </remarks>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Starting EmbeddedPostgresService initialization");
        
        try
        {
            var embeddedConfig = _configuration.GetSection("EmbeddedPostgres");
            var enabled = embeddedConfig.GetValue<bool>("Enabled", true);
            FallbackToMemoryAllowed = embeddedConfig.GetValue<bool>("FallbackToMemory", true);

            _logger.LogInformation("EmbeddedPostgres configuration - Enabled: {Enabled}, FallbackToMemory: {FallbackToMemory}", 
                enabled, FallbackToMemoryAllowed);

            if (!enabled)
            {
                _logger.LogInformation("Embedded PostgreSQL is disabled by configuration. Using external connection string");
                EmbeddedModeEnabled = false;
                _startupComplete.TrySetResult(true);
                return;
            }

            // Attempt external connection first
            var existingConnectionString = _configuration.GetConnectionString("DefaultConnection");
            if (!string.IsNullOrEmpty(existingConnectionString))
            {
                _logger.LogDebug("Testing external PostgreSQL connection: {ConnectionString}", 
                    SanitizeConnectionString(existingConnectionString));
                
                try
                {
                    using var testConn = new NpgsqlConnection(existingConnectionString);
                    await testConn.OpenAsync(cancellationToken);
                    
                    _logger.LogInformation("External PostgreSQL connection successful. Skipping embedded server");
                    ConnectionString = existingConnectionString;
                    EmbeddedModeEnabled = false;
                    _startupComplete.TrySetResult(true);
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to connect to external PostgreSQL: {Message}. Attempting embedded server startup", 
                        ex.Message);
                }
            }
            else
            {
                _logger.LogDebug("No external connection string configured. Proceeding with embedded PostgreSQL");
            }

            // Start embedded PostgreSQL
            await StartEmbeddedServerAsync(embeddedConfig, cancellationToken);
        }
        catch (Exception ex)
        {
            HandleStartupFailure(ex);
        }
    }
    
    #endregion
    
    #region Startup Methods

    /// <summary>
    /// Starts the embedded PostgreSQL server with specified configuration.
    /// </summary>
    /// <param name="config">Configuration section containing embedded PostgreSQL settings.</param>
    /// <param name="cancellationToken">Cancellation token for startup operation.</param>
    /// <returns>Task representing the asynchronous operation.</returns>
    /// <remarks>
    /// <para><strong>Startup Steps:</strong></para>
    /// <list type="number">
    /// <item>Check if PostgreSQL already running on configured port</item>
    /// <item>Download binaries if not present (via MysticMind library)</item>
    /// <item>Set execute permissions on all binaries (macOS/Linux)</item>
    /// <item>Initialize data directory with initdb if needed</item>
    /// <item>Start server via pg_ctl with trust authentication</item>
    /// <item>Verify connectivity and create database</item>
    /// </list>
    /// </remarks>
    private async Task StartEmbeddedServerAsync(IConfigurationSection config, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Initializing embedded PostgreSQL server configuration");
        
        var pgVersion = config.GetValue("Version", DefaultPgVersion);
        Port = config.GetValue("Port", DefaultPort);
        var dataDir = config.GetValue("DataDir", Path.Combine(Directory.GetCurrentDirectory(), "pg_data"));
        var instanceId = config.GetValue<Guid?>("InstanceId", null);

        // Validate port range
        if (Port < 1024 || Port > 65535)
        {
            _logger.LogWarning("Invalid port {Port} specified. Using default port {DefaultPort}", Port, DefaultPort);
            Port = DefaultPort;
        }

        _logger.LogInformation("Starting embedded PostgreSQL version {Version} on port {Port}", pgVersion, Port);
        _logger.LogInformation("Data directory: {DataDir}", dataDir);

        // Use a fixed instance ID so data persists between restarts
        var fixedInstanceId = instanceId ?? Guid.Parse(DefaultInstanceId);
        var instanceDir = Path.Combine(dataDir!, "pg_embed", fixedInstanceId.ToString());
        var pgDataDir = Path.Combine(instanceDir, "data");
        var binDir = Path.Combine(instanceDir, "bin");
        var initdbPath = Path.Combine(binDir, "initdb");

        // Store paths for shutdown
        _binDir = binDir;
        _dataDir = pgDataDir;

        // Check if PostgreSQL is already running on this port
        _logger.LogDebug("Checking if PostgreSQL is already running on port {Port}", Port);
        if (await TryConnectToExistingPostgres(Port))
        {
            _logger.LogInformation("PostgreSQL is already running on port {Port}. Reusing existing instance", Port);
            ConnectionString = BuildConnectionString(Port, DefaultDatabase, DefaultUser);
            _isRunning = true;
            await EnsureDatabaseExistsAsync();
            _startupComplete.TrySetResult(true);
            return;
        }
        
        _logger.LogDebug("No existing PostgreSQL instance found on port {Port}", Port);

        // Check if binaries exist, if not, download them using MysticMind library
        if (!File.Exists(initdbPath))
        {
            _logger.LogInformation("PostgreSQL binaries not found at {InitdbPath}. Initiating download", initdbPath);
            await DownloadPostgresBinariesAsync(dataDir!, pgVersion!, fixedInstanceId, cancellationToken);
        }
        else
        {
            _logger.LogDebug("PostgreSQL binaries found at {BinDir}", binDir);
        }

        // Ensure PostgreSQL binaries have execute permissions (important on macOS/Linux)
        await EnsureAllPostgresBinariesExecutableAsync(dataDir!, pgVersion!, cancellationToken);

        // Check if we need to initialize the data directory
        var pgVersionFile = Path.Combine(pgDataDir, "PG_VERSION");
        var needsInit = !Directory.Exists(pgDataDir) || !File.Exists(pgVersionFile);
        
        if (needsInit)
        {
            _logger.LogInformation("Data directory requires initialization at {PgDataDir}", pgDataDir);
            await InitializeDataDirectoryAsync(binDir, pgDataDir, cancellationToken);
        }
        else
        {
            _logger.LogDebug("Data directory already initialized at {PgDataDir}", pgDataDir);
        }

        // Start PostgreSQL using pg_ctl directly (more reliable than MysticMind library on macOS)
        _logger.LogInformation("Starting PostgreSQL server on port {Port}", Port);
        await StartPostgresDirectlyAsync(binDir, pgDataDir, Port, cancellationToken);

        // Build connection string
        ConnectionString = BuildConnectionString(Port, DefaultDatabase, DefaultUser);
        _isRunning = true;

        _logger.LogInformation("Embedded PostgreSQL started successfully. Connection string configured");

        // Ensure the database exists
        await EnsureDatabaseExistsAsync();

        _logger.LogInformation("Embedded PostgreSQL service fully initialized and ready");
        _startupComplete.TrySetResult(true);
    }

    /// <summary>
    /// Downloads PostgreSQL binaries using the MysticMind.PostgresEmbed library.
    /// </summary>
    /// <param name="dataDir">Root directory for PostgreSQL data and binaries.</param>
    /// <param name="pgVersion">PostgreSQL version to download (e.g., "15.3.0").</param>
    /// <param name="instanceId">Unique instance identifier for directory structure.</param>
    /// <param name="cancellationToken">Cancellation token with 5-minute timeout.</param>
    /// <returns>Task representing the asynchronous operation.</returns>
    /// <remarks>
    /// Uses MysticMind library only for binary download, not for server management.
    /// Creates temporary PgServer instance to trigger download, then stops it.
    /// Verifies binaries exist even if download/start times out or fails.
    /// </remarks>
    private async Task DownloadPostgresBinariesAsync(string dataDir, string pgVersion, Guid instanceId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Downloading PostgreSQL {Version} binaries to {DataDir}", pgVersion, dataDir);
        
        var serverParams = new Dictionary<string, string>
        {
            { "password_encryption", "scram-sha-256" }
        };

        PgServer? tempServer = null;
        try
        {
            // Create a temporary PgServer just to trigger binary download
            tempServer = new PgServer(
                pgVersion: pgVersion,
                pgUser: DefaultUser,
                dbDir: dataDir,
                instanceId: instanceId,
                port: Port,
                pgServerParams: serverParams,
                clearInstanceDirOnStop: false,
                clearWorkingDirOnStart: false,
                addLocalUserAccessPermission: true,
                startupWaitTime: (int)BinaryDownloadTimeout.TotalMilliseconds
            );

            _logger.LogInformation("Binary download initiated (this may take several minutes on first run)");
            
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(BinaryDownloadTimeout);
            
            await tempServer.StartAsync();
            
            // If it actually started, stop it - we'll start it ourselves
            _logger.LogDebug("Binary download completed. Stopping temporary server instance");
            try 
            { 
                await tempServer.StopAsync(); 
            } 
            catch (Exception ex) 
            { 
                _logger.LogWarning(ex, "Failed to stop temporary server during binary download: {Message}", ex.Message);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Binary download timed out after {Timeout} minutes. Verifying binaries", BinaryDownloadTimeout.TotalMinutes);
            VerifyBinariesDownloaded(dataDir, instanceId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Binary download encountered error: {Message}. Verifying binaries", ex.Message);
            VerifyBinariesDownloaded(dataDir, instanceId);
        }
        finally
        {
            tempServer?.Dispose();
        }
    }
    
    /// <summary>
    /// Verifies that PostgreSQL binaries were successfully downloaded.
    /// </summary>
    /// <param name="dataDir">Root data directory.</param>
    /// <param name="instanceId">Instance identifier.</param>
    /// <exception cref="InvalidOperationException">Thrown if binaries are not found.</exception>
    private void VerifyBinariesDownloaded(string dataDir, Guid instanceId)
    {
        var binDir = Path.Combine(dataDir, "pg_embed", instanceId.ToString(), "bin");
        var initdbPath = Path.Combine(binDir, "initdb");
        
        if (File.Exists(initdbPath))
        {
            _logger.LogInformation("PostgreSQL binaries verified at {BinDir}", binDir);
        }
        else
        {
            _logger.LogError("PostgreSQL binaries not found at {InitdbPath} after download attempt", initdbPath);
            throw new InvalidOperationException($"Failed to download PostgreSQL binaries to {binDir}");
        }
    }
    
    #endregion
    
    #region Connection and Validation Methods

    /// <summary>
    /// Tests connection to PostgreSQL server on specified port.
    /// </summary>
    /// <param name="port">Port number to test (e.g., 6235).</param>
    /// <returns>True if connection succeeds within timeout, false otherwise.</returns>
    /// <remarks>
    /// Uses 3-second timeout. Connects to default 'postgres' database.
    /// Does not throw exceptions - returns false for any connection failure.
    /// </remarks>
    private async Task<bool> TryConnectToExistingPostgres(int port)
    {
        try
        {
            _logger.LogTrace("Testing connection to PostgreSQL on port {Port}", port);
            
            var testConnStr = $"Host=localhost;Port={port};Database=postgres;Username={DefaultUser};Password=postgres;Pooling=false;Timeout={ConnectionTestTimeout}";
            await using var conn = new NpgsqlConnection(testConnStr);
            await conn.OpenAsync();
            
            _logger.LogDebug("Successfully connected to existing PostgreSQL on port {Port}", port);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Connection test failed for port {Port}: {Message}", port, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Initializes PostgreSQL data directory using initdb command.
    /// </summary>
    /// <param name="binDir">Directory containing PostgreSQL binaries (initdb).</param>
    /// <param name="dataDir">Target data directory for PostgreSQL cluster.</param>
    /// <param name="cancellationToken">Cancellation token for initialization.</param>
    /// <returns>Task representing the asynchronous operation.</returns>
    /// <exception cref="Exception">Thrown if initdb process fails or returns non-zero exit code.</exception>
    /// <remarks>
    /// Runs: initdb -D "{dataDir}" -U {DefaultUser}
    /// Creates necessary directory structure and configuration files.
    /// Only runs if PG_VERSION file missing from data directory.
    /// </remarks>
    private async Task InitializeDataDirectoryAsync(string binDir, string dataDir, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Initializing PostgreSQL data directory: {DataDir}", dataDir);
        
        var initdbPath = Path.Combine(binDir, "initdb");
        
        if (!File.Exists(initdbPath))
        {
            var errorMessage = $"initdb executable not found at {initdbPath}";
            _logger.LogError(errorMessage);
            throw new FileNotFoundException(errorMessage, initdbPath);
        }
        
        var psi = new ProcessStartInfo
        {
            FileName = initdbPath,
            Arguments = $"-D \"{dataDir}\" -U {DefaultUser}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        _logger.LogDebug("Executing initdb: {FileName} {Arguments}", psi.FileName, psi.Arguments);

        using var process = Process.Start(psi);
        if (process == null)
        {
            var errorMessage = "Failed to start initdb process";
            _logger.LogError(errorMessage);
            throw new InvalidOperationException(errorMessage);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(ProcessExecutionTimeout);

        var output = await process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
        var errorOutput = await process.StandardError.ReadToEndAsync(timeoutCts.Token);
        await process.WaitForExitAsync(timeoutCts.Token);

        if (process.ExitCode != 0)
        {
            _logger.LogError("initdb failed with exit code {ExitCode}. Error: {Error}", process.ExitCode, errorOutput);
            throw new InvalidOperationException($"initdb failed with exit code {process.ExitCode}: {errorOutput}");
        }

        _logger.LogInformation("PostgreSQL data directory initialized successfully");
        _logger.LogTrace("initdb output: {Output}", output.Trim());
    }

    /// <summary>
    /// Starts PostgreSQL server using pg_ctl command directly.
    /// </summary>
    /// <param name="binDir">Directory containing PostgreSQL binaries (pg_ctl).</param>
    /// <param name="dataDir">PostgreSQL data directory to start from.</param>
    /// <param name="port">Port number for server to listen on.</param>
    /// <param name="cancellationToken">Cancellation token for startup operation.</param>
    /// <returns>Task representing the asynchronous operation.</returns>
    /// <exception cref="Exception">Thrown if pg_ctl fails, server doesn't start, or connections rejected.</exception>
    /// <remarks>
    /// <para>Runs: pg_ctl -D "{dataDir}" -o "-p {port}" -l "{logFile}" start</para>
    /// <para>Waits 1 second after startup, then verifies connectivity with 10 retries (500ms intervals).</para>
    /// <para>Logs stored in: {dataDir}/postgresql.log</para>
    /// </remarks>
    private async Task StartPostgresDirectlyAsync(string binDir, string dataDir, int port, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting PostgreSQL server from {BinDir}", binDir);
        
        var pgCtlPath = Path.Combine(binDir, "pg_ctl");
        var logFile = Path.Combine(dataDir, "postgresql.log");

        if (!File.Exists(pgCtlPath))
        {
            var error = $"pg_ctl executable not found at {pgCtlPath}";
            _logger.LogError(error);
            throw new FileNotFoundException(error, pgCtlPath);
        }

        var psi = new ProcessStartInfo
        {
            FileName = pgCtlPath,
            Arguments = $"-D \"{dataDir}\" -o \"-p {port}\" -l \"{logFile}\" start",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        _logger.LogDebug("Executing pg_ctl: {FileName} {Arguments}", psi.FileName, psi.Arguments);

        using var process = Process.Start(psi);
        if (process == null)
        {
            var error = "Failed to start pg_ctl process";
            _logger.LogError(error);
            throw new InvalidOperationException(error);
        }

        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorOutput = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            _logger.LogError("pg_ctl start failed with exit code {ExitCode}. Error: {Error}", 
                process.ExitCode, errorOutput);
            
            // Read the log file for more details
            if (File.Exists(logFile))
            {
                try
                {
                    var logContent = await ReadLogFileSafelyAsync(logFile, cancellationToken);
                    _logger.LogError("PostgreSQL log excerpt: {Log}", logContent);
                }
                catch (Exception logEx)
                {
                    _logger.LogWarning(logEx, "Failed to read PostgreSQL log file: {Message}", logEx.Message);
                }
            }
            
            throw new InvalidOperationException($"pg_ctl start failed with exit code {process.ExitCode}: {errorOutput}");
        }

        _logger.LogInformation("PostgreSQL server start command completed: {Output}", output.Trim());
        
        // Wait for server to be ready
        _logger.LogDebug("Waiting {Delay}ms for server readiness", ServerReadyDelay.TotalMilliseconds);
        await Task.Delay(ServerReadyDelay, cancellationToken);
        
        // Verify we can connect
        _logger.LogDebug("Verifying PostgreSQL connectivity (max {MaxRetries} attempts)", MaxConnectionRetries);
        for (int i = 0; i < MaxConnectionRetries; i++)
        {
            if (await TryConnectToExistingPostgres(port))
            {
                _logger.LogInformation("PostgreSQL is accepting connections on port {Port}", port);
                return;
            }
            
            if (i < MaxConnectionRetries - 1)
            {
                _logger.LogTrace("Connection attempt {Attempt}/{MaxRetries} failed. Retrying in {Delay}ms", 
                    i + 1, MaxConnectionRetries, ConnectionRetryDelay.TotalMilliseconds);
                await Task.Delay(ConnectionRetryDelay, cancellationToken);
            }
        }
        
        var finalError = "PostgreSQL started but is not accepting connections after {MaxConnectionRetries} attempts";
        _logger.LogError(finalError, MaxConnectionRetries);
        throw new TimeoutException($"PostgreSQL started but is not accepting connections after {MaxConnectionRetries} attempts");
    }

    /// <summary>
    /// Ensures the MehguViewer application database exists, creating it if necessary.
    /// </summary>
    /// <returns>Task representing the asynchronous operation.</returns>
    /// <remarks>
    /// <para>Connects to 'postgres' database to query/create target database.</para>
    /// <para>Uses trust authentication (password ignored but required by Npgsql).</para>
    /// <para>Idempotent: Checks existence before creation.</para>
    /// </remarks>
    private async Task EnsureDatabaseExistsAsync()
    {
        _logger.LogDebug("Checking if database '{Database}' exists", DefaultDatabase);
        
        // Connect to 'postgres' database to create our target database
        var adminConnStr = BuildConnectionString(Port, "postgres", DefaultUser);
        
        try
        {
            await using var conn = new NpgsqlConnection(adminConnStr);
            await conn.OpenAsync();

            await using var checkCmd = conn.CreateCommand();
            checkCmd.CommandText = $"SELECT 1 FROM pg_database WHERE datname = '{DefaultDatabase}'";
            var exists = await checkCmd.ExecuteScalarAsync() != null;

            if (!exists)
            {
                _logger.LogInformation("Creating database '{Database}'", DefaultDatabase);
                await using var createCmd = conn.CreateCommand();
                createCmd.CommandText = $"CREATE DATABASE \"{DefaultDatabase}\"";
                await createCmd.ExecuteNonQueryAsync();
                _logger.LogInformation("Database '{Database}' created successfully", DefaultDatabase);
            }
            else
            {
                _logger.LogDebug("Database '{Database}' already exists", DefaultDatabase);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ensure database '{Database}' exists: {Message}", DefaultDatabase, ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Configures PostgreSQL to use trust authentication for local connections.
    /// </summary>
    /// <param name="dataDir">Root data directory containing pg_hba.conf.</param>
    /// <param name="instanceId">Instance ID for locating configuration file.</param>
    /// <returns>Task representing the asynchronous operation.</returns>
    /// <remarks>
    /// <para>Modifies: {dataDir}/pg_embed/{instanceId}/data/pg_hba.conf</para>
    /// <para>Replaces scram-sha-256 and md5 with trust for 127.0.0.1/32 and ::1/128.</para>
    /// <para>Requires server restart to apply changes.</para>
    /// </remarks>
    #endregion
    
    #region Error Handling and Shutdown

    /// <summary>
    /// Handles embedded PostgreSQL startup failures with fallback logic.
    /// </summary>
    /// <param name="ex">Exception that caused startup failure.</param>
    /// <remarks>
    /// <para><strong>Fallback Strategy:</strong></para>
    /// <list type="number">
    /// <item>Sets StartupFailed = true and EmbeddedModeEnabled = false</item>
    /// <item>If FallbackToMemory=true: Signals completion (allows MemoryRepository)</item>
    /// <item>If FallbackToMemory=false: Propagates exception (hard failure)</item>
    /// </list>
    /// <para>Logs include exception details and inner exceptions for debugging.</para>
    /// </remarks>
    private void HandleStartupFailure(Exception ex)
    {
        StartupFailed = true;
        EmbeddedModeEnabled = false;
        
        _logger.LogError(ex, "Embedded PostgreSQL startup failed: {Message}", ex.Message);
        
        if (ex.InnerException != null)
        {
            _logger.LogError("Inner exception: {InnerMessage}", ex.InnerException.Message);
            _logger.LogDebug("Inner exception stack trace: {StackTrace}", ex.InnerException.StackTrace);
        }
        
        if (FallbackToMemoryAllowed)
        {
            _logger.LogWarning(
                "FallbackToMemory is enabled. Application will use in-memory repository. " +
                "WARNING: All data will be lost on application restart!");
            _startupComplete.TrySetResult(false);
        }
        else
        {
            _logger.LogCritical(
                "FallbackToMemory is disabled. Application cannot start without a working database. " +
                "Set 'EmbeddedPostgres:FallbackToMemory' to true in appsettings.json to allow memory fallback, " +
                "or configure a valid external PostgreSQL connection string.");
            _startupComplete.TrySetException(ex);
        }
    }

    
    /// <summary>
    /// Stops the PostgreSQL service when the application shuts down.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for shutdown operation.</param>
    /// <returns>Task representing the asynchronous operation.</returns>
    /// <remarks>
    /// <para>Shutdown Strategy:</para>
    /// <list type="number">
    /// <item>If using MysticMind PgServer: Call StopAsync on server object</item>
    /// <item>If using direct pg_ctl: Call StopPostgresDirectlyAsync with fast shutdown</item>
    /// <item>Set _isRunning = false regardless of method</item>
    /// </list>
    /// </remarks>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_isDisposed)
        {
            _logger.LogWarning("StopAsync called on already disposed service");
            return;
        }
        
        if (!_isRunning)
        {
            _logger.LogDebug("StopAsync called but service is not running");
            return;
        }
        
        _logger.LogInformation("Stopping embedded PostgreSQL service");

        if (!string.IsNullOrEmpty(_binDir) && !string.IsNullOrEmpty(_dataDir))
        {
            await StopPostgresDirectlyAsync(_binDir, _dataDir, cancellationToken);
        }
        else
        {
            _logger.LogWarning("Cannot stop PostgreSQL: binary or data directory not set");
        }
        
        _isRunning = false;
        _logger.LogInformation("Embedded PostgreSQL service stopped");
    }

    /// <summary>
    /// Stops PostgreSQL server using pg_ctl command directly.
    /// </summary>
    /// <param name="binDir">Directory containing PostgreSQL binaries (pg_ctl).</param>
    /// <param name="dataDir">PostgreSQL data directory to stop.</param>
    /// <param name="cancellationToken">Cancellation token for shutdown operation.</param>
    /// <returns>Task representing the asynchronous operation.</returns>
    /// <remarks>
    /// <para>Runs: pg_ctl -D "{dataDir}" stop -m fast</para>
    /// <para>Fast shutdown: Rolls back active transactions, disconnects clients immediately.</para>
    /// <para>Does not throw exceptions - logs warnings on non-zero exit codes.</para>
    /// </remarks>
    private async Task StopPostgresDirectlyAsync(string binDir, string dataDir, CancellationToken cancellationToken)
    {
        try
        {
            var pgCtlPath = Path.Combine(binDir, "pg_ctl");
            
            if (!File.Exists(pgCtlPath))
            {
                _logger.LogWarning("pg_ctl not found at {Path}. Cannot stop server gracefully", pgCtlPath);
                return;
            }
            
            _logger.LogInformation("Stopping PostgreSQL using pg_ctl (fast mode)");
            
            var psi = new ProcessStartInfo
            {
                FileName = pgCtlPath,
                Arguments = $"-D \"{dataDir}\" stop -m fast",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                await process.WaitForExitAsync(cancellationToken);
                
                if (process.ExitCode == 0)
                {
                    _logger.LogInformation("PostgreSQL stopped successfully");
                }
                else
                {
                    var error = await process.StandardError.ReadToEndAsync(cancellationToken);
                    _logger.LogWarning("pg_ctl stop returned exit code {Code}: {Error}", process.ExitCode, error);
                }
            }
            else
            {
                _logger.LogWarning("Failed to start pg_ctl stop process");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop PostgreSQL: {Message}", ex.Message);
        }
    }
    
    #endregion
    
    #region Platform-Specific Utilities

    /// <summary>
    /// Sets execute permissions on all PostgreSQL binaries for Unix-based systems.
    /// </summary>
    /// <param name="dataDir">Root data directory containing pg_embed binaries.</param>
    /// <param name="pgVersion">PostgreSQL version (currently unused).</param>
    /// <param name="cancellationToken">Cancellation token for permission operations.</param>
    /// <returns>Task representing the asynchronous operation.</returns>
    /// <remarks>
    /// <para><strong>Platform Behavior:</strong></para>
    /// <list type="bullet">
    /// <item>Windows: Identifies .exe and .dll files (no permission changes needed)</item>
    /// <item>macOS/Linux: Identifies files without extensions, runs chmod +x on each</item>
    /// </list>
    /// <para>Targets: {dataDir}/pg_embed/00000000-0000-0000-0000-000000000001/bin/</para>
    /// <para>Does not throw exceptions - logs warnings for permission failures.</para>
    /// </remarks>
    private async Task EnsureAllPostgresBinariesExecutableAsync(string dataDir, string pgVersion, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            _logger.LogDebug("Running on Windows. Skipping executable permission configuration");
            return;
        }
        
        try
        {
            var binDir = Path.Combine(dataDir, "pg_embed", "00000000-0000-0000-0000-000000000001", "bin");

            if (!Directory.Exists(binDir))
            {
                _logger.LogWarning("PostgreSQL bin directory not found: {BinDir}", binDir);
                return;
            }

            var binaries = Directory.GetFiles(binDir);
            var processedCount = 0;
            
            _logger.LogDebug("Setting execute permissions on {Count} files in {BinDir}", binaries.Length, binDir);
            
            foreach (var binary in binaries)
            {
                var fileName = Path.GetFileName(binary);
                
                // On Unix systems, executable files typically don't have extensions
                var isExecutable = !fileName.Contains('.');

                if (isExecutable)
                {
                    try
                    {
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
                                var error = await process.StandardError.ReadToEndAsync(cancellationToken);
                                _logger.LogWarning("Failed to set execute permissions on {File}: {Error}", fileName, error);
                            }
                            else
                            {
                                processedCount++;
                                _logger.LogTrace("Set execute permissions on {File}", fileName);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to set execute permissions on {File}: {Message}", fileName, ex.Message);
                    }
                }
            }

            _logger.LogInformation("Successfully configured execute permissions on {Count} PostgreSQL binaries", processedCount);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to ensure PostgreSQL binary permissions: {Message}", ex.Message);
        }
    }
    
    #endregion
    
    #region IAsyncDisposable Implementation

    /// <summary>
    /// Disposes embedded PostgreSQL resources asynchronously.
    /// </summary>
    /// <returns>ValueTask representing the asynchronous disposal operation.</returns>
    /// <remarks>
    /// <para>Disposal Strategy:</para>
    /// <list type="number">
    /// <item>If _pgServer exists: Dispose MysticMind server object</item>
    /// <item>If using direct pg_ctl: Stop server via StopPostgresDirectlyAsync</item>
    /// <item>Ensures server is stopped even if StopAsync wasn't called</item>
    /// </list>
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }
        
        _logger.LogDebug("Disposing EmbeddedPostgresService");
        
        try
        {
            if (_isRunning && !string.IsNullOrEmpty(_binDir) && !string.IsNullOrEmpty(_dataDir))
            {
                _logger.LogInformation("Stopping PostgreSQL server during disposal");
                await StopPostgresDirectlyAsync(_binDir, _dataDir, CancellationToken.None);
            }
            
            _postgresProcess?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during EmbeddedPostgresService disposal: {Message}", ex.Message);
        }
        finally
        {
            _isDisposed = true;
            _isRunning = false;
        }
    }
    
    #endregion
    
    #region Helper Methods
    
    /// <summary>
    /// Builds a PostgreSQL connection string with standardized format.
    /// </summary>
    /// <param name="port">PostgreSQL server port.</param>
    /// <param name="database">Database name.</param>
    /// <param name="username">PostgreSQL username.</param>
    /// <returns>Formatted connection string.</returns>
    private static string BuildConnectionString(int port, string database, string username)
    {
        return $"Host=localhost;Port={port};Database={database};Username={username};Password=postgres;Pooling=false;Timeout=30";
    }
    
    /// <summary>
    /// Sanitizes connection strings for logging by removing sensitive information.
    /// </summary>
    /// <param name="connectionString">Original connection string.</param>
    /// <returns>Sanitized connection string safe for logging.</returns>
    private static string SanitizeConnectionString(string connectionString)
    {
        if (string.IsNullOrEmpty(connectionString))
        {
            return "[empty]";
        }
        
        // Remove password from connection string for logging
        return System.Text.RegularExpressions.Regex.Replace(
            connectionString,
            @"Password=[^;]*",
            "Password=***",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
    
    /// <summary>
    /// Reads PostgreSQL log file safely with size limits to prevent memory issues.
    /// </summary>
    /// <param name="logFilePath">Path to the PostgreSQL log file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Log file content (truncated if necessary).</returns>
    private async Task<string> ReadLogFileSafelyAsync(string logFilePath, CancellationToken cancellationToken)
    {
        var fileInfo = new FileInfo(logFilePath);
        
        if (!fileInfo.Exists)
        {
            return "[Log file not found]";
        }
        
        if (fileInfo.Length == 0)
        {
            return "[Log file is empty]";
        }
        
        // Read only the last portion of the log file to avoid memory issues
        if (fileInfo.Length > MaxLogFileSizeToRead)
        {
            await using var stream = new FileStream(logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            stream.Seek(-MaxLogFileSizeToRead, SeekOrigin.End);
            
            using var reader = new StreamReader(stream);
            var content = await reader.ReadToEndAsync(cancellationToken);
            return $"[Last {MaxLogFileSizeToRead} bytes of log]\n{content}";
        }
        
        return await File.ReadAllTextAsync(logFilePath, cancellationToken);
    }
    
    #endregion
}
