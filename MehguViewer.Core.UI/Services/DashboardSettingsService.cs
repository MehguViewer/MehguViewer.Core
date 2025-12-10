using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using System.Text.Json;

namespace MehguViewer.Core.UI.Services;

/// <summary>
/// Service for managing dashboard widget settings with persistence in browser local storage.
/// Provides secure, optimized access to user-customizable dashboard configurations.
/// </summary>
/// <remarks>
/// This service handles:
/// - Loading user dashboard preferences from local storage
/// - Saving widget visibility and order settings
/// - Merging saved settings with default configurations
/// - Validating settings for security and integrity
/// Thread-safe for concurrent access via IJSRuntime isolation.
/// </remarks>
public class DashboardSettingsService
{
    private readonly IJSRuntime _jsRuntime;
    private readonly ILogger<DashboardSettingsService> _logger;
    private const string StorageKey = "dashboardSettings";
    private const int MaxWidgets = 50; // Security: Prevent excessive widget configurations
    private const int MaxJsonSize = 10240; // Security: 10KB limit to prevent storage abuse
    
    /// <summary>
    /// Initializes a new instance of DashboardSettingsService.
    /// </summary>
    /// <param name="jsRuntime">JavaScript runtime for local storage access.</param>
    /// <param name="logger">Logger instance for structured logging.</param>
    /// <exception cref="ArgumentNullException">Thrown when jsRuntime or logger is null.</exception>
    public DashboardSettingsService(IJSRuntime jsRuntime, ILogger<DashboardSettingsService> logger)
    {
        _jsRuntime = jsRuntime ?? throw new ArgumentNullException(nameof(jsRuntime));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        _logger.LogDebug("DashboardSettingsService initialized with storage key: {StorageKey}", StorageKey);
    }
    
    /// <summary>
    /// Retrieves dashboard settings from local storage, merging with defaults for new widgets.
    /// </summary>
    /// <returns>
    /// Dashboard settings with all widgets (saved + new defaults).
    /// Returns default settings if no saved configuration exists or on error.
    /// </returns>
    /// <remarks>
    /// Performance: Cached in browser, no server round-trip.
    /// Security: Validates JSON size and widget count to prevent abuse.
    /// </remarks>
    public async Task<DashboardSettings> GetSettingsAsync()
    {
        _logger.LogInformation("Retrieving dashboard settings from local storage");
        
        try
        {
            var json = await _jsRuntime.InvokeAsync<string>("localStorage.getItem", StorageKey);
            
            if (string.IsNullOrWhiteSpace(json))
            {
                _logger.LogDebug("No saved dashboard settings found, returning defaults");
                return GetDefaultSettings();
            }
            
            // Security: Validate JSON size to prevent storage abuse
            if (json.Length > MaxJsonSize)
            {
                _logger.LogWarning(
                    "Dashboard settings JSON exceeds maximum size. Size: {Size}, Max: {MaxSize}. Using defaults",
                    json.Length,
                    MaxJsonSize);
                return GetDefaultSettings();
            }
            
            var savedSettings = JsonSerializer.Deserialize<DashboardSettings>(json, GetJsonOptions());
            
            if (savedSettings == null || savedSettings.Widgets == null)
            {
                _logger.LogWarning("Failed to deserialize dashboard settings, using defaults");
                return GetDefaultSettings();
            }
            
            // Security: Validate widget count
            if (savedSettings.Widgets.Count > MaxWidgets)
            {
                _logger.LogWarning(
                    "Dashboard settings contain too many widgets. Count: {Count}, Max: {MaxWidgets}. Truncating",
                    savedSettings.Widgets.Count,
                    MaxWidgets);
                savedSettings.Widgets = savedSettings.Widgets.Take(MaxWidgets).ToList();
            }
            
            // Merge with defaults to ensure new widgets are included
            var mergedSettings = MergeWithDefaults(savedSettings);
            
            _logger.LogInformation(
                "Successfully loaded dashboard settings with {WidgetCount} widgets ({VisibleCount} visible)",
                mergedSettings.Widgets.Count,
                mergedSettings.Widgets.Count(w => w.IsVisible));
            
            return mergedSettings;
        }
        catch (JsonException ex)
        {
            _logger.LogError(
                ex,
                "JSON deserialization error while loading dashboard settings. Returning defaults");
            return GetDefaultSettings();
        }
        catch (JSException ex)
        {
            _logger.LogError(
                ex,
                "JavaScript interop error while accessing local storage. Returning defaults");
            return GetDefaultSettings();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unexpected error while retrieving dashboard settings. Returning defaults");
            return GetDefaultSettings();
        }
    }
    
