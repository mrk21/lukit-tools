using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Lukit.Imaging;
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

        Title = "Lukit Tools Settings";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;

        var root = new StackPanel { Margin = new Thickness(16) };

        root.Children.Add(Header("HDR → SDR tone mapping"));

        _autoWhite = new CheckBox { Content = "Auto-detect SDR white level from display (recommended)", IsChecked = settings.AutoSdrWhite, Margin = new Thickness(0, 4, 0, 4) };
        _autoWhite.Checked += (_, _) => UpdateEnabled();
        _autoWhite.Unchecked += (_, _) => UpdateEnabled();
        root.Children.Add(_autoWhite);

        _manualWhite = new TextBox { Text = settings.ManualSdrWhiteNits.ToString(CultureInfo.InvariantCulture), Width = 80 };
        root.Children.Add(Row("Manual SDR white (nits):", _manualWhite));

        _operator = new ComboBox { Width = 160 };
        foreach (ToneMapOperator op in Enum.GetValues<ToneMapOperator>())
            _operator.Items.Add(op);
        _operator.SelectedItem = settings.Operator;
        root.Children.Add(Row("Tone map operator:", _operator));

        root.Children.Add(Header("Output"));
        _copyClipboard = Check("Copy to clipboard", settings.CopyToClipboard);
        _saveFile = Check("Save PNG to folder", settings.SaveToFile);
        _includeCursor = Check("Include mouse cursor", settings.IncludeCursor);
        _showNotifications = Check("Show notifications", settings.ShowNotifications);
        root.Children.Add(_copyClipboard);
        root.Children.Add(_saveFile);
        root.Children.Add(_includeCursor);
        root.Children.Add(_showNotifications);

        _saveFolder = new TextBox { Text = settings.ResolvedSaveFolder, Width = 280 };
        var browse = new Button { Content = "Browse…", Width = 70, Margin = new Thickness(6, 0, 0, 0) };
        browse.Click += (_, _) => BrowseFolder();
        var folderRow = Row("Save folder:", _saveFolder);
        ((StackPanel)folderRow).Children.Add(browse);
        root.Children.Add(folderRow);

        root.Children.Add(Header("Global hotkeys (restart to apply changes)"));
        _hkFull = new TextBox { Text = settings.HotkeyFullscreen, Width = 140 };
        _hkRegion = new TextBox { Text = settings.HotkeyRegion, Width = 140 };
        _hkWindow = new TextBox { Text = settings.HotkeyWindow, Width = 140 };
        root.Children.Add(Row("Full screen:", _hkFull));
        root.Children.Add(Row("Region:", _hkRegion));
        root.Children.Add(Row("Window:", _hkWindow));

        var save = new Button { Content = "Save", Width = 90, Margin = new Thickness(0, 12, 8, 0), IsDefault = true };
        save.Click += (_, _) => SaveAndClose();
        var cancel = new Button { Content = "Cancel", Width = 90, Margin = new Thickness(0, 12, 0, 0), IsCancel = true };
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

    private static Panel Row(string label, UIElement control)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
        panel.Children.Add(new TextBlock { Text = label, Width = 150, VerticalAlignment = VerticalAlignment.Center });
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
