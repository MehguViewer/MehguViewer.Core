using System.Collections.Concurrent;
using MehguViewer.Shared.Models;

namespace MehguViewer.Core.Backend.Services;

/// <summary>
/// In-memory log storage for viewing recent application logs.
/// Stores the most recent logs in a circular buffer.
/// </summary>
public class LogsService : ILogger
{
    private readonly ConcurrentQueue<LogEntry> _logs = new();
    private readonly int _maxEntries;
    
    public LogsService(int maxEntries = 500)
    {
        _maxEntries = maxEntries;
    }
    
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;
        
        var entry = new LogEntry(
            DateTime.UtcNow,
            logLevel.ToString(),
            formatter(state, exception),
            exception?.Message
        );
        
        _logs.Enqueue(entry);
        
        // Trim old entries
        while (_logs.Count > _maxEntries)
        {
            _logs.TryDequeue(out _);
        }
    }
    
    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;
    
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    
    public IEnumerable<LogEntry> GetLogs(int count = 100, string? level = null)
    {
        var logs = _logs.ToArray().Reverse();
        
        if (!string.IsNullOrEmpty(level))
        {
            logs = logs.Where(l => l.level.Equals(level, StringComparison.OrdinalIgnoreCase));
        }
        
        return logs.Take(count);
    }
    
    public int GetLogCount() => _logs.Count;
    
    public void Clear() => _logs.Clear();
}

/// <summary>
/// Logger provider that writes to both console and in-memory storage.
/// </summary>
public class InMemoryLoggerProvider : ILoggerProvider
{
    private readonly LogsService _logsService;
    
    public InMemoryLoggerProvider(LogsService logsService)
    {
        _logsService = logsService;
    }
    
    public ILogger CreateLogger(string categoryName) => _logsService;
    
    public void Dispose() { }
}