    /// <summary>
    /// Persists dashboard settings to browser local storage.
    /// </summary>
    /// <param name="settings">Dashboard settings to save. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when settings is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when settings validation fails.</exception>
    /// <remarks>
    /// Security: Validates settings before saving to prevent malicious configurations.
    /// Performance: Async write to local storage, non-blocking.
    /// </remarks>
    public async Task SaveSettingsAsync(DashboardSettings settings)
    {
        if (settings == null)
        {
            _logger.LogError("Attempted to save null dashboard settings");
            throw new ArgumentNullException(nameof(settings));
        }
        
        _logger.LogInformation(
            "Saving dashboard settings with {WidgetCount} widgets ({VisibleCount} visible)",
            settings.Widgets?.Count ?? 0,
            settings.Widgets?.Count(w => w.IsVisible) ?? 0);
        
        try
        {
            // Validate settings before saving
            ValidateSettings(settings);
            
            var json = JsonSerializer.Serialize(settings, GetJsonOptions());
            
            // Security: Double-check serialized size
            if (json.Length > MaxJsonSize)
            {
                _logger.LogError(
                    "Serialized dashboard settings exceed maximum size. Size: {Size}, Max: {MaxSize}",
                    json.Length,
                    MaxJsonSize);
                throw new InvalidOperationException($"Dashboard settings exceed maximum size of {MaxJsonSize} bytes");
            }
            
            await _jsRuntime.InvokeVoidAsync("localStorage.setItem", StorageKey, json);
            
            _logger.LogInformation("Dashboard settings saved successfully to local storage");
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON serialization error while saving dashboard settings");
            throw new InvalidOperationException("Failed to serialize dashboard settings", ex);
        }
        catch (JSException ex)
        {
            _logger.LogError(ex, "JavaScript interop error while saving to local storage");
            throw new InvalidOperationException("Failed to save dashboard settings to local storage", ex);
        }
        catch (Exception ex) when (ex is not ArgumentNullException && ex is not InvalidOperationException)
        {
            _logger.LogError(ex, "Unexpected error while saving dashboard settings");
            throw new InvalidOperationException("Failed to save dashboard settings", ex);
        }
    }
    
    /// <summary>
    /// Resets dashboard settings to defaults by clearing local storage.
    /// </summary>
    /// <returns>Default dashboard settings.</returns>
    public async Task<DashboardSettings> ResetToDefaultsAsync()
    {
        _logger.LogInformation("Resetting dashboard settings to defaults");
        
        try
        {
            await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", StorageKey);
            _logger.LogInformation("Dashboard settings reset to defaults successfully");
            return GetDefaultSettings();
        }
        catch (JSException ex)
        {
            _logger.LogError(ex, "JavaScript interop error while resetting dashboard settings");
            throw new InvalidOperationException("Failed to reset dashboard settings", ex);
        }
    }
    
    /// <summary>
    /// Merges saved settings with default settings to include new widgets.
    /// </summary>
    /// <param name="savedSettings">User's saved dashboard settings.</param>
    /// <returns>Merged settings with all available widgets.</returns>
    private DashboardSettings MergeWithDefaults(DashboardSettings savedSettings)
    {
        _logger.LogDebug("Merging saved settings with defaults");
        
        var defaults = GetDefaultSettings();
        var savedWidgetIds = new HashSet<string>(
            savedSettings.Widgets.Select(w => w.Id),
            StringComparer.OrdinalIgnoreCase);
        
        var newWidgetsAdded = 0;
        
        // Add new default widgets that aren't in saved settings
        foreach (var defaultWidget in defaults.Widgets)
        {
            if (!savedWidgetIds.Contains(defaultWidget.Id))
            {
                savedSettings.Widgets.Add(defaultWidget);
                newWidgetsAdded++;
            }
        }
        
        if (newWidgetsAdded > 0)
        {
            _logger.LogInformation(
                "Added {NewWidgetCount} new default widgets to saved settings",
                newWidgetsAdded);
        }
        
        // Sort widgets by order
        savedSettings.Widgets = savedSettings.Widgets.OrderBy(w => w.Order).ToList();
        
        return savedSettings;
    }
    
