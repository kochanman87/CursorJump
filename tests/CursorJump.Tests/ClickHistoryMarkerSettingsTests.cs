using System.Text.Json;
using CursorJump.App.Models;
using Xunit;

namespace CursorJump.Tests;

public class ClickHistoryMarkerSettingsTests
{
    [Fact]
    public void Old_settings_keep_history_depth_and_get_faint_marker_defaults()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("{\"ClickHistoryDepth\":5}")!;
        Assert.Equal(5, settings.ClickHistoryDepth);
        Assert.True(settings.ClickHistoryMarkersEnabled);
        Assert.Equal("#5BA8F0", settings.ClickHistoryMarkerColor);
        Assert.Equal(0.35, settings.ClickHistoryMarkerOpacity);
    }

    [Fact]
    public void Marker_preferences_survive_cloning_and_json_round_trip()
    {
        var settings = new AppSettings
        {
            ClickHistoryMarkersEnabled = false,
            ClickHistoryMarkerColor = "#AABBCC",
            ClickHistoryMarkerOpacity = 0.65
        };
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings.Clone()))!;
        Assert.False(restored.ClickHistoryMarkersEnabled);
        Assert.Equal("#AABBCC", restored.ClickHistoryMarkerColor);
        Assert.Equal(0.65, restored.ClickHistoryMarkerOpacity);
    }
}
