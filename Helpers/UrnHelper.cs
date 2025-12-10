using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace MehguViewer.Core.Helpers;

/// <summary>
/// Provides utility methods for creating, parsing, and validating Uniform Resource Names (URNs)
/// used throughout the MehguViewer system.
/// </summary>
/// <remarks>
/// <para>URN Format: urn:{namespace}:{type}:{id}</para>
/// 
/// <para>Supported Namespaces:</para>
/// <list type="bullet">
///   <item><description>mvn (MehguViewer): Internal resources (series, units, users, etc.)</description></item>
///   <item><description>src (Source): External/federated resources</description></item>
/// </list>
/// 
/// <para>Examples:</para>
/// <list type="bullet">
///   <item><description>urn:mvn:series:123e4567-e89b-12d3-a456-426614174000</description></item>
///   <item><description>urn:mvn:user:admin-001</description></item>
///   <item><description>urn:src:mangadex:abc123</description></item>
/// </list>
/// </remarks>
public static class UrnHelper
{
    #region Constants

    private const string MehguNamespace = "mvn";
    private const string SourceNamespace = "src";
    private const string UrnPrefix = "urn";
    private const char UrnDelimiter = ':';
    private const int MinUrnParts = 3;
    private const int MinMvnUrnParts = 4;
    private const int MinSrcUrnParts = 4;
    private const int MaxUrnLength = 512; // Security: Prevent DoS via excessive URN length
    private const int MaxIdLength = 256;   // Security: Limit ID component length
    
