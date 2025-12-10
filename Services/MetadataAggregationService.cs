using MehguViewer.Core.Helpers;
using MehguViewer.Core.Shared;
using Microsoft.Extensions.Logging;

namespace MehguViewer.Core.Services;

/// <summary>
/// Service for aggregating and inheriting metadata between series and their units.
/// </summary>
/// <remarks>
/// <para><strong>Purpose:</strong></para>
/// Manages bidirectional metadata flow:
/// <list type="bullet">
///   <item><strong>Aggregation (Units → Series):</strong> Collects unique metadata from all units into parent series</item>
///   <item><strong>Inheritance (Series → Unit):</strong> Propagates series metadata to units that lack explicit values</item>
/// </list>
/// 
/// <para><strong>Key Features:</strong></para>
/// <list type="bullet">
///   <item>Deduplicates tags, content warnings, authors, and scanlators across units</item>
///   <item>Handles localized metadata per language (especially scanlators)</item>
///   <item>Case-insensitive comparison for string-based metadata</item>
///   <item>Preserves unit-specific metadata while aggregating common elements</item>
///   <item>Comprehensive logging for diagnostics and monitoring</item>
///   <item>Input validation to prevent corruption</item>
/// </list>
/// 
/// <para><strong>Use Cases:</strong></para>
/// - When units are added/updated: aggregate their metadata to parent series
/// - When creating new units: inherit series-level defaults
/// - When series has multiple languages: maintain language-specific scanlation teams
/// 
/// <para><strong>Security & Performance:</strong></para>
/// - Validates all input parameters to prevent null reference exceptions
/// - Uses efficient HashSet/Dictionary for O(1) deduplication
/// - Immutable record types prevent unintended mutations
/// - Defensive copying for thread safety
/// 
/// <para><strong>Implementation Notes:</strong></para>
/// This service maintains no state and is thread-safe.
/// All operations are pure functions that return new instances.
/// Uses structured logging for observability and debugging.
/// </remarks>
public class MetadataAggregationService
{
    #region Fields & Constructor

