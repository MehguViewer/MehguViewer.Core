using System.Data;
using MehguViewer.Core.Shared;
using MehguViewer.Core.Services;
using MehguViewer.Core.Workers;
using MehguViewer.Core.Helpers;
using Npgsql;
using System.Text.Json;

namespace MehguViewer.Core.Infrastructures;

/// <summary>
/// PostgreSQL-based repository implementation providing persistent storage for MehguViewer data.
/// </summary>
/// <remarks>
/// <para><strong>Storage Strategy:</strong> Uses JSONB columns for flexible schema evolution while maintaining ACID compliance.</para>
/// <para><strong>Connection Management:</strong> Leverages NpgsqlDataSource for efficient connection pooling.</para>
/// <para><strong>Security:</strong> All queries use parameterized commands to prevent SQL injection attacks.</para>
/// </remarks>
public class PostgresRepository : IRepository
{
    #region Fields
    
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<PostgresRepository> _logger;
    private readonly MetadataAggregationService _metadataService;
    
    #endregion
    
    #region Constructors
    
    /// <summary>
    /// Initializes a new instance of the <see cref="PostgresRepository"/> class using configuration.
    /// </summary>
    /// <param name="configuration">Application configuration containing connection string.</param>
    /// <param name="logger">Logger instance for diagnostic output.</param>
    /// <param name="metadataService">Service for aggregating metadata from units to series.</param>
    public PostgresRepository(IConfiguration configuration, ILogger<PostgresRepository> logger, MetadataAggregationService metadataService)
        : this(configuration.GetConnectionString("DefaultConnection"), logger, metadataService)
    {
        _logger.LogDebug("PostgresRepository initialized via IConfiguration");
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgresRepository"/> class using a connection string.
    /// </summary>
    /// <param name="connectionString">PostgreSQL connection string.</param>
    /// <param name="logger">Logger instance for diagnostic output.</param>
    /// <param name="metadataService">Service for aggregating metadata from units to series.</param>
    /// <exception cref="InvalidOperationException">Thrown when connection string is null or empty.</exception>
    /// <exception cref="ArgumentNullException">Thrown when metadataService is null.</exception>
    public PostgresRepository(string? connectionString, ILogger<PostgresRepository> logger, MetadataAggregationService metadataService)
    {
        if (string.IsNullOrEmpty(connectionString))
        {
            _logger?.LogError("Connection string is null or empty");
            throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        }
        
        _dataSource = NpgsqlDataSource.Create(connectionString);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metadataService = metadataService ?? throw new ArgumentNullException(nameof(metadataService));
        
        _logger.LogInformation("PostgresRepository initializing with connection string");
        InitializeDatabase();
        _logger.LogInformation("PostgresRepository initialized successfully");
    }
    
    #endregion
    
    #region Database Initialization & Utilities

    /// <summary>
    /// Initializes the database schema, creates tables, indexes, and seeds default configuration.
    /// </summary>
    /// <remarks>
    /// This method is idempotent - it can be safely called multiple times.
    /// Uses CREATE IF NOT EXISTS to avoid errors on subsequent calls.
    /// </remarks>
    private void InitializeDatabase()
    {
        _logger.LogDebug("Starting database initialization");
        
        try
        {
            using var conn = _dataSource.OpenConnection();
            _logger.LogDebug("Database connection established");
            
            // 1. Create Schema
            CreateSchema(conn);
            
            // 2. Seed System Config
            SeedSystemConfig(conn);

            // 3. Seed Node Metadata
            SeedNodeMetadata(conn);
            
            _logger.LogInformation("Database initialization completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database initialization failed");
            throw;
        }
    }

    /// <summary>
    /// Creates all database tables and indexes.
    /// </summary>
    /// <param name="conn">Active database connection.</param>
    private void CreateSchema(NpgsqlConnection conn)
    {
        _logger.LogDebug("Creating database schema");
        
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS series (
                id TEXT PRIMARY KEY,
                data JSONB NOT NULL
            );
            CREATE TABLE IF NOT EXISTS units (
                id TEXT PRIMARY KEY,
                series_id TEXT NOT NULL,
                data JSONB NOT NULL
            );
            CREATE TABLE IF NOT EXISTS pages (
                unit_id TEXT PRIMARY KEY,
                data JSONB NOT NULL
            );
            CREATE TABLE IF NOT EXISTS users (
                id TEXT PRIMARY KEY,
                username TEXT UNIQUE NOT NULL,
                data JSONB NOT NULL
            );
            CREATE TABLE IF NOT EXISTS progress (
                user_id TEXT NOT NULL,
                series_urn TEXT NOT NULL,
                data JSONB NOT NULL,
                updated_at BIGINT NOT NULL,
                PRIMARY KEY (user_id, series_urn)
            );
            CREATE TABLE IF NOT EXISTS comments (
                id TEXT PRIMARY KEY,
                target_urn TEXT NOT NULL,
                data JSONB NOT NULL
            );
            CREATE TABLE IF NOT EXISTS votes (
                id TEXT PRIMARY KEY,
                user_id TEXT NOT NULL,
                data JSONB NOT NULL
            );
            CREATE TABLE IF NOT EXISTS collections (
                id TEXT PRIMARY KEY,
                user_id TEXT NOT NULL,
                data JSONB NOT NULL
            );
            CREATE TABLE IF NOT EXISTS reports (
                id TEXT PRIMARY KEY,
                data JSONB NOT NULL,
                created_at TIMESTAMP DEFAULT NOW()
            );
            CREATE TABLE IF NOT EXISTS system_config (
                key TEXT PRIMARY KEY,
                data JSONB NOT NULL
            );
            CREATE TABLE IF NOT EXISTS node_metadata (
                key TEXT PRIMARY KEY,
                data JSONB NOT NULL
            );
            CREATE TABLE IF NOT EXISTS storage_settings (
                key TEXT PRIMARY KEY,
                data JSONB NOT NULL
            );
            CREATE TABLE IF NOT EXISTS passkeys (
                id TEXT PRIMARY KEY,
                user_id TEXT NOT NULL,
                credential_id TEXT UNIQUE NOT NULL,
                data JSONB NOT NULL
            );
            CREATE TABLE IF NOT EXISTS taxonomy_config (
                key TEXT PRIMARY KEY,
                data JSONB NOT NULL
            );
            CREATE TABLE IF NOT EXISTS edit_permissions (
                target_urn TEXT NOT NULL,
                user_urn TEXT NOT NULL,
                granted_by TEXT NOT NULL,
                granted_at TIMESTAMP DEFAULT NOW(),
                PRIMARY KEY (target_urn, user_urn)
            );
            CREATE INDEX IF NOT EXISTS idx_edit_permissions_target ON edit_permissions(target_urn);
            CREATE INDEX IF NOT EXISTS idx_edit_permissions_user ON edit_permissions(user_urn);
            
            -- Indexes for performance
            CREATE INDEX IF NOT EXISTS idx_units_series_id ON units(series_id);
            CREATE INDEX IF NOT EXISTS idx_pages_unit_id ON pages(unit_id);
            CREATE INDEX IF NOT EXISTS idx_progress_user_id ON progress(user_id);
            CREATE INDEX IF NOT EXISTS idx_comments_target_urn ON comments(target_urn);
            CREATE INDEX IF NOT EXISTS idx_votes_user_id ON votes(user_id);
            CREATE INDEX IF NOT EXISTS idx_collections_user_id ON collections(user_id);
            CREATE INDEX IF NOT EXISTS idx_passkeys_user_id ON passkeys(user_id);
            CREATE INDEX IF NOT EXISTS idx_passkeys_credential_id ON passkeys(credential_id);
        ";
        cmd.ExecuteNonQuery();
        
        _logger.LogDebug("Database schema created successfully");
    }

    /// <summary>
    /// Seeds default system configuration if not already present.
    /// </summary>
    /// <param name="conn">Active database connection.</param>
    private void SeedSystemConfig(NpgsqlConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM system_config WHERE key = 'default'";
        var exists = cmd.ExecuteScalar() != null;
        
        if (!exists)
        {
            _logger.LogInformation("Seeding default system configuration");
            
            var defaultConfig = new SystemConfig(
                is_setup_complete: false, 
                registration_open: true, 
                maintenance_mode: false, 
                motd_message: "Welcome to MehguViewer Core", 
                default_language_filter: new[] { "en" },
                max_login_attempts: 5,
                lockout_duration_minutes: 15,
                token_expiry_hours: 24,
                cloudflare_enabled: false,
                cloudflare_site_key: "",
                cloudflare_secret_key: "",
                require_2fa_passkey: false,
                require_password_for_danger_zone: true
            );
            var json = ToJson(defaultConfig);
            _logger.LogDebug("Seeding system_config with: {Json}", json);
            
            cmd.Parameters.Clear();
            cmd.CommandText = "INSERT INTO system_config (key, data) VALUES ('default', $1::jsonb)";
            cmd.Parameters.AddWithValue(json);
            cmd.ExecuteNonQuery();
            
            _logger.LogInformation("System configuration seeded successfully");
        }
        else
        {
            _logger.LogDebug("System configuration already exists, skipping seed");
        }
    }

    /// <summary>
    /// Seeds default node metadata if not already present.
    /// </summary>
    /// <param name="conn">Active database connection.</param>
    private void SeedNodeMetadata(NpgsqlConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM node_metadata WHERE key = 'default'";
        var exists = cmd.ExecuteScalar() != null;
        
        if (!exists)
        {
            _logger.LogInformation("Seeding default node metadata");
            
            var defaultMetadata = new NodeMetadata(
                "1.0.0",
                "MehguViewer Core",
                "A MehguViewer Core Node",
                "https://auth.mehgu.example.com",
                new NodeCapabilities(true, true, true),
                new NodeMaintainer("Admin", "admin@example.com")
            );
            
            cmd.Parameters.Clear();
            cmd.CommandText = "INSERT INTO node_metadata (key, data) VALUES ('default', $1::jsonb)";
            cmd.Parameters.AddWithValue(ToJson(defaultMetadata));
            cmd.ExecuteNonQuery();
            
            _logger.LogInformation("Node metadata seeded successfully");
        }
        else
        {
            _logger.LogDebug("Node metadata already exists, skipping seed");
        }
    }

    /// <summary>
    /// Resets the database by dropping and recreating all tables.
    /// </summary>
    /// <remarks>
    /// <strong>WARNING:</strong> This operation is destructive and will delete all data.
    /// Use only in development/testing environments.
    /// </remarks>
    public void ResetDatabase()
    {
        _logger.LogWarning("Resetting database - ALL DATA WILL BE LOST");
        
        try
        {
            using var conn = _dataSource.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                DROP TABLE IF EXISTS series CASCADE;
                DROP TABLE IF EXISTS units CASCADE;
                DROP TABLE IF EXISTS pages CASCADE;
                DROP TABLE IF EXISTS users CASCADE;
                DROP TABLE IF EXISTS progress CASCADE;
                DROP TABLE IF EXISTS system_config CASCADE;
                DROP TABLE IF EXISTS node_metadata CASCADE;
                DROP TABLE IF EXISTS comments CASCADE;
                DROP TABLE IF EXISTS votes CASCADE;
                DROP TABLE IF EXISTS collections CASCADE;
                DROP TABLE IF EXISTS reports CASCADE;
                DROP TABLE IF EXISTS passkeys CASCADE;
                DROP TABLE IF EXISTS edit_permissions CASCADE;
                DROP TABLE IF EXISTS taxonomy_config CASCADE;
                DROP TABLE IF EXISTS storage_settings CASCADE;
            ";
            cmd.ExecuteNonQuery();
            
            _logger.LogInformation("All tables dropped, reinitializing schema");
            InitializeDatabase();
            _logger.LogInformation("Database reset completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database reset failed");
            throw;
        }
    }

    /// <summary>
    /// Checks if the database contains any user data or has been set up.
    /// </summary>
    /// <returns>True if database has data or setup is complete, false otherwise.</returns>
    public bool HasData()
    {
        _logger.LogDebug("Checking if database has data");
        
        try
        {
            using var conn = _dataSource.OpenConnection();
            using var cmd = conn.CreateCommand();
            
            // Check for actual user data, not auto-seeded system_config
            cmd.CommandText = "SELECT COUNT(*) FROM users";
            var userCount = (long)cmd.ExecuteScalar()!;

            // Also check if setup has been completed (indicates actual user setup)
            cmd.CommandText = "SELECT data->>'is_setup_complete' FROM system_config WHERE key = 'default' LIMIT 1";
            var setupComplete = cmd.ExecuteScalar()?.ToString() == "true";

            var hasData = userCount > 0 || setupComplete;
            _logger.LogDebug("Database has data: {HasData} (Users: {UserCount}, Setup complete: {SetupComplete})", 
                hasData, userCount, setupComplete);
            
            return hasData;
        }
        catch (Exception ex)
        {
            // If tables don't exist, it has no data
            _logger.LogDebug(ex, "Error checking database data (tables may not exist)");
            return false;
        }
    }

    /// <summary>
    /// Seeds the database with demo data for development and testing purposes.
    /// </summary>
    public void SeedDebugData()
    {
        _logger.LogInformation("Seeding debug data");
        
        try
        {
            // Seed a demo series
            var seriesId = UrnHelper.CreateSeriesUrn();
            var series = new Series(
                id: seriesId,
                federation_ref: "urn:mvn:node:local",
                title: "Demo Manga",
                description: "A demo manga for testing purposes. This contains sample chapters and pages.",
                poster: new Poster("https://placehold.co/400x600", "Demo Poster"),
                media_type: MediaTypes.Photo,
                external_links: new Dictionary<string, string>(),
                reading_direction: ReadingDirections.RTL,
                tags: ["Action", "Fantasy"],
                content_warnings: [],
                authors: [new Author("author-1", "Demo Author", "Author")],
                scanlators: [new Scanlator("scanlator-1", "Demo Scans", ScanlatorRole.Both)],
                groups: null,
                alt_titles: ["Demo Alternative Title"],
                status: "Ongoing",
                year: 2024,
                created_by: "urn:mvn:user:system",
                created_at: DateTime.UtcNow,
                updated_at: DateTime.UtcNow
            );
            AddSeries(series);

            // Add a unit/chapter
            var unitId = Guid.NewGuid().ToString();
            var unit = new Unit(
                unitId,
                seriesId,
                1,
                "Chapter 1: The Beginning",
                DateTime.UtcNow
            );
            AddUnit(unit);

            // Seed Pages
            var pages = new List<Page>();
            for (int i = 1; i <= 5; i++)
            {
                pages.Add(new Page(i, UrnHelper.CreateAssetUrn(), $"https://placehold.co/800x1200?text=Page+{i}"));
            }
            
            using var conn = _dataSource.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO pages (unit_id, data) VALUES ($1, $2::jsonb) ON CONFLICT (unit_id) DO UPDATE SET data = $2::jsonb";
            cmd.Parameters.AddWithValue(unitId);
            cmd.Parameters.AddWithValue(ToJson(pages));
            cmd.ExecuteNonQuery();
            
            _logger.LogInformation("Debug data seeded successfully with series {SeriesId}", seriesId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to seed debug data");
            throw;
        }
    }

    /// <summary>
    /// Serializes an object to JSON using the application's JSON serializer context.
    /// </summary>
    /// <typeparam name="T">Type of object to serialize.</typeparam>
    /// <param name="obj">Object to serialize.</param>
    /// <returns>JSON string representation.</returns>
    private string ToJson<T>(T obj)
    {
        try
        {
            var typeInfo = (System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>)AppJsonSerializerContext.Default.GetTypeInfo(typeof(T))!;
            return JsonSerializer.Serialize(obj, typeInfo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to serialize object of type {Type}", typeof(T).Name);
            throw;
        }
    }

    /// <summary>
    /// Deserializes JSON to an object using the application's JSON serializer context.
    /// </summary>
    /// <typeparam name="T">Type to deserialize to.</typeparam>
    /// <param name="json">JSON string to deserialize.</param>
    /// <returns>Deserialized object or null if deserialization fails.</returns>
    private T? FromJson<T>(string json)
    {
        try
        {
            var typeInfo = (System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>)AppJsonSerializerContext.Default.GetTypeInfo(typeof(T))!;
            return JsonSerializer.Deserialize(json, typeInfo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize JSON to type {Type}: {Json}", typeof(T).Name, json);
            return default;
        }
    }
    
    #endregion
    
    #region Series Operations

    /// <summary>
    /// Adds a new series to the repository.
    /// </summary>
    /// <param name="series">Series to add.</param>
    /// <exception cref="ArgumentNullException">Thrown when series is null.</exception>
    public void AddSeries(Series series)
    {
        if (series == null)
        {
            _logger.LogError("Attempted to add null series");
            throw new ArgumentNullException(nameof(series));
        }
        
        _logger.LogDebug("Adding series: {SeriesId}", series.id);
        
        try
        {
            using var conn = _dataSource.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO series (id, data) VALUES ($1, $2::jsonb) ON CONFLICT (id) DO NOTHING";
            cmd.Parameters.AddWithValue(series.id);
            cmd.Parameters.AddWithValue(ToJson(series));
            var rowsAffected = cmd.ExecuteNonQuery();
            
            if (rowsAffected > 0)
            {
                _logger.LogInformation("Series added successfully: {SeriesId} - {Title}", series.id, series.title);
            }
            else
            {
                _logger.LogWarning("Series already exists, skipping: {SeriesId}", series.id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add series: {SeriesId}", series.id);
            throw;
        }
    }

    /// <summary>
    /// Updates an existing series in the repository.
    /// </summary>
    /// <param name="series">Series to update.</param>
    /// <exception cref="ArgumentNullException">Thrown when series is null.</exception>
    public void UpdateSeries(Series series)
    {
        if (series == null)
        {
            _logger.LogError("Attempted to update null series");
            throw new ArgumentNullException(nameof(series));
        }
        
        _logger.LogDebug("Updating series: {SeriesId}", series.id);
        
        try
        {
            using var conn = _dataSource.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE series SET data = $2::jsonb WHERE id = $1";
            cmd.Parameters.AddWithValue(series.id);
            cmd.Parameters.AddWithValue(ToJson(series));
            var rowsAffected = cmd.ExecuteNonQuery();
            
            if (rowsAffected > 0)
            {
                _logger.LogInformation("Series updated successfully: {SeriesId} - {Title}", series.id, series.title);
            }
            else
            {
                _logger.LogWarning("Series not found for update: {SeriesId}", series.id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update series: {SeriesId}", series.id);
            throw;
        }
    }

    /// <summary>
    /// Retrieves a series by its ID.
    /// </summary>
    /// <param name="id">Series ID (URN format).</param>
    /// <returns>Series if found, null otherwise.</returns>
    public Series? GetSeries(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            _logger.LogWarning("GetSeries called with null or empty ID");
            return null;
        }
        
        _logger.LogDebug("Retrieving series: {SeriesId}", id);
        
        try
        {
            using var conn = _dataSource.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT data FROM series WHERE id = $1";
            cmd.Parameters.AddWithValue(id);
            using var reader = cmd.ExecuteReader();
            
            if (reader.Read())
            {
                var series = FromJson<Series>(reader.GetString(0));
                _logger.LogDebug("Series found: {SeriesId}", id);
                return series;
            }
            
            _logger.LogDebug("Series not found: {SeriesId}", id);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve series: {SeriesId}", id);
            throw;
        }
    }

    /// <summary>
    /// Lists series with pagination.
    /// </summary>
    /// <param name="offset">Number of series to skip.</param>
    /// <param name="limit">Maximum number of series to return.</param>
    /// <returns>Collection of series ordered by update time (newest first).</returns>
    public IEnumerable<Series> ListSeries(int offset = 0, int limit = 20)
    {
        _logger.LogDebug("Listing series with offset={Offset}, limit={Limit}", offset, limit);
        
        if (offset < 0)
        {
            _logger.LogWarning("Invalid offset {Offset}, using 0", offset);
            offset = 0;
        }
        
        if (limit <= 0 || limit > 100)
        {
            _logger.LogWarning("Invalid limit {Limit}, using 20", limit);
            limit = 20;
        }
        
        try
        {
            var list = new List<Series>();
            using var conn = _dataSource.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT data FROM series ORDER BY data->>'updated_at' DESC OFFSET $1 LIMIT $2";
            cmd.Parameters.AddWithValue(offset);
            cmd.Parameters.AddWithValue(limit);
            using var reader = cmd.ExecuteReader();
            
            while (reader.Read())
            {
                var s = FromJson<Series>(reader.GetString(0));
                if (s != null) list.Add(s);
            }
            
            _logger.LogDebug("Retrieved {Count} series", list.Count);
            return list;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list series");
            throw;
        }
    }

    /// <summary>
    /// Searches for series based on various criteria with pagination.
    /// </summary>
    /// <param name="query">Text query to match against series title (case-insensitive).</param>
    /// <param name="type">Media type filter (Photo, Text, Video).</param>
    /// <param name="tags">Array of tags - series must contain ALL specified tags.</param>
    /// <param name="status">Status filter (e.g., "Ongoing", "Completed").</param>
    /// <param name="offset">Number of results to skip.</param>
    /// <param name="limit">Maximum number of results to return.</param>
    /// <returns>Collection of matching series ordered by update time (newest first).</returns>
    public IEnumerable<Series> SearchSeries(string? query, string? type, string[]? tags, string? status, int offset = 0, int limit = 20)
    {
        _logger.LogDebug("Searching series with query={Query}, type={Type}, tags={Tags}, status={Status}, offset={Offset}, limit={Limit}",
            query, type, tags != null ? string.Join(",", tags) : "null", status, offset, limit);
        
        if (offset < 0)
        {
            _logger.LogWarning("Invalid offset {Offset}, using 0", offset);
            offset = 0;
        }
        
        if (limit <= 0 || limit > 100)
        {
            _logger.LogWarning("Invalid limit {Limit}, using 20", limit);
            limit = 20;
        }
        
        try
        {
            var list = new List<Series>();
            using var conn = _dataSource.OpenConnection();
            using var cmd = conn.CreateCommand();
            
            // Build query with proper parameter handling
            var paramsList = new List<NpgsqlParameter>();
            var whereClauses = new List<string>();

            if (!string.IsNullOrEmpty(query))
            {
                whereClauses.Add("data->>'title' ILIKE @query");
                paramsList.Add(new NpgsqlParameter("query", $"%{query}%"));
            }
            
            if (!string.IsNullOrEmpty(type))
            {
                whereClauses.Add("data->>'media_type' = @type");
                paramsList.Add(new NpgsqlParameter("type", type));
            }
            
            if (tags != null && tags.Length > 0)
            {
                // JSONB containment operator @> checks if series tags contain all specified tags
                var tagsJson = JsonSerializer.Serialize(tags);
                whereClauses.Add("data->'tags' @> @tags::jsonb");
                paramsList.Add(new NpgsqlParameter("tags", tagsJson));
            }
            
            if (!string.IsNullOrEmpty(status))
            {
                whereClauses.Add("data->>'status' = @status");
                paramsList.Add(new NpgsqlParameter("status", status));
            }

            var sql = whereClauses.Count > 0
                ? "SELECT data FROM series WHERE " + string.Join(" AND ", whereClauses)
                : "SELECT data FROM series";

            sql += " ORDER BY data->>'updated_at' DESC OFFSET @offset LIMIT @limit";
            paramsList.Add(new NpgsqlParameter("offset", offset));
            paramsList.Add(new NpgsqlParameter("limit", limit));

            cmd.CommandText = sql;
            foreach (var p in paramsList)
            {
                cmd.Parameters.Add(p);
            }

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var s = FromJson<Series>(reader.GetString(0));
                if (s != null) list.Add(s);
            }
            
            _logger.LogDebug("Search returned {Count} series", list.Count);
            return list;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search series");
            throw;
        }
    }

    /// <summary>
    /// Deletes a series and all its associated units and pages.
    /// </summary>
    /// <param name="id">Series ID (URN format).</param>
    /// <remarks>
    /// This operation cascades to delete all units and pages belonging to the series.
    /// </remarks>
    public void DeleteSeries(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            _logger.LogWarning("DeleteSeries called with null or empty ID");
            return;
        }
        
        _logger.LogInformation("Deleting series and all associated data: {SeriesId}", id);
        
        try
        {
            using var conn = _dataSource.OpenConnection();
            using var transaction = conn.BeginTransaction();
            
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = transaction;
                
                // Delete pages for units of this series first
                cmd.CommandText = "DELETE FROM pages WHERE unit_id IN (SELECT id FROM units WHERE series_id = $1)";
                cmd.Parameters.AddWithValue(id);
                var pagesDeleted = cmd.ExecuteNonQuery();
                _logger.LogDebug("Deleted {Count} page records for series {SeriesId}", pagesDeleted, id);
                
                // Delete units
                cmd.Parameters.Clear();
                cmd.CommandText = "DELETE FROM units WHERE series_id = $1";
                cmd.Parameters.AddWithValue(id);
                var unitsDeleted = cmd.ExecuteNonQuery();
                _logger.LogDebug("Deleted {Count} units for series {SeriesId}", unitsDeleted, id);
                
                // Delete series
                cmd.Parameters.Clear();
                cmd.CommandText = "DELETE FROM series WHERE id = $1";
                cmd.Parameters.AddWithValue(id);
                var seriesDeleted = cmd.ExecuteNonQuery();
                
                transaction.Commit();
                
                if (seriesDeleted > 0)
                {
                    _logger.LogInformation("Series deleted successfully: {SeriesId} ({UnitsDeleted} units, {PagesDeleted} page records)",
                        id, unitsDeleted, pagesDeleted);
                }
                else
                {
                    _logger.LogWarning("Series not found for deletion: {SeriesId}", id);
                }
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete series: {SeriesId}", id);
            throw;
        }
    }
    
    #endregion
    
    #region Unit Operations

    /// <summary>
    /// Adds a new unit (chapter/episode/volume) to a series.
    /// </summary>
    /// <param name="unit">Unit to add.</param>
    /// <exception cref="ArgumentNullException">Thrown when unit is null.</exception>
    public void AddUnit(Unit unit)
    {
        if (unit == null)
        {
            _logger.LogError("Attempted to add null unit");
            throw new ArgumentNullException(nameof(unit));
        }
        
        _logger.LogDebug("Adding unit: {UnitId} to series {SeriesId}", unit.id, unit.series_id);
        
        try
        {
            using var conn = _dataSource.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO units (id, series_id, data) VALUES ($1, $2, $3::jsonb) ON CONFLICT (id) DO NOTHING";
            cmd.Parameters.AddWithValue(unit.id);
            cmd.Parameters.AddWithValue(unit.series_id);
            cmd.Parameters.AddWithValue(ToJson(unit));
            var rowsAffected = cmd.ExecuteNonQuery();
            
            if (rowsAffected > 0)
            {
                _logger.LogInformation("Unit added successfully: {UnitId} - {Title} (Series: {SeriesId})",
                    unit.id, unit.title, unit.series_id);
            }
            else
            {
                _logger.LogWarning("Unit already exists, skipping: {UnitId}", unit.id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add unit: {UnitId}", unit.id);
            throw;
        }
    }

    /// <summary>
    /// Updates an existing unit in the repository.
    /// </summary>
    /// <param name="unit">Unit to update.</param>
    /// <exception cref="ArgumentNullException">Thrown when unit is null.</exception>
    public void UpdateUnit(Unit unit)
    {
        if (unit == null)
        {
            _logger.LogError("Attempted to update null unit");
            throw new ArgumentNullException(nameof(unit));
        }
        
        _logger.LogDebug("Updating unit: {UnitId}", unit.id);
        
        try
        {
            using var conn = _dataSource.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE units SET data = $2::jsonb WHERE id = $1";
            cmd.Parameters.AddWithValue(unit.id);
            cmd.Parameters.AddWithValue(ToJson(unit));
            var rowsAffected = cmd.ExecuteNonQuery();
            
            if (rowsAffected > 0)
            {
                _logger.LogInformation("Unit updated successfully: {UnitId} - {Title}", unit.id, unit.title);
            }
            else
            {
                _logger.LogWarning("Unit not found for update: {UnitId}", unit.id);
            }
            
            // Trigger metadata aggregation for parent series
            AggregateSeriesMetadataFromUnits(unit.series_id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update unit: {UnitId}", unit.id);
            throw;
        }
    }

    /// <summary>
    /// Deletes a unit and all its associated pages.
    /// </summary>
    /// <param name="id">Unit ID (URN format).</param>
    /// <remarks>
    /// This operation triggers metadata aggregation on the parent series.
    /// </remarks>
    public void DeleteUnit(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            _logger.LogWarning("DeleteUnit called with null or empty ID");
            return;
        }
        
        _logger.LogInformation("Deleting unit: {UnitId}", id);
        
        try
        {
            var unit = GetUnit(id);
            
            using var conn = _dataSource.OpenConnection();
            using var transaction = conn.BeginTransaction();
            
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = transaction;
                
                // Delete pages for this unit first
                cmd.CommandText = "DELETE FROM pages WHERE unit_id = $1";
                cmd.Parameters.AddWithValue(id);
                var pagesDeleted = cmd.ExecuteNonQuery();
                _logger.LogDebug("Deleted {Count} page records for unit {UnitId}", pagesDeleted, id);
                
                // Delete unit
                cmd.Parameters.Clear();
                cmd.CommandText = "DELETE FROM units WHERE id = $1";
                cmd.Parameters.AddWithValue(id);
                var unitsDeleted = cmd.ExecuteNonQuery();
                
                transaction.Commit();
                
                if (unitsDeleted > 0)
                {
                    _logger.LogInformation("Unit deleted successfully: {UnitId} ({PagesDeleted} page records)",
                        id, pagesDeleted);
                }
                else
                {
                    _logger.LogWarning("Unit not found for deletion: {UnitId}", id);
                }
                
                // Trigger metadata aggregation for parent series
                if (unit != null)
                {
                    AggregateSeriesMetadataFromUnits(unit.series_id);
                }
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete unit: {UnitId}", id);
            throw;
        }
    }

    /// <summary>
    /// Lists all units for a specific series, ordered by unit number.
    /// </summary>
    /// <param name="seriesId">Series ID (URN format).</param>
    /// <returns>Collection of units ordered by unit number.</returns>
    /// <summary>
    /// Lists all units for a specific series, ordered by unit number.
    /// </summary>
    /// <param name="seriesId">Series ID (URN format).</param>
    /// <returns>Collection of units ordered by unit number.</returns>
    public IEnumerable<Unit> ListUnits(string seriesId)
    {
        if (string.IsNullOrWhiteSpace(seriesId))
        {
            _logger.LogWarning("ListUnits called with null or empty series ID");
            return Enumerable.Empty<Unit>();
        }
        
        _logger.LogDebug("Listing units for series: {SeriesId}", seriesId);
        
        try
        {
            var list = new List<Unit>();
            using var conn = _dataSource.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT data FROM units WHERE series_id = $1";
            cmd.Parameters.AddWithValue(seriesId);
            using var reader = cmd.ExecuteReader();
            
            while (reader.Read())
            {
                var u = FromJson<Unit>(reader.GetString(0));
                if (u != null) list.Add(u);
            }
            
            var ordered = list.OrderBy(u => u.unit_number).ToList();
            _logger.LogDebug("Retrieved {Count} units for series {SeriesId}", ordered.Count, seriesId);
            return ordered;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list units for series: {SeriesId}", seriesId);
            throw;
        }
    }

    /// <summary>
    /// Retrieves a unit by its ID.
    /// </summary>
    /// <param name="id">Unit ID (URN format).</param>
    /// <returns>Unit if found, null otherwise.</returns>
    public Unit? GetUnit(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            _logger.LogWarning("GetUnit called with null or empty ID");
            return null;
        }
        
        _logger.LogDebug("Retrieving unit: {UnitId}", id);
        
        try
        {
            using var conn = _dataSource.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT data FROM units WHERE id = $1";
            cmd.Parameters.AddWithValue(id);
            using var reader = cmd.ExecuteReader();
            
            if (reader.Read())
            {
                var unit = FromJson<Unit>(reader.GetString(0));
                _logger.LogDebug("Unit found: {UnitId}", id);
                return unit;
            }
            
            _logger.LogDebug("Unit not found: {UnitId}", id);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve unit: {UnitId}", id);
            throw;
        }
    }
    
    #endregion
    
    #region Page Operations

    /// <summary>
    /// Adds a page to a unit.
    /// </summary>
    /// <param name="unitId">Unit ID to add page to.</param>
    /// <param name="page">Page to add.</param>
    /// <remarks>
    /// Pages are stored as a JSON array per unit. This operation reads existing pages,
    /// appends the new page, and saves back to the database.
    /// </remarks>
    /// <summary>
    /// Adds a page to a unit.
    /// </summary>
    /// <param name="unitId">Unit ID to add page to.</param>
    /// <param name="page">Page to add.</param>
    /// <remarks>
    /// Pages are stored as a JSON array per unit. This operation reads existing pages,
    /// appends the new page, and saves back to the database.
    /// </remarks>
    public void AddPage(string unitId, Page page)
    {
        if (string.IsNullOrWhiteSpace(unitId))
        {
            _logger.LogWarning("AddPage called with null or empty unit ID");
            return;
        }
        
        if (page == null)
        {
            _logger.LogWarning("AddPage called with null page");
            return;
        }
        
        _logger.LogDebug("Adding page {PageNumber} to unit {UnitId}", page.page_number, unitId);
        
        try
        {
            var pages = GetPages(unitId).ToList();
            pages.Add(page);
            
            using var conn = _dataSource.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM pages WHERE unit_id = $1; INSERT INTO pages (unit_id, data) VALUES ($1, $2::jsonb);";
            cmd.Parameters.AddWithValue(unitId);
            cmd.Parameters.AddWithValue(ToJson(pages));
            cmd.ExecuteNonQuery();
            
            _logger.LogInformation("Page {PageNumber} added to unit {UnitId}", page.page_number, unitId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add page to unit: {UnitId}", unitId);
            throw;
        }
    }

    /// <summary>
    /// Retrieves all pages for a unit.
    /// </summary>
    /// <param name="unitId">Unit ID (URN format).</param>
    /// <returns>Collection of pages, or empty if none exist.</returns>
    public IEnumerable<Page> GetPages(string unitId)
    {
        if (string.IsNullOrWhiteSpace(unitId))
        {
            _logger.LogWarning("GetPages called with null or empty unit ID");
            return Enumerable.Empty<Page>();
        }
        
        _logger.LogDebug("Retrieving pages for unit: {UnitId}", unitId);
        
        try
        {
            using var conn = _dataSource.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT data FROM pages WHERE unit_id = $1";
            cmd.Parameters.AddWithValue(unitId);
            using var reader = cmd.ExecuteReader();
            
            if (reader.Read())
            {
                var pages = FromJson<List<Page>>(reader.GetString(0)) ?? new List<Page>();
                _logger.LogDebug("Retrieved {Count} pages for unit {UnitId}", pages.Count, unitId);
                return pages;
            }
            
            _logger.LogDebug("No pages found for unit {UnitId}", unitId);
            return Enumerable.Empty<Page>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve pages for unit: {UnitId}", unitId);
            throw;
        }
    }
    
    #endregion
    
    #region Progress Operations

    /// <summary>
    /// Updates or creates reading progress for a user on a series.
    /// </summary>
    /// <param name="userId">User ID (URN format).</param>
    /// <param name="progress">Reading progress data.</param>
    public void UpdateProgress(string userId, ReadingProgress progress)
    {
        if (string.IsNullOrWhiteSpace(userId) || progress == null)
        {
            _logger.LogWarning("UpdateProgress called with invalid parameters");
            return;
        }
        
        _logger.LogDebug("Updating progress for user {UserId} on series {SeriesUrn}", userId, progress.series_urn);
        
        try
        {
            using var conn = _dataSource.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO progress (user_id, series_urn, data, updated_at) 
                VALUES ($1, $2, $3::jsonb, $4) 
                ON CONFLICT (user_id, series_urn) 
                DO UPDATE SET data = $3::jsonb, updated_at = $4";
            cmd.Parameters.AddWithValue(userId);
            cmd.Parameters.AddWithValue(progress.series_urn);
            cmd.Parameters.AddWithValue(ToJson(progress));
            cmd.Parameters.AddWithValue(progress.updated_at);
            cmd.ExecuteNonQuery();
            
            _logger.LogInformation("Progress updated for user {UserId} on series {SeriesUrn}", userId, progress.series_urn);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update progress for user {UserId}", userId);
            throw;
        }
    }

    /// <summary>
    /// Retrieves reading progress for a user on a specific series.
    /// </summary>
    /// <param name="userId">User ID (URN format).</param>
    /// <param name="seriesUrn">Series URN.</param>
    /// <returns>Reading progress if exists, null otherwise.</returns>
    public ReadingProgress? GetProgress(string userId, string seriesUrn)
    {
        _logger.LogDebug("Getting progress for user {UserId} on series {SeriesUrn}", userId, seriesUrn);
        
        try
        {
            using var conn = _dataSource.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT data FROM progress WHERE user_id = $1 AND series_urn = $2";
            cmd.Parameters.AddWithValue(userId);
            cmd.Parameters.AddWithValue(seriesUrn);
            using var reader = cmd.ExecuteReader();
            
            if (reader.Read())
            {
                return FromJson<ReadingProgress>(reader.GetString(0));
            }
            
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get progress for user {UserId}", userId);
            throw;
        }
    }

    /// <summary>
    /// Retrieves all series in a user's library (all series with progress).
    /// </summary>
    /// <param name="userId">User ID (URN format).</param>
    /// <returns>Collection of reading progress entries.</returns>
    public IEnumerable<ReadingProgress> GetLibrary(string userId)
    {
        _logger.LogDebug("Getting library for user {UserId}", userId);
        
        try
        {
            var list = new List<ReadingProgress>();
            using var conn = _dataSource.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT data FROM progress WHERE user_id = $1";
            cmd.Parameters.AddWithValue(userId);
            using var reader = cmd.ExecuteReader();
            
            while (reader.Read())
            {
                var p = FromJson<ReadingProgress>(reader.GetString(0));
                if (p != null) list.Add(p);
            }
            
            _logger.LogDebug("Retrieved {Count} library entries for user {UserId}", list.Count, userId);
            return list;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get library for user {UserId}", userId);
            throw;
        }
    }

    /// <summary>
    /// Retrieves reading history for a user, ordered by most recently updated.
    /// </summary>
    /// <param name="userId">User ID (URN format).</param>
    /// <returns>Collection of reading progress entries ordered by update time (newest first).</returns>
    public IEnumerable<ReadingProgress> GetHistory(string userId)
    {
        _logger.LogDebug("Getting history for user {UserId}", userId);
        
        try
        {
            var list = new List<ReadingProgress>();
            using var conn = _dataSource.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT data FROM progress WHERE user_id = $1 ORDER BY updated_at DESC";
            cmd.Parameters.AddWithValue(userId);
            using var reader = cmd.ExecuteReader();
            
            while (reader.Read())
            {
                var p = FromJson<ReadingProgress>(reader.GetString(0));
                if (p != null) list.Add(p);
            }
            
            _logger.LogDebug("Retrieved {Count} history entries for user {UserId}", list.Count, userId);
            return list;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get history for user {UserId}", userId);
            throw;
        }
    }
    
    #endregion
    
    #region Comment Operations

    /// <summary>
    /// Adds or updates a comment.
    /// </summary>
    /// <param name="comment">Comment to add or update.</param>
    public void AddComment(Comment comment)
    {
        using var conn = _dataSource.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO comments (id, target_urn, data) VALUES ($1, $2, $3::jsonb) ON CONFLICT (id) DO UPDATE SET data = $3::jsonb";
        cmd.Parameters.AddWithValue(comment.id);
        cmd.Parameters.AddWithValue(comment.id); // We need to store target_urn - use author.uid as placeholder
        cmd.Parameters.AddWithValue(ToJson(comment));
        cmd.ExecuteNonQuery();
    }

    public IEnumerable<Comment> GetComments(string targetUrn)
    {
        var list = new List<Comment>();
        using var conn = _dataSource.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT data FROM comments WHERE target_urn = $1 ORDER BY (data->>'created_at')::timestamp DESC";
        cmd.Parameters.AddWithValue(targetUrn);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var c = FromJson<Comment>(reader.GetString(0));
            if (c != null) list.Add(c);
        }
        return list;
    }

    // Votes
    public void AddVote(string userId, Vote vote)
    {
        using var conn = _dataSource.OpenConnection();
        using var cmd = conn.CreateCommand();
        var key = $"{userId}|{vote.target_id}";
        
        if (vote.value == 0)
        {
            // Remove vote
            cmd.CommandText = "DELETE FROM votes WHERE id = $1";
            cmd.Parameters.AddWithValue(key);
        }
        else
        {
            cmd.CommandText = "INSERT INTO votes (id, user_id, data) VALUES ($1, $2, $3::jsonb) ON CONFLICT (id) DO UPDATE SET data = $3::jsonb";
            cmd.Parameters.AddWithValue(key);
            cmd.Parameters.AddWithValue(userId);
            cmd.Parameters.AddWithValue(ToJson(vote));
        }
        cmd.ExecuteNonQuery();
    }

    // Collections
    public void AddCollection(string userId, Collection collection)
    {
        using var conn = _dataSource.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO collections (id, user_id, data) VALUES ($1, $2, $3::jsonb) ON CONFLICT (id) DO NOTHING";
        cmd.Parameters.AddWithValue(collection.id);
        cmd.Parameters.AddWithValue(userId);
        cmd.Parameters.AddWithValue(ToJson(collection));
        cmd.ExecuteNonQuery();
    }

    public IEnumerable<Collection> ListCollections(string userId)
    {
        var list = new List<Collection>();
        using var conn = _dataSource.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT data FROM collections WHERE user_id = $1";
        cmd.Parameters.AddWithValue(userId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var c = FromJson<Collection>(reader.GetString(0));
            if (c != null) list.Add(c);
        }
        return list;
    }

    public Collection? GetCollection(string id)
    {
        using var conn = _dataSource.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT data FROM collections WHERE id = $1";
        cmd.Parameters.AddWithValue(id);
        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            return FromJson<Collection>(reader.GetString(0));
        }
        return null;
    }

    public void UpdateCollection(Collection collection)
    {
        using var conn = _dataSource.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE collections SET data = $2::jsonb WHERE id = $1";
        cmd.Parameters.AddWithValue(collection.id);
        cmd.Parameters.AddWithValue(ToJson(collection));
        cmd.ExecuteNonQuery();
    }

    public void DeleteCollection(string id)
    {
        using var conn = _dataSource.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM collections WHERE id = $1";
        cmd.Parameters.AddWithValue(id);
        cmd.ExecuteNonQuery();
    }

    // Reports
    public void AddReport(Report report)
    {
        using var conn = _dataSource.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO reports (id, data) VALUES ($1, $2::jsonb)";
        cmd.Parameters.AddWithValue(Guid.NewGuid().ToString());
        cmd.Parameters.AddWithValue(ToJson(report));
        cmd.ExecuteNonQuery();
    }

    // System Config
    public SystemConfig GetSystemConfig()
    {
        using var conn = _dataSource.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT data FROM system_config WHERE key = 'default'";
        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            return FromJson<SystemConfig>(reader.GetString(0))!;
        }
        // Return default with all fields
        return new SystemConfig(
            is_setup_complete: false, 
            registration_open: true, 
            maintenance_mode: false, 
            motd_message: "Welcome to MehguViewer Core", 
            default_language_filter: new[] { "en" },
            max_login_attempts: 5,
            lockout_duration_minutes: 15,
            token_expiry_hours: 24,
            cloudflare_enabled: false,
            cloudflare_site_key: "",
            cloudflare_secret_key: "",
            require_2fa_passkey: false,
            require_password_for_danger_zone: true
        );
    }

    public void UpdateSystemConfig(SystemConfig config)
    {
        using var conn = _dataSource.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO system_config (key, data) VALUES ('default', $1::jsonb) ON CONFLICT (key) DO UPDATE SET data = $1::jsonb";
        cmd.Parameters.AddWithValue(ToJson(config));
        cmd.ExecuteNonQuery();
    }

    // System Stats
    public SystemStats GetSystemStats()
    {
        // Count users
        long userCount = 0;
        using (var conn = _dataSource.OpenConnection())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM users";
            userCount = (long)cmd.ExecuteScalar()!;
        }

        return new SystemStats(
            (int)userCount,
            0,
            (int)(DateTime.UtcNow - System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime()).TotalSeconds
        );
    }

    // User Management
    public void AddUser(User user)
    {
        using var conn = _dataSource.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO users (id, username, data) VALUES ($1, $2, $3::jsonb) ON CONFLICT (id) DO UPDATE SET data = $3::jsonb";
        cmd.Parameters.AddWithValue(user.id);
        cmd.Parameters.AddWithValue(user.username);
        cmd.Parameters.AddWithValue(ToJson(user));
        cmd.ExecuteNonQuery();
    }

    public void UpdateUser(User user)
    {
        using var conn = _dataSource.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE users SET data = $2::jsonb WHERE id = $1";
        cmd.Parameters.AddWithValue(user.id);
        cmd.Parameters.AddWithValue(ToJson(user));
        cmd.ExecuteNonQuery();
    }

    public User? GetUser(string id)
    {
        using var conn = _dataSource.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT data FROM users WHERE id = $1";
        cmd.Parameters.AddWithValue(id);
        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            return FromJson<User>(reader.GetString(0));
        }
        return null;
    }

    public User? GetUserByUsername(string username)
    {
        using var conn = _dataSource.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT data FROM users WHERE username = $1";
        cmd.Parameters.AddWithValue(username);
        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            return FromJson<User>(reader.GetString(0));
        }
        return null;
    }

    public IEnumerable<User> ListUsers()
    {
        var list = new List<User>();
        using var conn = _dataSource.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT data FROM users";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var u = FromJson<User>(reader.GetString(0));
            if (u != null) list.Add(u);
        }
        return list;
    }

