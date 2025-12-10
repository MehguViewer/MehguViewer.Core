using MehguViewer.Core.Shared;
using System.Text.Json;

namespace MehguViewer.Core.Middlewares;

/// <summary>
/// Middleware that intercepts authentication and authorization failures (401/403)
/// and formats them as RFC 7807 Problem Details responses.
/// 
/// This middleware runs after authentication/authorization to ensure consistent
/// error formatting across the MehguViewer Core API.
/// </summary>
/// <remarks>
/// - Converts 401 Unauthorized responses to URN-based Problem Details
/// - Converts 403 Forbidden responses to URN-based Problem Details
/// - Logs all authentication/authorization failures for security monitoring
/// - Must be registered after UseAuthentication() and before UseAuthorization()
/// </remarks>
public sealed class JwtMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<JwtMiddleware> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="JwtMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    /// <param name="logger">Logger instance for tracking authentication failures.</param>
    /// <exception cref="ArgumentNullException">Thrown when next or logger is null.</exception>
    public JwtMiddleware(RequestDelegate next, ILogger<JwtMiddleware> logger)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Processes the HTTP request and converts 401/403 responses to RFC 7807 format.
    /// </summary>
    /// <param name="context">The HTTP context for the current request.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        if (context == null)
        {
            _logger.LogError("HttpContext is null in JwtMiddleware");
            throw new ArgumentNullException(nameof(context));
        }

        var path = context.Request.Path;
        var method = context.Request.Method;
        var traceId = context.TraceIdentifier;
        
        _logger.LogTrace("JwtMiddleware processing request: {Method} {Path} (TraceId: {TraceId})", 
            method, path, traceId);
        
        await _next(context);

        // Handle 401 Unauthorized - Authentication failure
        if (context.Response.StatusCode == 401 && !context.Response.HasStarted)
        {
            var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            
            _logger.LogWarning(
                "Authentication failure: {Method} {Path} from IP {IP} (TraceId: {TraceId})", 
                method, path, ip, traceId);
            
            await WriteErrorAsync(
                context, 
                401, 
                "urn:mvn:error:unauthorized", 
                "Authentication required. Provide a valid JWT token in the Authorization header.");
        }
        // Handle 403 Forbidden - Authorization failure (authenticated but insufficient permissions)
        else if (context.Response.StatusCode == 403 && !context.Response.HasStarted)
        {
            var user = context.User?.Identity?.Name ?? "anonymous";
            var userId = context.User?.FindFirst("sub")?.Value ?? "unknown";
            
            _logger.LogWarning(
                "Authorization failure: User {User} (ID: {UserId}) attempted {Method} {Path} (TraceId: {TraceId})", 
                user, userId, method, path, traceId);
            
            await WriteErrorAsync(
                context, 
                403, 
                "urn:mvn:error:forbidden", 
                "Insufficient permissions to access this resource.");
        }
        else if (context.Response.StatusCode >= 200 && context.Response.StatusCode < 300)
        {
            _logger.LogDebug(
                "Request successful: {Method} {Path} returned {StatusCode} (TraceId: {TraceId})", 
                method, path, context.Response.StatusCode, traceId);
        }
    }

    /// <summary>
    /// Writes an RFC 7807 Problem Details error response to the HTTP context.
    /// </summary>
    /// <param name="ctx">The HTTP context to write the error to.</param>
    /// <param name="status">The HTTP status code.</param>
    /// <param name="type">The URN identifying the error type.</param>
    /// <param name="detail">Human-readable explanation of the error.</param>
    /// <returns>A task representing the asynchronous write operation.</returns>
    private async Task WriteErrorAsync(HttpContext ctx, int status, string type, string detail)
    {
        _logger.LogTrace(
            "Writing RFC 7807 error response: Status {Status}, Type {Type} (TraceId: {TraceId})", 
            status, type, ctx.TraceIdentifier);
        
        ctx.Response.ContentType = "application/problem+json";
        
        var problem = new Problem(
            type,
            status switch 
            { 
                401 => "Unauthorized", 
                403 => "Forbidden", 
                _ => "Error" 
            },
            status,
            detail,
            ctx.Request.Path.Value ?? "/"
        );

        try
        {
            await ctx.Response.WriteAsync(
                JsonSerializer.Serialize(problem, AppJsonSerializerContext.Default.Problem));
            
            _logger.LogInformation(
                "RFC 7807 error response sent: {Status} {Type} (TraceId: {TraceId})", 
                status, type, ctx.TraceIdentifier);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, 
                "Failed to write error response for {Type} (TraceId: {TraceId})", 
                type, ctx.TraceIdentifier);
            throw;
        }
    }
}
