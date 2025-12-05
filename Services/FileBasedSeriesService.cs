using System.Collections.Concurrent;
using System.Text.Json;
using MehguViewer.Shared.Models;

namespace MehguViewer.Core.Backend.Services;

/// <summary>
/// File-based storage for Series metadata and content.
/// Stores series as metadata.json files in an organized folder structure.
/// Structure: 
///   data/series/{series-id}/metadata.json
///   data/series/{series-id}/cover.{jpg|png|webp}
///   data/series/{series-id}/units/{unit-number}/metadata.json
///   data/series/{series-id}/units/{unit-number}/pages/001.png, 002.png, ...
/// For localized content:
///   data/series/{series-id}/units/{unit-number}/lang/{lang-code}/pages/...
/// </summary>
public class FileBasedSeriesService
{
    private readonly string _basePath;
    private readonly ILogger<FileBasedSeriesService> _logger;
    private readonly ConcurrentDictionary<string, Series> _seriesCache = new();
    private readonly ConcurrentDictionary<string, Unit> _unitCache = new();
    private readonly JsonSerializerOptions _jsonOptions;
    private bool _initialized = false;

    // Taxonomy caches - keyed by name for deduplication
    private readonly ConcurrentDictionary<string, Author> _authorsByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Scanlator> _scanlatorsByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Group> _groupsByName = new(StringComparer.OrdinalIgnoreCase);

    public FileBasedSeriesService(IConfiguration configuration, ILogger<FileBasedSeriesService> logger)
    {
        _basePath = configuration.GetValue<string>("Storage:DataPath") ?? Path.Combine(Directory.GetCurrentDirectory(), "data");
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
    }

    /// <summary>
    /// Initialize the service by loading all existing series and units from disk.
    /// Also builds taxonomy caches for authors, scanlators, and groups.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_initialized) return;

        var seriesPath = GetSeriesBasePath();
        if (!Directory.Exists(seriesPath))
        {
            Directory.CreateDirectory(seriesPath);
            _logger.LogInformation("Created series directory: {Path}", seriesPath);
        }

        // Load all existing series
        var seriesDirs = Directory.GetDirectories(seriesPath);
        foreach (var dir in seriesDirs)
        {
            var metadataPath = Path.Combine(dir, "metadata.json");
            if (File.Exists(metadataPath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(metadataPath);
                    var series = JsonSerializer.Deserialize<Series>(json, _jsonOptions);
                    if (series != null)
                    {
                        _seriesCache.TryAdd(series.id, series);
                        
                        // Build taxonomy caches
                        foreach (var author in series.authors)
                        {
                            _authorsByName.TryAdd(author.name, author);
                        }
                        foreach (var scanlator in series.scanlators)
                        {
                            _scanlatorsByName.TryAdd(scanlator.name, scanlator);
                        }
                        if (series.groups != null)
                        {
                            foreach (var group in series.groups)
                            {
                                _groupsByName.TryAdd(group.name, group);
                            }
                        }
                        
                        // Load units for this series
                        await LoadUnitsForSeriesAsync(series.id);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to load series from {Path}", metadataPath);
                }
            }
        }

        _logger.LogInformation("Loaded {SeriesCount} series and {UnitCount} units from disk", _seriesCache.Count, _unitCache.Count);
        _initialized = true;
    }

