using System.Text.Json;
using System.Text.Json.Serialization;

namespace GifPopup;

/// <summary>Persisted position/size of the control panel window.</summary>
public sealed class ControlWindowConfig
{
    public int X { get; set; } = 100;
    public int Y { get; set; } = 100;
    public int Width { get; set; } = 480;
    public int Height { get; set; } = 360;
}

/// <summary>How the popup is sized on the target screen.</summary>
public enum PopupSizeMode
{
    FullScreen,
    Fixed,
}

/// <summary>
/// App configuration, persisted as config.json next to the executable.
/// Tolerant of a missing or corrupt file — falls back to defaults rather
/// than crashing.
/// </summary>
public sealed class AppConfig
{
    public string GifsFolder { get; set; } = "./gifs";
    public int PopupMonitorIndex { get; set; } = 1;
    public PopupSizeMode PopupSizeMode { get; set; } = PopupSizeMode.FullScreen;
    public int PopupFixedWidth { get; set; } = 800;
    public int PopupFixedHeight { get; set; } = 600;
    public ControlWindowConfig ControlWindow { get; set; } = new();
    public Dictionary<string, string> HotkeyOverrides { get; set; } = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>Full path to config.json, next to the running executable.</summary>
    public static string ConfigPath =>
        Path.Combine(AppContext.BaseDirectory, "config.json");

    /// <summary>
    /// Loads config.json if present and valid; otherwise returns defaults
    /// and writes them out so a config.json always exists after startup.
    /// </summary>
    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
                if (config is not null)
                {
                    return config;
                }
            }
        }
        catch (Exception)
        {
            // Missing or corrupt file — fall through to defaults.
        }

        var defaults = new AppConfig();
        defaults.Save();
        return defaults;
    }

    /// <summary>Writes this configuration to config.json.</summary>
    public void Save()
    {
        var json = JsonSerializer.Serialize(this, JsonOptions);
        File.WriteAllText(ConfigPath, json);
    }
}
