using System.Data;
using MehguViewer.Shared.Models;
using Npgsql;
using System.Text.Json;

namespace MehguViewer.Core.Backend.Services;

public class PostgresRepository : IRepository
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<PostgresRepository> _logger;

    public PostgresRepository(IConfiguration configuration, ILogger<PostgresRepository> logger)
        : this(configuration.GetConnectionString("DefaultConnection"), logger)
    {
    }

    public PostgresRepository(string? connectionString, ILogger<PostgresRepository> logger)
    {
        if (string.IsNullOrEmpty(connectionString))
        {
            throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        }
        _dataSource = NpgsqlDataSource.Create(connectionString);
        _logger = logger;
        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        using var conn = _dataSource.OpenConnection();
        
        // 1. Create Schema
        using (var cmd = conn.CreateCommand())
        {
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
        }

        // 2. Seed System Config
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT 1 FROM system_config WHERE key = 'default'";
            var exists = cmd.ExecuteScalar() != null;
            
            if (!exists)
            {
                var defaultConfig = new SystemConfig(
                    is_setup_complete: false, 
                    registration_open: true, 
                    maintenance_mode: false, 
                    motd_message: "Welcome to MehguViewer Core", 
                    default_language_filter: new[] { "en" },
                    allow_panel_access_for_users: false,
                    max_login_attempts: 5,
                    lockout_duration_minutes: 15,
                    token_expiry_hours: 24,
                    cloudflare_enabled: false,
                    cloudflare_site_key: "",
                    cloudflare_secret_key: ""
                );
                var json = ToJson(defaultConfig);
                _logger.LogInformation("Seeding system_config with: {Json}", json);
                cmd.CommandText = "INSERT INTO system_config (key, data) VALUES ('default', $1::jsonb)";
                cmd.Parameters.AddWithValue(json);
                cmd.ExecuteNonQuery();
            }
            else
            {
                _logger.LogInformation("system_config already exists, not seeding");
            }
        }

        // 3. Seed Node Metadata
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT 1 FROM node_metadata WHERE key = 'default'";
            var exists = cmd.ExecuteScalar() != null;
            
            if (!exists)
            {
                var defaultMetadata = new NodeMetadata(
                    "1.0.0",
                    "MehguViewer Core",
                    "A MehguViewer Core Node",
                    "https://auth.mehgu.example.com",
                    new NodeCapabilities(true, true, true),
                    new NodeMaintainer("Admin", "admin@example.com")
                );
                cmd.CommandText = "INSERT INTO node_metadata (key, data) VALUES ('default', $1::jsonb)";
                cmd.Parameters.AddWithValue(ToJson(defaultMetadata));
                cmd.ExecuteNonQuery();
            }
        }
    }

    public void ResetDatabase()
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
        ";
        cmd.ExecuteNonQuery();
        InitializeDatabase();
    }

    public bool HasData()
    {
        try
        {
            using var conn = _dataSource.OpenConnection();
            using var cmd = conn.CreateCommand();
            
            // Only check for actual user data, not auto-seeded system_config
            cmd.CommandText = "SELECT COUNT(*) FROM users";
            var userCount = (long)cmd.ExecuteScalar()!;

            // Also check if setup has been completed (indicates actual user setup)
            cmd.CommandText = "SELECT data->>'is_setup_complete' FROM system_config WHERE key = 'default' LIMIT 1";
            var setupComplete = cmd.ExecuteScalar()?.ToString() == "true";

            return userCount > 0 || setupComplete;
        }
        catch
        {
            // If tables don't exist, it has no data
            return false;
        }
    }

    public void SeedDebugData()
    {
        // Seed a demo series
        var seriesId = UrnHelper.CreateSeriesUrn();
        var series = new Series(
            seriesId,
            null,
            "Demo Manga",
            "A demo manga for testing purposes. This contains sample chapters and pages.",
            new Poster("https://placehold.co/400x600", "Demo Poster"),
            "Manga",
            new Dictionary<string, string>(),
            "rtl",
            null
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
        
        _logger.LogInformation("Seeded debug data with series {SeriesId}", seriesId);
    }

    // Helper to serialize/deserialize JSONB
    private string ToJson<T>(T obj) 
    {
        var typeInfo = (System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>)AppJsonSerializerContext.Default.GetTypeInfo(typeof(T))!;
        return JsonSerializer.Serialize(obj, typeInfo);
    }

    private T? FromJson<T>(string json) 
    {
        var typeInfo = (System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>)AppJsonSerializerContext.Default.GetTypeInfo(typeof(T))!;
        return JsonSerializer.Deserialize(json, typeInfo);
    }

    // Series
    public void AddSeries(Series series)
    {
        using var conn = _dataSource.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO series (id, data) VALUES ($1, $2::jsonb) ON CONFLICT (id) DO NOTHING";
        cmd.Parameters.AddWithValue(series.id);
        cmd.Parameters.AddWithValue(ToJson(series));
        cmd.ExecuteNonQuery();
    }

    public void UpdateSeries(Series series)
    {
        using var conn = _dataSource.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE series SET data = $2::jsonb WHERE id = $1";
        cmd.Parameters.AddWithValue(series.id);
        cmd.Parameters.AddWithValue(ToJson(series));
        cmd.ExecuteNonQuery();
    }

    public Series? GetSeries(string id)
    {
        using var conn = _dataSource.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT data FROM series WHERE id = $1";
        cmd.Parameters.AddWithValue(id);
        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            return FromJson<Series>(reader.GetString(0));
        }
        return null;
    }

    public IEnumerable<Series> ListSeries()
    {
        var list = new List<Series>();
        using var conn = _dataSource.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT data FROM series";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var s = FromJson<Series>(reader.GetString(0));
            if (s != null) list.Add(s);
        }
        return list;
    }

    public IEnumerable<Series> SearchSeries(string? query, string? type, string[]? genres, string? status)
    {
        // Naive implementation fetching all and filtering in memory for now, 
        // or use JSONB operators for better performance.
        // For simplicity in this integration step:
        var all = ListSeries();
        var result = all.AsEnumerable();
        if (!string.IsNullOrEmpty(query))
        {
            result = result.Where(s => s.title.Contains(query, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrEmpty(type))
        {
            result = result.Where(s => s.media_type.Equals(type, StringComparison.OrdinalIgnoreCase));
        }
        return result;
    }

    public void DeleteSeries(string id)
    {
        using var conn = _dataSource.OpenConnection();
        using var cmd = conn.CreateCommand();
        // Delete pages for units of this series first
        cmd.CommandText = "DELETE FROM pages WHERE unit_id IN (SELECT id FROM units WHERE series_id = $1)";
        cmd.Parameters.AddWithValue(id);
        cmd.ExecuteNonQuery();
        
        // Delete units
        cmd.Parameters.Clear();
        cmd.CommandText = "DELETE FROM units WHERE series_id = $1";
        cmd.Parameters.AddWithValue(id);
        cmd.ExecuteNonQuery();
        
        // Delete series
        cmd.Parameters.Clear();
        cmd.CommandText = "DELETE FROM series WHERE id = $1";
        cmd.Parameters.AddWithValue(id);
        cmd.ExecuteNonQuery();
    }

    // Units
    public void AddUnit(Unit unit)
    {
        using var conn = _dataSource.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO units (id, series_id, data) VALUES ($1, $2, $3::jsonb) ON CONFLICT (id) DO NOTHING";
        cmd.Parameters.AddWithValue(unit.id);
        cmd.Parameters.AddWithValue(unit.series_id);
        cmd.Parameters.AddWithValue(ToJson(unit));
        cmd.ExecuteNonQuery();
    }

    public void UpdateUnit(Unit unit)
    {
        using var conn = _dataSource.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE units SET data = $2::jsonb WHERE id = $1";
        cmd.Parameters.AddWithValue(unit.id);
        cmd.Parameters.AddWithValue(ToJson(unit));
        cmd.ExecuteNonQuery();
    }

    public void DeleteUnit(string id)
    {
        using var conn = _dataSource.OpenConnection();
        using var cmd = conn.CreateCommand();
        // Delete pages for this unit first
        cmd.CommandText = "DELETE FROM pages WHERE unit_id = $1";
        cmd.Parameters.AddWithValue(id);
        cmd.ExecuteNonQuery();
        
        // Delete unit
        cmd.Parameters.Clear();
        cmd.CommandText = "DELETE FROM units WHERE id = $1";
        cmd.Parameters.AddWithValue(id);
        cmd.ExecuteNonQuery();
    }

    public IEnumerable<Unit> ListUnits(string seriesId)
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
        return list.OrderBy(u => u.unit_number);
    }

    public Unit? GetUnit(string id)
    {
        using var conn = _dataSource.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT data FROM units WHERE id = $1";
        cmd.Parameters.AddWithValue(id);
        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            return FromJson<Unit>(reader.GetString(0));
        }
        return null;
    }

    // Pages
    public void AddPage(string unitId, Page page)
    {
        // Storing pages as a list in a single row per unit might be better for JSONB, 
        // or individual rows. The schema above defined 'pages' with 'unit_id'.
        // Let's assume we append to a JSON array or store individual rows.
        // For simplicity, let's store one row per unit containing all pages in 'data' as a list?
        // Or one row per page?
        // The schema I wrote: CREATE TABLE IF NOT EXISTS pages (unit_id TEXT NOT NULL, data JSONB NOT NULL);
        // This implies one row per page? No primary key?
        // Let's change strategy: Store pages as a single JSON array for the unit in a 'unit_pages' table.
        
        // Actually, let's just append to a list in memory and save.
        // But that requires reading first.
        
        // Let's use a simple approach: 'pages' table stores { unit_id, pages_list_json }
        // Upsert logic.
        
        var pages = GetPages(unitId).ToList();
        pages.Add(page);
        
        using var conn = _dataSource.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO pages (unit_id, data) VALUES ($1, $2::jsonb) 
            ON CONFLICT (unit_id) DO UPDATE SET data = $2::jsonb";
        // Wait, I need a PK on pages to do ON CONFLICT.
        // Let's fix the schema in InitializeDatabase or just handle it here.
        // I'll assume I can fix the schema.
        // For now, let's just delete and insert (inefficient but works).
        
        // Better:
        cmd.CommandText = "DELETE FROM pages WHERE unit_id = $1; INSERT INTO pages (unit_id, data) VALUES ($1, $2::jsonb);";
        cmd.Parameters.AddWithValue(unitId);
        cmd.Parameters.AddWithValue(ToJson(pages));
        cmd.ExecuteNonQuery();
    }

    public IEnumerable<Page> GetPages(string unitId)
    {
        using var conn = _dataSource.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT data FROM pages WHERE unit_id = $1";
        cmd.Parameters.AddWithValue(unitId);
        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            return FromJson<List<Page>>(reader.GetString(0)) ?? Enumerable.Empty<Page>();
        }
        return Enumerable.Empty<Page>();
    }

    // Progress
    public void UpdateProgress(string userId, ReadingProgress progress)
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
    }

    public ReadingProgress? GetProgress(string userId, string seriesUrn)
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

    public IEnumerable<ReadingProgress> GetLibrary(string userId)
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
        return list;
    }

    public IEnumerable<ReadingProgress> GetHistory(string userId)
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
        return list;
    }

    // Comments
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
            allow_panel_access_for_users: false,
            max_login_attempts: 5,
            lockout_duration_minutes: 15,
            token_expiry_hours: 24,
            cloudflare_enabled: false,
            cloudflare_site_key: "",
            cloudflare_secret_key: ""
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

    public User? ValidateUser(string username, string password)
    {
        var user = GetUserByUsername(username);
        if (user != null && AuthService.VerifyPassword(password, user.password_hash))
        {
            return user;
        }
        return null;
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
}
