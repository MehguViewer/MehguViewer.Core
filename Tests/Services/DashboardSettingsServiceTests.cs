using MehguViewer.Core.UI.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace MehguViewer.Core.Tests.Services;

/// <summary>
/// Comprehensive unit tests for the DashboardSettingsService covering:
/// - Loading and saving dashboard settings
/// - Merging saved settings with defaults
/// - Security validation (size limits, widget counts, duplicate IDs)
/// - Error handling for JSON serialization and JS interop failures
/// - Settings reset functionality
/// </summary>
[Trait("Category", "Unit")]
[Trait("Service", "DashboardSettings")]
public class DashboardSettingsServiceTests
{
    private readonly TestJSRuntime _jsRuntime;
    private readonly DashboardSettingsService _service;
    private readonly ITestOutputHelper _output;

    public DashboardSettingsServiceTests(ITestOutputHelper output)
    {
        _output = output;
        _jsRuntime = new TestJSRuntime();
        var logger = NullLogger<DashboardSettingsService>.Instance;
        _service = new DashboardSettingsService(_jsRuntime, logger);
    }

    #region Constructor Tests

    /// <summary>
    /// Tests that constructor throws ArgumentNullException when jsRuntime is null.
    /// </summary>
    [Fact]
    public void Constructor_NullJSRuntime_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new DashboardSettingsService(null!, NullLogger<DashboardSettingsService>.Instance));
        
        Assert.Equal("jsRuntime", exception.ParamName);
        _output.WriteLine($"✓ Constructor correctly rejects null JSRuntime: {exception.Message}");
    }

    /// <summary>
    /// Tests that constructor throws ArgumentNullException when logger is null.
    /// </summary>
    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        // Arrange
        var jsRuntime = new TestJSRuntime();
        
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new DashboardSettingsService(jsRuntime, null!));
        
        Assert.Equal("logger", exception.ParamName);
        _output.WriteLine($"✓ Constructor correctly rejects null Logger: {exception.Message}");
    }

    #endregion

    #region GetSettingsAsync Tests

    /// <summary>
    /// Tests that GetSettingsAsync returns default settings when no saved data exists.
    /// </summary>
    [Fact]
    public async Task GetSettingsAsync_NoSavedData_ReturnsDefaults()
    {
        // Arrange
        _jsRuntime.SetStorageValue(null);

        // Act
        var result = await _service.GetSettingsAsync();

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Widgets);
        Assert.Equal(5, result.Widgets.Count); // Default has 5 widgets
        Assert.All(result.Widgets, widget => Assert.True(widget.IsVisible));
        
        _output.WriteLine($"✓ Returned {result.Widgets.Count} default widgets when no saved data");
    }

    /// <summary>
    /// Tests that GetSettingsAsync returns default settings when saved data is empty string.
    /// </summary>
    [Fact]
    public async Task GetSettingsAsync_EmptyString_ReturnsDefaults()
    {
        // Arrange
        _jsRuntime.SetStorageValue(string.Empty);

        // Act
        var result = await _service.GetSettingsAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(5, result.Widgets.Count);
        _output.WriteLine("✓ Returned default settings for empty string");
    }

    /// <summary>
    /// Tests that GetSettingsAsync correctly deserializes valid saved settings.
    /// </summary>
    [Fact]
    public async Task GetSettingsAsync_ValidSavedData_ReturnsDeserializedSettings()
    {
        // Arrange
        var savedSettings = new DashboardSettings
        {
            Widgets = new List<WidgetConfig>
            {
                new() { Id = "stats", Name = "Statistics", Icon = "Dashboard", IsVisible = true, Order = 0 },
                new() { Id = "recent-series", Name = "Recent Series", Icon = "MenuBook", IsVisible = false, Order = 1 },
                new() { Id = "quick-actions", Name = "Quick Actions", Icon = "FlashOn", IsVisible = true, Order = 2 }
            }
        };
        _jsRuntime.SetStorageValue(JsonSerializer.Serialize(savedSettings));

        // Act
        var result = await _service.GetSettingsAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(5, result.Widgets.Count); // Merged with 2 new defaults
        Assert.Contains(result.Widgets, w => w.Id == "stats" && w.IsVisible);
        Assert.Contains(result.Widgets, w => w.Id == "recent-series" && !w.IsVisible);
        
        _output.WriteLine($"✓ Loaded {savedSettings.Widgets.Count} saved widgets, merged to {result.Widgets.Count} total");
    }

    /// <summary>
    /// Tests that GetSettingsAsync merges new default widgets with saved settings.
    /// </summary>
    [Fact]
    public async Task GetSettingsAsync_SavedDataMissingNewWidgets_MergesDefaults()
    {
        // Arrange
        var savedSettings = new DashboardSettings
        {
            Widgets = new List<WidgetConfig>
            {
                new() { Id = "stats", Name = "Statistics", Icon = "Dashboard", IsVisible = true, Order = 0 }
            }
        };
        _jsRuntime.SetStorageValue(JsonSerializer.Serialize(savedSettings));

        // Act
        var result = await _service.GetSettingsAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(5, result.Widgets.Count); // 1 saved + 4 new defaults
        Assert.Contains(result.Widgets, w => w.Id == "stats");
        Assert.Contains(result.Widgets, w => w.Id == "node-info");
        Assert.Contains(result.Widgets, w => w.Id == "users-overview");
        
        _output.WriteLine($"✓ Merged 1 saved widget with 4 new defaults = {result.Widgets.Count} total");
    }

    /// <summary>
    /// Tests that GetSettingsAsync returns defaults when JSON is invalid.
    /// </summary>
    [Fact]
    public async Task GetSettingsAsync_InvalidJson_ReturnsDefaults()
    {
        // Arrange
        _jsRuntime.SetStorageValue("{ invalid json ]}");

        // Act
        var result = await _service.GetSettingsAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(5, result.Widgets.Count);
        _output.WriteLine("✓ Returned defaults when JSON deserialization failed");
    }

    /// <summary>
    /// Tests that GetSettingsAsync returns defaults when JSON size exceeds limit.
    /// </summary>
    [Fact]
    public async Task GetSettingsAsync_JsonExceedsMaxSize_ReturnsDefaults()
    {
        // Arrange
        _jsRuntime.SetStorageValue(new string('a', 20000)); // 20KB, exceeds 10KB limit

        // Act
        var result = await _service.GetSettingsAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(5, result.Widgets.Count);
        _output.WriteLine("✓ Returned defaults when JSON size exceeded limit");
    }

    /// <summary>
    /// Tests that GetSettingsAsync truncates widget list when it exceeds maximum count.
    /// </summary>
    [Fact]
    public async Task GetSettingsAsync_TooManyWidgets_Truncates()
    {
        // Arrange
        var settings = new DashboardSettings
        {
            Widgets = Enumerable.Range(0, 60) // 60 widgets, exceeds 50 limit
                .Select(i => new WidgetConfig
                {
                    Id = $"widget-{i}",
                    Name = $"Widget {i}",
                    Icon = "Icon",
                    IsVisible = true,
                    Order = i
                })
                .ToList()
        };
        _jsRuntime.SetStorageValue(JsonSerializer.Serialize(settings));

        // Act
        var result = await _service.GetSettingsAsync();

        // Assert
        Assert.NotNull(result);
        _output.WriteLine($"Widget count after load: {result.Widgets.Count}");
        // After truncating 60 to 50, then merging with 5 defaults that aren't in the saved widgets = 55 total
        Assert.True(result.Widgets.Count <= 55, $"Expected <= 55 widgets but got {result.Widgets.Count}");
        _output.WriteLine($"✓ Truncated 60 widgets to {result.Widgets.Count}");
    }

    /// <summary>
    /// Tests that GetSettingsAsync returns defaults when JS interop throws exception.
    /// </summary>
    [Fact]
    public async Task GetSettingsAsync_JSException_ReturnsDefaults()
    {
        // Arrange
        _jsRuntime.ThrowOnNextCall = true;

        // Act
        var result = await _service.GetSettingsAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(5, result.Widgets.Count);
        _output.WriteLine("✓ Returned defaults when JSException occurred");
    }

    #endregion

    #region SaveSettingsAsync Tests

    /// <summary>
    /// Tests that SaveSettingsAsync throws ArgumentNullException when settings is null.
    /// </summary>
    [Fact]
    public async Task SaveSettingsAsync_NullSettings_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _service.SaveSettingsAsync(null!));
        
        Assert.Equal("settings", exception.ParamName);
        _output.WriteLine($"✓ Correctly rejected null settings: {exception.Message}");
    }

    /// <summary>
    /// Tests that SaveSettingsAsync successfully saves valid settings.
    /// </summary>
    [Fact]
    public async Task SaveSettingsAsync_ValidSettings_SavesSuccessfully()
    {
        // Arrange
        var settings = new DashboardSettings
        {
            Widgets = new List<WidgetConfig>
            {
                new() { Id = "stats", Name = "Statistics", Icon = "Dashboard", IsVisible = true, Order = 0 },
                new() { Id = "recent-series", Name = "Recent Series", Icon = "MenuBook", IsVisible = false, Order = 1 }
            }
        };

        // Act
        await _service.SaveSettingsAsync(settings);

        // Assert
        Assert.NotNull(_jsRuntime.SavedValue);
        var savedSettings = JsonSerializer.Deserialize<DashboardSettings>(_jsRuntime.SavedValue);
        Assert.NotNull(savedSettings);
        Assert.Equal(2, savedSettings.Widgets.Count);
        
        _output.WriteLine($"✓ Successfully saved {settings.Widgets.Count} widgets to local storage");
    }

    /// <summary>
    /// Tests that SaveSettingsAsync throws when widget count exceeds maximum.
    /// </summary>
    [Fact]
    public async Task SaveSettingsAsync_TooManyWidgets_ThrowsInvalidOperationException()
    {
        // Arrange
        var settings = new DashboardSettings
        {
            Widgets = Enumerable.Range(0, 51) // 51 widgets, exceeds 50 limit
                .Select(i => new WidgetConfig
                {
                    Id = $"widget-{i}",
                    Name = $"Widget {i}",
                    Icon = "Icon",
                    IsVisible = true,
                    Order = i
                })
                .ToList()
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.SaveSettingsAsync(settings));
        
        Assert.Contains("cannot contain more than", exception.Message);
        _output.WriteLine($"✓ Rejected 51 widgets (exceeds limit): {exception.Message}");
    }

    /// <summary>
    /// Tests that SaveSettingsAsync throws when widget ID is null or empty.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SaveSettingsAsync_InvalidWidgetId_ThrowsInvalidOperationException(string invalidId)
    {
        // Arrange
        var settings = new DashboardSettings
        {
            Widgets = new List<WidgetConfig>
            {
                new() { Id = invalidId, Name = "Test", Icon = "Icon", IsVisible = true, Order = 0 }
            }
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.SaveSettingsAsync(settings));
        
        Assert.Contains("Widget ID cannot be null or empty", exception.Message);
        _output.WriteLine($"✓ Rejected invalid widget ID '{invalidId}': {exception.Message}");
    }

    /// <summary>
    /// Tests that SaveSettingsAsync throws when widget has duplicate ID.
    /// </summary>
    [Fact]
    public async Task SaveSettingsAsync_DuplicateWidgetId_ThrowsInvalidOperationException()
    {
        // Arrange
        var settings = new DashboardSettings
        {
            Widgets = new List<WidgetConfig>
            {
                new() { Id = "stats", Name = "Statistics", Icon = "Dashboard", IsVisible = true, Order = 0 },
                new() { Id = "stats", Name = "Stats Copy", Icon = "Dashboard", IsVisible = true, Order = 1 }
            }
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.SaveSettingsAsync(settings));
        
        Assert.Contains("Duplicate widget ID", exception.Message);
        _output.WriteLine($"✓ Rejected duplicate widget ID: {exception.Message}");
    }

    /// <summary>
    /// Tests that SaveSettingsAsync throws when widget field exceeds length limit.
    /// </summary>
    [Fact]
    public async Task SaveSettingsAsync_WidgetFieldTooLong_ThrowsInvalidOperationException()
    {
        // Arrange
        var settings = new DashboardSettings
        {
            Widgets = new List<WidgetConfig>
            {
                new() 
                { 
                    Id = new string('a', 101), // 101 chars, exceeds 100 limit
                    Name = "Test",
                    Icon = "Icon",
                    IsVisible = true,
                    Order = 0
                }
            }
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.SaveSettingsAsync(settings));
        
        Assert.Contains("exceeds maximum length", exception.Message);
        _output.WriteLine($"✓ Rejected widget with field too long: {exception.Message}");
    }

    /// <summary>
    /// Tests that SaveSettingsAsync throws when widget order is out of valid range.
    /// </summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(1001)]
    public async Task SaveSettingsAsync_InvalidWidgetOrder_ThrowsInvalidOperationException(int invalidOrder)
    {
        // Arrange
        var settings = new DashboardSettings
        {
            Widgets = new List<WidgetConfig>
            {
                new() { Id = "stats", Name = "Statistics", Icon = "Dashboard", IsVisible = true, Order = invalidOrder }
            }
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.SaveSettingsAsync(settings));
        
        Assert.Contains("Widget order must be between 0 and 1000", exception.Message);
        _output.WriteLine($"✓ Rejected widget order {invalidOrder}: {exception.Message}");
    }

    /// <summary>
    /// Tests that SaveSettingsAsync throws when widgets collection is null.
    /// </summary>
    [Fact]
    public async Task SaveSettingsAsync_NullWidgetsCollection_ThrowsInvalidOperationException()
    {
        // Arrange
        var settings = new DashboardSettings
        {
            Widgets = null!
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.SaveSettingsAsync(settings));
        
        Assert.Contains("must contain a Widgets collection", exception.Message);
        _output.WriteLine($"✓ Rejected null Widgets collection: {exception.Message}");
    }

    #endregion

    #region ResetToDefaultsAsync Tests

    /// <summary>
    /// Tests that ResetToDefaultsAsync removes settings from storage and returns defaults.
    /// </summary>
    [Fact]
    public async Task ResetToDefaultsAsync_RemovesStorageAndReturnsDefaults()
    {
        // Arrange
        _jsRuntime.SetStorageValue("some data");

        // Act
        var result = await _service.ResetToDefaultsAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(5, result.Widgets.Count);
        Assert.All(result.Widgets, widget => Assert.True(widget.IsVisible));
        Assert.True(_jsRuntime.WasRemoveCalled);
        
        _output.WriteLine($"✓ Reset to {result.Widgets.Count} default widgets");
    }

    /// <summary>
    /// Tests that ResetToDefaultsAsync throws InvalidOperationException on JS interop failure.
    /// </summary>
    [Fact]
    public async Task ResetToDefaultsAsync_JSException_ThrowsInvalidOperationException()
    {
        // Arrange
        _jsRuntime.ThrowOnRemove = true;

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.ResetToDefaultsAsync());
        
        Assert.Contains("Failed to reset dashboard settings", exception.Message);
        _output.WriteLine($"✓ Wrapped JSException on reset: {exception.Message}");
    }

    #endregion

    #region Integration Tests

    /// <summary>
    /// Tests full save and load cycle to ensure data integrity.
    /// </summary>
    [Fact]
    public async Task SaveAndLoad_RoundTrip_PreservesData()
    {
        // Arrange
        var originalSettings = new DashboardSettings
        {
            Widgets = new List<WidgetConfig>
            {
                new() { Id = "stats", Name = "Statistics", Icon = "Dashboard", IsVisible = false, Order = 2 },
                new() { Id = "recent-series", Name = "Recent Series", Icon = "MenuBook", IsVisible = true, Order = 0 },
                new() { Id = "custom", Name = "Custom Widget", Icon = "Star", IsVisible = true, Order = 1 }
            }
        };

        // Act
        await _service.SaveSettingsAsync(originalSettings);
        var loadedSettings = await _service.GetSettingsAsync();

        // Assert
        Assert.NotNull(_jsRuntime.SavedValue);
        Assert.NotNull(loadedSettings);
        // 3 saved widgets (stats, recent-series, custom) + 3 missing defaults (quick-actions, node-info, users-overview) = 6
        Assert.Equal(6, loadedSettings.Widgets.Count);
        
        var statsWidget = loadedSettings.Widgets.First(w => w.Id == "stats");
        Assert.False(statsWidget.IsVisible);
        Assert.Equal(2, statsWidget.Order);
        
        _output.WriteLine($"✓ Round-trip preserved 3 widgets, merged to {loadedSettings.Widgets.Count}");
    }

    /// <summary>
    /// Tests that widget ordering is preserved after save/load cycle.
    /// </summary>
    [Fact]
    public async Task SaveAndLoad_PreservesWidgetOrdering()
    {
        // Arrange - Use actual default widget IDs from DashboardSettingsService
        var settings = new DashboardSettings
        {
            Widgets = new List<WidgetConfig>
            {
                new() { Id = "node-info", Name = "Node Information", Icon = "Info", IsVisible = true, Order = 2 },
                new() { Id = "stats", Name = "Statistics", Icon = "Dashboard", IsVisible = true, Order = 0 },
                new() { Id = "recent-series", Name = "Recent Series", Icon = "MenuBook", IsVisible = true, Order = 1 },
                new() { Id = "quick-actions", Name = "Quick Actions", Icon = "FlashOn", IsVisible = true, Order = 3 },
                new() { Id = "users-overview", Name = "Users Overview", Icon = "People", IsVisible = true, Order = 4 }
            }
        };

        // Act
        await _service.SaveSettingsAsync(settings);
        var loadedSettings = await _service.GetSettingsAsync();

        // Assert
        var orderedWidgets = loadedSettings.Widgets.OrderBy(w => w.Order).ToList();
        Assert.Equal("stats", orderedWidgets[0].Id);
        Assert.Equal("recent-series", orderedWidgets[1].Id);
        Assert.Equal("node-info", orderedWidgets[2].Id);
        
        _output.WriteLine("✓ Widget ordering preserved correctly");
    }

    #endregion
}

