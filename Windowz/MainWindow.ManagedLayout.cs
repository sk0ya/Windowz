using System.Windows;
using System.Windows.Interop;
using WindowzTabManager.Models;

namespace WindowzTabManager;

public partial class MainWindow
{
    private enum ManagedWindowLayoutMode
    {
        None,
        SingleWindow,
        Tile
    }

    private readonly record struct ManagedWindowLayoutTarget(
        ManagedWindowLayoutMode Mode,
        TileLayout? Tile,
        IntPtr Handle)
    {
        public static ManagedWindowLayoutTarget None => new(ManagedWindowLayoutMode.None, null, IntPtr.Zero);

        public static ManagedWindowLayoutTarget ForSingleWindow(IntPtr handle) =>
            new(ManagedWindowLayoutMode.SingleWindow, null, handle);

        public static ManagedWindowLayoutTarget ForTile(TileLayout tile) =>
            new(ManagedWindowLayoutMode.Tile, tile, IntPtr.Zero);
    }

    private sealed class TileLayoutMembers
    {
        public List<(TabItem Tab, IntPtr Handle, int Index)> WindowMembers { get; } = [];
        public List<(TabItem Tab, int Index)> WebMembers { get; } = [];
        public List<(TabItem Tab, int Index)> ContentMembers { get; } = [];

        public int Count => WindowMembers.Count + WebMembers.Count + ContentMembers.Count;

        public IReadOnlySet<IntPtr> CreateWindowHandleSet() =>
            new HashSet<IntPtr>(WindowMembers.Select(member => member.Handle));
    }

    private void UpdateManagedWindowLayout(bool activate, bool positionOnlyUpdate = false)
    {
        if (_isSyncingWindFromManagedWindow)
            return;

        var target = ResolveManagedWindowLayoutTarget();

        switch (target.Mode)
        {
            case ManagedWindowLayoutMode.Tile:
                _isTileModeActive = true;
                UpdateTileLayout(target.Tile!, activate, positionOnlyUpdate);
                if (!positionOnlyUpdate)
                {
                    UpdateManagedSurfaceRegion(target);
                }
                return;

            case ManagedWindowLayoutMode.SingleWindow:
                ExitTileModeIfNeeded();
                UpdateSingleManagedWindowLayout(target.Handle, activate);
                if (!positionOnlyUpdate)
                {
                    UpdateManagedSurfaceRegion(target);
                }
                return;

            default:
                if (ShouldPreserveTileLayoutState())
                {
                    HideManagedWindows();
                    if (!positionOnlyUpdate)
                    {
                        UpdateManagedSurfaceRegion(target);
                    }
                    return;
                }

                ExitTileModeIfNeeded();
                HideManagedWindows();
                if (!positionOnlyUpdate)
                {
                    UpdateManagedSurfaceRegion(target);
                }
                return;
        }
    }

    private bool ShouldPreserveTileLayoutState()
    {
        return _isTileModeActive &&
               _viewModel.SelectedTab?.TileLayout != null &&
               _viewModel.IsWindowPickerOpen;
    }

    private ManagedWindowLayoutTarget ResolveManagedWindowLayoutTarget()
    {
        var selectedTab = _viewModel.SelectedTab;
        var tile = selectedTab?.TileLayout;

        if (tile != null &&
            tile.Tabs.Count >= 2 &&
            !_viewModel.IsWindowPickerOpen &&
            WindowState != WindowState.Minimized)
        {
            return ManagedWindowLayoutTarget.ForTile(tile);
        }

        if (_viewModel.IsWindowPickerOpen ||
            _viewModel.IsContentTabActive ||
            _viewModel.IsWebTabActive ||
            WindowState == WindowState.Minimized ||
            selectedTab == null ||
            !_viewModel.TryGetExternallyManagedWindowHandle(selectedTab, out var targetHandle))
        {
            return ManagedWindowLayoutTarget.None;
        }

        return ManagedWindowLayoutTarget.ForSingleWindow(targetHandle);
    }

    private void ExitTileModeIfNeeded()
    {
        if (!_isTileModeActive)
            return;

        _isTileModeActive = false;
        ResetTileViewState();
    }

