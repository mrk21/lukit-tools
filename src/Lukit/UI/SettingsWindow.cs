using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Lukit.Imaging;
using Lukit.Localization;
using Lukit.Settings;
using WF = System.Windows.Forms;

namespace Lukit.UI;

/// <summary>
/// A compact, code-only settings window. Kept XAML-free so the tray app has no
/// StartupUri / ApplicationDefinition to conflict with the custom entry point.
/// </summary>
public sealed class SettingsWindow : Window
{
    private readonly AppSettings _settings;

    private readonly ComboBox _language;
    private readonly (LanguagePreference Pref, string Label)[] _languageChoices;
    private readonly CheckBox _autoWhite;
    private readonly TextBox _manualWhite;
    private readonly ComboBox _operator;
    private readonly CheckBox _includeCursor;
    private readonly CheckBox _copyClipboard;
    private readonly CheckBox _saveFile;
    private readonly CheckBox _showNotifications;
    private readonly TextBox _saveFolder;
    private readonly TextBox _hkFull;
    private readonly TextBox _hkRegion;
    private readonly TextBox _hkWindow;

    public SettingsWindow(AppSettings settings)
    {
        _settings = settings;

        Title = Strings.SettingsTitle;
        Width = 460;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;

        var root = new StackPanel { Margin = new Thickness(16) };

        // "English" / "日本語" are shown in their own script (standard for a language picker);
        // only the "Auto" entry is localized.
        _languageChoices = new[]
        {
            (LanguagePreference.Auto, Strings.LanguageAuto),
            (LanguagePreference.English, "English"),
            (LanguagePreference.Japanese, "日本語"),
        };
        _language = new ComboBox { Width = 200 };
        foreach (var choice in _languageChoices)
            _language.Items.Add(choice.Label);
        int selected = Array.FindIndex(_languageChoices, c => c.Pref == settings.Language);
        _language.SelectedIndex = selected < 0 ? 0 : selected;
        root.Children.Add(Header(Strings.SectionLanguage));
        root.Children.Add(Row(Strings.LanguageLabel, _language));

        root.Children.Add(Header(Strings.SectionToneMapping));

        _autoWhite = new CheckBox { Content = Strings.AutoDetectSdrWhite, IsChecked = settings.AutoSdrWhite, Margin = new Thickness(0, 4, 0, 4) };
        _autoWhite.Checked += (_, _) => UpdateEnabled();
        _autoWhite.Unchecked += (_, _) => UpdateEnabled();
        root.Children.Add(_autoWhite);

        _manualWhite = new TextBox { Text = settings.ManualSdrWhiteNits.ToString(CultureInfo.InvariantCulture), Width = 80 };
        root.Children.Add(Row(Strings.ManualSdrWhite, _manualWhite));

        _operator = new ComboBox { Width = 160 };
        foreach (ToneMapOperator op in Enum.GetValues<ToneMapOperator>())
            _operator.Items.Add(op);
        _operator.SelectedItem = settings.Operator;
        root.Children.Add(Row(Strings.ToneMapOperatorLabel, _operator));

        root.Children.Add(Header(Strings.SectionOutput));
        _copyClipboard = Check(Strings.CopyToClipboardOption, settings.CopyToClipboard);
        _saveFile = Check(Strings.SavePngOption, settings.SaveToFile);
        _includeCursor = Check(Strings.IncludeCursorOption, settings.IncludeCursor);
        _showNotifications = Check(Strings.ShowNotificationsOption, settings.ShowNotifications);
        root.Children.Add(_copyClipboard);
        root.Children.Add(_saveFile);
        root.Children.Add(_includeCursor);
        root.Children.Add(_showNotifications);

        _saveFolder = new TextBox { Text = settings.ResolvedSaveFolder, Width = 280 };
        var browse = new Button { Content = Strings.Browse, Width = 70, Margin = new Thickness(6, 0, 0, 0) };
        browse.Click += (_, _) => BrowseFolder();
        var folderRow = Row(Strings.SaveFolderLabel, _saveFolder);
        ((StackPanel)folderRow).Children.Add(browse);
        root.Children.Add(folderRow);

        root.Children.Add(Header(Strings.SectionHotkeys));
        _hkFull = new TextBox { Text = settings.HotkeyFullscreen, Width = 140 };
        _hkRegion = new TextBox { Text = settings.HotkeyRegion, Width = 140 };
        _hkWindow = new TextBox { Text = settings.HotkeyWindow, Width = 140 };
        root.Children.Add(Row(Strings.HotkeyFullScreenLabel, _hkFull));
        root.Children.Add(Row(Strings.HotkeyRegionLabel, _hkRegion));
        root.Children.Add(Row(Strings.HotkeyWindowLabel, _hkWindow));

        var save = new Button { Content = Strings.Save, Width = 90, Margin = new Thickness(0, 12, 8, 0), IsDefault = true };
        save.Click += (_, _) => SaveAndClose();
        var cancel = new Button { Content = Strings.Cancel, Width = 90, Margin = new Thickness(0, 12, 0, 0), IsCancel = true };
        cancel.Click += (_, _) => Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);

        Content = root;
        UpdateEnabled();
    }

    private void UpdateEnabled() => _manualWhite.IsEnabled = _autoWhite.IsChecked != true;

    private static TextBlock Header(string text) => new()
    {
        Text = text,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 12, 0, 6),
    };

    private static CheckBox Check(string text, bool value) => new() { Content = text, IsChecked = value, Margin = new Thickness(0, 2, 0, 2) };

    // Japanese labels use wider full-width glyphs, so give the label column more room
    // there to keep the longest label ("手動 SDR 白色輝度 (nits)：") from clipping.
    private static readonly double LabelWidth = Strings.Language == AppLanguage.Japanese ? 176 : 150;

    private static Panel Row(string label, UIElement control)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
        panel.Children.Add(new TextBlock { Text = label, Width = LabelWidth, VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(control);
        return panel;
    }

    private void BrowseFolder()
    {
        using var dialog = new WF.FolderBrowserDialog { SelectedPath = _saveFolder.Text };
        if (dialog.ShowDialog() == WF.DialogResult.OK)
            _saveFolder.Text = dialog.SelectedPath;
    }

    private void SaveAndClose()
    {
        if (_language.SelectedIndex >= 0)
            _settings.Language = _languageChoices[_language.SelectedIndex].Pref;
        _settings.AutoSdrWhite = _autoWhite.IsChecked == true;
        if (float.TryParse(_manualWhite.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float nits))
            _settings.ManualSdrWhiteNits = Math.Clamp(nits, 1f, 10000f);
        if (_operator.SelectedItem is ToneMapOperator op)
            _settings.Operator = op;
        _settings.CopyToClipboard = _copyClipboard.IsChecked == true;
        _settings.SaveToFile = _saveFile.IsChecked == true;
        _settings.IncludeCursor = _includeCursor.IsChecked == true;
        _settings.ShowNotifications = _showNotifications.IsChecked == true;
        _settings.SaveFolder = _saveFolder.Text.Trim();
        _settings.HotkeyFullscreen = _hkFull.Text.Trim();
        _settings.HotkeyRegion = _hkRegion.Text.Trim();
        _settings.HotkeyWindow = _hkWindow.Text.Trim();

        _settings.Save();
        Close();
    }
}
