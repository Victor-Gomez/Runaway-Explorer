using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using RunawayExplorer.Core.FileSystem;
using RunawayExplorer.Core.Settings;
using RunawayExplorer.Services;

namespace RunawayExplorer.Views;

/// <summary>
/// In-app settings overlay: a Windows-Settings-style left-nav + content pane, rendered on top of
/// <see cref="MainWindow"/> rather than as a separate window. Directly mutates the shared
/// <see cref="AppSettings"/> and calls back into <see cref="MainWindow"/> for any change that
/// needs the running app to react (theme, playback rate, background toggle).
/// </summary>
public partial class SettingsPanel : UserControl
{
    private MainWindow? _owner;
    private AppSettings? _settings;
    // Suppress change handlers while we sync controls to loaded settings, so opening the panel
    // doesn't re-persist values or trigger reloads.
    private bool _initializing;

    public SettingsPanel()
    {
        InitializeComponent();
    }

    /// <summary>Show the panel bound to the given host and settings, initializing controls from them.</summary>
    public void Show(MainWindow owner, AppSettings settings)
    {
        _owner = owner;
        _settings = settings;
        Populate();
        IsVisible = true;
        Focus();
    }

    public void Hide() => IsVisible = false;

    private void Populate()
    {
        if (_settings is null) return;

        _initializing = true;
        try
        {
            FpsBox.Value = (decimal)_settings.AnimationFps;
            LoopAnimationCheck.IsChecked = _settings.LoopAnimation;
            AutoPlayAnimationCheck.IsChecked = _settings.AutoPlayAnimation;
            string bgMode = _settings.BackgroundMode.ToLowerInvariant();
            SettingsBgNoRadio.IsChecked = bgMode == "no";
            SettingsBgYesRadio.IsChecked = bgMode == "yes";
            SettingsBgGreyedRadio.IsChecked = bgMode == "greyed";
            VoiceRateBox.Value = _settings.VoiceSampleRate;
            UseScanCacheCheck.IsChecked = _settings.UseScanCache;

            LanguageCombo.SelectedIndex = LocalizationManager.NormalizeLanguage(_settings.Language) == "es" ? 1 : 0;

            ThemeCombo.SelectedIndex = _settings.Theme switch
            {
                "System" => 0,
                "Light" => 1,
                _ => 2,
            };

            // "Ask" (first launch, question not yet answered) shows as the recommended startup check;
            // whichever way the user leaves this combo persists a real answer, retiring the prompt.
            UpdateCheckCombo.SelectedIndex = _settings.UpdateCheckMode == "Never" ? 1 : 0;
            UpdateLastCheckText();
            UpdateScanCacheText();
        }
        finally
        {
            _initializing = false;
        }
    }

    private void CategoryList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (CategoryList.SelectedItem is not ListBoxItem { Tag: string tag })
            return;
        // The first ListBoxItem's IsSelected="True" fires SelectionChanged during XAML parse,
        // before the sections declared after the ListBox have been constructed.
        if (PlaybackSection is null)
            return;
        PlaybackSection.IsVisible = tag == "Playback";
        ExportSection.IsVisible = tag == "Export";
        ScanningSection.IsVisible = tag == "Scanning";
        AppearanceSection.IsVisible = tag == "Appearance";
        UpdatesSection.IsVisible = tag == "Updates";
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Hide();

    // Click on the scrim (outside the card) closes; the card's own PointerPressed marks the
    // event handled so clicks inside don't bubble out and dismiss the panel.
    private void Scrim_PointerPressed(object? sender, PointerPressedEventArgs e) => Hide();
    private void Card_PointerPressed(object? sender, PointerPressedEventArgs e) => e.Handled = true;

    private void Fps_ValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_initializing || _settings is null || _owner is null || FpsBox.Value is not { } value) return;
        _settings.AnimationFps = Math.Clamp((double)value, 1, 60);
        _settings.Save();
        _owner.OnAnimationFpsChanged();
    }

    private void LoopAnimation_Changed(object? sender, RoutedEventArgs e)
    {
        if (_initializing || _settings is null || _owner is null) return;
        _settings.LoopAnimation = LoopAnimationCheck.IsChecked == true;
        _settings.Save();
        _owner.OnLoopAnimationChanged();
    }

    private void AutoPlayAnimation_Changed(object? sender, RoutedEventArgs e)
    {
        if (_initializing || _settings is null) return;
        _settings.AutoPlayAnimation = AutoPlayAnimationCheck.IsChecked == true;
        _settings.Save();
    }

    private void SettingsBgRadio_Click(object? sender, RoutedEventArgs e)
    {
        if (_initializing || _settings is null || _owner is null) return;
        if (sender is RadioButton { Tag: string mode })
        {
            _settings.BackgroundMode = mode;
            _settings.Save();
            _owner.OnShowOnBackgroundChanged();
        }
    }

    private void VoiceRate_ValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_initializing || _settings is null || VoiceRateBox.Value is not { } value) return;
        _settings.VoiceSampleRate = (int)Math.Clamp(value, 8000, 48000);
        _settings.Save();
    }

    private void UseScanCache_Changed(object? sender, RoutedEventArgs e)
    {
        if (_initializing || _settings is null) return;
        _settings.UseScanCache = UseScanCacheCheck.IsChecked == true;
        _settings.Save();
    }

    private async void ClearScanCache_Click(object? sender, RoutedEventArgs e)
    {
        if (_owner is null) return;
        Hide();
        await _owner.ClearScanCacheAndReloadAsync();
    }

    private void UpdateScanCacheText()
    {
        string path = ScanCache.DefaultPath;
        ScanCacheText.Text = System.IO.File.Exists(path)
            ? $"Forget every cached classification and scan the open install again. Cache: {path}"
            : "No cache has been written yet.";
    }

    private void Language_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_initializing || _settings is null || _owner is null) return;
        if (LanguageCombo.SelectedItem is ComboBoxItem { Tag: string lang })
        {
            _settings.Language = lang;
            _settings.Save();
            _owner.ApplyLanguage(lang);
        }
    }

    private void Theme_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_initializing || _settings is null || _owner is null) return;
        if (ThemeCombo.SelectedItem is ComboBoxItem { Tag: string theme })
        {
            _settings.Theme = theme;
            _settings.Save();
            _owner.ApplyTheme(theme);
        }
    }

    private void UpdateCheck_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_initializing || _settings is null) return;
        if (UpdateCheckCombo.SelectedItem is ComboBoxItem { Tag: string mode })
        {
            _settings.UpdateCheckMode = mode;
            _settings.Save();
        }
    }

    private async void CheckForUpdatesNow_Click(object? sender, RoutedEventArgs e)
    {
        if (_owner is null || _settings is null) return;

        CheckNowButton.IsEnabled = false;
        try
        {
            await UpdateUi.CheckInteractiveAsync(_owner, _settings);
        }
        finally
        {
            CheckNowButton.IsEnabled = true;
            UpdateLastCheckText();
        }
    }

    private void UpdateLastCheckText() =>
        LastUpdateCheckText.Text = _settings?.LastUpdateCheckUtc is { } last
            ? $"Version {AppInfo.DisplayVersion}. Last checked {last.ToLocalTime():g}."
            : $"Version {AppInfo.DisplayVersion}. Never checked.";
}