    private void HideManagedWindows()
    {
        MinimizeManagedWindowsExcept(IntPtr.Zero);
        RemoveManagedWindowSyncHooks();
        HideTileSplitterOverlay();
        _activeManagedWindowHandle = IntPtr.Zero;
    }

    private void UpdateSingleManagedWindowLayout(IntPtr targetHandle, bool activate)
    {
        IntPtr previousHandle = IntPtr.Zero;
        bool deferPreviousWindowMinimize =
            activate &&
            TryGetManagedWindowSwitchSource(targetHandle, out previousHandle);

        if (deferPreviousWindowMinimize)
        {
            MinimizeManagedWindowsExcept(new HashSet<IntPtr> { targetHandle, previousHandle });
        }
        else
        {
            MinimizeManagedWindowsExcept(targetHandle);
        }

        if (targetHandle == IntPtr.Zero)
        {
            RemoveManagedWindowSyncHooks();
            _activeManagedWindowHandle = IntPtr.Zero;
            return;
        }

        EnsureManagedWindowSyncHooks(targetHandle);

        bool bringToFront = activate || targetHandle != _activeManagedWindowHandle;
        if (!TryGetManagedWindowBounds(out var bounds))
        {
            bool activatedAtCurrentBounds = TryActivateManagedWindowAtCurrentBounds(targetHandle, bringToFront);
            _activeManagedWindowHandle = targetHandle;

            if (deferPreviousWindowMinimize && activatedAtCurrentBounds)
            {
                MinimizeManagedWindowsExcept(targetHandle);
            }

            return;
        }

        ActivateManagedWindow(
            targetHandle,
            bounds.Left,
            bounds.Top,
            bounds.Width,
            bounds.Height,
            bringToFront);

        _activeManagedWindowHandle = targetHandle;

        if (deferPreviousWindowMinimize &&
            NativeMethods.IsWindow(targetHandle) &&
            NativeMethods.IsWindowVisible(targetHandle) &&
            !NativeMethods.IsIconic(targetHandle))
        {
            MinimizeManagedWindowsExcept(targetHandle);
        }
    }

    private bool TryGetManagedWindowSwitchSource(IntPtr targetHandle, out IntPtr previousHandle)
    {
        previousHandle = IntPtr.Zero;

        if (targetHandle == IntPtr.Zero ||
            _activeManagedWindowHandle == IntPtr.Zero ||
            _activeManagedWindowHandle == targetHandle ||
            !_windowManager.IsManaged(_activeManagedWindowHandle) ||
            !NativeMethods.IsWindow(_activeManagedWindowHandle))
        {
            return false;
        }

        var previousTab = FindExternallyManagedTabByHandle(_activeManagedWindowHandle);
        if (previousTab?.TileLayout != null)
            return false;

        previousHandle = _activeManagedWindowHandle;
        return true;
    }

    private bool TryActivateManagedWindowAtCurrentBounds(IntPtr targetHandle, bool bringToFront)
    {
        if (!NativeMethods.TryGetVisibleWindowRect(targetHandle, out var currentRect))
            return false;

        ActivateManagedWindow(
            targetHandle,
            currentRect.Left,
            currentRect.Top,
            Math.Max(1, currentRect.Width),
            Math.Max(1, currentRect.Height),
            bringToFront);

        return NativeMethods.IsWindow(targetHandle) &&
               NativeMethods.IsWindowVisible(targetHandle) &&
               !NativeMethods.IsIconic(targetHandle);
    }

    private void ActivateManagedWindow(
        IntPtr handle,
        int left,
        int top,
        int width,
        int height,
        bool bringToFront)
    {
        RunManagedWindowSync(() =>
        {
            _windowManager.ActivateManagedWindow(
                handle,
                left,
                top,
                width,
                height,
                bringToFront,
                _mainWindowHandle);
        }, ManagedWindowEventIgnoreDurationMs);
    }

    private void MinimizeManagedWindowsExcept(IntPtr targetHandle)
    {
        RunManagedWindowSync(() => _windowManager.MinimizeAllManagedWindowsExcept(targetHandle));
    }

    private void MinimizeManagedWindowsExcept(IReadOnlySet<IntPtr> targetHandles)
    {
        RunManagedWindowSync(() => _windowManager.MinimizeAllManagedWindowsExcept(targetHandles));
    }

