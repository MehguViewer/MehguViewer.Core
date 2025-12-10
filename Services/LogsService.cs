using MehguViewer.Core.Helpers;
using System.Collections.Concurrent;
using MehguViewer.Core.Shared;

namespace MehguViewer.Core.Services;

/// <summary>
/// In-memory log storage service with circular buffer management.
/// </summary>
/// <remarks>
/// Provides temporary storage for application logs with automatic size management.
/// Uses a circular buffer pattern to maintain only the most recent entries.
/// Ideal for debugging and real-time log viewing without disk I/O overhead.
/// 
/// Note: Logs are not persisted and will be lost on application restart.
/// For production systems requiring log persistence, integrate with a dedicated
/// logging infrastructure (e.g., Serilog, Application Insights).
/// </remarks>
public sealed class LogsService
{
    #region Constants

    /// <summary>
    /// Default maximum number of log entries to retain in memory.
    /// </summary>
    private const int DefaultMaxEntries = 500;

    /// <summary>
    /// Default number of log entries to return when retrieving logs.
    /// </summary>
    private const int DefaultLogCount = 100;

    /// <summary>
    /// Minimum allowed value for maximum log entries.
    /// </summary>
    private const int MinMaxEntries = 10;

    /// <summary>
    /// Maximum allowed value for maximum log entries to prevent memory exhaustion.
    /// </summary>
    private const int MaxMaxEntries = 10000;

    /// <summary>
    /// Maximum allowed count for log retrieval to prevent performance degradation.
    /// </summary>
    private const int MaxRetrievalCount = 5000;

    #endregion

    #region Fields

    /// <summary>
    /// Thread-safe queue for storing log entries.
    /// </summary>
    private readonly ConcurrentQueue<LogEntry> _logs = new();

    /// <summary>
    /// Maximum number of log entries to retain.
    /// </summary>
    private readonly int _maxEntries;

    /// <summary>
    /// Logger instance for internal logging (optional, may be null to avoid circular dependencies).
    /// </summary>
    private readonly ILogger<LogsService>? _logger;

