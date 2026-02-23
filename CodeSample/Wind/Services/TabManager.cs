using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Wind.Interop;
using Wind.Models;
using Wind.Views;

namespace Wind.Services;

public partial class TabManager
{
    private readonly WindowManager _windowManager;
    private readonly SettingsManager _settingsManager;
    private readonly ProcessTracker _processTracker;
    private readonly Dictionary<Guid, WindowHost> _windowHosts = new();
    private readonly Dictionary<Guid, IntPtr> _externallyManagedWindows = new();
    private readonly Dictionary<Guid, WebTabControl> _webTabControls = new();
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _cleanupTimer;

    public ObservableCollection<TabItem> Tabs { get; } = new();
    public ObservableCollection<TabGroup> Groups { get; } = new();

    private TabItem? _activeTab;
    public TabItem? ActiveTab
    {
        get => _activeTab;
        set
        {
            if (_activeTab != value)
            {
                if (_activeTab != null)
                    _activeTab.IsSelected = false;

                _activeTab = value;

                if (_activeTab != null)
                    _activeTab.IsSelected = true;

                ActiveTabChanged?.Invoke(this, _activeTab);
            }
        }
    }

    private TileLayout? _currentTileLayout;
    public TileLayout? CurrentTileLayout
    {
        get => _currentTileLayout;
        private set
        {
            _currentTileLayout = value;
            TileLayoutChanged?.Invoke(this, value);
        }
    }

    public event EventHandler<TabItem?>? ActiveTabChanged;
    public event EventHandler<TabItem>? TabAdded;
    public event EventHandler<TabItem>? TabRemoved;
    public event EventHandler<TileLayout?>? TileLayoutChanged;
    public event EventHandler? TileLayoutUpdated;
    public event EventHandler? MinimizeRequested;
    public event EventHandler? MaximizeRequested;
    public event Action<int, int>? MoveRequested;
    public event EventHandler? CloseWindRequested;
    public event EventHandler? BringToFrontRequested;

    public TabManager(WindowManager windowManager, SettingsManager settingsManager, ProcessTracker processTracker)
    {
        _windowManager = windowManager;
        _settingsManager = settingsManager;
        _processTracker = processTracker;
        _dispatcher = Dispatcher.CurrentDispatcher;

        // フォールバック: 定期的に無効なタブを検出・除去する
        _cleanupTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _cleanupTimer.Tick += (_, _) => CleanupInvalidTabs();
        _cleanupTimer.Start();

        _settingsManager.AutoEmbedNewWindowsChanged += _ => UpdateGlobalWindowHook();
        UpdateGlobalWindowHook();
    }

    public TabItem? AddTab(WindowInfo windowInfo, bool activate = true)
    {
        if (windowInfo.Handle == IntPtr.Zero) return null;

        // Check if already added
        var existingTab = Tabs.FirstOrDefault(t => t.Window?.Handle == windowInfo.Handle);
        if (existingTab != null)
        {
            if (activate)
                ActiveTab = existingTab;
            return existingTab;
        }

        // Safety net: don't attempt to embed elevated process windows from a non-admin Wind
        if (!App.IsRunningAsAdmin() && NativeMethods.IsProcessElevated(windowInfo.Handle))
            return null;

        var tab = new TabItem(windowInfo);
        bool useExternalManagement = WindowClassFilters.TryGetUnsupportedReasonForEmbedding(windowInfo.Handle, out _);

        if (useExternalManagement)
        {
            if (!_windowManager.TryManageWindow(windowInfo.Handle))
                return null;

            _externallyManagedWindows[tab.Id] = windowInfo.Handle;
        }
        else
        {
            var host = _windowManager.EmbedWindow(windowInfo.Handle);
            if (host == null) return null;

            _processTracker.Add(host.HostedProcessId);
            _windowHosts[tab.Id] = host;

            // Subscribe to hosted window events
            host.HostedWindowClosed += (s, e) => OnHostedWindowClosed(tab);
            host.MinimizeRequested += (s, e) => MinimizeRequested?.Invoke(this, EventArgs.Empty);
            host.MaximizeRequested += (s, e) => MaximizeRequested?.Invoke(this, EventArgs.Empty);
            host.MoveRequested += (dx, dy) => MoveRequested?.Invoke(dx, dy);
            host.NewWindowDetected += hwnd => OnNewWindowDetected(hwnd);
            host.BringToFrontRequested += (s, e) => BringToFrontRequested?.Invoke(this, EventArgs.Empty);
        }

        Tabs.Add(tab);

        TabAdded?.Invoke(this, tab);

        if (activate)
            ActiveTab = tab;

        return tab;
    }

