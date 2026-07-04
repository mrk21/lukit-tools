using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lukit.Imaging;

namespace Lukit.Settings;

/// <summary>
/// User-configurable settings, persisted as JSON under %APPDATA%\Lukit\settings.json.
/// </summary>
public sealed class AppSettings
{
    // --- Tone mapping ---

    /// <summary>
    /// When true, the SDR white level is read from the display (recommended). When
    /// false, <see cref="ManualSdrWhiteNits"/> is used.
    /// </summary>
    public bool AutoSdrWhite { get; set; } = true;

    /// <summary>Manual SDR white level in nits, used when <see cref="AutoSdrWhite"/> is false.</summary>
    public float ManualSdrWhiteNits { get; set; } = 200f;

    public ToneMapOperator Operator { get; set; } = ToneMapOperator.Reinhard;

    // --- Output ---

    public bool CopyToClipboard { get; set; } = true;
    public bool SaveToFile { get; set; } = true;

    /// <summary>Folder screenshots are saved to. Empty => Pictures\Lukit.</summary>
    public string SaveFolder { get; set; } = string.Empty;

    /// <summary>Whether to include the mouse cursor in captures.</summary>
    public bool IncludeCursor { get; set; } = false;

    /// <summary>Show a tray balloon notification after each capture.</summary>
    public bool ShowNotifications { get; set; } = true;

    // --- Hotkeys (see Hotkey.Parse for the string format, e.g. "Ctrl+Alt+2") ---

    public string HotkeyFullscreen { get; set; } = "Ctrl+Alt+1";
    public string HotkeyRegion { get; set; } = "Ctrl+Alt+2";
    public string HotkeyWindow { get; set; } = "Ctrl+Alt+3";

    // --- Persistence ---

    [JsonIgnore]
    public string ResolvedSaveFolder =>
        string.IsNullOrWhiteSpace(SaveFolder)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Lukit")
            : SaveFolder;

    [JsonIgnore]
    public static string SettingsPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lukit", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                string json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
        }
        catch
        {
            // Corrupt or unreadable settings fall back to defaults.
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            string dir = Path.GetDirectoryName(SettingsPath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch
        {
            // Best-effort; ignore write failures.
        }
    }

    /// <summary>Effective SDR white for a display with the given detected value.</summary>
    public float EffectiveSdrWhite(float detected) => AutoSdrWhite ? detected : ManualSdrWhiteNits;

    public ToneMapSettings ToToneMapSettings(float detectedSdrWhite) => new()
    {
        SdrWhiteNits = EffectiveSdrWhite(detectedSdrWhite),
        Operator = Operator,
    };
}
