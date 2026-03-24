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
                return;

            case ManagedWindowLayoutMode.SingleWindow:
                ExitTileModeIfNeeded();
                UpdateSingleManagedWindowLayout(target.Handle, activate);
                return;

            default:
                if (ShouldPreserveTileLayoutState())
                {
                    HideManagedWindows();
                    return;
                }

                ExitTileModeIfNeeded();
                HideManagedWindows();
                return;
        }
    }

    private bool ShouldPreserveTileLayoutState()
    {
        return _isTileModeActive &&
               _viewModel.SelectedTab?.TileLayout != null &&
               (_viewModel.IsWindowPickerOpen || _viewModel.IsCommandPaletteOpen);
    }

    private ManagedWindowLayoutTarget ResolveManagedWindowLayoutTarget()
    {
        var selectedTab = _viewModel.SelectedTab;
        var tile = selectedTab?.TileLayout;

        if (tile != null &&
            tile.Tabs.Count >= 2 &&
            !_viewModel.IsWindowPickerOpen &&
            !_viewModel.IsCommandPaletteOpen &&
            WindowState != WindowState.Minimized)
        {
            return ManagedWindowLayoutTarget.ForTile(tile);
        }

        if (_viewModel.IsWindowPickerOpen ||
            _viewModel.IsCommandPaletteOpen ||
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
        _activeManagedWindowHandle = IntPtr.Zero;
    }

    private void UpdateSingleManagedWindowLayout(IntPtr targetHandle, bool activate)
    {
        MinimizeManagedWindowsExcept(targetHandle);

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
            TryActivateManagedWindowAtCurrentBounds(targetHandle);
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
    }

    private void TryActivateManagedWindowAtCurrentBounds(IntPtr targetHandle)
    {
        if (!NativeMethods.GetWindowRect(targetHandle, out var currentRect))
            return;

        ActivateManagedWindow(
            targetHandle,
            currentRect.Left,
            currentRect.Top,
            Math.Max(1, currentRect.Width),
            Math.Max(1, currentRect.Height),
            bringToFront: false);
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
                bringToFront);
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
                control.Width = Math.Max(1, fraction.Width * containerWidth);
                control.Height = Math.Max(1, fraction.Height * containerHeight);
                control.Margin = new Thickness(
                    fraction.Left * containerWidth,
                    fraction.Top * containerHeight,
                    0,
                    0);
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
        ContentTabContainer.Width = Math.Max(1, fraction.Width * containerWidth);
        ContentTabContainer.Height = Math.Max(1, fraction.Height * containerHeight);
        ContentTabContainer.Margin = new Thickness(
            fraction.Left * containerWidth,
            fraction.Top * containerHeight,
            0,
            0);
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
                int left = totalBounds.Left + (int)Math.Round(fraction.Left * totalBounds.Width);
                int top = totalBounds.Top + (int)Math.Round(fraction.Top * totalBounds.Height);
                int width = Math.Max(1, (int)Math.Round(fraction.Width * totalBounds.Width));
                int height = Math.Max(1, (int)Math.Round(fraction.Height * totalBounds.Height));

                _windowManager.ActivateManagedWindow(
                    handle,
                    left,
                    top,
                    width,
                    height,
                    bringSelectedWindowToFront && selectedTab == tab && handle == primaryHandle);
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

                var f = fractions[i];
                int left   = totalBounds.Left + (int)Math.Round(f.Left   * totalBounds.Width);
                int top    = totalBounds.Top  + (int)Math.Round(f.Top    * totalBounds.Height);
                int width  = Math.Max(1, (int)Math.Round(f.Width  * totalBounds.Width));
                int height = Math.Max(1, (int)Math.Round(f.Height * totalBounds.Height));
                _windowManager.MoveManagedWindowAsync(tileHandle, left, top, width, height);
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
}