    public TabItem AddContentTab(string title, string contentKey, bool activate = true)
    {
        // Check if already added
        var existingTab = Tabs.FirstOrDefault(t => t.ContentKey == contentKey);
        if (existingTab != null)
        {
            if (activate)
                ActiveTab = existingTab;
            return existingTab;
        }

        var tab = new TabItem { ContentKey = contentKey };
        tab.Title = title;

        // Set Wind icon for content tabs
        try
        {
            var iconUri = new Uri("pack://application:,,,/Assets/Wind.ico");
            tab.Icon = new BitmapImage(iconUri);
        }
        catch
        {
            // Fallback if icon loading fails
            tab.Icon = null;
        }

        Tabs.Add(tab);
        TabAdded?.Invoke(this, tab);

        if (activate)
            ActiveTab = tab;

        return tab;
    }

    private static ImageSource? _globeIcon;
    private static ImageSource GlobeIcon => _globeIcon ??= CreateGlobeIcon();

    private static ImageSource CreateGlobeIcon()
    {
        var group = new DrawingGroup();
        var pen = new Pen(new SolidColorBrush(System.Windows.Media.Color.FromRgb(140, 140, 140)), 1.4);
        pen.Freeze();

        // Outer circle
        group.Children.Add(new GeometryDrawing(null, pen,
            new EllipseGeometry(new System.Windows.Point(16, 16), 13, 13)));
        // Vertical meridian
        group.Children.Add(new GeometryDrawing(null, pen,
            new EllipseGeometry(new System.Windows.Point(16, 16), 5.5, 13)));
        // Equator
        group.Children.Add(new GeometryDrawing(null, pen,
            new LineGeometry(new System.Windows.Point(3, 16), new System.Windows.Point(29, 16))));

        group.Freeze();
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }

    public TabItem AddWebTab(string url, bool activate = true)
    {
        var tab = new TabItem { WebUrl = url };
        tab.Title = "New Tab";
        tab.Icon = GlobeIcon;

        Tabs.Add(tab);
        TabAdded?.Invoke(this, tab);

        if (activate)
            ActiveTab = tab;

        return tab;
    }

    public void RegisterWebTabControl(Guid tabId, WebTabControl control)
    {
        _webTabControls[tabId] = control;
    }

    public WebTabControl? GetWebTabControl(Guid tabId)
    {
        return _webTabControls.TryGetValue(tabId, out var control) ? control : null;
    }

    public void RemoveWebTabControl(Guid tabId)
    {
        if (_webTabControls.TryGetValue(tabId, out var control))
        {
            control.Dispose();
            _webTabControls.Remove(tabId);
        }
    }

