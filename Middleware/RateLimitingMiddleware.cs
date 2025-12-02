using System.Collections.Concurrent;
using System.Security.Claims;

namespace MehguViewer.Core.Backend.Middleware;

public class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ConcurrentDictionary<string, RequestLog> _clients = new();
    
    // Policy: 100 requests per minute
    private const int Limit = 100;
    private const int WindowSeconds = 60;

    public RateLimitingMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var key = GetClientKey(context);
        var now = DateTimeOffset.UtcNow;
        var resetTime = now.AddSeconds(WindowSeconds).ToUnixTimeSeconds();

        var log = _clients.AddOrUpdate(key, 
            _ => new RequestLog { Count = 1, WindowStart = now },
            (_, l) => 
            {
                if ((now - l.WindowStart).TotalSeconds > WindowSeconds)
                {
                    return new RequestLog { Count = 1, WindowStart = now };
                }
                l.Count++;
                return l;
            });

        var remaining = Math.Max(0, Limit - log.Count);
        var reset = log.WindowStart.AddSeconds(WindowSeconds).ToUnixTimeSeconds();

        context.Response.Headers.Append("X-RateLimit-Limit", Limit.ToString());
        context.Response.Headers.Append("X-RateLimit-Remaining", remaining.ToString());
        context.Response.Headers.Append("X-RateLimit-Reset", reset.ToString());

        if (log.Count > Limit)
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers.Append("Retry-After", WindowSeconds.ToString());
            await context.Response.WriteAsync("Too Many Requests");
            return;
        }

        await _next(context);
    }

    private string GetClientKey(HttpContext context)
    {
        // Prefer User ID if authenticated, otherwise IP
        if (context.User.Identity?.IsAuthenticated == true)
        {
            return context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? context.User.Identity.Name ?? "unknown_user";
        }
        
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown_ip";
    }

    private class RequestLog
    {
        public int Count { get; set; }
        public DateTimeOffset WindowStart { get; set; }
    }
}
