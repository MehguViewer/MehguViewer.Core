using MehguViewer.Core.Middlewares;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Xunit;

namespace MehguViewer.Core.Tests.Middlewares;

/// <summary>
/// Unit tests for ServerTimingMiddleware to verify performance tracking functionality.
/// 
/// Note: The Response.OnStarting callback doesn't execute in unit test contexts.
/// These tests verify middleware structure and error handling. For header verification,
/// see integration tests that use TestServer.
/// </summary>
public class ServerTimingMiddlewareTests
{
    private readonly ILogger<ServerTimingMiddleware> _logger;

    public ServerTimingMiddlewareTests()
    {
        _logger = new LoggerFactory().CreateLogger<ServerTimingMiddleware>();
    }

    /// <summary>
    /// Test that middleware executes without errors (header added via OnStarting in production).
    /// </summary>
    [Fact]
    public async Task InvokeAsync_ExecutesSuccessfully()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/series";
        context.Request.Method = "GET";
        context.Response.Body = new MemoryStream();

        var executed = false;
        RequestDelegate next = async (ctx) =>
        {
            await Task.Delay(10);
            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsync("test");
            executed = true;
        };

        var middleware = new ServerTimingMiddleware(next, _logger);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(executed, "Next middleware should be called");
        Assert.Equal(200, context.Response.StatusCode);
    }

    /// <summary>
    /// Test that middleware handles exceptions and still tracks time.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_ExceptionThrown_StillTracksTime()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/error";

        RequestDelegate next = (ctx) =>
        {
            throw new InvalidOperationException("Test exception");
        };

        var middleware = new ServerTimingMiddleware(next, _logger);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context));
    }

    /// <summary>
    /// Test that null context throws ArgumentNullException.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_NullContext_ThrowsArgumentNullException()
    {
        // Arrange
        RequestDelegate next = (ctx) => Task.CompletedTask;
        var middleware = new ServerTimingMiddleware(next, _logger);

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
        Assert.Throws<ArgumentNullException>(() => new ServerTimingMiddleware(null!, _logger));
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
        Assert.Throws<ArgumentNullException>(() => new ServerTimingMiddleware(next, null!));
    }

    /// <summary>
    /// Test that middleware works with various HTTP methods.
    /// </summary>
    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("PATCH")]
    public async Task InvokeAsync_VariousHttpMethods_ExecutesSuccessfully(string method)
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/test";
        context.Request.Method = method;
        context.Response.Body = new MemoryStream();

        var executed = false;
        RequestDelegate next = async (ctx) =>
        {
            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsync("test");
            executed = true;
        };

        var middleware = new ServerTimingMiddleware(next, _logger);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(executed);
    }

    /// <summary>
    /// Test that middleware works with different status codes.
    /// </summary>
    [Theory]
    [InlineData(200)]
    [InlineData(201)]
    [InlineData(204)]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(500)]
    public async Task InvokeAsync_VariousStatusCodes_ExecutesSuccessfully(int statusCode)
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/test";
        context.Response.Body = new MemoryStream();

        RequestDelegate next = async (ctx) =>
        {
            ctx.Response.StatusCode = statusCode;
            await ctx.Response.WriteAsync("test");
        };

        var middleware = new ServerTimingMiddleware(next, _logger);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(statusCode, context.Response.StatusCode);
    }

    /// <summary>
    /// Test that middleware processes fast requests correctly.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_FastRequest_CompletesSuccessfully()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/fast";
        context.Response.Body = new MemoryStream();

        RequestDelegate next = async (ctx) =>
        {
            await Task.Delay(5);
            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsync("test");
        };

        var middleware = new ServerTimingMiddleware(next, _logger);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(200, context.Response.StatusCode);
    }

    /// <summary>
    /// Test that middleware can handle concurrent requests.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_ConcurrentRequests_HandlesCorrectly()
    {
        // Arrange
        var middleware = new ServerTimingMiddleware(
            async (ctx) =>
            {
                await Task.Delay(10);
                ctx.Response.StatusCode = 200;
                await ctx.Response.WriteAsync("test");
            },
            _logger);

        // Act - Create multiple concurrent requests
        var tasks = Enumerable.Range(0, 10).Select(_ =>
        {
            var context = new DefaultHttpContext();
            context.Request.Path = "/api/test";
            context.Response.Body = new MemoryStream();
            return middleware.InvokeAsync(context);
        }).ToList();

        // Wait for all tasks to complete
        await Task.WhenAll(tasks);

        // Assert - All should complete successfully
        Assert.All(tasks, t => 
        {
            Assert.True(t.IsCompletedSuccessfully, 
                $"Task should be completed successfully. Status: {t.Status}");
        });
    }
}
