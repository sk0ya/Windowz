using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WindowzTabManager.Models;
using WindowzTabManager.Services;
using WindowzTabManager.ViewModels;
using WindowzTabManager.Views;
using WindowzTabManager.Views.Settings;
using WpfButton = System.Windows.Controls.Button;

namespace WindowzTabManager;

public partial class MainWindow : Window
{
    private enum WindowControlAction
    {
        None,
        Menu,
        Minimize,
        Maximize,
        Close
    }

    private readonly MainViewModel _viewModel;
    private readonly HotkeyManager _hotkeyManager;
    private readonly TabManager _tabManager;
    private readonly WindowManager _windowManager;
    private readonly SettingsManager _settingsManager;
    private IntPtr _mainWindowHandle;
    private IntPtr _activeManagedWindowHandle;
    private Point? _dragStartPoint;
    private bool _isDragging;
    private bool _suppressManagedWindowPromotion;
    private WindowControlAction _pendingWindowControlAction;
    private WpfButton? _pendingWindowControlButton;
    private double _dragWindowOriginX;
    private double _dragWindowOriginY;
    private int _dragCursorOriginX;
    private int _dragCursorOriginY;
    private DispatcherTimer? _dragPollTimer;
    private SettingsTabsPage? _settingsTabsPage;
    private string _pendingSettingsContentKey = "GeneralSettings";
    private string _currentTabPosition = "Top";
    private bool _isTabBarCollapsed;
    private bool _wasMinimized;
    private readonly Dictionary<Guid, WebTabControl> _webTabControls = new();
    private Guid? _currentWebTabId;
    private bool _isTileModeActive;
    private const int CloseWaitTimeoutMs = 10000;
    private bool _isWaitingForCloseTargets;
    private bool _skipCloseWaitOnce;
    private bool _isCleanupCompleted;
    private CancellationTokenSource? _closeWaitCts;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = App.GetService<MainViewModel>();
        _hotkeyManager = App.GetService<HotkeyManager>();
        _tabManager = App.GetService<TabManager>();
        _windowManager = App.GetService<WindowManager>();
        _settingsManager = App.GetService<SettingsManager>();

        DataContext = _viewModel;
        WindowPickerControl.DataContext = App.GetService<WindowPickerViewModel>();

        SourceInitialized += MainWindow_SourceInitialized;
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        SizeChanged += MainWindow_SizeChanged;
        LocationChanged += MainWindow_LocationChanged;
        Activated += MainWindow_Activated;
        Deactivated += MainWindow_Deactivated;
        WindowHostContainer.SizeChanged += WindowHostContainer_SizeChanged;

        _viewModel.PropertyChanged += ViewModel_PropertyChanged;

        // Wire up window picker events
        var pickerVm = (WindowPickerViewModel) WindowPickerControl.DataContext;
        pickerVm.WindowSelected += (s, window) =>
        {
            _viewModel.AddWindowCommand.Execute(window);
            RestoreEmbeddedWindow();
        };
        pickerVm.Cancelled += (s, e) =>
        {
            _viewModel.CloseWindowPickerCommand.Execute(null);
            RestoreEmbeddedWindow();
        };
        pickerVm.QuickLaunchSettingsRequested += (s, e) =>
        {
            _viewModel.CloseWindowPickerCommand.Execute(null);
            // Settings replaces the current content immediately, so restoring the
            // previously active embedded/tiled content here only creates a
            // transient foreground jump for WebView2.
            OpenSettingsTab("QuickLaunchSettings");
        };
        pickerVm.WebTabRequested += (s, url) =>
        {
            _viewModel.CloseWindowPickerCommand.Execute(null);
            RestoreEmbeddedWindow();
            _viewModel.OpenWebTabCommand.Execute(url);
        };
        pickerVm.TileGroupWindowsReady += (s, windows) =>
        {
            var addedTabs = new List<Models.TabItem>();
            foreach (var win in windows)
            {
                var tab = _tabManager.AddTab(win, activate: false);
                if (tab != null) addedTabs.Add(tab);
            }
            if (addedTabs.Count >= 2)
            {
                var tile = _tabManager.TileSpecificTabs(addedTabs);
                if (tile?.Tabs.Count > 0)
                    _tabManager.ActiveTab = tile.Tabs[0];
            }
            else if (addedTabs.Count == 1)
            {
                _tabManager.ActiveTab = addedTabs[0];
            }
        };