/// <summary>
/// Test implementation of IJSRuntime for unit testing.
/// Simulates browser local storage behavior without requiring actual JS runtime.
/// </summary>
internal class TestJSRuntime : IJSRuntime
{
    private string? _storageValue;
    
    public string? SavedValue { get; private set; }
    public bool WasRemoveCalled { get; private set; }
    public bool ThrowOnNextCall { get; set; }
    public bool ThrowOnRemove { get; set; }

    public void SetStorageValue(string? value)
    {
        _storageValue = value;
    }

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
    {
        if (ThrowOnNextCall)
        {
            ThrowOnNextCall = false;
            throw new JSException("Test JS exception");
        }

        if (identifier == "localStorage.getItem")
        {
            return new ValueTask<TValue>((TValue)(object)_storageValue!);
        }

        if (identifier == "localStorage.setItem" && args != null && args.Length == 2)
        {
            SavedValue = args[1]?.ToString();
            _storageValue = SavedValue;
        }

        if (identifier == "localStorage.removeItem")
        {
            if (ThrowOnRemove)
            {
                throw new JSException("Test remove exception");
            }
            WasRemoveCalled = true;
            _storageValue = null;
        }

        return new ValueTask<TValue>(default(TValue)!);
    }

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
    {
        return InvokeAsync<TValue>(identifier, args);
    }
}