    /// <summary>
    /// Load all units for a specific series.
    /// </summary>
    private async Task LoadUnitsForSeriesAsync(string seriesId)
    {
        var unitsPath = Path.Combine(GetSeriesPath(seriesId), "units");
        if (!Directory.Exists(unitsPath)) return;

        foreach (var unitDir in Directory.GetDirectories(unitsPath))
        {
            var unitMetadataPath = Path.Combine(unitDir, "metadata.json");
            if (File.Exists(unitMetadataPath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(unitMetadataPath);
                    var unit = JsonSerializer.Deserialize<Unit>(json, _jsonOptions);
                    if (unit != null)
                    {
                        _unitCache.TryAdd(unit.id, unit);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to load unit from {Path}", unitMetadataPath);
                }
            }
        }
    }

    /// <summary>
    /// Get the base path for series storage.
    /// </summary>
    public string GetSeriesBasePath() => Path.Combine(_basePath, "series");

    /// <summary>
    /// Get the folder path for a specific series.
    /// </summary>
    public string GetSeriesPath(string seriesId)
    {
        // Extract UUID from URN if needed
        var id = seriesId.StartsWith("urn:mvn:series:") 
            ? seriesId["urn:mvn:series:".Length..] 
            : seriesId;
        return Path.Combine(GetSeriesBasePath(), id);
    }

    /// <summary>
    /// Get the metadata.json path for a series.
    /// </summary>
    public string GetMetadataPath(string seriesId) => Path.Combine(GetSeriesPath(seriesId), "metadata.json");

    /// <summary>
    /// Save a series to disk and update cache.
    /// Also updates taxonomy caches.
    /// </summary>
    public async Task SaveSeriesAsync(Series series)
    {
        var seriesPath = GetSeriesPath(series.id);
        Directory.CreateDirectory(seriesPath);

        var metadataPath = GetMetadataPath(series.id);
        var json = JsonSerializer.Serialize(series, _jsonOptions);
        await File.WriteAllTextAsync(metadataPath, json);

        _seriesCache.AddOrUpdate(series.id, series, (_, _) => series);
        
        // Update taxonomy caches
        foreach (var author in series.authors)
        {
            _authorsByName.TryAdd(author.name, author);
        }
        foreach (var scanlator in series.scanlators)
        {
            _scanlatorsByName.TryAdd(scanlator.name, scanlator);
        }
        if (series.groups != null)
        {
            foreach (var group in series.groups)
            {
                _groupsByName.TryAdd(group.name, group);
            }
        }
        
        _logger.LogInformation("Saved series {Id} to {Path}", series.id, metadataPath);
    }

    /// <summary>
    /// Get a series by ID (from cache).
    /// </summary>
    public Series? GetSeries(string id)
    {
        var normalizedId = id.StartsWith("urn:mvn:series:") ? id : $"urn:mvn:series:{id}";
        return _seriesCache.TryGetValue(normalizedId, out var series) ? series : null;
    }

    /// <summary>
    /// List all series (from cache).
    /// </summary>
    public IEnumerable<Series> ListSeries() => _seriesCache.Values;

    /// <summary>
    /// Delete a series from disk and cache.
    /// </summary>
    public void DeleteSeries(string id)
    {
        var normalizedId = id.StartsWith("urn:mvn:series:") ? id : $"urn:mvn:series:{id}";
        var seriesPath = GetSeriesPath(normalizedId);
        
        if (Directory.Exists(seriesPath))
        {
            Directory.Delete(seriesPath, recursive: true);
        }
        
        _seriesCache.TryRemove(normalizedId, out _);
        
        // Remove units for this series from cache
        var unitsToRemove = _unitCache.Values.Where(u => u.series_id == normalizedId).Select(u => u.id).ToList();
        foreach (var unitId in unitsToRemove)
        {
            _unitCache.TryRemove(unitId, out _);
        }
        
        _logger.LogInformation("Deleted series {Id}", normalizedId);
    }

    /// <summary>
    /// Search series by query.
    /// </summary>
    public IEnumerable<Series> SearchSeries(string? query, string? type, string[]? tags, string? status)
    {
        var result = _seriesCache.Values.AsEnumerable();
        
        if (!string.IsNullOrEmpty(query))
        {
            result = result.Where(s => 
                s.title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                s.description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (s.alt_titles?.Any(t => t.Contains(query, StringComparison.OrdinalIgnoreCase)) ?? false));
        }
        
        if (!string.IsNullOrEmpty(type))
        {
            result = result.Where(s => s.media_type.Equals(type, StringComparison.OrdinalIgnoreCase));
        }
        
        if (tags?.Length > 0)
        {
            result = result.Where(s => tags.Any(t => s.tags.Contains(t, StringComparer.OrdinalIgnoreCase)));
        }
        
        if (!string.IsNullOrEmpty(status))
        {
            result = result.Where(s => s.status?.Equals(status, StringComparison.OrdinalIgnoreCase) ?? false);
        }
        
        return result;
    }

    /// <summary>
    /// Save a cover image for a series.
    /// Returns the relative URL path to the image.
    /// </summary>
    public async Task<string> SaveCoverImageAsync(string seriesId, Stream imageStream, string fileName)
    {
        var seriesPath = GetSeriesPath(seriesId);
        Directory.CreateDirectory(seriesPath);

        // Determine file extension
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (string.IsNullOrEmpty(extension))
        {
            extension = ".jpg"; // Default
        }

        var coverFileName = $"cover{extension}";
        var coverPath = Path.Combine(seriesPath, coverFileName);

        // Delete old cover files
        foreach (var oldCover in Directory.GetFiles(seriesPath, "cover.*"))
        {
            File.Delete(oldCover);
        }

        // Save new cover
        await using var fileStream = File.Create(coverPath);
        await imageStream.CopyToAsync(fileStream);

        // Return relative URL
        var id = seriesId.StartsWith("urn:mvn:series:") 
            ? seriesId["urn:mvn:series:".Length..] 
            : seriesId;
        return $"/api/v1/series/{id}/cover";
    }

    /// <summary>
    /// Get the cover image path for a series.
    /// </summary>
    public string? GetCoverImagePath(string seriesId)
    {
        var seriesPath = GetSeriesPath(seriesId);
        var coverFiles = Directory.GetFiles(seriesPath, "cover.*");
        return coverFiles.FirstOrDefault();
    }

    /// <summary>
    /// Get folder structure for a series.
    /// </summary>
    public SeriesFolderInfo GetFolderStructure(string seriesId)
    {
        var seriesPath = GetSeriesPath(seriesId);
        var info = new SeriesFolderInfo
        {
            Path = seriesPath,
            Exists = Directory.Exists(seriesPath),
            Files = new List<string>(),
            Units = new List<UnitFolderInfo>()
        };

        if (info.Exists)
        {
            info.Files = Directory.GetFiles(seriesPath)
                .Select(Path.GetFileName)
                .Where(f => f != null)
                .Cast<string>()
                .ToList();

            var unitsPath = Path.Combine(seriesPath, "units");
            if (Directory.Exists(unitsPath))
            {
                foreach (var unitDir in Directory.GetDirectories(unitsPath).OrderBy(d => d))
                {
                    var unitInfo = new UnitFolderInfo
                    {
                        Name = Path.GetFileName(unitDir),
                        Path = unitDir,
                        Files = Directory.GetFiles(unitDir)
                            .Select(Path.GetFileName)
                            .Where(f => f != null)
                            .Cast<string>()
                            .ToList()
                    };
                    info.Units.Add(unitInfo);
                }
            }
        }

        return info;
    }

    /// <summary>
    /// Get all authors from taxonomy cache (deduplicated by name).
    /// </summary>
    public Author[] GetAllAuthors() => _authorsByName.Values.OrderBy(a => a.name).ToArray();

    /// <summary>
    /// Get all scanlators from taxonomy cache (deduplicated by name).
    /// </summary>
    public Scanlator[] GetAllScanlators() => _scanlatorsByName.Values.OrderBy(s => s.name).ToArray();

    /// <summary>
    /// Get all groups from taxonomy cache (deduplicated by name).
    /// </summary>
    public Group[] GetAllGroups() => _groupsByName.Values.OrderBy(g => g.name).ToArray();

    /// <summary>
    /// Aggregate all tags from series in cache.
    /// </summary>
    public string[] GetAllTags()
    {
        return _seriesCache.Values
            .SelectMany(s => s.tags)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t)
            .ToArray();
    }