    public void DeleteUser(string id)
    {
        using var conn = _dataSource.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM users WHERE id = $1";
        cmd.Parameters.AddWithValue(id);
        cmd.ExecuteNonQuery();
    }

    public void DeleteUserHistory(string userId)
    {
        using var conn = _dataSource.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM progress WHERE user_id = $1";
        cmd.Parameters.AddWithValue(userId);
        cmd.ExecuteNonQuery();
    }

    public void AnonymizeUserContent(string userId)
    {
        // Anonymize all comments by this user
        using var conn = _dataSource.OpenConnection();
        using var cmd = conn.CreateCommand();
        
        // Update all comments from this user to have anonymized author
        cmd.CommandText = @"
            UPDATE comments 
            SET data = jsonb_set(
                jsonb_set(
                    jsonb_set(
                        jsonb_set(data, '{author,uid}', '""urn:mvn:user:deleted""'),
                        '{author,display_name}', '""Deleted User""'
                    ),
                    '{author,avatar_url}', '""""'
                ),
                '{author,role_badge}', '""Ghost""'
            )
            WHERE data->'author'->>'uid' = $1
        ";
        cmd.Parameters.AddWithValue(userId);
        cmd.ExecuteNonQuery();
    }

    public bool IsAdminSet()
    {
        // Check if any user has role Admin in JSON
        // This is inefficient with JSONB without index, but fine for small scale
        var users = ListUsers();
        return users.Any(u => u.role == "Admin");
    }

