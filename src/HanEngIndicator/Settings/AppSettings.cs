using System.Text.Json;
using System.Text.Json.Serialization;

namespace HanEngIndicator.Settings;

public enum PositionMode
{
    /// <summary>Prefer the caret; fall back to the mouse pointer.</summary>
    CaretThenMouse = 0,

    /// <summary>Always follow the mouse pointer.</summary>
    Mouse,

    /// <summary>Pin to a fixed screen corner.</summary>
    FixedCorner,
}

public enum ScreenCorner
{
    BottomRight = 0,
    BottomLeft,
    TopRight,
    TopLeft,
}

public enum DisplayPolicy
{
    /// <summary>Show the badge for both Korean and English states.</summary>
    Always = 0,

    /// <summary>Only show the badge while in Korean (가) mode.</summary>
    KoreanOnly,
}

/// <summary>
/// User-configurable settings. Persisted as JSON under
/// %LOCALAPPDATA%\HanEngIndicator\settings.json.
///
/// IMPORTANT: This file stores ONLY preferences. No patient data, no typed
/// text, and no keystrokes are ever written here.
/// </summary>
public sealed class AppSettings
{
    public bool Enabled { get; set; } = true;

    public DisplayPolicy DisplayPolicy { get; set; } = DisplayPolicy.Always;

    public PositionMode PositionMode { get; set; } = PositionMode.CaretThenMouse;

    public ScreenCorner FixedCorner { get; set; } = ScreenCorner.BottomRight;

    /// <summary>Badge size scale relative to the 96-DPI base size. 1.0 = ~28px.</summary>
    public double FontScale { get; set; } = 1.0;

    /// <summary>Overlay opacity, 0.2 - 1.0.</summary>
    public double Opacity { get; set; } = 0.92;

    /// <summary>Horizontal offset (logical px @96dpi) from the anchor.</summary>
    public int OffsetX { get; set; } = 12;

    /// <summary>Vertical offset (logical px @96dpi) from the anchor.</summary>
    public int OffsetY { get; set; } = 10;

    /// <summary>Polling interval in milliseconds (50 - 500).</summary>
    public int PollingIntervalMs { get; set; } = 120;

    /// <summary>Start automatically when Windows starts (HKCU Run key).</summary>
    public bool AutoStart { get; set; } = false;

    /// <summary>
    /// When true, write non-sensitive diagnostic lines (window class, thread id,
    /// layout id, IME status, caret-detection result). Never logs typed text.
    /// </summary>
    public bool DiagnosticLogging { get; set; } = false;

    // ----- persistence ------------------------------------------------------

    [JsonIgnore]
    public static string SettingsDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HanEngIndicator");

    [JsonIgnore]
    public static string SettingsFilePath =>
        Path.Combine(SettingsDirectory, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                string json = File.ReadAllText(SettingsFilePath);
                AppSettings? loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (loaded is not null)
                {
                    return loaded.Clamped();
                }
            }
        }
        catch
        {
            // Corrupt or unreadable settings should never crash the app;
            // fall back to defaults.
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            string json = JsonSerializer.Serialize(this, JsonOptions);
            File.WriteAllText(SettingsFilePath, json);
        }
        catch
        {
            // Saving is best-effort. Failing to persist preferences must not
            // interrupt the user's work.
        }
    }

    /// <summary>Clamp every value into a sane range. Returns this for chaining.</summary>
    public AppSettings Clamped()
    {
        FontScale = Math.Clamp(FontScale, 0.6, 3.0);
        Opacity = Math.Clamp(Opacity, 0.2, 1.0);
        OffsetX = Math.Clamp(OffsetX, -200, 200);
        OffsetY = Math.Clamp(OffsetY, -200, 200);
        PollingIntervalMs = Math.Clamp(PollingIntervalMs, 50, 500);
        return this;
    }
}
