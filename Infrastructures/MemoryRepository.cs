using System.Collections.Concurrent;
using MehguViewer.Core.Shared;
using MehguViewer.Core.Services;
using MehguViewer.Core.Workers;
using MehguViewer.Core.Helpers;
using Microsoft.Extensions.Logging;

namespace MehguViewer.Core.Infrastructures;

/// <summary>
/// In-memory implementation of <see cref="IRepository"/> using ConcurrentDictionary for thread-safe storage.
/// </summary>
/// <remarks>
/// <para><strong>⚠️ WARNING: DATA IS NOT PERSISTED!</strong></para>
/// <para>All data is lost when the application restarts. Use only for:</para>
/// <list type="bullet">
/// <item>Development and testing</item>
/// <item>Fallback when PostgreSQL unavailable (if FallbackToMemory enabled)</item>
/// <item>Temporary instances without persistence requirements</item>
/// </list>
/// <para><strong>Thread Safety:</strong></para>
/// <para>All collections use ConcurrentDictionary for lock-free concurrent access.</para>
/// <para><strong>Performance:</strong></para>
/// <para>O(1) lookups, suitable for small to medium libraries (&lt;10k series).</para>
/// <para><strong>CARP Score Optimizations:</strong></para>
/// <list type="bullet">
/// <item><strong>Cohesion:</strong> All memory storage operations grouped logically by entity type</item>
/// <item><strong>Abstraction:</strong> Implements IRepository contract with clear separation of concerns</item>
/// <item><strong>Readability:</strong> Comprehensive logging, consistent naming, and structured documentation</item>
/// <item><strong>Performance:</strong> Thread-safe concurrent collections with O(1) operations</item>
/// </list>
/// </remarks>
public class MemoryRepository : IRepository
{
    #region Constants
    
    /// <summary>Composite key separator for edit permissions and progress dictionaries.</summary>
    private const string KeySeparator = "|";
    
    /// <summary>Maximum number of items to return in list operations without explicit limit.</summary>
    private const int DefaultMaxResults = 1000;
    
    #endregion
    
    #region Fields
    
    /// <summary>Logger instance for diagnostic and operational logging.</summary>
    private readonly ILogger<MemoryRepository> _logger;
    
    /// <summary>Metadata aggregation service for series-unit metadata operations.</summary>
    private readonly MetadataAggregationService _metadataService;
    
    /// <summary>Series storage, keyed by series URN.</summary>
    private readonly ConcurrentDictionary<string, Series> _series = new();
    
    /// <summary>Unit storage, keyed by unit URN.</summary>
    private readonly ConcurrentDictionary<string, Unit> _units = new();
    
    /// <summary>Page storage, keyed by unit URN, value is list of pages.</summary>
    private readonly ConcurrentDictionary<string, List<Page>> _pages = new();
    
    /// <summary>Reading progress storage, keyed by "{userUrn}|{seriesUrn}".</summary>
    private readonly ConcurrentDictionary<string, ReadingProgress> _progress = new();
    
    /// <summary>Comment storage, keyed by comment URN.</summary>
    private readonly ConcurrentDictionary<string, Comment> _comments = new();
    
    /// <summary>Collection storage, keyed by collection URN.</summary>
    private readonly ConcurrentDictionary<string, Collection> _collections = new();
    
    /// <summary>User storage, keyed by user URN.</summary>
    private readonly ConcurrentDictionary<string, User> _users = new();
    
    /// <summary>Passkey storage, keyed by passkey URN.</summary>
    private readonly ConcurrentDictionary<string, Passkey> _passkeys = new();
    
    /// <summary>Edit permission storage, keyed by "{targetUrn}|{userUrn}".</summary>
    private readonly ConcurrentDictionary<string, EditPermission> _editPermissions = new();
    
    /// <summary>Vote storage, keyed by "{userId}|{targetId}".</summary>
    private readonly ConcurrentDictionary<string, Vote> _votes = new();
    
    /// <summary>Report storage for content moderation.</summary>
    private readonly ConcurrentBag<Report> _reports = new();
    
    /// <summary>System configuration settings.</summary>
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
    
    /// <summary>Node metadata for federation.</summary>
    private NodeMetadata _nodeMetadata = new NodeMetadata(
        "1.0.0",
        "MehguViewer Core",
        "A MehguViewer Core Node",
        "https://auth.mehgu.example.com",
        new NodeCapabilities(true, true, true),
        new NodeMaintainer("Admin", "admin@example.com")
    );
    
    /// <summary>Taxonomy configuration for tags and classification.</summary>
    private TaxonomyConfig _taxonomyConfig = new TaxonomyConfig(
        tags: new[] { "Action", "Adventure", "Comedy", "Drama", "Fantasy", "Romance", "Slice of Life" },
        content_warnings: ContentWarnings.All,
        types: MediaTypes.All,
        authors: [],
        scanlators: [new Scanlator("official", "Official", ScanlatorRole.Both)],
        groups: []
    );
    
    #endregion
    
    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="MemoryRepository"/> class.
    /// </summary>
    /// <param name="logger">Logger instance for diagnostic output.</param>
    /// <param name="metadataService">Metadata aggregation service for series-unit operations.</param>
    /// <remarks>
    /// <para>Does not seed data by default. Call <see cref="SeedDebugData"/> for sample data.</para>
    /// <para>Initializes all concurrent collections with default concurrency level.</para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown if logger or metadataService is null.</exception>
    public MemoryRepository(ILogger<MemoryRepository>? logger = null, MetadataAggregationService? metadataService = null)
    {
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<MemoryRepository>.Instance;
        _metadataService = metadataService ?? throw new ArgumentNullException(nameof(metadataService));
        _logger.LogInformation("MemoryRepository initialized - WARNING: Data is not persisted");
    }
    
