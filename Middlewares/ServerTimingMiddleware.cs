using System.Diagnostics;

namespace MehguViewer.Core.Middlewares;

/// <summary>
/// Middleware that adds Server-Timing headers to HTTP responses for performance monitoring.
/// Tracks the total request duration and logs warnings for slow requests.
/// </summary>
/// <remarks>
/// Performance thresholds:
/// - Debug log: &lt;500ms (normal)
/// - Info log: 500-1000ms (moderate)
/// - Warning log: &gt;1000ms (slow)
/// 
/// The Server-Timing header enables browser DevTools to display backend timing information.
/// </remarks>
public sealed class ServerTimingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ServerTimingMiddleware> _logger;

    // Performance thresholds in milliseconds
    private const long SlowRequestThreshold = 1000;
    private const long ModerateRequestThreshold = 500;

    /// <summary>
    /// Initializes a new instance of the <see cref="ServerTimingMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    /// <param name="logger">Logger instance for performance monitoring.</param>
    /// <exception cref="ArgumentNullException">Thrown when next or logger is null.</exception>
    public ServerTimingMiddleware(RequestDelegate next, ILogger<ServerTimingMiddleware> logger)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Processes the HTTP request and adds Server-Timing headers.
    /// </summary>
    /// <param name="context">The HTTP context for the current request.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        if (context == null)
        {
            _logger.LogError("HttpContext is null in ServerTimingMiddleware");
            throw new ArgumentNullException(nameof(context));
        }

        var stopwatch = Stopwatch.StartNew();
        var path = context.Request.Path;
        var method = context.Request.Method;
        var traceId = context.TraceIdentifier;
        
        _logger.LogTrace("ServerTimingMiddleware started for {Method} {Path} (TraceId: {TraceId})", 
            method, path, traceId);
        
        try
        {
            // Register callback to add Server-Timing header before response is sent
            context.Response.OnStarting(() =>
            {
                stopwatch.Stop();
                var elapsedMs = stopwatch.ElapsedMilliseconds;
                
                // Add Server-Timing header for performance monitoring
                // Format: Server-Timing: total;dur=123;desc="Total request time"
                context.Response.Headers.Append(
                    "Server-Timing", 
                    $"total;dur={elapsedMs};desc=\"Total request time\"");
                
                // Log based on performance thresholds
                if (elapsedMs > SlowRequestThreshold)
                {
                    _logger.LogWarning(
                        "Slow request detected: {Method} {Path} took {Duration}ms (TraceId: {TraceId})", 
                        method, path, elapsedMs, traceId);
                }
                else if (elapsedMs > ModerateRequestThreshold)
                {
                    _logger.LogInformation(
                        "Moderate request time: {Method} {Path} took {Duration}ms (TraceId: {TraceId})", 
                        method, path, elapsedMs, traceId);
                }
                else
                {
                    _logger.LogDebug(
                        "Request completed: {Method} {Path} in {Duration}ms (TraceId: {TraceId})", 
                        method, path, elapsedMs, traceId);
                }
                
                return Task.CompletedTask;
            });

            await _next(context);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var elapsedMs = stopwatch.ElapsedMilliseconds;
            
            _logger.LogError(ex, 
                "Request failed: {Method} {Path} after {Duration}ms (TraceId: {TraceId})", 
                method, path, elapsedMs, traceId);
            
            throw;
        }
    }
}
