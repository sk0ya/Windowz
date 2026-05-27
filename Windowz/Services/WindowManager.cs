using System.Collections.ObjectModel;
using System.Diagnostics;
using WindowzTabManager.Models;

namespace WindowzTabManager.Services;

public class WindowManager
{
    private readonly HashSet<IntPtr> _excludedHandles;
    private readonly Dictionary<IntPtr, ManagedWindowState> _managedWindowStates = new();
    // ドラッグ中の SWP_ASYNCWINDOWPOS 重複ポストを防ぐキャッシュ
    private readonly Dictionary<IntPtr, (int x, int y, int w, int h)> _lastAsyncPos = new();
    // DWM フレームインセットキャッシュ: ウィンドウスタイルが変わらない限り不変なので1回だけ取得する
    private readonly Dictionary<IntPtr, (int Left, int Top, int Right, int Bottom)> _frameInsetsCache = new();
    public string? LastEmbedFailureMessage { get; private set; }

    public ObservableCollection<WindowInfo> AvailableWindows { get; } = new();

    public WindowManager(SettingsManager settingsManager, IEnumerable<IntPtr>? excludedHandles = null)
    {
        _ = settingsManager;
        _excludedHandles = excludedHandles != null
            ? new HashSet<IntPtr>(excludedHandles)
            : new HashSet<IntPtr>();
    }

    public void RefreshWindowList()
    {
        AvailableWindows.Clear();
        var windows = EnumerateWindows();
        foreach (var window in windows)
        {
            if (!_managedWindowStates.ContainsKey(window.Handle))
            {
                AvailableWindows.Add(window);
            }
        }
    }

    public List<WindowInfo> EnumerateWindows()
    {
        var windows = new List<WindowInfo>();
        var currentProcessId = Environment.ProcessId;

        NativeMethods.EnumWindows((hWnd, _) =>
        {
            if (_excludedHandles.Contains(hWnd))
                return true;

            if (_managedWindowStates.ContainsKey(hWnd))
                return true;

            if (!IsValidWindow(hWnd, currentProcessId))
                return true;

            var windowInfo = WindowInfo.FromHandle(hWnd);
            if (windowInfo != null)
            {
                windows.Add(windowInfo);
            }

            return true;
        }, IntPtr.Zero);

        return windows.OrderBy(w => w.ProcessName).ThenBy(w => w.Title).ToList();
    }

    private bool IsValidWindow(IntPtr hWnd, int currentProcessId)
    {
        // Must be visible
        if (!NativeMethods.IsWindowVisible(hWnd)) return false;

        // Get window style
        int style = NativeMethods.GetWindowLong(hWnd, NativeMethods.GWL_STYLE);
        int exStyle = NativeMethods.GetWindowLong(hWnd, NativeMethods.GWL_EXSTYLE);

        // Skip child windows
        if ((style & (int)NativeMethods.WS_CHILD) != 0) return false;

        // Skip tool windows
        if ((exStyle & (int)NativeMethods.WS_EX_TOOLWINDOW) != 0) return false;

        // Must have a title
        string title = NativeMethods.GetWindowTitle(hWnd);
        if (string.IsNullOrWhiteSpace(title)) return false;

        // Skip our own window
        NativeMethods.GetWindowThreadProcessId(hWnd, out uint processId);
        if (processId == currentProcessId) return false;

        // Skip certain system windows
        string className = NativeMethods.GetWindowClassName(hWnd);
        if (IsSystemWindow(className)) return false;

        return true;
    }

    private bool IsSystemWindow(string className)
    {
        return className switch
        {
            "Progman" => true,
            "WorkerW" => true,
            "Shell_TrayWnd" => true,
            "Shell_SecondaryTrayWnd" => true,
            "Windows.UI.Core.CoreWindow" => true,
            "ApplicationFrameWindow" => true, // UWP host window
            _ => false
        };
    }