    private void UpdateTileLayout(TileLayout tile, bool activate, bool positionOnlyUpdate = false)
    {
        var fractions = tile.GetLayoutFractions();
        var members = ClassifyTileMembers(tile, fractions.Length);

        if (members.Count < 2)
        {
            _tabManager.ReleaseTile(tile);
            UpdateManagedWindowLayout(activate);
            return;
        }

        WindowHostContainer.Visibility = Visibility.Visible;
        MinimizeManagedWindowsExcept(members.CreateWindowHandleSet());

        ApplyTileWebLayout(members.WebMembers, fractions, positionOnlyUpdate);
        ApplyTileContentLayout(members.ContentMembers, fractions);
        ApplyTileWindowLayout(members, fractions, activate);
        UpdateTileSplitterOverlay(tile, fractions);
    }

    private TileLayoutMembers ClassifyTileMembers(TileLayout tile, int fractionCount)
    {
        var members = new TileLayoutMembers();

        for (int i = 0; i < tile.Tabs.Count && i < fractionCount; i++)
        {
            var tab = tile.Tabs[i];

            if (tab.IsContentTab)
            {
                members.ContentMembers.Add((tab, i));
                continue;
            }

            if (tab.IsWebTab)
            {
                members.WebMembers.Add((tab, i));
                continue;
            }

            if (_tabManager.TryGetExternallyManagedWindowHandle(tab, out var handle) && handle != IntPtr.Zero)
            {
                members.WindowMembers.Add((tab, handle, i));
            }
        }

        return members;
    }

    private void ApplyTileWebLayout(
        IReadOnlyList<(TabItem Tab, int Index)> webMembers,
        (double Left, double Top, double Width, double Height)[] fractions,
        bool positionOnlyUpdate)
    {
        if (webMembers.Count == 0)
        {
            HideAllWebTabs();
            WebTabContainer.Visibility = Visibility.Collapsed;
            return;
        }

        if (!positionOnlyUpdate)
        {
            foreach (var control in _webTabControls.Values)
            {
                control.Visibility = Visibility.Collapsed;
                control.HorizontalAlignment = HorizontalAlignment.Stretch;
                control.VerticalAlignment = VerticalAlignment.Stretch;
                control.Width = double.NaN;
                control.Height = double.NaN;
                control.Margin = new Thickness(0);
            }

            double containerWidth = WindowHostContainer.ActualWidth;
            double containerHeight = WindowHostContainer.ActualHeight;

            foreach (var (tab, index) in webMembers)
            {
                if (!_webTabControls.TryGetValue(tab.Id, out var control))
                {
                    _ = InitWebTabForTileAsync(tab);
                    continue;
                }

                var fraction = fractions[index];
                control.HorizontalAlignment = HorizontalAlignment.Left;
                control.VerticalAlignment = VerticalAlignment.Top;
                var bounds = GetTileSlotBoundsDip(fraction, containerWidth, containerHeight);
                control.Width = Math.Max(1, bounds.Width);
                control.Height = Math.Max(1, bounds.Height);
                control.Margin = new Thickness(bounds.Left, bounds.Top, 0, 0);
                control.Visibility = Visibility.Visible;
            }
        }

        WebTabContainer.Visibility = Visibility.Visible;
        _currentWebTabId = webMembers.Last().Tab.Id;
    }

    private void ApplyTileContentLayout(
        IReadOnlyList<(TabItem Tab, int Index)> contentMembers,
        (double Left, double Top, double Width, double Height)[] fractions)
    {
        if (contentMembers.Count == 0)
        {
            ContentTabContainer.Visibility = Visibility.Collapsed;
            ContentTabContent.Content = null;
            ContentTabContainer.HorizontalAlignment = HorizontalAlignment.Stretch;
            ContentTabContainer.VerticalAlignment = VerticalAlignment.Stretch;
            ContentTabContainer.Width = double.NaN;
            ContentTabContainer.Height = double.NaN;
            ContentTabContainer.Margin = new Thickness(0);
            return;
        }

        double containerWidth = WindowHostContainer.ActualWidth;
        double containerHeight = WindowHostContainer.ActualHeight;
        var (contentTab, contentIndex) = contentMembers[0];
        var fraction = fractions[contentIndex];

        ContentTabContainer.HorizontalAlignment = HorizontalAlignment.Left;
        ContentTabContainer.VerticalAlignment = VerticalAlignment.Top;
        var bounds = GetTileSlotBoundsDip(fraction, containerWidth, containerHeight);
        ContentTabContainer.Width = Math.Max(1, bounds.Width);
        ContentTabContainer.Height = Math.Max(1, bounds.Height);
        ContentTabContainer.Margin = new Thickness(bounds.Left, bounds.Top, 0, 0);
        ShowContentTab(contentTab.ContentKey);
        ContentTabContainer.Visibility = Visibility.Visible;
    }

