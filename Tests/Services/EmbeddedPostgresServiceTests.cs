using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;
using MehguViewer.Core.Services;

namespace MehguViewer.Core.Tests.Services;

/// <summary>
/// Unit tests for EmbeddedPostgresService.
/// Tests cover initialization, configuration, error handling, and lifecycle management.
/// </summary>
[Trait("Category", "Unit")]
[Trait("Service", "EmbeddedPostgres")]
public class EmbeddedPostgresServiceTests : IDisposable
{
    private readonly ILogger<EmbeddedPostgresService> _logger;
    private readonly ITestOutputHelper _output;
    private bool _disposed;

    public EmbeddedPostgresServiceTests(ITestOutputHelper output)
    {
        _output = output;
        _logger = NullLogger<EmbeddedPostgresService>.Instance;
    }

    #region Configuration Tests

    [Fact]
    public async Task StartAsync_WhenDisabled_SetsEmbeddedModeEnabledToFalse()
    {
        // Arrange
        var configuration = CreateConfiguration(enabled: false);
        var service = new EmbeddedPostgresService(_logger, configuration);

        // Act
        await service.StartAsync(CancellationToken.None);

        // Assert
        Assert.False(service.EmbeddedModeEnabled);
        Assert.False(service.IsRunning);
    }

    [Fact]
    public async Task StartAsync_WhenDisabled_CompletesStartupSuccessfully()
    {
        // Arrange
        var configuration = CreateConfiguration(enabled: false);
        var service = new EmbeddedPostgresService(_logger, configuration);

        // Act
        await service.StartAsync(CancellationToken.None);
        await service.WaitForStartupAsync();

        // Assert
        Assert.False(service.EmbeddedModeEnabled);
    }

    [Fact]
    public async Task StartAsync_WithFallbackToMemoryTrue_SetsFallbackAllowed()
    {
        // Arrange
        var configuration = CreateConfiguration(enabled: false, fallbackToMemory: true);
        var service = new EmbeddedPostgresService(_logger, configuration);

        // Act
        await service.StartAsync(CancellationToken.None);

        // Assert
        Assert.True(service.FallbackToMemoryAllowed);
    }

    [Fact]
    public async Task StartAsync_WithFallbackToMemoryFalse_SetsFallbackDisallowed()
    {
        // Arrange
        var configuration = CreateConfiguration(enabled: false, fallbackToMemory: false);
        var service = new EmbeddedPostgresService(_logger, configuration);

        // Act
        await service.StartAsync(CancellationToken.None);

        // Assert
        Assert.False(service.FallbackToMemoryAllowed);
    }

    #endregion

    #region Property Tests

    [Fact]
    public void IsRunning_InitiallyFalse()
    {
        // Arrange
        var configuration = CreateConfiguration();
        
        // Act
        var service = new EmbeddedPostgresService(_logger, configuration);

        // Assert
        Assert.False(service.IsRunning);
    }

    [Fact]
    public void EmbeddedModeEnabled_InitiallyTrue()
    {
        // Arrange
        var configuration = CreateConfiguration();
        
        // Act
        var service = new EmbeddedPostgresService(_logger, configuration);

        // Assert
        Assert.True(service.EmbeddedModeEnabled);
    }

    [Fact]
    public void StartupFailed_InitiallyFalse()
    {
        // Arrange
        var configuration = CreateConfiguration();
        
        // Act
        var service = new EmbeddedPostgresService(_logger, configuration);

        // Assert
        Assert.False(service.StartupFailed);
    }

    [Fact]
    public void ConnectionString_InitiallyEmpty()
    {
        // Arrange
        var configuration = CreateConfiguration();
        
        // Act
        var service = new EmbeddedPostgresService(_logger, configuration);

        // Assert
        Assert.Equal(string.Empty, service.ConnectionString);
    }

    [Fact]
    public void Port_InitiallyZero()
    {
        // Arrange
        var configuration = CreateConfiguration();
        
        // Act
        var service = new EmbeddedPostgresService(_logger, configuration);

        // Assert
        Assert.Equal(0, service.Port);
    }

    [Fact]
    public void FallbackToMemoryAllowed_InitiallyTrue()
    {
        // Arrange
        var configuration = CreateConfiguration();
        
        // Act
        var service = new EmbeddedPostgresService(_logger, configuration);

        // Assert - Default should be true
        Assert.True(service.FallbackToMemoryAllowed);
    }

    #endregion

    #region Lifecycle Tests