    public bool TryManageWindow(IntPtr handle)
    {
        LastEmbedFailureMessage = null;

        if (handle == IntPtr.Zero)
        {
            LastEmbedFailureMessage = "Failed to add window: invalid handle";
            return false;
        }

        if (_managedWindowStates.ContainsKey(handle))
        {
            LastEmbedFailureMessage = "Failed to add window: already managed";
            return false;
        }

        if (!IsWindowValid(handle))
        {
            LastEmbedFailureMessage = "Failed to add window: window is no longer valid";
            return false;
        }

        NativeMethods.GetWindowRect(handle, out var rect);

        _managedWindowStates[handle] = new ManagedWindowState
        {
            OriginalRect = rect,
            WasVisible = NativeMethods.IsWindowVisible(handle),
            WasMinimized = NativeMethods.IsIconic(handle),
            WasMaximized = NativeMethods.IsZoomed(handle),
            OriginalExStyle = NativeMethods.GetWindowLong(handle, NativeMethods.GWL_EXSTYLE)
        };

        // ドラッグ中の DWM クエリを避けるため、フレームインセットを先行キャッシュする
        NativeMethods.TryGetWindowFrameInsets(handle, out int iL, out int iT, out int iR, out int iB);
        _frameInsetsCache[handle] = (iL, iT, iR, iB);

        return true;
    }

    public bool IsManaged(IntPtr handle)
    {
        return _managedWindowStates.ContainsKey(handle);
    }

    public void ActivateManagedWindow(
        IntPtr handle,
        int x,
        int y,
        int width,
        int height,
        bool bringToFront,
        IntPtr windWindowHandle = default,
        bool setZOrder = true)
    {
        if (!_managedWindowStates.ContainsKey(handle)) return;
        if (!NativeMethods.IsWindow(handle))
        {
            _managedWindowStates.Remove(handle);
            return;
        }

        width = Math.Max(1, width);
        height = Math.Max(1, height);

        // ウィンドウがすでに表示済みかどうかを確認する。
        // ShowWindowNoAnimation (DwmSetWindowAttribute x2 + ShowWindow) は最小化または
        // 非表示の場合のみ必要。表示済みウィンドウへの SW_SHOWNOACTIVATE は実質 no-op だが、
        // クロスプロセス呼び出しコストが毎回かかるため、スキップする。
        bool alreadyVisible = !NativeMethods.IsIconic(handle) && NativeMethods.IsWindowVisible(handle);

        if (!alreadyVisible)
        {
            // SW_SHOWNOACTIVATE: 最小化ウィンドウも復元するが、フォーカスは奪わない。
            // bringToFront=true の場合は後続の ForceForegroundWindow で明示的にフォーカスを渡す。
            ShowWindowNoAnimation(handle, NativeMethods.SW_SHOWNOACTIVATE);
        }

        ConvertVisibleBoundsToWindowBounds(
            handle,
            x,
            y,
            width,
            height,
            out int windowX,
            out int windowY,
            out int windowWidth,
            out int windowHeight);

        // setZOrder=false のとき (タイル表示) は呼び出し元が RaiseTileWindowsAboveWindowz で
        // Z-order を一括設定するため、ここでは位置・サイズのみ更新する。
        uint flags = NativeMethods.SWP_NOACTIVATE;
        if (!setZOrder)
            flags |= NativeMethods.SWP_NOZORDER;

        if (!bringToFront && alreadyVisible)
        {
            // 位置・サイズ更新のみのパス (リサイズ・LocationChanged 等の高頻度呼び出し):
            // SWP_ASYNCWINDOWPOS でポストして UI スレッドをブロックしない。
            // _lastAsyncPos で重複ポストを抑制し、キュー蓄積を防ぐ。
            if (_lastAsyncPos.TryGetValue(handle, out var last) &&
                last.x == windowX && last.y == windowY && last.w == windowWidth && last.h == windowHeight)
            {
                if (setZOrder)
                    RepairManagedWindowZOrder(handle, windWindowHandle);
                return;
            }

            _lastAsyncPos[handle] = (windowX, windowY, windowWidth, windowHeight);
            NativeMethods.SetWindowPos(
                handle,
                setZOrder ? NativeMethods.HWND_TOP : IntPtr.Zero,
                windowX, windowY, windowWidth, windowHeight,
                flags | NativeMethods.SWP_ASYNCWINDOWPOS);
            return;
        }

        // アクティブ化・復元パス: 同期 SetWindowPos で確定する。
        // 次回の MoveManagedWindowAsync が古い位置をキャッシュヒットしないよう _lastAsyncPos をクリア。
        _lastAsyncPos.Remove(handle);
        flags |= NativeMethods.SWP_SHOWWINDOW;

        bool positioned = NativeMethods.SetWindowPos(
            handle,
            setZOrder ? NativeMethods.HWND_TOP : IntPtr.Zero,
            windowX,
            windowY,
            windowWidth,
            windowHeight,
            flags);

        if (!positioned)
        {
            NativeMethods.MoveWindow(handle, windowX, windowY, windowWidth, windowHeight, true);
        }

        if (bringToFront)
        {
            NativeMethods.ForceForegroundWindow(handle);
        }

        // z-order を直す必要があるのは managed window を前面に出すときだけ。
        // bringToFront=false の位置更新パス (ドラッグ・LocationChanged 等) では
        // SetWindowPos がアクセシビリティイベントを再発火させてループになるため呼ばない。
        if (bringToFront && windWindowHandle != default && NativeMethods.IsWindow(windWindowHandle) &&
            NativeMethods.GetWindow(handle, NativeMethods.GW_HWNDNEXT) != windWindowHandle)
        {
            PlaceWindowzBehindManagedWindow(handle, windWindowHandle);
        }
    }