    private void ApplyTileWindowLayout(
        TileLayoutMembers members,
        (double Left, double Top, double Width, double Height)[] fractions,
        bool activate)
    {
        if (members.WindowMembers.Count == 0)
        {
            RemoveManagedWindowSyncHooks();
            _activeManagedWindowHandle = IntPtr.Zero;
            return;
        }

        if (!TryGetManagedWindowBounds(out var totalBounds))
            return;

        var selectedTab = _viewModel.SelectedTab;
        var primaryHandle = ResolveTilePrimaryWindowHandle(members, selectedTab);
        bool bringSelectedWindowToFront =
            activate &&
            selectedTab != null &&
            !selectedTab.IsContentTab &&
            !selectedTab.IsWebTab;
        var orderedWindowMembers = members.WindowMembers
            .OrderBy(member => member.Handle == primaryHandle ? 1 : 0)
            .ToList();
        var windHwnd = new WindowInteropHelper(this).Handle;

        RunManagedWindowSync(() =>
        {
            foreach (var (tab, handle, index) in orderedWindowMembers)
            {
                var fraction = fractions[index];
                var bounds = GetTileSlotBoundsPx(fraction, totalBounds);

                _windowManager.ActivateManagedWindow(
                    handle,
                    bounds.Left,
                    bounds.Top,
                    bounds.Width,
                    bounds.Height,
                    bringSelectedWindowToFront && selectedTab == tab && handle == primaryHandle,
                    setZOrder: false);
            }

            RaiseTileWindowsAboveWindowz(
                windHwnd,
                orderedWindowMembers.Select(member => member.Handle));
        }, ManagedWindowEventIgnoreDurationMs);

        EnsureManagedWindowSyncHooks(primaryHandle);
        _activeManagedWindowHandle = primaryHandle;
        SetupTileExtraHooks(members.WindowMembers.Select(member => (member.Handle, member.Index)).ToList());
    }

    private static IntPtr ResolveTilePrimaryWindowHandle(TileLayoutMembers members, TabItem? selectedTab)
    {
        if (selectedTab != null)
        {
            var selectedMember = members.WindowMembers.FirstOrDefault(member => member.Tab == selectedTab);
            if (selectedMember.Handle != IntPtr.Zero)
                return selectedMember.Handle;
        }

        return members.WindowMembers[0].Handle;
    }

    private static void RaiseTileWindowsAboveWindowz(IntPtr windHwnd, IEnumerable<IntPtr> tileHandles)
    {
        if (windHwnd == IntPtr.Zero || !NativeMethods.IsWindow(windHwnd))
            return;

        IntPtr insertAfter = windHwnd;

        foreach (var handle in tileHandles)
        {
            if (handle == IntPtr.Zero ||
                handle == windHwnd ||
                !NativeMethods.IsWindow(handle))
            {
                continue;
            }

            NativeMethods.SetWindowPos(
                handle,
                insertAfter,
                0,
                0,
                0,
                0,
                NativeMethods.SWP_NOMOVE |
                NativeMethods.SWP_NOSIZE |
                NativeMethods.SWP_NOACTIVATE);

            insertAfter = handle;
        }
    }

    private async Task InitWebTabForTileAsync(TabItem tab)
    {
        if (_webTabControls.ContainsKey(tab.Id))
            return;

        var envService = App.GetService<Services.WebViewEnvironmentService>();
        var control = new Views.WebTabControl(tab.Id, envService);

        control.TitleChanged += (_, title) => tab.Title = title;
        control.UrlChanged += (_, url) => tab.WebUrl = url;
        control.FaviconChanged += (_, icon) =>
        {
            if (icon != null)
                tab.Icon = icon;
        };

        _webTabControls[tab.Id] = control;
        _tabManager.RegisterWebTabControl(tab.Id, control);
        WebTabContainer.Children.Add(control);

        await control.InitializeAsync(tab.WebUrl ?? "https://www.google.com");

        if (tab.TileLayout != null)
            UpdateManagedWindowLayout(activate: false);
    }

