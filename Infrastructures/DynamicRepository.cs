using MehguViewer.Core.Shared;
using MehguViewer.Core.Services;
using MehguViewer.Core.Workers;
using MehguViewer.Core.Helpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using System.Text.Json;

namespace MehguViewer.Core.Infrastructures;

/// <summary>
/// Dynamic repository wrapper that switches between storage backends at runtime.
/// Provides seamless fallback from PostgreSQL to in-memory storage with file-based series management.
/// </summary>
/// <remarks>
/// <para><strong>Initialization Strategy:</strong></para>
/// <list type="number">
/// <item>Starts with MemoryRepository as default</item>
/// <item>Initializes FileBasedSeriesService for series/unit persistence</item>
/// <item>Attempts embedded PostgreSQL connection (if configured)</item>
/// <item>Falls back to external PostgreSQL (if connection string provided)</item>
/// <item>Remains on MemoryRepository if all database connections fail</item>
/// </list>
/// <para><strong>Runtime Switching:</strong></para>
/// <para>Can switch from MemoryRepository to PostgresRepository via SwitchToPostgres().</para>
/// <para>Useful for setup wizards and initial configuration flows.</para>
/// <para><strong>Thread Safety:</strong></para>
/// <para>Repository switching operations are not thread-safe. Ensure initialization completes before concurrent access.</para>
/// </remarks>
public sealed class DynamicRepository : IRepository
{
    #region Constants
    
    /// <summary>Maximum time to wait for embedded PostgreSQL startup.</summary>
    private const int MaxStartupWaitSeconds = 30;
    
    /// <summary>Database name for admin operations.</summary>
    private const string AdminDatabase = "postgres";
    
    #endregion
    
    #region Fields
    
    /// <summary>Current active repository implementation.</summary>
    private IRepository _current;
    
    /// <summary>Logger factory for creating typed loggers.</summary>
    private readonly ILoggerFactory _loggerFactory;
    
    /// <summary>Logger instance for DynamicRepository operations.</summary>
    private readonly ILogger<DynamicRepository> _logger;
    
    /// <summary>Application configuration provider.</summary>
    private readonly IConfiguration _configuration;
    
    /// <summary>Optional embedded PostgreSQL service instance.</summary>
    private readonly EmbeddedPostgresService? _embeddedPostgres;
    
    /// <summary>Optional file-based series service for persistent series/unit storage.</summary>
    private readonly FileBasedSeriesService? _fileService;
    
    /// <summary>Metadata aggregation service for series-unit metadata operations.</summary>
    private readonly MetadataAggregationService _metadataService;
    
    /// <summary>Lock object for thread-safe repository switching.</summary>
    private readonly object _switchLock = new();
    
    #endregion
    
    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="DynamicRepository"/> class.
    /// </summary>
    /// <param name="configuration">Application configuration for connection strings.</param>
    /// <param name="loggerFactory">Logger factory for creating typed loggers.</param>
    /// <param name="embeddedPostgres">Optional embedded PostgreSQL service instance.</param>
    /// <param name="fileService">Optional file-based series service for persistent series/unit storage.</param>
    /// <param name="metadataService">Metadata aggregation service for series-unit operations.</param>
    /// <exception cref="ArgumentNullException">Thrown when required parameters are null.</exception>
    /// <remarks>
    /// <para>Starts with MemoryRepository as the default backend.</para>
    /// <para>Call <see cref="InitializeAsync"/> to attempt upgrading to PostgreSQL.</para>
    /// </remarks>
    public DynamicRepository(
        IConfiguration configuration, 
        ILoggerFactory loggerFactory,
        EmbeddedPostgresService? embeddedPostgres = null,
        FileBasedSeriesService? fileService = null,
        MetadataAggregationService? metadataService = null)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _logger = _loggerFactory.CreateLogger<DynamicRepository>();
        _embeddedPostgres = embeddedPostgres;
        _fileService = fileService;
        _metadataService = metadataService ?? throw new ArgumentNullException(nameof(metadataService));

        // Start with MemoryRepository as the default backend
        // Will be upgraded to PostgreSQL during InitializeAsync() if available
        _current = new MemoryRepository(loggerFactory.CreateLogger<MemoryRepository>(), _metadataService);
        