    private static void RepairManagedWindowZOrder(IntPtr handle, IntPtr windWindowHandle)
    {
        if (windWindowHandle != default &&
            NativeMethods.IsWindow(windWindowHandle) &&
            NativeMethods.GetWindow(handle, NativeMethods.GW_HWNDNEXT) == windWindowHandle)
            return;

        NativeMethods.SetWindowPos(
            handle,
            NativeMethods.HWND_TOP,
            0,
            0,
            0,
            0,
            NativeMethods.SWP_NOMOVE |
            NativeMethods.SWP_NOSIZE |
            NativeMethods.SWP_NOACTIVATE);

        if (windWindowHandle != default &&
            NativeMethods.IsWindow(windWindowHandle) &&
            NativeMethods.GetWindow(handle, NativeMethods.GW_HWNDNEXT) != windWindowHandle)
        {
            PlaceWindowzBehindManagedWindow(handle, windWindowHandle);
        }
    }

    private static void PlaceWindowzBehindManagedWindow(IntPtr handle, IntPtr windWindowHandle)
    {
        NativeMethods.SetWindowPos(
            windWindowHandle,
            handle,
            0,
            0,
            0,
            0,
            NativeMethods.SWP_NOMOVE |
            NativeMethods.SWP_NOSIZE |
            NativeMethods.SWP_NOACTIVATE);
    }

    /// <summary>
    /// ドラッグ中の軽量追従用。SWP_ASYNCWINDOWPOS でポストするため UIスレッドをブロックしない。
    /// ShowWindow・bringToFront は行わず位置・サイズのみ更新する。
    /// </summary>
    public void MoveManagedWindowAsync(IntPtr handle, int x, int y, int width, int height)
    {
        if (!_managedWindowStates.ContainsKey(handle)) return;
        if (!NativeMethods.IsWindow(handle)) return;

        ConvertVisibleBoundsToWindowBounds(
            handle,
            x,
            y,
            width,
            height,
            out int windowX,
            out int windowY,
            out int windowWidth,
            out int windowHeight);

        // 直前と同一座標への重複ポストをスキップしてキュー蓄積を防ぐ（RDP 環境で効果的）
        if (_lastAsyncPos.TryGetValue(handle, out var last) &&
            last.x == windowX && last.y == windowY && last.w == windowWidth && last.h == windowHeight)
            return;

        _lastAsyncPos[handle] = (windowX, windowY, windowWidth, windowHeight);

        NativeMethods.SetWindowPos(
            handle,
            IntPtr.Zero,
            windowX,
            windowY,
            windowWidth,
            windowHeight,
            NativeMethods.SWP_NOZORDER |
            NativeMethods.SWP_NOACTIVATE |
            NativeMethods.SWP_ASYNCWINDOWPOS);
    }

