using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace WindowzTabManager;

public partial class MainWindow
{
    private delegate void ManagedWinEventDelegate(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(
        uint eventMin,
        uint eventMax,
        IntPtr hmodWinEventProc,
        ManagedWinEventDelegate lpfnWinEventProc,
        uint idProcess,
        uint idThread,
        uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    private const uint EVENT_SYSTEM_MOVESIZESTART_M = 0x000A;
    private const uint EVENT_SYSTEM_MOVESIZEEND_M = 0x000B;
    private const uint EVENT_SYSTEM_MINIMIZESTART_M = 0x0016;
    private const uint EVENT_SYSTEM_FOREGROUND_M = 0x0003;
    private const uint EVENT_OBJECT_LOCATIONCHANGE_M = 0x800B;
    private const uint WINEVENT_OUTOFCONTEXT_M = 0x0000;
    private const int OBJID_WINDOW_M = 0;
    private const int ManagedWindowEventIgnoreDurationMs = 120;

    private IntPtr _managedWinEventHookMoveSize;
    private IntPtr _managedWinEventHookMinimize;
    private IntPtr _managedWinEventHookLocation;
    private IntPtr _managedWinEventHookForeground;
    private ManagedWinEventDelegate? _managedWinEventProc;
    private IntPtr _managedSyncWindowHandle;
    private bool _managedWindowMoveOrSizeInProgress;
    private bool _isSyncingManagedWindowFromWind;
    private bool _isSyncingWindFromManagedWindow;
    private long _ignoreManagedWindowEventsUntilTick;

    // タイル表示中: 非プライマリウィンドウのプロセスごとに追加したフックの一覧
    private readonly List<IntPtr> _tileExtraHooks = new();
    private readonly Dictionary<IntPtr, int> _tileWindowFractionIndex = new();

    private void EnsureManagedWindowSyncHooks(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
            return;

        if (_managedSyncWindowHandle == handle &&
            (_managedWinEventHookMoveSize != IntPtr.Zero ||
             _managedWinEventHookMinimize != IntPtr.Zero ||
             _managedWinEventHookLocation != IntPtr.Zero ||
             _managedWinEventHookForeground != IntPtr.Zero))
        {
            return;
        }

        RemoveManagedWindowSyncHooks();

        NativeMethods.GetWindowThreadProcessId(handle, out uint processId);
        if (processId == 0)
            return;

        _managedSyncWindowHandle = handle;
        _managedWinEventProc = ManagedWindowWinEventCallback;

        _managedWinEventHookMoveSize = SetWinEventHook(
            EVENT_SYSTEM_MOVESIZESTART_M,
            EVENT_SYSTEM_MOVESIZEEND_M,
            IntPtr.Zero,
            _managedWinEventProc,
            processId,
            0,
            WINEVENT_OUTOFCONTEXT_M);

        _managedWinEventHookMinimize = SetWinEventHook(
            EVENT_SYSTEM_MINIMIZESTART_M,
            EVENT_SYSTEM_MINIMIZESTART_M,
            IntPtr.Zero,
            _managedWinEventProc,
            processId,
            0,
            WINEVENT_OUTOFCONTEXT_M);

        _managedWinEventHookLocation = SetWinEventHook(
            EVENT_OBJECT_LOCATIONCHANGE_M,
            EVENT_OBJECT_LOCATIONCHANGE_M,
            IntPtr.Zero,
            _managedWinEventProc,
            processId,
            0,
            WINEVENT_OUTOFCONTEXT_M);

        // Foreground change must be global to catch transitions from other processes.
        _managedWinEventHookForeground = SetWinEventHook(
            EVENT_SYSTEM_FOREGROUND_M,
            EVENT_SYSTEM_FOREGROUND_M,
            IntPtr.Zero,
            _managedWinEventProc,
            0,
            0,
            WINEVENT_OUTOFCONTEXT_M);
    }

    private void RemoveManagedWindowSyncHooks()
    {
        if (_managedWinEventHookMoveSize != IntPtr.Zero)
        {
            UnhookWinEvent(_managedWinEventHookMoveSize);
            _managedWinEventHookMoveSize = IntPtr.Zero;
        }

        if (_managedWinEventHookMinimize != IntPtr.Zero)
        {
            UnhookWinEvent(_managedWinEventHookMinimize);
            _managedWinEventHookMinimize = IntPtr.Zero;
        }

        if (_managedWinEventHookLocation != IntPtr.Zero)
        {
            UnhookWinEvent(_managedWinEventHookLocation);
            _managedWinEventHookLocation = IntPtr.Zero;
        }

        if (_managedWinEventHookForeground != IntPtr.Zero)
        {
            UnhookWinEvent(_managedWinEventHookForeground);
            _managedWinEventHookForeground = IntPtr.Zero;
        }

        // タイル追加フックも解除してから null 化する
        RemoveTileExtraHooks();

        _managedWinEventProc = null;
        _managedSyncWindowHandle = IntPtr.Zero;
        _managedWindowMoveOrSizeInProgress = false;
    }

    private void SetupTileExtraHooks(IReadOnlyList<(IntPtr Handle, int FractionIndex)> tileWindows)
    {
        RemoveTileExtraHooks();
        foreach (var (h, idx) in tileWindows)
            _tileWindowFractionIndex[h] = idx;

        if (_managedWinEventProc == null) return;

        // プライマリウィンドウのプロセスは EnsureManagedWindowSyncHooks で既にカバー済み。
        // 異なるプロセスのウィンドウに対して MOVESIZESTART/END・LOCATIONCHANGE・MINIMIZESTART の
        // プロセス別フックを追加する。
        NativeMethods.GetWindowThreadProcessId(_managedSyncWindowHandle, out uint primaryPid);
        var hookedPids = new HashSet<uint> { primaryPid };

        foreach (var (handle, _) in tileWindows)
        {
            if (handle == _managedSyncWindowHandle) continue;

            NativeMethods.GetWindowThreadProcessId(handle, out uint pid);
            if (pid == 0 || !hookedPids.Add(pid)) continue;

            void AddHook(uint eMin, uint eMax)
            {
                var h = SetWinEventHook(eMin, eMax, IntPtr.Zero, _managedWinEventProc, pid, 0, WINEVENT_OUTOFCONTEXT_M);
                if (h != IntPtr.Zero) _tileExtraHooks.Add(h);
            }

            AddHook(EVENT_SYSTEM_MOVESIZESTART_M, EVENT_SYSTEM_MOVESIZEEND_M);
            AddHook(EVENT_SYSTEM_MINIMIZESTART_M, EVENT_SYSTEM_MINIMIZESTART_M);
            AddHook(EVENT_OBJECT_LOCATIONCHANGE_M, EVENT_OBJECT_LOCATIONCHANGE_M);
        }
    }

    private void RemoveTileExtraHooks()
    {
        foreach (var h in _tileExtraHooks)
            if (h != IntPtr.Zero) UnhookWinEvent(h);
        _tileExtraHooks.Clear();
        _tileWindowFractionIndex.Clear();
    }

    private void ManagedWindowWinEventCallback(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime)
    {
        bool isPrimary = _managedSyncWindowHandle != IntPtr.Zero && hwnd == _managedSyncWindowHandle;

        if (isPrimary)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Send, () => OnManagedWindowEvent(eventType, hwnd, idObject));
            return;
        }

        // 追加フック経由: プライマリ以外のタイルウィンドウのイベントを処理する
        if (_tileWindowFractionIndex.ContainsKey(hwnd))
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Send, () => OnTileWindowEvent(eventType, hwnd, idObject));
        }
    }

    private void OnTileWindowEvent(uint eventType, IntPtr hwnd, int idObject)
    {
        if (!_tileWindowFractionIndex.ContainsKey(hwnd)) return;

        switch (eventType)
        {
            case EVENT_SYSTEM_MOVESIZESTART_M:
                if (_isSyncingManagedWindowFromWind ||
                    Environment.TickCount64 <= _ignoreManagedWindowEventsUntilTick)
                    return;
                _managedWindowMoveOrSizeInProgress = true;
                return;

            case EVENT_SYSTEM_MOVESIZEEND_M:
                if (_isSyncingManagedWindowFromWind ||
                    Environment.TickCount64 <= _ignoreManagedWindowEventsUntilTick)
                    return;
                _managedWindowMoveOrSizeInProgress = false;
                SyncWindFromTileWindow(hwnd);
                return;

            case EVENT_SYSTEM_MINIMIZESTART_M:
                if (_isSyncingManagedWindowFromWind) return;
                if (WindowState != WindowState.Minimized)
                {
                    _isSyncingWindFromManagedWindow = true;
                    try { WindowState = WindowState.Minimized; }
                    finally { _isSyncingWindFromManagedWindow = false; }
                }
                return;

            case EVENT_OBJECT_LOCATIONCHANGE_M:
                if (_isSyncingManagedWindowFromWind ||
                    Environment.TickCount64 <= _ignoreManagedWindowEventsUntilTick)
                    return;
                if (idObject != OBJID_WINDOW_M) return;
                if (NativeMethods.IsZoomed(hwnd))
                {
                    HandleManagedWindowMaximize(hwnd);
                    return;
                }
                if (!_managedWindowMoveOrSizeInProgress && !NativeMethods.IsIconic(hwnd))
                    SyncWindFromTileWindow(hwnd);
                return;
        }
    }

    private void OnManagedWindowEvent(uint eventType, IntPtr hwnd, int idObject)
    {
        if (_managedSyncWindowHandle == IntPtr.Zero || hwnd != _managedSyncWindowHandle)
            return;

        switch (eventType)
        {
            case EVENT_SYSTEM_FOREGROUND_M:
                EnsureWindBehindManagedWindow(hwnd);
                return;

            case EVENT_SYSTEM_MOVESIZESTART_M:
                if (_isSyncingManagedWindowFromWind ||
                    Environment.TickCount64 <= _ignoreManagedWindowEventsUntilTick)
                {
                    return;
                }

                _managedWindowMoveOrSizeInProgress = true;
                return;

            case EVENT_SYSTEM_MOVESIZEEND_M:
                if (_isSyncingManagedWindowFromWind ||
                    Environment.TickCount64 <= _ignoreManagedWindowEventsUntilTick)
                {
                    return;
                }

                _managedWindowMoveOrSizeInProgress = false;
                SyncWindFromManagedWindow();
                return;

            case EVENT_SYSTEM_MINIMIZESTART_M:
                if (_isSyncingManagedWindowFromWind)
                {
                    return;
                }

                // Mirror managed app minimize intent onto Windowz minimize.
                if (WindowState != WindowState.Minimized)
                {
                    _isSyncingWindFromManagedWindow = true;
                    try
                    {
                        WindowState = WindowState.Minimized;
                    }
                    finally
                    {
                        _isSyncingWindFromManagedWindow = false;
                    }
                }
                return;

            case EVENT_OBJECT_LOCATIONCHANGE_M:
                if (_isSyncingManagedWindowFromWind ||
                    Environment.TickCount64 <= _ignoreManagedWindowEventsUntilTick)
                {
                    return;
                }

                if (idObject != OBJID_WINDOW_M)
                    return;

                if (NativeMethods.IsZoomed(hwnd))
                {
                    HandleManagedWindowMaximize(hwnd);
                    return;
                }

                if (_managedWindowMoveOrSizeInProgress || !NativeMethods.IsIconic(_managedSyncWindowHandle))
                    SyncWindFromManagedWindow();
                return;
        }
    }

    private void EnsureWindBehindManagedWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
            return;

        if (WindowState == WindowState.Minimized ||
            _viewModel.IsWindowPickerOpen ||
            _viewModel.IsCommandPaletteOpen)
        {
            return;
        }

        var windowzHwnd = new WindowInteropHelper(this).Handle;
        if (windowzHwnd == IntPtr.Zero || windowzHwnd == hwnd || !NativeMethods.IsWindow(windowzHwnd))
            return;

        // タイル表示中は全タイルウィンドウの後ろに Windowz を配置する
        // （IsContentTabActive / IsWebTabActive のガードより前に処理する）
        var tile = _viewModel.SelectedTab?.TileLayout;
        if (tile != null)
        {
            var tileHandles = tile.Tabs
                .Select(t => { _tabManager.TryGetExternallyManagedWindowHandle(t, out var h); return h; })
                .Where(h => h != IntPtr.Zero && h != windowzHwnd && NativeMethods.IsWindow(h))
                .ToList();

            if (tileHandles.Count > 0)
            {
                // 全タイルウィンドウを順に HWND_TOP へ押し上げて Z 順を確定させる
                foreach (var h in tileHandles)
                {
                    NativeMethods.SetWindowPos(
                        h, NativeMethods.HWND_TOP, 0, 0, 0, 0,
                        NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE |
                        NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOREDRAW);
                }
                // tileHandles[0] が Z 順で最下位になるため、その下に Windowz を配置する
                NativeMethods.SetWindowPos(
                    windowzHwnd,
                    tileHandles[0],
                    0, 0, 0, 0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
                UpdateBackdropPosition();
                return;
            }
        }

        // 非タイル: コンテンツ・Web タブが前面にある場合はスキップ
        if (_viewModel.IsContentTabActive || _viewModel.IsWebTabActive)
            return;

        NativeMethods.SetWindowPos(
            windowzHwnd,
            hwnd,
            0,
            0,
            0,
            0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);

        UpdateBackdropPosition();
    }

    private void HandleManagedWindowMaximize(IntPtr hwnd)
    {
        // External managed windows must not remain maximized.
        // Immediately cancel their maximize, then toggle Windowz maximize state.
        RestoreManagedWindowSilently(hwnd, ManagedWindowEventIgnoreDurationMs * 3);

        _isSyncingWindFromManagedWindow = true;
        try
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }
        finally
        {
            _isSyncingWindFromManagedWindow = false;
        }

        // Re-apply once after maximize layout settles and once at idle to beat late Z-order updates.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            UpdateManagedWindowLayout(activate: false);
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => UpdateManagedWindowLayout(activate: false));
        });
    }

    private void RestoreManagedWindowSilently(IntPtr hwnd, int ignoreDurationMs = ManagedWindowEventIgnoreDurationMs)
    {
        if (hwnd == IntPtr.Zero)
            return;

        _ignoreManagedWindowEventsUntilTick = Environment.TickCount64 + Math.Max(ignoreDurationMs, ManagedWindowEventIgnoreDurationMs);
        _isSyncingManagedWindowFromWind = true;
        try
        {
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
        }
        finally
        {
            _isSyncingManagedWindowFromWind = false;
        }
    }

    private void SyncWindFromManagedWindow()
    {
        if (_managedSyncWindowHandle == IntPtr.Zero)
            return;

        if (_viewModel.IsWindowPickerOpen ||
            _viewModel.IsCommandPaletteOpen ||
            _viewModel.IsContentTabActive ||
            _viewModel.IsWebTabActive)
        {
            return;
        }

        // タイル表示中: ドラッグ操作中はスキップ（MOVESIZEEND 後に同期する）
        var tile = _viewModel.SelectedTab?.TileLayout;
        if (tile != null && _managedWindowMoveOrSizeInProgress)
            return;

        if (!NativeMethods.IsWindow(_managedSyncWindowHandle))
        {
            RemoveManagedWindowSyncHooks();
            return;
        }

        bool isMinimized = NativeMethods.IsIconic(_managedSyncWindowHandle);
        bool isMaximized = NativeMethods.IsZoomed(_managedSyncWindowHandle);

        if (isMaximized)
        {
            HandleManagedWindowMaximize(_managedSyncWindowHandle);
            return;
        }

        _isSyncingWindFromManagedWindow = true;
        bool movedWindowz = false;
        bool resizedWindowz = false;
        try
        {
            if (isMinimized)
            {
                if (WindowState != WindowState.Minimized)
                    WindowState = WindowState.Minimized;
                return;
            }

            if (WindowState == WindowState.Maximized)
                WindowState = WindowState.Normal;

            if (!NativeMethods.GetWindowRect(_managedSyncWindowHandle, out var managedRect))
                return;

            if (!TryGetManagedWindowOffsets(
                    out double dpiScaleX,
                    out double dpiScaleY,
                    out double offsetXPx,
                    out double offsetYPx,
                    out double frameExtraWidthDip,
                    out double frameExtraHeightDip))
            {
                return;
            }

            double nextLeft = managedRect.Left - offsetXPx;
            double nextTop  = managedRect.Top  - offsetYPx;

            const double epsilon = 0.5;

            if (tile != null &&
                _tileWindowFractionIndex.TryGetValue(_managedSyncWindowHandle, out int primaryFracIdx))
            {
                // タイル表示中: 監視ウィンドウの割合から Windowz の位置・サイズを逆算
                var fractions = tile.GetLayoutFractions();
                if (primaryFracIdx < fractions.Length)
                {
                    var f = fractions[primaryFracIdx];
                    nextLeft -= f.Left * WindowHostContainer.ActualWidth  * dpiScaleX;
                    nextTop  -= f.Top  * WindowHostContainer.ActualHeight * dpiScaleY;
                    if (Math.Abs(Left - nextLeft) > epsilon) { Left = nextLeft; movedWindowz = true; }
                    if (Math.Abs(Top  - nextTop)  > epsilon) { Top  = nextTop;  movedWindowz = true; }

                    if (f.Width > 0)
                    {
                        double nextW = Math.Max(MinWidth, managedRect.Width / f.Width / dpiScaleX + frameExtraWidthDip);
                        if (Math.Abs(Width - nextW) > epsilon) { Width = nextW; movedWindowz = true; resizedWindowz = true; }
                    }
                    if (f.Height > 0)
                    {
                        double nextH = Math.Max(MinHeight, managedRect.Height / f.Height / dpiScaleY + frameExtraHeightDip);
                        if (Math.Abs(Height - nextH) > epsilon) { Height = nextH; movedWindowz = true; resizedWindowz = true; }
                    }
                }
            }
            else
            {
                if (Math.Abs(Left - nextLeft) > epsilon) { Left = nextLeft; movedWindowz = true; }
                if (Math.Abs(Top  - nextTop)  > epsilon) { Top  = nextTop;  movedWindowz = true; }

                double nextContainerWidthDip  = managedRect.Width  / dpiScaleX;
                double nextContainerHeightDip = managedRect.Height / dpiScaleY;
                double nextWidth  = Math.Max(MinWidth,  nextContainerWidthDip  + frameExtraWidthDip);
                double nextHeight = Math.Max(MinHeight, nextContainerHeightDip + frameExtraHeightDip);
                if (Math.Abs(Width  - nextWidth)  > epsilon) Width  = nextWidth;
                if (Math.Abs(Height - nextHeight) > epsilon) Height = nextHeight;
            }
        }
        finally
        {
            _isSyncingWindFromManagedWindow = false;
        }

        // タイル表示中: Windowz が移動/リサイズした場合、他のタイルウィンドウを再配置する
        // サイズ変更時は positionOnlyUpdate: false にして Web タブも再計算させる
        if (tile != null && movedWindowz)
        {
            UpdateManagedWindowLayout(activate: false, positionOnlyUpdate: !resizedWindowz);
        }
    }

    // グローバルフック経由で捕捉した非プライマリタイルウィンドウのドラッグ完了時に呼ぶ
    private void SyncWindFromTileWindow(IntPtr hwnd)
    {
        if (!_tileWindowFractionIndex.TryGetValue(hwnd, out int fractionIdx)) return;

        var tile = _viewModel.SelectedTab?.TileLayout;
        if (tile == null) return;

        if (_viewModel.IsWindowPickerOpen || _viewModel.IsCommandPaletteOpen ||
            _viewModel.IsContentTabActive || _viewModel.IsWebTabActive)
            return;

        if (!NativeMethods.IsWindow(hwnd)) return;
        if (!NativeMethods.GetWindowRect(hwnd, out var rect)) return;

        var fractions = tile.GetLayoutFractions();
        if (fractionIdx >= fractions.Length) return;

        if (!TryGetManagedWindowOffsets(
                out double dpiScaleX, out double dpiScaleY,
                out double offsetXPx, out double offsetYPx,
                out double frameExtraWidthDip, out double frameExtraHeightDip))
            return;

        var f = fractions[fractionIdx];
        double nextLeft = rect.Left - offsetXPx - f.Left * WindowHostContainer.ActualWidth  * dpiScaleX;
        double nextTop  = rect.Top  - offsetYPx - f.Top  * WindowHostContainer.ActualHeight * dpiScaleY;

        const double epsilon = 0.5;
        bool moved = false;
        bool resized = false;
        _isSyncingWindFromManagedWindow = true;
        try
        {
            if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal;
            if (Math.Abs(Left - nextLeft) > epsilon) { Left = nextLeft; moved = true; }
            if (Math.Abs(Top  - nextTop)  > epsilon) { Top  = nextTop;  moved = true; }

            // フラクションから Windowz サイズを逆算
            if (f.Width > 0)
            {
                double nextW = Math.Max(MinWidth, rect.Width / f.Width / dpiScaleX + frameExtraWidthDip);
                if (Math.Abs(Width - nextW) > epsilon) { Width = nextW; moved = true; resized = true; }
            }
            if (f.Height > 0)
            {
                double nextH = Math.Max(MinHeight, rect.Height / f.Height / dpiScaleY + frameExtraHeightDip);
                if (Math.Abs(Height - nextH) > epsilon) { Height = nextH; moved = true; resized = true; }
            }
        }
        finally
        {
            _isSyncingWindFromManagedWindow = false;
        }

        // サイズ変更時は positionOnlyUpdate: false にして Web タブも再計算させる
        if (moved)
            UpdateManagedWindowLayout(activate: false, positionOnlyUpdate: !resized);
    }

    private bool TryGetManagedWindowOffsets(
        out double dpiScaleX,
        out double dpiScaleY,
        out double offsetXPx,
        out double offsetYPx,
        out double frameExtraWidthDip,
        out double frameExtraHeightDip)
    {
        dpiScaleX = 1.0;
        dpiScaleY = 1.0;
        offsetXPx = 0;
        offsetYPx = 0;
        frameExtraWidthDip = 0;
        frameExtraHeightDip = 0;

        double containerWidthDip = WindowHostContainer.ActualWidth;
        double containerHeightDip = WindowHostContainer.ActualHeight;
        if (containerWidthDip <= 0 || containerHeightDip <= 0)
            return false;

        var source = PresentationSource.FromVisual(this);
        dpiScaleX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        dpiScaleY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

        var containerOffsetDip = WindowHostContainer.TranslatePoint(new Point(0, 0), this);
        offsetXPx = containerOffsetDip.X * dpiScaleX;
        offsetYPx = containerOffsetDip.Y * dpiScaleY;

        frameExtraWidthDip = ActualWidth - containerWidthDip;
        frameExtraHeightDip = ActualHeight - containerHeightDip;
        return true;
    }
}
