using MehguViewer.Core.Infrastructures;
using MehguViewer.Core.Services;
using MehguViewer.Core.Workers;
using MehguViewer.Core.Helpers;
using MehguViewer.Core.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace MehguViewer.Core.Tests.Helpers;

/// <summary>
/// Comprehensive unit tests for ResultsExtensions covering:
/// - RFC 7807 compliance for all error types
/// - Input validation and sanitization
/// - Logging integration
/// - Security (no information disclosure)
/// - All HTTP status codes (4xx and 5xx)
/// </summary>
/// <remarks>
/// <para><strong>Test Categories:</strong></para>
/// <list type="bullet">
///   <item>RFC 7807 format validation</item>
///   <item>Input sanitization (null, empty, whitespace)</item>
///   <item>Logging behavior verification</item>
///   <item>Security hardening checks</item>
///   <item>Content type validation</item>
/// </list>
/// </remarks>
[Trait("Category", "Unit")]
[Trait("Service", "ResultsExtensions")]
public class ResultsExtensionsTests
{
    private readonly ITestOutputHelper _output;
    private readonly TestLogger<ResultsExtensionsTests> _logger;

    public ResultsExtensionsTests(ITestOutputHelper output)
    {
        _output = output;
        _logger = new TestLogger<ResultsExtensionsTests>(output);
    }

    #region Helper Methods

