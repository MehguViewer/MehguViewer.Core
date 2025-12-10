using MehguViewer.Core.Helpers;
using MehguViewer.Core.Shared;
using MehguViewer.Core.Infrastructures;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace MehguViewer.Core.Services;

/// <summary>
/// Service for validating taxonomy entities (tags, authors, scanlators, groups) against configured taxonomy.
/// Provides fuzzy matching suggestions for invalid entries and comprehensive validation reports.
/// </summary>
/// <remarks>
/// <para><strong>Key Features:</strong></para>
/// <list type="bullet">
///   <item><description>Tag validation with fuzzy matching suggestions (Levenshtein distance ≤ 3)</description></item>
///   <item><description>Entity validation (authors, scanlators, groups) by ID</description></item>
///   <item><description>Full library validation with 5-minute cache</description></item>
///   <item><description>Series and unit-level validation support</description></item>
///   <item><description>Comprehensive logging at multiple levels (Debug, Info, Warning, Error)</description></item>
/// </list>
/// 
/// <para><strong>Validation Rules:</strong></para>
/// <list type="bullet">
///   <item><description>Empty/unconfigured taxonomy categories are permissive (all entries valid)</description></item>
///   <item><description>Case-insensitive comparison for all entity types</description></item>
///   <item><description>Whitespace trimming and normalization</description></item>
///   <item><description>Null safety with appropriate error handling</description></item>
/// </list>
/// 
/// <para><strong>Performance:</strong></para>
/// Full validation results cached for 5 minutes. Uses HashSet for O(1) lookup.
/// Thread-safe caching with ConcurrentDictionary.
/// </remarks>
public class TaxonomyValidationService
{
    #region Constants

    private const int MaxSuggestions = 3;
    private const int MaxEditDistance = 3;
    private static readonly TimeSpan ValidationCacheExpiry = TimeSpan.FromMinutes(5);

    #endregion

    #region Fields

    private readonly IRepository _repo;
    private readonly ILogger<TaxonomyValidationService> _logger;
    private readonly ConcurrentDictionary<string, DateTime> _lastValidationCache = new();

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the TaxonomyValidationService.
    /// </summary>
    /// <param name="repo">Repository for accessing taxonomy configuration and entities.</param>
    /// <param name="logger">Logger for validation operations and diagnostics.</param>
    public TaxonomyValidationService(IRepository repo, ILogger<TaxonomyValidationService> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    #endregion

    #region Public Methods - Tag Validation

    /// <summary>
    /// Validates tags against the current taxonomy configuration.
    /// </summary>
    /// <param name="tags">Array of tags to validate.</param>
    /// <returns>Validation result with valid/invalid tags and suggestions for invalid ones.</returns>
    /// <remarks>
    /// <para><strong>Validation Process:</strong></para>
    /// <list type="number">
    ///   <item>Retrieves taxonomy configuration from repository</item>
    ///   <item>If no tags configured in taxonomy, treats all tags as valid (permissive mode)</item>
    ///   <item>Normalizes input tags (trim whitespace, ignore empty strings)</item>
    ///   <item>Performs case-insensitive lookup against configured tags</item>
    ///   <item>For invalid tags, finds similar suggestions using Levenshtein distance</item>
    /// </list>
    /// 
    /// <para><strong>Suggestion Algorithm:</strong></para>
    /// Uses fuzzy matching (edit distance ≤ 3) to suggest corrections.
    /// Example: "Actoin" → suggests "Action"
    /// </remarks>
    public TagValidationResult ValidateTags(string[]? tags)
    {
        if (tags == null || tags.Length == 0)
        {
            _logger.LogDebug("ValidateTags called with null or empty tags array");
            return new TagValidationResult(
                IsValid: true,
                ValidTags: Array.Empty<string>(),
                InvalidTags: Array.Empty<string>(),
                Suggestions: new Dictionary<string, string[]>()
            );
        }

        _logger.LogDebug("Validating {TagCount} tags", tags.Length);
        var config = _repo.GetTaxonomyConfig();
        
        if (config == null)
        {
            _logger.LogWarning("Taxonomy configuration is null, treating all tags as valid");
            return new TagValidationResult(
                IsValid: true,
                ValidTags: tags.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).ToArray(),
                InvalidTags: Array.Empty<string>(),
                Suggestions: new Dictionary<string, string[]>()
            );
        }
        
        // If no tags configured in taxonomy, treat all tags as valid
        if (config.tags == null || config.tags.Length == 0)
        {
            _logger.LogInformation("No tags configured in taxonomy, treating all {TagCount} tags as valid", tags.Length);
            return new TagValidationResult(
                IsValid: true,
                ValidTags: tags.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).ToArray(),
                InvalidTags: Array.Empty<string>(),
                Suggestions: new Dictionary<string, string[]>()
            );
        }
        
