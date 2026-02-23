using System.Windows.Interop;
using System.Windows.Media;

namespace Wind.Interop;

public partial class WindowHost : HwndHost
{
    private const int ConsoleTopViewportCompensationPx = 18;
    private const int ConsoleBottomViewportCompensationPx = 6;

    private IntPtr _hostedWindowHandle;
    private IntPtr _hwndHost;
    private int _originalStyle;
    private int _originalExStyle;
    private NativeMethods.RECT _originalRect;
    private bool _isHostedWindowClosed;
    private bool _hideFromTaskbar;
    private bool _taskbarButtonRegistered;
    private readonly bool _isConsoleWindowClass;
    private readonly bool _embedAsChildWindow;
    private readonly bool _stripLayeredExStyleForEmbeddedConsole;

    public IntPtr HostedWindowHandle => _hostedWindowHandle;
    public int HostedProcessId { get; }

    public event EventHandler? HostedWindowClosed;
    public event EventHandler? MinimizeRequested;
    public event EventHandler? MaximizeRequested;
    public event EventHandler? BringToFrontRequested;

    /// <summary>
    /// Fired when the hosted window is being dragged. Parameters are (dx, dy) in physical pixels.
    /// </summary>
    public event Action<int, int>? MoveRequested;

    /// <summary>
    /// Fired when the hosted process creates a new top-level window.
    /// The parameter is the HWND of the new window.
    /// </summary>
    public event Action<IntPtr>? NewWindowDetected;

    // Background color property
    private Color _backgroundColor = Colors.Black;

    public Color BackgroundColor
    {
        get => _backgroundColor;
        set
        {
            if (_backgroundColor != value)
            {
                _backgroundColor = value;
                // Trigger repaint if host window exists
                if (_hwndHost != IntPtr.Zero)
                {
                    InvalidateRect(_hwndHost, IntPtr.Zero, true);
                }
            }
        }
    }

    public WindowHost(IntPtr windowHandle, bool hideFromTaskbar)
    {
        _hostedWindowHandle = windowHandle;
        _hideFromTaskbar = hideFromTaskbar;

        // Store the process ID so we can force-kill if needed at shutdown.
        NativeMethods.GetWindowThreadProcessId(windowHandle, out uint pid);
        HostedProcessId = (int)pid;

        // Save original state immediately for later restoration.
        _originalStyle = NativeMethods.GetWindowLong(_hostedWindowHandle, NativeMethods.GWL_STYLE);
        _originalExStyle = NativeMethods.GetWindowLong(_hostedWindowHandle, NativeMethods.GWL_EXSTYLE);
        NativeMethods.GetWindowRect(_hostedWindowHandle, out _originalRect);

        string className = NativeMethods.GetWindowClassName(_hostedWindowHandle);
        _isConsoleWindowClass = string.Equals(className, "ConsoleWindowClass", StringComparison.OrdinalIgnoreCase);
        _embedAsChildWindow = _isConsoleWindowClass;
        _stripLayeredExStyleForEmbeddedConsole = _isConsoleWindowClass &&
                                                 (_originalExStyle & (int)NativeMethods.WS_EX_LAYERED) != 0;

        // Apply initial taskbar style.
        // We hide immediately only when taskbar hiding is enabled to avoid a brief
        // floating flash before BuildWindowCore attaches the window.
        ApplyEmbeddedExStyle();
        if (_hideFromTaskbar)
        {
            NativeMethods.ShowWindow(_hostedWindowHandle, 0); // SW_HIDE = 0
        }
    }

    private int GetEmbeddedExStyle()
    {
        int newExStyle = _originalExStyle;

        if (_stripLayeredExStyleForEmbeddedConsole)
        {
            // ConsoleWindowClass (PowerShell/conhost) can render with a clipped top edge
            // when kept layered after reparenting.
            newExStyle &= ~(int)NativeMethods.WS_EX_LAYERED;
        }

        if (_hideFromTaskbar)
        {
            newExStyle &= ~(int)NativeMethods.WS_EX_APPWINDOW;
            newExStyle |= (int)NativeMethods.WS_EX_TOOLWINDOW;
            return newExStyle;
        }

        // Keep a visible taskbar button while embedded.
        newExStyle &= ~(int)NativeMethods.WS_EX_TOOLWINDOW;
        newExStyle |= (int)NativeMethods.WS_EX_APPWINDOW;

        return newExStyle;
    }