    // Passkey / WebAuthn
    public void AddPasskey(Passkey passkey)
    {
        using var conn = _dataSource.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO passkeys (id, user_id, credential_id, data) VALUES ($1, $2, $3, $4::jsonb)";
        cmd.Parameters.AddWithValue(passkey.id);
        cmd.Parameters.AddWithValue(passkey.user_id);
        cmd.Parameters.AddWithValue(passkey.credential_id);
        cmd.Parameters.AddWithValue(ToJson(passkey));
        cmd.ExecuteNonQuery();
    }

    public void UpdatePasskey(Passkey passkey)
    {
        using var conn = _dataSource.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE passkeys SET data = $1::jsonb WHERE id = $2";
        cmd.Parameters.AddWithValue(ToJson(passkey));
        cmd.Parameters.AddWithValue(passkey.id);
        cmd.ExecuteNonQuery();
    }

    public IEnumerable<Passkey> GetPasskeysByUser(string userId)
    {
        using var conn = _dataSource.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT data FROM passkeys WHERE user_id = $1";
        cmd.Parameters.AddWithValue(userId);
        using var reader = cmd.ExecuteReader();
        var results = new List<Passkey>();
        while (reader.Read())
        {
            results.Add(FromJson<Passkey>(reader.GetString(0))!);
        }
        return results;
    }