    /// <summary>
    /// ドラッグ中に SWP_ASYNCWINDOWPOS で管理ウィンドウを非同期追従させる。
    /// UIスレッドをブロックしないため、ドラッグがスムーズになる。
    /// </summary>
    private void AsyncMoveManagedWindowsDuringDrag()
    {
        if (_isSyncingWindFromManagedWindow) return;
        if (_viewModel.IsWindowPickerOpen ||
            _viewModel.IsCommandPaletteOpen ||
            _viewModel.IsContentTabActive ||
            _viewModel.IsWebTabActive)
            return;

        var selectedTab = _viewModel.SelectedTab;
        if (selectedTab == null) return;

        if (!TryGetManagedWindowBounds(out var totalBounds)) return;

        var tile = selectedTab.TileLayout;
        if (tile != null && tile.Tabs.Count >= 2 && WindowState != WindowState.Minimized)
        {
            // タイル: 各スロットのウィンドウを非同期で移動
            var fractions = tile.GetLayoutFractions();
            for (int i = 0; i < tile.Tabs.Count && i < fractions.Length; i++)
            {
                var tab = tile.Tabs[i];
                if (!_tabManager.TryGetExternallyManagedWindowHandle(tab, out var tileHandle) ||
                    tileHandle == IntPtr.Zero)
                    continue;

                var bounds = GetTileSlotBoundsPx(fractions[i], totalBounds);
                _windowManager.MoveManagedWindowAsync(
                    tileHandle,
                    bounds.Left,
                    bounds.Top,
                    bounds.Width,
                    bounds.Height);
            }
        }
        else if (_viewModel.TryGetExternallyManagedWindowHandle(selectedTab, out var handle) &&
                 handle != IntPtr.Zero)
        {
            // シングルウィンドウ: 非同期で移動
            _windowManager.MoveManagedWindowAsync(
                handle,
                totalBounds.Left,
                totalBounds.Top,
                totalBounds.Width,
                totalBounds.Height);
        }
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

    private void UpdateManagedSurfaceRegion(ManagedWindowLayoutTarget target)
    {
        var hwnd = _mainWindowHandle != IntPtr.Zero
            ? _mainWindowHandle
            : new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
            return;

        if (!TryBuildManagedSurfaceRegion(target, out int windowWidth, out int windowHeight, out var holeRects) ||
            holeRects.Count == 0)
        {
            ClearManagedSurfaceRegion(hwnd);
            return;
        }

        string nextRegionKey = BuildManagedSurfaceRegionKey(windowWidth, windowHeight, holeRects);
        if (string.Equals(_managedSurfaceRegionKey, nextRegionKey, StringComparison.Ordinal))
            return;

        IntPtr region = NativeMethods.CreateRectRgn(0, 0, windowWidth, windowHeight);
        if (region == IntPtr.Zero)
        {
            ClearManagedSurfaceRegion(hwnd);
            return;
        }

        try
        {
            foreach (var holeRect in holeRects)
            {
                IntPtr holeRegion = NativeMethods.CreateRectRgn(
                    holeRect.Left,
                    holeRect.Top,
                    holeRect.Right,
                    holeRect.Bottom);
                if (holeRegion == IntPtr.Zero)
                    continue;

                try
                {
                    NativeMethods.CombineRgn(region, region, holeRegion, NativeMethods.RGN_DIFF);
                }
                finally
                {
                    NativeMethods.DeleteObject(holeRegion);
                }
            }

            if (NativeMethods.SetWindowRgn(hwnd, region, true) == 0)
            {
                NativeMethods.DeleteObject(region);
                ClearManagedSurfaceRegion(hwnd);
                return;
            }

            region = IntPtr.Zero;
            _managedSurfaceRegionKey = nextRegionKey;
        }
        finally
        {
            if (region != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(region);
            }
        }
    }

    private void ClearManagedSurfaceRegion(IntPtr hwnd)
    {
        if (_managedSurfaceRegionKey == null)
            return;

        NativeMethods.SetWindowRgn(hwnd, IntPtr.Zero, true);
        _managedSurfaceRegionKey = null;
    }

    private bool TryBuildManagedSurfaceRegion(
        ManagedWindowLayoutTarget target,
        out int windowWidth,
        out int windowHeight,
        out List<NativeMethods.RECT> holeRects)
    {
        windowWidth = 0;
        windowHeight = 0;
        holeRects = [];

        if (target.Mode == ManagedWindowLayoutMode.None)
            return false;

        var hwnd = _mainWindowHandle != IntPtr.Zero
            ? _mainWindowHandle
            : new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !NativeMethods.GetWindowRect(hwnd, out var windowRect))
            return false;

        windowWidth = Math.Max(1, windowRect.Width);
        windowHeight = Math.Max(1, windowRect.Height);

        if (!TryGetManagedSurfaceBoundsRelativeToWindow(out var hostBounds))
            return false;

        switch (target.Mode)
        {
            case ManagedWindowLayoutMode.SingleWindow:
                if (TryClipRegionRect(hostBounds, windowWidth, windowHeight, out var singleBounds))
                {
                    holeRects.Add(singleBounds);
                    return true;
                }

                return false;

            case ManagedWindowLayoutMode.Tile:
                var tile = target.Tile;
                if (tile == null)
                    return false;

                var fractions = tile.GetLayoutFractions();
                var members = ClassifyTileMembers(tile, fractions.Length);
                foreach (var (_, _, index) in members.WindowMembers)
                {
                    if (index < 0 || index >= fractions.Length)
                        continue;

                    var slotBounds = GetTileSlotBoundsPx(fractions[index], hostBounds);
                    if (TryClipRegionRect(slotBounds, windowWidth, windowHeight, out var clippedSlotBounds))
                    {
                        holeRects.Add(clippedSlotBounds);
                    }
                }

                return holeRects.Count > 0;

            default:
                return false;
        }
    }