    /// <summary>
    /// Valid resource types for MehguViewer namespace.
    /// </summary>
    private static readonly HashSet<string> ValidMvnTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "series", "unit", "user", "asset", "comment", "error", 
        "collection", "tag", "annotation", "session"
    };
    
    /// <summary>
    /// Regex pattern for validating URN component characters (alphanumeric, hyphen, underscore).
    /// Security: Prevents injection attacks and ensures RFC compliance.
    /// </summary>
    private static readonly Regex ComponentValidationPattern = 
        new(@"^[a-zA-Z0-9_\-]+$", RegexOptions.Compiled);

    #endregion

    #region URN Creation

    /// <summary>
    /// Creates a new Series URN with a unique GUID identifier.
    /// </summary>
    /// <returns>A URN string in the format: urn:mvn:series:{guid}</returns>
    public static string CreateSeriesUrn() => CreateMvnUrn("series");
    
    /// <summary>
    /// Creates a new Unit URN with a unique GUID identifier.
    /// </summary>
    /// <returns>A URN string in the format: urn:mvn:unit:{guid}</returns>
    public static string CreateUnitUrn() => CreateMvnUrn("unit");
    
    /// <summary>
    /// Creates a new User URN with a unique GUID identifier.
    /// </summary>
    /// <returns>A URN string in the format: urn:mvn:user:{guid}</returns>
    public static string CreateUserUrn() => CreateMvnUrn("user");
    
    /// <summary>
    /// Creates a new Asset URN with a unique GUID identifier.
    /// </summary>
    /// <returns>A URN string in the format: urn:mvn:asset:{guid}</returns>
    public static string CreateAssetUrn() => CreateMvnUrn("asset");
    
    /// <summary>
    /// Creates a new Comment URN with a unique GUID identifier.
    /// </summary>
    /// <returns>A URN string in the format: urn:mvn:comment:{guid}</returns>
    public static string CreateCommentUrn() => CreateMvnUrn("comment");
    
    /// <summary>
    /// Creates a new Collection URN with a unique GUID identifier.
    /// </summary>
    /// <returns>A URN string in the format: urn:mvn:collection:{guid}</returns>
    public static string CreateCollectionUrn() => CreateMvnUrn("collection");
    
    /// <summary>
    /// Creates a new Tag URN with a unique GUID identifier.
    /// </summary>
    /// <returns>A URN string in the format: urn:mvn:tag:{guid}</returns>
    public static string CreateTagUrn() => CreateMvnUrn("tag");
    
    /// <summary>
    /// Creates an Error URN with a specific error code.
    /// </summary>
    /// <param name="code">The error code to include in the URN (alphanumeric, hyphens, underscores only).</param>
    /// <returns>A URN string in the format: urn:mvn:error:{code}</returns>
    /// <exception cref="ArgumentException">Thrown when code is null, empty, or contains invalid characters.</exception>
    public static string CreateErrorUrn(string code)
    {
        // Input validation
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Error code cannot be null or empty", nameof(code));
        }
        
        // Security: Validate error code format to prevent injection
        if (!ComponentValidationPattern.IsMatch(code))
        {
            throw new ArgumentException(
                $"Error code '{code}' contains invalid characters. Only alphanumeric characters, hyphens, and underscores are allowed.", 
                nameof(code));
        }
        
        // Security: Prevent excessively long error codes
        if (code.Length > MaxIdLength)
        {
            throw new ArgumentException(
                $"Error code exceeds maximum length of {MaxIdLength} characters", 
                nameof(code));
        }
        
        return $"{UrnPrefix}{UrnDelimiter}{MehguNamespace}{UrnDelimiter}error{UrnDelimiter}{code.ToLowerInvariant()}";
    }
    
    /// <summary>
    /// Creates a Source URN for external resources.
    /// </summary>
    /// <param name="source">The source system name (e.g., "mangadex", "anilist").</param>
    /// <param name="id">The resource identifier in the source system.</param>
    /// <returns>A URN string in the format: urn:src:{source}:{id}</returns>
    /// <exception cref="ArgumentException">Thrown when parameters are invalid.</exception>
    public static string CreateSourceUrn(string source, string id)
    {
        // Input validation
        if (string.IsNullOrWhiteSpace(source))
        {
            throw new ArgumentException("Source cannot be null or empty", nameof(source));
        }
        
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("ID cannot be null or empty", nameof(id));
        }
        
        // Security: Validate source format
        if (!ComponentValidationPattern.IsMatch(source))
        {
            throw new ArgumentException(
                $"Source '{source}' contains invalid characters. Only alphanumeric characters, hyphens, and underscores are allowed.", 
                nameof(source));
        }
        
        // Security: Validate total length
        var urnLength = UrnPrefix.Length + SourceNamespace.Length + source.Length + id.Length + 3; // +3 for delimiters
        if (urnLength > MaxUrnLength)
        {
            throw new ArgumentException(
                $"Combined URN length would exceed maximum of {MaxUrnLength} characters", 
                nameof(id));
        }
        
        return $"{UrnPrefix}{UrnDelimiter}{SourceNamespace}{UrnDelimiter}{source.ToLowerInvariant()}{UrnDelimiter}{id}";
    }
    
    /// <summary>
    /// Internal helper to create MehguViewer URNs with GUID identifiers.
    /// </summary>
    /// <param name="type">The resource type.</param>
    /// <returns>A URN string.</returns>
    private static string CreateMvnUrn(string type)
    {
        var guid = Guid.NewGuid().ToString("D"); // Use "D" format for consistency (lowercase, hyphenated)
        return $"{UrnPrefix}{UrnDelimiter}{MehguNamespace}{UrnDelimiter}{type}{UrnDelimiter}{guid}";
    }

    #endregion

    #region URN Validation

    /// <summary>
    /// Validates that a URN is a properly formatted Series URN.
    /// </summary>
    /// <param name="urn">The URN to validate.</param>
    /// <returns>True if the URN is a valid Series URN; otherwise, false.</returns>
    public static bool IsValidSeriesUrn(string? urn) => IsValidUrn(urn, "series");
    
    /// <summary>
    /// Validates that a URN is a properly formatted Unit URN.
    /// </summary>
    /// <param name="urn">The URN to validate.</param>
    /// <returns>True if the URN is a valid Unit URN; otherwise, false.</returns>
    public static bool IsValidUnitUrn(string? urn) => IsValidUrn(urn, "unit");
    
    /// <summary>
    /// Validates that a URN is a properly formatted User URN.
    /// </summary>
    /// <param name="urn">The URN to validate.</param>
    /// <returns>True if the URN is a valid User URN; otherwise, false.</returns>
    public static bool IsValidUserUrn(string? urn) => IsValidUrn(urn, "user");
    
    /// <summary>
    /// Validates that a URN is a properly formatted Asset URN.
    /// </summary>
    /// <param name="urn">The URN to validate.</param>
    /// <returns>True if the URN is a valid Asset URN; otherwise, false.</returns>
    public static bool IsValidAssetUrn(string? urn) => IsValidUrn(urn, "asset");
    
    /// <summary>
    /// Validates that a URN is a properly formatted Collection URN.
    /// </summary>
    /// <param name="urn">The URN to validate.</param>
    /// <returns>True if the URN is a valid Collection URN; otherwise, false.</returns>
    public static bool IsValidCollectionUrn(string? urn) => IsValidUrn(urn, "collection");
    
    /// <summary>
    /// Validates any URN format (both mvn and src namespaces).
    /// </summary>
    /// <param name="urn">The URN to validate.</param>
    /// <returns>True if the URN is valid; otherwise, false.</returns>
    public static bool IsValid(string? urn)
    {
        if (string.IsNullOrWhiteSpace(urn)) 
        {
            return false;
        }
        
        // Security: Check length before processing
        if (urn.Length > MaxUrnLength)
        {
            return false;
        }
        
        try
        {
            _ = Parse(urn);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Validates a URN against an expected type and namespace.
    /// </summary>
    /// <param name="urn">The URN to validate.</param>
    /// <param name="expectedType">The expected resource type (e.g., "series", "unit").</param>
    /// <returns>True if the URN matches the expected type and is valid; otherwise, false.</returns>
    private static bool IsValidUrn(string? urn, string expectedType)
    {
        if (string.IsNullOrWhiteSpace(urn)) 
        {
            return false;
        }
        
        // Security: Check length before processing
        if (urn.Length > MaxUrnLength)
        {
            return false;
        }
        
        try
        {
            var parts = Parse(urn);
            return parts.Namespace == MehguNamespace && 
                   string.Equals(parts.Type, expectedType, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    #endregion

    #region URN Parsing

    /// <summary>
    /// Parses a URN string into its component parts.
    /// </summary>
    /// <param name="urn">The URN string to parse.</param>
    /// <returns>A <see cref="UrnParts"/> record containing the parsed components.</returns>
    /// <exception cref="ArgumentException">Thrown when URN format is invalid.</exception>
    /// <remarks>
    /// <para>Supported formats:</para>
    /// <list type="bullet">
    ///   <item><description>MehguViewer URNs: urn:mvn:{type}:{id}</description></item>
    ///   <item><description>Source URNs: urn:src:{source}:{id}</description></item>
    /// </list>
    /// 
    /// <para>The ID portion may contain colons, which will be preserved.</para>
    /// </remarks>
    public static UrnParts Parse(string urn)
    {
        // Input validation
        if (string.IsNullOrWhiteSpace(urn)) 
        {
            throw new ArgumentException("URN cannot be null or empty", nameof(urn));
        }

        // Security: Prevent DoS attacks via excessively long URNs
        if (urn.Length > MaxUrnLength)
        {
            throw new ArgumentException(
                $"URN exceeds maximum length of {MaxUrnLength} characters", 
                nameof(urn));
        }

        var parts = urn.Split(UrnDelimiter);
        
        // Basic validation - must start with "urn" and have at least 3 parts
        if (parts.Length < MinUrnParts || !string.Equals(parts[0], UrnPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Invalid URN format: {urn}. Expected format: urn:{{namespace}}:{{type}}:{{id}}", 
                nameof(urn));
        }

        var namespaceValue = parts[1].ToLowerInvariant();

        // Parse based on namespace
        return namespaceValue switch
        {
            SourceNamespace => ParseSourceUrn(urn, parts),
            MehguNamespace => ParseMehguViewerUrn(urn, parts),
            _ => throw new ArgumentException(
                $"Unknown URN namespace: {namespaceValue}. Supported namespaces: '{MehguNamespace}', '{SourceNamespace}'", 
                nameof(urn))
        };
    }
    
    /// <summary>
    /// Attempts to parse a URN string without throwing exceptions.
    /// </summary>
    /// <param name="urn">The URN string to parse.</param>
    /// <param name="parts">The parsed URN components if successful.</param>
    /// <returns>True if parsing succeeded; otherwise, false.</returns>
    public static bool TryParse(string? urn, out UrnParts? parts)
    {
        parts = null;
        
        if (string.IsNullOrWhiteSpace(urn))
        {
            return false;
        }
        
        try
        {
            parts = Parse(urn);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Parses a Source URN (urn:src:{source}:{id}).
    /// </summary>
    /// <param name="urn">The original URN string for error reporting.</param>
    /// <param name="parts">The split URN parts.</param>
    /// <returns>A <see cref="UrnParts"/> record.</returns>
    /// <exception cref="ArgumentException">Thrown when Source URN format is invalid.</exception>
    private static UrnParts ParseSourceUrn(string urn, string[] parts)
    {
        if (parts.Length < MinSrcUrnParts) 
        {
            throw new ArgumentException(
                $"Invalid Source URN: {urn}. Expected format: urn:src:{{source}}:{{id}}", 
                nameof(urn));
        }
        
        var source = parts[2];
        
        // Security: Validate source component
        if (string.IsNullOrWhiteSpace(source))
        {
            throw new ArgumentException($"Invalid Source URN: {urn}. Source cannot be empty", nameof(urn));
        }
        
        if (!ComponentValidationPattern.IsMatch(source))
        {
            throw new ArgumentException(
                $"Invalid Source URN: {urn}. Source '{source}' contains invalid characters", 
                nameof(urn));
        }
        
        // Join remaining parts in case ID contains colons
        var id = string.Join(UrnDelimiter.ToString(), parts.Skip(3));
        
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException($"Invalid Source URN: {urn}. ID cannot be empty", nameof(urn));
        }
        
        // Security: Validate ID length
        if (id.Length > MaxIdLength)
        {
            throw new ArgumentException(
                $"Invalid Source URN: {urn}. ID exceeds maximum length of {MaxIdLength} characters", 
                nameof(urn));
        }
        
        return new UrnParts(SourceNamespace, source.ToLowerInvariant(), id);
    }

    /// <summary>
    /// Parses a MehguViewer URN (urn:mvn:{type}:{id}).
    /// </summary>
    /// <param name="urn">The original URN string for error reporting.</param>
    /// <param name="parts">The split URN parts.</param>
    /// <returns>A <see cref="UrnParts"/> record.</returns>
    /// <exception cref="ArgumentException">Thrown when MehguViewer URN format is invalid.</exception>
    private static UrnParts ParseMehguViewerUrn(string urn, string[] parts)
    {
        if (parts.Length < MinMvnUrnParts) 
        {
            throw new ArgumentException(
                $"Invalid MehguViewer URN: {urn}. Expected format: urn:mvn:{{type}}:{{id}}", 
                nameof(urn));
        }
        
        var type = parts[2];
        var id = string.Join(UrnDelimiter.ToString(), parts.Skip(3));
        
        // Validate type component
        if (string.IsNullOrWhiteSpace(type))
        {
            throw new ArgumentException($"Invalid MehguViewer URN: {urn}. Type cannot be empty", nameof(urn));
        }
        
        // Security: Validate type is in allowed list
        if (!ValidMvnTypes.Contains(type))
        {
            throw new ArgumentException(
                $"Invalid MehguViewer URN: {urn}. Unknown type '{type}'. Valid types: {string.Join(", ", ValidMvnTypes)}", 
                nameof(urn));
        }
        
        // Validate ID component
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException($"Invalid MehguViewer URN: {urn}. ID cannot be empty", nameof(urn));
        }
        
        // Security: Validate ID component characters
        if (!ComponentValidationPattern.IsMatch(id))
        {
            throw new ArgumentException(
                $"Invalid MehguViewer URN: {urn}. ID '{id}' contains invalid characters",
                nameof(urn));
        }
        
        // Security: Validate ID length
        if (id.Length > MaxIdLength)
        {
            throw new ArgumentException(
                $"Invalid MehguViewer URN: {urn}. ID exceeds maximum length of {MaxIdLength} characters", 
                nameof(urn));
        }
        
        return new UrnParts(MehguNamespace, type.ToLowerInvariant(), id);
    }
    
    /// <summary>
    /// Extracts the ID portion from a URN.
    /// </summary>
    /// <param name="urn">The URN to extract the ID from.</param>
    /// <returns>The ID component of the URN.</returns>
    /// <exception cref="ArgumentException">Thrown when URN format is invalid.</exception>
    /// <remarks>
    /// Example: urn:mvn:series:123 returns "123"
    /// </remarks>
    public static string ExtractId(string urn)
    {
        var parts = Parse(urn);
        return parts.Id;
    }
    
    /// <summary>
    /// Attempts to extract the ID portion from a URN without throwing exceptions.
    /// </summary>
    /// <param name="urn">The URN to extract the ID from.</param>
    /// <param name="id">The extracted ID if successful.</param>
    /// <returns>True if extraction succeeded; otherwise, false.</returns>
    public static bool TryExtractId(string? urn, out string? id)
    {
        id = null;
        
        if (!TryParse(urn, out var parts) || parts == null)
        {
            return false;
        }
        
        id = parts.Id;
        return true;
    }
    
    /// <summary>
    /// Extracts the type portion from a MehguViewer URN.
    /// </summary>
    /// <param name="urn">The URN to extract the type from.</param>
    /// <returns>The type component of the URN.</returns>
    /// <exception cref="ArgumentException">Thrown when URN is not a MehguViewer URN or format is invalid.</exception>
    /// <remarks>
    /// <para>Example: urn:mvn:series:123 returns "series"</para>
    /// <para>Only works with MehguViewer URNs (namespace: mvn).</para>
    /// </remarks>
    public static string ExtractType(string urn)
    {
        var parts = Parse(urn);
        
        if (parts.Namespace != MehguNamespace)
        {
            throw new ArgumentException(
                $"Cannot extract type from non-MehguViewer URN: {urn}. Only '{MehguNamespace}' namespace supports type extraction.", 
                nameof(urn));
        }
        
        return parts.Type;
    }
    
    /// <summary>
    /// Attempts to extract the type portion from a MehguViewer URN without throwing exceptions.
    /// </summary>
    /// <param name="urn">The URN to extract the type from.</param>
    /// <param name="type">The extracted type if successful.</param>
    /// <returns>True if extraction succeeded; otherwise, false.</returns>
    public static bool TryExtractType(string? urn, out string? type)
    {
        type = null;
        
        if (!TryParse(urn, out var parts) || parts == null || parts.Namespace != MehguNamespace)
        {
            return false;
        }
        
        type = parts.Type;
        return true;
    }

    #endregion

    #region URN Normalization

    /// <summary>
    /// Normalizes a series ID to proper URN format if not already in URN format.
    /// </summary>
    /// <param name="seriesId">The series ID or URN to normalize.</param>
    /// <returns>A normalized series URN.</returns>
    public static string NormalizeSeriesUrn(string seriesId)
    {
        if (string.IsNullOrWhiteSpace(seriesId))
        {
            throw new ArgumentException("Series ID cannot be null or empty", nameof(seriesId));
        }

        return seriesId.StartsWith("urn:mvn:series:", StringComparison.OrdinalIgnoreCase) 
            ? seriesId 
            : $"urn:mvn:series:{seriesId}";
    }

    /// <summary>
    /// Normalizes a unit ID to proper URN format if not already in URN format.
    /// </summary>
    /// <param name="unitId">The unit ID or URN to normalize.</param>
    /// <returns>A normalized unit URN.</returns>
    public static string NormalizeUnitUrn(string unitId)
    {
        if (string.IsNullOrWhiteSpace(unitId))
        {
            throw new ArgumentException("Unit ID cannot be null or empty", nameof(unitId));
        }

        return unitId.StartsWith("urn:mvn:unit:", StringComparison.OrdinalIgnoreCase) 
            ? unitId 
            : $"urn:mvn:unit:{unitId}";
    }

    /// <summary>
    /// Normalizes a user ID to proper URN format if not already in URN format.
    /// </summary>
    /// <param name="userId">The user ID or URN to normalize.</param>
    /// <returns>A normalized user URN.</returns>
    public static string NormalizeUserUrn(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User ID cannot be null or empty", nameof(userId));
        }

        return userId.StartsWith("urn:mvn:user:", StringComparison.OrdinalIgnoreCase) 
            ? userId 
            : $"urn:mvn:user:{userId}";
    }

    #endregion
}

/// <summary>
/// Represents the parsed components of a URN.
/// </summary>
/// <param name="Namespace">The URN namespace (e.g., "mvn" for MehguViewer, "src" for Source).</param>
/// <param name="Type">The resource type (e.g., "series", "unit", "user") or source name for Source URNs.</param>
/// <param name="Id">The unique identifier for the resource.</param>
public record UrnParts(string Namespace, string Type, string Id);