    private readonly ILogger<MetadataAggregationService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MetadataAggregationService"/> class.
    /// </summary>
    /// <param name="logger">Logger instance for diagnostic logging.</param>
    /// <exception cref="ArgumentNullException">Thrown when logger is null.</exception>
    public MetadataAggregationService(ILogger<MetadataAggregationService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    #endregion

    #region Public Methods - Aggregation
    
    /// <summary>
    /// Aggregates metadata from all units in a series into the parent series.
    /// </summary>
    /// <param name="series">The parent series to update.</param>
    /// <param name="units">Collection of units to aggregate metadata from.</param>
    /// <returns>Updated series with aggregated metadata from all units.</returns>
    /// <exception cref="ArgumentNullException">Thrown when series or units is null.</exception>
    /// <remarks>
    /// <para><strong>Aggregation Rules:</strong></para>
    /// <list type="bullet">
    ///   <item><strong>Tags:</strong> Union of all series and unit tags (case-insensitive)</item>
    ///   <item><strong>Content Warnings:</strong> Union of all warnings (case-insensitive)</item>
    ///   <item><strong>Authors:</strong> Deduplicated by ID from series and all units</item>
    ///   <item><strong>Scanlators:</strong> Deduplicated by ID, aggregated from:
    ///     <list type="number">
    ///       <item>Series-level scanlators</item>
    ///       <item>Localized scanlators (per language)</item>
    ///     </list>
    ///   </item>
    ///   <item><strong>Localized Metadata:</strong> Merged per language, preserving language-specific scanlators</item>
    /// </list>
    /// 
    /// <para><strong>Example Scenario:</strong></para>
    /// Series has English scanlator "TeamA", Unit 1 adds "TeamB" for English.
    /// Result: Series English scanlators = [TeamA, TeamB].
    /// 
    /// Updates the series timestamp (updated_at) to current UTC time.
    /// </remarks>
    public Series AggregateMetadata(Series series, IEnumerable<Unit> units)
    {
        // Input validation
        if (series == null)
        {
            _logger.LogError("AggregateMetadata called with null series");
            throw new ArgumentNullException(nameof(series));
        }

        if (units == null)
        {
            _logger.LogError("AggregateMetadata called with null units for series {SeriesId}", series.id);
            throw new ArgumentNullException(nameof(units));
        }

        _logger.LogDebug("Starting metadata aggregation for series {SeriesId} ({SeriesTitle})", 
            series.id, series.title);

        var unitList = units.ToList();
        
        if (!unitList.Any())
        {
            _logger.LogDebug("No units to aggregate for series {SeriesId}, returning unchanged", series.id);
            return series;
        }

        _logger.LogInformation(
            "Aggregating metadata from {UnitCount} units for series {SeriesId}",
            unitList.Count, series.id);

        try
        {
            var aggregatedTags = AggregateStringList(series.tags, unitList.SelectMany(u => u.tags ?? []));
            var aggregatedWarnings = AggregateStringList(series.content_warnings, unitList.SelectMany(u => u.content_warnings ?? []));
            var aggregatedAuthors = AggregateAuthors(series, unitList);
            var aggregatedLocalized = AggregateLocalizedMetadata(series, unitList);
            var aggregatedScanlators = AggregateScanlators(series, aggregatedLocalized);

            _logger.LogInformation(
                "Metadata aggregation completed for series {SeriesId}: {TagCount} tags, {WarningCount} warnings, {AuthorCount} authors, {ScanCount} scanlators, {LangCount} languages",
                series.id, aggregatedTags?.Length ?? 0, aggregatedWarnings?.Length ?? 0, 
                aggregatedAuthors?.Length ?? 0, aggregatedScanlators?.Length ?? 0, 
                aggregatedLocalized?.Count ?? 0);

            return series with
            {
                tags = aggregatedTags ?? [],
                content_warnings = aggregatedWarnings ?? [],
                authors = aggregatedAuthors ?? [],
                scanlators = aggregatedScanlators ?? [],
                localized = aggregatedLocalized?.Count > 0 ? aggregatedLocalized : null,
                updated_at = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, 
                "Failed to aggregate metadata for series {SeriesId} from {UnitCount} units", 
                series.id, unitList.Count);
            throw;
        }
    }
    
    #endregion

    #region Public Methods - Inheritance
    
    /// <summary>
    /// Inherits metadata from parent series to a unit that lacks explicit values.
    /// </summary>
    /// <param name="unit">The unit to populate with inherited metadata.</param>
    /// <param name="series">The parent series to inherit from.</param>
    /// <param name="language">Optional language code to inherit language-specific metadata (e.g., "en", "ja").</param>
    /// <returns>Unit with inherited metadata where unit values were null.</returns>
    /// <exception cref="ArgumentNullException">Thrown when unit or series is null.</exception>
    /// <remarks>
    /// <para><strong>Inheritance Rules:</strong></para>
    /// <list type="bullet">
    ///   <item><strong>Tags:</strong> Uses unit tags if set, otherwise inherits from series</item>
    ///   <item><strong>Content Warnings:</strong> Uses unit warnings if set, otherwise inherits from series</item>
    ///   <item><strong>Authors:</strong> Uses unit authors if set, otherwise inherits from series</item>
    ///   <item><strong>Localized Metadata:</strong> If language specified and series has localized data:
    ///     <list type="bullet">
    ///       <item>Inherits language-specific scanlators</item>
    ///       <item>Only creates localized entry if unit doesn't already have one</item>
    ///     </list>
    ///   </item>
    /// </list>
    /// 
    /// <para><strong>Non-Overwriting Behavior:</strong></para>
    /// This method never overwrites existing unit metadata.
    /// Only populates null/missing fields with series defaults.
    /// 
    /// <para><strong>Example Scenario:</strong></para>
    /// Series has tags ["Action", "Drama"], Unit has no tags.
    /// Result: Unit inherits ["Action", "Drama"].
    /// If Unit has tags ["Comedy"], it keeps ["Comedy"] (no inheritance).
    /// </remarks>
    public Unit InheritMetadataFromSeries(Unit unit, Series series, string? language = null)
    {
        // Input validation
        if (unit == null)
        {
            _logger.LogError("InheritMetadataFromSeries called with null unit");
            throw new ArgumentNullException(nameof(unit));
        }

        if (series == null)
        {
            _logger.LogError("InheritMetadataFromSeries called with null series for unit {UnitId}", 
                unit?.id ?? "unknown");
            throw new ArgumentNullException(nameof(series));
        }

        _logger.LogDebug(
            "Inheriting metadata for unit {UnitId} from series {SeriesId}, language: {Language}",
            unit.id, series.id, language ?? "none");

        try
        {
            // Inherit metadata only if unit doesn't have it
            var tags = unit.tags ?? series.tags;
            var warnings = unit.content_warnings ?? series.content_warnings;
            var authors = unit.authors ?? series.authors;
            
            // Handle localized metadata inheritance
            Dictionary<string, UnitLocalizedMetadata>? inheritedLocalized = null;
            if (!string.IsNullOrWhiteSpace(language) && series.localized?.ContainsKey(language) == true)
            {
                var seriesLangData = series.localized[language];
                inheritedLocalized = new Dictionary<string, UnitLocalizedMetadata>
                {
                    [language] = new UnitLocalizedMetadata(
                        title: null,
                        scanlators: seriesLangData.scanlators,
                        content_folder: null
                    )
                };

                _logger.LogDebug(
                    "Inherited {ScanCount} scanlators for language {Language} in unit {UnitId}",
                    seriesLangData.scanlators?.Length ?? 0, language, unit.id);
            }

            var hasInheritedData = tags != unit.tags || warnings != unit.content_warnings || 
                                   authors != unit.authors || inheritedLocalized != null;

            if (hasInheritedData)
            {
                _logger.LogInformation(
                    "Unit {UnitId} inherited metadata: tags={TagInherited}, warnings={WarningInherited}, authors={AuthorInherited}, localized={LocalizedInherited}",
                    unit.id,
                    tags != unit.tags,
                    warnings != unit.content_warnings,
                    authors != unit.authors,
                    inheritedLocalized != null);
            }

            return unit with
            {
                tags = tags,
                content_warnings = warnings,
                authors = authors,
                localized = unit.localized ?? inheritedLocalized
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to inherit metadata for unit {UnitId} from series {SeriesId}",
                unit.id, series.id);
            throw;
        }
    }
    
    #endregion

    #region Private Helper Methods
    
    /// <summary>
    /// Aggregates string lists (tags, warnings) with case-insensitive deduplication.
    /// </summary>
    /// <param name="seriesValues">String values from the series level.</param>
    /// <param name="unitValues">String values from all units.</param>
    /// <returns>Deduplicated array of all unique values, or null if both inputs are empty.</returns>
    private string[]? AggregateStringList(string[]? seriesValues, IEnumerable<string> unitValues)
    {
        if (unitValues == null)
        {
            _logger.LogWarning("AggregateStringList called with null unitValues");
            return seriesValues;
        }

        var aggregated = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        // Add series values, filtering out null/empty/whitespace
        if (seriesValues != null)
        {
            foreach (var value in seriesValues)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    aggregated.Add(value);
                }
            }
        }
        
