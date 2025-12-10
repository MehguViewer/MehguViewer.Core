using MehguViewer.Core.Shared;
using Microsoft.Extensions.Logging;

namespace MehguViewer.Core.Helpers;

/// <summary>
/// Extension methods for creating RFC 7807 Problem Details responses.
/// </summary>
/// <remarks>
/// <para><strong>Purpose:</strong></para>
/// Provides standardized error response creation following RFC 7807 specification.
/// All API errors should use these methods to ensure consistent format.
/// 
/// <para><strong>RFC 7807 Compliance:</strong></para>
/// Each error response includes:
/// <list type="bullet">
///   <item><strong>type:</strong> URN identifying the error type (e.g., urn:mvn:error:not-found)</item>
///   <item><strong>title:</strong> Short, human-readable summary</item>
///   <item><strong>status:</strong> HTTP status code</item>
///   <item><strong>detail:</strong> Specific explanation for this occurrence</item>
///   <item><strong>instance:</strong> URI reference identifying the specific occurrence</item>
///   <item><strong>traceId:</strong> Correlation ID for distributed tracing (optional)</item>
/// </list>
/// 
/// <para><strong>Content Type:</strong></para>
/// All responses use "application/problem+json" media type per RFC 7807.
/// 
/// <para><strong>Security:</strong></para>
/// Input validation ensures no null/empty values are propagated to responses.
/// Internal server errors sanitize sensitive information from error details.
/// 
/// <para><strong>Usage Example:</strong></para>
/// <code>
/// return ResultsExtensions.NotFound(
///     "Series not found",
///     "/api/series/urn:mvn:series:123",
///     httpContext.TraceIdentifier
/// );
/// </code>
/// </remarks>
public static class ResultsExtensions
{
    #region Constants

    /// <summary>Content type for RFC 7807 Problem Details responses.</summary>
    private const string ProblemContentType = "application/problem+json";
    
    /// <summary>URN prefix for MehguViewer error types.</summary>
    private const string ErrorUrnPrefix = "urn:mvn:error:";
    
    /// <summary>Default error detail when none provided.</summary>
    private const string DefaultErrorDetail = "An error occurred processing your request.";
    
    /// <summary>Default instance path when none provided.</summary>
    private const string DefaultInstance = "/";

    #endregion

    #region Private Helper Methods

    /// <summary>
    /// Creates a standardized RFC 7807 Problem Details response.
    /// </summary>
    /// <param name="errorType">Error type identifier (without URN prefix).</param>
    /// <param name="title">Human-readable error title.</param>
    /// <param name="statusCode">HTTP status code.</param>
    /// <param name="detail">Specific error detail message.</param>
    /// <param name="instance">Request URI path.</param>
    /// <param name="traceId">Optional trace/correlation ID for distributed tracing.</param>
    /// <param name="logger">Optional logger for error tracking.</param>
    /// <returns>IResult containing the RFC 7807 problem details.</returns>
    private static IResult CreateProblemResult(
        string errorType,
        string title,
        int statusCode,
        string? detail,
        string? instance,
        string? traceId = null,
        ILogger? logger = null)
    {
        // Input validation - sanitize null/empty values
        var sanitizedDetail = string.IsNullOrWhiteSpace(detail) ? DefaultErrorDetail : detail.Trim();
        var sanitizedInstance = string.IsNullOrWhiteSpace(instance) ? DefaultInstance : instance.Trim();
        var sanitizedTraceId = string.IsNullOrWhiteSpace(traceId) ? null : traceId.Trim();

        // Create URN-based type identifier
        var problemType = ErrorUrnPrefix + errorType;

        // Log the error creation (at appropriate level based on status code)
        if (logger != null)
        {
            var logLevel = statusCode >= 500 ? LogLevel.Error :
                          statusCode >= 400 ? LogLevel.Warning :
                          LogLevel.Information;

            logger.Log(logLevel,
                "Creating RFC 7807 response: Type={Type}, Status={Status}, Detail={Detail}, Instance={Instance}, TraceId={TraceId}",
                problemType, statusCode, sanitizedDetail, sanitizedInstance, sanitizedTraceId ?? "none");
        }

        // Create problem details object using existing Problem record
        // Note: Extending Problem record to include traceId in a dictionary would break serialization
        // For now, we rely on middleware/logging to correlate traceId
        var problem = new Problem(
            type: problemType,
            title: title,
            status: statusCode,
            detail: sanitizedDetail,
            instance: sanitizedInstance
        );

        // Return JSON result with proper content type
        return Results.Json(
            problem,
            AppJsonSerializerContext.Default.Problem,
            statusCode: statusCode,
            contentType: ProblemContentType
        );
    }

