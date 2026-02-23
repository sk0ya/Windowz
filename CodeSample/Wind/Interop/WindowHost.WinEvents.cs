using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Wind.Interop;

public partial class WindowHost
{
    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(
        uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool IsZoomed(IntPtr hWnd);

    private delegate void WinEventDelegate(
        IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint EVENT_OBJECT_SHOW = 0x8002;
    private const uint EVENT_OBJECT_DESTROY = 0x8001;
    private const uint EVENT_SYSTEM_MOVESIZESTART = 0x000A;
    private const uint EVENT_SYSTEM_MOVESIZEEND = 0x000B;
    private const uint EVENT_SYSTEM_MINIMIZESTART = 0x0016;
    private const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private const int OBJID_WINDOW = 0;

    private IntPtr _winEventHookMoveSize;
    private IntPtr _winEventHookMinimize;
    private IntPtr _winEventHookLocation;
    private IntPtr _winEventHookDestroy;
    private IntPtr _winEventHookShow;
    private IntPtr _winEventHookForeground;
    private WinEventDelegate? _winEventProc;
    private bool _wasMaximized;
    // EVENT_SYSTEM_FOREGROUND で直前のフォアグラウンドウィンドウを追跡する
    private IntPtr _lastForeground;

    // Move tracking state
    private bool _isHostedMoving;

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        if (_hostedWindowHandle == IntPtr.Zero)
        {
            return new HandleRef(this, IntPtr.Zero);
        }

        EnsureClassRegistered();

        const uint WS_CLIPCHILDREN = 0x02000000;
        const uint WS_CLIPSIBLINGS = 0x04000000;

        // Create a host window
        _hwndHost = CreateWindowEx(
            0,
            HostClassName,
            "WindHost",
            NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE | WS_CLIPCHILDREN | WS_CLIPSIBLINGS,
            0, 0, 100, 100,
            hwndParent.Handle,
            IntPtr.Zero,
            GetModuleHandle(null),
            IntPtr.Zero);

        if (_hwndHost == IntPtr.Zero)
        {
            return new HandleRef(this, IntPtr.Zero);
        }

        // Register this instance for WndProc lookup
        _instances[_hwndHost] = this;

        // Remove window decorations (original state was saved in constructor)
        int newStyle = _originalStyle;
        newStyle &= ~(int)(NativeMethods.WS_CAPTION | NativeMethods.WS_THICKFRAME |
                          NativeMethods.WS_MINIMIZEBOX | NativeMethods.WS_MAXIMIZEBOX |
                          NativeMethods.WS_SYSMENU | NativeMethods.WS_BORDER | NativeMethods.WS_DLGFRAME);

        if (_embedAsChildWindow)
        {
            // ConsoleWindowClass (PowerShell/conhost) behaves better as a true child.
            newStyle &= unchecked((int)~NativeMethods.WS_POPUP);
            newStyle |= (int)NativeMethods.WS_CHILD | (int)NativeMethods.WS_VISIBLE;
        }
        else
        {
            // Use WS_POPUP + SetParent for non-console windows. Many apps (Chromium,
            // Office, Electron, etc.) break when made WS_CHILD because their rendering
            // and input pipelines assume a top-level window.
            newStyle &= ~(int)NativeMethods.WS_CHILD;
            newStyle |= unchecked((int)NativeMethods.WS_POPUP) | (int)NativeMethods.WS_VISIBLE;
        }

        NativeMethods.SetWindowLong(_hostedWindowHandle, NativeMethods.GWL_STYLE, newStyle);

        // Set parent to our host window
        NativeMethods.SetParent(_hostedWindowHandle, _hwndHost);
        ApplyEmbeddedExStyle();

        // Position the window at 0,0 within the host
        NativeMethods.SetWindowPos(_hostedWindowHandle, IntPtr.Zero, 0, 0, 100, 100,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_FRAMECHANGED | NativeMethods.SWP_SHOWWINDOW);

        // Make sure it's visible
        NativeMethods.ShowWindow(_hostedWindowHandle, NativeMethods.SW_SHOW);
        UpdateTaskbarButtonRegistration();

        // Set up event hook to monitor hosted window events
        SetupWinEventHook();

        return new HandleRef(this, _hwndHost);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        // Remove from instance mapping
        if (_hwndHost != IntPtr.Zero)
            _instances.Remove(_hwndHost);

        // Detach the hosted window before destroying the host HWND.
        // If the hosted window is still a child of _hwndHost when DestroyWindow
        // is called, Windows will cascade-destroy it, killing the hosted process's window.
        if (_hostedWindowHandle != IntPtr.Zero && !_isHostedWindowClosed)
        {
            UnregisterTaskbarButton();
            RemoveWinEventHook();
            NativeMethods.SetWindowRgn(_hostedWindowHandle, IntPtr.Zero, false);
            NativeMethods.SetParent(_hostedWindowHandle, IntPtr.Zero);
            NativeMethods.SetWindowLong(_hostedWindowHandle, NativeMethods.GWL_STYLE, _originalStyle);
            NativeMethods.SetWindowLong(_hostedWindowHandle, NativeMethods.GWL_EXSTYLE, _originalExStyle);
            NativeMethods.SetWindowPos(_hostedWindowHandle, IntPtr.Zero,
                _originalRect.Left, _originalRect.Top, _originalRect.Width, _originalRect.Height,
                NativeMethods.SWP_NOZORDER | NativeMethods.SWP_FRAMECHANGED | NativeMethods.SWP_SHOWWINDOW);
            NativeMethods.ShowWindow(_hostedWindowHandle, NativeMethods.SW_RESTORE);
            _hostedWindowHandle = IntPtr.Zero;
        }

        if (_hwndHost != IntPtr.Zero)
        {
            DestroyWindow(_hwndHost);
            _hwndHost = IntPtr.Zero;
        }
    }

    protected override void OnWindowPositionChanged(Rect rcBoundingBox)
    {
        base.OnWindowPositionChanged(rcBoundingBox);

        if (_hwndHost != IntPtr.Zero && _hostedWindowHandle != IntPtr.Zero && !_isHostedWindowClosed)
        {
            int width = (int)rcBoundingBox.Width;
            int height = (int)rcBoundingBox.Height;

            if (width > 0 && height > 0)
            {
                ResizeHostedWindow(width, height);
            }
        }
    }

    protected override IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_PARENTNOTIFY)
        {
            int eventCode = wParam.ToInt32() & 0xFFFF;
            if (eventCode == WM_DESTROY)
            {
                // Hosted window is being destroyed
                UnregisterTaskbarButton();
                _isHostedWindowClosed = true;
                _hostedWindowHandle = IntPtr.Zero;
                HostedWindowClosed?.Invoke(this, EventArgs.Empty);
                handled = true;
                return IntPtr.Zero;
            }
        }