    private bool TryGetManagedSurfaceBoundsRelativeToWindow(out NativeMethods.RECT bounds)
    {
        bounds = default;

        if (!IsLoaded ||
            WindowHostContainer.Visibility != Visibility.Visible ||
            WindowHostContainer.ActualWidth <= 0 ||
            WindowHostContainer.ActualHeight <= 0)
        {
            return false;
        }

        var (dpiScaleX, dpiScaleY) = GetCurrentDpiScale();
        Point hostOffsetDip = WindowHostContainer.TranslatePoint(new Point(0, 0), this);

        int left = (int)Math.Round(hostOffsetDip.X * dpiScaleX);
        int top = (int)Math.Round(hostOffsetDip.Y * dpiScaleY);
        int width = Math.Max(1, (int)Math.Round(WindowHostContainer.ActualWidth * dpiScaleX));
        int height = Math.Max(1, (int)Math.Round(WindowHostContainer.ActualHeight * dpiScaleY));

        bounds = new NativeMethods.RECT
        {
            Left = left,
            Top = top,
            Right = left + width,
            Bottom = top + height
        };
        return true;
    }

    private static bool TryClipRegionRect(
        NativeMethods.RECT bounds,
        int windowWidth,
        int windowHeight,
        out NativeMethods.RECT clippedBounds)
    {
        clippedBounds = default;

        int left = Math.Clamp(bounds.Left, 0, windowWidth);
        int top = Math.Clamp(bounds.Top, 0, windowHeight);
        int right = Math.Clamp(bounds.Right, 0, windowWidth);
        int bottom = Math.Clamp(bounds.Bottom, 0, windowHeight);
        if (right <= left || bottom <= top)
            return false;

        clippedBounds = new NativeMethods.RECT
        {
            Left = left,
            Top = top,
            Right = right,
            Bottom = bottom
        };
        return true;
    }

    private static string BuildManagedSurfaceRegionKey(
        int windowWidth,
        int windowHeight,
        IReadOnlyList<NativeMethods.RECT> holeRects)
    {
        return $"{windowWidth}x{windowHeight}:{string.Join(";", holeRects.Select(rect =>
            $"{rect.Left},{rect.Top},{rect.Right},{rect.Bottom}"))}";
    }
}