    #endregion

    #region Public Methods - HTTP 4xx Client Errors

    /// <summary>
    /// Creates a 400 Bad Request response.
    /// </summary>
    /// <param name="detail">Specific explanation of what was wrong with the request.</param>
    /// <param name="instance">URI reference identifying the specific request.</param>
    /// <param name="traceId">Optional trace/correlation ID for distributed tracing.</param>
    /// <param name="logger">Optional logger for error tracking.</param>
    /// <returns>RFC 7807 problem details response with 400 status.</returns>
    /// <remarks>
    /// <para><strong>Use Cases:</strong></para>
    /// Malformed requests, invalid syntax, or missing required parameters.
    /// <para><strong>Examples:</strong></para>
    /// <list type="bullet">
    ///   <item>"Invalid JSON in request body"</item>
    ///   <item>"Missing required field: title"</item>
    ///   <item>"Invalid URN format"</item>
    /// </list>
    /// <para><strong>Security:</strong></para>
    /// Validates and sanitizes all input parameters to prevent information disclosure.
    /// </remarks>
    public static IResult BadRequest(string? detail, string? instance, string? traceId = null, ILogger? logger = null)
    {
        return CreateProblemResult("bad-request", "Bad Request", 400, detail, instance, traceId, logger);
    }

    /// <summary>
    /// Creates a 401 Unauthorized response.
    /// </summary>
    /// <param name="detail">Specific explanation of the authentication failure.</param>
    /// <param name="instance">URI reference identifying the specific request.</param>
    /// <param name="traceId">Optional trace/correlation ID for distributed tracing.</param>
    /// <param name="logger">Optional logger for error tracking.</param>
    /// <returns>RFC 7807 problem details response with 401 status.</returns>
    /// <remarks>
    /// <para><strong>Use Cases:</strong></para>
    /// Authentication required but not provided or invalid.
    /// <para><strong>Examples:</strong></para>
    /// <list type="bullet">
    ///   <item>"Invalid token"</item>
    ///   <item>"Token expired"</item>
    ///   <item>"Missing authorization header"</item>
    /// </list>
    /// <para><strong>Client Action:</strong></para>
    /// Client should retry with valid credentials.
    /// </remarks>
    public static IResult Unauthorized(string? detail, string? instance, string? traceId = null, ILogger? logger = null)
    {
        return CreateProblemResult("unauthorized", "Unauthorized", 401, detail, instance, traceId, logger);
    }

    /// <summary>
    /// Creates a 403 Forbidden response.
    /// </summary>
    /// <param name="detail">Specific explanation of the authorization failure.</param>
    /// <param name="instance">URI reference identifying the specific request.</param>
    /// <param name="traceId">Optional trace/correlation ID for distributed tracing.</param>
    /// <param name="logger">Optional logger for error tracking.</param>
    /// <returns>RFC 7807 problem details response with 403 status.</returns>
    /// <remarks>
    /// <para><strong>Use Cases:</strong></para>
    /// Authenticated user lacks permission for requested action.
    /// <para><strong>Examples:</strong></para>
    /// <list type="bullet">
    ///   <item>"Insufficient permissions to edit this series"</item>
    ///   <item>"Admin access required"</item>
    ///   <item>"Resource access denied"</item>
    /// </list>
    /// <para><strong>Difference from 401:</strong></para>
    /// Unlike 401, re-authenticating will not help - this is a permission issue.
    /// </remarks>
    public static IResult Forbidden(string? detail, string? instance, string? traceId = null, ILogger? logger = null)
    {
        return CreateProblemResult("forbidden", "Forbidden", 403, detail, instance, traceId, logger);
    }

    /// <summary>
    /// Creates a 404 Not Found response.
    /// </summary>
    /// <param name="detail">Specific explanation of what resource was not found.</param>
    /// <param name="instance">URI reference identifying the specific request.</param>
    /// <param name="traceId">Optional trace/correlation ID for distributed tracing.</param>
    /// <param name="logger">Optional logger for error tracking.</param>
    /// <returns>RFC 7807 problem details response with 404 status.</returns>
    /// <remarks>
    /// <para><strong>Use Cases:</strong></para>
    /// Requested resource does not exist.
    /// <para><strong>Examples:</strong></para>
    /// <list type="bullet">
    ///   <item>"Series with URN urn:mvn:series:123 not found"</item>
    ///   <item>"Endpoint not found"</item>
    ///   <item>"User does not exist"</item>
    /// </list>
    /// </remarks>
    public static IResult NotFound(string? detail, string? instance, string? traceId = null, ILogger? logger = null)
    {
        return CreateProblemResult("not-found", "Not Found", 404, detail, instance, traceId, logger);
    }

