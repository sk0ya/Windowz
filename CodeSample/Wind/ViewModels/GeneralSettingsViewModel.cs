using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using Wind.Converters;
using Wind.Services;

namespace Wind.ViewModels;

public partial class GeneralSettingsViewModel : ObservableObject
{
    private readonly SettingsManager _settingsManager;

    [ObservableProperty]
    private bool _runAtWindowsStartup;

    [ObservableProperty]
    private bool _runAsAdmin;

    [ObservableProperty]
    private bool _isRunningAsAdmin;

    [ObservableProperty]
    private string _closeWindowsOnExit = "None";

    [ObservableProperty]
    private string _tabHeaderPosition = "Top";

    [ObservableProperty]
    private string _embedCloseAction = "CloseApp";

    [ObservableProperty]
    private bool _hideEmbeddedFromTaskbar = true;

    [ObservableProperty]
    private bool _autoEmbedNewWindows = false;

    [ObservableProperty]
    private string _selectedAccentColor = "#0078D4";

    [ObservableProperty]
    private bool _useSystemAccent = false;

    [ObservableProperty]
    private string _selectedBackgroundColor = "";

    public ObservableCollection<AutoEmbedExclusionItem> AutoEmbedExclusions { get; } = new();
    public bool HasNoExclusions => AutoEmbedExclusions.Count == 0;

    public ObservableCollection<PresetColor> PresetColors { get; } = new()
    {
        new PresetColor("Blue", "#0078D4"),
        new PresetColor("Purple", "#744DA9"),
        new PresetColor("Pink", "#E3008C"),
        new PresetColor("Red", "#E81123"),
        new PresetColor("Orange", "#FF8C00"),
        new PresetColor("Yellow", "#FFB900"),
        new PresetColor("Green", "#107C10"),
        new PresetColor("Teal", "#00B294"),
    };

    public ObservableCollection<PresetColor> BackgroundPresetColors { get; } = new()
    {
        new PresetColor("Default", ""),
        new PresetColor("Dark", "#1E1E1E"),
        new PresetColor("Darker", "#0D0D0D"),
        new PresetColor("Navy", "#0A1929"),
        new PresetColor("Forest", "#0D1F0D"),
        new PresetColor("Wine", "#1F0D0D"),
        new PresetColor("Slate", "#1A1A2E"),
        new PresetColor("Charcoal", "#2D2D2D"),
        new PresetColor("Light", "#F5F5F5"),
        new PresetColor("White", "#FFFFFF"),
        new PresetColor("Cream", "#FAF8F0"),
        new PresetColor("Sky", "#E3F2FD"),
        new PresetColor("Mint", "#E8F5E9"),
        new PresetColor("Lavender", "#F3E5F5"),
        new PresetColor("Peach", "#FFF3E0"),
        new PresetColor("Silver", "#ECEFF1"),
    };

    public GeneralSettingsViewModel(SettingsManager settingsManager)
    {
        _settingsManager = settingsManager;
        _settingsManager.AutoEmbedExclusionsChanged += LoadExclusions;
        LoadSettings();
    }

    private void LoadSettings()
    {
        var settings = _settingsManager.Settings;

        RunAtWindowsStartup = _settingsManager.IsRunAtWindowsStartup();
        RunAsAdmin = settings.RunAsAdmin;
        IsRunningAsAdmin = App.IsRunningAsAdmin();
        CloseWindowsOnExit = settings.CloseWindowsOnExit;
        TabHeaderPosition = settings.TabHeaderPosition;
        EmbedCloseAction = settings.EmbedCloseAction;
        HideEmbeddedFromTaskbar = settings.HideEmbeddedFromTaskbar;
        AutoEmbedNewWindows = settings.AutoEmbedNewWindows;
        LoadExclusions();
        SelectedAccentColor = settings.AccentColor;
        UseSystemAccent = settings.UseSystemAccent;
        SelectedBackgroundColor = settings.BackgroundColor;
    }

    partial void OnRunAtWindowsStartupChanged(bool value)
    {
        _settingsManager.SetRunAtWindowsStartup(value);
    }