    public void MinimizeManagedWindow(IntPtr handle)
    {
        if (!_managedWindowStates.ContainsKey(handle)) return;
        if (!NativeMethods.IsWindow(handle))
        {
            _managedWindowStates.Remove(handle);
            return;
        }

        // 非アクティブタブのウィンドウを最小化する。アニメーションなしで実行。
        if (!NativeMethods.IsIconic(handle))
            ShowWindowNoAnimation(handle, NativeMethods.SW_MINIMIZE);
    }

    public void MinimizeAllManagedWindowsExcept(IntPtr handleToKeep)
    {
        foreach (var handle in _managedWindowStates.Keys.ToList())
        {
            if (handle == handleToKeep) continue;
            MinimizeManagedWindow(handle);
        }
    }

    /// <summary>指定ハンドルセット以外の管理ウィンドウをすべて最小化する（タイル表示用）</summary>
    public void MinimizeAllManagedWindowsExcept(IReadOnlySet<IntPtr> handlesToKeep)
    {
        foreach (var handle in _managedWindowStates.Keys.ToList())
        {
            if (handlesToKeep.Contains(handle)) continue;
            MinimizeManagedWindow(handle);
        }
    }

    private static void ShowWindowNoAnimation(IntPtr handle, int nCmdShow)
    {
        int one = 1;
        NativeMethods.DwmSetWindowAttribute(handle, NativeMethods.DWMWA_TRANSITIONS_FORCEDISABLED, ref one, sizeof(int));
        NativeMethods.ShowWindow(handle, nCmdShow);
        int zero = 0;
        NativeMethods.DwmSetWindowAttribute(handle, NativeMethods.DWMWA_TRANSITIONS_FORCEDISABLED, ref zero, sizeof(int));
    }

    private void ConvertVisibleBoundsToWindowBounds(
        IntPtr handle,
        int visibleX,
        int visibleY,
        int visibleWidth,
        int visibleHeight,
        out int windowX,
        out int windowY,
        out int windowWidth,
        out int windowHeight)
    {
        int boundedWidth = Math.Max(1, visibleWidth);
        int boundedHeight = Math.Max(1, visibleHeight);

        windowX = visibleX;
        windowY = visibleY;
        windowWidth = boundedWidth;
        windowHeight = boundedHeight;

        if (!_frameInsetsCache.TryGetValue(handle, out var insets))
        {
            if (!NativeMethods.TryGetWindowFrameInsets(
                    handle,
                    out int leftInset,
                    out int topInset,
                    out int rightInset,
                    out int bottomInset))
            {
                return;
            }

            insets = (leftInset, topInset, rightInset, bottomInset);
            _frameInsetsCache[handle] = insets;
        }

        windowX -= insets.Left;
        windowY -= insets.Top;
        windowWidth += insets.Left + insets.Right;
        windowHeight += insets.Top + insets.Bottom;
    }