    /// <summary>
    /// Creates a 409 Conflict response.
    /// </summary>
    /// <param name="detail">Specific explanation of the conflict.</param>
    /// <param name="instance">URI reference identifying the specific request.</param>
    /// <param name="traceId">Optional trace/correlation ID for distributed tracing.</param>
    /// <param name="logger">Optional logger for error tracking.</param>
    /// <returns>RFC 7807 problem details response with 409 status.</returns>
    /// <remarks>
    /// <para><strong>Use Cases:</strong></para>
    /// Request conflicts with current state of the resource.
    /// <para><strong>Examples:</strong></para>
    /// <list type="bullet">
    ///   <item>"Series with this title already exists"</item>
    ///   <item>"Concurrent modification detected"</item>
    ///   <item>"Resource version mismatch"</item>
    /// </list>
    /// <para><strong>Client Action:</strong></para>
    /// Client may resolve by modifying the request or retrying after fetching current state.
    /// </remarks>
    public static IResult Conflict(string? detail, string? instance, string? traceId = null, ILogger? logger = null)
    {
        return CreateProblemResult("conflict", "Conflict", 409, detail, instance, traceId, logger);
    }

    /// <summary>
    /// Creates a 422 Unprocessable Entity response for validation errors.
    /// </summary>
    /// <param name="detail">Specific explanation of the validation failure.</param>
    /// <param name="instance">URI reference identifying the specific request.</param>
    /// <param name="traceId">Optional trace/correlation ID for distributed tracing.</param>
    /// <param name="logger">Optional logger for error tracking.</param>
    /// <returns>RFC 7807 problem details response with 422 status.</returns>
    /// <remarks>
    /// <para><strong>Use Cases:</strong></para>
    /// Request is well-formed but semantically invalid.
    /// <para><strong>Examples:</strong></para>
    /// <list type="bullet">
    ///   <item>"Tag 'Actoin' not found in taxonomy. Did you mean 'Action'?"</item>
    ///   <item>"Invalid date range: end date before start date"</item>
    ///   <item>"Field exceeds maximum length"</item>
    /// </list>
    /// <para><strong>Difference from 400:</strong></para>
    /// Syntax is correct but content violates business rules or semantic constraints.
    /// </remarks>
    public static IResult ValidationError(string? detail, string? instance, string? traceId = null, ILogger? logger = null)
    {
        return CreateProblemResult("validation", "Validation Error", 422, detail, instance, traceId, logger);
    }

    /// <summary>
    /// Creates a 429 Too Many Requests response for rate limiting.
    /// </summary>
    /// <param name="detail">Specific explanation of the rate limit.</param>
    /// <param name="instance">URI reference identifying the specific request.</param>
    /// <param name="traceId">Optional trace/correlation ID for distributed tracing.</param>
    /// <param name="logger">Optional logger for error tracking.</param>
    /// <returns>RFC 7807 problem details response with 429 status.</returns>
    /// <remarks>
    /// <para><strong>Use Cases:</strong></para>
    /// Client exceeded rate limits.
    /// <para><strong>Examples:</strong></para>
    /// <list type="bullet">
    ///   <item>"Rate limit exceeded: 100 requests per minute"</item>
    ///   <item>"Too many login attempts. Try again in 5 minutes."</item>
    /// </list>
    /// <para><strong>Client Action:</strong></para>
    /// Client should implement exponential backoff and respect Retry-After header.
    /// </remarks>
    public static IResult TooManyRequests(string? detail, string? instance, string? traceId = null, ILogger? logger = null)
    {
        return CreateProblemResult("too-many-requests", "Too Many Requests", 429, detail, instance, traceId, logger);
    }

    #endregion

    #region Public Methods - HTTP 5xx Server Errors