    /// <summary>
    /// Lock object for thread-safe operations during buffer trimming.
    /// </summary>
    private readonly object _trimLock = new();

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the LogsService.
    /// </summary>
    /// <param name="maxEntries">Maximum number of log entries to retain. Must be between 10 and 10,000.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when maxEntries is outside the allowed range.</exception>
    /// <remarks>
    /// Logger is intentionally not injected to avoid circular dependency with ILoggerFactory.
    /// This service is used by InMemoryLoggerProvider which is part of the logging infrastructure.
    /// </remarks>
    public LogsService(int maxEntries = DefaultMaxEntries)
    {
        // Validate maxEntries to prevent memory issues
        if (maxEntries < MinMaxEntries || maxEntries > MaxMaxEntries)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxEntries),
                maxEntries,
                $"Maximum entries must be between {MinMaxEntries:N0} and {MaxMaxEntries:N0}.");
        }

        _maxEntries = maxEntries;
        _logger = null; // Intentionally null to avoid circular dependency
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Adds a log entry to the in-memory buffer.
    /// </summary>
    /// <param name="entry">The log entry to add.</param>
    /// <remarks>
    /// Automatically trims oldest entries when buffer exceeds maximum capacity.
    /// This operation is thread-safe and optimized for high-throughput scenarios.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when entry is null.</exception>
    public void AddLog(LogEntry entry)
    {
        // Validate input
        ArgumentNullException.ThrowIfNull(entry, nameof(entry));

        try
        {
            // Enqueue the new entry
            _logs.Enqueue(entry);

            // Trim old entries if buffer size exceeded
            TrimExcessEntries();
        }
        catch (Exception ex)
        {
            // Log internal errors without failing
            _logger?.LogError(ex, "Failed to add log entry to buffer");
        }
    }

    /// <summary>
    /// Retrieves recent log entries, optionally filtered by level.
    /// </summary>
    /// <param name="count">Maximum number of log entries to return. Must be between 1 and 5,000.</param>
    /// <param name="level">Optional log level filter (e.g., "Error", "Warning", "Information").</param>
    /// <returns>Collection of log entries in reverse chronological order (newest first).</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when count is outside the allowed range.</exception>
    public IEnumerable<LogEntry> GetLogs(int count = DefaultLogCount, string? level = null)
    {
        // Validate count parameter
        if (count < 1 || count > MaxRetrievalCount)
        {
            _logger?.LogWarning(
                "Invalid log retrieval count requested: {Count}. Clamping to valid range.",
                count);
            
            count = Math.Clamp(count, 1, MaxRetrievalCount);
        }

        try
        {
            // Create snapshot to avoid collection modification issues
            var logsSnapshot = _logs.ToArray();
            
            _logger?.LogDebug(
                "Retrieving {Count} logs from {Total} total entries. Level filter: {Level}",
                count,
                logsSnapshot.Length,
                level ?? "none");

            // Reverse to get newest first
            IEnumerable<LogEntry> result = logsSnapshot.Reverse();

            // Apply level filter if specified
            if (!string.IsNullOrWhiteSpace(level))
            {
                result = result.Where(l => 
                    l.level.Equals(level, StringComparison.OrdinalIgnoreCase));
            }

            // Take requested count
            return result.Take(count);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error retrieving logs");
            return Enumerable.Empty<LogEntry>();
        }
    }

    /// <summary>
    /// Gets the current number of log entries in the buffer.
    /// </summary>
    /// <returns>Total count of stored log entries.</returns>
    public int GetLogCount()
    {
        var count = _logs.Count;
        _logger?.LogTrace("Current log count: {Count}", count);
        return count;
    }

    /// <summary>
    /// Clears all log entries from the buffer.
    /// </summary>
    /// <remarks>
    /// This operation is thread-safe and will completely empty the log buffer.
    /// </remarks>
    public void Clear()
    {
        var previousCount = _logs.Count;
        _logs.Clear();
        
        _logger?.LogInformation(
            "Cleared {Count} log entries from buffer",
            previousCount);
    }

    /// <summary>
    /// Gets log statistics including total count and count by level.
    /// </summary>
    /// <returns>Dictionary containing log level counts.</returns>
    public Dictionary<string, int> GetLogStatistics()
    {
        try
        {
            var logsSnapshot = _logs.ToArray();
            var statistics = logsSnapshot
                .GroupBy(l => l.level)
                .ToDictionary(g => g.Key, g => g.Count());

            statistics["Total"] = logsSnapshot.Length;

            _logger?.LogDebug(
                "Log statistics calculated: {Statistics}",
                string.Join(", ", statistics.Select(kvp => $"{kvp.Key}={kvp.Value}")));

            return statistics;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error calculating log statistics");
            return new Dictionary<string, int> { ["Total"] = 0 };
        }
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Trims excess log entries to maintain buffer size within limits.
    /// </summary>
    /// <remarks>
    /// Uses locking to prevent race conditions during concurrent trimming operations.
    /// </remarks>
    private void TrimExcessEntries()
    {
        // Only trim if we've exceeded the limit
        if (_logs.Count <= _maxEntries)
        {
            return;
        }

        // Use lock to prevent multiple threads from trimming simultaneously
        lock (_trimLock)
        {
            var removedCount = 0;
            
            // Trim excess entries
            while (_logs.Count > _maxEntries && _logs.TryDequeue(out _))
            {
                removedCount++;
            }

            if (removedCount > 0)
            {
                _logger?.LogTrace(
                    "Trimmed {RemovedCount} log entries. Current count: {CurrentCount}",
                    removedCount,
                    _logs.Count);
            }
        }
    }

    #endregion
}

/// <summary>
/// Logger implementation that writes to LogsService in-memory buffer.
/// </summary>
/// <remarks>
/// Implements the standard ILogger interface to integrate with .NET logging infrastructure.
/// Filters logs to Information level and above by default.
/// Thread-safe and optimized for minimal allocation overhead.
/// </remarks>
public sealed class InMemoryLogger : ILogger
{
    #region Fields

    /// <summary>
    /// The logs service instance for writing log entries.
    /// </summary>
    private readonly LogsService _logsService;

