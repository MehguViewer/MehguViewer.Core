using System.Diagnostics;

namespace MehguViewer.Core.Backend.Middleware;

public class ServerTimingMiddleware
{
    private readonly RequestDelegate _next;

    public ServerTimingMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        
        context.Response.OnStarting(() =>
        {
            stopwatch.Stop();
            var elapsedMs = stopwatch.ElapsedMilliseconds;
            // Simple total duration metric
            context.Response.Headers.Append("Server-Timing", $"total;dur={elapsedMs}");
            return Task.CompletedTask;
        });

        await _next(context);
    }
}
