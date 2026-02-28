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

namespace WindowzTabManager;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly HotkeyManager _hotkeyManager;
    private readonly TabManager _tabManager;
    private readonly WindowManager _windowManager;
    private readonly SettingsManager _settingsManager;
    private IntPtr _activeManagedWindowHandle;
    private Point? _dragStartPoint;
    private bool _isDragging;
    private SettingsTabsPage? _settingsTabsPage;
    private string _pendingSettingsContentKey = "GeneralSettings";
    private string _currentTabPosition = "Top";
    private bool _isTabBarCollapsed;
    private bool _wasMinimized;
    private readonly Dictionary<Guid, WebTabControl> _webTabControls = new();
    private Guid? _currentWebTabId;
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
            RestoreEmbeddedWindow();
            OpenSettingsTab("QuickLaunchSettings");
        };
        pickerVm.WebTabRequested += (s, url) =>
        {
            _viewModel.CloseWindowPickerCommand.Execute(null);
            RestoreEmbeddedWindow();
            _viewModel.OpenWebTabCommand.Execute(url);
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
        var source = HwndSource.FromHwnd(hwnd);
        source?.AddHook(WndProc);

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
        UpdateManagedWindowLayout(activate: false);
    }

    private void WindowHostContainer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateManagedWindowLayout(activate: false);
    }

    private void UpdateBlockerPosition()
    {
        // Windowz behavior: no resize helper blocker.
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
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void UpdateManagedWindowLayout(bool activate)
    {
        if (_isSyncingWindFromManagedWindow)
            return;

        // タイル表示モードチェック
        var selectedTab = _viewModel.SelectedTab;
        var activeTile = selectedTab?.TileLayout;
        bool canShowTile =
            activeTile != null &&
            activeTile.Tabs.Count >= 2 &&
            !_viewModel.IsWindowPickerOpen &&
            !_viewModel.IsCommandPaletteOpen &&
            WindowState != WindowState.Minimized;

        if (canShowTile)
        {
            UpdateTileLayout(activeTile!, activate);
            return;
        }

        IntPtr targetHandle = IntPtr.Zero;
        bool canShowManagedWindow =
            !_viewModel.IsWindowPickerOpen &&
            !_viewModel.IsCommandPaletteOpen &&
            !_viewModel.IsContentTabActive &&
            !_viewModel.IsWebTabActive &&
            WindowState != WindowState.Minimized &&
            _viewModel.SelectedTab != null &&
            _viewModel.TryGetExternallyManagedWindowHandle(_viewModel.SelectedTab, out targetHandle);

        if (!canShowManagedWindow)
        {
            targetHandle = IntPtr.Zero;
        }

        _isSyncingManagedWindowFromWind = true;
        try
        {
            _windowManager.MinimizeAllManagedWindowsExcept(targetHandle);
        }
        finally
        {
            _isSyncingManagedWindowFromWind = false;
        }

        if (targetHandle == IntPtr.Zero)
        {
            RemoveManagedWindowSyncHooks();
            _activeManagedWindowHandle = IntPtr.Zero;
            return;
        }

        EnsureManagedWindowSyncHooks(targetHandle);

        bool bringToFront = activate || targetHandle != _activeManagedWindowHandle;
        var windHwnd = new WindowInteropHelper(this).Handle;

        if (!TryGetManagedWindowBounds(out var bounds))
        {
            // Layout is not ready yet. Keep the managed window at its current position
            // without updating _activeManagedWindowHandle, so the next call (once layout
            // has settled) still treats this as a first-time activation and can set
            // bringToFront correctly.
            if (NativeMethods.GetWindowRect(targetHandle, out var currentRect))
            {
                _ignoreManagedWindowEventsUntilTick = Environment.TickCount64 + ManagedWindowEventIgnoreDurationMs;
                _isSyncingManagedWindowFromWind = true;
                try
                {
                    _windowManager.ActivateManagedWindow(
                        targetHandle,
                        currentRect.Left,
                        currentRect.Top,
                        Math.Max(1, currentRect.Width),
                        Math.Max(1, currentRect.Height),
                        bringToFront: false,
                        windHwnd);
                }
                finally
                {
                    _isSyncingManagedWindowFromWind = false;
                }
            }

            return;
        }

        _ignoreManagedWindowEventsUntilTick = Environment.TickCount64 + ManagedWindowEventIgnoreDurationMs;
        _isSyncingManagedWindowFromWind = true;
        try
        {
            _windowManager.ActivateManagedWindow(
                targetHandle,
                bounds.Left,
                bounds.Top,
                bounds.Width,
                bounds.Height,
                bringToFront,
                windHwnd);
        }
        finally
        {
            _isSyncingManagedWindowFromWind = false;
        }

        _activeManagedWindowHandle = targetHandle;
    }

    private void UpdateTileLayout(Models.TileLayout tile, bool activate)
    {
        var fractions = tile.GetLayoutFractions();

        // タイルメンバーを種別ごとに分類
        var windowMembers = new List<(Models.TabItem Tab, IntPtr Handle, int Index)>();
        var webMembers = new List<(Models.TabItem Tab, int Index)>();
        var contentMembers = new List<(Models.TabItem Tab, int Index)>();

        for (int i = 0; i < tile.Tabs.Count && i < fractions.Length; i++)
        {
            var tab = tile.Tabs[i];
            if (tab.IsContentTab)
            {
                contentMembers.Add((tab, i));
            }
            else if (tab.IsWebTab)
            {
                webMembers.Add((tab, i));
            }
            else if (_tabManager.TryGetExternallyManagedWindowHandle(tab, out var h) && h != IntPtr.Zero)
            {
                windowMembers.Add((tab, h, i));
            }
        }

        // 有効なメンバーが 2 つ未満なら通常モードへフォールバック
        if (windowMembers.Count + webMembers.Count + contentMembers.Count < 2)
        {
            _tabManager.ReleaseTile(tile);
            UpdateManagedWindowLayout(activate);
            return;
        }

        // サイズ計算の基準として WindowHostContainer を必ず表示
        WindowHostContainer.Visibility = System.Windows.Visibility.Visible;

        // タイル以外のウィンドウを最小化
        var tileHandles = new HashSet<IntPtr>(windowMembers.Select(x => x.Handle));
        _isSyncingManagedWindowFromWind = true;
        try { _windowManager.MinimizeAllManagedWindowsExcept(tileHandles); }
        finally { _isSyncingManagedWindowFromWind = false; }

        // コンテナの DIP サイズ（Web タブ・ウィンドウタブ共通の基準）
        double containerW = WindowHostContainer.ActualWidth;
        double containerH = WindowHostContainer.ActualHeight;

        // ---- Web タブの配置 ----
        if (webMembers.Count > 0)
        {
            // まず全 Web コントロールをリセット
            foreach (var ctrl in _webTabControls.Values)
            {
                ctrl.Visibility = System.Windows.Visibility.Collapsed;
                ctrl.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
                ctrl.VerticalAlignment = System.Windows.VerticalAlignment.Stretch;
                ctrl.Width = double.NaN;
                ctrl.Height = double.NaN;
                ctrl.Margin = new System.Windows.Thickness(0);
            }

            foreach (var (tab, idx) in webMembers)
            {
                if (!_webTabControls.TryGetValue(tab.Id, out var control))
                {
                    // コントロール未作成の場合は非同期で初期化してレイアウトを再適用
                    _ = InitWebTabForTileAsync(tab);
                    continue;
                }

                var f = fractions[idx];
                control.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
                control.VerticalAlignment = System.Windows.VerticalAlignment.Top;
                control.Width = Math.Max(1, f.Width * containerW);
                control.Height = Math.Max(1, f.Height * containerH);
                control.Margin = new System.Windows.Thickness(f.Left * containerW, f.Top * containerH, 0, 0);
                control.Visibility = System.Windows.Visibility.Visible;
            }

            WebTabContainer.Visibility = System.Windows.Visibility.Visible;
            _currentWebTabId = webMembers.Last().Tab.Id;
        }
        else
        {
            HideAllWebTabs();
            WebTabContainer.Visibility = System.Windows.Visibility.Collapsed;
        }

        // ---- コンテンツタブ（設定など）の配置 ----
        if (contentMembers.Count > 0)
        {
            // コンテンツタブは同時に 1 つのみ表示（先頭を採用）
            var (contentTab, contentIdx) = contentMembers[0];
            var f = fractions[contentIdx];
            ContentTabContainer.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
            ContentTabContainer.VerticalAlignment = System.Windows.VerticalAlignment.Top;
            ContentTabContainer.Width = Math.Max(1, f.Width * containerW);
            ContentTabContainer.Height = Math.Max(1, f.Height * containerH);
            ContentTabContainer.Margin = new System.Windows.Thickness(f.Left * containerW, f.Top * containerH, 0, 0);
            ShowContentTab(contentTab.ContentKey);
            ContentTabContainer.Visibility = System.Windows.Visibility.Visible;
        }
        else
        {
            ContentTabContainer.Visibility = System.Windows.Visibility.Collapsed;
            ContentTabContent.Content = null;
            ContentTabContainer.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
            ContentTabContainer.VerticalAlignment = System.Windows.VerticalAlignment.Stretch;
            ContentTabContainer.Width = double.NaN;
            ContentTabContainer.Height = double.NaN;
            ContentTabContainer.Margin = new System.Windows.Thickness(0);
        }

        // ---- ウィンドウタブの配置 ----
        if (windowMembers.Count > 0)
        {
            if (!TryGetManagedWindowBounds(out var totalBounds)) return;

            var windHwnd = new WindowInteropHelper(this).Handle;
            _ignoreManagedWindowEventsUntilTick = Environment.TickCount64 + ManagedWindowEventIgnoreDurationMs;
            _isSyncingManagedWindowFromWind = true;
            try
            {
                foreach (var (_, handle, idx) in windowMembers)
                {
                    var f = fractions[idx];
                    int left   = totalBounds.Left + (int)Math.Round(f.Left   * totalBounds.Width);
                    int top    = totalBounds.Top  + (int)Math.Round(f.Top    * totalBounds.Height);
                    int width  = Math.Max(1, (int)Math.Round(f.Width  * totalBounds.Width));
                    int height = Math.Max(1, (int)Math.Round(f.Height * totalBounds.Height));
                    _windowManager.ActivateManagedWindow(handle, left, top, width, height,
                        bringToFront: false, windHwnd);
                }

                // ActivateManagedWindow はループ内で Windowz を各ウィンドウの直下へ配置するため、
                // ループ終了後は最後に処理したウィンドウだけが Windowz より上に残る。
                // 最初に配置した windowMembers[0] が Z 順で最下位にあるため、
                // その下へ Windowz を移動することで全タイルウィンドウの後ろに収まる。
                if (windowMembers.Count > 1)
                {
                    NativeMethods.SetWindowPos(
                        windHwnd,
                        windowMembers[0].Handle,
                        0, 0, 0, 0,
                        NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE |
                        NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOREDRAW);
                }
            }
            finally
            {
                _isSyncingManagedWindowFromWind = false;
            }

            // 同期フックは先頭ウィンドウタブに設定
            EnsureManagedWindowSyncHooks(windowMembers[0].Handle);
            _activeManagedWindowHandle = windowMembers[0].Handle;
        }
        else
        {
            RemoveManagedWindowSyncHooks();
            _activeManagedWindowHandle = IntPtr.Zero;
        }
    }

    private async System.Threading.Tasks.Task InitWebTabForTileAsync(Models.TabItem tab)
    {
        if (_webTabControls.ContainsKey(tab.Id)) return;

        var envService = App.GetService<Services.WebViewEnvironmentService>();
        var control = new Views.WebTabControl(tab.Id, envService);

        control.TitleChanged += (s, title) => tab.Title = title;
        control.UrlChanged += (s, url) => tab.WebUrl = url;
        control.FaviconChanged += (s, icon) => { if (icon != null) tab.Icon = icon; };

        _webTabControls[tab.Id] = control;
        _tabManager.RegisterWebTabControl(tab.Id, control);
        WebTabContainer.Children.Add(control);

        await control.InitializeAsync(tab.WebUrl ?? "https://www.google.com");

        // 初期化完了後にタイルレイアウトを再適用
        if (tab.TileLayout != null)
            UpdateManagedWindowLayout(activate: false);
    }

    private bool TryGetManagedWindowBounds(out NativeMethods.RECT bounds)
    {
        bounds = default;

        double widthDip = WindowHostContainer.ActualWidth;
        double heightDip = WindowHostContainer.ActualHeight;
        if (widthDip <= 0 || heightDip <= 0)
            return false;

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
            return false;

        if (!NativeMethods.GetWindowRect(hwnd, out var windRect))
            return false;

        var source = PresentationSource.FromVisual(this);
        double dpiScaleX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        double dpiScaleY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

        Point contentOffsetDip = WindowHostContainer.TranslatePoint(new Point(0, 0), this);
        int left = windRect.Left + (int)Math.Round(contentOffsetDip.X * dpiScaleX);
        int top = windRect.Top + (int)Math.Round(contentOffsetDip.Y * dpiScaleY);
        int width = Math.Max(1, (int)Math.Round(widthDip * dpiScaleX));
        int height = Math.Max(1, (int)Math.Round(heightDip * dpiScaleY));

        bounds = new NativeMethods.RECT
        {
            Left = left,
            Top = top,
            Right = left + width,
            Bottom = top + height
        };
        return true;
    }
}