    private void ApplyEmbeddedExStyle()
    {
        if (_hostedWindowHandle == IntPtr.Zero || _isHostedWindowClosed)
            return;

        NativeMethods.SetWindowLong(_hostedWindowHandle, NativeMethods.GWL_EXSTYLE, GetEmbeddedExStyle());

        // Ask Windows shell to re-evaluate non-client/taskbar presentation.
        NativeMethods.SetWindowPos(
            _hostedWindowHandle,
            IntPtr.Zero,
            0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE |
            NativeMethods.SWP_NOSIZE |
            NativeMethods.SWP_NOZORDER |
            NativeMethods.SWP_NOACTIVATE |
            NativeMethods.SWP_FRAMECHANGED);
    }

    private void UpdateTaskbarButtonRegistration()
    {
        if (_hideFromTaskbar)
            UnregisterTaskbarButton();
        else
            RegisterTaskbarButton();
    }

    private bool TryGetClientInsets(out int insetLeft, out int insetTop, out int insetRight, out int insetBottom)
    {
        insetLeft = 0;
        insetTop = 0;
        insetRight = 0;
        insetBottom = 0;

        if (!_isConsoleWindowClass || _embedAsChildWindow || _hostedWindowHandle == IntPtr.Zero || _isHostedWindowClosed)
            return false;

        if (!NativeMethods.GetWindowRect(_hostedWindowHandle, out var windowRect))
            return false;

        if (!NativeMethods.GetClientRect(_hostedWindowHandle, out var clientRect))
            return false;

        var clientOrigin = new NativeMethods.POINT { X = 0, Y = 0 };
        if (!NativeMethods.ClientToScreen(_hostedWindowHandle, ref clientOrigin))
            return false;

        int clientWidth = Math.Max(0, clientRect.Width);
        int clientHeight = Math.Max(0, clientRect.Height);

        insetLeft = Math.Max(0, clientOrigin.X - windowRect.Left);
        insetTop = Math.Max(0, clientOrigin.Y - windowRect.Top);
        insetRight = Math.Max(0, windowRect.Width - insetLeft - clientWidth);
        insetBottom = Math.Max(0, windowRect.Height - insetTop - clientHeight);
        return true;
    }

    private void RegisterTaskbarButton()
    {
        if (_taskbarButtonRegistered || _hostedWindowHandle == IntPtr.Zero || _isHostedWindowClosed)
            return;

        TaskbarListInterop.Instance.AddTab(_hostedWindowHandle);
        _taskbarButtonRegistered = true;
    }

    private void UnregisterTaskbarButton()
    {
        if (!_taskbarButtonRegistered)
            return;

        TaskbarListInterop.Instance.DeleteTab(_hostedWindowHandle);
        _taskbarButtonRegistered = false;
    }

    public void SetHideFromTaskbar(bool hideFromTaskbar)
    {
        if (_hideFromTaskbar == hideFromTaskbar)
            return;

        _hideFromTaskbar = hideFromTaskbar;
        ApplyEmbeddedExStyle();

        if (_hostedWindowHandle != IntPtr.Zero && !_isHostedWindowClosed)
        {
            NativeMethods.ShowWindow(_hostedWindowHandle, NativeMethods.SW_SHOW);
        }

        UpdateTaskbarButtonRegistration();
    }

    public void ReleaseWindow()
    {
        if (_hostedWindowHandle == IntPtr.Zero || _isHostedWindowClosed) return;

        UnregisterTaskbarButton();

        // Remove from instance mapping
        if (_hwndHost != IntPtr.Zero)
            _instances.Remove(_hwndHost);

        // Remove event hook before releasing
        RemoveWinEventHook();

        // Remove clipping region before restoring
        NativeMethods.SetWindowRgn(_hostedWindowHandle, IntPtr.Zero, false);

        // Restore parent (to desktop)
        NativeMethods.SetParent(_hostedWindowHandle, IntPtr.Zero);

        // Restore original styles
        NativeMethods.SetWindowLong(_hostedWindowHandle, NativeMethods.GWL_STYLE, _originalStyle);
        NativeMethods.SetWindowLong(_hostedWindowHandle, NativeMethods.GWL_EXSTYLE, _originalExStyle);

        // Apply style changes and restore to original position and size
        NativeMethods.SetWindowPos(_hostedWindowHandle, IntPtr.Zero,
            _originalRect.Left, _originalRect.Top, _originalRect.Width, _originalRect.Height,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_FRAMECHANGED | NativeMethods.SWP_SHOWWINDOW);

        NativeMethods.ShowWindow(_hostedWindowHandle, NativeMethods.SW_RESTORE);

        _hostedWindowHandle = IntPtr.Zero;
    }

