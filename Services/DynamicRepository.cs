using MehguViewer.Shared.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace MehguViewer.Core.Backend.Services;

public class DynamicRepository : IRepository
{
    private IRepository _current;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IConfiguration _configuration;
    private readonly EmbeddedPostgresService? _embeddedPostgres;

    public DynamicRepository(
        IConfiguration configuration, 
        ILoggerFactory loggerFactory,
        EmbeddedPostgresService? embeddedPostgres = null)
    {
        _configuration = configuration;
        _loggerFactory = loggerFactory;
        _embeddedPostgres = embeddedPostgres;

        // Start with MemoryRepository, will be upgraded when embedded Postgres is ready
        _current = new MemoryRepository();
    }

    /// <summary>
    /// Initialize the repository. Should be called after EmbeddedPostgresService is started.
    /// </summary>
    public async Task InitializeAsync(bool resetData = false)
    {
        var logger = _loggerFactory.CreateLogger<DynamicRepository>();

        // First, try embedded postgres if available and not failed
        if (_embeddedPostgres != null && !_embeddedPostgres.StartupFailed)
        {
            try
            {
                // Wait for embedded postgres to be ready
                await _embeddedPostgres.WaitForStartupAsync();

                if (!_embeddedPostgres.StartupFailed && !string.IsNullOrEmpty(_embeddedPostgres.ConnectionString))
                {
                    logger.LogInformation("Connecting to embedded PostgreSQL...");
                    
                    if (resetData)
                    {
                        logger.LogInformation("Resetting embedded PostgreSQL data...");
                        ResetDatabase(_embeddedPostgres.ConnectionString);
                    }
                    
                    EnsureDatabaseExists(_embeddedPostgres.ConnectionString);
                    _current = new PostgresRepository(_embeddedPostgres.ConnectionString, _loggerFactory.CreateLogger<PostgresRepository>());
                    logger.LogInformation("Successfully connected to embedded PostgreSQL");
                    return;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to initialize embedded PostgreSQL. Falling back to external or memory.");
            }
        }

        // Try external connection string
        var connectionString = _configuration.GetConnectionString("DefaultConnection");
        if (!string.IsNullOrEmpty(connectionString))
        {
            try
            {
                if (resetData)
                {
                    logger.LogInformation("Resetting PostgreSQL data...");
                    ResetDatabase(connectionString);
                }
                
                EnsureDatabaseExists(connectionString);
                _current = new PostgresRepository(connectionString, _loggerFactory.CreateLogger<PostgresRepository>());
                logger.LogInformation("Successfully connected to external PostgreSQL");
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to initialize PostgresRepository from config. Falling back to MemoryRepository.");
            }
        }

        logger.LogWarning("Using MemoryRepository - data will not persist!");
    }

    /// <summary>
    /// Reset the database by dropping all tables
    /// </summary>
    private void ResetDatabase(string connectionString)
    {
        using var conn = new Npgsql.NpgsqlConnection(connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            DROP TABLE IF EXISTS jobs CASCADE;
            DROP TABLE IF EXISTS collections CASCADE;
            DROP TABLE IF EXISTS series CASCADE;
            DROP TABLE IF EXISTS assets CASCADE;
            DROP TABLE IF EXISTS users CASCADE;
            DROP TABLE IF EXISTS system_config CASCADE;";
        cmd.ExecuteNonQuery();
    }

    public bool IsInMemory => _current is MemoryRepository;

    public bool TestConnection(string connectionString)
    {
        EnsureDatabaseExists(connectionString);
        // Try to initialize a temporary PostgresRepository
        // If it throws, the connection is invalid
        var repo = new PostgresRepository(connectionString, _loggerFactory.CreateLogger<PostgresRepository>());
        // Check if it has data
        return repo.HasData();
    }

    public void SwitchToPostgres(string connectionString, bool reset)
    {
        EnsureDatabaseExists(connectionString);
        var newRepo = new PostgresRepository(connectionString, _loggerFactory.CreateLogger<PostgresRepository>());
        
        if (reset)
        {
            newRepo.ResetDatabase();
        }
        
        _current = newRepo;
        
        // Update appsettings.json
        UpdateAppSettings(connectionString);
    }

    private void EnsureDatabaseExists(string connectionString)
    {
        try 
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            var targetDb = builder.Database;
            if (string.IsNullOrEmpty(targetDb)) return;

            // Connect to 'postgres' db to check/create
            builder.Database = "postgres";
            using var conn = new NpgsqlConnection(builder.ToString());
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT 1 FROM pg_database WHERE datname = '{targetDb}'";
            var exists = cmd.ExecuteScalar() != null;

            if (!exists)
            {
                cmd.CommandText = $"CREATE DATABASE \"{targetDb}\"";
                cmd.ExecuteNonQuery();
            }
        }
        catch (Exception ex)
        {
            var logger = _loggerFactory.CreateLogger<DynamicRepository>();
            logger.LogWarning(ex, "Failed to ensure database exists. Proceeding to connect...");
            // We don't rethrow here because maybe the user doesn't have permission to create DBs
            // but the DB might already exist or be created by another process.
            // If the DB really doesn't exist, the subsequent connection attempt will fail anyway.
        }
    }

    private void UpdateAppSettings(string connectionString)
    {
        // This is a bit hacky for a running app, but works for simple setups.
        // We'll try to update appsettings.json and appsettings.Development.json
        try
        {
            var configPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
            var json = File.ReadAllText(configPath);
            var jObject = System.Text.Json.Nodes.JsonNode.Parse(json);
            if (jObject != null)
            {
                var connStrings = jObject["ConnectionStrings"];
                if (connStrings == null)
                {
                    jObject["ConnectionStrings"] = new System.Text.Json.Nodes.JsonObject();
                    connStrings = jObject["ConnectionStrings"];
                }
                connStrings!["DefaultConnection"] = connectionString;
                
                File.WriteAllText(configPath, jObject.ToString());
            }
        }
        catch (Exception ex)
        {
            var logger = _loggerFactory.CreateLogger<DynamicRepository>();
            logger.LogError(ex, "Failed to update appsettings.json");
        }
    }

    // Delegation
    public void SeedDebugData() => _current.SeedDebugData();
    public void AddSeries(Series series) => _current.AddSeries(series);
    public Series? GetSeries(string id) => _current.GetSeries(id);
    public IEnumerable<Series> ListSeries() => _current.ListSeries();
    public IEnumerable<Series> SearchSeries(string? query, string? type, string[]? genres, string? status) => _current.SearchSeries(query, type, genres, status);
    public void DeleteSeries(string id) => _current.DeleteSeries(id);
    public void AddUnit(Unit unit) => _current.AddUnit(unit);
    public IEnumerable<Unit> ListUnits(string seriesId) => _current.ListUnits(seriesId);
    public Unit? GetUnit(string id) => _current.GetUnit(id);
    public void AddPage(string unitId, Page page) => _current.AddPage(unitId, page);
    public IEnumerable<Page> GetPages(string unitId) => _current.GetPages(unitId);
    public void UpdateProgress(string userId, ReadingProgress progress) => _current.UpdateProgress(userId, progress);
    public ReadingProgress? GetProgress(string userId, string seriesUrn) => _current.GetProgress(userId, seriesUrn);
    public IEnumerable<ReadingProgress> GetLibrary(string userId) => _current.GetLibrary(userId);
    public IEnumerable<ReadingProgress> GetHistory(string userId) => _current.GetHistory(userId);
    public void AddComment(Comment comment) => _current.AddComment(comment);
    public IEnumerable<Comment> GetComments(string targetUrn) => _current.GetComments(targetUrn);
    public void AddVote(string userId, Vote vote) => _current.AddVote(userId, vote);
    public void AddCollection(Collection collection) => _current.AddCollection(collection);
    public IEnumerable<Collection> ListCollections(string userId) => _current.ListCollections(userId);
    public Collection? GetCollection(string id) => _current.GetCollection(id);
    public void UpdateCollection(Collection collection) => _current.UpdateCollection(collection);
    public void DeleteCollection(string id) => _current.DeleteCollection(id);
    public void AddReport(Report report) => _current.AddReport(report);
    public SystemConfig GetSystemConfig() => _current.GetSystemConfig();
    public void UpdateSystemConfig(SystemConfig config) => _current.UpdateSystemConfig(config);
    public SystemStats GetSystemStats() => _current.GetSystemStats();
    public void AddUser(User user) => _current.AddUser(user);
    public void UpdateUser(User user) => _current.UpdateUser(user);
    public User? GetUser(string id) => _current.GetUser(id);
    public User? GetUserByUsername(string username) => _current.GetUserByUsername(username);
    public IEnumerable<User> ListUsers() => _current.ListUsers();
    public void DeleteUser(string id) => _current.DeleteUser(id);
    public void DeleteUserHistory(string userId) => _current.DeleteUserHistory(userId);
    public void AnonymizeUserContent(string userId) => _current.AnonymizeUserContent(userId);
    public User? ValidateUser(string username, string password) => _current.ValidateUser(username, password);
    public bool IsAdminSet() => _current.IsAdminSet();
    public NodeMetadata GetNodeMetadata() => _current.GetNodeMetadata();
    public void UpdateNodeMetadata(NodeMetadata metadata) => _current.UpdateNodeMetadata(metadata);
    public void ResetAllData() => _current.ResetAllData();
    
    public void ResetDatabase()
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
