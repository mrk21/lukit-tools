using System;
using System.Globalization;

namespace Lukit.Localization;

/// <summary>Supported UI languages.</summary>
public enum AppLanguage
{
    English,
    Japanese,
}

/// <summary>
/// The user's language choice as persisted in settings. <see cref="Auto"/> follows the OS
/// UI language; the others force a specific language.
/// </summary>
public enum LanguagePreference
{
    Auto,
    English,
    Japanese,
}

/// <summary>
/// The user-facing UI string catalog, with one value per <see cref="AppLanguage"/>.
///
/// Kept code-only (no .resx / satellite assemblies) to match the code-only WPF UI and to
/// embed cleanly in the self-contained single-file publish. Only GUI text lives here —
/// tray menu, settings window, capture notifications, and the single-instance dialog. The
/// CLI diagnostics (--display-info, --help, …) stay English on purpose: they are
/// developer-facing and documented in English.
/// </summary>
public static class Strings
{
    /// <summary>
    /// Maps a UI culture to a supported language. Japanese cultures (ja, ja-JP) resolve to
    /// Japanese; everything else (including unknown cultures) falls back to English. Pure —
    /// unit-tested, no dependency on the ambient <see cref="CultureInfo.CurrentUICulture"/>.
    /// </summary>
    public static AppLanguage FromCulture(CultureInfo culture)
        => culture.TwoLetterISOLanguageName.Equals("ja", StringComparison.OrdinalIgnoreCase)
            ? AppLanguage.Japanese
            : AppLanguage.English;

    /// <summary>
    /// Resolves a persisted preference to an actual language: <see cref="LanguagePreference.Auto"/>
    /// follows <paramref name="osCulture"/>; an explicit choice overrides it. Pure — unit-tested.
    /// </summary>
    public static AppLanguage Resolve(LanguagePreference preference, CultureInfo osCulture) => preference switch
    {
        LanguagePreference.English => AppLanguage.English,
        LanguagePreference.Japanese => AppLanguage.Japanese,
        _ => FromCulture(osCulture),
    };

    /// <summary>Sets <see cref="Language"/> from a persisted preference against the OS UI language.</summary>
    public static void Apply(LanguagePreference preference) => Language = Resolve(preference, CultureInfo.CurrentUICulture);

    /// <summary>
    /// The active language. Defaults to the OS UI language; call <see cref="Apply"/> with the
    /// user's saved preference at startup to override. Read by every string member below.
    /// </summary>
    public static AppLanguage Language { get; set; } = FromCulture(CultureInfo.CurrentUICulture);

    private static bool Ja => Language == AppLanguage.Japanese;

    // --- Settings window ---

    public static string SettingsTitle => Ja ? "Lukit Tools 設定" : "Lukit Tools Settings";
    public static string SectionLanguage => Ja ? "表示言語" : "Language";
    public static string LanguageLabel => Ja ? "言語：" : "Language:";
    public static string LanguageAuto => Ja ? "自動（OS 設定）" : "Automatic (OS setting)";
    public static string SectionToneMapping => Ja ? "HDR → SDR トーンマッピング" : "HDR → SDR tone mapping";
    public static string AutoDetectSdrWhite => Ja
        ? "SDR 白色輝度をディスプレイから自動検出（推奨）"
        : "Auto-detect SDR white level from display (recommended)";
    public static string ManualSdrWhite => Ja ? "手動 SDR 白色輝度 (nits)：" : "Manual SDR white (nits):";
    public static string ToneMapOperatorLabel => Ja ? "トーンマップ演算子：" : "Tone map operator:";
    public static string SectionOutput => Ja ? "出力" : "Output";
    public static string CopyToClipboardOption => Ja ? "クリップボードにコピー" : "Copy to clipboard";
    public static string SavePngOption => Ja ? "フォルダに PNG を保存" : "Save PNG to folder";
    public static string IncludeCursorOption => Ja ? "マウスカーソルを含める" : "Include mouse cursor";
    public static string ShowNotificationsOption => Ja ? "通知を表示" : "Show notifications";
    public static string Browse => Ja ? "参照…" : "Browse…";
    public static string SaveFolderLabel => Ja ? "保存先フォルダ：" : "Save folder:";
    public static string SectionHotkeys => Ja ? "グローバルホットキー" : "Global hotkeys";
    public static string HotkeyFullScreenLabel => Ja ? "画面全体：" : "Full screen:";
    public static string HotkeyRegionLabel => Ja ? "矩形選択：" : "Region:";
    public static string HotkeyWindowLabel => Ja ? "ウィンドウ：" : "Window:";
    public static string Save => Ja ? "保存" : "Save";
    public static string Cancel => Ja ? "キャンセル" : "Cancel";