    public void ResizeHostedWindow(int width, int height)
    {
        if (_hwndHost == IntPtr.Zero || _hostedWindowHandle == IntPtr.Zero || _isHostedWindowClosed) return;

        // Only resize the hosted window within the host.
        // _hwndHost position is managed by HwndHost base class — do not move it.
        if (!_isHostedMoving)
        {
            if (_embedAsChildWindow)
            {
                int childY = 0;
                int childTargetHeight = Math.Max(1, height);

                if (_isConsoleWindowClass)
                {
                    // ConsoleWindowClass can keep an internal upward viewport drift after reparenting.
                    // Shift it down and over-scan height so the clipped first rows become visible.
                    childY = ConsoleTopViewportCompensationPx;
                    childTargetHeight = Math.Max(1, height + ConsoleTopViewportCompensationPx + ConsoleBottomViewportCompensationPx);
                }

                NativeMethods.SetWindowPos(_hostedWindowHandle, IntPtr.Zero,
                    0, childY, Math.Max(1, width), childTargetHeight,
                    NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOCOPYBITS);

                // WS_CHILD windows are automatically clipped by parent; keep region cleared.
                NativeMethods.SetWindowRgn(_hostedWindowHandle, IntPtr.Zero, true);
                return;
            }

            int x = 0;
            int y = 0;
            int targetWidth = width;
            int targetHeight = height;
            int regionLeft = 0;
            int regionTop = 0;
            int regionRight = width;
            int regionBottom = height;

            if (TryGetClientInsets(out int insetLeft, out int insetTop, out int insetRight, out int insetBottom))
            {
                // ConsoleWindowClass can keep non-client insets even after style stripping.
                // Offset and expand the hosted window so its client area exactly fills the host.
                x = -insetLeft;
                y = -insetTop;
                targetWidth = width + insetLeft + insetRight;
                targetHeight = height + insetTop + insetBottom;
                regionLeft = insetLeft;
                regionTop = insetTop;
                regionRight = insetLeft + width;
                regionBottom = insetTop + height;
            }

            targetWidth = Math.Max(1, targetWidth);
            targetHeight = Math.Max(1, targetHeight);
            regionRight = Math.Max(regionLeft + 1, regionRight);
            regionBottom = Math.Max(regionTop + 1, regionBottom);

            // Use SetWindowPos with SWP_NOCOPYBITS to prevent Windows from copying
            // old pixel content during resize, which causes ghost artifacts.
            NativeMethods.SetWindowPos(_hostedWindowHandle, IntPtr.Zero,
                x, y, targetWidth, targetHeight,
                NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOCOPYBITS);

            // Clip the WS_POPUP window to the host's client area.
            // Unlike WS_CHILD, WS_POPUP windows are not automatically clipped by
            // their parent, so we enforce it with a window region.
            // SetWindowRgn takes ownership of the region handle — do not delete it.
            IntPtr rgn = NativeMethods.CreateRectRgn(regionLeft, regionTop, regionRight, regionBottom);
            NativeMethods.SetWindowRgn(_hostedWindowHandle, rgn, true);
        }
    }

    public void FocusHostedWindow()
    {
        if (_hostedWindowHandle == IntPtr.Zero || _isHostedWindowClosed) return;

        // SetForegroundWindow を使うと EVENT_SYSTEM_FOREGROUND が発火して
        // BringToFrontRequested との誤発火ループが起きる。
        // AttachThreadInput + SetFocus でフォーカスのみ移す（フォアグラウンドは変えない）。
        var currentThread = NativeMethods.GetCurrentThreadId();
        var hostedThread = NativeMethods.GetWindowThreadProcessId(_hostedWindowHandle, out _);

        if (hostedThread != 0 && hostedThread != currentThread)
            NativeMethods.AttachThreadInput(currentThread, hostedThread, true);

        NativeMethods.SetFocus(_hostedWindowHandle);

        if (hostedThread != 0 && hostedThread != currentThread)
            NativeMethods.AttachThreadInput(currentThread, hostedThread, false);
    }

    public void ForceRedraw()
    {
        if (_hostedWindowHandle == IntPtr.Zero || _isHostedWindowClosed) return;

        NativeMethods.ShowWindow(_hostedWindowHandle, NativeMethods.SW_SHOW);
        NativeMethods.RedrawWindow(
            _hostedWindowHandle,
            IntPtr.Zero,
            IntPtr.Zero,
            NativeMethods.RDW_INVALIDATE |
            NativeMethods.RDW_ERASE |
            NativeMethods.RDW_FRAME |
            NativeMethods.RDW_ALLCHILDREN |
            NativeMethods.RDW_UPDATENOW);
        NativeMethods.UpdateWindow(_hostedWindowHandle);

        if (_hwndHost != IntPtr.Zero)
        {
            NativeMethods.RedrawWindow(
                _hwndHost,
                IntPtr.Zero,
                IntPtr.Zero,
                NativeMethods.RDW_INVALIDATE |
                NativeMethods.RDW_ERASE |
                NativeMethods.RDW_UPDATENOW);
            NativeMethods.UpdateWindow(_hwndHost);
        }
    }
}