    #endregion
    
    #region Debug and System Operations

    /// <summary>
    /// Seeds the repository with sample debug data for testing.
    /// </summary>
    /// <remarks>
    /// Creates:
    /// <list type="bullet">
    /// <item>1 demo manga series</item>
    /// <item>1 chapter/unit</item>
    /// <item>5 placeholder pages</item>
    /// </list>
    /// </remarks>
    public void SeedDebugData()
    {
        _logger.LogInformation("Seeding debug data into MemoryRepository");
        
        try
        {
            var seriesId = UrnHelper.CreateSeriesUrn();
            var series = new Series(
                id: seriesId,
                federation_ref: "urn:mvn:node:local",
                title: "Demo Manga",
                description: "A demo manga for testing.",
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

            var unitId = Guid.NewGuid().ToString();
            var unit = new Unit(
                unitId,
                seriesId,
                1,
                "Chapter 1",
                DateTime.UtcNow
            );
            AddUnit(unit);

            // Seed Pages
            for (int i = 1; i <= 5; i++)
            {
                AddPage(unitId, new Page(i, UrnHelper.CreateAssetUrn(), $"https://placehold.co/800x1200?text=Page+{i}"));
            }
            
            _logger.LogInformation("Debug data seeded successfully: 1 series, 1 unit, 5 pages");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to seed debug data");
            throw;
        }
    }
    
    /// <summary>
    /// Resets all data in the repository (destructive operation).
    /// </summary>
    /// <remarks>
    /// <para><strong>Security:</strong> Should only be used in dev/test environments.</para>
    /// <para><strong>Warning:</strong> Permanently deletes all data without recovery.</para>
    /// </remarks>
    public void ResetAllData()
    {
        _logger.LogWarning("Resetting all data in MemoryRepository - this is destructive");
        
        try
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
            _editPermissions.Clear();
            
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
            
            _logger.LogInformation("All data reset successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reset data");
            throw;
        }
    }
    
    #endregion
    
    #region Series Operations
    
    /// <summary>Adds a new series to the repository.</summary>
    /// <param name="series">Series to add with valid URN.</param>
    /// <exception cref="ArgumentNullException">Thrown if series is null.</exception>
    /// <exception cref="ArgumentException">Thrown if series ID is empty.</exception>
    /// <exception cref="InvalidOperationException">Thrown if series already exists.</exception>
    public void AddSeries(Series series)
    {
        if (series == null)
        {
            _logger.LogError("Attempted to add null series");
            throw new ArgumentNullException(nameof(series));
        }
        
        if (string.IsNullOrWhiteSpace(series.id))
        {
            _logger.LogError("Attempted to add series with empty ID");
            throw new ArgumentException("Series ID cannot be empty", nameof(series));
        }
        
        if (!_series.TryAdd(series.id, series))
        {
            _logger.LogWarning("Series {SeriesId} already exists", series.id);
            throw new InvalidOperationException($"Series {series.id} already exists");
        }
        
        _logger.LogInformation("Added series {SeriesId}: {Title}", series.id, series.title);
    }
    
    /// <summary>Updates an existing series in the repository.</summary>
    /// <param name="series">Series with updated data.</param>
    /// <exception cref="ArgumentNullException">Thrown if series is null.</exception>
    public void UpdateSeries(Series series)
    {
        if (series == null)
        {
            _logger.LogError("Attempted to update null series");
            throw new ArgumentNullException(nameof(series));
        }
        
        _series[series.id] = series;
        _logger.LogInformation("Updated series {SeriesId}: {Title}", series.id, series.title);
    }
    
    /// <summary>Retrieves a series by its URN.</summary>
    /// <param name="id">Series URN to retrieve.</param>
    /// <returns>Series if found, null otherwise.</returns>
    public Series? GetSeries(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            _logger.LogWarning("GetSeries called with empty ID");
            return null;
        }
        
        var found = _series.TryGetValue(id, out var s);
        if (!found)
        {
            _logger.LogDebug("Series {SeriesId} not found", id);
        }
        