    /// <summary>
    /// Extracts Problem object from IResult by serializing and deserializing.
    /// </summary>
    private async Task<(Problem? problem, int statusCode, string? contentType)> ExtractProblemFromResult(IResult result)
    {
        var services = new ServiceCollection();
        services.AddLogging(); // Add logging services
        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNameCaseInsensitive = true;
        });
        var serviceProvider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = serviceProvider
        };
        httpContext.Response.Body = new MemoryStream();

        await result.ExecuteAsync(httpContext);

        httpContext.Response.Body.Seek(0, SeekOrigin.Begin);
        var reader = new StreamReader(httpContext.Response.Body);
        var responseBody = await reader.ReadToEndAsync();

        _output.WriteLine($"Response Body: {responseBody}");
        _output.WriteLine($"Status Code: {httpContext.Response.StatusCode}");
        _output.WriteLine($"Content Type: {httpContext.Response.ContentType}");

        var problem = string.IsNullOrWhiteSpace(responseBody) 
            ? null 
            : JsonSerializer.Deserialize<Problem>(responseBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        return (problem, httpContext.Response.StatusCode, httpContext.Response.ContentType);
    }

    #endregion

    #region 4xx Client Error Tests

    /// <summary>
    /// Tests BadRequest (400) creates proper RFC 7807 response.
    /// </summary>
    [Fact]
    public async Task BadRequest_CreatesValidRfc7807Response()
    {
        // Arrange
        var detail = "Invalid JSON in request body";
        var instance = "/api/series";
        var traceId = "trace-123";

        // Act
        var result = ResultsExtensions.BadRequest(detail, instance, traceId, _logger);
        var (problem, statusCode, contentType) = await ExtractProblemFromResult(result);

        // Assert
        Assert.NotNull(problem);
        Assert.Equal("urn:mvn:error:bad-request", problem.type);
        Assert.Equal("Bad Request", problem.title);
        Assert.Equal(400, problem.status);
        Assert.Equal(detail, problem.detail);
        Assert.Equal(instance, problem.instance);
        Assert.Equal(400, statusCode);
        Assert.Equal("application/problem+json", contentType);
    }

    /// <summary>
    /// Tests Unauthorized (401) creates proper RFC 7807 response.
    /// </summary>
    [Fact]
    public async Task Unauthorized_CreatesValidRfc7807Response()
    {
        // Arrange
        var detail = "Token expired";
        var instance = "/api/user/profile";

        // Act
        var result = ResultsExtensions.Unauthorized(detail, instance);
        var (problem, statusCode, contentType) = await ExtractProblemFromResult(result);

        // Assert
        Assert.NotNull(problem);
        Assert.Equal("urn:mvn:error:unauthorized", problem.type);
        Assert.Equal("Unauthorized", problem.title);
        Assert.Equal(401, problem.status);
        Assert.Equal(detail, problem.detail);
        Assert.Equal(instance, problem.instance);
        Assert.Equal(401, statusCode);
        Assert.Equal("application/problem+json", contentType);
    }

    /// <summary>
    /// Tests Forbidden (403) creates proper RFC 7807 response.
    /// </summary>
    [Fact]
    public async Task Forbidden_CreatesValidRfc7807Response()
    {
        // Arrange
        var detail = "Insufficient permissions to edit this series";
        var instance = "/api/series/urn:mvn:series:123";

        // Act
        var result = ResultsExtensions.Forbidden(detail, instance, logger: _logger);
        var (problem, statusCode, contentType) = await ExtractProblemFromResult(result);

        // Assert
        Assert.NotNull(problem);
        Assert.Equal("urn:mvn:error:forbidden", problem.type);
        Assert.Equal("Forbidden", problem.title);
        Assert.Equal(403, problem.status);
        Assert.Equal(detail, problem.detail);
        Assert.Equal(instance, problem.instance);
        Assert.Equal(403, statusCode);
    }

    /// <summary>
    /// Tests NotFound (404) creates proper RFC 7807 response.
    /// </summary>
    [Fact]
    public async Task NotFound_CreatesValidRfc7807Response()
    {
        // Arrange
        var detail = "Series with URN urn:mvn:series:123 not found";
        var instance = "/api/series/urn:mvn:series:123";

        // Act
        var result = ResultsExtensions.NotFound(detail, instance);
        var (problem, statusCode, contentType) = await ExtractProblemFromResult(result);

        // Assert
        Assert.NotNull(problem);
        Assert.Equal("urn:mvn:error:not-found", problem.type);
        Assert.Equal("Not Found", problem.title);
        Assert.Equal(404, problem.status);
        Assert.Equal(detail, problem.detail);
        Assert.Equal(404, statusCode);
    }

    /// <summary>
    /// Tests Conflict (409) creates proper RFC 7807 response.
    /// </summary>
    [Fact]
    public async Task Conflict_CreatesValidRfc7807Response()
    {
        // Arrange
        var detail = "Series with this title already exists";
        var instance = "/api/series";

        // Act
        var result = ResultsExtensions.Conflict(detail, instance);
        var (problem, statusCode, contentType) = await ExtractProblemFromResult(result);

        // Assert
        Assert.NotNull(problem);
        Assert.Equal("urn:mvn:error:conflict", problem.type);
        Assert.Equal("Conflict", problem.title);
        Assert.Equal(409, problem.status);
        Assert.Equal(409, statusCode);
    }

    /// <summary>
    /// Tests ValidationError (422) creates proper RFC 7807 response.
    /// </summary>
    [Fact]
    public async Task ValidationError_CreatesValidRfc7807Response()
    {
        // Arrange
        var detail = "Tag 'Actoin' not found in taxonomy. Did you mean 'Action'?";
        var instance = "/api/series";

        // Act
        var result = ResultsExtensions.ValidationError(detail, instance);
        var (problem, statusCode, contentType) = await ExtractProblemFromResult(result);

        // Assert
        Assert.NotNull(problem);
        Assert.Equal("urn:mvn:error:validation", problem.type);
        Assert.Equal("Validation Error", problem.title);
        Assert.Equal(422, problem.status);
        Assert.Equal(422, statusCode);
    }

    /// <summary>
    /// Tests TooManyRequests (429) creates proper RFC 7807 response.
    /// </summary>
    [Fact]
    public async Task TooManyRequests_CreatesValidRfc7807Response()
    {
        // Arrange
        var detail = "Rate limit exceeded: 100 requests per minute";
        var instance = "/api/series";

        // Act
        var result = ResultsExtensions.TooManyRequests(detail, instance);
        var (problem, statusCode, contentType) = await ExtractProblemFromResult(result);

        // Assert
        Assert.NotNull(problem);
        Assert.Equal("urn:mvn:error:too-many-requests", problem.type);
        Assert.Equal("Too Many Requests", problem.title);
        Assert.Equal(429, problem.status);
        Assert.Equal(429, statusCode);
    }

    #endregion

    #region 5xx Server Error Tests

    /// <summary>
    /// Tests InternalServerError (500) creates proper RFC 7807 response.
    /// </summary>
    [Fact]
    public async Task InternalServerError_CreatesValidRfc7807Response()
    {
        // Arrange
        var detail = "An unexpected error occurred. Please try again later.";
        var instance = "/api/series";

        // Act
        var result = ResultsExtensions.InternalServerError(detail, instance, logger: _logger);
        var (problem, statusCode, contentType) = await ExtractProblemFromResult(result);

        // Assert
        Assert.NotNull(problem);
        Assert.Equal("urn:mvn:error:internal-server-error", problem.type);
        Assert.Equal("Internal Server Error", problem.title);
        Assert.Equal(500, problem.status);
        Assert.Equal(500, statusCode);
        
        // Verify error-level logging occurred
        Assert.Contains(_logger.LogEntries, 
            e => e.LogLevel == LogLevel.Error && e.Message.Contains("Internal Server Error"));
    }

    /// <summary>
    /// Tests BadGateway (502) creates proper RFC 7807 response.
    /// </summary>
    [Fact]
    public async Task BadGateway_CreatesValidRfc7807Response()
    {
        // Arrange
        var detail = "External API returned invalid response";
        var instance = "/api/media/proxy";

        // Act
        var result = ResultsExtensions.BadGateway(detail, instance);
        var (problem, statusCode, contentType) = await ExtractProblemFromResult(result);

        // Assert
        Assert.NotNull(problem);
        Assert.Equal("urn:mvn:error:bad-gateway", problem.type);
        Assert.Equal("Bad Gateway", problem.title);
        Assert.Equal(502, problem.status);
        Assert.Equal(502, statusCode);
    }

    /// <summary>
    /// Tests ServiceUnavailable (503) creates proper RFC 7807 response.
    /// </summary>
    [Fact]
    public async Task ServiceUnavailable_CreatesValidRfc7807Response()
    {
        // Arrange
        var detail = "Database maintenance in progress";
        var instance = "/api/series";

        // Act
        var result = ResultsExtensions.ServiceUnavailable(detail, instance);
        var (problem, statusCode, contentType) = await ExtractProblemFromResult(result);

        // Assert
        Assert.NotNull(problem);
        Assert.Equal("urn:mvn:error:service-unavailable", problem.type);
        Assert.Equal("Service Unavailable", problem.title);
        Assert.Equal(503, problem.status);
        Assert.Equal(503, statusCode);
    }

    /// <summary>
    /// Tests GatewayTimeout (504) creates proper RFC 7807 response.
    /// </summary>
    [Fact]
    public async Task GatewayTimeout_CreatesValidRfc7807Response()
    {
        // Arrange
        var detail = "External API request timed out";
        var instance = "/api/metadata";

        // Act
        var result = ResultsExtensions.GatewayTimeout(detail, instance);
        var (problem, statusCode, contentType) = await ExtractProblemFromResult(result);

        // Assert
        Assert.NotNull(problem);
        Assert.Equal("urn:mvn:error:gateway-timeout", problem.type);
        Assert.Equal("Gateway Timeout", problem.title);
        Assert.Equal(504, problem.status);
        Assert.Equal(504, statusCode);
    }

    #endregion

    #region Input Validation Tests

    /// <summary>
    /// Tests that null detail is sanitized to default message.
    /// </summary>
    [Fact]
    public async Task NullDetail_UsesDefaultMessage()
    {
        // Act
        var result = ResultsExtensions.BadRequest(null, "/api/test");
        var (problem, statusCode, contentType) = await ExtractProblemFromResult(result);

        // Assert
        Assert.NotNull(problem);
        Assert.NotNull(problem.detail);
        Assert.NotEmpty(problem.detail);
        Assert.Equal("An error occurred processing your request.", problem.detail);
    }

    /// <summary>
    /// Tests that empty detail is sanitized to default message.
    /// </summary>
    [Fact]
    public async Task EmptyDetail_UsesDefaultMessage()
    {
        // Act
        var result = ResultsExtensions.NotFound("", "/api/test");
        var (problem, statusCode, contentType) = await ExtractProblemFromResult(result);

        // Assert
        Assert.NotNull(problem);
        Assert.Equal("An error occurred processing your request.", problem.detail);
    }

    /// <summary>
    /// Tests that whitespace-only detail is sanitized to default message.
    /// </summary>
    [Fact]
    public async Task WhitespaceDetail_UsesDefaultMessage()
    {
        // Act
        var result = ResultsExtensions.Forbidden("   ", "/api/test");
        var (problem, statusCode, contentType) = await ExtractProblemFromResult(result);

        // Assert
        Assert.NotNull(problem);
        Assert.Equal("An error occurred processing your request.", problem.detail);
    }

    /// <summary>
    /// Tests that null instance is sanitized to default "/".
    /// </summary>
    [Fact]
    public async Task NullInstance_UsesDefaultPath()
    {
        // Act
        var result = ResultsExtensions.BadRequest("Error", null);
        var (problem, statusCode, contentType) = await ExtractProblemFromResult(result);

        // Assert
        Assert.NotNull(problem);
        Assert.Equal("/", problem.instance);
    }

    /// <summary>
    /// Tests that empty instance is sanitized to default "/".
    /// </summary>
    [Fact]
    public async Task EmptyInstance_UsesDefaultPath()
    {
        // Act
        var result = ResultsExtensions.NotFound("Error", "");
        var (problem, statusCode, contentType) = await ExtractProblemFromResult(result);

        // Assert
        Assert.NotNull(problem);
        Assert.Equal("/", problem.instance);
    }

    /// <summary>
    /// Tests that detail with leading/trailing whitespace is trimmed.
    /// </summary>
    [Fact]
    public async Task Detail_WithWhitespace_IsTrimmed()
    {
        // Act
        var result = ResultsExtensions.BadRequest("  Error message  ", "/api/test");
        var (problem, statusCode, contentType) = await ExtractProblemFromResult(result);

        // Assert
        Assert.NotNull(problem);
        Assert.Equal("Error message", problem.detail);
    }

    /// <summary>
    /// Tests that instance with leading/trailing whitespace is trimmed.
    /// </summary>
    [Fact]
    public async Task Instance_WithWhitespace_IsTrimmed()
    {
        // Act
        var result = ResultsExtensions.NotFound("Error", "  /api/test  ");
        var (problem, statusCode, contentType) = await ExtractProblemFromResult(result);

        // Assert
        Assert.NotNull(problem);
        Assert.Equal("/api/test", problem.instance);
    }

    #endregion

    #region Logging Tests

    /// <summary>
    /// Tests that 4xx errors log at Warning level.
    /// </summary>
    [Fact]
    public async Task ClientError_LogsAtWarningLevel()
    {
        // Act
        var result = ResultsExtensions.BadRequest("Invalid request", "/api/test", "trace-123", _logger);
        await ExtractProblemFromResult(result);

        // Assert
        Assert.Contains(_logger.LogEntries, 
            e => e.LogLevel == LogLevel.Warning && e.Message.Contains("RFC 7807"));
    }

    /// <summary>
    /// Tests that 5xx errors log at Error level.
    /// </summary>
    [Fact]
    public async Task ServerError_LogsAtErrorLevel()
    {
        // Act
        var result = ResultsExtensions.InternalServerError("Server error", "/api/test", "trace-123", _logger);
        await ExtractProblemFromResult(result);

        // Assert
        Assert.Contains(_logger.LogEntries, 
            e => e.LogLevel == LogLevel.Error);
    }

    /// <summary>
    /// Tests that logging includes all relevant context (traceId, instance, detail).
    /// </summary>
    [Fact]
    public async Task Logging_IncludesAllContext()
    {
        // Arrange
        var detail = "Test error";
        var instance = "/api/test";
        var traceId = "trace-xyz";

        // Act
        var result = ResultsExtensions.NotFound(detail, instance, traceId, _logger);
        await ExtractProblemFromResult(result);

        // Assert
        var logEntry = _logger.LogEntries.FirstOrDefault(e => e.Message.Contains("RFC 7807"));
        Assert.NotNull(logEntry);
        Assert.Contains(detail, logEntry.Message);
        Assert.Contains(instance, logEntry.Message);
        Assert.Contains(traceId, logEntry.Message);
    }

    #endregion

    #region Security Tests

    /// <summary>
    /// Tests that sensitive information in detail is not exposed (responsibility of caller).
    /// This test validates that the mechanism doesn't accidentally add sensitive data.
    /// </summary>
    [Fact]
    public async Task InternalServerError_DoesNotExposeStackTrace()
    {
        // Arrange - simulate caller providing sanitized message
        var sanitizedDetail = "An unexpected error occurred. Please try again later.";

        // Act
        var result = ResultsExtensions.InternalServerError(sanitizedDetail, "/api/test");
        var (problem, statusCode, contentType) = await ExtractProblemFromResult(result);

        // Assert
        Assert.NotNull(problem);
        Assert.DoesNotContain("Exception", problem.detail ?? "");
        Assert.DoesNotContain("StackTrace", problem.detail ?? "");
        Assert.DoesNotContain("at ", problem.detail ?? "");
    }

    /// <summary>
    /// Tests that all responses use secure content type (application/problem+json).
    /// </summary>
    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(409)]
    [InlineData(422)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    public async Task AllResponses_UseCorrectContentType(int statusCode)
    {
        // Arrange & Act
        IResult result = statusCode switch
        {
            400 => ResultsExtensions.BadRequest("Error", "/"),
            401 => ResultsExtensions.Unauthorized("Error", "/"),
            403 => ResultsExtensions.Forbidden("Error", "/"),
            404 => ResultsExtensions.NotFound("Error", "/"),
            409 => ResultsExtensions.Conflict("Error", "/"),
            422 => ResultsExtensions.ValidationError("Error", "/"),
            429 => ResultsExtensions.TooManyRequests("Error", "/"),
            500 => ResultsExtensions.InternalServerError("Error", "/"),
            502 => ResultsExtensions.BadGateway("Error", "/"),
            503 => ResultsExtensions.ServiceUnavailable("Error", "/"),
            504 => ResultsExtensions.GatewayTimeout("Error", "/"),
            _ => throw new ArgumentException("Invalid status code")
        };

        var (problem, responseStatus, contentType) = await ExtractProblemFromResult(result);

        // Assert
        Assert.Equal(statusCode, responseStatus);
        Assert.Equal("application/problem+json", contentType);
    }

    #endregion

    #region Edge Cases

    /// <summary>
    /// Tests handling of very long detail messages.
    /// </summary>
    [Fact]
    public async Task VeryLongDetail_IsHandledCorrectly()
    {
        // Arrange
        var longDetail = new string('A', 10000);

        // Act
        var result = ResultsExtensions.BadRequest(longDetail, "/api/test");
        var (problem, statusCode, contentType) = await ExtractProblemFromResult(result);

        // Assert
        Assert.NotNull(problem);
        Assert.Equal(longDetail, problem.detail);
    }

    /// <summary>
    /// Tests handling of special characters in detail and instance.
    /// </summary>
    [Fact]
    public async Task SpecialCharacters_AreHandledCorrectly()
    {
        // Arrange
        var detail = "Error with special chars: <>&\"'";
        var instance = "/api/test?param=<>&\"'";

        // Act
        var result = ResultsExtensions.NotFound(detail, instance);
        var (problem, statusCode, contentType) = await ExtractProblemFromResult(result);

        // Assert
        Assert.NotNull(problem);
        Assert.Contains("<>&\"'", problem.detail ?? "");
        Assert.Contains("<>&\"'", problem.instance);
    }

    /// <summary>
    /// Tests handling of Unicode characters.
    /// </summary>
    [Fact]
    public async Task UnicodeCharacters_AreHandledCorrectly()
    {
        // Arrange
        var detail = "シリーズが見つかりません (Series not found)";
        var instance = "/api/シリーズ";

        // Act
        var result = ResultsExtensions.NotFound(detail, instance);
        var (problem, statusCode, contentType) = await ExtractProblemFromResult(result);

        // Assert
        Assert.NotNull(problem);
        Assert.Contains("シリーズ", problem.detail ?? "");
        Assert.Contains("シリーズ", problem.instance);
    }

    #endregion
}

/// <summary>
/// Test logger that captures log entries for verification.
/// </summary>
public class TestLogger<T> : ILogger<T>
{
    private readonly ITestOutputHelper _output;
    public List<LogEntry> LogEntries { get; } = new();

    public TestLogger(ITestOutputHelper output)
    {
        _output = output;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        LogEntries.Add(new LogEntry(logLevel, message, exception));
        _output.WriteLine($"[{logLevel}] {message}");
    }

    public record LogEntry(LogLevel LogLevel, string Message, Exception? Exception);
}