        var initialCount = aggregated.Count;
        var addedCount = 0;

        foreach (var value in unitValues)
        {
            if (!string.IsNullOrWhiteSpace(value) && aggregated.Add(value))
            {
                addedCount++;
            }
        }

        _logger.LogTrace(
            "Aggregated string list: {InitialCount} initial, {AddedCount} added from units, {TotalCount} total",
            initialCount, addedCount, aggregated.Count);

        return aggregated.Count > 0 ? aggregated.ToArray() : null;
    }
    
    /// <summary>
    /// Aggregates authors from series and all units, deduplicated by ID.
    /// </summary>
    /// <param name="series">The parent series.</param>
    /// <param name="units">List of all units to aggregate from.</param>
    /// <returns>Deduplicated array of all authors, or null if none exist.</returns>
    private Author[]? AggregateAuthors(Series series, List<Unit> units)
    {
        var aggregatedAuthors = new Dictionary<string, Author>(StringComparer.OrdinalIgnoreCase);
        
        // Add series-level authors
        if (series.authors != null)
        {
            foreach (var author in series.authors)
            {
                if (author != null && !string.IsNullOrWhiteSpace(author.id))
                {
                    aggregatedAuthors[author.id] = author;
                }
            }
        }
        
        var initialCount = aggregatedAuthors.Count;

        // Add unit-level authors
        foreach (var unit in units)
        {
            if (unit.authors != null)
            {
                foreach (var author in unit.authors)
                {
                    if (author != null && !string.IsNullOrWhiteSpace(author.id))
                    {
                        aggregatedAuthors[author.id] = author;
                    }
                }
            }
        }
        
        _logger.LogTrace(
            "Aggregated authors: {InitialCount} from series, {TotalCount} total after units",
            initialCount, aggregatedAuthors.Count);

        return aggregatedAuthors.Count > 0 ? aggregatedAuthors.Values.ToArray() : null;
    }
    
    /// <summary>
    /// Aggregates localized metadata across all units, merging scanlators per language.
    /// </summary>
    /// <param name="series">The parent series.</param>
    /// <param name="units">List of all units to aggregate from.</param>
    /// <returns>Dictionary of localized metadata per language code, or null if none exist.</returns>
    private Dictionary<string, LocalizedMetadata>? AggregateLocalizedMetadata(Series series, List<Unit> units)
    {
        var aggregated = new Dictionary<string, LocalizedMetadata>(
            series.localized ?? new Dictionary<string, LocalizedMetadata>()
        );
        
        var languagesAdded = 0;

        foreach (var unit in units)
        {
            if (unit.localized == null) continue;

            foreach (var (lang, unitLangData) in unit.localized)
            {
                // Validate language code
                if (string.IsNullOrWhiteSpace(lang))
                {
                    _logger.LogWarning(
                        "Skipping empty language code in unit {UnitId} localized metadata",
                        unit.id);
                    continue;
                }

                if (!aggregated.ContainsKey(lang))
                {
                    aggregated[lang] = new LocalizedMetadata(
                        title: null,
                        description: null,
                        alt_titles: null,
                        scanlators: unitLangData.scanlators ?? [],
                        content_folder: null
                    );
                    languagesAdded++;
                }
                else
                {
                    var existingScanlators = aggregated[lang].scanlators ?? [];
                    var newScanlators = unitLangData.scanlators ?? [];
                    
                    var mergedScanlators = new Dictionary<string, Scanlator>(StringComparer.OrdinalIgnoreCase);
                    
                    // Add existing scanlators
                    foreach (var s in existingScanlators)
                    {
                        if (s != null && !string.IsNullOrWhiteSpace(s.id))
                        {
                            mergedScanlators[s.id] = s;
                        }
                    }
                    
                    // Add new scanlators
                    var newScanCount = 0;
                    foreach (var s in newScanlators)
                    {
                        if (s != null && !string.IsNullOrWhiteSpace(s.id) && !mergedScanlators.ContainsKey(s.id))
                        {
                            mergedScanlators[s.id] = s;
                            newScanCount++;
                        }
                    }

                    if (newScanCount > 0)
                    {
                        _logger.LogTrace(
                            "Added {Count} new scanlators for language {Language} from unit {UnitId}",
                            newScanCount, lang, unit.id);
                    }

                    aggregated[lang] = aggregated[lang] with
                    {
                        scanlators = mergedScanlators.Values.ToArray()
                    };
                }
            }
        }

        if (languagesAdded > 0)
        {
            _logger.LogDebug("Added {Count} new language(s) during localized metadata aggregation", 
                languagesAdded);
        }
        
        return aggregated.Count > 0 ? aggregated : null;
    }
    
    /// <summary>
    /// Aggregates scanlators from series-level and all localized metadata.
    /// </summary>
    /// <param name="series">The parent series.</param>
    /// <param name="aggregatedLocalized">Previously aggregated localized metadata.</param>
    /// <returns>Deduplicated array of all scanlators, or null if none exist.</returns>
    private Scanlator[]? AggregateScanlators(Series series, Dictionary<string, LocalizedMetadata>? aggregatedLocalized)
    {
        var aggregatedScanlators = new Dictionary<string, Scanlator>(StringComparer.OrdinalIgnoreCase);
        
        // Add series-level scanlators
        if (series.scanlators != null)
        {
            foreach (var scanlator in series.scanlators)
            {
                if (scanlator != null && !string.IsNullOrWhiteSpace(scanlator.id))
                {
                    aggregatedScanlators[scanlator.id] = scanlator;
                }
            }
        }
        
        var initialCount = aggregatedScanlators.Count;

        // Add scanlators from localized metadata
        if (aggregatedLocalized != null)
        {
            foreach (var (lang, langData) in aggregatedLocalized)
            {
                if (langData.scanlators != null)
                {
                    foreach (var scanlator in langData.scanlators)
                    {
                        if (scanlator != null && !string.IsNullOrWhiteSpace(scanlator.id))
                        {
                            aggregatedScanlators[scanlator.id] = scanlator;
                        }
                    }
                }
            }
        }
        
        _logger.LogTrace(
            "Aggregated scanlators: {InitialCount} from series, {TotalCount} total after localized",
            initialCount, aggregatedScanlators.Count);
        
        return aggregatedScanlators.Count > 0 ? aggregatedScanlators.Values.ToArray() : null;
    }
    
    #endregion
}