    private void OnNewWindowDetected(IntPtr hwnd)
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(() => OnNewWindowDetected(hwnd));
            return;
        }

        // Skip if already embedded
        if (_windowManager.IsEmbedded(hwnd)) return;

        // Skip if already tracked as a tab
        if (Tabs.Any(t => t.Window?.Handle == hwnd)) return;

        var windowInfo = WindowInfo.FromHandle(hwnd);
        if (windowInfo == null) return;

        if (_settingsManager.IsAutoEmbedExcluded(windowInfo.ExecutablePath)) return;

        AddTab(windowInfo, activate: true);
    }

    private void OnHostedWindowClosed(TabItem tab)
    {
        // Ensure we're on the UI thread
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(() => OnHostedWindowClosed(tab));
            return;
        }

        // Update tile layout if this tab was tiled
        UpdateTileForRemovedTab(tab);

        // Remove the tab without trying to release the window (it's already closed)
        if (_windowHosts.TryGetValue(tab.Id, out var host))
        {
            _processTracker.Remove(host.HostedProcessId);
            _windowHosts.Remove(tab.Id);
        }
        else if (_externallyManagedWindows.TryGetValue(tab.Id, out var managedHandle))
        {
            _windowManager.ForgetManagedWindow(managedHandle);
            _externallyManagedWindows.Remove(tab.Id);
        }

        tab.Group?.RemoveTab(tab);

        var index = Tabs.IndexOf(tab);
        if (index < 0) return;

        Tabs.Remove(tab);
        TabRemoved?.Invoke(this, tab);

        // Select adjacent tab
        if (ActiveTab == tab)
        {
            if (Tabs.Count > 0)
            {
                var newIndex = Math.Min(index, Tabs.Count - 1);
                ActiveTab = Tabs[newIndex];
            }
            else
            {
                ActiveTab = null;
            }
        }
    }

    public void RemoveTab(TabItem tab)
    {
        // Update tile layout if this tab was tiled
        UpdateTileForRemovedTab(tab);

        if (tab.IsWebTab)
        {
            RemoveWebTabControl(tab.Id);
        }
        else if (!tab.IsContentTab && _windowHosts.TryGetValue(tab.Id, out var host))
        {
            _processTracker.Remove(host.HostedProcessId);
            SuppressAutoEmbedForWindow(host.HostedWindowHandle);
            _windowManager.ReleaseWindow(host);
            _windowHosts.Remove(tab.Id);
        }
        else if (!tab.IsContentTab && _externallyManagedWindows.TryGetValue(tab.Id, out var managedHandle))
        {
            SuppressAutoEmbedForWindow(managedHandle);
            _windowManager.ReleaseManagedWindow(managedHandle);
            _externallyManagedWindows.Remove(tab.Id);
        }

        // Remove from group if in one
        tab.Group?.RemoveTab(tab);

        var index = Tabs.IndexOf(tab);
        Tabs.Remove(tab);

        TabRemoved?.Invoke(this, tab);

        // Select adjacent tab
        if (ActiveTab == tab)
        {
            if (Tabs.Count > 0)
            {
                var newIndex = Math.Min(index, Tabs.Count - 1);
                ActiveTab = Tabs[newIndex];
            }
            else
            {
                ActiveTab = null;
            }
        }
    }

    public void CloseTab(TabItem tab)
    {
        // Content tabs and web tabs are not embedded apps, so always just remove them
        if (tab.IsContentTab || tab.IsWebTab)
        {
            RemoveTab(tab);
            return;
        }

        var closeAction = _settingsManager.Settings.EmbedCloseAction;
        System.Diagnostics.Debug.WriteLine($"CloseTab called with action: {closeAction}");

        switch (closeAction)
        {
            case "CloseApp":
                // Default behavior: close the embedded application (non-blocking)
                if (tab.Window?.Handle != IntPtr.Zero)
                {
                    NativeMethods.PostMessage(tab.Window!.Handle, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                }

                if (_externallyManagedWindows.TryGetValue(tab.Id, out var managedHandle))
                {
                    _windowManager.ForgetManagedWindow(managedHandle);
                    _externallyManagedWindows.Remove(tab.Id);
                }

                RemoveTab(tab);
                break;

            case "ReleaseEmbed":
                // Update tile layout if this tab was tiled
                UpdateTileForRemovedTab(tab);

                // Release embedding and restore to desktop
                if (!tab.IsContentTab && _windowHosts.TryGetValue(tab.Id, out var host))
                {
                    _processTracker.Remove(host.HostedProcessId);
                    SuppressAutoEmbedForWindow(host.HostedWindowHandle);
                    _windowManager.ReleaseWindow(host);
                    _windowHosts.Remove(tab.Id);
                }
                else if (!tab.IsContentTab && _externallyManagedWindows.TryGetValue(tab.Id, out var managedHandleToRelease))
                {
                    SuppressAutoEmbedForWindow(managedHandleToRelease);
                    _windowManager.ReleaseManagedWindow(managedHandleToRelease);
                    _externallyManagedWindows.Remove(tab.Id);
                }

                // Remove from group if in one
                tab.Group?.RemoveTab(tab);

                var index = Tabs.IndexOf(tab);
                Tabs.Remove(tab);

                TabRemoved?.Invoke(this, tab);

                // Select adjacent tab
                if (ActiveTab == tab)
                {
                    if (Tabs.Count > 0)
                    {
                        var newIndex = Math.Min(index, Tabs.Count - 1);
                        ActiveTab = Tabs[newIndex];
                    }
                    else
                    {
                        ActiveTab = null;
                    }
                }
                break;

            case "CloseWind":
                // Close Wind application
                CloseWindRequested?.Invoke(this, EventArgs.Empty);
                break;

            default:
                // Fallback to default behavior
                goto case "CloseApp";
        }
    }

    public WindowHost? GetWindowHost(TabItem tab)
    {
        return _windowHosts.TryGetValue(tab.Id, out var host) ? host : null;
    }

    public bool IsExternallyManagedTab(TabItem tab)
    {
        return _externallyManagedWindows.ContainsKey(tab.Id);
    }

    public bool TryGetExternallyManagedWindowHandle(TabItem tab, out IntPtr handle)
    {
        return _externallyManagedWindows.TryGetValue(tab.Id, out handle);
    }

    public bool CanTileTab(TabItem tab)
    {
        return _windowHosts.ContainsKey(tab.Id);
    }

    public void SelectTab(int index)
    {
        if (index >= 0 && index < Tabs.Count)
        {
            ActiveTab = Tabs[index];
        }
    }

    public void SelectNextTab()
    {
        if (Tabs.Count == 0 || ActiveTab == null) return;

        var index = Tabs.IndexOf(ActiveTab);
        var nextIndex = (index + 1) % Tabs.Count;
        ActiveTab = Tabs[nextIndex];
    }

    public void SelectPreviousTab()
    {
        if (Tabs.Count == 0 || ActiveTab == null) return;

        var index = Tabs.IndexOf(ActiveTab);
        var prevIndex = (index - 1 + Tabs.Count) % Tabs.Count;
        ActiveTab = Tabs[prevIndex];
    }

    public void MoveTab(TabItem tab, int newIndex)
    {
        var oldIndex = Tabs.IndexOf(tab);
        if (oldIndex < 0 || oldIndex == newIndex) return;

        Tabs.Move(oldIndex, newIndex);
    }
}
