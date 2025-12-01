using System.Collections.Concurrent;
using MehguViewer.Core.Backend.Models;

namespace MehguViewer.Core.Backend.Services;

public class MemoryRepository
{
    private readonly ConcurrentDictionary<string, Series> _series = new();
    private readonly ConcurrentDictionary<string, Unit> _units = new();
    private readonly ConcurrentDictionary<string, List<Page>> _pages = new();
    private readonly ConcurrentDictionary<string, ReadingProgress> _progress = new();
    private readonly ConcurrentDictionary<string, Comment> _comments = new();
    private readonly ConcurrentDictionary<string, Collection> _collections = new();
    private SystemConfig _systemConfig = new SystemConfig(true, false, "Welcome to MehguViewer Core", new[] { "en" });

    public MemoryRepository()
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
    public void UpdateProgress(ReadingProgress progress)
    {
        var key = $"{progress.series_urn}:{progress.chapter_id}"; // Simplified key
        // In a real DB, we'd query by user + series. Here we assume single user for simplicity or key by series.
        // Actually, the spec says /me/progress, so it's per user.
        // For this memory repo, we'll just store by series_urn since we don't have real users yet.
        _progress.AddOrUpdate(progress.series_urn, progress, (_, _) => progress);
    }
    public ReadingProgress? GetProgress(string seriesUrn) => _progress.TryGetValue(seriesUrn, out var p) ? p : null;
    public IEnumerable<ReadingProgress> GetAllProgress() => _progress.Values;

    // Comments
    public void AddComment(Comment comment) => _comments.TryAdd(comment.id, comment);
    public IEnumerable<Comment> GetComments(string targetUrn) => _comments.Values; // Filter logic needed later

    // Collections
    public void AddCollection(Collection collection) => _collections.TryAdd(collection.id, collection);
    public IEnumerable<Collection> ListCollections() => _collections.Values;

    // System Config
    public SystemConfig GetSystemConfig() => _systemConfig;
    public void UpdateSystemConfig(SystemConfig config) => _systemConfig = config;
}

public static class UrnHelper
{
    public static string CreateSeriesUrn() => $"urn:mvn:series:{Guid.NewGuid()}";
    public static string CreateUserUrn() => $"urn:mvn:user:{Guid.NewGuid()}";
    public static string CreateAssetUrn() => $"urn:mvn:asset:{Guid.NewGuid()}";
    public static string CreateErrorUrn(string code) => $"urn:mvn:error:{code}";
}