    /// <summary>
    /// Validates dashboard settings for security and integrity.
    /// </summary>
    /// <param name="settings">Settings to validate.</param>
    /// <exception cref="InvalidOperationException">Thrown when validation fails.</exception>
    private void ValidateSettings(DashboardSettings settings)
    {
        if (settings.Widgets == null)
        {
            throw new InvalidOperationException("Dashboard settings must contain a Widgets collection");
        }
        
        // Security: Limit number of widgets
        if (settings.Widgets.Count > MaxWidgets)
        {
            _logger.LogWarning(
                "Dashboard settings contain too many widgets: {Count}. Maximum allowed: {MaxWidgets}",
                settings.Widgets.Count,
                MaxWidgets);
            throw new InvalidOperationException($"Dashboard cannot contain more than {MaxWidgets} widgets");
        }
        
        // Validate each widget
        var widgetIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        foreach (var widget in settings.Widgets)
        {
            // Security: Validate widget ID
            if (string.IsNullOrWhiteSpace(widget.Id))
            {
                throw new InvalidOperationException("Widget ID cannot be null or empty");
            }
            
            // Security: Prevent XSS via widget names/icons
            if (widget.Id.Length > 100 || widget.Name.Length > 100 || widget.Icon.Length > 100)
            {
                _logger.LogWarning(
                    "Widget contains suspiciously long field. Id: {Id}, Name: {Name}, Icon: {Icon}",
                    widget.Id.Length,
                    widget.Name.Length,
                    widget.Icon.Length);
                throw new InvalidOperationException("Widget field exceeds maximum length");
            }
            
            // Security: Check for duplicate widget IDs
            if (!widgetIds.Add(widget.Id))
            {
                _logger.LogWarning("Duplicate widget ID detected: {WidgetId}", widget.Id);
                throw new InvalidOperationException($"Duplicate widget ID: {widget.Id}");
            }
            
            // Validate order range
            if (widget.Order < 0 || widget.Order > 1000)
            {
                throw new InvalidOperationException($"Widget order must be between 0 and 1000, got: {widget.Order}");
            }
        }
        
        _logger.LogDebug("Dashboard settings validation passed for {WidgetCount} widgets", settings.Widgets.Count);
    }
    
    /// <summary>
    /// Gets default dashboard settings with all available widgets.
    /// </summary>
    /// <returns>Default dashboard configuration.</returns>
    private static DashboardSettings GetDefaultSettings()
    {
        return new DashboardSettings
        {
            Widgets = new List<WidgetConfig>
            {
                new() { Id = "stats", Name = "Statistics", Icon = "Dashboard", IsVisible = true, Order = 0 },
                new() { Id = "recent-series", Name = "Recent Series", Icon = "MenuBook", IsVisible = true, Order = 1 },
                new() { Id = "quick-actions", Name = "Quick Actions", Icon = "FlashOn", IsVisible = true, Order = 2 },
                new() { Id = "node-info", Name = "Node Information", Icon = "Info", IsVisible = true, Order = 3 },
                new() { Id = "users-overview", Name = "Users Overview", Icon = "People", IsVisible = true, Order = 4 },
            }
        };
    }
    
    /// <summary>
    /// Gets JSON serialization options for consistent formatting.
    /// </summary>
    /// <returns>JSON serializer options.</returns>
    private static JsonSerializerOptions GetJsonOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false // Minimize storage size
        };
    }
}

/// <summary>
/// Represents dashboard configuration including widget visibility and ordering.
/// </summary>
public class DashboardSettings
{
    /// <summary>
    /// Gets or sets the collection of dashboard widgets.
    /// </summary>
    public List<WidgetConfig> Widgets { get; set; } = new();
}

/// <summary>
/// Represents configuration for a single dashboard widget.
/// </summary>
public class WidgetConfig
{
    /// <summary>
    /// Gets or sets the unique identifier for the widget.
    /// </summary>
    /// <remarks>Must be unique within a dashboard configuration.</remarks>
    public string Id { get; set; } = "";
    
    /// <summary>
    /// Gets or sets the display name of the widget.
    /// </summary>
    public string Name { get; set; } = "";
    
    /// <summary>
    /// Gets or sets the icon identifier for the widget.
    /// </summary>
    /// <remarks>Should correspond to Material Icons or similar icon set.</remarks>
    public string Icon { get; set; } = "";
    
    /// <summary>
    /// Gets or sets whether the widget is currently visible on the dashboard.
    /// </summary>
    public bool IsVisible { get; set; } = true;
    
    /// <summary>
    /// Gets or sets the display order of the widget (0-based index).
    /// </summary>
    /// <remarks>Lower values appear first. Must be between 0 and 1000.</remarks>
    public int Order { get; set; }
}
