using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using Wind.Interop;
using Wind.Models;
using Wind.Services;

namespace Wind.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly WindowManager _windowManager;
    private readonly TabManager _tabManager;
    private readonly HotkeyManager _hotkeyManager;
    private readonly WindowPickerViewModel _windowPickerViewModel;
    private readonly SettingsManager _settingsManager;

    public ObservableCollection<TabItem> Tabs => _tabManager.Tabs;
    public ObservableCollection<TabGroup> Groups => _tabManager.Groups;
    public ObservableCollection<WindowInfo> AvailableWindows => _windowManager.AvailableWindows;

    [ObservableProperty]
    private TabItem? _selectedTab;

    [ObservableProperty]
    private WindowHost? _currentWindowHost;

    [ObservableProperty]
    private bool _isWindowPickerOpen;

    [ObservableProperty]
    private bool _isCommandPaletteOpen;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private TileLayout? _currentTileLayout;

    /// <summary>
    /// True when a tile layout exists (may or may not be visible).
    /// </summary>
    [ObservableProperty]
    private bool _isTiled;

    /// <summary>
    /// True when the tile view is currently shown (active tab is a tiled tab).
    /// </summary>
    [ObservableProperty]
    private bool _isTileVisible;

    /// <summary>
    /// True when the active tab is a content tab (e.g. Settings).
    /// </summary>
    [ObservableProperty]
    private bool _isContentTabActive;

    /// <summary>
    /// The content key of the active content tab, or null.
    /// </summary>
    [ObservableProperty]
    private string? _activeContentKey;

    /// <summary>
    /// True when the active tab is a web tab.
    /// </summary>
    [ObservableProperty]
    private bool _isWebTabActive;

    /// <summary>
    /// The Id of the active web tab, or null.
    /// </summary>
    [ObservableProperty]
    private Guid? _activeWebTabId;

    public MainViewModel(
        WindowManager windowManager,
        TabManager tabManager,
        HotkeyManager hotkeyManager,
        WindowPickerViewModel windowPickerViewModel,
        SettingsManager settingsManager)
    {
        _windowManager = windowManager;
        _tabManager = tabManager;
        _hotkeyManager = hotkeyManager;
        _windowPickerViewModel = windowPickerViewModel;
        _settingsManager = settingsManager;

        _tabManager.ActiveTabChanged += OnActiveTabChanged;
        _tabManager.TileLayoutChanged += OnTileLayoutChanged;
        _hotkeyManager.HotkeyPressed += OnHotkeyPressed;
    }

    private void OnActiveTabChanged(object? sender, TabItem? tab)
    {
        SelectedTab = tab;

        if (tab != null && tab.IsContentTab)
        {
            // Content tab (e.g. Settings)
            IsTileVisible = false;
            CurrentWindowHost = null;
            IsContentTabActive = true;
            ActiveContentKey = tab.ContentKey;
            IsWebTabActive = false;
            ActiveWebTabId = null;
        }
        else if (tab != null && tab.IsWebTab)
        {
            // Web tab
            IsTileVisible = false;
            CurrentWindowHost = null;
            IsContentTabActive = false;
            ActiveContentKey = null;
            IsWebTabActive = true;
            ActiveWebTabId = tab.Id;
        }
        else if (IsTiled && tab != null && tab.IsTiled)
        {
            // Clicked a tiled tab — show tile view
            IsTileVisible = true;
            CurrentWindowHost = null;
            IsContentTabActive = false;
            ActiveContentKey = null;
            IsWebTabActive = false;
            ActiveWebTabId = null;
        }
        else
        {
            // Clicked a non-tiled tab or no tile layout — show single view
            IsTileVisible = false;
            CurrentWindowHost = tab != null ? _tabManager.GetWindowHost(tab) : null;
            IsContentTabActive = false;
            ActiveContentKey = null;
            IsWebTabActive = false;
            ActiveWebTabId = null;
        }
    }

    private void OnTileLayoutChanged(object? sender, TileLayout? layout)
    {
        CurrentTileLayout = layout;
        IsTiled = layout?.IsActive == true;
        IsTileVisible = layout?.IsActive == true;
    }

    private void OnHotkeyPressed(object? sender, HotkeyBinding binding)
    {
        switch (binding.Action)
        {
            case HotkeyAction.NextTab:
                _tabManager.SelectNextTab();
                break;
            case HotkeyAction.PreviousTab:
                _tabManager.SelectPreviousTab();
                break;
            case HotkeyAction.CloseTab:
                if (SelectedTab != null)
                    CloseTab(SelectedTab);
                break;
            case HotkeyAction.NewTab:
                OpenWindowPicker();
                break;
            case HotkeyAction.SwitchToTab1:
            case HotkeyAction.SwitchToTab2:
            case HotkeyAction.SwitchToTab3:
            case HotkeyAction.SwitchToTab4:
            case HotkeyAction.SwitchToTab5:
            case HotkeyAction.SwitchToTab6:
            case HotkeyAction.SwitchToTab7:
            case HotkeyAction.SwitchToTab8:
            case HotkeyAction.SwitchToTab9:
                int index = binding.Action - HotkeyAction.SwitchToTab1;
                _tabManager.SelectTab(index);
                break;
            case HotkeyAction.CommandPalette:
                if (IsCommandPaletteOpen)
                    CloseCommandPalette();
                else
                    OpenCommandPalette();
                break;
        }
    }

    // Non-command methods for multi-parameter operations
    public WindowHost? GetWindowHost(TabItem tab)
    {
        return _tabManager.GetWindowHost(tab);
    }

    public bool IsExternallyManagedTab(TabItem tab)
    {
        return _tabManager.IsExternallyManagedTab(tab);
    }

    public bool TryGetExternallyManagedWindowHandle(TabItem tab, out IntPtr handle)
    {
        return _tabManager.TryGetExternallyManagedWindowHandle(tab, out handle);
    }

    public void AddTabToGroup(TabItem tab, TabGroup group)
    {
        _tabManager.AddTabToGroup(tab, group);
    }

    public void RemoveTabFromGroup(TabItem tab)
    {
        _tabManager.RemoveTabFromGroup(tab);
    }

    public void Cleanup()
    {
        // Stop the window picker timer first to prevent UI updates during cleanup
        _windowPickerViewModel.Stop();
        _tabManager.StopCleanupTimer();

        _tabManager.ActiveTabChanged -= OnActiveTabChanged;
        _tabManager.TileLayoutChanged -= OnTileLayoutChanged;
        _hotkeyManager.HotkeyPressed -= OnHotkeyPressed;

        switch (_settingsManager.Settings.CloseWindowsOnExit)
        {
            case "All":
                _tabManager.CloseAllTabs();
                break;
            case "StartupOnly":
                _tabManager.CloseStartupTabs();
                break;
            default:
                _tabManager.ReleaseAllTabs();
                break;
        }

        _hotkeyManager.Dispose();
    }
}
