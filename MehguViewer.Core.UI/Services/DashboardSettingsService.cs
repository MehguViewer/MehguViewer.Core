using Microsoft.JSInterop;
using System.Text.Json;

namespace MehguViewer.Core.UI.Services;

public class DashboardSettingsService
{
    private readonly IJSRuntime _jsRuntime;
    private const string StorageKey = "dashboardSettings";
    
    public DashboardSettingsService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }
    
    public async Task<DashboardSettings> GetSettingsAsync()
    {
        try
        {
            var json = await _jsRuntime.InvokeAsync<string>("localStorage.getItem", StorageKey);
            if (!string.IsNullOrEmpty(json))
            {
                return JsonSerializer.Deserialize<DashboardSettings>(json) ?? GetDefaultSettings();
            }
        }
        catch
        {
            // Return defaults on error
        }
        return GetDefaultSettings();
    }
    
    public async Task SaveSettingsAsync(DashboardSettings settings)
    {
        var json = JsonSerializer.Serialize(settings);
        await _jsRuntime.InvokeVoidAsync("localStorage.setItem", StorageKey, json);
    }
    
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
            }
        };
    }
}

public class DashboardSettings
{
    public List<WidgetConfig> Widgets { get; set; } = new();
}

public class WidgetConfig
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Icon { get; set; } = "";
    public bool IsVisible { get; set; } = true;
    public int Order { get; set; }
}