    public void ReleaseManagedWindow(IntPtr handle)
    {
        if (!_managedWindowStates.TryGetValue(handle, out var state))
            return;

        _managedWindowStates.Remove(handle);
        _lastAsyncPos.Remove(handle);
        _frameInsetsCache.Remove(handle);

        if (!NativeMethods.IsWindow(handle))
            return;

        // Managed tabs are pushed to HWND_BOTTOM while inactive (not minimized).
        // Still restore iconic/maximized state if it somehow occurred, so position
        // restoration works reliably for shell windows (e.g. Explorer).
        if (NativeMethods.IsIconic(handle) || NativeMethods.IsZoomed(handle))
        {
            NativeMethods.ShowWindow(handle, NativeMethods.SW_RESTORE);
        }

        // exStyle を元に戻す（HideFromTaskbar で変更した WS_EX_TOOLWINDOW / WS_EX_APPWINDOW を含む）
        int currentExStyle = NativeMethods.GetWindowLong(handle, NativeMethods.GWL_EXSTYLE);
        if (currentExStyle != state.OriginalExStyle)
            NativeMethods.SetWindowLong(handle, NativeMethods.GWL_EXSTYLE, state.OriginalExStyle);

        if (state.OriginalRect.Width > 0 && state.OriginalRect.Height > 0)
        {
            bool restored = NativeMethods.SetWindowPos(
                handle,
                IntPtr.Zero,
                state.OriginalRect.Left,
                state.OriginalRect.Top,
                state.OriginalRect.Width,
                state.OriginalRect.Height,
                NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_FRAMECHANGED);

            if (!restored)
            {
                NativeMethods.MoveWindow(
                    handle,
                    state.OriginalRect.Left,
                    state.OriginalRect.Top,
                    state.OriginalRect.Width,
                    state.OriginalRect.Height,
                    true);
            }
        }

        if (state.WasMinimized)
            NativeMethods.ShowWindow(handle, NativeMethods.SW_MINIMIZE);
        else if (state.WasMaximized)
            NativeMethods.ShowWindow(handle, NativeMethods.SW_MAXIMIZE);
        else if (state.WasVisible)
            NativeMethods.ShowWindow(handle, NativeMethods.SW_RESTORE);
        else
            NativeMethods.ShowWindow(handle, NativeMethods.SW_HIDE);

        if (state.WasVisible)
        {
            NativeMethods.RedrawWindow(
                handle,
                IntPtr.Zero,
                IntPtr.Zero,
                NativeMethods.RDW_INVALIDATE |
                NativeMethods.RDW_ERASE |
                NativeMethods.RDW_FRAME |
                NativeMethods.RDW_ALLCHILDREN |
                NativeMethods.RDW_UPDATENOW);
            NativeMethods.UpdateWindow(handle);
        }
    }