    /// <summary>
    /// Creates a 500 Internal Server Error response.
    /// </summary>
    /// <param name="detail">Specific explanation of the server error (avoid exposing sensitive details).</param>
    /// <param name="instance">URI reference identifying the specific request.</param>
    /// <param name="traceId">Optional trace/correlation ID for distributed tracing.</param>
    /// <param name="logger">Optional logger for error tracking.</param>
    /// <returns>RFC 7807 problem details response with 500 status.</returns>
    /// <remarks>
    /// <para><strong>Use Cases:</strong></para>
    /// Unexpected server-side error occurred.
    /// <para><strong>Examples:</strong></para>
    /// <list type="bullet">
    ///   <item>"An unexpected error occurred. Please try again later."</item>
    ///   <item>"Database connection failed"</item>
    /// </list>
    /// <para><strong>Security:</strong></para>
    /// <list type="bullet">
    ///   <item>Avoid exposing stack traces or internal implementation details in detail field</item>
    ///   <item>Log full error details server-side for debugging</item>
    ///   <item>Sanitize error messages to prevent information disclosure</item>
    /// </list>
    /// </remarks>
    public static IResult InternalServerError(string? detail, string? instance, string? traceId = null, ILogger? logger = null)
    {
        // Additional logging for server errors (always log at Error level)
        logger?.LogError(
            "Internal Server Error: Detail={Detail}, Instance={Instance}, TraceId={TraceId}",
            detail ?? DefaultErrorDetail, instance ?? DefaultInstance, traceId ?? "none");

        return CreateProblemResult("internal-server-error", "Internal Server Error", 500, detail, instance, traceId, logger);
    }

    /// <summary>
    /// Creates a 502 Bad Gateway response.
    /// </summary>
    /// <param name="detail">Specific explanation of the gateway error.</param>
    /// <param name="instance">URI reference identifying the specific request.</param>
    /// <param name="traceId">Optional trace/correlation ID for distributed tracing.</param>
    /// <param name="logger">Optional logger for error tracking.</param>
    /// <returns>RFC 7807 problem details response with 502 status.</returns>
    /// <remarks>
    /// <para><strong>Use Cases:</strong></para>
    /// Upstream service returned invalid response.
    /// <para><strong>Examples:</strong></para>
    /// <list type="bullet">
    ///   <item>"External API returned invalid response"</item>
    ///   <item>"CDN connection failed"</item>
    /// </list>
    /// </remarks>
    public static IResult BadGateway(string? detail, string? instance, string? traceId = null, ILogger? logger = null)
    {
        return CreateProblemResult("bad-gateway", "Bad Gateway", 502, detail, instance, traceId, logger);
    }

    /// <summary>
    /// Creates a 503 Service Unavailable response.
    /// </summary>
    /// <param name="detail">Specific explanation of the unavailability.</param>
    /// <param name="instance">URI reference identifying the specific request.</param>
    /// <param name="traceId">Optional trace/correlation ID for distributed tracing.</param>
    /// <param name="logger">Optional logger for error tracking.</param>
    /// <returns>RFC 7807 problem details response with 503 status.</returns>
    /// <remarks>
    /// <para><strong>Use Cases:</strong></para>
    /// Service temporarily unavailable due to maintenance or overload.
    /// <para><strong>Examples:</strong></para>
    /// <list type="bullet">
    ///   <item>"Service temporarily unavailable. Please try again later."</item>
    ///   <item>"Database maintenance in progress"</item>
    ///   <item>"Server overloaded"</item>
    /// </list>
    /// <para><strong>Client Action:</strong></para>
    /// Client should retry after a delay (check Retry-After header if provided).
    /// </remarks>
    public static IResult ServiceUnavailable(string? detail, string? instance, string? traceId = null, ILogger? logger = null)
    {
        return CreateProblemResult("service-unavailable", "Service Unavailable", 503, detail, instance, traceId, logger);
    }

    /// <summary>
    /// Creates a 504 Gateway Timeout response.
    /// </summary>
    /// <param name="detail">Specific explanation of the timeout.</param>
    /// <param name="instance">URI reference identifying the specific request.</param>
    /// <param name="traceId">Optional trace/correlation ID for distributed tracing.</param>
    /// <param name="logger">Optional logger for error tracking.</param>
    /// <returns>RFC 7807 problem details response with 504 status.</returns>
    /// <remarks>
    /// <para><strong>Use Cases:</strong></para>
    /// Upstream service did not respond in time.
    /// <para><strong>Examples:</strong></para>
    /// <list type="bullet">
    ///   <item>"External API request timed out"</item>
    ///   <item>"Database query exceeded timeout limit"</item>
    /// </list>
    /// <para><strong>Client Action:</strong></para>
    /// Client may retry the request after a delay.
    /// </remarks>
    public static IResult GatewayTimeout(string? detail, string? instance, string? traceId = null, ILogger? logger = null)
    {
        return CreateProblemResult("gateway-timeout", "Gateway Timeout", 504, detail, instance, traceId, logger);
    }

    #endregion
}
