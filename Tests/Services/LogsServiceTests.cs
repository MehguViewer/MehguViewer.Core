using MehguViewer.Core.Services;
using MehguViewer.Core.Workers;
using MehguViewer.Core.Helpers;
using MehguViewer.Core.Shared;
using Microsoft.Extensions.Logging;
using Xunit;

namespace MehguViewer.Core.Tests.Services;

/// <summary>
/// Unit tests for LogsService class.
/// </summary>
/// <remarks>
/// Validates log storage, retrieval, filtering, and buffer management functionality.
/// Tests cover thread safety, boundary conditions, and error handling.
/// </remarks>
public class LogsServiceTests
{
    #region Constructor Tests

    /// <summary>
    /// Verifies that LogsService initializes correctly with default parameters.
    /// </summary>
    [Fact]
    public void Constructor_WithDefaultParameters_InitializesSuccessfully()
    {
        // Arrange & Act
        var service = new LogsService();

        // Assert
        Assert.NotNull(service);
        Assert.Equal(0, service.GetLogCount());
    }

    /// <summary>
    /// Verifies that LogsService initializes correctly with custom max entries.
    /// </summary>
    [Fact]
    public void Constructor_WithCustomMaxEntries_InitializesSuccessfully()
    {
        // Arrange & Act
        var service = new LogsService(maxEntries: 100);

        // Assert
        Assert.NotNull(service);
        Assert.Equal(0, service.GetLogCount());
    }

    /// <summary>
    /// Verifies that constructor throws when maxEntries is too low.
    /// </summary>
    [Fact]
    public void Constructor_WithTooLowMaxEntries_ThrowsArgumentOutOfRangeException()
    {
        // Arrange, Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new LogsService(maxEntries: 5));
        Assert.Contains("between 10 and 10,000", exception.Message);
    }

    /// <summary>
    /// Verifies that constructor throws when maxEntries is too high.
    /// </summary>
    [Fact]
    public void Constructor_WithTooHighMaxEntries_ThrowsArgumentOutOfRangeException()
    {
        // Arrange, Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new LogsService(maxEntries: 20000));
        Assert.Contains("between 10 and 10,000", exception.Message);
    }

    #endregion

    #region AddLog Tests

    /// <summary>
    /// Verifies that a single log entry can be added successfully.
    /// </summary>
    [Fact]
    public void AddLog_WithValidEntry_AddsSuccessfully()
    {
        // Arrange
        var service = new LogsService();
        var entry = CreateLogEntry("Test message", LogLevel.Information);

        // Act
        service.AddLog(entry);

        // Assert
        Assert.Equal(1, service.GetLogCount());
        var logs = service.GetLogs();
        Assert.Single(logs);
        Assert.Equal("Test message", logs.First().message);
    }

