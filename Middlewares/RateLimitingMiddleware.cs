using System.Collections.Concurrent;
using System.Security.Claims;
using MehguViewer.Core.Shared;
using System.Text.Json;

namespace MehguViewer.Core.Middlewares;

/// <summary>
/// Middleware that implements sliding window rate limiting to protect against abuse.
/// Rate limits are tracked per authenticated user or IP address.
/// </summary>
/// <remarks>
/// Default policy: 100 requests per 60-second window
/// 
/// Response headers added:
/// - X-RateLimit-Limit: Maximum requests allowed in the window
/// - X-RateLimit-Remaining: Requests remaining in current window
/// - X-RateLimit-Reset: Unix timestamp when the window resets
/// 
/// When rate limit is exceeded:
/// - Returns 429 Too Many Requests with RFC 7807 Problem Details
/// - Includes Retry-After header indicating seconds to wait
/// </remarks>
public sealed class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RateLimitingMiddleware> _logger;
    private readonly ConcurrentDictionary<string, RequestLog> _clients = new();
    
    // Rate limiting policy configuration
    private const int Limit = 100;
    private const int WindowSeconds = 60;
    private const int ApproachingLimitThreshold = 10;

    /// <summary>
    /// Initializes a new instance of the <see cref="RateLimitingMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    /// <param name="logger">Logger instance for rate limiting events.</param>
    /// <exception cref="ArgumentNullException">Thrown when next or logger is null.</exception>
    public RateLimitingMiddleware(RequestDelegate next, ILogger<RateLimitingMiddleware> logger)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Processes the HTTP request and enforces rate limiting.
    /// </summary>
    /// <param name="context">The HTTP context for the current request.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        if (context == null)
        {
            _logger.LogError("HttpContext is null in RateLimitingMiddleware");
            throw new ArgumentNullException(nameof(context));
        }

        var key = GetClientKey(context);
        var now = DateTimeOffset.UtcNow;
        var traceId = context.TraceIdentifier;

        _logger.LogTrace("Processing rate limit for client {Key} (TraceId: {TraceId})", key, traceId);

        // Update or create request log for this client
        var log = _clients.AddOrUpdate(
            key, 
            _ => 
            {
                _logger.LogDebug("Initializing rate limit tracking for client {Key} (TraceId: {TraceId})", 
                    key, traceId);
                return new RequestLog { Count = 1, WindowStart = now };
            },
            (_, existingLog) => 
            {
                // Check if current window has expired
                if ((now - existingLog.WindowStart).TotalSeconds > WindowSeconds)
                {
                    _logger.LogDebug("Rate limit window reset for client {Key} (TraceId: {TraceId})", 
                        key, traceId);
                    return new RequestLog { Count = 1, WindowStart = now };
                }
                
                // Increment count in existing window
                existingLog.Count++;
                return existingLog;
            });

        // Calculate rate limit headers
        var remaining = Math.Max(0, Limit - log.Count);
        var reset = log.WindowStart.AddSeconds(WindowSeconds).ToUnixTimeSeconds();

        // Add rate limit headers to response
        context.Response.Headers.Append("X-RateLimit-Limit", Limit.ToString());
        context.Response.Headers.Append("X-RateLimit-Remaining", remaining.ToString());
        context.Response.Headers.Append("X-RateLimit-Reset", reset.ToString());

        // Check if rate limit exceeded
        if (log.Count > Limit)
        {
            _logger.LogWarning(
                "Rate limit exceeded for client {Key}: {Count}/{Limit} requests in {Window}s window. " +
                "Path: {Path}, Method: {Method} (TraceId: {TraceId})",
                key, log.Count, Limit, WindowSeconds, context.Request.Path, context.Request.Method, traceId);
            
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers.Append("Retry-After", WindowSeconds.ToString());
            context.Response.ContentType = "application/problem+json";
            
            var problem = new Problem(
                "urn:mvn:error:rate-limit-exceeded",
                "Too Many Requests",
                429,
                $"Rate limit of {Limit} requests per {WindowSeconds} seconds exceeded. Please retry after {WindowSeconds} seconds.",
                context.Request.Path.Value ?? "/"
            );
            
            await context.Response.WriteAsync(
                JsonSerializer.Serialize(problem, AppJsonSerializerContext.Default.Problem));
            
            return;
        }

        // Warn when approaching rate limit
        if (remaining <= ApproachingLimitThreshold && remaining > 0)
        {
            _logger.LogInformation(
                "Client {Key} approaching rate limit: {Remaining}/{Limit} requests remaining (TraceId: {TraceId})", 
                key, remaining, Limit, traceId);
        }

        _logger.LogTrace(
            "Rate limit check passed for client {Key}: {Count}/{Limit} requests (TraceId: {TraceId})", 
            key, log.Count, Limit, traceId);

        await _next(context);
    }

    /// <summary>
    /// Determines the client identifier for rate limiting.
    /// Prefers authenticated user ID over IP address.
    /// </summary>
    /// <param name="context">The HTTP context containing user and connection information.</param>
    /// <returns>A unique identifier for the client (user ID or IP address).</returns>
    private string GetClientKey(HttpContext context)
    {
        // Prefer User ID if authenticated for more accurate tracking
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                ?? context.User.FindFirst("sub")?.Value
                ?? context.User.Identity.Name 
                ?? "unknown_user";
            
            _logger.LogTrace("Rate limiting by authenticated user ID: {UserId}", userId);
            return $"user:{userId}";
        }
        
        // Fall back to IP address for unauthenticated requests
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown_ip";
        _logger.LogTrace("Rate limiting by IP address: {IP}", ip);
        return $"ip:{ip}";
    }

    /// <summary>
    /// Represents the request tracking data for a client in a time window.
    /// </summary>
    private sealed class RequestLog
    {
        /// <summary>
        /// Gets or sets the number of requests in the current window.
        /// </summary>
        public int Count { get; set; }
        
        /// <summary>
        /// Gets or sets the start time of the current window.
        /// </summary>
        public DateTimeOffset WindowStart { get; set; }
    }
}
