using System.Collections.Concurrent;
using MehguViewer.Shared.Models;

namespace MehguViewer.Core.Backend.Services;

public class MemoryRepository : IRepository
{
    private readonly ConcurrentDictionary<string, Series> _series = new();
    private readonly ConcurrentDictionary<string, Unit> _units = new();
    private readonly ConcurrentDictionary<string, List<Page>> _pages = new();
    private readonly ConcurrentDictionary<string, ReadingProgress> _progress = new();
    private readonly ConcurrentDictionary<string, Comment> _comments = new();
    private readonly ConcurrentDictionary<string, Collection> _collections = new();
    private readonly ConcurrentDictionary<string, User> _users = new();
    
    private SystemConfig _systemConfig = new SystemConfig(false, true, false, "Welcome to MehguViewer Core", new[] { "en" });
    
    private NodeMetadata _nodeMetadata = new NodeMetadata(
        "1.0.0",
        "MehguViewer Core",
        "A MehguViewer Core Node",
        "https://auth.mehgu.example.com",
        new NodeCapabilities(true, true, true),
        new NodeMaintainer("Admin", "admin@example.com")
    );

    public MemoryRepository()
    {
        // No default seeding
    }

    public void SeedDebugData()
    {
        // Seed some data
        var seriesId = UrnHelper.CreateSeriesUrn();
        var series = new Series(
            seriesId,
            null,
            "Demo Manga",
            "A demo manga for testing.",
            new Poster("https://placehold.co/400x600", "Demo Poster"),
            "MANGA",
            new Dictionary<string, string>(),
            "RTL",
            null
        );
        _series.TryAdd(seriesId, series);

        var unitId = Guid.NewGuid().ToString();
        var unit = new Unit(
            unitId,
            seriesId,
            1,
            "Chapter 1",
            DateTime.UtcNow
        );
        _units.TryAdd(unitId, unit);

        // Seed Pages
        for (int i = 1; i <= 5; i++)
        {
            AddPage(unitId, new Page(i, UrnHelper.CreateAssetUrn(), $"https://placehold.co/800x1200?text=Page+{i}"));
        }
    }

