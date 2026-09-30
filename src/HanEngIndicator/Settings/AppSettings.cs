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

    /// <summary>Overlay opacity, 0.2 - 1.0. Menu offers 50% / 75% / 100%.</summary>
    public double Opacity { get; set; } = 1.0;

    /// <summary>Horizontal offset (logical px @96dpi) from the anchor.</summary>
    public int OffsetX { get; set; } = 12;

    /// <summary>Vertical offset (logical px @96dpi) from the anchor.</summary>
    public int OffsetY { get; set; } = 10;

    /// <summary>Polling interval in milliseconds (50 - 500).</summary>
    public int PollingIntervalMs { get; set; } = 120;

    /// <summary>
    /// Use UI Automation to find the caret in controls that do not expose a
    /// standard Win32 caret (browsers, some custom chart controls). If a
    /// program's UIA provider is slow/unresponsive this can be turned off to
    /// fall back to the mouse pointer. A per-session circuit breaker also
    /// disables it automatically if it is repeatedly slow.
    /// </summary>
    public bool UseUiAutomation { get; set; } = false;

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

    public static AppSettings Load(string? filePath = null)
    {
        string path = filePath ?? SettingsFilePath;
        foreach (string candidate in new[] { path, path + ".bak" })
        {
            try
            {
                if (File.Exists(candidate))
                {
                    AppSettings? loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(candidate), JsonOptions);
                    if (loaded is not null) return loaded.Clamped();
                }
            }
            catch { /* Try the last valid backup before falling back. */ }
        }
        return new AppSettings();
    }

    public bool Save(string? filePath = null)
    {
        string path = filePath ?? SettingsFilePath;
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            byte[] json = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(Clamped(), JsonOptions));
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(json);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(path))
                File.Replace(temporary, path, path + ".bak");
            else
                File.Move(temporary, path);
            return true;
        }
        catch { return false; }
        finally
        {
            try { File.Delete(temporary); } catch { }
        }
    }

    /// <summary>Clamp every value into a sane range. Returns this for chaining.</summary>
    public AppSettings Clamped()
    {
        FontScale = double.IsFinite(FontScale) ? Math.Clamp(FontScale, 0.6, 3.0) : 1.0;
        Opacity = double.IsFinite(Opacity) ? Math.Clamp(Opacity, 0.2, 1.0) : 1.0;
        if (!Enum.IsDefined(DisplayPolicy)) DisplayPolicy = DisplayPolicy.Always;
        if (!Enum.IsDefined(PositionMode)) PositionMode = PositionMode.CaretThenMouse;
        if (!Enum.IsDefined(FixedCorner)) FixedCorner = ScreenCorner.BottomRight;
        OffsetX = Math.Clamp(OffsetX, -200, 200);
        OffsetY = Math.Clamp(OffsetY, -200, 200);
        PollingIntervalMs = Math.Clamp(PollingIntervalMs, 50, 500);
        return this;
    }
}