        var configuredTags = new HashSet<string>(config.tags, StringComparer.OrdinalIgnoreCase);
        
        var validTags = new List<string>();
        var invalidTags = new List<string>();
        var suggestions = new Dictionary<string, string[]>();

        foreach (var tag in tags)
        {
            if (string.IsNullOrWhiteSpace(tag))
            {
                continue;
            }

            var normalizedTag = tag.Trim();
            
            // Check if tag exists in taxonomy
            if (configuredTags.Contains(normalizedTag))
            {
                validTags.Add(normalizedTag);
            }
            else
            {
                invalidTags.Add(normalizedTag);
                // Find suggestions using fuzzy matching
                var tagSuggestions = FindSimilarTags(normalizedTag, config.tags);
                if (tagSuggestions.Length > 0)
                {
                    suggestions[normalizedTag] = tagSuggestions;
                }
            }
        }

        var isValid = invalidTags.Count == 0;
        
        if (!isValid)
        {
            _logger.LogWarning("Tag validation failed: {InvalidCount} invalid tags found out of {TotalCount}", 
                invalidTags.Count, tags.Length);
            _logger.LogDebug("Invalid tags: {InvalidTags}", string.Join(", ", invalidTags));
        }
        else
        {
            _logger.LogDebug("All {TagCount} tags validated successfully", validTags.Count);
        }
        
