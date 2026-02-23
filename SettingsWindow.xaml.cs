using System;
using System.Linq;
using System.Windows;
using WindowzTabManager.Services;
using WindowzTabManager.ViewModels;
using WindowzTabManager.Views.Settings;

namespace WindowzTabManager;

public partial class SettingsWindow : Window
{
    private readonly SettingsManager _settingsManager;
    private readonly HotkeyManager _hotkeyManager;
    private readonly GeneralSettingsViewModel _generalSettingsViewModel;
    private readonly HotkeySettingsViewModel _hotkeySettingsViewModel;
    private readonly StartupSettingsViewModel _startupSettingsViewModel;
    private readonly QuickLaunchSettingsViewModel _quickLaunchSettingsViewModel;
    private readonly ProcessInfoViewModel _processInfoViewModel;
    private bool _hotkeyManagerInitialized;

    public SettingsWindow(SettingsManager settingsManager, Func<IEnumerable<ManagedWindow>>? getManagedWindows = null)
    {
        _settingsManager = settingsManager;
        _hotkeyManager = new HotkeyManager();
        _generalSettingsViewModel = new GeneralSettingsViewModel(settingsManager);
        _hotkeySettingsViewModel = new HotkeySettingsViewModel(_hotkeyManager);
        _startupSettingsViewModel = new StartupSettingsViewModel(settingsManager);
        _quickLaunchSettingsViewModel = new QuickLaunchSettingsViewModel(settingsManager);
        _processInfoViewModel = new ProcessInfoViewModel(getManagedWindows ?? (() => Enumerable.Empty<ManagedWindow>()));

        InitializeComponent();

        Loaded += SettingsWindow_Loaded;
        Closed += SettingsWindow_Closed;

        GeneralTab.Content = new GeneralSettingsPage(_generalSettingsViewModel);
        HotkeyTab.Content = new HotkeySettingsPage(_hotkeySettingsViewModel);
        StartupTab.Content = new StartupSettingsPage(_startupSettingsViewModel);
        QuickLaunchTab.Content = new QuickLaunchSettingsPage(_quickLaunchSettingsViewModel);
        var processInfoPage = new ProcessInfoPage(_processInfoViewModel);
        ProcessInfoTab.Content = processInfoPage;
        _processInfoViewModel.RefreshCommand.Execute(null);
    }

    private void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_hotkeyManagerInitialized)
            return;

        _hotkeyManager.Initialize(Owner ?? this, _settingsManager);
        _hotkeyManagerInitialized = true;
    }

    private void SettingsWindow_Closed(object? sender, EventArgs e)
    {
        _hotkeyManager.Dispose();
    }
}