        return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
    }

    private void SetupWinEventHook()
    {
        if (_hostedWindowHandle == IntPtr.Zero) return;

        GetWindowThreadProcessId(_hostedWindowHandle, out uint processId);

        _winEventProc = WinEventCallback;

        // Hook for move/size start and end
        _winEventHookMoveSize = SetWinEventHook(
            EVENT_SYSTEM_MOVESIZESTART, EVENT_SYSTEM_MOVESIZEEND,
            IntPtr.Zero, _winEventProc, processId, 0, WINEVENT_OUTOFCONTEXT);

        // Hook for minimize events
        _winEventHookMinimize = SetWinEventHook(
            EVENT_SYSTEM_MINIMIZESTART, EVENT_SYSTEM_MINIMIZESTART,
            IntPtr.Zero, _winEventProc, processId, 0, WINEVENT_OUTOFCONTEXT);

        // Hook for location/size changes to detect maximize and move
        _winEventHookLocation = SetWinEventHook(
            EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE,
            IntPtr.Zero, _winEventProc, processId, 0, WINEVENT_OUTOFCONTEXT);

        // Hook for window destruction (WS_POPUP ウィンドウは WM_PARENTNOTIFY が送信されないため必要)
        _winEventHookDestroy = SetWinEventHook(
            EVENT_OBJECT_DESTROY, EVENT_OBJECT_DESTROY,
            IntPtr.Zero, _winEventProc, processId, 0, WINEVENT_OUTOFCONTEXT);

        // Hook for new window detection (同プロセスの新規ウィンドウを検出)
        _winEventHookShow = SetWinEventHook(
            EVENT_OBJECT_SHOW, EVENT_OBJECT_SHOW,
            IntPtr.Zero, _winEventProc, processId, 0, WINEVENT_OUTOFCONTEXT);

        // Hook for foreground changes (グローバル: 埋め込みアプリがフォアグラウンドになったことを検知する)
        // プロセスIDを0にしてグローバルにしないと、別プロセスのフォアグラウンドイベントを取得できない
        _winEventHookForeground = SetWinEventHook(
            EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _winEventProc, 0, 0, WINEVENT_OUTOFCONTEXT);
    }

    private void RemoveWinEventHook()
    {
        if (_winEventHookMoveSize != IntPtr.Zero)
        {
            UnhookWinEvent(_winEventHookMoveSize);
            _winEventHookMoveSize = IntPtr.Zero;
        }
        if (_winEventHookMinimize != IntPtr.Zero)
        {
            UnhookWinEvent(_winEventHookMinimize);
            _winEventHookMinimize = IntPtr.Zero;
        }
        if (_winEventHookLocation != IntPtr.Zero)
        {
            UnhookWinEvent(_winEventHookLocation);
            _winEventHookLocation = IntPtr.Zero;
        }
        if (_winEventHookDestroy != IntPtr.Zero)
        {
            UnhookWinEvent(_winEventHookDestroy);
            _winEventHookDestroy = IntPtr.Zero;
        }
        if (_winEventHookShow != IntPtr.Zero)
        {
            UnhookWinEvent(_winEventHookShow);
            _winEventHookShow = IntPtr.Zero;
        }
        if (_winEventHookForeground != IntPtr.Zero)
        {
            UnhookWinEvent(_winEventHookForeground);
            _winEventHookForeground = IntPtr.Zero;
        }
        _winEventProc = null;
    }

    private static readonly HashSet<string> _ephemeralWindowClasses = new(StringComparer.Ordinal)
    {
        "#32768",             // Menu
        "#32769",             // Desktop
        "#32770",             // Dialog (MessageBox etc.)
        "#32771",             // Task switch
        "tooltips_class32",   // Tooltip
        "SysShadow",          // Shadow
        "IME",                // IME window
        "MSCTFIME UI",        // IME UI
    };

    private bool IsEmbeddableWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || hwnd == _hostedWindowHandle) return false;
        if (!NativeMethods.IsWindowVisible(hwnd)) return false;

        int style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_STYLE);
        int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);

        // Skip child windows
        if ((style & (int)NativeMethods.WS_CHILD) != 0) return false;

        // Skip tool windows (tooltips, etc.)
        if ((exStyle & (int)NativeMethods.WS_EX_TOOLWINDOW) != 0) return false;

        // Must have a title
        string title = NativeMethods.GetWindowTitle(hwnd);
        if (string.IsNullOrWhiteSpace(title)) return false;

        // Skip ephemeral window classes
        string className = NativeMethods.GetWindowClassName(hwnd);
        if (_ephemeralWindowClasses.Contains(className)) return false;
        if (WindowClassFilters.IsUnsupportedForEmbedding(className)) return false;

        return true;
    }

    private bool IsHostedDialogWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || hwnd == _hostedWindowHandle) return false;
        if (!NativeMethods.IsWindow(hwnd) || !NativeMethods.IsWindowVisible(hwnd)) return false;

        string className = NativeMethods.GetWindowClassName(hwnd);
        if (!string.Equals(className, "#32770", StringComparison.Ordinal)) return false;

        NativeMethods.GetWindowThreadProcessId(hwnd, out uint processId);
        return processId == (uint)HostedProcessId;
    }

    private void EnsureHostedDialogIsInFront(IntPtr dialogHwnd)
    {
        if (!NativeMethods.IsWindow(dialogHwnd) || !NativeMethods.IsWindowVisible(dialogHwnd)) return;

        var windRoot = _hwndHost != IntPtr.Zero
            ? NativeMethods.GetAncestor(_hwndHost, NativeMethods.GA_ROOT)
            : IntPtr.Zero;
        if (windRoot == IntPtr.Zero) return;

        var foreground = NativeMethods.GetForegroundWindow();
        NativeMethods.GetWindowThreadProcessId(foreground, out uint foregroundPid);

        if (foreground != windRoot &&
            foreground != _hostedWindowHandle &&
            foregroundPid != (uint)HostedProcessId)
        {
            return;
        }

        NativeMethods.ForceForegroundWindow(dialogHwnd);
    }

    private void WinEventCallback(
        IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        // Detect new windows from the same process
        if (eventType == EVENT_OBJECT_SHOW && idObject == OBJID_WINDOW)
        {
            // Keep hosted app dialogs (MessageBox class) in front of Wind.
            if (IsHostedDialogWindow(hwnd))
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Input, () => EnsureHostedDialogIsInFront(hwnd));
                return;
            }

            if (hwnd != _hostedWindowHandle && IsEmbeddableWindow(hwnd))
            {
                NewWindowDetected?.Invoke(hwnd);
                return;
            }
        }

        // WindがバックグラウンドのときにWS_POPUPの埋め込みアプリがフォアグラウンドになった場合、
        // Windを前面に持ってくるよう要求する。
        // WS_POPUPはSetParentで親を設定してもクリック時のアクティベーションが親に伝播しないため、
        // EVENT_SYSTEM_FOREGROUNDで明示的に検知する必要がある。
        if (eventType == EVENT_SYSTEM_FOREGROUND)
        {
            var prevForeground = _lastForeground;
            _lastForeground = hwnd;

            if (hwnd == _hostedWindowHandle)
            {
                var windRoot = _hwndHost != IntPtr.Zero
                    ? NativeMethods.GetAncestor(_hwndHost, NativeMethods.GA_ROOT)
                    : IntPtr.Zero;
                if (windRoot == IntPtr.Zero) return;

                // Windがすでにフォアグラウンドなら何もしない
                if (NativeMethods.GetForegroundWindow() == windRoot) return;

                // 直前のフォアグラウンドを確認して誤発火を防ぐ
                if (prevForeground != IntPtr.Zero)
                {
                    // コンテキストメニュー・IME等のエフェメラルウィンドウが閉じた場合は無視
                    var prevClass = NativeMethods.GetWindowClassName(prevForeground);
                    if (_ephemeralWindowClasses.Contains(prevClass)) return;

                    // 埋め込みアプリと同一プロセスのウィンドウが閉じた場合は無視
                    // (アプリ内ポップアップ・サブウィンドウなど)
                    NativeMethods.GetWindowThreadProcessId(prevForeground, out uint prevPid);
                    if (prevPid == (uint)HostedProcessId) return;

                    // Windと同一プロセスのウィンドウが閉じた場合は無視
                    NativeMethods.GetWindowThreadProcessId(windRoot, out uint windPid);
                    if (prevPid == windPid) return;
                }

                BringToFrontRequested?.Invoke(this, EventArgs.Empty);
            }
            return;
        }

        if (hwnd != _hostedWindowHandle) return;

        if (eventType == EVENT_OBJECT_DESTROY && idObject == OBJID_WINDOW)
        {
            // ホストされたウィンドウが破棄された
            // WS_POPUP ウィンドウでは WM_PARENTNOTIFY が送信されないため、ここで検出する
            if (!_isHostedWindowClosed)
            {
                UnregisterTaskbarButton();
                _isHostedWindowClosed = true;
                RemoveWinEventHook();
                _hostedWindowHandle = IntPtr.Zero;
                HostedWindowClosed?.Invoke(this, EventArgs.Empty);
            }
            return;
        }

        if (eventType == EVENT_SYSTEM_MOVESIZESTART)
        {
            _isHostedMoving = true;
        }
        else if (eventType == EVENT_SYSTEM_MOVESIZEEND)
        {
            if (_isHostedMoving)
            {
                _isHostedMoving = false;
                // Reset hosted window to fill host
                if (_hwndHost != IntPtr.Zero)
                {
                    NativeMethods.GetWindowRect(_hwndHost, out var hostRect);
                    int w = hostRect.Width, h = hostRect.Height;
                    ResizeHostedWindow(w, h);
                }
            }
        }
        else if (eventType == EVENT_SYSTEM_MINIMIZESTART)
        {
            // The hosted window is trying to minimize
            // Restore it immediately and minimize Wind instead
            NativeMethods.ShowWindow(_hostedWindowHandle, NativeMethods.SW_RESTORE);
            MinimizeRequested?.Invoke(this, EventArgs.Empty);
        }
        else if (eventType == EVENT_OBJECT_LOCATIONCHANGE && idObject == OBJID_WINDOW)
        {
            // Check if window became maximized
            if (!_isHostedMoving)
            {
                bool isMaximized = IsZoomed(_hostedWindowHandle);
                if (isMaximized && !_wasMaximized)
                {
                    _wasMaximized = true;
                    NativeMethods.ShowWindow(_hostedWindowHandle, NativeMethods.SW_RESTORE);
                    MaximizeRequested?.Invoke(this, EventArgs.Empty);
                }
                else if (!isMaximized)
                {
                    _wasMaximized = false;
                }
            }

            // During a hosted window move, detect displacement from host
            // and translate it to Wind movement, then reset position.
            if (_isHostedMoving && _hwndHost != IntPtr.Zero &&
                NativeMethods.GetWindowRect(_hostedWindowHandle, out var hostedRect) &&
                NativeMethods.GetWindowRect(_hwndHost, out var hostRect))
            {
                int dx = hostedRect.Left - hostRect.Left;
                int dy = hostedRect.Top - hostRect.Top;

                if (dx != 0 || dy != 0)
                {
                    MoveRequested?.Invoke(dx, dy);
                    ResizeHostedWindow(hostRect.Width, hostRect.Height);
                }
            }
        }
    }
}