    /// <summary>
    /// Find an existing author by name (case-insensitive).
    /// Returns the existing author if found, to maintain consistent IDs.
    /// </summary>
    public Author? FindAuthorByName(string name)
    {
        return _authorsByName.TryGetValue(name, out var author) ? author : null;
    }

    /// <summary>
    /// Find an existing scanlator by name (case-insensitive).
    /// Returns the existing scanlator if found, to maintain consistent IDs.
    /// </summary>
    public Scanlator? FindScanlatorByName(string name)
    {
        return _scanlatorsByName.TryGetValue(name, out var scanlator) ? scanlator : null;
    }

    /// <summary>
    /// Find an existing group by name (case-insensitive).
    /// Returns the existing group if found, to maintain consistent IDs.
    /// </summary>
    public Group? FindGroupByName(string name)
    {
        return _groupsByName.TryGetValue(name, out var group) ? group : null;
    }

    // ==================== Unit Operations ====================

    /// <summary>
    /// Get the path for a unit folder.
    /// </summary>
    public string GetUnitPath(string seriesId, double unitNumber)
    {
        return Path.Combine(GetSeriesPath(seriesId), "units", unitNumber.ToString("F1").Replace(".", "_"));
    }

    /// <summary>
    /// Get the pages folder path for a unit.
    /// </summary>
    public string GetUnitPagesPath(string seriesId, double unitNumber, string? language = null)
    {
        var unitPath = GetUnitPath(seriesId, unitNumber);
        if (!string.IsNullOrEmpty(language))
        {
            return Path.Combine(unitPath, "lang", language, "pages");
        }
        return Path.Combine(unitPath, "pages");
    }

