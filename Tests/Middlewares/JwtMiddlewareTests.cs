using MehguViewer.Core.Middlewares;
using MehguViewer.Core.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using System.Text.Json;
using Xunit;

namespace MehguViewer.Core.Tests.Middlewares;

/// <summary>
/// Unit tests for JwtMiddleware to verify proper handling of authentication/authorization failures.
/// </summary>
public class JwtMiddlewareTests
{
    private readonly ILogger<JwtMiddleware> _logger;

    public JwtMiddlewareTests()
    {
        _logger = new LoggerFactory().CreateLogger<JwtMiddleware>();
    }

    /// <summary>
    /// Test that 401 Unauthorized responses are converted to RFC 7807 Problem Details.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_Returns401_WritesProblemDetails()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/series";
        context.Request.Method = "GET";
        context.Response.Body = new MemoryStream();

        RequestDelegate next = (ctx) =>
        {
            ctx.Response.StatusCode = 401;
            return Task.CompletedTask;
        };

        var middleware = new JwtMiddleware(next, _logger);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(401, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var responseBody = await new StreamReader(context.Response.Body).ReadToEndAsync();
        var problem = JsonSerializer.Deserialize<Problem>(responseBody);

        Assert.NotNull(problem);
        Assert.Equal("urn:mvn:error:unauthorized", problem.type);
        Assert.Equal("Unauthorized", problem.title);
        Assert.Equal(401, problem.status);
        Assert.Contains("JWT token", problem.detail);
    }

    /// <summary>
    /// Test that 403 Forbidden responses are converted to RFC 7807 Problem Details.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_Returns403_WritesProblemDetails()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/admin/users";
        context.Request.Method = "POST";
        context.Response.Body = new MemoryStream();

        var claims = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, "testuser"),
            new Claim("sub", "user-123")
        }, "TestAuth"));
        context.User = claims;

        RequestDelegate next = (ctx) =>
        {
            ctx.Response.StatusCode = 403;
            return Task.CompletedTask;
        };

        var middleware = new JwtMiddleware(next, _logger);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(403, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var responseBody = await new StreamReader(context.Response.Body).ReadToEndAsync();
        var problem = JsonSerializer.Deserialize<Problem>(responseBody);

        Assert.NotNull(problem);
        Assert.Equal("urn:mvn:error:forbidden", problem.type);
        Assert.Equal("Forbidden", problem.title);
        Assert.Equal(403, problem.status);
        Assert.Contains("Insufficient permissions", problem.detail);
    }

    /// <summary>
    /// Test that successful (2xx) responses are not modified.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_Returns200_NoModification()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/series";
        context.Request.Method = "GET";
        context.Response.Body = new MemoryStream();

        RequestDelegate next = (ctx) =>
        {
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            return ctx.Response.WriteAsync("{\"success\":true}");
        };

        var middleware = new JwtMiddleware(next, _logger);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(200, context.Response.StatusCode);
        Assert.Equal("application/json", context.Response.ContentType);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var responseBody = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.Equal("{\"success\":true}", responseBody);
    }

    /// <summary>
    /// Test that the middleware handles concurrent status code scenarios correctly.
    /// In production, HasStarted prevents double-writes, but in tests the behavior differs.
    /// This test verifies the middleware doesn't crash when response has content.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_ResponseWithContent_HandlesGracefully()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/series";
        context.Response.Body = new MemoryStream();

        RequestDelegate next = async (ctx) =>
        {
            ctx.Response.StatusCode = 200; // Success status
            await ctx.Response.WriteAsync("Success");
        };

        var middleware = new JwtMiddleware(next, _logger);

        // Act
        await middleware.InvokeAsync(context);

        // Assert - Should complete without exception
        Assert.Equal(200, context.Response.StatusCode);
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var responseBody = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.Equal("Success", responseBody);
    }

    /// <summary>
    /// Test that null context throws ArgumentNullException.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_NullContext_ThrowsArgumentNullException()
    {
        // Arrange
        RequestDelegate next = (ctx) => Task.CompletedTask;
        var middleware = new JwtMiddleware(next, _logger);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => middleware.InvokeAsync(null!));
    }

    /// <summary>
    /// Test that constructor validates required parameters.
    /// </summary>
    [Fact]
    public void Constructor_NullNext_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new JwtMiddleware(null!, _logger));
    }

    /// <summary>
    /// Test that constructor validates logger parameter.
    /// </summary>
    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        // Arrange
        RequestDelegate next = (ctx) => Task.CompletedTask;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new JwtMiddleware(next, null!));
    }

    /// <summary>
    /// Test that Problem Details include correct instance path.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_401Response_IncludesCorrectInstancePath()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/series/urn:mvn:series:123";
        context.Request.Method = "DELETE";
        context.Response.Body = new MemoryStream();

        RequestDelegate next = (ctx) =>
        {
            ctx.Response.StatusCode = 401;
            return Task.CompletedTask;
        };

        var middleware = new JwtMiddleware(next, _logger);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var responseBody = await new StreamReader(context.Response.Body).ReadToEndAsync();
        var problem = JsonSerializer.Deserialize<Problem>(responseBody);

        Assert.NotNull(problem);
        Assert.Equal("/api/series/urn:mvn:series:123", problem.instance);
    }

    /// <summary>
    /// Test that 404 and other error codes are not modified by middleware.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_Returns404_NoModification()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/notfound";
        context.Response.Body = new MemoryStream();

        RequestDelegate next = (ctx) =>
        {
            ctx.Response.StatusCode = 404;
            return Task.CompletedTask;
        };

        var middleware = new JwtMiddleware(next, _logger);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(404, context.Response.StatusCode);
        Assert.Null(context.Response.ContentType); // Should not be modified
    }
}