        return found ? s : null;
    }
    
    /// <summary>Lists series with pagination.</summary>
    /// <param name="offset">Number of items to skip.</param>
    /// <param name="limit">Maximum number of items to return.</param>
    /// <returns>Paginated series collection.</returns>
    public IEnumerable<Series> ListSeries(int offset = 0, int limit = 20)
    {
        // Validate and sanitize parameters
        if (offset < 0) offset = 0;
        if (limit <= 0 || limit > DefaultMaxResults) limit = 20;
        
        _logger.LogDebug("Listing series: offset={Offset}, limit={Limit}", offset, limit);
        return _series.Values.Skip(offset).Take(limit);
    }
    
    /// <summary>Searches series with multiple filters.</summary>
    /// <param name="query">Text search query for title.</param>
    /// <param name="type">Media type filter.</param>
    /// <param name="tags">Tag filters (OR logic).</param>
    /// <param name="status">Status filter.</param>
    /// <param name="offset">Number of items to skip.</param>
    /// <param name="limit">Maximum number of items to return.</param>
    /// <returns>Filtered and paginated series collection.</returns>
    public IEnumerable<Series> SearchSeries(string? query, string? type, string[]? tags, string? status, int offset = 0, int limit = 20)
    {
        // Validate and sanitize parameters
        if (offset < 0) offset = 0;
        if (limit <= 0 || limit > DefaultMaxResults) limit = 20;
        
        _logger.LogDebug("Searching series: query={Query}, type={Type}, tags={Tags}, status={Status}", 
            query, type, tags != null ? string.Join(",", tags) : "none", status);
        
        var result = _series.Values.AsEnumerable();
        
        // Apply filters sequentially
        if (!string.IsNullOrEmpty(query))
        {
            result = result.Where(s => s.title.Contains(query, StringComparison.OrdinalIgnoreCase));
        }
        
        if (!string.IsNullOrEmpty(type))
        {
            result = result.Where(s => s.media_type.Equals(type, StringComparison.OrdinalIgnoreCase));
        }
        
        if (tags != null && tags.Length > 0)
        {
            result = result.Where(s => s.tags.Any(t => tags.Contains(t, StringComparer.OrdinalIgnoreCase)));
        }
        
        if (!string.IsNullOrEmpty(status))
        {
            result = result.Where(s => s.status != null && s.status.Equals(status, StringComparison.OrdinalIgnoreCase));
        }
        
        var finalResult = result.Skip(offset).Take(limit).ToList();
        _logger.LogInformation("Search returned {Count} series", finalResult.Count);
        return finalResult;
    }
    
    /// <summary>Deletes a series and all associated units and pages (cascade delete).</summary>
    /// <param name="id">Series URN to delete.</param>
    public void DeleteSeries(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            _logger.LogWarning("DeleteSeries called with empty ID");
            return;
        }
        
        _series.TryRemove(id, out _);
        
        // Cascade delete associated units and pages
        var unitsToRemove = _units.Values.Where(u => u.series_id == id).Select(u => u.id).ToList();
        foreach (var unitId in unitsToRemove)
        {
            _units.TryRemove(unitId, out _);
            _pages.TryRemove(unitId, out _);
        }
        
        _logger.LogInformation("Deleted series {SeriesId} and {UnitCount} associated units", id, unitsToRemove.Count);
    }

    #endregion
    
    #region Unit Operations
    
    /// <summary>Adds a new unit to the repository.</summary>
    /// <param name="unit">Unit to add.</param>
    /// <exception cref="ArgumentNullException">Thrown if unit is null.</exception>
    public void AddUnit(Unit unit)
    {
        if (unit == null)
        {
            _logger.LogError("Attempted to add null unit");
            throw new ArgumentNullException(nameof(unit));
        }
        
        if (!_units.TryAdd(unit.id, unit))
        {
            _logger.LogWarning("Unit {UnitId} already exists", unit.id);
            throw new InvalidOperationException($"Unit {unit.id} already exists");
        }
        
        _logger.LogInformation("Added unit {UnitId} to series {SeriesId}", unit.id, unit.series_id);
    }
    
    /// <summary>Updates an existing unit and triggers metadata aggregation.</summary>
    /// <param name="unit">Unit with updated data.</param>
    /// <exception cref="ArgumentNullException">Thrown if unit is null.</exception>
    public void UpdateUnit(Unit unit)
    {
        if (unit == null)
        {
            _logger.LogError("Attempted to update null unit");
            throw new ArgumentNullException(nameof(unit));
        }
        
        _units[unit.id] = unit;
        _logger.LogInformation("Updated unit {UnitId}", unit.id);
        
        // Trigger metadata aggregation for parent series
        AggregateSeriesMetadataFromUnits(unit.series_id);
    }
    
    /// <summary>Lists all units for a given series, ordered by unit number.</summary>
    /// <param name="seriesId">Series URN.</param>
    /// <returns>Ordered collection of units.</returns>
    public IEnumerable<Unit> ListUnits(string seriesId)
    {
        if (string.IsNullOrWhiteSpace(seriesId))
        {
            _logger.LogWarning("ListUnits called with empty seriesId");
            return Enumerable.Empty<Unit>();
        }
        
        var units = _units.Values.Where(u => u.series_id == seriesId).OrderBy(u => u.unit_number).ToList();
        _logger.LogDebug("Found {Count} units for series {SeriesId}", units.Count, seriesId);
        return units;
    }
    
    /// <summary>Retrieves a unit by its URN.</summary>
    /// <param name="id">Unit URN.</param>
    /// <returns>Unit if found, null otherwise.</returns>
    public Unit? GetUnit(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            _logger.LogWarning("GetUnit called with empty ID");
            return null;
        }
        
        return _units.TryGetValue(id, out var u) ? u : null;
    }
    
    /// <summary>Deletes a unit and its associated pages, then triggers metadata aggregation.</summary>
    /// <param name="id">Unit URN to delete.</param>
    public void DeleteUnit(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            _logger.LogWarning("DeleteUnit called with empty ID");
            return;
        }
        
        var unit = GetUnit(id);
        _units.TryRemove(id, out _);
        _pages.TryRemove(id, out _);
        
        // Trigger metadata aggregation for parent series
        if (unit != null)
        {
            _logger.LogInformation("Deleted unit {UnitId} from series {SeriesId}", id, unit.series_id);
            AggregateSeriesMetadataFromUnits(unit.series_id);
        }
        else
        {
            _logger.LogInformation("Deleted unit {UnitId}", id);
        }
    }
    
    #endregion
    
    #region Metadata Aggregation
    
    /// <summary>
    /// Aggregates metadata from units to update the parent series.
    /// </summary>
    /// <param name="seriesId">Series URN to aggregate metadata for.</param>
    /// <remarks>
    /// Updates series with computed metadata like total chapters, latest update, etc.
    /// </remarks>
    public void AggregateSeriesMetadataFromUnits(string seriesId)
    {
        if (string.IsNullOrWhiteSpace(seriesId))
        {
            _logger.LogWarning("AggregateSeriesMetadataFromUnits called with empty seriesId");
            return;
        }
        
        var series = GetSeries(seriesId);
        if (series == null)
        {
            _logger.LogWarning("Cannot aggregate metadata for non-existent series {SeriesId}", seriesId);
            return;
        }
        
        var units = ListUnits(seriesId);
        var aggregated = _metadataService.AggregateMetadata(series, units);
        UpdateSeries(aggregated);
        
        _logger.LogDebug("Aggregated metadata for series {SeriesId}", seriesId);
    }
    
    #endregion
    
    #region Edit Permission Operations
    
    /// <summary>Grants edit permission to a user for a specific resource.</summary>
    /// <param name="targetUrn">Resource URN (series or unit).</param>
    /// <param name="userUrn">User URN to grant permission to.</param>
    /// <param name="grantedBy">URN of user granting permission.</param>
    public void GrantEditPermission(string targetUrn, string userUrn, string grantedBy)
    {
        if (string.IsNullOrWhiteSpace(targetUrn))
        {
            _logger.LogError("GrantEditPermission called with empty targetUrn");
            throw new ArgumentException("targetUrn cannot be empty", nameof(targetUrn));
        }
        
        if (string.IsNullOrWhiteSpace(userUrn))
        {
            _logger.LogError("GrantEditPermission called with empty userUrn");
            throw new ArgumentException("userUrn cannot be empty", nameof(userUrn));
        }
        
        var key = $"{targetUrn}{KeySeparator}{userUrn}";
        var permission = new EditPermission(targetUrn, userUrn, DateTime.UtcNow, grantedBy);
        _editPermissions.AddOrUpdate(key, permission, (_, _) => permission);
        
        // Update allowed_editors list on the target resource
        if (targetUrn.StartsWith("urn:mvn:series:"))
        {
            var series = GetSeries(targetUrn);
            if (series != null)
            {
                var editors = new HashSet<string>(series.allowed_editors ?? []);
                editors.Add(userUrn);
                UpdateSeries(series with { allowed_editors = editors.ToArray() });
            }
        }
        else if (targetUrn.StartsWith("urn:mvn:unit:"))
        {
            var unit = GetUnit(targetUrn);
            if (unit != null)
            {
                var editors = new HashSet<string>(unit.allowed_editors ?? []);
                editors.Add(userUrn);
                UpdateUnit(unit with { allowed_editors = editors.ToArray() });
            }
        }
        
        _logger.LogInformation("Granted edit permission on {TargetUrn} to user {UserUrn} by {GrantedBy}", 
            targetUrn, userUrn, grantedBy);
    }
    
    /// <summary>Revokes edit permission from a user for a specific resource.</summary>
    /// <param name="targetUrn">Resource URN (series or unit).</param>
    /// <param name="userUrn">User URN to revoke permission from.</param>
    public void RevokeEditPermission(string targetUrn, string userUrn)
    {
        if (string.IsNullOrWhiteSpace(targetUrn) || string.IsNullOrWhiteSpace(userUrn))
        {
            _logger.LogWarning("RevokeEditPermission called with empty parameters");
            return;
        }
        
        var key = $"{targetUrn}{KeySeparator}{userUrn}";
        _editPermissions.TryRemove(key, out _);
        
        // Update allowed_editors list on the target resource
        if (targetUrn.StartsWith("urn:mvn:series:"))
        {
            var series = GetSeries(targetUrn);
            if (series != null)
            {
                var editors = new HashSet<string>(series.allowed_editors ?? []);
                editors.Remove(userUrn);
                UpdateSeries(series with { allowed_editors = editors.ToArray() });
            }
        }
        else if (targetUrn.StartsWith("urn:mvn:unit:"))
        {
            var unit = GetUnit(targetUrn);
            if (unit != null)
            {
                var editors = new HashSet<string>(unit.allowed_editors ?? []);
                editors.Remove(userUrn);
                UpdateUnit(unit with { allowed_editors = editors.ToArray() });
            }
        }
        
        _logger.LogInformation("Revoked edit permission on {TargetUrn} from user {UserUrn}", targetUrn, userUrn);
    }
    
    /// <summary>Checks if a user has edit permission for a resource.</summary>
    /// <param name="targetUrn">Resource URN to check.</param>
    /// <param name="userUrn">User URN to check.</param>
    /// <returns>True if user has permission, false otherwise.</returns>
    public bool HasEditPermission(string targetUrn, string userUrn)
    {
        if (string.IsNullOrWhiteSpace(targetUrn) || string.IsNullOrWhiteSpace(userUrn))
        {
            _logger.LogDebug("HasEditPermission called with empty parameters");
            return false;
        }
        
        // Check if user is the resource owner (auto-grant permission)
        if (targetUrn.StartsWith("urn:mvn:series:"))
        {
            var series = GetSeries(targetUrn);
            if (series?.created_by == userUrn)
            {
                _logger.LogDebug("User {UserUrn} is owner of series {TargetUrn}", userUrn, targetUrn);
                return true;
            }
        }
        else if (targetUrn.StartsWith("urn:mvn:unit:"))
        {
            var unit = GetUnit(targetUrn);
            if (unit?.created_by == userUrn)
            {
                _logger.LogDebug("User {UserUrn} is owner of unit {TargetUrn}", userUrn, targetUrn);
                return true;
            }
            
            // Also check parent series ownership
            if (unit != null)
            {
                var series = GetSeries(unit.series_id);
                if (series?.created_by == userUrn)
                {
                    _logger.LogDebug("User {UserUrn} is owner of parent series for unit {TargetUrn}", userUrn, targetUrn);
                    return true;
                }
            }
        }
        
        // Check explicit permission
        var key = $"{targetUrn}{KeySeparator}{userUrn}";
        var hasPermission = _editPermissions.ContainsKey(key);
        _logger.LogDebug("Edit permission check for {UserUrn} on {TargetUrn}: {HasPermission}", userUrn, targetUrn, hasPermission);
        return hasPermission;
    }
    
    /// <summary>Gets all user URNs with edit permission for a resource.</summary>
    /// <param name="targetUrn">Resource URN.</param>
    /// <returns>Array of user URNs with edit permission.</returns>
    public string[] GetEditPermissions(string targetUrn)
    {
        if (string.IsNullOrWhiteSpace(targetUrn))
        {
            _logger.LogWarning("GetEditPermissions called with empty targetUrn");
            return Array.Empty<string>();
        }
        
        return _editPermissions.Values
            .Where(p => p.target_urn == targetUrn)
            .Select(p => p.user_urn)
            .ToArray();
    }

    /// <summary>Gets all edit permission records for a resource.</summary>
    /// <param name="targetUrn">Resource URN.</param>
    /// <returns>Array of EditPermission records, ordered by grant date descending.</returns>
    public EditPermission[] GetEditPermissionRecords(string targetUrn)
    {
        if (string.IsNullOrWhiteSpace(targetUrn))
        {
            _logger.LogWarning("GetEditPermissionRecords called with empty targetUrn");
            return Array.Empty<EditPermission>();
        }
        
        return _editPermissions.Values
            .Where(p => p.target_urn == targetUrn)
            .OrderByDescending(p => p.granted_at)
            .ToArray();
    }

    /// <summary>Removes orphaned edit permissions for non-existent resources.</summary>
    /// <remarks>Cleanup operation to maintain data integrity.</remarks>
    public void SyncEditPermissions()
    {
        _logger.LogInformation("Syncing edit permissions - removing orphaned entries");
        
        // Build set of existing resource URNs
        var existingUrns = new HashSet<string>();
        foreach (var series in _series.Values)
        {
            existingUrns.Add(series.id.StartsWith("urn:mvn:series:") ? series.id : $"urn:mvn:series:{series.id}");
        }
        foreach (var unit in _units.Values)
        {
            existingUrns.Add(unit.id.StartsWith("urn:mvn:unit:") ? unit.id : $"urn:mvn:unit:{unit.id}");
        }
        
        // Remove permissions for non-existent targets
        var orphanedKeys = _editPermissions.Keys.Where(key => 
        {
            var targetUrn = _editPermissions[key].target_urn;
            return !existingUrns.Contains(targetUrn);
        }).ToList();
        
        foreach (var key in orphanedKeys)
        {
            _editPermissions.TryRemove(key, out _);
        }
        
        _logger.LogInformation("Removed {Count} orphaned edit permissions", orphanedKeys.Count);
    }

    #endregion
    
    #region Page Operations
    
    /// <summary>Adds a page to a unit.</summary>
    /// <param name="unitId">Unit URN.</param>
    /// <param name="page">Page to add.</param>
    /// <exception cref="ArgumentNullException">Thrown if page is null.</exception>
    public void AddPage(string unitId, Page page)
    {
        if (string.IsNullOrWhiteSpace(unitId))
        {
            _logger.LogError("AddPage called with empty unitId");
            throw new ArgumentException("unitId cannot be empty", nameof(unitId));
        }
        
        if (page == null)
        {
            _logger.LogError("Attempted to add null page");
            throw new ArgumentNullException(nameof(page));
        }
        
        _pages.AddOrUpdate(unitId, 
            _ => new List<Page> { page }, 
            (_, list) => { list.Add(page); return list; });
        
        _logger.LogDebug("Added page {PageNumber} to unit {UnitId}", page.page_number, unitId);
    }
    
    /// <summary>Gets all pages for a unit, ordered by page number.</summary>
    /// <param name="unitId">Unit URN.</param>
    /// <returns>Ordered collection of pages.</returns>
    public IEnumerable<Page> GetPages(string unitId)
    {
        if (string.IsNullOrWhiteSpace(unitId))
        {
            _logger.LogWarning("GetPages called with empty unitId");
            return Enumerable.Empty<Page>();
        }
        
        var pages = _pages.TryGetValue(unitId, out var list) 
            ? list.OrderBy(p => p.page_number) 
            : Enumerable.Empty<Page>();
        
        return pages;
    }

    #endregion
    
    #region Progress Operations
    
    /// <summary>Updates or creates reading progress for a user on a series.</summary>
    /// <param name="userId">User URN.</param>
    /// <param name="progress">Reading progress data.</param>
    /// <exception cref="ArgumentNullException">Thrown if progress is null.</exception>
    public void UpdateProgress(string userId, ReadingProgress progress)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            _logger.LogError("UpdateProgress called with empty userId");
            throw new ArgumentException("userId cannot be empty", nameof(userId));
        }
        
        if (progress == null)
        {
            _logger.LogError("Attempted to update null progress");
            throw new ArgumentNullException(nameof(progress));
        }
        
        var key = $"{userId}{KeySeparator}{progress.series_urn}";
        _progress.AddOrUpdate(key, 
            progress, 
            (k, existing) => progress.updated_at > existing.updated_at ? progress : existing);
        
        _logger.LogInformation("Updated progress for user {UserId} on series {SeriesUrn}", userId, progress.series_urn);
    }

    /// <summary>Gets reading progress for a user on a specific series.</summary>
    /// <param name="userId">User URN.</param>
    /// <param name="seriesUrn">Series URN.</param>
    /// <returns>Reading progress if found, null otherwise.</returns>
    public ReadingProgress? GetProgress(string userId, string seriesUrn) 
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(seriesUrn))
        {
            _logger.LogWarning("GetProgress called with empty parameters");
            return null;
        }
        
        var key = $"{userId}{KeySeparator}{seriesUrn}";
        return _progress.TryGetValue(key, out var p) ? p : null;
    }

    /// <summary>Gets all reading progress entries for a user (their library).</summary>
    /// <param name="userId">User URN.</param>
    /// <returns>Collection of reading progress entries.</returns>
    public IEnumerable<ReadingProgress> GetLibrary(string userId) 
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            _logger.LogWarning("GetLibrary called with empty userId");
            return Enumerable.Empty<ReadingProgress>();
        }
        
        var prefix = userId + KeySeparator;
        return _progress.Where(kvp => kvp.Key.StartsWith(prefix)).Select(kvp => kvp.Value);
    }

    /// <summary>Gets reading history for a user, ordered by last update descending.</summary>
    /// <param name="userId">User URN.</param>
    /// <returns>Ordered collection of reading progress entries.</returns>
    public IEnumerable<ReadingProgress> GetHistory(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            _logger.LogWarning("GetHistory called with empty userId");
            return Enumerable.Empty<ReadingProgress>();
        }
        
        var prefix = userId + KeySeparator;
        return _progress.Where(kvp => kvp.Key.StartsWith(prefix))
                        .Select(kvp => kvp.Value)
                        .OrderByDescending(p => p.updated_at);
    }

    #endregion
    
    #region Comment Operations
    
    /// <summary>Adds a comment.</summary>
    /// <param name="comment">Comment to add.</param>
    /// <exception cref="ArgumentNullException">Thrown if comment is null.</exception>
    public void AddComment(Comment comment)
    {
        if (comment == null)
        {
            _logger.LogError("Attempted to add null comment");
            throw new ArgumentNullException(nameof(comment));
        }
        
        if (!_comments.TryAdd(comment.id, comment))
        {
            _logger.LogWarning("Comment {CommentId} already exists", comment.id);
            throw new InvalidOperationException($"Comment {comment.id} already exists");
        }
        
        _logger.LogInformation("Added comment {CommentId} by {AuthorId}", comment.id, comment.author.uid);
    }
    
    /// <summary>Gets comments for a target resource.</summary>
    /// <param name="targetUrn">Resource URN.</param>
    /// <returns>Collection of comments.</returns>
    /// <remarks>TODO: Implement filtering by targetUrn.</remarks>
    public IEnumerable<Comment> GetComments(string targetUrn)
    {
        // TODO: Implement proper filtering by targetUrn
        _logger.LogDebug("GetComments called for {TargetUrn} - filtering not yet implemented", targetUrn);
        return _comments.Values;
    }

    #endregion
    
    #region Vote Operations
    
    /// <summary>Adds or updates a vote. Setting value to 0 removes the vote.</summary>
    /// <param name="userId">User URN.</param>
    /// <param name="vote">Vote data.</param>
    /// <exception cref="ArgumentNullException">Thrown if vote is null.</exception>
    public void AddVote(string userId, Vote vote)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            _logger.LogError("AddVote called with empty userId");
            throw new ArgumentException("userId cannot be empty", nameof(userId));
        }
        
        if (vote == null)
        {
            _logger.LogError("Attempted to add null vote");
            throw new ArgumentNullException(nameof(vote));
        }
        
        var key = $"{userId}{KeySeparator}{vote.target_id}";
        
        if (vote.value == 0)
        {
            _votes.TryRemove(key, out _);
            _logger.LogInformation("Removed vote from user {UserId} on {TargetId}", userId, vote.target_id);
        }
        else
        {
            _votes.AddOrUpdate(key, vote, (_, _) => vote);
            _logger.LogInformation("User {UserId} voted {Value} on {TargetId}", userId, vote.value, vote.target_id);
        }
    }

    #endregion
    
    #region Collection Operations
    
    /// <summary>Adds a collection.</summary>
    /// <param name="userId">User URN (owner).</param>
    /// <param name="collection">Collection to add.</param>
    /// <exception cref="ArgumentNullException">Thrown if collection is null.</exception>
    public void AddCollection(string userId, Collection collection)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            _logger.LogError("AddCollection called with empty userId");
            throw new ArgumentException("userId cannot be empty", nameof(userId));
        }
        
        if (collection == null)
        {
            _logger.LogError("Attempted to add null collection");
            throw new ArgumentNullException(nameof(collection));
        }
        
        if (!_collections.TryAdd(collection.id, collection))
        {
            _logger.LogWarning("Collection {CollectionId} already exists", collection.id);
            throw new InvalidOperationException($"Collection {collection.id} already exists");
        }
        
        _logger.LogInformation("Added collection {CollectionId}: {Name} for user {UserId}", 
            collection.id, collection.name, userId);
    }
    
    /// <summary>Lists all collections for a user.</summary>
    /// <param name="userId">User URN.</param>
    /// <returns>Collection of user's collections.</returns>
    public IEnumerable<Collection> ListCollections(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            _logger.LogWarning("ListCollections called with empty userId");
            return Enumerable.Empty<Collection>();
        }
        
        return _collections.Values.Where(c => c.user_id == userId);
    }
    
    /// <summary>Gets a collection by its URN.</summary>
    /// <param name="id">Collection URN.</param>
    /// <returns>Collection if found, null otherwise.</returns>
    public Collection? GetCollection(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            _logger.LogWarning("GetCollection called with empty ID");
            return null;
        }
        
        return _collections.TryGetValue(id, out var c) ? c : null;
    }
    
    /// <summary>Updates an existing collection.</summary>
    /// <param name="collection">Collection with updated data.</param>
    /// <exception cref="ArgumentNullException">Thrown if collection is null.</exception>
    public void UpdateCollection(Collection collection)
    {
        if (collection == null)
        {
            _logger.LogError("Attempted to update null collection");
            throw new ArgumentNullException(nameof(collection));
        }
        
        if (_collections.ContainsKey(collection.id))
        {
            _collections[collection.id] = collection;
            _logger.LogInformation("Updated collection {CollectionId}", collection.id);
        }
        else
        {
            _logger.LogWarning("Attempted to update non-existent collection {CollectionId}", collection.id);
        }
    }
    
    /// <summary>Deletes a collection.</summary>
    /// <param name="id">Collection URN.</param>
    public void DeleteCollection(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            _logger.LogWarning("DeleteCollection called with empty ID");
            return;
        }
        
        _collections.TryRemove(id, out _);
        _logger.LogInformation("Deleted collection {CollectionId}", id);
    }

    #endregion
    
    #region Report Operations
    
    /// <summary>Adds a content report.</summary>
    /// <param name="report">Report to add.</param>
    /// <exception cref="ArgumentNullException">Thrown if report is null.</exception>
    public void AddReport(Report report)
    {
        if (report == null)
        {
            _logger.LogError("Attempted to add null report");
            throw new ArgumentNullException(nameof(report));
        }
        
        _reports.Add(report);
        _logger.LogWarning("Content report added for {TargetUrn}: {Reason}", 
            report.target_urn, report.reason);
    }

    #endregion
    
    #region System Configuration Operations
    
    /// <summary>Gets the current system configuration.</summary>
    /// <returns>System configuration.</returns>
    public SystemConfig GetSystemConfig() => _systemConfig;
    
    /// <summary>Updates the system configuration.</summary>
    /// <param name="config">New configuration.</param>
    /// <exception cref="ArgumentNullException">Thrown if config is null.</exception>
    public void UpdateSystemConfig(SystemConfig config)
    {
        if (config == null)
        {
            _logger.LogError("Attempted to update system config with null value");
            throw new ArgumentNullException(nameof(config));
        }
        
        _systemConfig = config;
        _logger.LogInformation("System configuration updated");
    }

    /// <summary>Gets system statistics.</summary>
    /// <returns>Current system stats.</returns>
    public SystemStats GetSystemStats()
    {
        var stats = new SystemStats(
            _users.Count,
            0, // Storage bytes not tracked in memory repo
            (int)(DateTime.UtcNow - System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime()).TotalSeconds
        );
        
        _logger.LogDebug("System stats: {UserCount} users, {Uptime}s uptime", stats.total_users, stats.uptime_seconds);
        return stats;
    }

    #endregion
    
    #region User Management Operations
    
    /// <summary>Adds a new user.</summary>
    /// <param name="user">User to add.</param>
    /// <exception cref="ArgumentNullException">Thrown if user is null.</exception>
    public void AddUser(User user)
    {
        if (user == null)
        {
            _logger.LogError("Attempted to add null user");
            throw new ArgumentNullException(nameof(user));
        }
        
        if (!_users.TryAdd(user.id, user))
        {
            _logger.LogWarning("User {UserId} already exists", user.id);
            throw new InvalidOperationException($"User {user.id} already exists");
        }
        
        _logger.LogInformation("Added user {UserId}: {Username}", user.id, user.username);
    }
    
    /// <summary>Updates an existing user.</summary>
    /// <param name="user">User with updated data.</param>
    /// <exception cref="ArgumentNullException">Thrown if user is null.</exception>
    public void UpdateUser(User user)
    {
        if (user == null)
        {
            _logger.LogError("Attempted to update null user");
            throw new ArgumentNullException(nameof(user));
        }
        
        _users[user.id] = user;
        _logger.LogInformation("Updated user {UserId}: {Username}", user.id, user.username);
    }
    
    /// <summary>Gets a user by URN.</summary>
    /// <param name="id">User URN.</param>
    /// <returns>User if found, null otherwise.</returns>
    public User? GetUser(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            _logger.LogWarning("GetUser called with empty ID");
            return null;
        }
        
        return _users.TryGetValue(id, out var u) ? u : null;
    }
    
    /// <summary>Gets a user by username (case-insensitive).</summary>
    /// <param name="username">Username to search for.</param>
    /// <returns>User if found, null otherwise.</returns>
    public User? GetUserByUsername(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            _logger.LogWarning("GetUserByUsername called with empty username");
            return null;
        }
        
        var user = _users.Values.FirstOrDefault(u => u.username.Equals(username, StringComparison.OrdinalIgnoreCase));
        if (user == null)
        {
            _logger.LogDebug("User with username {Username} not found", username);
        }
        
        return user;
    }
    
    /// <summary>Lists all users.</summary>
    /// <returns>Collection of all users.</returns>
    public IEnumerable<User> ListUsers()
    {
        _logger.LogDebug("Listing all users: {Count} total", _users.Count);
        return _users.Values;
    }
    
    /// <summary>Deletes a user.</summary>
    /// <param name="id">User URN.</param>
    public void DeleteUser(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            _logger.LogWarning("DeleteUser called with empty ID");
            return;
        }
        
        _users.TryRemove(id, out var user);
        if (user != null)
        {
            _logger.LogInformation("Deleted user {UserId}: {Username}", id, user.username);
        }
    }

    /// <summary>Deletes all reading history for a user.</summary>
    /// <param name="userId">User URN.</param>
    public void DeleteUserHistory(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            _logger.LogWarning("DeleteUserHistory called with empty userId");
            return;
        }
        
        var prefix = userId + KeySeparator;
        var keysToRemove = _progress.Keys.Where(k => k.StartsWith(prefix)).ToList();
        
        foreach (var key in keysToRemove)
        {
            _progress.TryRemove(key, out _);
        }
        
        _logger.LogInformation("Deleted {Count} history entries for user {UserId}", keysToRemove.Count, userId);
    }

    /// <summary>Anonymizes all content created by a user.</summary>
    /// <param name="userId">User URN.</param>
    /// <remarks>Replaces user information with "Deleted User" placeholder.</remarks>
    public void AnonymizeUserContent(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            _logger.LogWarning("AnonymizeUserContent called with empty userId");
            return;
        }
        
        var commentCount = 0;
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
            commentCount++;
        }
        
        _logger.LogInformation("Anonymized {Count} comments for user {UserId}", commentCount, userId);
    }
    
    /// <summary>Checks if an admin user has been created.</summary>
    /// <returns>True if at least one admin exists, false otherwise.</returns>
    public bool IsAdminSet()
    {
        var hasAdmin = _users.Values.Any(u => u.role == "Admin");
        _logger.LogDebug("Admin set check: {HasAdmin}", hasAdmin);
        return hasAdmin;
    }

    #endregion
    
    #region Passkey Operations
    
    /// <summary>Adds a new passkey.</summary>
    /// <param name="passkey">Passkey to add.</param>
    /// <exception cref="ArgumentNullException">Thrown if passkey is null.</exception>
    public void AddPasskey(Passkey passkey)
    {
        if (passkey == null)
        {
            _logger.LogError("Attempted to add null passkey");
            throw new ArgumentNullException(nameof(passkey));
        }
        
        _passkeys[passkey.id] = passkey;
        _logger.LogInformation("Added passkey {PasskeyId} for user {UserId}", passkey.id, passkey.user_id);
    }
    
    /// <summary>Updates an existing passkey.</summary>
    /// <param name="passkey">Passkey with updated data.</param>
    /// <exception cref="ArgumentNullException">Thrown if passkey is null.</exception>
    public void UpdatePasskey(Passkey passkey)
    {
        if (passkey == null)
        {
            _logger.LogError("Attempted to update null passkey");
            throw new ArgumentNullException(nameof(passkey));
        }
        
        _passkeys[passkey.id] = passkey;
        _logger.LogInformation("Updated passkey {PasskeyId}", passkey.id);
    }
    
    /// <summary>Gets all passkeys for a user.</summary>
    /// <param name="userId">User URN.</param>
    /// <returns>Collection of user's passkeys.</returns>
    public IEnumerable<Passkey> GetPasskeysByUser(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            _logger.LogWarning("GetPasskeysByUser called with empty userId");
            return Enumerable.Empty<Passkey>();
        }
        
        return _passkeys.Values.Where(p => p.user_id == userId);
    }
    
    /// <summary>Gets a passkey by its credential ID.</summary>
    /// <param name="credentialId">WebAuthn credential ID.</param>
    /// <returns>Passkey if found, null otherwise.</returns>
    public Passkey? GetPasskeyByCredentialId(string credentialId)
    {
        if (string.IsNullOrWhiteSpace(credentialId))
        {
            _logger.LogWarning("GetPasskeyByCredentialId called with empty credentialId");
            return null;
        }
        
        return _passkeys.Values.FirstOrDefault(p => p.credential_id == credentialId);
    }
    
    /// <summary>Gets a passkey by its URN.</summary>
    /// <param name="id">Passkey URN.</param>
    /// <returns>Passkey if found, null otherwise.</returns>
    public Passkey? GetPasskey(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            _logger.LogWarning("GetPasskey called with empty ID");
            return null;
        }
        
        return _passkeys.TryGetValue(id, out var passkey) ? passkey : null;
    }
    
    /// <summary>Deletes a passkey.</summary>
    /// <param name="id">Passkey URN.</param>
    public void DeletePasskey(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            _logger.LogWarning("DeletePasskey called with empty ID");
            return;
        }
        
        _passkeys.TryRemove(id, out _);
        _logger.LogInformation("Deleted passkey {PasskeyId}", id);
    }

    #endregion
    
    #region Node Metadata Operations
    
    /// <summary>Gets the node metadata.</summary>
    /// <returns>Current node metadata.</returns>
    public NodeMetadata GetNodeMetadata() => _nodeMetadata;
    
    /// <summary>Updates the node metadata.</summary>
    /// <param name="metadata">New metadata.</param>
    /// <exception cref="ArgumentNullException">Thrown if metadata is null.</exception>
    public void UpdateNodeMetadata(NodeMetadata metadata)
    {
        if (metadata == null)
        {
            _logger.LogError("Attempted to update node metadata with null value");
            throw new ArgumentNullException(nameof(metadata));
        }
        
        _nodeMetadata = metadata;
        _logger.LogInformation("Node metadata updated: {NodeName} v{Version}", metadata.node_name, metadata.version);
    }

    #endregion
    
    #region Taxonomy Operations
    
    /// <summary>Gets the taxonomy configuration.</summary>
    /// <returns>Current taxonomy config.</returns>
    public TaxonomyConfig GetTaxonomyConfig() => _taxonomyConfig;
    
    /// <summary>Updates the taxonomy configuration.</summary>
    /// <param name="config">New taxonomy configuration.</param>
    /// <exception cref="ArgumentNullException">Thrown if config is null.</exception>
    public void UpdateTaxonomyConfig(TaxonomyConfig config)
    {
        if (config == null)
        {
            _logger.LogError("Attempted to update taxonomy config with null value");
            throw new ArgumentNullException(nameof(config));
        }
        
        _taxonomyConfig = config;
        _logger.LogInformation("Taxonomy configuration updated: {TagCount} tags, {AuthorCount} authors", 
            config.tags.Length, config.authors.Length);
    }

    #endregion
}