    // Series
    public void AddSeries(Series series) => _series.TryAdd(series.id, series);
    public Series? GetSeries(string id) => _series.TryGetValue(id, out var s) ? s : null;
    public IEnumerable<Series> ListSeries() => _series.Values;
    public IEnumerable<Series> SearchSeries(string? query, string? type, string[]? genres, string? status)
    {
        var result = _series.Values.AsEnumerable();
        if (!string.IsNullOrEmpty(query))
        {
            result = result.Where(s => s.title.Contains(query, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrEmpty(type))
        {
            result = result.Where(s => s.media_type.Equals(type, StringComparison.OrdinalIgnoreCase));
        }
        // Genres and Status not implemented in Series model yet, skipping filter
        return result;
    }

    // Units
    public void AddUnit(Unit unit) => _units.TryAdd(unit.id, unit);
    public IEnumerable<Unit> ListUnits(string seriesId) => _units.Values.Where(u => u.series_id == seriesId).OrderBy(u => u.unit_number);
    public Unit? GetUnit(string id) => _units.TryGetValue(id, out var u) ? u : null;

    // Pages
    public void AddPage(string unitId, Page page)
    {
        _pages.AddOrUpdate(unitId, 
            _ => new List<Page> { page }, 
            (_, list) => { list.Add(page); return list; });
    }
    public IEnumerable<Page> GetPages(string unitId) => _pages.TryGetValue(unitId, out var list) ? list.OrderBy(p => p.page_number) : Enumerable.Empty<Page>();

    // Progress
    // Key: "{userId}|{seriesUrn}"
    public void UpdateProgress(string userId, ReadingProgress progress)
    {
        var key = $"{userId}|{progress.series_urn}";
        _progress.AddOrUpdate(key, 
            progress, 
            (k, existing) => progress.updated_at > existing.updated_at ? progress : existing);
    }

    public ReadingProgress? GetProgress(string userId, string seriesUrn) 
    {
        var key = $"{userId}|{seriesUrn}";
        return _progress.TryGetValue(key, out var p) ? p : null;
    }

    public IEnumerable<ReadingProgress> GetLibrary(string userId) 
    {
        var prefix = userId + "|";
        return _progress.Where(kvp => kvp.Key.StartsWith(prefix)).Select(kvp => kvp.Value);
    }

    public IEnumerable<ReadingProgress> GetHistory(string userId)
    {
        var prefix = userId + "|";
        return _progress.Where(kvp => kvp.Key.StartsWith(prefix))
                        .Select(kvp => kvp.Value)
                        .OrderByDescending(p => p.updated_at);
    }

    // Comments
    public void AddComment(Comment comment) => _comments.TryAdd(comment.id, comment);
    public IEnumerable<Comment> GetComments(string targetUrn) => _comments.Values; // Filter logic needed later

    // Votes
    private readonly ConcurrentDictionary<string, Vote> _votes = new(); // Key: "{userId}|{targetId}"
    public void AddVote(string userId, Vote vote)
    {
        var key = $"{userId}|{vote.target_id}";
        if (vote.value == 0)
        {
            _votes.TryRemove(key, out _);
        }
        else
        {
            _votes.AddOrUpdate(key, vote, (_, _) => vote);
        }
    }

    // Collections
    public void AddCollection(Collection collection) => _collections.TryAdd(collection.id, collection);
    public IEnumerable<Collection> ListCollections(string userId) => _collections.Values; // Should filter by user
    public Collection? GetCollection(string id) => _collections.TryGetValue(id, out var c) ? c : null;
    public void UpdateCollection(Collection collection) => _collections.TryUpdate(collection.id, collection, _collections[collection.id]);
    public void DeleteCollection(string id) => _collections.TryRemove(id, out _);

    // Reports
    private readonly ConcurrentBag<Report> _reports = new();
    public void AddReport(Report report) => _reports.Add(report);

    // System Config
    public SystemConfig GetSystemConfig() => _systemConfig;
    public void UpdateSystemConfig(SystemConfig config) => _systemConfig = config;

    // System Stats
    public SystemStats GetSystemStats()
    {
        return new SystemStats(
            _users.Count,
            0, // Storage bytes not tracked in memory repo
            (int)(DateTime.UtcNow - System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime()).TotalSeconds
        );
    }

    // User Management

    // User Management
    public void AddUser(User user) => _users.TryAdd(user.id, user);
    public void UpdateUser(User user) => _users[user.id] = user;
    public User? GetUser(string id) => _users.TryGetValue(id, out var u) ? u : null;
    public User? GetUserByUsername(string username) => _users.Values.FirstOrDefault(u => u.username.Equals(username, StringComparison.OrdinalIgnoreCase));
    public IEnumerable<User> ListUsers() => _users.Values;
    public void DeleteUser(string id) => _users.TryRemove(id, out _);

    public void DeleteUserHistory(string userId)
    {
        var prefix = userId + "|";
        var keysToRemove = _progress.Keys.Where(k => k.StartsWith(prefix)).ToList();
        foreach (var key in keysToRemove)
        {
            _progress.TryRemove(key, out _);
        }
    }

    public void AnonymizeUserContent(string userId)
    {
        // In a real DB, this would be a SQL UPDATE
        foreach (var comment in _comments.Values.Where(c => c.author.uid == userId))
        {
            var anonymizedAuthor = comment.author with { 
                uid = "urn:mvn:user:deleted", 
                display_name = "Deleted User", 
                avatar_url = "", 
                role_badge = "Ghost" 
            };
            var updatedComment = comment with { author = anonymizedAuthor };
            _comments.TryUpdate(comment.id, updatedComment, comment);
        }
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
    
    public bool IsAdminSet() => _users.Values.Any(u => u.role == "Admin");

    // Node Metadata
    public NodeMetadata GetNodeMetadata() => _nodeMetadata;
    public void UpdateNodeMetadata(NodeMetadata metadata) => _nodeMetadata = metadata;

    // Reset Operations
    public void ResetAllData()
    {
        _series.Clear();
        _units.Clear();
        _pages.Clear();
        _progress.Clear();
        _comments.Clear();
        _votes.Clear();
        _collections.Clear();
        _reports.Clear();
        _users.Clear();
        _systemConfig = new SystemConfig(false, false, false, "", Array.Empty<string>());
    }
}