        // Wire up command palette events
        CommandPaletteControl.DataContext = App.GetService<CommandPaletteViewModel>();
        var paletteVm = (CommandPaletteViewModel)CommandPaletteControl.DataContext;
        paletteVm.ItemExecuted += OnCommandPaletteItemExecuted;
        paletteVm.Cancelled += (s, e) =>
        {
            _viewModel.CloseCommandPaletteCommand.Execute(null);
        };

        _tabManager.CloseWindRequested += (s, e) => { Close(); };

        // Subscribe to tab position changes
        _settingsManager.TabHeaderPositionChanged += OnTabHeaderPositionChanged;

        // Apply initial tab position
        ApplyTabHeaderPosition(_settingsManager.Settings.TabHeaderPosition);
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;

        // Add WndProc hook before the window is shown so WM_NCCALCSIZE is
        // intercepted from the very first frame draw.
        _mainWindowHandle = hwnd;

        var source = HwndSource.FromHwnd(hwnd);
        source?.AddHook(WndProc);

        SetupForegroundActivationHook();

        // Remove the OS accent-color border drawn by DWM on Windows 11.
        uint noBorder = NativeMethods.DWMWA_COLOR_NONE;
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_BORDER_COLOR, ref noBorder, sizeof(uint));

        // Force a frame recalculation so the WM_NCCALCSIZE = 0 result is applied
        // before the window becomes visible.
        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE |
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE |
            NativeMethods.SWP_FRAMECHANGED);
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _hotkeyManager.Initialize(this, _settingsManager);
        SetupExternalDragHooks();
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_isCleanupCompleted)
            return;

        if (_skipCloseWaitOnce)
        {
            _skipCloseWaitOnce = false;
            PerformShutdownCleanup();
            return;
        }

        if (_isWaitingForCloseTargets)
        {
            e.Cancel = true;
            return;
        }

        if (TryStartCloseTargetWait(e))
            return;

        PerformShutdownCleanup();
    }

    private bool TryStartCloseTargetWait(System.ComponentModel.CancelEventArgs e)
    {
        var setting = _settingsManager.Settings.CloseWindowsOnExit;
        if (setting != "All" && setting != "StartupOnly")
            return false;

        bool startupOnly = setting == "StartupOnly";
        var tabsSnapshot = _tabManager.Tabs.ToList();
        var pidsToWait = new HashSet<int>();
        var tabIdsToWait = new HashSet<Guid>();

        foreach (var tab in tabsSnapshot)
        {
            bool isEmbeddedTab = !tab.IsContentTab && !tab.IsWebTab;
            bool shouldWaitByPid = isEmbeddedTab && (!startupOnly || tab.IsLaunchedAtStartup);

            if (shouldWaitByPid)
            {
                int processId = 0;

                if (_tabManager.IsExternallyManagedTab(tab) && tab.Window?.ProcessId is int managedPid)
                {
                    processId = managedPid;
                }
                else
                {
                    tabIdsToWait.Add(tab.Id);
                    TryRemoveTab(tab);
                    continue;
                }

                if (tab.Window?.Handle is IntPtr hwnd && hwnd != IntPtr.Zero)
                {
                    NativeMethods.PostMessage(hwnd, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                }

                if (processId != 0 && IsProcessRunning(processId))
                {
                    pidsToWait.Add(processId);
                }
                else
                {
                    tabIdsToWait.Add(tab.Id);
                }

                continue;
            }

            // Non-embedded tabs and non-target embedded tabs are tracked by tab closure.
            tabIdsToWait.Add(tab.Id);
            TryRemoveTab(tab);
        }

        tabIdsToWait.RemoveWhere(tabId => !_tabManager.Tabs.Any(t => t.Id == tabId));
        pidsToWait.RemoveWhere(pid => !IsProcessRunning(pid));

        if (pidsToWait.Count == 0 && tabIdsToWait.Count == 0)
            return false;

        e.Cancel = true;
        _isWaitingForCloseTargets = true;

        _closeWaitCts?.Cancel();
        _closeWaitCts?.Dispose();
        _closeWaitCts = new CancellationTokenSource();
        _ = WaitForCloseTargetsAsync(pidsToWait, tabIdsToWait, _closeWaitCts.Token);
        return true;
    }

    private static bool IsProcessRunning(int pid)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            return !proc.HasExited;
        }
        catch { return false; }
    }

    private async Task WaitForCloseTargetsAsync(
        HashSet<int> pidsToWait,
        HashSet<Guid> tabIdsToWait,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            while (sw.ElapsedMilliseconds < CloseWaitTimeoutMs)
            {
                cancellationToken.ThrowIfCancellationRequested();

                pidsToWait.RemoveWhere(pid => !IsProcessRunning(pid));
                tabIdsToWait.RemoveWhere(tabId => !_tabManager.Tabs.Any(t => t.Id == tabId));

                if (pidsToWait.Count == 0 && tabIdsToWait.Count == 0)
                    break;

                await Task.Delay(100, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (cancellationToken.IsCancellationRequested)
            return;

        _ = Dispatcher.BeginInvoke(() =>
        {
            if (_isCleanupCompleted)
                return;

            _isWaitingForCloseTargets = false;
            _skipCloseWaitOnce = true;
            Close();
        });
    }

    private void TryRemoveTab(TabItem tab)
    {
        if (!_tabManager.Tabs.Contains(tab))
            return;

        try
        {
            _tabManager.RemoveTab(tab);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to close tab {tab.Title}: {ex.Message}");
        }
    }

    private void PerformShutdownCleanup()
    {
        if (_isCleanupCompleted)
            return;

        _isCleanupCompleted = true;
        _isWaitingForCloseTargets = false;

        RemoveExternalDragHooks();
        RemoveForegroundActivationHook();
        RemoveManagedWindowSyncHooks();

        _closeWaitCts?.Cancel();
        _closeWaitCts?.Dispose();
        _closeWaitCts = null;

        _settingsManager.TabHeaderPositionChanged -= OnTabHeaderPositionChanged;

        // Dispose all web tab controls
        foreach (var control in _webTabControls.Values.ToList())
        {
            try
            {
                control.Dispose();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to dispose web tab control: {ex.Message}");
            }
        }
        _webTabControls.Clear();

        try
        {
            _viewModel.Cleanup();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Cleanup failed during shutdown: {ex.Message}");
        }
    }

    private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateManagedWindowLayout(activate: false);
    }

    private void MainWindow_LocationChanged(object? sender, EventArgs e)
    {
        if (_isDragging)
        {
            // ドラッグ中は SWP_ASYNCWINDOWPOS で管理ウィンドウを非同期追従させる。
            // 同期クロスプロセス SetWindowPos はUIスレッドをブロックしてカクつくが、
            // 非同期版はポストして即返るため Windowz のドラッグがスムーズになる。
            // WinEvent LOCATIONCHANGE フィードバックを抑制するため ignore tick も更新する。
            _ignoreManagedWindowEventsUntilTick =
                Environment.TickCount64 + ManagedWindowEventIgnoreDurationMs;
            AsyncMoveManagedWindowsDuringDrag();
            return;
        }

        // Web タブは WPF コントロールのため、ウィンドウ移動時は自動追従する。
        // positionOnlyUpdate=true で Web タブのレイアウト再計算をスキップし、
        // WebView2 の不要な再描画（チカチカ）を防ぐ。
        UpdateManagedWindowLayout(activate: false, positionOnlyUpdate: true);
    }

    private void WindowHostContainer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateManagedWindowLayout(activate: false);
    }

    private void Window_StateChanged(object sender, EventArgs e)
    {
        // Update maximize button icon
        if (WindowState == WindowState.Maximized)
        {
            MaximizeIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.SquareMultiple24;
        }
        else
        {
            MaximizeIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.Maximize24;
        }

        if (_wasMinimized && WindowState != WindowState.Minimized)
        {
            // Allow UpdateManagedWindowLayout to bring the managed window to the
            // foreground regardless of whether the handle has changed since minimize.
            _activeManagedWindowHandle = IntPtr.Zero;
        }

        _wasMinimized = WindowState == WindowState.Minimized;
        UpdateManagedWindowLayout(activate: false);
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        ExecuteWindowControlAction(WindowControlAction.Minimize);
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        ExecuteWindowControlAction(WindowControlAction.Maximize);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        ExecuteWindowControlAction(WindowControlAction.Close);
    }
}
