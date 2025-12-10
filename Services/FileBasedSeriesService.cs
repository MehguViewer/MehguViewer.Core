using MehguViewer.Core.Helpers;
using System.Collections.Concurrent;
using System.Text.Json;
using MehguViewer.Core.Shared;
using MehguViewer.Core.Infrastructures;

namespace MehguViewer.Core.Services;

/// <summary>
/// File-based storage service for Series metadata, units, and associated media.
/// Manages series data as JSON files in an organized directory structure with in-memory caching.
/// </summary>
/// <remarks>
/// <para><strong>Directory Structure:</strong></para>
/// <code>
/// data/series/
///   {series-id}/
///     metadata.json                          (Series metadata)
///     cover-{variant}.{ext}                  (Cover images: thumbnail/web/raw)
///     units/
///       {unit-number}/
///         metadata.json                      (Unit metadata)
///         pages/
///           001.png, 002.png, ...            (Default language pages)
///         lang/
///           {lang-code}/
///             pages/
///               001.png, 002.png, ...        (Localized pages)
/// </code>
/// <para><strong>Caching Strategy:</strong></para>
/// <list type="bullet">
/// <item>All series and units loaded into ConcurrentDictionary on initialization</item>
/// <item>Taxonomy entities (authors, scanlators, groups) deduplicated by name</item>
/// <item>Cache kept in sync with file system via SaveSeriesAsync/DeleteSeries</item>
/// <item>RefreshCacheAsync rebuilds entire cache from disk</item>
/// </list>
/// <para><strong>URN Handling:</strong></para>
/// <para>Supports both full URNs (urn:mvn:series:{uuid}) and bare UUIDs throughout API.</para>
/// </remarks>
public class FileBasedSeriesService
{
    #region Constants
    
    /// <summary>URN prefix for series identifiers.</summary>
    private const string SeriesUrnPrefix = "urn:mvn:series:";
    
    /// <summary>URN prefix for unit identifiers.</summary>
    private const string UnitUrnPrefix = "urn:mvn:unit:";
    
    /// <summary>Default storage subdirectory for series data.</summary>
    private const string SeriesSubdirectory = "series";
    
    /// <summary>Default file extension for cover images.</summary>
    private const string DefaultCoverExtension = ".jpg";
    
    /// <summary>Metadata filename for series and units.</summary>
    private const string MetadataFileName = "metadata.json";
    
    /// <summary>Units subdirectory within series folder.</summary>
    private const string UnitsSubdirectory = "units";
    
    /// <summary>Language-specific content subdirectory.</summary>
    private const string LanguageSubdirectory = "lang";
    
    /// <summary>Cover file search pattern (all extensions).</summary>
    private const string CoverFilePattern = "cover*.*";
    
    #endregion
    
    #region Fields
    
    private readonly string _basePath;
    private readonly ILogger<FileBasedSeriesService> _logger;
    private readonly ConcurrentDictionary<string, Series> _seriesCache = new();
    private readonly ConcurrentDictionary<string, Unit> _unitCache = new();
    private readonly JsonSerializerOptions _jsonOptions;
    private bool _initialized = false;

    /// <summary>Taxonomy cache for authors, keyed by name (case-insensitive).</summary>
    private readonly ConcurrentDictionary<string, Author> _authorsByName = new(StringComparer.OrdinalIgnoreCase);
    
    /// <summary>Taxonomy cache for scanlators, keyed by name (case-insensitive).</summary>
    private readonly ConcurrentDictionary<string, Scanlator> _scanlatorsByName = new(StringComparer.OrdinalIgnoreCase);
    
    /// <summary>Taxonomy cache for groups, keyed by name (case-insensitive).</summary>
    private readonly ConcurrentDictionary<string, Group> _groupsByName = new(StringComparer.OrdinalIgnoreCase);
    
    #endregion
    
    #region Constructor
    
    /// <summary>
    /// Initializes a new instance of the <see cref="FileBasedSeriesService"/> class.
    /// </summary>
    /// <param name="configuration">Application configuration containing storage path.</param>
    /// <param name="logger">Logger for service operations.</param>
    /// <exception cref="ArgumentNullException">Thrown when configuration or logger is null.</exception>
    /// <remarks>
    /// Configures JSON serialization with snake_case naming and indented formatting.
    /// Base path defaults to "./data" if not specified in configuration.
    /// Validates and sanitizes the storage path for security.
    /// </remarks>
    public FileBasedSeriesService(IConfiguration configuration, ILogger<FileBasedSeriesService> logger)
    {
        ArgumentNullException.ThrowIfNull(configuration, nameof(configuration));
        ArgumentNullException.ThrowIfNull(logger, nameof(logger));
        
        _logger = logger;
        
        var configuredPath = configuration.GetValue<string>("Storage:DataPath");
        _basePath = string.IsNullOrWhiteSpace(configuredPath) 
            ? Path.Combine(Directory.GetCurrentDirectory(), "data")
            : Path.GetFullPath(configuredPath); // Normalize path for security
        
        _logger.LogDebug("FileBasedSeriesService initialized with base path: {BasePath}", _basePath);
        
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
    }
    