    /// <summary>
    /// Verifies that AddLog throws when entry is null.
    /// </summary>
    [Fact]
    public void AddLog_WithNullEntry_ThrowsArgumentNullException()
    {
        // Arrange
        var service = new LogsService();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => service.AddLog(null!));
    }

    /// <summary>
    /// Verifies that multiple log entries can be added in sequence.
    /// </summary>
    [Fact]
    public void AddLog_WithMultipleEntries_AddsAllSuccessfully()
    {
        // Arrange
        var service = new LogsService();
        var entries = new[]
        {
            CreateLogEntry("Message 1", LogLevel.Information),
            CreateLogEntry("Message 2", LogLevel.Warning),
            CreateLogEntry("Message 3", LogLevel.Error)
        };

        // Act
        foreach (var entry in entries)
        {
            service.AddLog(entry);
        }

        // Assert
        Assert.Equal(3, service.GetLogCount());
        var logs = service.GetLogs(10).ToArray();
        Assert.Equal(3, logs.Length);
    }

    /// <summary>
    /// Verifies that old entries are trimmed when buffer exceeds max capacity.
    /// </summary>
    [Fact]
    public void AddLog_ExceedingMaxEntries_TrimsOldestEntries()
    {
        // Arrange
        var maxEntries = 10;
        var service = new LogsService(maxEntries: maxEntries);

        // Act - Add more entries than max
        for (int i = 0; i < maxEntries + 5; i++)
        {
            service.AddLog(CreateLogEntry($"Message {i}", LogLevel.Information));
        }

        // Assert
        Assert.Equal(maxEntries, service.GetLogCount());
        var logs = service.GetLogs(maxEntries).ToArray();
        
        // Verify newest entries are retained (last 10: messages 5-14)
        // GetLogs returns newest first, so Last() is oldest in the retained set
        Assert.Contains("Message 14", logs.First().message); // Newest
        Assert.Contains("Message 5", logs.Last().message);  // Oldest retained
    }

    /// <summary>
    /// Verifies that AddLog is thread-safe when called concurrently.
    /// </summary>
    [Fact]
    public async Task AddLog_ConcurrentCalls_HandlesThreadSafely()
    {
        // Arrange
        var service = new LogsService(maxEntries: 1000);
        var tasks = new List<Task>();
        var totalEntries = 100;

        // Act - Add logs from multiple threads
        for (int i = 0; i < totalEntries; i++)
        {
            var index = i;
            tasks.Add(Task.Run(() =>
            {
                service.AddLog(CreateLogEntry($"Concurrent message {index}", LogLevel.Information));
            }));
        }

        await Task.WhenAll(tasks);

        // Assert
        Assert.Equal(totalEntries, service.GetLogCount());
    }

    #endregion

    #region GetLogs Tests

    /// <summary>
    /// Verifies that GetLogs returns entries in reverse chronological order.
    /// </summary>
    [Fact]
    public void GetLogs_WithMultipleEntries_ReturnsNewestFirst()
    {
        // Arrange
        var service = new LogsService();
        service.AddLog(CreateLogEntry("First", LogLevel.Information));
        Thread.Sleep(10); // Ensure different timestamps
        service.AddLog(CreateLogEntry("Second", LogLevel.Information));
        Thread.Sleep(10);
        service.AddLog(CreateLogEntry("Third", LogLevel.Information));

        // Act
        var logs = service.GetLogs().ToArray();

        // Assert
        Assert.Equal("Third", logs[0].message);
        Assert.Equal("Second", logs[1].message);
        Assert.Equal("First", logs[2].message);
    }

    /// <summary>
    /// Verifies that GetLogs filters by log level correctly.
    /// </summary>
    [Fact]
    public void GetLogs_WithLevelFilter_ReturnsOnlyMatchingEntries()
    {
        // Arrange
        var service = new LogsService();
        service.AddLog(CreateLogEntry("Info 1", LogLevel.Information));
        service.AddLog(CreateLogEntry("Error 1", LogLevel.Error));
        service.AddLog(CreateLogEntry("Info 2", LogLevel.Information));
        service.AddLog(CreateLogEntry("Warning 1", LogLevel.Warning));

        // Act
        var errorLogs = service.GetLogs(level: "Error").ToArray();
        var infoLogs = service.GetLogs(level: "Information").ToArray();

        // Assert
        Assert.Single(errorLogs);
        Assert.Equal("Error 1", errorLogs[0].message);
        Assert.Equal(2, infoLogs.Length);
    }

    /// <summary>
    /// Verifies that GetLogs respects the count parameter.
    /// </summary>
    [Fact]
    public void GetLogs_WithCountLimit_ReturnsCorrectNumber()
    {
        // Arrange
        var service = new LogsService();
        for (int i = 0; i < 10; i++)
        {
            service.AddLog(CreateLogEntry($"Message {i}", LogLevel.Information));
        }

        // Act
        var logs = service.GetLogs(count: 5).ToArray();

        // Assert
        Assert.Equal(5, logs.Length);
    }

    /// <summary>
    /// Verifies that GetLogs handles invalid count by clamping to valid range.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    [InlineData(10000)]
    public void GetLogs_WithInvalidCount_ClampsToValidRange(int invalidCount)
    {
        // Arrange
        var service = new LogsService();
        service.AddLog(CreateLogEntry("Test", LogLevel.Information));

        // Act
        var logs = service.GetLogs(count: invalidCount).ToArray();

        // Assert - Should not throw and should return results
        Assert.NotNull(logs);
    }

    /// <summary>
    /// Verifies that GetLogs returns empty collection when no logs exist.
    /// </summary>
    [Fact]
    public void GetLogs_WithNoEntries_ReturnsEmptyCollection()
    {
        // Arrange
        var service = new LogsService();

        // Act
        var logs = service.GetLogs();

        // Assert
        Assert.Empty(logs);
    }

    /// <summary>
    /// Verifies that GetLogs handles case-insensitive level filtering.
    /// </summary>
    [Theory]
    [InlineData("error")]
    [InlineData("ERROR")]
    [InlineData("Error")]
    public void GetLogs_WithCaseInsensitiveLevel_FiltersCorrectly(string levelFilter)
    {
        // Arrange
        var service = new LogsService();
        service.AddLog(CreateLogEntry("Error message", LogLevel.Error));
        service.AddLog(CreateLogEntry("Info message", LogLevel.Information));

        // Act
        var logs = service.GetLogs(level: levelFilter).ToArray();

        // Assert
        Assert.Single(logs);
        Assert.Equal("Error message", logs[0].message);
    }

    #endregion

    #region Clear Tests

    /// <summary>
    /// Verifies that Clear removes all log entries.
    /// </summary>
    [Fact]
    public void Clear_WithEntries_RemovesAllLogs()
    {
        // Arrange
        var service = new LogsService();
        for (int i = 0; i < 10; i++)
        {
            service.AddLog(CreateLogEntry($"Message {i}", LogLevel.Information));
        }

        // Act
        service.Clear();

        // Assert
        Assert.Equal(0, service.GetLogCount());
        Assert.Empty(service.GetLogs());
    }

    /// <summary>
    /// Verifies that Clear works correctly on empty buffer.
    /// </summary>
    [Fact]
    public void Clear_WithNoEntries_CompletesSuccessfully()
    {
        // Arrange
        var service = new LogsService();

        // Act
        service.Clear();

        // Assert
        Assert.Equal(0, service.GetLogCount());
    }

    #endregion

    #region GetLogStatistics Tests

    /// <summary>
    /// Verifies that GetLogStatistics returns correct counts by level.
    /// </summary>
    [Fact]
    public void GetLogStatistics_WithMultipleLevels_ReturnsCorrectCounts()
    {
        // Arrange
        var service = new LogsService();
        service.AddLog(CreateLogEntry("Info 1", LogLevel.Information));
        service.AddLog(CreateLogEntry("Info 2", LogLevel.Information));
        service.AddLog(CreateLogEntry("Error 1", LogLevel.Error));
        service.AddLog(CreateLogEntry("Warning 1", LogLevel.Warning));
        service.AddLog(CreateLogEntry("Warning 2", LogLevel.Warning));
        service.AddLog(CreateLogEntry("Warning 3", LogLevel.Warning));

        // Act
        var stats = service.GetLogStatistics();

        // Assert
        Assert.Equal(6, stats["Total"]);
        Assert.Equal(2, stats["Information"]);
        Assert.Equal(1, stats["Error"]);
        Assert.Equal(3, stats["Warning"]);
    }

    /// <summary>
    /// Verifies that GetLogStatistics returns empty stats for empty buffer.
    /// </summary>
    [Fact]
    public void GetLogStatistics_WithNoEntries_ReturnsZeroTotal()
    {
        // Arrange
        var service = new LogsService();

        // Act
        var stats = service.GetLogStatistics();

        // Assert
        Assert.Equal(0, stats["Total"]);
    }

    #endregion

    #region InMemoryLogger Tests

    /// <summary>
    /// Verifies that InMemoryLogger writes to LogsService correctly.
    /// </summary>
    [Fact]
    public void InMemoryLogger_Log_WritesToLogsService()
    {
        // Arrange
        var service = new LogsService();
        var logger = new InMemoryLogger(service, "TestCategory");

        // Act
        logger.LogInformation("Test message");

        // Assert
        Assert.Equal(1, service.GetLogCount());
        var logs = service.GetLogs().ToArray();
        Assert.Equal("Test message", logs[0].message);
        Assert.Equal("TestCategory", logs[0].category);
    }

    /// <summary>
    /// Verifies that InMemoryLogger respects log level filtering.
    /// </summary>
    [Fact]
    public void InMemoryLogger_IsEnabled_FiltersLowLevels()
    {
        // Arrange
        var service = new LogsService();
        var logger = new InMemoryLogger(service, "TestCategory");

        // Act
        logger.LogTrace("Trace message");
        logger.LogDebug("Debug message");
        logger.LogInformation("Info message");

        // Assert
        Assert.Equal(1, service.GetLogCount()); // Only Information should be logged
    }

    /// <summary>
    /// Verifies that InMemoryLogger captures exception details.
    /// </summary>
    [Fact]
    public void InMemoryLogger_LogWithException_CapturesExceptionDetails()
    {
        // Arrange
        var service = new LogsService();
        var logger = new InMemoryLogger(service, "TestCategory");
        var exception = new InvalidOperationException("Test exception");

        // Act
        logger.LogError(exception, "Error occurred");

        // Assert
        var logs = service.GetLogs().ToArray();
        Assert.Single(logs);
        Assert.Contains("Test exception", logs[0].exception ?? "");
    }

    /// <summary>
    /// Verifies that InMemoryLogger constructor validates parameters.
    /// </summary>
    [Fact]
    public void InMemoryLogger_Constructor_WithNullService_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new InMemoryLogger(null!, "TestCategory"));
    }

    /// <summary>
    /// Verifies that InMemoryLogger constructor validates category name.
    /// </summary>
    [Fact]
    public void InMemoryLogger_Constructor_WithNullCategory_ThrowsArgumentException()
    {
        // Arrange
        var service = new LogsService();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new InMemoryLogger(service, null!));
    }

    #endregion

    #region InMemoryLoggerProvider Tests

    /// <summary>
    /// Verifies that InMemoryLoggerProvider creates loggers correctly.
    /// </summary>
    [Fact]
    public void InMemoryLoggerProvider_CreateLogger_ReturnsValidLogger()
    {
        // Arrange
        var service = new LogsService();
        var provider = new InMemoryLoggerProvider(service);

        // Act
        var logger = provider.CreateLogger("TestCategory");

        // Assert
        Assert.NotNull(logger);
        Assert.IsType<InMemoryLogger>(logger);
    }

    /// <summary>
    /// Verifies that InMemoryLoggerProvider validates constructor parameters.
    /// </summary>
    [Fact]
    public void InMemoryLoggerProvider_Constructor_WithNullService_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new InMemoryLoggerProvider(null!));
    }

    /// <summary>
    /// Verifies that InMemoryLoggerProvider can be disposed safely.
    /// </summary>
    [Fact]
    public void InMemoryLoggerProvider_Dispose_CompletesSuccessfully()
    {
        // Arrange
        var service = new LogsService();
        var provider = new InMemoryLoggerProvider(service);

        // Act & Assert - Should not throw
        provider.Dispose();
        provider.Dispose(); // Double dispose should be safe
    }

    /// <summary>
    /// Verifies that disposed provider throws when creating loggers.
    /// </summary>
    [Fact]
    public void InMemoryLoggerProvider_CreateLogger_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        var service = new LogsService();
        var provider = new InMemoryLoggerProvider(service);
        provider.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() => provider.CreateLogger("TestCategory"));
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Creates a log entry for testing purposes.
    /// </summary>
    /// <param name="message">The log message.</param>
    /// <param name="level">The log level.</param>
    /// <returns>A new LogEntry instance.</returns>
    private static LogEntry CreateLogEntry(string message, LogLevel level)
    {
        return new LogEntry(
            DateTime.UtcNow,
            level.ToString(),
            message,
            null,
            "TestCategory"
        );
    }

    #endregion
}