    partial void OnRunAsAdminChanged(bool value)
    {
        _settingsManager.Settings.RunAsAdmin = value;
        _settingsManager.SaveSettings();
    }

    partial void OnCloseWindowsOnExitChanged(string value)
    {
        _settingsManager.Settings.CloseWindowsOnExit = value;
        _settingsManager.SaveSettings();
    }

    partial void OnTabHeaderPositionChanged(string value)
    {
        _settingsManager.SetTabHeaderPosition(value);
    }

    partial void OnEmbedCloseActionChanged(string value)
    {
        _settingsManager.Settings.EmbedCloseAction = value;
        _settingsManager.SaveSettings();
    }

    partial void OnHideEmbeddedFromTaskbarChanged(bool value)
    {
        _settingsManager.SetHideEmbeddedFromTaskbar(value);
    }

    partial void OnAutoEmbedNewWindowsChanged(bool value)
    {
        _settingsManager.SetAutoEmbedNewWindows(value);
    }

    private void LoadExclusions()
    {
        AutoEmbedExclusions.Clear();
        foreach (var path in _settingsManager.Settings.AutoEmbedExcludedExecutables)
            AutoEmbedExclusions.Add(new AutoEmbedExclusionItem(path));
        OnPropertyChanged(nameof(HasNoExclusions));
    }

    [RelayCommand]
    private void RemoveExclusion(AutoEmbedExclusionItem item)
    {
        _settingsManager.RemoveAutoEmbedExclusion(item.Path);
        // LoadExclusions() is called via AutoEmbedExclusionsChanged event
    }