        _logger.LogDebug("DynamicRepository initialized with MemoryRepository as default backend");
    }
    
    #endregion
    
    #region Initialization and Switching

    /// <summary>
    /// Initializes the repository, attempting to upgrade from MemoryRepository to PostgreSQL.
    /// Should be called after EmbeddedPostgresService has started (if configured).
    /// </summary>
    /// <param name="resetData">If true, drops all tables before initialization. Use with caution in production.</param>
    /// <returns>Task representing the asynchronous initialization operation.</returns>
    /// <remarks>
    /// <para><strong>Initialization Order:</strong></para>
    /// <list type="number">
    /// <item>Initialize FileBasedSeriesService (if available)</item>
    /// <item>Try embedded PostgreSQL (if available and not failed)</item>
    /// <item>Try external PostgreSQL from connection string</item>
    /// <item>Fall back to MemoryRepository with warning</item>
    /// </list>
    /// <para><strong>Error Handling:</strong></para>
    /// <para>Failures are logged but do not throw exceptions. Falls back gracefully to next option.</para>
    /// </remarks>
    public async Task InitializeAsync(bool resetData = false)
    {
        _logger.LogInformation("Starting repository initialization (resetData: {ResetData})", resetData);

        // Step 1: Initialize FileBasedSeriesService for series/unit persistence
        if (_fileService != null)
        {
            try
            {
                _logger.LogDebug("Initializing FileBasedSeriesService...");
                await _fileService.InitializeAsync();
                var seriesCount = _fileService.ListSeries().Count();
                _logger.LogInformation("FileBasedSeriesService initialized successfully with {SeriesCount} series", seriesCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize FileBasedSeriesService - series operations may be limited");
            }
        }
        else
        {
            _logger.LogWarning("FileBasedSeriesService not configured - series will not persist to file system");
        }

        // Step 2: Try embedded PostgreSQL if available and not failed
        if (_embeddedPostgres != null && !_embeddedPostgres.StartupFailed)
        {
            if (await TryInitializeEmbeddedPostgresAsync(resetData))
            {
                _logger.LogInformation("Repository initialized successfully with embedded PostgreSQL");
                return;
            }
        }

        // Step 3: Try external PostgreSQL from connection string
        var connectionString = _configuration.GetConnectionString("DefaultConnection");
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            if (TryInitializeExternalPostgres(connectionString, resetData))
            {
                _logger.LogInformation("Repository initialized successfully with external PostgreSQL");
                return;
            }
        }
        else
        {
            _logger.LogDebug("No DefaultConnection string configured, skipping external PostgreSQL");
        }

        // Step 4: Fallback to MemoryRepository
        _logger.LogWarning("All PostgreSQL initialization attempts failed or not configured - using MemoryRepository (data will not persist across restarts!)");
    }

    /// <summary>
    /// Attempts to initialize embedded PostgreSQL.
    /// </summary>
    /// <param name="resetData">Whether to reset database data.</param>
    /// <returns>True if initialization succeeded, false otherwise.</returns>
    private async Task<bool> TryInitializeEmbeddedPostgresAsync(bool resetData)
    {
        if (_embeddedPostgres == null)
        {
            return false;
        }

        try
        {
            _logger.LogDebug("Waiting for embedded PostgreSQL to start (max {MaxWait}s)...", MaxStartupWaitSeconds);
            
            // Wait for embedded postgres to be ready
            await _embeddedPostgres.WaitForStartupAsync();

            if (_embeddedPostgres.StartupFailed)
            {
                _logger.LogWarning("Embedded PostgreSQL startup failed, skipping");
                return false;
            }

            if (string.IsNullOrWhiteSpace(_embeddedPostgres.ConnectionString))
            {
                _logger.LogWarning("Embedded PostgreSQL connection string is null or empty");
                return false;
            }

            _logger.LogInformation("Connecting to embedded PostgreSQL...");
            
            if (resetData)
            {
                _logger.LogWarning("Resetting embedded PostgreSQL data (DESTRUCTIVE OPERATION)");
                ResetDatabase(_embeddedPostgres.ConnectionString);
            }
            
            EnsureDatabaseExists(_embeddedPostgres.ConnectionString);
            
            lock (_switchLock)
            {
                _current = new PostgresRepository(_embeddedPostgres.ConnectionString, _loggerFactory.CreateLogger<PostgresRepository>(), _metadataService);
            }
            
            _logger.LogInformation("Successfully connected to embedded PostgreSQL at {ConnectionString}", 
                MaskConnectionString(_embeddedPostgres.ConnectionString));
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize embedded PostgreSQL, will try external or fallback to memory");
            return false;
        }
    }

    /// <summary>
    /// Attempts to initialize external PostgreSQL from connection string.
    /// </summary>
    /// <param name="connectionString">PostgreSQL connection string.</param>
    /// <param name="resetData">Whether to reset database data.</param>
    /// <returns>True if initialization succeeded, false otherwise.</returns>
    private bool TryInitializeExternalPostgres(string connectionString, bool resetData)
    {
        try
        {
            _logger.LogInformation("Connecting to external PostgreSQL...");
            
            if (resetData)
            {
                _logger.LogWarning("Resetting PostgreSQL data (DESTRUCTIVE OPERATION)");
                ResetDatabase(connectionString);
            }
            
            EnsureDatabaseExists(connectionString);
            
            lock (_switchLock)
            {
                _current = new PostgresRepository(connectionString, _loggerFactory.CreateLogger<PostgresRepository>(), _metadataService);
            }
            
            _logger.LogInformation("Successfully connected to external PostgreSQL at {ConnectionString}", 
                MaskConnectionString(connectionString));
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize external PostgreSQL from connection string, falling back to MemoryRepository");
            return false;
        }
    }

    /// <summary>
    /// Resets the database by dropping all tables.
    /// </summary>
    /// <param name="connectionString">PostgreSQL connection string.</param>
    /// <exception cref="NpgsqlException">Thrown when database operations fail.</exception>
    /// <remarks>
    /// <para><strong>⚠️ WARNING: DESTRUCTIVE OPERATION!</strong></para>
    /// <para>Drops the following tables with CASCADE:</para>
    /// <list type="bullet">
    /// <item>jobs</item>
    /// <item>collections</item>
    /// <item>series</item>
    /// <item>assets</item>
    /// <item>users</item>
    /// <item>system_config</item>
    /// </list>
    /// </remarks>
    private void ResetDatabase(string connectionString)
    {
        _logger.LogWarning("Executing database reset on {ConnectionString}", MaskConnectionString(connectionString));
        
        try
        {
            using var conn = new NpgsqlConnection(connectionString);
            conn.Open();
            
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                DROP TABLE IF EXISTS jobs CASCADE;
                DROP TABLE IF EXISTS collections CASCADE;
                DROP TABLE IF EXISTS series CASCADE;
                DROP TABLE IF EXISTS assets CASCADE;
                DROP TABLE IF EXISTS users CASCADE;
                DROP TABLE IF EXISTS system_config CASCADE;";
            
            var affectedRows = cmd.ExecuteNonQuery();
            _logger.LogInformation("Database reset completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reset database at {ConnectionString}", MaskConnectionString(connectionString));
            throw;
        }
    }

    /// <summary>
    /// Masks sensitive information in connection strings for logging.
    /// </summary>
    /// <param name="connectionString">Connection string to mask.</param>
    /// <returns>Masked connection string with password hidden.</returns>
    private static string MaskConnectionString(string connectionString)
    {
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            if (!string.IsNullOrEmpty(builder.Password))
            {
                builder.Password = "***MASKED***";
            }
            return builder.ToString();
        }
        catch
        {
            // If parsing fails, return a generic masked string
            return "***CONNECTION_STRING_MASKED***";
        }
    }

    /// <summary>
    /// Indicates whether the current repository is in-memory (non-persistent).
    /// </summary>
    /// <value>True if using MemoryRepository, false if using PostgresRepository.</value>
    /// <remarks>Use this property to warn users about data persistence before critical operations.</remarks>
    public bool IsInMemory
    {
        get
        {
            lock (_switchLock)
            {
                return _current is MemoryRepository;
            }
        }
    }

    /// <summary>
    /// Tests a PostgreSQL connection string for validity without changing the current repository.
    /// </summary>
    /// <param name="connectionString">Connection string to test.</param>
    /// <returns>True if connection succeeds and repository can be initialized, false otherwise.</returns>
    /// <exception cref="ArgumentNullException">Thrown when connectionString is null or whitespace.</exception>
    /// <remarks>
    /// <para>Performs the following checks:</para>
    /// <list type="number">
    /// <item>Ensures target database exists (creates if necessary)</item>
    /// <item>Creates a temporary PostgresRepository instance</item>
    /// <item>Validates the repository has data or can accept data</item>
    /// </list>
    /// </remarks>
    public bool TestConnection(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentNullException(nameof(connectionString), "Connection string cannot be null or empty");
        }

        _logger.LogDebug("Testing PostgreSQL connection to {ConnectionString}", MaskConnectionString(connectionString));

        try
        {
            EnsureDatabaseExists(connectionString);
            
            // Try to initialize a temporary PostgresRepository
            var testRepo = new PostgresRepository(connectionString, _loggerFactory.CreateLogger<PostgresRepository>(), _metadataService);
            
            // Check if it has data or can store data
            var hasData = testRepo.HasData();
            
            _logger.LogInformation("Connection test successful for {ConnectionString} (HasData: {HasData})", 
                MaskConnectionString(connectionString), hasData);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Connection test failed for {ConnectionString}", MaskConnectionString(connectionString));
            return false;
        }
    }

    /// <summary>
    /// Switches the current repository to PostgreSQL with the specified connection string.
    /// </summary>
    /// <param name="connectionString">PostgreSQL connection string.</param>
    /// <param name="reset">If true, resets all database data before switching.</param>
    /// <exception cref="ArgumentNullException">Thrown when connectionString is null or whitespace.</exception>
    /// <exception cref="InvalidOperationException">Thrown when PostgreSQL initialization fails.</exception>
    /// <remarks>
    /// <para><strong>⚠️ Thread Safety Warning:</strong></para>
    /// <para>This method is not thread-safe. Ensure no concurrent repository operations during switching.</para>
    /// <para><strong>Persistence:</strong></para>
    /// <para>Updates appsettings.json with the new connection string for persistence across restarts.</para>
    /// </remarks>
    public void SwitchToPostgres(string connectionString, bool reset)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentNullException(nameof(connectionString), "Connection string cannot be null or empty");
        }

        _logger.LogInformation("Switching to PostgreSQL (reset: {Reset})", reset);

        try
        {
            EnsureDatabaseExists(connectionString);
            
            var newRepo = new PostgresRepository(connectionString, _loggerFactory.CreateLogger<PostgresRepository>(), _metadataService);
            
            if (reset)
            {
                _logger.LogWarning("Resetting new PostgreSQL repository data");
                newRepo.ResetDatabase();
            }
            
            lock (_switchLock)
            {
                _current = newRepo;
            }
            
            // Persist connection string to configuration
            UpdateAppSettings(connectionString);
            
            _logger.LogInformation("Successfully switched to PostgreSQL at {ConnectionString}", 
                MaskConnectionString(connectionString));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to switch to PostgreSQL at {ConnectionString}", 
                MaskConnectionString(connectionString));
            throw new InvalidOperationException($"Failed to switch to PostgreSQL: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Ensures the target database exists, creating it if necessary.
    /// </summary>
    /// <param name="connectionString">PostgreSQL connection string.</param>
    /// <remarks>
    /// <para>Connects to the 'postgres' admin database to check/create the target database.</para>
    /// <para>If creation fails due to permissions, logs a warning but continues (database may already exist).</para>
    /// </remarks>
    private void EnsureDatabaseExists(string connectionString)
    {
        try 
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            var targetDb = builder.Database;
            
            if (string.IsNullOrWhiteSpace(targetDb))
            {
                _logger.LogDebug("No database specified in connection string, skipping database creation check");
                return;
            }

            _logger.LogDebug("Ensuring database '{DatabaseName}' exists", targetDb);

            // Connect to admin 'postgres' database to check/create target database
            builder.Database = AdminDatabase;
            using var conn = new NpgsqlConnection(builder.ToString());
            conn.Open();

            using var checkCmd = conn.CreateCommand();
            checkCmd.CommandText = "SELECT 1 FROM pg_database WHERE datname = @dbname";
            checkCmd.Parameters.AddWithValue("dbname", targetDb);
            
            var exists = checkCmd.ExecuteScalar() != null;

            if (!exists)
            {
                _logger.LogInformation("Database '{DatabaseName}' does not exist, creating...", targetDb);
                
                using var createCmd = conn.CreateCommand();
                // Use quoted identifier to handle special characters in database name
                createCmd.CommandText = $"CREATE DATABASE \"{targetDb}\"";
                createCmd.ExecuteNonQuery();
                
                _logger.LogInformation("Database '{DatabaseName}' created successfully", targetDb);
            }
            else
            {
                _logger.LogDebug("Database '{DatabaseName}' already exists", targetDb);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, 
                "Failed to ensure database exists. This may be fine if the database already exists or user lacks CREATE DATABASE permission. Proceeding with connection attempt...");
            
            // We don't rethrow here because:
            // 1. The database might already exist
            // 2. User might not have permission to create databases
            // 3. Database might be created by another process/admin
            // If the database truly doesn't exist, the subsequent connection will fail anyway
        }
    }

    /// <summary>
    /// Updates appsettings.json with the new connection string for persistence across restarts.
    /// </summary>
    /// <param name="connectionString">PostgreSQL connection string to persist.</param>
    /// <remarks>
    /// <para>Updates both 'appsettings.json' in the current directory.</para>
    /// <para>Failures are logged but do not throw exceptions (non-critical operation).</para>
    /// </remarks>
    private void UpdateAppSettings(string connectionString)
    {
        try
        {
            var configPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
            
            if (!File.Exists(configPath))
            {
                _logger.LogWarning("appsettings.json not found at {Path}, cannot persist connection string", configPath);
                return;
            }

            _logger.LogDebug("Updating appsettings.json with new connection string");

            var json = File.ReadAllText(configPath);
            var jObject = System.Text.Json.Nodes.JsonNode.Parse(json);
            
            if (jObject == null)
            {
                _logger.LogWarning("Failed to parse appsettings.json, cannot persist connection string");
                return;
            }

            // Ensure ConnectionStrings section exists
            var connStrings = jObject["ConnectionStrings"];
            if (connStrings == null)
            {
                jObject["ConnectionStrings"] = new System.Text.Json.Nodes.JsonObject();
                connStrings = jObject["ConnectionStrings"];
            }

            // Update DefaultConnection
            connStrings!["DefaultConnection"] = connectionString;
            
            // Write back with formatting
            var options = new JsonSerializerOptions { WriteIndented = true };
            var updatedJson = jObject.ToJsonString(options);
            File.WriteAllText(configPath, updatedJson);
            
            _logger.LogInformation("Successfully updated appsettings.json with new connection string");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update appsettings.json - connection string will not persist across restarts");
            // Don't throw - this is a non-critical operation
        }
    }
    
    #endregion
    
    #region IRepository Delegation (File System Priority)

    /// <summary>
    /// Seeds the repository with debug/sample data for development and testing.
    /// </summary>
    /// <remarks>Delegates to the current repository implementation.</remarks>
    public void SeedDebugData()
    {
        _logger.LogInformation("Seeding debug data");
        lock (_switchLock)
        {
            _current.SeedDebugData();
        }
    }
    
    #endregion
    
    #region Series Operations (File-Based)
    
    /// <summary>
    /// Adds a new series to the file system.
    /// </summary>
    /// <param name="series">Series to add.</param>
    /// <exception cref="InvalidOperationException">Thrown when FileBasedSeriesService is not initialized.</exception>
    /// <remarks>File system is the source of truth for series data.</remarks>
    public void AddSeries(Series series)
    {
        if (_fileService == null)
        {
            _logger.LogError("Attempted to add series {SeriesId} but FileBasedSeriesService is not initialized", series?.id);
            throw new InvalidOperationException("FileBasedSeriesService is not initialized. Series operations require file-based storage.");
        }

        _logger.LogDebug("Adding series {SeriesId} to file system", series.id);
        _ = SaveSeriesToFileAsync(series);
    }
    
    /// <summary>
    /// Updates an existing series in the file system.
    /// </summary>
    /// <param name="series">Series with updated data.</param>
    /// <exception cref="InvalidOperationException">Thrown when FileBasedSeriesService is not initialized.</exception>
    /// <remarks>File system is the source of truth for series data.</remarks>
    public void UpdateSeries(Series series)
    {
        if (_fileService == null)
        {
            _logger.LogError("Attempted to update series {SeriesId} but FileBasedSeriesService is not initialized", series?.id);
            throw new InvalidOperationException("FileBasedSeriesService is not initialized. Series operations require file-based storage.");
        }

        _logger.LogDebug("Updating series {SeriesId} in file system", series.id);
        _ = SaveSeriesToFileAsync(series);
    }
    
    /// <summary>
    /// Retrieves a series by its URN or UUID.
    /// </summary>
    /// <param name="id">Series URN or UUID.</param>
    /// <returns>Series if found, null otherwise.</returns>
    public Series? GetSeries(string id)
    {
        _logger.LogTrace("Getting series {SeriesId}", id);
        return _fileService?.GetSeries(id);
    }
    
    /// <summary>
    /// Lists all series from the file system.
    /// </summary>
    /// <returns>Enumerable of all series.</returns>
    public IEnumerable<Series> ListSeries()
    {
        _logger.LogTrace("Listing all series");
        return _fileService?.ListSeries() ?? Enumerable.Empty<Series>();
    }
    
    /// <summary>
    /// Lists series with pagination.
    /// </summary>
    /// <param name="offset">Number of series to skip.</param>
    /// <param name="limit">Maximum number of series to return.</param>
    /// <returns>Paginated enumerable of series.</returns>
    public IEnumerable<Series> ListSeries(int offset, int limit)
    {
        _logger.LogTrace("Listing series with offset {Offset} and limit {Limit}", offset, limit);
        return _fileService?.ListSeries().Skip(offset).Take(limit) ?? Enumerable.Empty<Series>();
    }
        
    /// <summary>
    /// Searches series with filters (no pagination).
    /// </summary>
    /// <param name="query">Text search query.</param>
    /// <param name="type">Media type filter.</param>
    /// <param name="tags">Tags filter.</param>
    /// <param name="status">Status filter.</param>
    /// <returns>Filtered enumerable of series.</returns>
    public IEnumerable<Series> SearchSeries(string? query, string? type, string[]? tags, string? status)
    {
        _logger.LogDebug("Searching series with query: {Query}, type: {Type}, tags: {Tags}, status: {Status}", 
            query, type, tags != null ? string.Join(",", tags) : "none", status);
        return _fileService?.SearchSeries(query, type, tags, status) ?? Enumerable.Empty<Series>();
    }

    /// <summary>
    /// Searches series with filters and pagination.
    /// </summary>
    /// <param name="query">Text search query.</param>
    /// <param name="type">Media type filter.</param>
    /// <param name="tags">Tags filter.</param>
    /// <param name="status">Status filter.</param>
    /// <param name="offset">Number of series to skip.</param>
    /// <param name="limit">Maximum number of series to return.</param>
    /// <returns>Filtered and paginated enumerable of series.</returns>
    public IEnumerable<Series> SearchSeries(string? query, string? type, string[]? tags, string? status, int offset, int limit)
    {
        _logger.LogDebug("Searching series with pagination - query: {Query}, type: {Type}, tags: {Tags}, status: {Status}, offset: {Offset}, limit: {Limit}", 
            query, type, tags != null ? string.Join(",", tags) : "none", status, offset, limit);
        return _fileService?.SearchSeries(query, type, tags, status).Skip(offset).Take(limit) ?? Enumerable.Empty<Series>();
    }
    
    /// <summary>
    /// Deletes a series from the file system.
    /// </summary>
    /// <param name="id">Series URN or UUID.</param>
    /// <exception cref="InvalidOperationException">Thrown when FileBasedSeriesService is not initialized.</exception>
    /// <remarks>File system is the source of truth for series data.</remarks>
    public void DeleteSeries(string id)
    {
        if (_fileService == null)
        {
            _logger.LogError("Attempted to delete series {SeriesId} but FileBasedSeriesService is not initialized", id);
            throw new InvalidOperationException("FileBasedSeriesService is not initialized. Series operations require file-based storage.");
        }

        _logger.LogInformation("Deleting series {SeriesId} from file system", id);
        _fileService.DeleteSeries(id);
    }
    
    /// <summary>
    /// Saves a series to the file system asynchronously.
    /// </summary>
    /// <param name="series">Series to save.</param>
    /// <returns>Task representing the asynchronous save operation.</returns>
    private async Task SaveSeriesToFileAsync(Series series)
    {
        if (_fileService == null)
        {
            _logger.LogWarning("Cannot save series {SeriesId} - FileBasedSeriesService not initialized", series?.id);
            return;
        }

        try
        {
            await _fileService.SaveSeriesAsync(series);
            _logger.LogDebug("Successfully saved series {SeriesId} to file system", series.id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save series {SeriesId} to file system", series.id);
            throw;
        }
    }
    
    #endregion
    
    #region Unit Operations (File-Based)
    
    /// <summary>
    /// Adds a new unit (chapter/episode) to the file system.
    /// </summary>
    /// <param name="unit">Unit to add.</param>
    /// <exception cref="InvalidOperationException">Thrown when FileBasedSeriesService is not initialized.</exception>
    /// <remarks>File system is the source of truth for unit data.</remarks>
    public void AddUnit(Unit unit)
    {
        if (_fileService == null)
        {
            _logger.LogError("Attempted to add unit {UnitId} but FileBasedSeriesService is not initialized", unit?.id);
            throw new InvalidOperationException("FileBasedSeriesService is not initialized. Unit operations require file-based storage.");
        }

        _logger.LogDebug("Adding unit {UnitId} to file system", unit.id);
        _ = SaveUnitToFileAsync(unit);
    }
    
    /// <summary>
    /// Updates an existing unit in the file system.
    /// </summary>
    /// <param name="unit">Unit with updated data.</param>
    /// <exception cref="InvalidOperationException">Thrown when FileBasedSeriesService is not initialized.</exception>
    /// <remarks>File system is the source of truth for unit data.</remarks>
    public void UpdateUnit(Unit unit)
    {
        if (_fileService == null)
        {
            _logger.LogError("Attempted to update unit {UnitId} but FileBasedSeriesService is not initialized", unit?.id);
            throw new InvalidOperationException("FileBasedSeriesService is not initialized. Unit operations require file-based storage.");
        }

        _logger.LogDebug("Updating unit {UnitId} in file system", unit.id);
        _ = SaveUnitToFileAsync(unit);
    }
    
    /// <summary>
    /// Lists all units for a specific series.
    /// </summary>
    /// <param name="seriesId">Series URN or UUID.</param>
    /// <returns>Enumerable of units for the series.</returns>
    public IEnumerable<Unit> ListUnits(string seriesId)
    {
        _logger.LogTrace("Listing units for series {SeriesId}", seriesId);
        return _fileService?.GetUnitsForSeries(seriesId) ?? Enumerable.Empty<Unit>();
    }
    
    /// <summary>
    /// Retrieves a unit by its URN or UUID.
    /// </summary>
    /// <param name="id">Unit URN or UUID.</param>
    /// <returns>Unit if found, null otherwise.</returns>
    public Unit? GetUnit(string id)
    {
        _logger.LogTrace("Getting unit {UnitId}", id);
        return _fileService?.GetUnit(id);
    }
    
    /// <summary>
    /// Deletes a unit from the file system.
    /// </summary>
    /// <param name="id">Unit URN or UUID.</param>
    /// <remarks>File system is the source of truth for unit data.</remarks>
    public void DeleteUnit(string id)
    {
        var unit = _fileService?.GetUnit(id);
        if (unit == null)
        {
            _logger.LogWarning("Attempted to delete unit {UnitId} but it was not found", id);
            return;
        }

        if (_fileService == null)
        {
            _logger.LogError("Attempted to delete unit {UnitId} but FileBasedSeriesService is not initialized", id);
            throw new InvalidOperationException("FileBasedSeriesService is not initialized. Unit operations require file-based storage.");
        }

        _logger.LogInformation("Deleting unit {UnitId} from series {SeriesId}", id, unit.series_id);
        _fileService.DeleteUnit(unit.series_id, id);
    }
    
    /// <summary>
    /// Saves a unit to the file system asynchronously.
    /// </summary>
    /// <param name="unit">Unit to save.</param>
    /// <returns>Task representing the asynchronous save operation.</returns>
    private async Task SaveUnitToFileAsync(Unit unit)
    {
        if (_fileService == null)
        {
            _logger.LogWarning("Cannot save unit {UnitId} - FileBasedSeriesService not initialized", unit?.id);
            return;
        }

        try
        {
            await _fileService.SaveUnitAsync(unit);
            _logger.LogDebug("Successfully saved unit {UnitId} to file system", unit.id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save unit {UnitId} to file system", unit.id);
            throw;
        }
    }
    
    #endregion
    
    #region Metadata Aggregation
    
    /// <summary>
    /// Aggregates series metadata from its units (tags, authors, scanlators, etc.).
    /// </summary>
    /// <param name="seriesId">Series URN or UUID.</param>
    /// <remarks>
    /// <para>Collects metadata from all units belonging to the series and updates the series accordingly.</para>
    /// <para>File system is updated with the aggregated metadata.</para>
    /// </remarks>
    public void AggregateSeriesMetadataFromUnits(string seriesId)
    {
        _logger.LogDebug("Aggregating metadata for series {SeriesId}", seriesId);

        var series = _fileService?.GetSeries(seriesId);
        if (series == null)
        {
            _logger.LogWarning("Cannot aggregate metadata - series {SeriesId} not found", seriesId);
            return;
        }
        
        var units = _fileService?.GetUnitsForSeries(seriesId) ?? Enumerable.Empty<Unit>();
        var unitCount = units.Count();
        
        _logger.LogDebug("Aggregating metadata from {UnitCount} units for series {SeriesId}", unitCount, seriesId);
        
        // Aggregate metadata from units using the aggregation service
        var aggregated = _metadataService.AggregateMetadata(series, units);
        
        // Save the updated series back to file system
        UpdateSeries(aggregated);
        
        _logger.LogInformation("Successfully aggregated metadata for series {SeriesId} from {UnitCount} units", seriesId, unitCount);
    }
    
    #endregion
    
    #region Database Delegated Operations
    
    // Edit Permissions - Stored in database
    public void GrantEditPermission(string targetUrn, string userUrn, string grantedBy)
    {
        _logger.LogInformation("Granting edit permission for {TargetUrn} to {UserUrn} (granted by {GrantedBy})", 
            targetUrn, userUrn, grantedBy);
        lock (_switchLock)
        {
            _current.GrantEditPermission(targetUrn, userUrn, grantedBy);
        }
    }
    
    public void RevokeEditPermission(string targetUrn, string userUrn)
    {
        _logger.LogInformation("Revoking edit permission for {TargetUrn} from {UserUrn}", targetUrn, userUrn);
        lock (_switchLock)
        {
            _current.RevokeEditPermission(targetUrn, userUrn);
        }
    }
    
    public bool HasEditPermission(string targetUrn, string userUrn)
    {
        lock (_switchLock)
        {
            return _current.HasEditPermission(targetUrn, userUrn);
        }
    }
    
    public string[] GetEditPermissions(string targetUrn)
    {
        lock (_switchLock)
        {
            return _current.GetEditPermissions(targetUrn);
        }
    }
    
    public EditPermission[] GetEditPermissionRecords(string targetUrn)
    {
        lock (_switchLock)
        {
            return _current.GetEditPermissionRecords(targetUrn);
        }
    }
    
    public void SyncEditPermissions()
    {
        _logger.LogInformation("Synchronizing edit permissions");
        lock (_switchLock)
        {
            _current.SyncEditPermissions();
        }
    }
    
    // Page Operations - Stored in database
    public void AddPage(string unitId, Page page)
    {
        _logger.LogDebug("Adding page to unit {UnitId}", unitId);
        lock (_switchLock)
        {
            _current.AddPage(unitId, page);
        }
    }
    
    public IEnumerable<Page> GetPages(string unitId)
    {
        lock (_switchLock)
        {
            return _current.GetPages(unitId);
        }
    }
    
    // Reading Progress - Stored in database
    public void UpdateProgress(string userId, ReadingProgress progress)
    {
        _logger.LogDebug("Updating reading progress for user {UserId} on series {SeriesUrn}", 
            userId, progress.series_urn);
        lock (_switchLock)
        {
            _current.UpdateProgress(userId, progress);
        }
    }
    
    public ReadingProgress? GetProgress(string userId, string seriesUrn)
    {
        lock (_switchLock)
        {
            return _current.GetProgress(userId, seriesUrn);
        }
    }
    
    public IEnumerable<ReadingProgress> GetLibrary(string userId)
    {
        lock (_switchLock)
        {
            return _current.GetLibrary(userId);
        }
    }
    
    public IEnumerable<ReadingProgress> GetHistory(string userId)
    {
        lock (_switchLock)
        {
            return _current.GetHistory(userId);
        }
    }
    
    // Social Features - Stored in database
    public void AddComment(Comment comment)
    {
        _logger.LogDebug("Adding comment {CommentId}", comment.id);
        lock (_switchLock)
        {
            _current.AddComment(comment);
        }
    }
    
    public IEnumerable<Comment> GetComments(string targetUrn)
    {
        lock (_switchLock)
        {
            return _current.GetComments(targetUrn);
        }
    }
    
    public void AddVote(string userId, Vote vote)
    {
        _logger.LogDebug("Adding vote by user {UserId} on {TargetId} (type: {TargetType})", 
            userId, vote.target_id, vote.target_type);
        lock (_switchLock)
        {
            _current.AddVote(userId, vote);
        }
    }
    
    // Collection Operations - Stored in database
    public void AddCollection(string userId, Collection collection)
    {
        _logger.LogInformation("Adding collection {CollectionId} for user {UserId}", collection.id, userId);
        lock (_switchLock)
        {
            _current.AddCollection(userId, collection);
        }
    }
    
    public IEnumerable<Collection> ListCollections(string userId)
    {
        lock (_switchLock)
        {
            return _current.ListCollections(userId);
        }
    }
    
    public Collection? GetCollection(string id)
    {
        lock (_switchLock)
        {
            return _current.GetCollection(id);
        }
    }
    
    public void UpdateCollection(Collection collection)
    {
        _logger.LogDebug("Updating collection {CollectionId}", collection.id);
        lock (_switchLock)
        {
            _current.UpdateCollection(collection);
        }
    }
    
    public void DeleteCollection(string id)
    {
        _logger.LogInformation("Deleting collection {CollectionId}", id);
        lock (_switchLock)
        {
            _current.DeleteCollection(id);
        }
    }
    
    // Moderation - Stored in database
    public void AddReport(Report report)
    {
        _logger.LogWarning("Report submitted for {TargetUrn}: {Reason} (severity: {Severity})", 
            report.target_urn, report.reason, report.severity);
        lock (_switchLock)
        {
            _current.AddReport(report);
        }
    }
    
    // System Configuration - Stored in database
    public SystemConfig GetSystemConfig()
    {
        lock (_switchLock)
        {
            return _current.GetSystemConfig();
        }
    }
    
    public void UpdateSystemConfig(SystemConfig config)
    {
        _logger.LogInformation("Updating system configuration");
        lock (_switchLock)
        {
            _current.UpdateSystemConfig(config);
        }
    }
    
    public SystemStats GetSystemStats()
    {
        lock (_switchLock)
        {
            return _current.GetSystemStats();
        }
    }
    
    // User Management - Stored in database
    public void AddUser(User user)
    {
        _logger.LogInformation("Adding user {UserId} with username {Username}", user.id, user.username);
        lock (_switchLock)
        {
            _current.AddUser(user);
        }
    }
    
    public void UpdateUser(User user)
    {
        _logger.LogDebug("Updating user {UserId}", user.id);
        lock (_switchLock)
        {
            _current.UpdateUser(user);
        }
    }
    
    public User? GetUser(string id)
    {
        lock (_switchLock)
        {
            return _current.GetUser(id);
        }
    }
    
    public User? GetUserByUsername(string username)
    {
        lock (_switchLock)
        {
            return _current.GetUserByUsername(username);
        }
    }
    
    public IEnumerable<User> ListUsers()
    {
        lock (_switchLock)
        {
            return _current.ListUsers();
        }
    }
    
    public void DeleteUser(string id)
    {
        _logger.LogWarning("Deleting user {UserId}", id);
        lock (_switchLock)
        {
            _current.DeleteUser(id);
        }
    }
    
    public void DeleteUserHistory(string userId)
    {
        _logger.LogInformation("Deleting history for user {UserId}", userId);
        lock (_switchLock)
        {
            _current.DeleteUserHistory(userId);
        }
    }
    
    public void AnonymizeUserContent(string userId)
    {
        _logger.LogInformation("Anonymizing content for user {UserId}", userId);
        lock (_switchLock)
        {
            _current.AnonymizeUserContent(userId);
        }
    }
    
    public bool IsAdminSet()
    {
        lock (_switchLock)
        {
            return _current.IsAdminSet();
        }
    }
    
    // Passkey / WebAuthn - Stored in database
    public void AddPasskey(Passkey passkey)
    {
        _logger.LogInformation("Adding passkey {PasskeyId} for user {UserId}", passkey.id, passkey.user_id);
        lock (_switchLock)
        {
            _current.AddPasskey(passkey);
        }
    }
    
    public void UpdatePasskey(Passkey passkey)
    {
        _logger.LogDebug("Updating passkey {PasskeyId}", passkey.id);
        lock (_switchLock)
        {
            _current.UpdatePasskey(passkey);
        }
    }
    
    public IEnumerable<Passkey> GetPasskeysByUser(string userId)
    {
        lock (_switchLock)
        {
            return _current.GetPasskeysByUser(userId);
        }
    }
    
    public Passkey? GetPasskeyByCredentialId(string credentialId)
    {
        lock (_switchLock)
        {
            return _current.GetPasskeyByCredentialId(credentialId);
        }
    }
    
    public Passkey? GetPasskey(string id)
    {
        lock (_switchLock)
        {
            return _current.GetPasskey(id);
        }
    }
    
    public void DeletePasskey(string id)
    {
        _logger.LogInformation("Deleting passkey {PasskeyId}", id);
        lock (_switchLock)
        {
            _current.DeletePasskey(id);
        }
    }
    
    // Node Metadata - Stored in database
    public NodeMetadata GetNodeMetadata()
    {
        lock (_switchLock)
        {
            return _current.GetNodeMetadata();
        }
    }
    
    public void UpdateNodeMetadata(NodeMetadata metadata)
    {
        _logger.LogInformation("Updating node metadata: {NodeName}", metadata.node_name);
        lock (_switchLock)
        {
            _current.UpdateNodeMetadata(metadata);
        }
    }
    
    // Taxonomy Configuration - Stored in database
    public TaxonomyConfig GetTaxonomyConfig()
    {
        lock (_switchLock)
        {
            return _current.GetTaxonomyConfig();
        }
    }
    
    public void UpdateTaxonomyConfig(TaxonomyConfig config)
    {
        _logger.LogInformation("Updating taxonomy configuration");
        lock (_switchLock)
        {
            _current.UpdateTaxonomyConfig(config);
        }
    }
    
    // Data Management
    public void ResetAllData()
    {
        _logger.LogWarning("Resetting all repository data (DESTRUCTIVE OPERATION)");
        lock (_switchLock)
        {
            _current.ResetAllData();
        }
    }
    
    public void ResetDatabase()
    {
        _logger.LogWarning("Resetting database (DESTRUCTIVE OPERATION)");
        lock (_switchLock)
        {
            if (_current is PostgresRepository pgRepo)
            {
                pgRepo.ResetDatabase();
            }
            else
            {
                _current.ResetAllData();
            }
        }
    }
    
    #endregion
}