    /// <summary>
    /// Save a unit to disk and update cache.
    /// </summary>
    public async Task SaveUnitAsync(Unit unit)
    {
        var unitPath = GetUnitPath(unit.series_id, unit.unit_number);
        Directory.CreateDirectory(unitPath);

        var metadataPath = Path.Combine(unitPath, "metadata.json");
        var json = JsonSerializer.Serialize(unit, _jsonOptions);
        await File.WriteAllTextAsync(metadataPath, json);

        _unitCache.AddOrUpdate(unit.id, unit, (_, _) => unit);
        _logger.LogInformation("Saved unit {Id} to {Path}", unit.id, metadataPath);
    }

    /// <summary>
    /// Get a unit by ID (from cache).
    /// </summary>
    public Unit? GetUnit(string id) => _unitCache.TryGetValue(id, out var unit) ? unit : null;

    /// <summary>
    /// Get all units for a series (from cache).
    /// </summary>
    public IEnumerable<Unit> GetUnitsForSeries(string seriesId)
    {
        var normalizedId = seriesId.StartsWith("urn:mvn:series:") ? seriesId : $"urn:mvn:series:{seriesId}";
        return _unitCache.Values.Where(u => u.series_id == normalizedId).OrderBy(u => u.unit_number);
    }

    /// <summary>
    /// Delete a unit from disk and cache.
    /// </summary>
    public void DeleteUnit(string seriesId, string unitId)
    {
        if (_unitCache.TryRemove(unitId, out var unit))
        {
            var unitPath = GetUnitPath(seriesId, unit.unit_number);
            if (Directory.Exists(unitPath))
            {
                Directory.Delete(unitPath, recursive: true);
            }
            _logger.LogInformation("Deleted unit {Id}", unitId);
        }
    }

    /// <summary>
    /// Save a page image for a unit.
    /// Returns the relative URL path to the image.
    /// </summary>
    public async Task<string> SavePageImageAsync(string seriesId, double unitNumber, int pageNumber, Stream imageStream, string fileName, string? language = null)
    {
        var pagesPath = GetUnitPagesPath(seriesId, unitNumber, language);
        Directory.CreateDirectory(pagesPath);

        // Determine file extension
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (string.IsNullOrEmpty(extension))
        {
            extension = ".png"; // Default
        }

        var pageFileName = $"{pageNumber:D3}{extension}";
        var pagePath = Path.Combine(pagesPath, pageFileName);

        // Save image
        await using var fileStream = File.Create(pagePath);
        await imageStream.CopyToAsync(fileStream);

        _logger.LogInformation("Saved page {PageNumber} to {Path}", pageNumber, pagePath);
        return pagePath;
    }

    /// <summary>
    /// Download an image from URL and save it as a page.
    /// </summary>
    public async Task<string> DownloadAndSavePageAsync(HttpClient httpClient, string seriesId, double unitNumber, int pageNumber, string imageUrl, string? language = null)
    {
        var response = await httpClient.GetAsync(imageUrl);
        response.EnsureSuccessStatusCode();

        var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/png";
        var extension = contentType switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            "image/gif" => ".gif",
            _ => ".png"
        };

        await using var stream = await response.Content.ReadAsStreamAsync();
        return await SavePageImageAsync(seriesId, unitNumber, pageNumber, stream, $"page{extension}", language);
    }

    /// <summary>
    /// Get all page files for a unit.
    /// </summary>
    public List<PageFileInfo> GetUnitPages(string seriesId, double unitNumber, string? language = null)
    {
        var pagesPath = GetUnitPagesPath(seriesId, unitNumber, language);
        var pages = new List<PageFileInfo>();

        if (!Directory.Exists(pagesPath)) return pages;

        var files = Directory.GetFiles(pagesPath)
            .Where(f => IsImageFile(f))
            .OrderBy(f => f)
            .ToList();

        for (int i = 0; i < files.Count; i++)
        {
            var file = files[i];
            pages.Add(new PageFileInfo
            {
                PageNumber = i + 1,
                FileName = Path.GetFileName(file),
                FilePath = file,
                FileSize = new FileInfo(file).Length
            });
        }

        return pages;
    }

    private bool IsImageFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif";
    }
}

/// <summary>
/// Information about a series folder structure.
/// </summary>
public class SeriesFolderInfo
{
    public string Path { get; set; } = "";
    public bool Exists { get; set; }
    public List<string> Files { get; set; } = new();
    public List<UnitFolderInfo> Units { get; set; } = new();
}

/// <summary>
/// Information about a unit (chapter) folder.
/// </summary>
public class UnitFolderInfo
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public List<string> Files { get; set; } = new();
}

/// <summary>
/// Information about a page file.
/// </summary>
public class PageFileInfo
{
    public int PageNumber { get; set; }
    public string FileName { get; set; } = "";
    public string FilePath { get; set; } = "";
    public long FileSize { get; set; }
}