    [RelayCommand]
    private void BrowseAndAddExclusion()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "実行ファイル (*.exe)|*.exe",
            Title = "除外するアプリを選択"
        };
        if (dialog.ShowDialog() == true)
            _settingsManager.AddAutoEmbedExclusion(dialog.FileName);
        // LoadExclusions() is called via AutoEmbedExclusionsChanged event
    }

    partial void OnSelectedAccentColorChanged(string value)
    {
        if (UseSystemAccent) return;

        _settingsManager.Settings.AccentColor = value;
        _settingsManager.SaveSettings();
        ApplyAccentColor();
    }

    partial void OnUseSystemAccentChanged(bool value)
    {
        _settingsManager.Settings.UseSystemAccent = value;
        _settingsManager.SaveSettings();
        ApplyAccentColor();
    }

    public void SelectPresetColor(string colorCode)
    {
        UseSystemAccent = false;
        SelectedAccentColor = colorCode;
    }

    partial void OnSelectedBackgroundColorChanged(string value)
    {
        _settingsManager.Settings.BackgroundColor = value;
        _settingsManager.SaveSettings();
        ApplyBackgroundColor();
    }

    public void SelectBackgroundPresetColor(string colorCode)
    {
        SelectedBackgroundColor = colorCode;
    }

    private void ApplyAccentColor()
    {
        if (UseSystemAccent)
        {
            Wpf.Ui.Appearance.ApplicationAccentColorManager.ApplySystemAccent();
        }
        else
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(SelectedAccentColor);
                Wpf.Ui.Appearance.ApplicationAccentColorManager.Apply(color, Wpf.Ui.Appearance.ApplicationTheme.Dark);
            }
            catch
            {
                // Invalid color, ignore
            }
        }
    }

    private void ApplyBackgroundColor()
    {
        if (string.IsNullOrEmpty(SelectedBackgroundColor))
        {
            // Reset to theme default - re-apply theme to restore original colors
            return;
        }

        ApplyBackgroundColorStatic(SelectedBackgroundColor);
    }

    public static void ApplyBackgroundColorStatic(string colorCode)
    {
        if (string.IsNullOrEmpty(colorCode))
            return;

        try
        {
            var baseColor = (Color)ColorConverter.ConvertFromString(colorCode);
            var app = System.Windows.Application.Current;

            // Calculate luminance to determine if background is light or dark
            double luminance = (0.299 * baseColor.R + 0.587 * baseColor.G + 0.114 * baseColor.B) / 255.0;
            bool isLightBackground = luminance > 0.5;

            // Helper to create and freeze brush
            SolidColorBrush CreateBrush(Color c)
            {
                var b = new SolidColorBrush(c);
                b.Freeze();
                return b;
            }

            // Helper to lighten/darken color
            Color AdjustBrightness(Color c, int amount)
            {
                // For light backgrounds, invert the adjustment direction
                int adjustedAmount = isLightBackground ? -amount : amount;
                return Color.FromArgb(
                    c.A,
                    (byte)Math.Clamp(c.R + adjustedAmount, 0, 255),
                    (byte)Math.Clamp(c.G + adjustedAmount, 0, 255),
                    (byte)Math.Clamp(c.B + adjustedAmount, 0, 255));
            }

            Color WithAlpha(Color c, byte alpha)
            {
                return Color.FromArgb(alpha, c.R, c.G, c.B);
            }

            // Text colors based on background brightness
            Color textPrimary = isLightBackground ? Color.FromRgb(0x1A, 0x1A, 0x1A) : Color.FromRgb(0xFF, 0xFF, 0xFF);
            Color textSecondary = isLightBackground ? Color.FromArgb(0xB3, 0x1A, 0x1A, 0x1A) : Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF);
            Color textTertiary = isLightBackground ? Color.FromArgb(0x80, 0x1A, 0x1A, 0x1A) : Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF);
            Color textDisabled = isLightBackground ? Color.FromArgb(0x5C, 0x1A, 0x1A, 0x1A) : Color.FromArgb(0x5C, 0xFF, 0xFF, 0xFF);

            // Main background
            app.Resources["ApplicationBackgroundBrush"] = CreateBrush(baseColor);

            // Solid backgrounds
            app.Resources["SolidBackgroundFillColorBaseBrush"] = CreateBrush(baseColor);
            app.Resources["SolidBackgroundFillColorBaseAltBrush"] = CreateBrush(baseColor);
            app.Resources["SolidBackgroundFillColorSecondaryBrush"] = CreateBrush(AdjustBrightness(baseColor, 10));
            app.Resources["SolidBackgroundFillColorTertiaryBrush"] = CreateBrush(AdjustBrightness(baseColor, 20));
            app.Resources["SolidBackgroundFillColorQuarternaryBrush"] = CreateBrush(AdjustBrightness(baseColor, 30));

            // Layer backgrounds
            app.Resources["LayerFillColorDefaultBrush"] = CreateBrush(WithAlpha(AdjustBrightness(baseColor, 15), 128));
            app.Resources["LayerFillColorAltBrush"] = CreateBrush(WithAlpha(baseColor, 128));
            app.Resources["LayerOnMicaBaseAltFillColorDefaultBrush"] = CreateBrush(WithAlpha(AdjustBrightness(baseColor, 20), 200));

            // Card backgrounds
            app.Resources["CardBackgroundFillColorDefaultBrush"] = CreateBrush(WithAlpha(AdjustBrightness(baseColor, 20), 180));
            app.Resources["CardBackgroundFillColorSecondaryBrush"] = CreateBrush(WithAlpha(AdjustBrightness(baseColor, 15), 150));

            // Control backgrounds
            app.Resources["ControlFillColorDefaultBrush"] = CreateBrush(WithAlpha(AdjustBrightness(baseColor, 30), 180));
            app.Resources["ControlFillColorSecondaryBrush"] = CreateBrush(WithAlpha(AdjustBrightness(baseColor, 40), 140));
            app.Resources["ControlFillColorTertiaryBrush"] = CreateBrush(WithAlpha(AdjustBrightness(baseColor, 25), 100));
            app.Resources["ControlFillColorDisabledBrush"] = CreateBrush(WithAlpha(AdjustBrightness(baseColor, 20), 80));

            // Subtle fills
            app.Resources["SubtleFillColorTransparentBrush"] = CreateBrush(Colors.Transparent);
            app.Resources["SubtleFillColorSecondaryBrush"] = CreateBrush(WithAlpha(AdjustBrightness(baseColor, 40), 100));
            app.Resources["SubtleFillColorTertiaryBrush"] = CreateBrush(WithAlpha(AdjustBrightness(baseColor, 30), 80));

            // Smoke/Overlay
            app.Resources["SmokeFillColorDefaultBrush"] = CreateBrush(WithAlpha(baseColor, 100));

            // Text colors
            app.Resources["TextFillColorPrimaryBrush"] = CreateBrush(textPrimary);
            app.Resources["TextFillColorSecondaryBrush"] = CreateBrush(textSecondary);
            app.Resources["TextFillColorTertiaryBrush"] = CreateBrush(textTertiary);
            app.Resources["TextFillColorDisabledBrush"] = CreateBrush(textDisabled);

            // Control stroke colors for light backgrounds
            if (isLightBackground)
            {
                app.Resources["ControlStrongStrokeColorDefaultBrush"] = CreateBrush(Color.FromArgb(0x72, 0x00, 0x00, 0x00));
                app.Resources["ControlStrokeColorDefaultBrush"] = CreateBrush(Color.FromArgb(0x0F, 0x00, 0x00, 0x00));
                app.Resources["ControlStrokeColorSecondaryBrush"] = CreateBrush(Color.FromArgb(0x29, 0x00, 0x00, 0x00));
                app.Resources["ControlStrongStrokeColorDisabledBrush"] = CreateBrush(Color.FromArgb(0x37, 0x00, 0x00, 0x00));
                app.Resources["DividerStrokeColorDefaultBrush"] = CreateBrush(Color.FromArgb(0x14, 0x00, 0x00, 0x00));
                app.Resources["FocusStrokeColorOuterBrush"] = CreateBrush(Color.FromArgb(0xE4, 0x00, 0x00, 0x00));
                app.Resources["FocusStrokeColorInnerBrush"] = CreateBrush(Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF));
            }
            else
            {
                app.Resources["ControlStrongStrokeColorDefaultBrush"] = CreateBrush(Color.FromArgb(0x8B, 0xFF, 0xFF, 0xFF));
                app.Resources["ControlStrokeColorDefaultBrush"] = CreateBrush(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF));
                app.Resources["ControlStrokeColorSecondaryBrush"] = CreateBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF));
                app.Resources["ControlStrongStrokeColorDisabledBrush"] = CreateBrush(Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF));
                app.Resources["DividerStrokeColorDefaultBrush"] = CreateBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
                app.Resources["FocusStrokeColorOuterBrush"] = CreateBrush(Color.FromArgb(0xE4, 0xFF, 0xFF, 0xFF));
                app.Resources["FocusStrokeColorInnerBrush"] = CreateBrush(Color.FromArgb(0xB3, 0x00, 0x00, 0x00));
            }

            // TextBox background colors
            var textControlBg = WithAlpha(AdjustBrightness(baseColor, 20), 180);
            var textControlBgHover = WithAlpha(AdjustBrightness(baseColor, 25), 200);
            var textControlBgFocused = isLightBackground
                ? Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)  // White for light theme
                : Color.FromArgb(0xFF, 0x1E, 0x1E, 0x1E); // Dark for dark theme
            var textControlBgDisabled = WithAlpha(AdjustBrightness(baseColor, 10), 100);

            app.Resources["TextControlBackground"] = CreateBrush(textControlBg);
            app.Resources["TextControlBackgroundPointerOver"] = CreateBrush(textControlBgHover);
            app.Resources["TextControlBackgroundFocused"] = CreateBrush(textControlBgFocused);
            app.Resources["TextControlBackgroundDisabled"] = CreateBrush(textControlBgDisabled);

            // TextBox border colors
            var textControlBorder = isLightBackground
                ? Color.FromArgb(0x80, 0x00, 0x00, 0x00)
                : Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF);
            var textControlBorderHover = isLightBackground
                ? Color.FromArgb(0xA0, 0x00, 0x00, 0x00)
                : Color.FromArgb(0xA0, 0xFF, 0xFF, 0xFF);

            app.Resources["TextControlBorderBrush"] = CreateBrush(textControlBorder);
            app.Resources["TextControlBorderBrushPointerOver"] = CreateBrush(textControlBorderHover);
            app.Resources["TextControlBorderBrushFocused"] = CreateBrush(textControlBorder);
            app.Resources["TextControlBorderBrushDisabled"] = CreateBrush(WithAlpha(textControlBorder, 0x40));

            // TextBox placeholder text - use contrasting colors for focused state
            var placeholderFocused = isLightBackground
                ? Color.FromArgb(0x99, 0x00, 0x00, 0x00)  // Dark text on white bg
                : Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF); // Light text on dark bg

            app.Resources["TextControlPlaceholderForeground"] = CreateBrush(textTertiary);
            app.Resources["TextControlPlaceholderForegroundPointerOver"] = CreateBrush(textSecondary);
            app.Resources["TextControlPlaceholderForegroundFocused"] = CreateBrush(placeholderFocused);
            app.Resources["TextControlPlaceholderForegroundDisabled"] = CreateBrush(textDisabled);

            // TextBox foreground - use contrasting colors for focused state
            var textFocused = isLightBackground
                ? Color.FromRgb(0x00, 0x00, 0x00)  // Black text on white bg
                : Color.FromRgb(0xFF, 0xFF, 0xFF); // White text on dark bg

            app.Resources["TextControlForeground"] = CreateBrush(textPrimary);
            app.Resources["TextControlForegroundPointerOver"] = CreateBrush(textPrimary);
            app.Resources["TextControlForegroundFocused"] = CreateBrush(textFocused);
            app.Resources["TextControlForegroundDisabled"] = CreateBrush(textDisabled);

            // TextBox selection colors
            app.Resources["TextControlSelectionHighlightColor"] = CreateBrush(Color.FromArgb(0x99, 0x00, 0x78, 0xD4));

            // ComboBox
            app.Resources["ComboBoxForeground"] = CreateBrush(textPrimary);
            app.Resources["ComboBoxForegroundDisabled"] = CreateBrush(textDisabled);
            app.Resources["ComboBoxPlaceHolderForeground"] = CreateBrush(textSecondary);
            app.Resources["ComboBoxDropDownForeground"] = CreateBrush(textPrimary);
            app.Resources["ComboBoxBackground"] = CreateBrush(textControlBg);
            app.Resources["ComboBoxBackgroundPointerOver"] = CreateBrush(textControlBgHover);
            app.Resources["ComboBoxBackgroundPressed"] = CreateBrush(textControlBgHover);
            app.Resources["ComboBoxBackgroundDisabled"] = CreateBrush(textControlBgDisabled);
            app.Resources["ComboBoxDropDownBackground"] = CreateBrush(AdjustBrightness(baseColor, 15));
            app.Resources["ComboBoxDropDownBackgroundPointerOver"] = CreateBrush(AdjustBrightness(baseColor, 25));
            app.Resources["ComboBoxItemForeground"] = CreateBrush(textPrimary);
            app.Resources["ComboBoxItemForegroundSelected"] = CreateBrush(textPrimary);
            app.Resources["ComboBoxItemForegroundPointerOver"] = CreateBrush(textPrimary);
            app.Resources["ComboBoxBorderBrush"] = CreateBrush(textControlBorder);
            app.Resources["ComboBoxBorderBrushPointerOver"] = CreateBrush(textControlBorderHover);
            app.Resources["ComboBoxBorderBrushPressed"] = CreateBrush(textControlBorderHover);
            app.Resources["ComboBoxBorderBrushDisabled"] = CreateBrush(WithAlpha(textControlBorder, 0x40));

            // ListBox/ListView
            app.Resources["ListViewItemForeground"] = CreateBrush(textPrimary);
            app.Resources["ListViewItemForegroundPointerOver"] = CreateBrush(textPrimary);
            app.Resources["ListViewItemForegroundSelected"] = CreateBrush(textPrimary);
            app.Resources["ListViewItemBackground"] = CreateBrush(Colors.Transparent);
            app.Resources["ListViewItemBackgroundPointerOver"] = CreateBrush(WithAlpha(AdjustBrightness(baseColor, 30), 100));
            app.Resources["ListViewItemBackgroundSelected"] = CreateBrush(WithAlpha(AdjustBrightness(baseColor, 40), 120));
            app.Resources["ListViewItemBackgroundSelectedPointerOver"] = CreateBrush(WithAlpha(AdjustBrightness(baseColor, 45), 140));
            app.Resources["ListBoxItemBackground"] = CreateBrush(Colors.Transparent);
            app.Resources["ListBoxItemBackgroundPointerOver"] = CreateBrush(WithAlpha(AdjustBrightness(baseColor, 30), 100));
            app.Resources["ListBoxItemBackgroundSelected"] = CreateBrush(WithAlpha(AdjustBrightness(baseColor, 40), 120));
            app.Resources["ListBoxItemBackgroundSelectedPointerOver"] = CreateBrush(WithAlpha(AdjustBrightness(baseColor, 45), 140));

            // CheckBox
            app.Resources["CheckBoxForegroundUnchecked"] = CreateBrush(textPrimary);
            app.Resources["CheckBoxForegroundUncheckedPointerOver"] = CreateBrush(textPrimary);
            app.Resources["CheckBoxForegroundUncheckedPressed"] = CreateBrush(textPrimary);
            app.Resources["CheckBoxForegroundChecked"] = CreateBrush(textPrimary);
            app.Resources["CheckBoxForegroundCheckedPointerOver"] = CreateBrush(textPrimary);
            app.Resources["CheckBoxForegroundCheckedPressed"] = CreateBrush(textPrimary);
            app.Resources["CheckBoxCheckBackgroundFillUnchecked"] = CreateBrush(textControlBg);
            app.Resources["CheckBoxCheckBackgroundFillUncheckedPointerOver"] = CreateBrush(textControlBgHover);
            app.Resources["CheckBoxCheckBackgroundFillUncheckedPressed"] = CreateBrush(WithAlpha(AdjustBrightness(baseColor, 15), 160));
            app.Resources["CheckBoxCheckBackgroundFillUncheckedDisabled"] = CreateBrush(textControlBgDisabled);
            app.Resources["CheckBoxCheckBackgroundStrokeUnchecked"] = CreateBrush(textControlBorder);
            app.Resources["CheckBoxCheckBackgroundStrokeUncheckedPointerOver"] = CreateBrush(textControlBorderHover);
            app.Resources["CheckBoxCheckBackgroundStrokeUncheckedPressed"] = CreateBrush(textControlBorder);
            app.Resources["CheckBoxCheckBackgroundStrokeUncheckedDisabled"] = CreateBrush(WithAlpha(textControlBorder, 0x40));

            // Button
            app.Resources["ButtonForeground"] = CreateBrush(textPrimary);
            app.Resources["ButtonForegroundPointerOver"] = CreateBrush(textPrimary);
            app.Resources["ButtonForegroundPressed"] = CreateBrush(textSecondary);
            app.Resources["ButtonForegroundDisabled"] = CreateBrush(textDisabled);
            app.Resources["ButtonBackground"] = CreateBrush(textControlBg);
            app.Resources["ButtonBackgroundPointerOver"] = CreateBrush(textControlBgHover);
            app.Resources["ButtonBackgroundPressed"] = CreateBrush(WithAlpha(AdjustBrightness(baseColor, 15), 160));
            app.Resources["ButtonBackgroundDisabled"] = CreateBrush(textControlBgDisabled);
            app.Resources["ButtonBorderBrush"] = CreateBrush(textControlBorder);
            app.Resources["ButtonBorderBrushPointerOver"] = CreateBrush(textControlBorderHover);
            app.Resources["ButtonBorderBrushPressed"] = CreateBrush(textControlBorder);
            app.Resources["ButtonBorderBrushDisabled"] = CreateBrush(WithAlpha(textControlBorder, 0x40));

            // ToggleSwitch
            app.Resources["ToggleSwitchContentForeground"] = CreateBrush(textPrimary);
            app.Resources["ToggleSwitchHeaderForeground"] = CreateBrush(textPrimary);
            app.Resources["ToggleSwitchFillOff"] = CreateBrush(textControlBg);
            app.Resources["ToggleSwitchFillOffPointerOver"] = CreateBrush(textControlBgHover);
            app.Resources["ToggleSwitchFillOffPressed"] = CreateBrush(WithAlpha(AdjustBrightness(baseColor, 15), 160));
            app.Resources["ToggleSwitchFillOffDisabled"] = CreateBrush(textControlBgDisabled);
            app.Resources["ToggleSwitchStrokeOff"] = CreateBrush(textControlBorder);
            app.Resources["ToggleSwitchStrokeOffPointerOver"] = CreateBrush(textControlBorderHover);
            app.Resources["ToggleSwitchStrokeOffPressed"] = CreateBrush(textControlBorder);
            app.Resources["ToggleSwitchStrokeOffDisabled"] = CreateBrush(WithAlpha(textControlBorder, 0x40));
            var knobColor = isLightBackground ? Color.FromRgb(0x5C, 0x5C, 0x5C) : Color.FromRgb(0xD0, 0xD0, 0xD0);
            app.Resources["ToggleSwitchKnobFillOff"] = CreateBrush(knobColor);
            app.Resources["ToggleSwitchKnobFillOffPointerOver"] = CreateBrush(knobColor);
            app.Resources["ToggleSwitchKnobFillOffPressed"] = CreateBrush(knobColor);
            app.Resources["ToggleSwitchKnobFillOffDisabled"] = CreateBrush(WithAlpha(knobColor, 0x80));

            // Slider
            app.Resources["SliderHeaderForeground"] = CreateBrush(textPrimary);

            // ToolTip
            app.Resources["ToolTipForeground"] = CreateBrush(textPrimary);
            app.Resources["ToolTipBackground"] = CreateBrush(AdjustBrightness(baseColor, 15));
            app.Resources["ToolTipBorderBrush"] = CreateBrush(textControlBorder);

            // Menu/ContextMenu/MenuFlyout
            var menuBg = AdjustBrightness(baseColor, isLightBackground ? -5 : 15);
            app.Resources["MenuFlyoutPresenterBackground"] = CreateBrush(menuBg);
            app.Resources["MenuFlyoutItemForeground"] = CreateBrush(textPrimary);
            app.Resources["MenuFlyoutItemForegroundPointerOver"] = CreateBrush(textPrimary);
            app.Resources["MenuFlyoutItemForegroundPressed"] = CreateBrush(textPrimary);
            app.Resources["MenuFlyoutItemForegroundDisabled"] = CreateBrush(textDisabled);
            app.Resources["MenuFlyoutItemBackground"] = CreateBrush(Colors.Transparent);
            app.Resources["MenuFlyoutItemBackgroundPointerOver"] = CreateBrush(WithAlpha(AdjustBrightness(baseColor, 30), 100));
            app.Resources["MenuFlyoutItemBackgroundPressed"] = CreateBrush(WithAlpha(AdjustBrightness(baseColor, 25), 120));

            // WPF ContextMenu specific
            app.Resources["MenuBackground"] = CreateBrush(menuBg);
            app.Resources["MenuBorderBrush"] = CreateBrush(textControlBorder);
            app.Resources["MenuItemBackground"] = CreateBrush(Colors.Transparent);
            app.Resources["MenuItemBackgroundPointerOver"] = CreateBrush(WithAlpha(AdjustBrightness(baseColor, 30), 100));
            app.Resources["MenuItemBackgroundPressed"] = CreateBrush(WithAlpha(AdjustBrightness(baseColor, 25), 120));
            app.Resources["MenuItemForeground"] = CreateBrush(textPrimary);
            app.Resources["MenuItemForegroundPointerOver"] = CreateBrush(textPrimary);
            app.Resources["MenuItemForegroundPressed"] = CreateBrush(textPrimary);
            app.Resources["MenuItemForegroundDisabled"] = CreateBrush(textDisabled);

            // MenuItem icon foreground (for ui:SymbolIcon)
            app.Resources["MenuItemIconForeground"] = CreateBrush(textPrimary);

            // WPF Menu static resources (used by default templates)
            app.Resources["Menu.Static.Background"] = CreateBrush(menuBg);
            app.Resources["Menu.Static.Border"] = CreateBrush(textControlBorder);
            app.Resources["Menu.Static.Foreground"] = CreateBrush(textPrimary);
            app.Resources["Menu.Static.Separator"] = CreateBrush(WithAlpha(textPrimary, 0x30));
            app.Resources["Menu.Disabled.Foreground"] = CreateBrush(textDisabled);
            app.Resources["MenuItem.Selected.Background"] = CreateBrush(WithAlpha(AdjustBrightness(baseColor, 30), 100));
            app.Resources["MenuItem.Selected.Border"] = CreateBrush(Colors.Transparent);
            app.Resources["MenuItem.Highlight.Background"] = CreateBrush(WithAlpha(AdjustBrightness(baseColor, 30), 100));
            app.Resources["MenuItem.Highlight.Border"] = CreateBrush(Colors.Transparent);
            app.Resources["MenuItem.Highlight.Disabled.Background"] = CreateBrush(Colors.Transparent);
            app.Resources["MenuItem.Highlight.Disabled.Border"] = CreateBrush(Colors.Transparent);
            app.Resources["MenuItem.SubMenu.Background"] = CreateBrush(menuBg);
            app.Resources["MenuItem.SubMenu.Border"] = CreateBrush(textControlBorder);

            // ScrollBar
            var scrollBarThumb = isLightBackground ? Color.FromArgb(0x80, 0x60, 0x60, 0x60) : Color.FromArgb(0x80, 0xA0, 0xA0, 0xA0);
            var scrollBarThumbHover = isLightBackground ? Color.FromArgb(0xA0, 0x50, 0x50, 0x50) : Color.FromArgb(0xA0, 0xB0, 0xB0, 0xB0);
            app.Resources["ScrollBarThumbFill"] = CreateBrush(scrollBarThumb);
            app.Resources["ScrollBarThumbFillPointerOver"] = CreateBrush(scrollBarThumbHover);
            app.Resources["ScrollBarThumbFillPressed"] = CreateBrush(scrollBarThumbHover);
            app.Resources["ScrollBarTrackFill"] = CreateBrush(Colors.Transparent);
            app.Resources["ScrollBarTrackFillPointerOver"] = CreateBrush(WithAlpha(AdjustBrightness(baseColor, 10), 50));

            // System fill colors
            app.Resources["SystemFillColorCriticalBrush"] = CreateBrush(Color.FromRgb(0xFF, 0x99, 0x99));
            app.Resources["SystemFillColorSuccessBrush"] = CreateBrush(Color.FromRgb(0x6C, 0xCB, 0x5F));
            app.Resources["SystemFillColorCautionBrush"] = CreateBrush(Color.FromRgb(0xFC, 0xE1, 0x00));
            app.Resources["SystemFillColorNeutralBrush"] = CreateBrush(textSecondary);

            // ContextMenu
            app.Resources["ContextMenuBackground"] = CreateBrush(AdjustBrightness(baseColor, 10));
            app.Resources["ContextMenuBorderBrush"] = CreateBrush(textControlBorder);
            app.Resources["MenuItemForeground"] = CreateBrush(textPrimary);
            app.Resources["MenuItemForegroundPointerOver"] = CreateBrush(textPrimary);
            app.Resources["MenuItemForegroundPressed"] = CreateBrush(textPrimary);
            app.Resources["MenuItemForegroundDisabled"] = CreateBrush(textDisabled);
            app.Resources["MenuItemBackgroundPointerOver"] = CreateBrush(WithAlpha(AdjustBrightness(baseColor, 30), 100));
            app.Resources["MenuItemBackgroundPressed"] = CreateBrush(WithAlpha(AdjustBrightness(baseColor, 25), 120));
        }
        catch
        {
            // Invalid color, ignore
        }
    }
}

public class AutoEmbedExclusionItem
{
    public string Path { get; }
    public string Name { get; }
    public ImageSource? Icon { get; }

    public AutoEmbedExclusionItem(string path)
    {
        Path = path;
        Name = System.IO.Path.GetFileNameWithoutExtension(path);
        Icon = PathToIconConverter.GetIconForPath(path);
    }
}

public class PresetColor
{
    public string Name { get; }
    public string ColorCode { get; }
    public Color Color { get; }

    public PresetColor(string name, string colorCode)
    {
        Name = name;
        ColorCode = colorCode;
        if (string.IsNullOrEmpty(colorCode))
        {
            Color = Colors.Transparent;
        }
        else
        {
            Color = (Color)ColorConverter.ConvertFromString(colorCode);
        }
    }
}
