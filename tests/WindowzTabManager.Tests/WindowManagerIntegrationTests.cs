using System.Runtime.InteropServices;
using WindowzTabManager.Models;
using WindowzTabManager.Services;

namespace WindowzTabManager.Tests;

/// <summary>
/// 実 Win32 ウィンドウ (CreateWindowEx) を使用した
/// WindowManager / TabManager のインテグレーションテスト。
///
/// テスト対象:
///   - WindowManager によるウィンドウ位置制御 (管理・移動・最小化・解放)
///   - TabManager への実 HWND 登録・解放のライフサイクル
///   - WinEventHook によるフォアグラウンド通知の到着確認
/// </summary>
internal static class WindowManagerIntegrationTests
{
    // ═══════════════════════════════════════════
    // P/Invoke (テスト専用)
    // ═══════════════════════════════════════════

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT pvAttr, int cbAttr);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool PeekMessage(out MSG msg, IntPtr hWnd, uint min, uint max, uint remove);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG msg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG msg);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(
        uint eventMin, uint eventMax,
        IntPtr hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc,
        uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
        public int Width  => Right  - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam, lParam;
        public uint time;
        public int x, y;
    }

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    private delegate void   WinEventDelegate(IntPtr hook, uint evt, IntPtr hwnd, int obj, int child, uint thread, uint time);

    private const uint WS_POPUP    = 0x80000000;
    private const uint WS_VISIBLE  = 0x10000000;
    private const int  SW_SHOW          = 5;
    private const int  SW_SHOWNOACTIVATE = 4;
    private const int  SW_MINIMIZE      = 6;
    private const int  DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    private const uint PM_REMOVE        = 1;
    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint WINEVENT_OUTOFCONTEXT   = 0x0000;

    private const string TestClassName = "WindowzIntegrationTestClass";

    // WndProc デリゲートは GC されないように static で保持する
    private static readonly WndProcDelegate _wndProc =
        (hWnd, msg, wParam, lParam) => DefWindowProc(hWnd, msg, wParam, lParam);

    private static bool _classRegistered;
    private static readonly object _classLock = new();

    private static void EnsureWindowClassRegistered()
    {
        lock (_classLock)
        {
            if (_classRegistered) return;
            var wc = new WNDCLASSEX
            {
                cbSize        = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc   = Marshal.GetFunctionPointerForDelegate(_wndProc),
                hInstance     = GetModuleHandle(null),
                lpszClassName = TestClassName,
            };
            _classRegistered = RegisterClassEx(ref wc) != 0;
            if (!_classRegistered)
                throw new InvalidOperationException($"RegisterClassEx failed: {Marshal.GetLastWin32Error()}");
        }
    }

    // ═══════════════════════════════════════════
    // WindowManager 単体テスト (実 HWND)
    // ═══════════════════════════════════════════

    internal static void TryManageWindow_ValidHwnd_ReturnsTrue()
    {
        using var scope = new TempSettingsScope();
        var mgr = new WindowManager(scope.Manager);
        using var win = new TestWindowScope();

        bool result = mgr.TryManageWindow(win.Handle);

        Assert(result, "有効な HWND に対して TryManageWindow は true を返すべき。");
        Assert(mgr.IsManaged(win.Handle), "TryManageWindow 成功後は IsManaged が true であるべき。");
    }

    internal static void TryManageWindow_ZeroHandle_ReturnsFalse()
    {
        using var scope = new TempSettingsScope();
        var mgr = new WindowManager(scope.Manager);

        bool result = mgr.TryManageWindow(IntPtr.Zero);

        Assert(!result, "ゼロハンドルに対して TryManageWindow は false を返すべき。");
    }

    internal static void TryManageWindow_AlreadyManagedHandle_ReturnsFalse()
    {
        using var scope = new TempSettingsScope();
        var mgr = new WindowManager(scope.Manager);
        using var win = new TestWindowScope();

        mgr.TryManageWindow(win.Handle);
        bool second = mgr.TryManageWindow(win.Handle);

        Assert(!second, "既に管理中の HWND に対して TryManageWindow は false を返すべき。");
    }

    internal static void ForgetManagedWindow_RemovesFromTracking()
    {
        using var scope = new TempSettingsScope();
        var mgr = new WindowManager(scope.Manager);
        using var win = new TestWindowScope();

        mgr.TryManageWindow(win.Handle);
        mgr.ForgetManagedWindow(win.Handle);

        Assert(!mgr.IsManaged(win.Handle), "ForgetManagedWindow 後は IsManaged が false であるべき。");
    }

    internal static void ActivateManagedWindow_SetsWindowToRequestedVisibleRect()
    {
        using var scope = new TempSettingsScope();
        var mgr = new WindowManager(scope.Manager);
        using var win = new TestWindowScope();

        mgr.TryManageWindow(win.Handle);
        // 要求: visible rect が (100, 120, 400×300) になること
        mgr.ActivateManagedWindow(win.Handle, x: 100, y: 120, width: 400, height: 300, bringToFront: false);

        // DWM extended frame bounds = 実際に見えている領域
        int hr = DwmGetWindowAttribute(win.Handle, DWMWA_EXTENDED_FRAME_BOUNDS, out var vis, Marshal.SizeOf<RECT>());
        if (hr == 0 && vis.Width > 0)
        {
            AssertNear(vis.Left,  100, "Left",   tolerance: 2);
            AssertNear(vis.Top,   120, "Top",    tolerance: 2);
            AssertNear(vis.Width, 400, "Width",  tolerance: 2);
            AssertNear(vis.Height,300, "Height", tolerance: 2);
        }
        else
        {
            // DWM が使えない環境は GetWindowRect で代用 (WS_POPUP はインセット≒0)
            GetWindowRect(win.Handle, out var wr);
            AssertNear(wr.Left,  100, "Left",   tolerance: 2);
            AssertNear(wr.Top,   120, "Top",    tolerance: 2);
            AssertNear(wr.Width, 400, "Width",  tolerance: 2);
            AssertNear(wr.Height,300, "Height", tolerance: 2);
        }
    }

    internal static void ActivateManagedWindow_ShowsMinimizedWindow()
    {
        using var scope = new TempSettingsScope();
        var mgr = new WindowManager(scope.Manager);
        using var win = new TestWindowScope();

        mgr.TryManageWindow(win.Handle);
        ShowWindow(win.Handle, SW_MINIMIZE);
        Assert(IsIconic(win.Handle), "前提: ウィンドウが最小化されているべき。");

        mgr.ActivateManagedWindow(win.Handle, x: 100, y: 100, width: 200, height: 150, bringToFront: false);

        Assert(!IsIconic(win.Handle), "ActivateManagedWindow は最小化ウィンドウを復元するべき。");
    }

    internal static void ReleaseManagedWindow_RestoresOriginalPosition()
    {
        using var scope = new TempSettingsScope();
        var mgr = new WindowManager(scope.Manager);
        using var win = new TestWindowScope(x: 50, y: 60, width: 240, height: 160);

        GetWindowRect(win.Handle, out var orig);
        mgr.TryManageWindow(win.Handle);

        // 別の場所へ移動してから解放
        mgr.ActivateManagedWindow(win.Handle, x: 700, y: 700, width: 500, height: 400, bringToFront: false);
        mgr.ReleaseManagedWindow(win.Handle);

        GetWindowRect(win.Handle, out var restored);
        AssertNear(restored.Left,   orig.Left,   "Left",   tolerance: 2);
        AssertNear(restored.Top,    orig.Top,    "Top",    tolerance: 2);
        AssertNear(restored.Width,  orig.Width,  "Width",  tolerance: 2);
        AssertNear(restored.Height, orig.Height, "Height", tolerance: 2);
    }

    internal static void ReleaseManagedWindow_NotManaged_DoesNotMoveWindow()
    {
        using var scope = new TempSettingsScope();
        var mgr = new WindowManager(scope.Manager);
        using var win = new TestWindowScope(x: 50, y: 60, width: 200, height: 150);

        GetWindowRect(win.Handle, out var before);
        mgr.ReleaseManagedWindow(win.Handle); // 管理していない → 何もしない
        GetWindowRect(win.Handle, out var after);

        Assert(before.Left == after.Left && before.Top == after.Top,
            "管理外 HWND に対する ReleaseManagedWindow はウィンドウ位置を変更しないべき。");
    }

    internal static void MinimizeManagedWindow_MakesWindowIconic()
    {
        using var scope = new TempSettingsScope();
        var mgr = new WindowManager(scope.Manager);
        using var win = new TestWindowScope();

        mgr.TryManageWindow(win.Handle);
        mgr.MinimizeManagedWindow(win.Handle);

        Assert(IsIconic(win.Handle), "MinimizeManagedWindow はウィンドウをアイコン化するべき。");
    }

    internal static void MinimizeManagedWindow_NotManaged_DoesNotMinimize()
    {
        using var scope = new TempSettingsScope();
        var mgr = new WindowManager(scope.Manager);
        using var win = new TestWindowScope();

        mgr.MinimizeManagedWindow(win.Handle); // no-op のはず
        Assert(!IsIconic(win.Handle), "管理外 HWND に対する MinimizeManagedWindow はウィンドウを変更しないべき。");
    }

    internal static void MinimizeAllManagedWindowsExcept_OnlyMinimizesOthers()
    {
        using var scope = new TempSettingsScope();
        var mgr = new WindowManager(scope.Manager);
        using var winA = new TestWindowScope();
        using var winB = new TestWindowScope();
        using var winC = new TestWindowScope();

        mgr.TryManageWindow(winA.Handle);
        mgr.TryManageWindow(winB.Handle);
        mgr.TryManageWindow(winC.Handle);

        mgr.MinimizeAllManagedWindowsExcept(winB.Handle);

        Assert( IsIconic(winA.Handle), "A は最小化されるべき。");
        Assert(!IsIconic(winB.Handle), "B (除外指定) は最小化されないべき。");
        Assert( IsIconic(winC.Handle), "C は最小化されるべき。");
    }

    // ═══════════════════════════════════════════
    // TabManager + WindowManager 統合テスト
    // ═══════════════════════════════════════════

    internal static void AddTab_WithRealWindow_RegistersHandleAndSetsActiveTab()
    {
        using var scope = new TempSettingsScope();
        var winMgr = new WindowManager(scope.Manager);
        var tabMgr = new TabManager(winMgr, scope.Manager, null!);
        using var win = new TestWindowScope();
        try
        {
            var tab = tabMgr.AddTab(MakeWindowInfo(win.Handle), activate: true);

            Assert(tab != null, "AddTab は有効な HWND に対して null を返すべきでない。");
            Assert(tabMgr.ActiveTab == tab, "追加直後はそのタブがアクティブであるべき。");
            Assert(
                tabMgr.TryGetExternallyManagedWindowHandle(tab!, out var h) && h == win.Handle,
                "追加した HWND が ExternallyManaged として登録されているべき。");
        }
        finally { tabMgr.StopCleanupTimer(); }
    }

    internal static void AddTab_SameHwndTwice_ReturnsSameTab()
    {
        using var scope = new TempSettingsScope();
        var winMgr = new WindowManager(scope.Manager);
        var tabMgr = new TabManager(winMgr, scope.Manager, null!);
        using var win = new TestWindowScope();
        try
        {
            var tab1 = tabMgr.AddTab(MakeWindowInfo(win.Handle), activate: false);
            var tab2 = tabMgr.AddTab(MakeWindowInfo(win.Handle), activate: false);

            Assert(tab1 != null && tab2 != null, "AddTab は null を返すべきでない。");
            Assert(ReferenceEquals(tab1, tab2), "同じ HWND を 2 回 AddTab すると既存タブが返るべき。");
            Assert(tabMgr.Tabs.Count == 1, "重複タブは追加されないべき。");
        }
        finally { tabMgr.StopCleanupTimer(); }
    }

    internal static void RemoveTab_WithRealWindow_ReleasesWindowToOriginalPosition()
    {
        using var scope = new TempSettingsScope();
        var winMgr = new WindowManager(scope.Manager);
        var tabMgr = new TabManager(winMgr, scope.Manager, null!);
        using var win = new TestWindowScope(x: 40, y: 50, width: 250, height: 180);
        try
        {
            GetWindowRect(win.Handle, out var orig);
            var tab = tabMgr.AddTab(MakeWindowInfo(win.Handle), activate: true)!;

            // アクティブ化で別の位置へ移動
            winMgr.ActivateManagedWindow(win.Handle, x: 800, y: 600, width: 500, height: 400, bringToFront: false);

            tabMgr.RemoveTab(tab);

            GetWindowRect(win.Handle, out var restored);
            AssertNear(restored.Left,   orig.Left,   "Left",   tolerance: 2);
            AssertNear(restored.Top,    orig.Top,    "Top",    tolerance: 2);
            AssertNear(restored.Width,  orig.Width,  "Width",  tolerance: 2);
            AssertNear(restored.Height, orig.Height, "Height", tolerance: 2);
        }
        finally { tabMgr.StopCleanupTimer(); }
    }

    internal static void RemoveTab_WithRealWindow_TabIsNoLongerManaged()
    {
        using var scope = new TempSettingsScope();
        var winMgr = new WindowManager(scope.Manager);
        var tabMgr = new TabManager(winMgr, scope.Manager, null!);
        using var win = new TestWindowScope();
        try
        {
            var tab = tabMgr.AddTab(MakeWindowInfo(win.Handle), activate: false)!;
            tabMgr.RemoveTab(tab);

            Assert(!winMgr.IsManaged(win.Handle),
                "RemoveTab 後は WindowManager での管理が解除されているべき。");
            Assert(!tabMgr.TryGetExternallyManagedWindowHandle(tab, out _),
                "RemoveTab 後は TabManager の ExternallyManaged 登録が解除されているべき。");
        }
        finally { tabMgr.StopCleanupTimer(); }
    }

    /// <summary>
    /// フォアグラウンド変更フック (OnForegroundWindowChanged) が使う
    /// 「HWND → タブ」ルックアップが正しく機能することを確認する。
    /// </summary>
    internal static void HandleToTab_Lookup_ReturnsCorrectTabForEachWindow()
    {
        using var scope = new TempSettingsScope();
        var winMgr = new WindowManager(scope.Manager);
        var tabMgr = new TabManager(winMgr, scope.Manager, null!);
        using var winA = new TestWindowScope();
        using var winB = new TestWindowScope();
        try
        {
            var tabA = tabMgr.AddTab(MakeWindowInfo(winA.Handle), activate: true)!;
            var tabB = tabMgr.AddTab(MakeWindowInfo(winB.Handle), activate: false)!;

            // OnForegroundWindowChanged が行う検索と同等のルックアップ
            var foundForA = tabMgr.Tabs.FirstOrDefault(t =>
                tabMgr.TryGetExternallyManagedWindowHandle(t, out var h) && h == winA.Handle);
            var foundForB = tabMgr.Tabs.FirstOrDefault(t =>
                tabMgr.TryGetExternallyManagedWindowHandle(t, out var h) && h == winB.Handle);

            Assert(foundForA == tabA,
                "HWND-A でルックアップするとタブ A が返るべき。");
            Assert(foundForB == tabB,
                "HWND-B でルックアップするとタブ B が返るべき。");
        }
        finally { tabMgr.StopCleanupTimer(); }
    }

    // ═══════════════════════════════════════════
    // WinEventHook / フォアグラウンド通知テスト
    // ═══════════════════════════════════════════

    /// <summary>
    /// EVENT_SYSTEM_FOREGROUND フックが SetForegroundWindow 後に
    /// 正しい HWND でコールバックされることを確認する。
    /// (MainWindow.ManagedSync.cs の SetupForegroundActivationHook と同じ仕組み)
    /// </summary>
    internal static void ForegroundHook_AfterSetForeground_CallbackReceivesCorrectHandle()
    {
        // anchor (可視) を先にフォアグラウンドに固定する。
        // win は非表示で作成し SW_SHOWNOACTIVATE で表示することで
        // 「anchor → win」の切替を確実に発生させる。
        // (WS_VISIBLE で作成すると OS がフォアグラウンドを奪い、
        //  後続の SetForegroundWindow が変化なしと判断してイベントを発火しない)
        using var anchor = new TestWindowScope(visible: true);
        SetForegroundWindow(anchor.Handle);
        DrainMessages(timeoutMs: 150, until: () => GetForegroundWindow() == anchor.Handle);

        using var win = new TestWindowScope(visible: false);
        ShowWindow(win.Handle, SW_SHOWNOACTIVATE); // 可視にするが前景は奪わない

        IntPtr received = IntPtr.Zero;
        WinEventDelegate hookProc = (_, _, hwnd, obj, _, _, _) =>
        {
            if (obj == 0 /* OBJID_WINDOW */) received = hwnd;
        };

        var hook = SetWinEventHook(
            EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, hookProc,
            0, 0, WINEVENT_OUTOFCONTEXT);
        Assert(hook != IntPtr.Zero, "WinEventHook の登録に失敗。");

        try
        {
            // anchor → win への切替: フォアグラウンド変更が確実に発生する
            SetForegroundWindow(win.Handle);

            // WINEVENT_OUTOFCONTEXT はメッセージポンプ経由で届く
            DrainMessages(timeoutMs: 500, until: () => received == win.Handle);

            Assert(received == win.Handle,
                $"フォアグラウンドフックは SetForegroundWindow した HWND を受け取るべき。" +
                $"期待: 0x{win.Handle:X} / 実際: 0x{received:X}");
        }
        finally
        {
            UnhookWinEvent(hook);
            GC.KeepAlive(hookProc);
        }
    }

    /// <summary>
    /// 2 つの管理ウィンドウを切り替えたとき、フォアグラウンドフックが
    /// それぞれの HWND を順に受け取ることを確認する。
    /// </summary>
    internal static void ForegroundHook_SwitchBetweenTwoWindows_BothHandlesReceived()
    {
        using var winA = new TestWindowScope();
        using var winB = new TestWindowScope();
        ShowWindow(winA.Handle, SW_SHOW);
        ShowWindow(winB.Handle, SW_SHOW);

        var receivedHandles = new List<IntPtr>();
        WinEventDelegate hookProc = (_, _, hwnd, obj, _, _, _) =>
        {
            if (obj == 0) receivedHandles.Add(hwnd);
        };

        var hook = SetWinEventHook(
            EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, hookProc,
            0, 0, WINEVENT_OUTOFCONTEXT);
        Assert(hook != IntPtr.Zero, "WinEventHook の登録に失敗。");

        try
        {
            SetForegroundWindow(winA.Handle);
            DrainMessages(timeoutMs: 300, until: () => receivedHandles.Contains(winA.Handle));

            SetForegroundWindow(winB.Handle);
            DrainMessages(timeoutMs: 300, until: () => receivedHandles.Contains(winB.Handle));

            Assert(receivedHandles.Contains(winA.Handle),
                "ウィンドウ A をフォアグラウンドにしたとき、フックに A の HWND が届くべき。");
            Assert(receivedHandles.Contains(winB.Handle),
                "ウィンドウ B をフォアグラウンドにしたとき、フックに B の HWND が届くべき。");
        }
        finally
        {
            UnhookWinEvent(hook);
            GC.KeepAlive(hookProc);
        }
    }

    // ═══════════════════════════════════════════
    // ヘルパー
    // ═══════════════════════════════════════════

    private static WindowInfo MakeWindowInfo(IntPtr handle) => new()
    {
        Handle      = handle,
        Title       = $"TestWin-{handle:X}",
        ProcessName = "testhost",
        ProcessId   = Environment.ProcessId,
    };

    private static void DrainMessages(int timeoutMs, Func<bool> until)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline && !until())
        {
            if (PeekMessage(out var msg, IntPtr.Zero, 0, 0, PM_REMOVE))
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
            else
            {
                Thread.Sleep(10);
            }
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void AssertNear(int actual, int expected, string label, int tolerance)
    {
        if (Math.Abs(actual - expected) > tolerance)
            throw new InvalidOperationException(
                $"{label}: 期待値 {expected} から {tolerance} px 以内であるべきだが、実際は {actual}。");
    }

    private sealed class TempSettingsScope : IDisposable
    {
        public string DirectoryPath { get; }
        public SettingsManager Manager { get; }

        public TempSettingsScope()
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), "WindowzTabManager.Tests", Guid.NewGuid().ToString("N"));
            Manager = new SettingsManager(DirectoryPath);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, recursive: true); }
            catch { }
        }
    }

    private sealed class TestWindowScope : IDisposable
    {
        public IntPtr Handle { get; }

        /// <param name="visible">
        /// true  (既定): WS_VISIBLE で作成。ウィンドウはフォアグラウンドを取得する場合がある。
        /// false        : 非表示で作成。ShowWindow で後から表示でき、フォアグラウンドを奪わない。
        /// </param>
        public TestWindowScope(int x = 10, int y = 10, int width = 320, int height = 200, bool visible = true)
        {
            EnsureWindowClassRegistered();
            uint style = WS_POPUP;
            if (visible) style |= WS_VISIBLE;
            Handle = CreateWindowEx(
                0, TestClassName, $"WindowzTest-{Guid.NewGuid():N}",
                style,
                x, y, width, height,
                IntPtr.Zero, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);

            if (Handle == IntPtr.Zero)
                throw new InvalidOperationException($"CreateWindowEx 失敗: {Marshal.GetLastWin32Error()}");
        }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero && IsWindow(Handle))
                DestroyWindow(Handle);
        }
    }
}