    #endregion
    
    #region Initialization and Cache Management

    /// <summary>
    /// Initializes the service by loading all existing series and units from disk.
    /// Builds in-memory caches for fast access and taxonomy entity deduplication.
    /// </summary>
    /// <returns>Task representing the asynchronous initialization operation.</returns>
    /// <remarks>
    /// <para><strong>Initialization Steps:</strong></para>
    /// <list type="number">
    /// <item>Create series directory if not exists</item>
    /// <item>Scan all series directories for metadata.json files</item>
    /// <item>Deserialize and cache each series</item>
    /// <item>Build taxonomy caches (authors, scanlators, groups) with case-insensitive deduplication</item>
    /// <item>Load all units for each series</item>
    /// <item>Mark service as initialized</item>
    /// </list>
    /// <para><strong>Performance Note:</strong></para>
    /// <para>Loads entire library into memory. For large libraries (>10k series), consider lazy loading or pagination.</para>
    /// </remarks>
    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            _logger.LogDebug("Service already initialized, skipping re-initialization");
            return;
        }

        _logger.LogInformation("Starting FileBasedSeriesService initialization...");
        var startTime = DateTime.UtcNow;
        
        var seriesPath = GetSeriesBasePath();
        if (!Directory.Exists(seriesPath))
        {
            try
            {
                Directory.CreateDirectory(seriesPath);
                _logger.LogInformation("Created series directory: {Path}", seriesPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create series directory: {Path}", seriesPath);
                throw;
            }
        }

        // Load all existing series
        // TODO: Optimize this to not load everything into memory at once for large libraries (>10k series)
        // Consider implementing lazy loading or pagination for better scalability
        var seriesDirs = Directory.GetDirectories(seriesPath);
        _logger.LogInformation("Found {Count} series directories to load", seriesDirs.Length);
        
        var loadedCount = 0;
        var failedCount = 0;
        
        foreach (var dir in seriesDirs)
        {
            var metadataPath = Path.Combine(dir, MetadataFileName);
            if (!File.Exists(metadataPath))
            {
                _logger.LogWarning("Metadata file not found in directory: {Directory}", dir);
                continue;
            }
            
            try
            {
                var json = await File.ReadAllTextAsync(metadataPath);
                
                if (string.IsNullOrWhiteSpace(json))
                {
                    _logger.LogWarning("Empty metadata file found: {Path}", metadataPath);
                    failedCount++;
                    continue;
                }
                
                var series = JsonSerializer.Deserialize<Series>(json, _jsonOptions);
                if (series != null)
                {
                    if (_seriesCache.TryAdd(series.id, series))
                    {
                        loadedCount++;
                    }
                    else
                    {
                        _logger.LogWarning("Duplicate series ID detected: {SeriesId} at {Path}", series.id, metadataPath);
                    }
                        
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
                else
                {
                    _logger.LogWarning("Failed to deserialize series from {Path}", metadataPath);
                    failedCount++;
                }
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "JSON deserialization error in {Path}", metadataPath);
                failedCount++;
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, "IO error reading {Path}", metadataPath);
                failedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error loading series from {Path}", metadataPath);
                failedCount++;
            }
        }
        
        _initialized = true;
        var duration = (DateTime.UtcNow - startTime).TotalSeconds;
        
        _logger.LogInformation(
            "FileBasedSeriesService initialization complete: {SeriesCount} series, {UnitCount} units loaded in {Duration:F2}s (Failed: {FailedCount})",
            _seriesCache.Count, _unitCache.Count, duration, failedCount);
    }

    /// <summary>
    /// Synchronizes edit permissions with file system by removing orphaned permission records.
    /// Called after library initialization to clean up permissions for deleted series/units.
    /// </summary>
    /// <param name="repository">Repository instance to synchronize permissions with.</param>
    /// <exception cref="ArgumentNullException">Thrown when repository is null.</exception>
    /// <remarks>
    /// Delegates to repository's SyncEditPermissions method.
    /// Ensures permission records match current file system state.
    /// </remarks>
    public void SyncPermissions(IRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository, nameof(repository));
        
        _logger.LogInformation("Starting edit permissions synchronization with file system...");
        
        try
        {
            repository.SyncEditPermissions();
            _logger.LogInformation("Edit permissions synchronized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to synchronize edit permissions");
            throw;
        }
    }

    /// <summary>
    /// Refreshes all caches by reloading series and units from the filesystem.
    /// </summary>
    /// <returns>Task representing the asynchronous refresh operation.</returns>
    /// <remarks>
    /// <para><strong>Refresh Process:</strong></para>
    /// <list type="number">
    /// <item>Clear series cache</item>
    /// <item>Clear unit cache</item>
    /// <item>Clear all taxonomy caches (authors, scanlators, groups)</item>
    /// <item>Mark as uninitialized</item>
    /// <item>Reload all data via InitializeAsync</item>
    /// </list>
    /// <para>Use when file system changes bypass the service (e.g., manual file operations).</para>
    /// </remarks>
    public async Task RefreshCacheAsync()
    {
        _logger.LogInformation("Starting cache refresh from filesystem...");
        var startTime = DateTime.UtcNow;
        
        try
        {
            // Clear all caches
            var previousSeriesCount = _seriesCache.Count;
            var previousUnitCount = _unitCache.Count;
            
            _seriesCache.Clear();
            _unitCache.Clear();
            _authorsByName.Clear();
            _scanlatorsByName.Clear();
            _groupsByName.Clear();
            
            _logger.LogDebug("Cleared {SeriesCount} series and {UnitCount} units from cache", 
                previousSeriesCount, previousUnitCount);
            
            // Mark as uninitialized so InitializeAsync will reload everything
            _initialized = false;
            
            // Reload from filesystem
            await InitializeAsync();
            
            var duration = (DateTime.UtcNow - startTime).TotalSeconds;
            _logger.LogInformation(
                "Cache refresh complete: {SeriesCount} series, {UnitCount} units reloaded in {Duration:F2}s", 
                _seriesCache.Count, _unitCache.Count, duration);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cache refresh failed");
            throw;
        }
    }
    
    #endregion
    
    #region Private Helper Methods

    /// <summary>
    /// Loads all units for a specific series from disk into cache.
    /// </summary>
    /// <param name="seriesId">Series ID (URN or UUID) to load units for.</param>
    /// <returns>Task representing the asynchronous load operation.</returns>
    /// <remarks>
    /// Scans units subdirectory for numbered unit folders containing metadata.json.
    /// Silently skips units that fail to deserialize (logs error).
    /// </remarks>
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
    
    #endregion
    
    #region Path Utilities

    /// <summary>
    /// Gets the base directory path for all series storage.
    /// </summary>
    /// <returns>Absolute path to series root directory.</returns>
    /// <remarks>Path is normalized to prevent directory traversal attacks.</remarks>
    public string GetSeriesBasePath() => Path.GetFullPath(Path.Combine(_basePath, SeriesSubdirectory));

    /// <summary>
    /// Gets the directory path for a specific series.
    /// </summary>
    /// <param name="seriesId">Series ID (URN or UUID).</param>
    /// <returns>Absolute path to series directory.</returns>
    /// <exception cref="ArgumentException">Thrown when seriesId is null, empty, or contains invalid characters.</exception>
    /// <remarks>
    /// Strips URN prefix if present, using only UUID for directory name.
    /// Validates path to prevent directory traversal attacks.
    /// </remarks>
    public string GetSeriesPath(string seriesId)
    {
        if (string.IsNullOrWhiteSpace(seriesId))
        {
            throw new ArgumentException("Series ID cannot be null or empty", nameof(seriesId));
        }
        
        // Extract UUID from URN if needed
        var id = seriesId.StartsWith(SeriesUrnPrefix, StringComparison.Ordinal) 
            ? seriesId[SeriesUrnPrefix.Length..] 
            : seriesId;
        
        // Validate ID doesn't contain path traversal characters
        if (id.Contains("..") || id.Contains('/') || id.Contains('\\'))
        {
            _logger.LogWarning("Potentially malicious series ID detected: {SeriesId}", seriesId);
            throw new ArgumentException($"Invalid series ID format: {seriesId}", nameof(seriesId));
        }
        
        return Path.GetFullPath(Path.Combine(GetSeriesBasePath(), id));
    }

    /// <summary>
    /// Gets the metadata.json file path for a series.
    /// </summary>
    /// <param name="seriesId">Series ID (URN or UUID).</param>
    /// <returns>Absolute path to series metadata.json file.</returns>
    public string GetMetadataPath(string seriesId) => Path.Combine(GetSeriesPath(seriesId), MetadataFileName);
    
    #endregion
    
    #region Series CRUD Operations

    /// <summary>
    /// Save a series to disk and update cache.
    /// Also updates taxonomy caches.
    /// </summary>
    /// <param name="series">The series object to save.</param>
    /// <exception cref="ArgumentNullException">Thrown when series is null.</exception>
    /// <exception cref="IOException">Thrown when file operations fail.</exception>
    public async Task SaveSeriesAsync(Series series)
    {
        ArgumentNullException.ThrowIfNull(series, nameof(series));
        
        if (string.IsNullOrWhiteSpace(series.id))
        {
            throw new ArgumentException("Series ID cannot be null or empty", nameof(series));
        }
        
        _logger.LogDebug("Saving series {SeriesId} ({Title})", series.id, series.title);
        
        try
        {
            var seriesPath = GetSeriesPath(series.id);
            
            if (!Directory.Exists(seriesPath))
            {
                Directory.CreateDirectory(seriesPath);
                _logger.LogDebug("Created directory for series {SeriesId}: {Path}", series.id, seriesPath);
            }

            var metadataPath = GetMetadataPath(series.id);
            var json = JsonSerializer.Serialize(series, _jsonOptions);
            
            // Write atomically using temp file
            var tempPath = $"{metadataPath}.tmp";
            await File.WriteAllTextAsync(tempPath, json);
            File.Move(tempPath, metadataPath, overwrite: true);

            var isNew = !_seriesCache.ContainsKey(series.id);
            _seriesCache.AddOrUpdate(series.id, series, (_, _) => series);
            
            _logger.LogInformation("{Action} series {SeriesId} ({Title}) in cache", 
                isNew ? "Added" : "Updated", series.id, series.title);
            
            // Update taxonomy caches with null safety
            var taxonomyUpdateCount = 0;
            
            if (series.authors != null)
            {
                foreach (var author in series.authors)
                {
                    if (author != null && !string.IsNullOrWhiteSpace(author.name))
                    {
                        if (_authorsByName.TryAdd(author.name, author))
                        {
                            taxonomyUpdateCount++;
                        }
                    }
                }
            }
            
            if (series.scanlators != null)
            {
                foreach (var scanlator in series.scanlators)
                {
                    if (scanlator != null && !string.IsNullOrWhiteSpace(scanlator.name))
                    {
                        if (_scanlatorsByName.TryAdd(scanlator.name, scanlator))
                        {
                            taxonomyUpdateCount++;
                        }
                    }
                }
            }
            
            if (series.groups != null)
            {
                foreach (var group in series.groups)
                {
                    if (group != null && !string.IsNullOrWhiteSpace(group.name))
                    {
                        if (_groupsByName.TryAdd(group.name, group))
                        {
                            taxonomyUpdateCount++;
                        }
                    }
                }
            }
            
            if (taxonomyUpdateCount > 0)
            {
                _logger.LogDebug("Updated {Count} taxonomy entries for series {SeriesId}", taxonomyUpdateCount, series.id);
            }
            
            _logger.LogInformation("Successfully saved series {SeriesId} to {Path}", series.id, metadataPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save series {SeriesId}", series.id);
            throw;
        }
    }

    /// <summary>
    /// Get a series by ID (from cache).
    /// </summary>
    /// <param name="id">Series ID (URN or UUID).</param>
    /// <returns>Series object if found, null otherwise.</returns>
    public Series? GetSeries(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            _logger.LogWarning("GetSeries called with null or empty ID");
            return null;
        }
        
        var normalizedId = id.StartsWith(SeriesUrnPrefix, StringComparison.Ordinal) ? id : $"{SeriesUrnPrefix}{id}";
        var found = _seriesCache.TryGetValue(normalizedId, out var series);
        
        if (!found)
        {
            _logger.LogDebug("Series not found in cache: {SeriesId}", normalizedId);
        }
        
        return series;
    }

    /// <summary>
    /// List all series (from cache).
    /// </summary>
    /// <returns>Collection of all cached series.</returns>
    /// <remarks>Returns a snapshot of the cache to prevent concurrent modification issues.</remarks>
    public IEnumerable<Series> ListSeries()
    {
        _logger.LogDebug("Listing all series: {Count} total", _seriesCache.Count);
        return _seriesCache.Values.ToList(); // Return snapshot to prevent concurrent modification
    }

    /// <summary>
    /// Delete a series from disk and cache.
    /// </summary>
    /// <param name="id">Series ID (URN or UUID).</param>
    /// <exception cref="ArgumentException">Thrown when id is null or empty.</exception>
    public void DeleteSeries(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Series ID cannot be null or empty", nameof(id));
        }
        
        var normalizedId = id.StartsWith(SeriesUrnPrefix, StringComparison.Ordinal) ? id : $"{SeriesUrnPrefix}{id}";
        var seriesPath = GetSeriesPath(normalizedId);
        
        _logger.LogInformation("Deleting series {SeriesId} from path {Path}", normalizedId, seriesPath);
        
        var deleteSuccess = false;
        var cacheRemoveSuccess = false;
        
        // Delete from file system
        if (Directory.Exists(seriesPath))
        {
            try
            {
                // Get subdirectories count for logging
                var filesCount = Directory.GetFiles(seriesPath, "*", SearchOption.AllDirectories).Length;
                
                Directory.Delete(seriesPath, recursive: true);
                deleteSuccess = true;
                _logger.LogInformation("Deleted series directory: {Path} ({FileCount} files)", seriesPath, filesCount);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(ex, "Access denied when deleting series directory: {Path}", seriesPath);
                throw;
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, "IO error when deleting series directory: {Path}", seriesPath);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error when deleting series directory: {Path}", seriesPath);
                throw;
            }
        }
        else
        {
            _logger.LogWarning("Series directory not found: {Path}", seriesPath);
        }
        
        // Remove from cache
        cacheRemoveSuccess = _seriesCache.TryRemove(normalizedId, out var removedSeries);
        if (cacheRemoveSuccess && removedSeries != null)
        {
            _logger.LogInformation("Removed series {SeriesId} ({Title}) from cache", normalizedId, removedSeries.title);
        }
        else
        {
            _logger.LogWarning("Series {SeriesId} not found in cache. Cache keys sample: {Keys}", 
                normalizedId, 
                string.Join(", ", _seriesCache.Keys.Take(5)));
        }
        
        // Remove units for this series from cache
        var unitsToRemove = _unitCache.Values
            .Where(u => u.series_id == normalizedId)
            .Select(u => u.id)
            .ToList();
            
        _logger.LogDebug("Removing {Count} units for series {SeriesId}", unitsToRemove.Count, normalizedId);
        
        var unitsRemovedCount = 0;
        foreach (var unitId in unitsToRemove)
        {
            if (_unitCache.TryRemove(unitId, out _))
            {
                unitsRemovedCount++;
            }
        }
        
        _logger.LogInformation(
            "Successfully deleted series {SeriesId}: FileSystem={FileSystem}, Cache={Cache}, Units={UnitsRemoved}/{UnitsTotal}",
            normalizedId, deleteSuccess, cacheRemoveSuccess, unitsRemovedCount, unitsToRemove.Count);
    }

    /// <summary>
    /// Search series by query with multiple filter criteria.
    /// </summary>
    /// <param name="query">Text search across title, description, and alt_titles.</param>
    /// <param name="type">Filter by media type.</param>
    /// <param name="tags">Filter by tags (series must contain at least one tag).</param>
    /// <param name="status">Filter by publication status.</param>
    /// <returns>Filtered series collection.</returns>
    /// <remarks>All filters are case-insensitive. Returns snapshot to prevent concurrent modification.</remarks>
    public IEnumerable<Series> SearchSeries(string? query, string? type, string[]? tags, string? status)
    {
        _logger.LogDebug(
            "Searching series: query={Query}, type={Type}, tags={Tags}, status={Status}",
            query ?? "null", type ?? "null", tags != null ? string.Join(",", tags) : "null", status ?? "null");
        
        var startTime = DateTime.UtcNow;
        IEnumerable<Series> result = _seriesCache.Values.ToList(); // Snapshot for thread safety
        
        if (!string.IsNullOrWhiteSpace(query))
        {
            result = result.Where(s => 
                (s.title?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (s.description?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (s.alt_titles?.Any(t => t?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ?? false));
        }
        
        if (!string.IsNullOrWhiteSpace(type))
        {
            result = result.Where(s => 
                s.media_type?.Equals(type, StringComparison.OrdinalIgnoreCase) ?? false);
        }
        
        if (tags?.Length > 0)
        {
            var validTags = tags.Where(t => !string.IsNullOrWhiteSpace(t)).ToArray();
            if (validTags.Length > 0)
            {
                result = result.Where(s => 
                    s.tags != null && validTags.Any(t => s.tags.Contains(t, StringComparer.OrdinalIgnoreCase)));
            }
        }
        
        if (!string.IsNullOrWhiteSpace(status))
        {
            result = result.Where(s => 
                s.status?.Equals(status, StringComparison.OrdinalIgnoreCase) ?? false);
        }
        
        var resultList = result.ToList();
        var duration = (DateTime.UtcNow - startTime).TotalMilliseconds;
        
        _logger.LogDebug("Search completed: {Count} results in {Duration:F2}ms", resultList.Count, duration);
        
        return resultList;
    }

    /// <summary>
    /// Save a cover image for a series.
    /// Returns the relative URL path to the image.
    /// </summary>
    /// <param name="seriesId">Series ID (URN or UUID).</param>
    /// <param name="imageStream">Stream containing image data.</param>
    /// <param name="fileName">Original filename for extension detection.</param>
    /// <returns>Relative URL path to the saved image.</returns>
    /// <exception cref="ArgumentException">Thrown for invalid inputs.</exception>
    /// <exception cref="ArgumentNullException">Thrown when imageStream is null.</exception>
    public async Task<string> SaveCoverImageAsync(string seriesId, Stream imageStream, string fileName)
    {
        if (string.IsNullOrWhiteSpace(seriesId))
        {
            throw new ArgumentException("Series ID cannot be null or empty", nameof(seriesId));
        }
        
        ArgumentNullException.ThrowIfNull(imageStream, nameof(imageStream));
        
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("Filename cannot be null or empty", nameof(fileName));
        }
        
        _logger.LogDebug("Saving cover image for series {SeriesId}: {FileName}", seriesId, fileName);
        
        try
        {
            var seriesPath = GetSeriesPath(seriesId);
            Directory.CreateDirectory(seriesPath);

            // Determine and validate file extension
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            if (string.IsNullOrEmpty(extension))
            {
                extension = DefaultCoverExtension;
                _logger.LogDebug("No extension detected, using default: {Extension}", extension);
            }
            
            // Validate extension is an image format
            var validExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
            if (!validExtensions.Contains(extension))
            {
                _logger.LogWarning("Unsupported image extension: {Extension}, defaulting to .jpg", extension);
                extension = ".jpg";
            }

            var coverFileName = $"cover{extension}";
            var coverPath = Path.Combine(seriesPath, coverFileName);

            // Delete old cover files to avoid orphaned files
            var deletedCount = 0;
            foreach (var oldCover in Directory.GetFiles(seriesPath, CoverFilePattern))
            {
                try
                {
                    File.Delete(oldCover);
                    deletedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete old cover: {Path}", oldCover);
                }
            }
            
            if (deletedCount > 0)
            {
                _logger.LogDebug("Deleted {Count} old cover files", deletedCount);
            }

            // Save new cover atomically using temp file
            var tempPath = $"{coverPath}.tmp";
            await using (var fileStream = File.Create(tempPath))
            {
                await imageStream.CopyToAsync(fileStream);
            }
            
            File.Move(tempPath, coverPath, overwrite: true);
            
            var fileInfo = new FileInfo(coverPath);
            _logger.LogInformation(
                "Saved cover image for series {SeriesId}: {FileName} ({Size} bytes)",
                seriesId, coverFileName, fileInfo.Length);

            // Return relative URL
            var id = seriesId.StartsWith(SeriesUrnPrefix, StringComparison.Ordinal) 
                ? seriesId[SeriesUrnPrefix.Length..] 
                : seriesId;
            return $"/api/v1/series/{id}/cover";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save cover image for series {SeriesId}", seriesId);
            throw;
        }
    }

    /// <summary>
    /// Save cover image variants (THUMBNAIL, WEB, RAW) for a series.
    /// Returns the relative URL to the cover (defaults to WEB variant).
    /// </summary>
    /// <param name="seriesId">Series ID (URN or UUID).</param>
    /// <param name="variants">Dictionary of variant names to image data.</param>
    /// <param name="extension">File extension including leading dot.</param>
    /// <param name="language">Optional language code for localized covers.</param>
    /// <returns>Relative URL to the web variant of the cover.</returns>
    /// <exception cref="ArgumentException">Thrown for invalid inputs.</exception>
    /// <exception cref="ArgumentNullException">Thrown when variants is null.</exception>
    public async Task<string> SaveCoverImageVariantsAsync(string seriesId, Dictionary<string, byte[]> variants, string extension, string? language = null)
    {
        if (string.IsNullOrWhiteSpace(seriesId))
        {
            throw new ArgumentException("Series ID cannot be null or empty", nameof(seriesId));
        }
        
        ArgumentNullException.ThrowIfNull(variants, nameof(variants));
        
        if (variants.Count == 0)
        {
            throw new ArgumentException("Variants dictionary cannot be empty", nameof(variants));
        }
        
        if (string.IsNullOrWhiteSpace(extension))
        {
            throw new ArgumentException("Extension cannot be null or empty", nameof(extension));
        }
        
        _logger.LogDebug(
            "Saving {VariantCount} cover variants for series {SeriesId} (language: {Language})",
            variants.Count, seriesId, language ?? "default");
        
        try
        {
            var seriesPath = GetSeriesPath(seriesId);
            string coverBasePath;
            
            if (!string.IsNullOrEmpty(language))
            {
                // Localized cover: data/series/{id}/lang/{lang-code}/
                coverBasePath = Path.Combine(seriesPath, LanguageSubdirectory, language);
            }
            else
            {
                // Default cover: data/series/{id}/
                coverBasePath = seriesPath;
            }
            
            Directory.CreateDirectory(coverBasePath);

            // Delete old cover files (all variants) for this language
            var deletedCount = 0;
            if (Directory.Exists(coverBasePath))
            {
                foreach (var oldCover in Directory.GetFiles(coverBasePath, CoverFilePattern))
                {
                    try
                    {
                        File.Delete(oldCover);
                        deletedCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete old cover variant: {Path}", oldCover);
                    }
                }
            }
            
            if (deletedCount > 0)
            {
                _logger.LogDebug("Deleted {Count} old cover variant files", deletedCount);
            }

            // Save each variant atomically
            var savedCount = 0;
            foreach (var variant in variants)
            {
                if (variant.Value == null || variant.Value.Length == 0)
                {
                    _logger.LogWarning("Skipping empty variant: {Variant}", variant.Key);
                    continue;
                }
                
                var variantName = variant.Key.ToLowerInvariant(); // thumbnail, web, raw
                var fileName = $"cover-{variantName}{extension}";
                var filePath = Path.Combine(coverBasePath, fileName);
                
                try
                {
                    // Atomic write using temp file
                    var tempPath = $"{filePath}.tmp";
                    await File.WriteAllBytesAsync(tempPath, variant.Value);
                    File.Move(tempPath, filePath, overwrite: true);
                    
                    savedCount++;
                    _logger.LogDebug(
                        "Saved {Variant} cover variant for series {SeriesId} (lang: {Language}): {FileName} ({Size} bytes)", 
                        variant.Key, seriesId, language ?? "default", fileName, variant.Value.Length);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to save variant {Variant} for series {SeriesId}", variant.Key, seriesId);
                    throw;
                }
            }
            
            _logger.LogInformation(
                "Saved {SavedCount}/{TotalCount} cover variants for series {SeriesId} (language: {Language})",
                savedCount, variants.Count, seriesId, language ?? "default");

            // Return relative URL to the WEB variant (default)
            var id = seriesId.StartsWith(SeriesUrnPrefix, StringComparison.Ordinal) 
                ? seriesId[SeriesUrnPrefix.Length..] 
                : seriesId;
            
            if (!string.IsNullOrEmpty(language))
            {
                return $"/api/v1/series/{id}/cover?variant=web&lang={language}";
            }
            return $"/api/v1/series/{id}/cover?variant=web";
        }
        catch (Exception ex) when (ex is not ArgumentException)
        {
            _logger.LogError(ex, "Failed to save cover variants for series {SeriesId}", seriesId);
            throw;
        }
    }

    /// <summary>
    /// Get the cover image path for a series.
    /// </summary>
    /// <param name="seriesId">Series ID (URN or UUID).</param>
    /// <param name="variant">Variant name (thumbnail, web, raw). Default is web.</param>
    /// <param name="language">Optional language code for localized covers.</param>
    /// <returns>Absolute path to cover image file, or null if not found.</returns>
    public string? GetCoverImagePath(string seriesId, string variant = "web", string? language = null)
    {
        if (string.IsNullOrWhiteSpace(seriesId))
        {
            _logger.LogWarning("GetCoverImagePath called with null or empty seriesId");
            return null;
        }
        
        try
        {
            var seriesPath = GetSeriesPath(seriesId);
            if (!Directory.Exists(seriesPath))
            {
                _logger.LogDebug("Series directory not found for {SeriesId}: {Path}", seriesId, seriesPath);
                return null;
            }
            
            string coverBasePath;
            
            if (!string.IsNullOrEmpty(language))
            {
                // Look for localized cover: data/series/{id}/lang/{lang-code}/
                coverBasePath = Path.Combine(seriesPath, LanguageSubdirectory, language);
                if (!Directory.Exists(coverBasePath))
                {
                    // Fall back to default language if localized cover doesn't exist
                    _logger.LogDebug(
                        "Localized cover not found for {SeriesId} (lang: {Language}), falling back to default", 
                        seriesId, language);
                    coverBasePath = seriesPath;
                }
            }
            else
            {
                // Default cover: data/series/{id}/
                coverBasePath = seriesPath;
            }
            
            // Try to find variant-specific cover (e.g., cover-web.jpg, cover-thumbnail.jpg, cover-raw.jpg)
            var variantPattern = $"cover-{variant.ToLowerInvariant()}.*";
            if (Directory.Exists(coverBasePath))
            {
                var variantFiles = Directory.GetFiles(coverBasePath, variantPattern)
                    .Where(f => IsImageFile(f))
                    .ToArray();
                    
                if (variantFiles.Length > 0)
                {
                    var selectedFile = variantFiles.First();
                    _logger.LogDebug(
                        "Found {Variant} variant for series {SeriesId} (lang: {Language}): {Path}",
                        variant, seriesId, language ?? "default", selectedFile);
                    return selectedFile;
                }
            }
            
            // Fall back to legacy cover.* pattern (for backwards compatibility) in default location
            if (Directory.Exists(seriesPath))
            {
                var coverFiles = Directory.GetFiles(seriesPath, "cover.*")
                    .Where(f => IsImageFile(f))
                    .ToArray();
                    
                if (coverFiles.Length > 0)
                {
                    var selectedFile = coverFiles.First();
                    _logger.LogDebug(
                        "Found legacy cover for series {SeriesId}: {Path}",
                        seriesId, selectedFile);
                    return selectedFile;
                }
            }
            
            _logger.LogDebug(
                "No cover image found for series {SeriesId} (variant: {Variant}, language: {Language})",
                seriesId, variant, language ?? "default");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving cover path for series {SeriesId}", seriesId);
            return null;
        }
    }    /// <summary>
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
    /// <param name="unit">The unit object to save.</param>
    /// <exception cref="ArgumentNullException">Thrown when unit is null.</exception>
    /// <exception cref="ArgumentException">Thrown when unit has invalid data.</exception>
    public async Task SaveUnitAsync(Unit unit)
    {
        ArgumentNullException.ThrowIfNull(unit, nameof(unit));
        
        if (string.IsNullOrWhiteSpace(unit.id))
        {
            throw new ArgumentException("Unit ID cannot be null or empty", nameof(unit));
        }
        
        if (string.IsNullOrWhiteSpace(unit.series_id))
        {
            throw new ArgumentException("Unit series_id cannot be null or empty", nameof(unit));
        }
        
        _logger.LogDebug(
            "Saving unit {UnitId} (number: {UnitNumber}) for series {SeriesId}",
            unit.id, unit.unit_number, unit.series_id);
        
        try
        {
            var unitPath = GetUnitPath(unit.series_id, unit.unit_number);
            
            if (!Directory.Exists(unitPath))
            {
                Directory.CreateDirectory(unitPath);
                _logger.LogDebug("Created directory for unit {UnitId}: {Path}", unit.id, unitPath);
            }

            var metadataPath = Path.Combine(unitPath, MetadataFileName);
            var json = JsonSerializer.Serialize(unit, _jsonOptions);
            
            // Atomic write using temp file
            var tempPath = $"{metadataPath}.tmp";
            await File.WriteAllTextAsync(tempPath, json);
            File.Move(tempPath, metadataPath, overwrite: true);

            var isNew = !_unitCache.ContainsKey(unit.id);
            _unitCache.AddOrUpdate(unit.id, unit, (_, _) => unit);
            
            _logger.LogInformation(
                "{Action} unit {UnitId} (number: {UnitNumber}) for series {SeriesId}",
                isNew ? "Added" : "Updated", unit.id, unit.unit_number, unit.series_id);
            
            // Update taxonomy caches from unit metadata with null safety
            var taxonomyUpdateCount = 0;
            
            if (unit.authors != null)
            {
                foreach (var author in unit.authors)
                {
                    if (author != null && !string.IsNullOrWhiteSpace(author.name))
                    {
                        if (_authorsByName.TryAdd(author.name, author))
                        {
                            taxonomyUpdateCount++;
                        }
                    }
                }
            }
            
            // Add scanlators from localized metadata
            if (unit.localized != null)
            {
                foreach (var (langCode, langData) in unit.localized)
                {
                    if (langData?.scanlators != null)
                    {
                        foreach (var scanlator in langData.scanlators)
                        {
                            if (scanlator != null && !string.IsNullOrWhiteSpace(scanlator.name))
                            {
                                if (_scanlatorsByName.TryAdd(scanlator.name, scanlator))
                                {
                                    taxonomyUpdateCount++;
                                }
                            }
                        }
                    }
                }
            }
            
            if (taxonomyUpdateCount > 0)
            {
                _logger.LogDebug("Updated {Count} taxonomy entries for unit {UnitId}", taxonomyUpdateCount, unit.id);
            }
            
            _logger.LogInformation("Successfully saved unit {UnitId} to {Path}", unit.id, metadataPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save unit {UnitId}", unit.id);
            throw;
        }
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
    /// Note: Metadata aggregation is handled by the repository layer (DynamicRepository).
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

    /// <summary>
    /// Get all available cover language variants for a series.
    /// </summary>
    public List<CoverInfo> GetAllAvailableCovers(string seriesId)
    {
        var seriesPath = GetSeriesPath(seriesId);
        var covers = new List<CoverInfo>();

        // Add default/original cover
        var defaultVariants = GetVariantsFromFiles(seriesPath, "cover");
        if (defaultVariants.Any())
        {
            covers.Add(new CoverInfo(null, "Original", defaultVariants));
        }

        // Check for lang subdirectory
        var langPath = Path.Combine(seriesPath, "lang");
        if (!Directory.Exists(langPath))
            return covers;

        // Get all language subdirectories
        var langDirs = Directory.GetDirectories(langPath);
        foreach (var langDir in langDirs)
        {
            var langCode = Path.GetFileName(langDir);
            var variants = GetVariantsFromFiles(langDir, "cover");
            
            if (variants.Any())
            {
                var languageName = GetLanguageName(langCode);
                covers.Add(new CoverInfo(langCode, languageName, variants));
            }
        }

        return covers;
    }

    /// <summary>
    /// Get available image variants (thumbnail, web, raw) for a cover.
    /// </summary>
    private string[] GetVariantsFromFiles(string directoryPath, string baseName)
    {
        var variants = new List<string>();
        
        if (!Directory.Exists(directoryPath))
            return variants.ToArray();

        var searchPattern = $"{baseName}-*.{'{'}jpg,jpeg,png,webp,gif{'}'}";
        var files = Directory.GetFiles(directoryPath, $"{baseName}-*.*")
            .Where(f => IsImageFile(f))
            .ToList();

        // Check for each variant type
        var variantTypes = new[] { "thumbnail", "web", "raw" };
        foreach (var variant in variantTypes)
        {
            if (files.Any(f => Path.GetFileNameWithoutExtension(f).EndsWith($"-{variant}", StringComparison.OrdinalIgnoreCase)))
            {
                variants.Add(variant);
            }
        }

        return variants.ToArray();
    }

    /// <summary>
    /// Get display name for a language code.
    /// </summary>
    private string GetLanguageName(string langCode)
    {
        return langCode?.ToLowerInvariant() switch
        {
            "en" => "English",
            "ja" => "Japanese (日本語)",
            "zh" or "zh-cn" => "Chinese Simplified (简体中文)",
            "zh-tw" => "Chinese Traditional (繁體中文)",
            "ko" => "Korean (한국어)",
            "fr" => "French (Français)",
            "de" => "German (Deutsch)",
            "es" => "Spanish (Español)",
            "it" => "Italian (Italiano)",
            "pt" or "pt-br" => "Portuguese (Português)",
            "ru" => "Russian (Русский)",
            "ar" => "Arabic (العربية)",
            "th" => "Thai (ไทย)",
            "vi" => "Vietnamese (Tiếng Việt)",
            "id" => "Indonesian (Bahasa Indonesia)",
            _ => langCode?.ToUpperInvariant() ?? "Unknown"
        };
    }
    
    #endregion
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