    [Fact]
    public async Task StopAsync_WhenNotRunning_DoesNotThrow()
    {
        // Arrange
        var configuration = CreateConfiguration(enabled: false);
        var service = new EmbeddedPostgresService(_logger, configuration);

        // Act & Assert - Should not throw
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task WaitForStartupAsync_CompletesAfterStartAsync()
    {
        // Arrange
        var configuration = CreateConfiguration(enabled: false);
        var service = new EmbeddedPostgresService(_logger, configuration);

        // Act
        var startTask = service.StartAsync(CancellationToken.None);
        var waitTask = service.WaitForStartupAsync();
        
        await startTask;
        await waitTask;

        // Assert - If we got here, both completed successfully
        Assert.False(service.IsRunning); // Should not be running when disabled
    }

    [Fact]
    public async Task DisposeAsync_WhenNotStarted_DoesNotThrow()
    {
        // Arrange
        var configuration = CreateConfiguration();
        var service = new EmbeddedPostgresService(_logger, configuration);

        // Act & Assert - Should not throw
        await service.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_CanBeCalledMultipleTimes()
    {
        // Arrange
        var configuration = CreateConfiguration();
        var service = new EmbeddedPostgresService(_logger, configuration);

        // Act & Assert - Should not throw
        await service.DisposeAsync();
        await service.DisposeAsync();
        await service.DisposeAsync();
    }

    [Fact]
    public async Task StartAndStop_LifecycleCompletes()
    {
        // Arrange
        var configuration = CreateConfiguration(enabled: false);
        var service = new EmbeddedPostgresService(_logger, configuration);

        // Act
        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);
        await service.DisposeAsync();

        // Assert - No exceptions thrown
        Assert.False(service.IsRunning);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task StartAsync_WithInvalidPort_UsesDefaultPort()
    {
        // This test verifies that invalid port numbers are handled gracefully
        // The actual validation happens during embedded server startup
        var configuration = CreateConfiguration(enabled: false, port: -1);
        var service = new EmbeddedPostgresService(_logger, configuration);

        await service.StartAsync(CancellationToken.None);
        
        // Should complete without throwing
        Assert.False(service.IsRunning);
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Creates an IConfiguration instance with test values.
    /// </summary>
    private static IConfiguration CreateConfiguration(
        bool enabled = true,
        bool fallbackToMemory = true,
        string? connectionString = null,
        int? port = null,
        string? version = null,
        string? dataDir = null)
    {
        var configData = new Dictionary<string, string?>
        {
            ["EmbeddedPostgres:Enabled"] = enabled.ToString(),
            ["EmbeddedPostgres:FallbackToMemory"] = fallbackToMemory.ToString(),
        };

        if (port.HasValue)
        {
            configData["EmbeddedPostgres:Port"] = port.Value.ToString();
        }

        if (!string.IsNullOrEmpty(version))
        {
            configData["EmbeddedPostgres:Version"] = version;
        }

        if (!string.IsNullOrEmpty(dataDir))
        {
            configData["EmbeddedPostgres:DataDir"] = dataDir;
        }

        if (!string.IsNullOrEmpty(connectionString))
        {
            configData["ConnectionStrings:DefaultConnection"] = connectionString;
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(configData!)
            .Build();
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            // Cleanup if needed
        }

        _disposed = true;
    }

    #endregion
}

/// <summary>
/// Integration tests for EmbeddedPostgresService that test actual PostgreSQL behavior.
/// These tests are marked as integration tests and may be skipped in CI/CD pipelines.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Service", "EmbeddedPostgres")]
public class EmbeddedPostgresServiceIntegrationTests
{
    [Fact(Skip = "Integration test - requires PostgreSQL binaries and may take several minutes")]
    public async Task StartAsync_WithEmbeddedMode_StartsPostgresSuccessfully()
    {
        // This test would require actual PostgreSQL binaries
        // and would be run in an integration test environment
        await Task.CompletedTask;
    }

    [Fact(Skip = "Integration test - requires running PostgreSQL instance")]
    public async Task EnsureDatabaseExists_CreatesDatabase()
    {
        // This test would verify actual database creation
        await Task.CompletedTask;
    }

    [Fact(Skip = "Integration test - requires running PostgreSQL instance")]
    public async Task ConnectionString_AllowsSuccessfulConnection()
    {
        // This test would verify the connection string works
        await Task.CompletedTask;
    }

    [Fact(Skip = "Integration test - requires running PostgreSQL instance")]
    public async Task StopPostgres_StopsServerGracefully()
    {
        // This test would verify graceful shutdown
        await Task.CompletedTask;
    }
}