    public Passkey? GetPasskeyByCredentialId(string credentialId)
    {
        using var conn = _dataSource.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT data FROM passkeys WHERE credential_id = $1";
        cmd.Parameters.AddWithValue(credentialId);
        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            return FromJson<Passkey>(reader.GetString(0));
        }
        return null;
    }

    public Passkey? GetPasskey(string id)
    {
        using var conn = _dataSource.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT data FROM passkeys WHERE id = $1";
        cmd.Parameters.AddWithValue(id);
        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            return FromJson<Passkey>(reader.GetString(0));
        }
        return null;
    }

    public void DeletePasskey(string id)
    {
        using var conn = _dataSource.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM passkeys WHERE id = $1";
        cmd.Parameters.AddWithValue(id);
        cmd.ExecuteNonQuery();
    }

    // Node Metadata
    public NodeMetadata GetNodeMetadata()
    {
        using var conn = _dataSource.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT data FROM node_metadata WHERE key = 'default'";
        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            return FromJson<NodeMetadata>(reader.GetString(0))!;
        }
        return new NodeMetadata(
            "1.0.0",
            "MehguViewer Core",
            "A MehguViewer Core Node",
            "https://auth.mehgu.example.com",
            new NodeCapabilities(true, true, true),
            new NodeMaintainer("Admin", "admin@example.com")
        );
    }

    public void UpdateNodeMetadata(NodeMetadata metadata)
    {
        using var conn = _dataSource.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO node_metadata (key, data) VALUES ('default', $1::jsonb) ON CONFLICT (key) DO UPDATE SET data = $1::jsonb";
        cmd.Parameters.AddWithValue(ToJson(metadata));
        cmd.ExecuteNonQuery();
    }

    // Taxonomy Configuration
    public TaxonomyConfig GetTaxonomyConfig()
    {
        using var conn = _dataSource.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT data FROM taxonomy_config WHERE key = 'default'";
        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            return FromJson<TaxonomyConfig>(reader.GetString(0))!;
        }
        // Return default taxonomy config
        // Note: types are fixed (Photo, Text, Video) - not customizable
        return new TaxonomyConfig(
            tags: new[] { "Action", "Adventure", "Comedy", "Drama", "Fantasy", "Romance", "Slice of Life" },
            content_warnings: ContentWarnings.All,
            types: MediaTypes.All,
            authors: [],
            scanlators: [new Scanlator("official", "Official", ScanlatorRole.Both)],
            groups: []
        );
    }

    public void UpdateTaxonomyConfig(TaxonomyConfig config)
    {
        using var conn = _dataSource.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO taxonomy_config (key, data) VALUES ('default', $1::jsonb) ON CONFLICT (key) DO UPDATE SET data = $1::jsonb";
        cmd.Parameters.AddWithValue(ToJson(config));
        cmd.ExecuteNonQuery();
    }

    public void ResetAllData()
    {
        using var conn = _dataSource.OpenConnection();
        using var cmd = conn.CreateCommand();
        // Truncate all data tables but preserve schema
        cmd.CommandText = @"
            TRUNCATE TABLE series CASCADE;
            TRUNCATE TABLE units CASCADE;
            TRUNCATE TABLE pages CASCADE;
            TRUNCATE TABLE progress CASCADE;
            TRUNCATE TABLE comments CASCADE;
            TRUNCATE TABLE votes CASCADE;
            TRUNCATE TABLE collections CASCADE;
            TRUNCATE TABLE reports CASCADE;
            TRUNCATE TABLE users CASCADE;
            TRUNCATE TABLE passkeys CASCADE;
            TRUNCATE TABLE edit_permissions CASCADE;
            UPDATE system_config SET data = '{""is_setup_complete"": false, ""registration_open"": false, ""maintenance_mode"": false, ""motd_message"": """", ""default_language_filter"": []}'::jsonb WHERE key = 'default';
        ";
        try
        {
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Some tables may not exist during reset, continuing...");
            // Tables might not exist, try individual truncates
        }
    }
    
    // Metadata Aggregation
    public void AggregateSeriesMetadataFromUnits(string seriesId)
    {
        var series = GetSeries(seriesId);
        if (series == null) return;
        
        var units = ListUnits(seriesId);
        var aggregated = _metadataService.AggregateMetadata(series, units);
        UpdateSeries(aggregated);
    }
    
    // Edit Permissions
    public void GrantEditPermission(string targetUrn, string userUrn, string grantedBy)
    {
        using var conn = _dataSource.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO edit_permissions (target_urn, user_urn, granted_by, granted_at)
            VALUES ($1, $2, $3, NOW())
            ON CONFLICT (target_urn, user_urn) DO UPDATE SET granted_by = $3, granted_at = NOW()";
        cmd.Parameters.AddWithValue(targetUrn);
        cmd.Parameters.AddWithValue(userUrn);
        cmd.Parameters.AddWithValue(grantedBy);
        cmd.ExecuteNonQuery();
    }
    
    public void RevokeEditPermission(string targetUrn, string userUrn)
    {
        using var conn = _dataSource.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM edit_permissions WHERE target_urn = $1 AND user_urn = $2";
        cmd.Parameters.AddWithValue(targetUrn);
        cmd.Parameters.AddWithValue(userUrn);
        cmd.ExecuteNonQuery();
    }
    
    public bool HasEditPermission(string targetUrn, string userUrn)
    {
        _logger.LogDebug("HasEditPermission check: targetUrn={TargetUrn}, userUrn={UserUrn}", targetUrn, userUrn);
        
        // Check if user is the owner
        if (targetUrn.StartsWith("urn:mvn:series:"))
        {
            var series = GetSeries(targetUrn);
            if (series?.created_by == userUrn) 
            {
                _logger.LogDebug("User is series owner");
                return true;
            }
        }
        else if (targetUrn.StartsWith("urn:mvn:unit:"))
        {
            var unit = GetUnit(targetUrn);
            if (unit?.created_by == userUrn)
            {
                _logger.LogDebug("User is unit owner");
                return true;
            }
            
            // Also check parent series ownership
            if (unit != null)
            {
                var series = GetSeries(unit.series_id);
                if (series?.created_by == userUrn)
                {
                    _logger.LogDebug("User is parent series owner");
                    return true;
                }
            }
        }
        
        // Check explicit permission
        using var conn = _dataSource.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM edit_permissions WHERE target_urn = $1 AND user_urn = $2 LIMIT 1";
        cmd.Parameters.AddWithValue(targetUrn);
        cmd.Parameters.AddWithValue(userUrn);
        using var reader = cmd.ExecuteReader();
        var hasPermission = reader.Read();
        _logger.LogDebug("Database permission check result: {HasPermission}", hasPermission);
        return hasPermission;
    }
    
    public string[] GetEditPermissions(string targetUrn)
    {
        var permissions = new List<string>();
        using var conn = _dataSource.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT user_urn FROM edit_permissions WHERE target_urn = $1";
        cmd.Parameters.AddWithValue(targetUrn);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            permissions.Add(reader.GetString(0));
        }
        return permissions.ToArray();
    }

    public EditPermission[] GetEditPermissionRecords(string targetUrn)
    {
        var permissions = new List<EditPermission>();
        using var conn = _dataSource.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT target_urn, user_urn, granted_at, granted_by FROM edit_permissions WHERE target_urn = $1 ORDER BY granted_at DESC";
        cmd.Parameters.AddWithValue(targetUrn);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            permissions.Add(new EditPermission(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetDateTime(2),
                reader.GetString(3)
            ));
        }
        return permissions.ToArray();
    }

    public void SyncEditPermissions()
    {
        using var conn = _dataSource.OpenConnection();
        
        // Get all permission target URNs
        using var selectCmd = conn.CreateCommand();
        selectCmd.CommandText = "SELECT DISTINCT target_urn FROM edit_permissions";
        var targetUrns = new List<string>();
        using (var reader = selectCmd.ExecuteReader())
        {
            while (reader.Read())
            {
                targetUrns.Add(reader.GetString(0));
            }
        }
        
        // Check each target URN and delete if series/unit doesn't exist
        var orphanedCount = 0;
        foreach (var targetUrn in targetUrns)
        {
            bool exists = false;
            
            if (targetUrn.StartsWith("urn:mvn:series:"))
            {
                var series = GetSeries(targetUrn);
                exists = series != null;
            }
            else if (targetUrn.StartsWith("urn:mvn:unit:"))
            {
                var unit = GetUnit(targetUrn);
                exists = unit != null;
            }
            
            if (!exists)
            {
                using var deleteCmd = conn.CreateCommand();
                deleteCmd.CommandText = "DELETE FROM edit_permissions WHERE target_urn = $1";
                deleteCmd.Parameters.AddWithValue(targetUrn);
                var deleted = deleteCmd.ExecuteNonQuery();
                orphanedCount += deleted;
                _logger.LogInformation("Cleaned up {Count} orphaned permissions for deleted target: {Target}", deleted, targetUrn);
            }
        }
        
        if (orphanedCount > 0)
        {
            _logger.LogInformation("Permission sync complete: removed {Count} orphaned permission records", orphanedCount);
        }
        else
        {
            _logger.LogDebug("Permission sync complete: no orphaned permissions found");
        }
    }
    
    #endregion
}
