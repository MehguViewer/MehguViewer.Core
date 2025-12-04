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
    private readonly ConcurrentDictionary<string, Passkey> _passkeys = new();
    
    private SystemConfig _systemConfig = new SystemConfig(
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
    public void UpdateSeries(Series series) => _series[series.id] = series;
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
    public void DeleteSeries(string id)
    {
        _series.TryRemove(id, out _);
        // Also remove associated units and pages
        var unitsToRemove = _units.Values.Where(u => u.series_id == id).Select(u => u.id).ToList();
        foreach (var unitId in unitsToRemove)
        {
            _units.TryRemove(unitId, out _);
            _pages.TryRemove(unitId, out _);
        }
    }

    // Units
    public void AddUnit(Unit unit) => _units.TryAdd(unit.id, unit);
    public void UpdateUnit(Unit unit) => _units[unit.id] = unit;
    public IEnumerable<Unit> ListUnits(string seriesId) => _units.Values.Where(u => u.series_id == seriesId).OrderBy(u => u.unit_number);
    public Unit? GetUnit(string id) => _units.TryGetValue(id, out var u) ? u : null;
    public void DeleteUnit(string id)
    {
        _units.TryRemove(id, out _);
        _pages.TryRemove(id, out _);
    }

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
    public void AddCollection(string userId, Collection collection) => _collections.TryAdd(collection.id, collection);
    public IEnumerable<Collection> ListCollections(string userId) => _collections.Values.Where(c => c.user_id == userId);
    public Collection? GetCollection(string id) => _collections.TryGetValue(id, out var c) ? c : null;
    public void UpdateCollection(Collection collection)
    {
        if (_collections.ContainsKey(collection.id))
        {
            _collections[collection.id] = collection;
        }
    }
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

    // Passkey / WebAuthn
    public void AddPasskey(Passkey passkey) => _passkeys[passkey.id] = passkey;
    public void UpdatePasskey(Passkey passkey) => _passkeys[passkey.id] = passkey;
    public IEnumerable<Passkey> GetPasskeysByUser(string userId) => _passkeys.Values.Where(p => p.user_id == userId);
    public Passkey? GetPasskeyByCredentialId(string credentialId) => _passkeys.Values.FirstOrDefault(p => p.credential_id == credentialId);
    public Passkey? GetPasskey(string id) => _passkeys.TryGetValue(id, out var passkey) ? passkey : null;
    public void DeletePasskey(string id) => _passkeys.TryRemove(id, out _);

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
        _passkeys.Clear();
        _systemConfig = new SystemConfig(
            is_setup_complete: false, 
            registration_open: false, 
            maintenance_mode: false, 
            motd_message: "", 
            default_language_filter: Array.Empty<string>(),
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
}