    // --- Tray icon and menu ---

    public static string TrayTooltip => Ja
        ? "Lukit Tools — HDR でも色が破綻しないスクリーンショット"
        : "Lukit Tools — HDR-correct screenshots";
    public static string MenuCaptureFullScreen(string hotkey) => Ja
        ? $"画面全体をキャプチャ   ({hotkey})"
        : $"Capture full screen   ({hotkey})";
    public static string MenuCaptureRegion(string hotkey) => Ja
        ? $"矩形選択でキャプチャ   ({hotkey})"
        : $"Capture region   ({hotkey})";
    public static string MenuCaptureWindow(string hotkey) => Ja
        ? $"ウィンドウをキャプチャ   ({hotkey})"
        : $"Capture window   ({hotkey})";
    public static string MenuCaptureSpecificDisplay => Ja ? "ディスプレイを指定してキャプチャ" : "Capture specific display";
    public static string MenuLoading => Ja ? "(読み込み中…)" : "(loading…)";
    public static string MenuOpenSaveFolder => Ja ? "保存先フォルダを開く" : "Open save folder";
    public static string MenuSettings => Ja ? "設定…" : "Settings…";
    public static string MenuExit => Ja ? "終了" : "Exit";
    public static string DisplayLabel(int number, bool isPrimary, int width, int height)
    {
        string primary = isPrimary ? (Ja ? "（プライマリ）" : " (primary)") : "";
        string prefix = Ja ? "ディスプレイ" : "Display ";
        return $"{prefix}{number}{primary}  —  {width}×{height}";
    }
    public static string AllDisplaysCombined => Ja ? "全ディスプレイ（合成）" : "All displays (combined)";

    // --- Notifications / dialogs ---

    /// <summary>Title used for tray balloons and dialogs. The product name, kept as-is.</summary>
    public const string AppName = "Lukit Tools";

    public static string HotkeyUnavailable(string spec) => Ja
        ? $"ホットキー '{spec}' は使用できません（既に使用中の可能性）"
        : $"Hotkey '{spec}' is unavailable (already in use?)";
    public static string ErrorPrefix(string message) => Ja ? $"エラー: {message}" : $"Error: {message}";
    public static string CaptureFailed(string message) => Ja
        ? $"キャプチャに失敗しました: {message}"
        : $"Capture failed: {message}";
    public static string NoMonitorsFound => Ja ? "モニタが見つかりません" : "No monitors found";
    public static string NoWindowToCapture => Ja ? "キャプチャするウィンドウがありません" : "No window to capture";
    public static string SavedFile(string fileName, bool copied)
    {
        if (Ja)
            return $"{fileName} を保存しました" + (copied ? " • コピー済み" : "");
        return $"Saved {fileName}" + (copied ? " • copied" : "");
    }
    public static string CopiedToClipboard => Ja ? "クリップボードにコピーしました" : "Copied to clipboard";

    public static string AlreadyRunning => Ja
        ? "Lukit は既に起動しています。システムトレイのアイコンを確認してください。"
        : "Lukit is already running. Look for its icon in the system tray.";
    /// <summary>Short dialog title for the single-instance message. The product name.</summary>
    public const string AppShortName = "Lukit";
}