    /// <summary>
    /// 管理ウィンドウのタスクバー表示を切り替える。
    /// hide=true で WS_EX_TOOLWINDOW を付与し WS_EX_APPWINDOW を除去（タスクバーから非表示）。
    /// hide=false で元の exStyle の WS_EX_APPWINDOW ビットを復元し WS_EX_TOOLWINDOW を除去。
    /// </summary>
    public void ApplyTaskbarVisibility(IntPtr handle, bool hide)
    {
        if (!NativeMethods.IsWindow(handle)) return;

        int current = NativeMethods.GetWindowLong(handle, NativeMethods.GWL_EXSTYLE);
        // GetWindowLong が失敗すると 0 を返す。GetLastWin32Error で判別する。
        if (current == 0 && System.Runtime.InteropServices.Marshal.GetLastWin32Error() != 0) return;

        int newStyle;
        if (hide)
        {
            newStyle = (current | (int)NativeMethods.WS_EX_TOOLWINDOW) & ~(int)NativeMethods.WS_EX_APPWINDOW;
        }
        else
        {
            // 元の exStyle から WS_EX_APPWINDOW ビットを復元する
            int originalAppWindow = _managedWindowStates.TryGetValue(handle, out var state)
                ? state.OriginalExStyle & (int)NativeMethods.WS_EX_APPWINDOW
                : (int)NativeMethods.WS_EX_APPWINDOW;
            newStyle = (current & ~(int)NativeMethods.WS_EX_TOOLWINDOW) | originalAppWindow;
        }

        if (newStyle == current) return;

        NativeMethods.SetWindowLong(handle, NativeMethods.GWL_EXSTYLE, newStyle);
        NativeMethods.SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE |
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_FRAMECHANGED);
    }

    public void ForgetManagedWindow(IntPtr handle)
    {
        _managedWindowStates.Remove(handle);
        _frameInsetsCache.Remove(handle);
    }

    public bool IsWindowValid(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return false;

        try
        {
            NativeMethods.GetWindowThreadProcessId(handle, out uint processId);
            if (processId == 0) return false;

            // Try to get process - if it throws, the window is gone
            using var process = Process.GetProcessById((int)processId);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    public void ArrangeTopmostWindows()
    {
        var currentProcessId = Environment.ProcessId;
        var topmostWindows = new List<(IntPtr Handle, NativeMethods.RECT Rect, string Title)>();

        NativeMethods.EnumWindows((hWnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hWnd)) return true;

            int style = NativeMethods.GetWindowLong(hWnd, NativeMethods.GWL_STYLE);
            int exStyle = NativeMethods.GetWindowLong(hWnd, NativeMethods.GWL_EXSTYLE);

            if ((style & (int)NativeMethods.WS_CHILD) != 0) return true;
            if ((exStyle & (int)NativeMethods.WS_EX_TOPMOST) == 0) return true;

            NativeMethods.GetWindowThreadProcessId(hWnd, out uint processId);
            if (processId == currentProcessId) return true;

            string className = NativeMethods.GetWindowClassName(hWnd);
            if (IsSystemWindow(className)) return true;

            NativeMethods.GetWindowRect(hWnd, out var rect);
            // Skip tiny helper windows (e.g. WPF internal 1x1 windows)
            if (rect.Width < 10 || rect.Height < 10) return true;

            string windowTitle = NativeMethods.GetWindowTitle(hWnd);
            if (string.IsNullOrWhiteSpace(windowTitle)) windowTitle = $"(0x{hWnd:X})";
            topmostWindows.Add((hWnd, rect, windowTitle));

            return true;
        }, IntPtr.Zero);

        if (topmostWindows.Count == 0)
        {
            Debug.WriteLine("[ArrangeTopmost] No topmost windows found.");
            return;
        }

        var workArea = new NativeMethods.RECT();
        NativeMethods.SystemParametersInfo(NativeMethods.SPI_GETWORKAREA, 0, ref workArea, 0);
        Debug.WriteLine($"[ArrangeTopmost] WorkArea: L={workArea.Left} T={workArea.Top} R={workArea.Right} B={workArea.Bottom}");

        int x = workArea.Right;
        int y = workArea.Bottom;
        int columnMaxWidth = 0;

        foreach (var (handle, rect, windowTitle) in topmostWindows)
        {
            int w = rect.Width;
            int h = rect.Height;

            if (y - h < workArea.Top)
            {
                x -= columnMaxWidth;
                y = workArea.Bottom;
                columnMaxWidth = 0;
            }

            int posX = x - w;
            int posY = y - h;

            bool result = NativeMethods.SetWindowPos(handle, NativeMethods.HWND_TOPMOST,
                posX, posY, w, h, NativeMethods.SWP_NOACTIVATE);
            if (!result)
            {
                int error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                Debug.WriteLine($"[ArrangeTopmost] {windowTitle} (0x{handle:X}) -> ({posX},{posY}) {w}x{h} SetWindowPos FAILED error={error}");
                result = NativeMethods.MoveWindow(handle, posX, posY, w, h, true);
                if (!result)
                {
                    error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                    Debug.WriteLine($"[ArrangeTopmost] {windowTitle} (0x{handle:X}) MoveWindow FAILED error={error}");
                }
            }
            else
            {
                Debug.WriteLine($"[ArrangeTopmost] {windowTitle} (0x{handle:X}) -> ({posX},{posY}) {w}x{h} OK");
            }

            y -= h;
            if (w > columnMaxWidth) columnMaxWidth = w;
        }
    }

    private sealed class ManagedWindowState
    {
        public NativeMethods.RECT OriginalRect { get; init; }
        public bool WasVisible { get; init; }
        public bool WasMinimized { get; init; }
        public bool WasMaximized { get; init; }
        public int OriginalExStyle { get; init; }
    }
}
