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
                    unit_id TEXT NOT NULL,
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
                CREATE TABLE IF NOT EXISTS system_config (
                    key TEXT PRIMARY KEY,
                    data JSONB NOT NULL
                );
                CREATE TABLE IF NOT EXISTS node_metadata (
                    key TEXT PRIMARY KEY,
                    data JSONB NOT NULL
                );
                
                -- Indexes for performance
                CREATE INDEX IF NOT EXISTS idx_units_series_id ON units(series_id);
                CREATE INDEX IF NOT EXISTS idx_pages_unit_id ON pages(unit_id);
                CREATE INDEX IF NOT EXISTS idx_progress_user_id ON progress(user_id);
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
                var defaultConfig = new SystemConfig(false, true, false, "Welcome to MehguViewer Core", new[] { "en" });
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
            cmd.CommandText = "SELECT COUNT(*) FROM users";
            var userCount = (long)cmd.ExecuteScalar()!;
            
            cmd.CommandText = "SELECT COUNT(*) FROM system_config";
            var configCount = (long)cmd.ExecuteScalar()!;

            return userCount > 0 || configCount > 0;
        }
        catch
        {
            // If tables don't exist, it has no data
            return false;
        }
    }

    public void SeedDebugData()
    {
        // Stub
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
        cmd.CommandText = "INSERT INTO series (id, data) VALUES ($1, $2::jsonb) ON CONFLICT (id) DO UPDATE SET data = $2::jsonb";
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

    // Units
    public void AddUnit(Unit unit)
    {
        using var conn = _dataSource.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO units (id, series_id, data) VALUES ($1, $2, $3::jsonb) ON CONFLICT (id) DO UPDATE SET data = $3::jsonb";
        cmd.Parameters.AddWithValue(unit.id);
        cmd.Parameters.AddWithValue(unit.series_id);
        cmd.Parameters.AddWithValue(ToJson(unit));
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

    // Comments - Stub
    public void AddComment(Comment comment) { }
    public IEnumerable<Comment> GetComments(string targetUrn) => Enumerable.Empty<Comment>();

    // Votes - Stub
    public void AddVote(string userId, Vote vote) { }

    // Collections - Stub
    public void AddCollection(Collection collection) { }
    public IEnumerable<Collection> ListCollections(string userId) => Enumerable.Empty<Collection>();
    public Collection? GetCollection(string id) => null;
    public void UpdateCollection(Collection collection) { }
    public void DeleteCollection(string id) { }

    // Reports - Stub
    public void AddReport(Report report) { }

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
        // Return default
        return new SystemConfig(false, true, false, "Welcome to MehguViewer Core", new[] { "en" });
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
        // Stub
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
            UPDATE system_config SET data = '{""is_setup_complete"": false, ""registration_open"": false, ""maintenance_mode"": false, ""motd_message"": """", ""default_language_filter"": []}'::jsonb WHERE key = 'default';
        ";
        cmd.ExecuteNonQuery();
    }
}