        return new TagValidationResult(
            IsValid: isValid,
            ValidTags: validTags.ToArray(),
            InvalidTags: invalidTags.ToArray(),
            Suggestions: suggestions
        );
    }
    
    #endregion

    #region Public Methods - Entity Validation

    /// <summary>
    /// Validates authors against the current taxonomy configuration.
    /// </summary>
    /// <param name="authors">Array of authors to validate. Can be null.</param>
    /// <returns>Validation result with valid/invalid author IDs.</returns>
    /// <remarks>
    /// <para><strong>Validation Behavior:</strong></para>
    /// <list type="bullet">
    ///   <item>If authors array is null/empty, returns valid result</item>
    ///   <item>If taxonomy has no configured authors, all authors are treated as valid</item>
    ///   <item>Validation is based on author ID using case-insensitive comparison</item>
    ///   <item>Null or empty author IDs are logged as warnings</item>
    /// </list>
    /// </remarks>
    public EntityValidationResult ValidateAuthors(Author[]? authors)
    {
        if (authors == null || authors.Length == 0)
        {
            _logger.LogDebug("ValidateAuthors called with null or empty authors array");
            return new EntityValidationResult(true, Array.Empty<string>(), Array.Empty<string>());
        }
        
        _logger.LogDebug("Validating {AuthorCount} authors", authors.Length);

        var config = _repo.GetTaxonomyConfig();
        
        if (config == null)
        {
            _logger.LogWarning("Taxonomy configuration is null, treating all authors as valid");
            return new EntityValidationResult(
                IsValid: true,
                ValidIds: authors.Select(a => a.id).ToArray(),
                InvalidIds: Array.Empty<string>()
            );
        }
        
        // If no authors configured in taxonomy, treat all authors as valid
        if (config.authors == null || config.authors.Length == 0)
        {
            _logger.LogInformation("No authors configured in taxonomy, treating all {AuthorCount} authors as valid", authors.Length);
            return new EntityValidationResult(
                IsValid: true,
                ValidIds: authors.Select(a => a.id).ToArray(),
                InvalidIds: Array.Empty<string>()
            );
        }
        
        var configuredAuthorIds = new HashSet<string>(
            config.authors.Select(a => a.id), 
            StringComparer.OrdinalIgnoreCase
        );

        var validIds = new List<string>();
        var invalidIds = new List<string>();

        foreach (var author in authors)
        {
            if (string.IsNullOrWhiteSpace(author.id))
            {
                _logger.LogWarning("Author with null or empty ID found: {AuthorName}", author.name ?? "(unnamed)");
                invalidIds.Add(author.id ?? "(null)");
                continue;
            }
            
            if (configuredAuthorIds.Contains(author.id))
            {
                validIds.Add(author.id);
            }
            else
            {
                invalidIds.Add(author.id);
            }
        }
        
        var isValid = invalidIds.Count == 0;
        
        if (!isValid)
        {
            _logger.LogWarning("Author validation failed: {InvalidCount} invalid authors out of {TotalCount}", 
                invalidIds.Count, authors.Length);
            _logger.LogDebug("Invalid author IDs: {InvalidIds}", string.Join(", ", invalidIds));
        }
        else
        {
            _logger.LogDebug("All {AuthorCount} authors validated successfully", validIds.Count);
        }

        return new EntityValidationResult(
            IsValid: isValid,
            ValidIds: validIds.ToArray(),
            InvalidIds: invalidIds.ToArray()
        );
    }

    /// <summary>
    /// Validates scanlators against the current taxonomy configuration.
    /// </summary>
    /// <param name="scanlators">Array of scanlators to validate. Can be null.</param>
    /// <returns>Validation result with valid/invalid scanlator IDs.</returns>
    /// <remarks>
    /// <para><strong>Validation Behavior:</strong></para>
    /// <list type="bullet">
    ///   <item>If scanlators array is null/empty, returns valid result</item>
    ///   <item>If taxonomy has no configured scanlators, all scanlators are treated as valid</item>
    ///   <item>Validation is based on scanlator ID using case-insensitive comparison</item>
    ///   <item>Null or empty scanlator IDs are logged as warnings</item>
    /// </list>
    /// </remarks>
    public EntityValidationResult ValidateScanlators(Scanlator[]? scanlators)
    {
        if (scanlators == null || scanlators.Length == 0)
        {
            _logger.LogDebug("ValidateScanlators called with null or empty scanlators array");
            return new EntityValidationResult(true, Array.Empty<string>(), Array.Empty<string>());
        }
        
        _logger.LogDebug("Validating {ScanlatorCount} scanlators", scanlators.Length);

        var config = _repo.GetTaxonomyConfig();
        
        if (config == null)
        {
            _logger.LogWarning("Taxonomy configuration is null, treating all scanlators as valid");
            return new EntityValidationResult(
                IsValid: true,
                ValidIds: scanlators.Select(s => s.id).ToArray(),
                InvalidIds: Array.Empty<string>()
            );
        }
        
        // If no scanlators configured in taxonomy, treat all scanlators as valid
        if (config.scanlators == null || config.scanlators.Length == 0)
        {
            _logger.LogInformation("No scanlators configured in taxonomy, treating all {ScanlatorCount} scanlators as valid", scanlators.Length);
            return new EntityValidationResult(
                IsValid: true,
                ValidIds: scanlators.Select(s => s.id).ToArray(),
                InvalidIds: Array.Empty<string>()
            );
        }
        
        var configuredScanlatorIds = new HashSet<string>(
            config.scanlators.Select(s => s.id), 
            StringComparer.OrdinalIgnoreCase
        );

        var validIds = new List<string>();
        var invalidIds = new List<string>();

        foreach (var scanlator in scanlators)
        {
            if (string.IsNullOrWhiteSpace(scanlator.id))
            {
                _logger.LogWarning("Scanlator with null or empty ID found: {ScanlatorName}", scanlator.name ?? "(unnamed)");
                invalidIds.Add(scanlator.id ?? "(null)");
                continue;
            }
            
            if (configuredScanlatorIds.Contains(scanlator.id))
            {
                validIds.Add(scanlator.id);
            }
            else
            {
                invalidIds.Add(scanlator.id);
            }
        }
        
        var isValid = invalidIds.Count == 0;
        
        if (!isValid)
        {
            _logger.LogWarning("Scanlator validation failed: {InvalidCount} invalid scanlators out of {TotalCount}", 
                invalidIds.Count, scanlators.Length);
            _logger.LogDebug("Invalid scanlator IDs: {InvalidIds}", string.Join(", ", invalidIds));
        }
        else
        {
            _logger.LogDebug("All {ScanlatorCount} scanlators validated successfully", validIds.Count);
        }

        return new EntityValidationResult(
            IsValid: isValid,
            ValidIds: validIds.ToArray(),
            InvalidIds: invalidIds.ToArray()
        );
    }

    /// <summary>
    /// Validates groups against the current taxonomy configuration.
    /// </summary>
    /// <param name="groups">Array of groups to validate. Can be null.</param>
    /// <returns>Validation result with valid/invalid group IDs.</returns>
    /// <remarks>
    /// <para><strong>Validation Behavior:</strong></para>
    /// <list type="bullet">
    ///   <item>If groups array is null/empty, returns valid result</item>
    ///   <item>If taxonomy has no configured groups, all groups are treated as valid</item>
    ///   <item>Validation is based on group ID using case-insensitive comparison</item>
    ///   <item>Null or empty group IDs are logged as warnings</item>
    /// </list>
    /// </remarks>
    public EntityValidationResult ValidateGroups(Group[]? groups)
    {
        if (groups == null || groups.Length == 0)
        {
            _logger.LogDebug("ValidateGroups called with null or empty groups array");
            return new EntityValidationResult(true, Array.Empty<string>(), Array.Empty<string>());
        }
        
        _logger.LogDebug("Validating {GroupCount} groups", groups.Length);

        var config = _repo.GetTaxonomyConfig();
        
        if (config == null)
        {
            _logger.LogWarning("Taxonomy configuration is null, treating all groups as valid");
            return new EntityValidationResult(
                IsValid: true,
                ValidIds: groups.Select(g => g.id).ToArray(),
                InvalidIds: Array.Empty<string>()
            );
        }
        
        // If no groups configured in taxonomy, treat all groups as valid
        if (config.groups == null || config.groups.Length == 0)
        {
            _logger.LogInformation("No groups configured in taxonomy, treating all {GroupCount} groups as valid", groups.Length);
            return new EntityValidationResult(
                IsValid: true,
                ValidIds: groups.Select(g => g.id).ToArray(),
                InvalidIds: Array.Empty<string>()
            );
        }
        
        var configuredGroupIds = new HashSet<string>(
            config.groups.Select(g => g.id), 
            StringComparer.OrdinalIgnoreCase
        );

        var validIds = new List<string>();
        var invalidIds = new List<string>();

        foreach (var group in groups)
        {
            if (string.IsNullOrWhiteSpace(group.id))
            {
                _logger.LogWarning("Group with null or empty ID found: {GroupName}", group.name ?? "(unnamed)");
                invalidIds.Add(group.id ?? "(null)");
                continue;
            }
            
            if (configuredGroupIds.Contains(group.id))
            {
                validIds.Add(group.id);
            }
            else
            {
                invalidIds.Add(group.id);
            }
        }
        
        var isValid = invalidIds.Count == 0;
        
        if (!isValid)
        {
            _logger.LogWarning("Group validation failed: {InvalidCount} invalid groups out of {TotalCount}", 
                invalidIds.Count, groups.Length);
            _logger.LogDebug("Invalid group IDs: {InvalidIds}", string.Join(", ", invalidIds));
        }
        else
        {
            _logger.LogDebug("All {GroupCount} groups validated successfully", validIds.Count);
        }

        return new EntityValidationResult(
            IsValid: isValid,
            ValidIds: validIds.ToArray(),
            InvalidIds: invalidIds.ToArray()
        );
    }
    
    #endregion

    #region Public Methods - Composite Validation

    /// <summary>
    /// Validates all entities in a series against taxonomy.
    /// </summary>
    /// <param name="series">The series to validate. Cannot be null.</param>
    /// <returns>Comprehensive validation result for all series entities.</returns>
    /// <exception cref="ArgumentNullException">Thrown when series is null.</exception>
    /// <remarks>
    /// <para><strong>Validation Process:</strong></para>
    /// <list type="number">
    ///   <item>Validates tags against taxonomy</item>
    ///   <item>Validates authors against taxonomy</item>
    ///   <item>Validates scanlators against taxonomy</item>
    ///   <item>Validates groups if present (optional)</item>
    /// </list>
    /// Overall validation passes only if all entity types are valid.
    /// </remarks>
    public SeriesValidationResult ValidateSeries(Series series)
    {
        if (series == null)
        {
            _logger.LogError("ValidateSeries called with null series");
            throw new ArgumentNullException(nameof(series), "Series cannot be null");
        }
        
        _logger.LogDebug("Validating series: {SeriesId} - {SeriesTitle}", series.id, series.title);
        
        var tagResult = ValidateTags(series.tags ?? Array.Empty<string>());
        var authorResult = ValidateAuthors(series.authors);
        var scanlatorResult = ValidateScanlators(series.scanlators);
        
        // Validate groups if they exist (groups are optional)
        var groupResult = ValidateGroups(series.groups);
        
        var isValid = tagResult.IsValid && authorResult.IsValid && scanlatorResult.IsValid && groupResult.IsValid;
        
        if (!isValid)
        {
            _logger.LogWarning("Series validation failed for: {SeriesId} - {SeriesTitle}", series.id, series.title);
        }
        else
        {
            _logger.LogDebug("Series validation passed for: {SeriesId}", series.id);
        }

        return new SeriesValidationResult(
            IsValid: isValid,
            Tags: tagResult,
            Authors: authorResult,
            Scanlators: scanlatorResult,
            Groups: groupResult
        );
    }

    /// <summary>
    /// Validates all entities in a unit against taxonomy.
    /// </summary>
    /// <param name="unit">The unit to validate. Cannot be null.</param>
    /// <returns>Comprehensive validation result for all unit entities.</returns>
    /// <exception cref="ArgumentNullException">Thrown when unit is null.</exception>
    /// <remarks>
    /// <para><strong>Validation Process:</strong></para>
    /// <list type="number">
    ///   <item>Validates tags against taxonomy</item>
    ///   <item>Validates authors against taxonomy</item>
    ///   <item>Extracts and validates scanlators from all localized metadata</item>
    /// </list>
    /// Overall validation passes only if all entity types are valid.
    /// Groups are not validated at unit level.
    /// </remarks>
    public SeriesValidationResult ValidateUnit(Unit unit)
    {
        if (unit == null)
        {
            _logger.LogError("ValidateUnit called with null unit");
            throw new ArgumentNullException(nameof(unit), "Unit cannot be null");
        }
        
        _logger.LogDebug("Validating unit: {UnitId} - {UnitTitle}", unit.id, unit.title);
        
        var tagResult = ValidateTags(unit.tags ?? Array.Empty<string>());
        var authorResult = ValidateAuthors(unit.authors);
        
        // Extract scanlators from localized metadata
        var allScanlators = new List<Scanlator>();
        if (unit.localized != null)
        {
            foreach (var (language, metadata) in unit.localized)
            {
                if (metadata?.scanlators != null)
                {
                    _logger.LogTrace("Extracting {Count} scanlators from language: {Language}", 
                        metadata.scanlators.Length, language);
                    allScanlators.AddRange(metadata.scanlators);
                }
            }
        }
        
        var scanlatorResult = ValidateScanlators(allScanlators.ToArray());
        
        var isValid = tagResult.IsValid && authorResult.IsValid && scanlatorResult.IsValid;
        
        if (!isValid)
        {
            _logger.LogWarning("Unit validation failed for: {UnitId} - {UnitTitle}", unit.id, unit.title);
        }
        else
        {
            _logger.LogDebug("Unit validation passed for: {UnitId}", unit.id);
        }

        return new SeriesValidationResult(
            IsValid: isValid,
            Tags: tagResult,
            Authors: authorResult,
            Scanlators: scanlatorResult,
            Groups: new EntityValidationResult(true, Array.Empty<string>(), Array.Empty<string>())
        );
    }
    
    #endregion

    #region Public Methods - Full Validation

    /// <summary>
    /// Runs a full validation job across all series and units in the system.
    /// </summary>
    /// <returns>Detailed validation report with all issues found.</returns>
    /// <remarks>
    /// <para><strong>Performance:</strong></para>
    /// Results are cached for 5 minutes to prevent frequent full scans.
    /// If validation was run within cache window, returns cached timestamp with zero counts.
    /// 
    /// <para><strong>Process:</strong></para>
    /// <list type="number">
    ///   <item>Checks validation cache (5-minute expiry)</item>
    ///   <item>Retrieves all series from repository</item>
    ///   <item>Validates each series (tags, authors, scanlators, groups)</item>
    ///   <item>Retrieves and validates all units for each series</item>
    ///   <item>Aggregates validation issues into report</item>
    ///   <item>Updates cache with validation timestamp</item>
    /// </list>
    /// 
    /// <para><strong>Report Contents:</strong></para>
    /// - Total series/units counts
    /// - Series-level validation issues (with URN and title)
    /// - Unit-level validation issues (with URN and title)
    /// - Summary message
    /// </remarks>
    public async Task<TaxonomyValidationReport> RunFullValidationAsync()
    {
        await Task.CompletedTask;
        var cacheKey = "full-validation";
        
        // Check cache to prevent frequent re-validation
        if (_lastValidationCache.TryGetValue(cacheKey, out var lastRun))
        {
            var timeSinceLastRun = DateTime.UtcNow - lastRun;
            if (timeSinceLastRun < ValidationCacheExpiry)
            {
                _logger.LogInformation(
                    "Validation skipped - last run was {ElapsedMinutes:F1} minutes ago (cache expires after {CacheMinutes} minutes)",
                    timeSinceLastRun.TotalMinutes, ValidationCacheExpiry.TotalMinutes);
                return new TaxonomyValidationReport(
                    ValidatedAt: lastRun,
                    TotalSeries: 0,
                    TotalUnits: 0,
                    SeriesIssues: Array.Empty<ValidationIssue>(),
                    UnitIssues: Array.Empty<ValidationIssue>(),
                    Summary: $"Cached - validation skipped (last run: {timeSinceLastRun.TotalMinutes:F1} minutes ago)"
                );
            }
        }

        _logger.LogInformation("Starting full taxonomy validation across entire library");
        var startTime = DateTime.UtcNow;

        var seriesIssues = new List<ValidationIssue>();
        var unitIssues = new List<ValidationIssue>();
        var totalUnits = 0;
        var processedSeries = 0;
        var processedUnits = 0;

        try
        {
            // Validate all series
            var allSeries = _repo.ListSeries().ToList();
            _logger.LogInformation("Validating {SeriesCount} series in library", allSeries.Count);
            
            foreach (var series in allSeries)
            {
                try
                {
                    var result = ValidateSeries(series);
                    processedSeries++;
                    
                    if (!result.IsValid)
                    {
                        var issues = new List<string>();
                        if (result.Tags.InvalidTags.Length > 0)
                        {
                            issues.Add($"Invalid tags: {string.Join(", ", result.Tags.InvalidTags)}");
                        }
                        if (result.Authors.InvalidIds.Length > 0)
                        {
                            issues.Add($"Invalid authors: {string.Join(", ", result.Authors.InvalidIds)}");
                        }
                        if (result.Scanlators.InvalidIds.Length > 0)
                        {
                            issues.Add($"Invalid scanlators: {string.Join(", ", result.Scanlators.InvalidIds)}");
                        }
                        if (result.Groups.InvalidIds.Length > 0)
                        {
                            issues.Add($"Invalid groups: {string.Join(", ", result.Groups.InvalidIds)}");
                        }

                        seriesIssues.Add(new ValidationIssue(
                            EntityUrn: series.id,
                            EntityTitle: series.title,
                            Issues: issues.ToArray()
                        ));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error validating series: {SeriesId} - {SeriesTitle}", series.id, series.title);
                    seriesIssues.Add(new ValidationIssue(
                        EntityUrn: series.id,
                        EntityTitle: series.title,
                        Issues: new[] { $"Validation error: {ex.Message}" }
                    ));
                }

                // Validate units in this series
                try
                {
                    var units = _repo.ListUnits(series.id).ToList();
                    totalUnits += units.Count;
                    
                    if (units.Count > 0)
                    {
                        _logger.LogTrace("Validating {UnitCount} units for series: {SeriesId}", units.Count, series.id);
                    }
                    
                    foreach (var unit in units)
                    {
                        try
                        {
                            var unitResult = ValidateUnit(unit);
                            processedUnits++;
                            
                            if (!unitResult.IsValid)
                            {
                                var issues = new List<string>();
                                if (unitResult.Tags.InvalidTags.Length > 0)
                                {
                                    issues.Add($"Invalid tags: {string.Join(", ", unitResult.Tags.InvalidTags)}");
                                }
                                if (unitResult.Authors.InvalidIds.Length > 0)
                                {
                                    issues.Add($"Invalid authors: {string.Join(", ", unitResult.Authors.InvalidIds)}");
                                }
                                if (unitResult.Scanlators.InvalidIds.Length > 0)
                                {
                                    issues.Add($"Invalid scanlators: {string.Join(", ", unitResult.Scanlators.InvalidIds)}");
                                }

                                unitIssues.Add(new ValidationIssue(
                                    EntityUrn: unit.id,
                                    EntityTitle: unit.title,
                                    Issues: issues.ToArray()
                                ));
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error validating unit: {UnitId} - {UnitTitle}", unit.id, unit.title);
                            unitIssues.Add(new ValidationIssue(
                                EntityUrn: unit.id,
                                EntityTitle: unit.title,
                                Issues: new[] { $"Validation error: {ex.Message}" }
                            ));
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error listing units for series: {SeriesId}", series.id);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Critical error during full validation");
            return new TaxonomyValidationReport(
                ValidatedAt: startTime,
                TotalSeries: processedSeries,
                TotalUnits: processedUnits,
                SeriesIssues: seriesIssues.ToArray(),
                UnitIssues: unitIssues.ToArray(),
                Summary: $"Validation failed with error: {ex.Message}"
            );
        }

        _lastValidationCache[cacheKey] = startTime;
        var duration = DateTime.UtcNow - startTime;

        var report = new TaxonomyValidationReport(
            ValidatedAt: startTime,
            TotalSeries: processedSeries,
            TotalUnits: totalUnits,
            SeriesIssues: seriesIssues.ToArray(),
            UnitIssues: unitIssues.ToArray(),
            Summary: $"Validated {processedSeries} series and {processedUnits} units in {duration.TotalSeconds:F2}s. " +
                     $"Found {seriesIssues.Count} series issues and {unitIssues.Count} unit issues."
        );
        
        // Log comprehensive summary
        if (seriesIssues.Count == 0 && unitIssues.Count == 0)
        {
            _logger.LogInformation(
                "✓ Taxonomy validation completed successfully in {Duration:F2}s. " +
                "All {TotalSeries} series and {TotalUnits} units are valid.",
                duration.TotalSeconds, processedSeries, processedUnits
            );
        }
        else
        {
            _logger.LogWarning(
                "⚠ Taxonomy validation completed in {Duration:F2}s with issues. " +
                "Series: {TotalSeries} ({IssueCount} with issues), Units: {TotalUnits} ({UnitIssueCount} with issues)",
                duration.TotalSeconds, processedSeries, seriesIssues.Count, processedUnits, unitIssues.Count
            );
            
            // Log detailed issues at debug level
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                foreach (var issue in seriesIssues.Take(10)) // Log first 10 issues
                {
                    _logger.LogDebug("Series issue - {Urn}: {Issues}", 
                        issue.EntityUrn, string.Join("; ", issue.Issues));
                }
                
                if (seriesIssues.Count > 10)
                {
                    _logger.LogDebug("... and {More} more series issues", seriesIssues.Count - 10);
                }
            }
        }

        return report;
    }
    
    #endregion

    #region Private Helper Methods

    /// <summary>
    /// Finds similar tags using Levenshtein distance for fuzzy matching.
    /// </summary>
    /// <param name="input">The invalid tag to find suggestions for.</param>
    /// <param name="existingTags">Array of valid tags from taxonomy.</param>
    /// <param name="maxResults">Maximum number of suggestions to return (default 3).</param>
    /// <returns>Array of suggested tags ordered by similarity (most similar first).</returns>
    /// <remarks>
    /// <para><strong>Algorithm:</strong></para>
    /// Uses Levenshtein edit distance to measure similarity.
    /// Only returns suggestions with edit distance ≤ 3.
    /// 
    /// <para><strong>Example:</strong></para>
    /// Input "actio" → suggests "action" (distance 1)
    /// </remarks>
    private string[] FindSimilarTags(string input, string[] existingTags, int maxResults = MaxSuggestions)
    {
        var similarities = existingTags
            .Select(tag => new { Tag = tag, Distance = LevenshteinDistance(input.ToLowerInvariant(), tag.ToLowerInvariant()) })
            .Where(x => x.Distance <= MaxEditDistance) // Only suggest if distance is 3 or less
            .OrderBy(x => x.Distance)
            .Take(maxResults)
            .Select(x => x.Tag)
            .ToArray();

        return similarities;
    }

    /// <summary>
    /// Calculates Levenshtein distance between two strings for fuzzy matching.
    /// </summary>
    /// <param name="s">First string.</param>
    /// <param name="t">Second string.</param>
    /// <returns>Edit distance (minimum number of single-character edits required).</returns>
    /// <remarks>
    /// <para><strong>Algorithm:</strong></para>
    /// Classic dynamic programming implementation of Levenshtein distance.
    /// Measures similarity by counting insertions, deletions, and substitutions.
    /// 
    /// <para><strong>Complexity:</strong></para>
    /// - Time: O(n × m) where n and m are string lengths
    /// - Space: O(n × m) for distance matrix
    /// 
    /// <para><strong>Examples:</strong></para>
    /// - "kitten" vs "sitting" = 3 (k→s, e→i, insert g)
    /// - "action" vs "actoin" = 1 (swap i and o)
    /// </remarks>
    private int LevenshteinDistance(string s, string t)
    {
        if (string.IsNullOrEmpty(s))
        {
            return string.IsNullOrEmpty(t) ? 0 : t.Length;
        }

        if (string.IsNullOrEmpty(t))
        {
            return s.Length;
        }

        int n = s.Length;
        int m = t.Length;
        int[,] d = new int[n + 1, m + 1];

        for (int i = 0; i <= n; i++)
        {
            d[i, 0] = i;
        }

        for (int j = 0; j <= m; j++)
        {
            d[0, j] = j;
        }

        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                int cost = (t[j - 1] == s[i - 1]) ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }

        return d[n, m];
    }
    
    #endregion
}

#region Validation Result Models

/// <summary>
/// Result of tag validation with suggestions for invalid tags.
/// </summary>
/// <param name="IsValid">Whether all tags are valid.</param>
/// <param name="ValidTags">Array of tags found in taxonomy.</param>
/// <param name="InvalidTags">Array of tags not found in taxonomy.</param>
/// <param name="Suggestions">Dictionary mapping invalid tags to suggested corrections.</param>
public record TagValidationResult(
    bool IsValid,
    string[] ValidTags,
    string[] InvalidTags,
    Dictionary<string, string[]> Suggestions
);

/// <summary>
/// Result of entity validation (authors, scanlators, groups).
/// </summary>
/// <param name="IsValid">Whether all entity IDs are valid.</param>
/// <param name="ValidIds">Array of IDs found in taxonomy.</param>
/// <param name="InvalidIds">Array of IDs not found in taxonomy.</param>
public record EntityValidationResult(
    bool IsValid,
    string[] ValidIds,
    string[] InvalidIds
);

/// <summary>
/// Comprehensive validation result for series or unit.
/// </summary>
/// <param name="IsValid">Whether all entities are valid.</param>
/// <param name="Tags">Tag validation result.</param>
/// <param name="Authors">Author validation result.</param>
/// <param name="Scanlators">Scanlator validation result.</param>
/// <param name="Groups">Group validation result.</param>
public record SeriesValidationResult(
    bool IsValid,
    TagValidationResult Tags,
    EntityValidationResult Authors,
    EntityValidationResult Scanlators,
    EntityValidationResult Groups
);

/// <summary>
/// Single validation issue for an entity.
/// </summary>
/// <param name="EntityUrn">URN of the entity with issues.</param>
/// <param name="EntityTitle">Title of the entity for display.</param>
/// <param name="Issues">Array of issue descriptions.</param>
public record ValidationIssue(
    string EntityUrn,
    string EntityTitle,
    string[] Issues
);

/// <summary>
/// Full validation report across entire library.
/// </summary>
/// <param name="ValidatedAt">Timestamp when validation was performed.</param>
/// <param name="TotalSeries">Total number of series validated.</param>
/// <param name="TotalUnits">Total number of units validated.</param>
/// <param name="SeriesIssues">Array of series-level validation issues.</param>
/// <param name="UnitIssues">Array of unit-level validation issues.</param>
/// <param name="Summary">Human-readable summary of validation results.</param>
public record TaxonomyValidationReport(
    DateTime ValidatedAt,
    int TotalSeries,
    int TotalUnits,
    ValidationIssue[] SeriesIssues,
    ValidationIssue[] UnitIssues,
    string Summary
);

#endregion
