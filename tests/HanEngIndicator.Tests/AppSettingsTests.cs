using System.Text.Json;
using HanEngIndicator.Settings;
using Xunit;

namespace HanEngIndicator.Tests;

public class AppSettingsTests
{
    [Fact]
    public void Defaults_are_sensible()
    {
        var s = new AppSettings();
        Assert.True(s.Enabled);
        Assert.Equal(DisplayPolicy.Always, s.DisplayPolicy);
        Assert.Equal(PositionMode.CaretThenMouse, s.PositionMode);
        Assert.InRange(s.PollingIntervalMs, 50, 500);
        Assert.False(s.DiagnosticLogging);
        Assert.False(s.AutoStart);
    }

    [Fact]
    public void Clamped_constrains_out_of_range_values()
    {
        var s = new AppSettings
        {
            FontScale = 99,
            Opacity = 5,
            OffsetX = 9999,
            OffsetY = -9999,
            PollingIntervalMs = 1,
        };

        s.Clamped();

        Assert.InRange(s.FontScale, 0.6, 3.0);
        Assert.InRange(s.Opacity, 0.2, 1.0);
        Assert.InRange(s.OffsetX, -200, 200);
        Assert.InRange(s.OffsetY, -200, 200);
        Assert.InRange(s.PollingIntervalMs, 50, 500);
    }

    [Fact]
    public void Round_trips_through_json_with_enum_names()
    {
        var s = new AppSettings
        {
            DisplayPolicy = DisplayPolicy.KoreanOnly,
            PositionMode = PositionMode.FixedCorner,
            FixedCorner = ScreenCorner.TopLeft,
            FontScale = 1.25,
            Opacity = 0.75,
        };

        var options = new JsonSerializerOptions
        {
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
        };

        string json = JsonSerializer.Serialize(s, options);

        // Enums persisted as readable names, not integers.
        Assert.Contains("KoreanOnly", json);
        Assert.Contains("FixedCorner", json);
        Assert.Contains("TopLeft", json);

        AppSettings? back = JsonSerializer.Deserialize<AppSettings>(json, options);
        Assert.NotNull(back);
        Assert.Equal(DisplayPolicy.KoreanOnly, back!.DisplayPolicy);
        Assert.Equal(PositionMode.FixedCorner, back.PositionMode);
        Assert.Equal(ScreenCorner.TopLeft, back.FixedCorner);
        Assert.Equal(1.25, back.FontScale);
    }

    [Fact]
    public void Settings_path_is_under_local_appdata_haengindicator()
    {
        Assert.EndsWith(Path.Combine("HanEngIndicator", "settings.json"), AppSettings.SettingsFilePath);
    }
}