    /// <summary>
    /// The category name for this logger (typically the source class name).
    /// </summary>
    private readonly string _categoryName;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the InMemoryLogger.
    /// </summary>
    /// <param name="logsService">The logs service to write entries to.</param>
    /// <param name="categoryName">The category name for log entries (typically the source class name).</param>
    /// <exception cref="ArgumentNullException">Thrown when logsService or categoryName is null.</exception>
    public InMemoryLogger(LogsService logsService, string categoryName)
    {
        ArgumentNullException.ThrowIfNull(logsService, nameof(logsService));
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryName, nameof(categoryName));

        _logsService = logsService;
        _categoryName = categoryName;
    }

    #endregion

    #region ILogger Implementation

    /// <summary>
    /// Writes a log entry to the in-memory buffer.
    /// </summary>
    /// <typeparam name="TState">The type of the state object.</typeparam>
    /// <param name="logLevel">The severity level of the log entry.</param>
    /// <param name="eventId">The event identifier for the log entry.</param>
    /// <param name="state">The state object containing log data.</param>
    /// <param name="exception">The exception associated with the log entry, if any.</param>
    /// <param name="formatter">Function to format the log message.</param>
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        // Early exit if logging is not enabled for this level
        if (!IsEnabled(logLevel))
        {
            return;
        }

        // Validate formatter
        ArgumentNullException.ThrowIfNull(formatter, nameof(formatter));

        try
        {
            // Format the message
            var message = formatter(state, exception);

            // Create log entry
            var entry = new LogEntry(
                DateTime.UtcNow,
                logLevel.ToString(),
                message,
                exception?.ToString(), // Use ToString() for full stack trace
                _categoryName
            );

            // Add to logs service
            _logsService.AddLog(entry);
        }
        catch
        {
            // Silently fail to prevent logging errors from breaking the application
            // Cannot log the error as it would create a circular dependency
        }
    }

    /// <summary>
    /// Determines if logging is enabled for the specified log level.
    /// </summary>
    /// <param name="logLevel">The log level to check.</param>
    /// <returns>True if log level is Information or above; otherwise false.</returns>
    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

    /// <summary>
    /// Begins a logical operation scope (not implemented for in-memory logging).
    /// </summary>
    /// <typeparam name="TState">The type of the state object.</typeparam>
    /// <param name="state">The state object for the scope.</param>
    /// <returns>Always returns null as scopes are not supported.</returns>
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    #endregion
}

/// <summary>
/// Logger provider that creates InMemoryLogger instances for the logging infrastructure.
/// </summary>
/// <remarks>
/// Integrates in-memory logging with the standard .NET logging framework.
/// Allows simultaneous logging to multiple destinations (console, file, in-memory).
/// Thread-safe and designed for use with dependency injection.
/// </remarks>
public sealed class InMemoryLoggerProvider : ILoggerProvider
{
    #region Fields

    /// <summary>
    /// The logs service instance shared across all loggers.
    /// </summary>
    private readonly LogsService _logsService;

    /// <summary>
    /// Indicates whether this provider has been disposed.
    /// </summary>
    private bool _disposed;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the InMemoryLoggerProvider.
    /// </summary>
    /// <param name="logsService">The logs service for storing log entries.</param>
    /// <exception cref="ArgumentNullException">Thrown when logsService is null.</exception>
    public InMemoryLoggerProvider(LogsService logsService)
    {
        ArgumentNullException.ThrowIfNull(logsService, nameof(logsService));
        _logsService = logsService;
    }

    #endregion

    #region ILoggerProvider Implementation

    /// <summary>
    /// Creates a new InMemoryLogger for the specified category.
    /// </summary>
    /// <param name="categoryName">The category name for the logger.</param>
    /// <returns>A new InMemoryLogger instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when categoryName is null or empty.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when provider has been disposed.</exception>
    public ILogger CreateLogger(string categoryName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryName, nameof(categoryName));

        return new InMemoryLogger(_logsService, categoryName);
    }

    /// <summary>
    /// Disposes of the logger provider and releases resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    #endregion
}
