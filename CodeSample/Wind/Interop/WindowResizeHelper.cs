using System.Runtime.InteropServices;

namespace Wind.Interop;

/// <summary>
/// Creates thin overlay Win32 child windows at each edge of the parent window.
/// These sit on top of HwndHost content in Z-order, intercept mouse events at the
/// window edges, and initiate native resize on the parent window.
/// </summary>
public class WindowResizeHelper : IDisposable
{
    private readonly IntPtr _parentHwnd;
    private readonly IntPtr[] _gripHwnds = new IntPtr[4];
    private IntPtr _blockerHwnd;
    private bool _disposed;
    private bool _visible = true;

    private const int GripSize = 6;
    private const int CornerSize = 10;

    private const int LEFT = 0;
    private const int RIGHT = 1;
    private const int TOP = 2;
    private const int BOTTOM = 3;
    private const int BLOCKER = 4;

    private static readonly Dictionary<IntPtr, (WindowResizeHelper Helper, int Edge)> _hwndMap = new();
    private static WndProcDelegate? _wndProcDelegate;
    private static bool _classRegistered;
    private const string ClassName = "WindResizeGrip";

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    // Win32 constants
    private const uint WS_CHILD = 0x40000000;
    private const uint WS_VISIBLE = 0x10000000;
    private const uint WS_CLIPSIBLINGS = 0x04000000;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int GWL_EXSTYLE = -20;
    private const byte LWA_ALPHA = 0x02;
    private const uint WM_SETCURSOR = 0x0020;
    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint WM_PAINT = 0x000F;
    private const uint WM_ERASEBKGND = 0x0014;
    private const uint WM_NCDESTROY = 0x0082;
    private const int SC_SIZE = 0xF000;
    private const uint WM_SYSCOMMAND = 0x0112;
    private const int IDC_ARROW = 32512;
    private const int IDC_SIZEWE = 32644;
    private const int IDC_SIZENS = 32645;
    private const int IDC_SIZENWSE = 32642;
    private const int IDC_SIZENESW = 32643;
    private static readonly IntPtr HWND_TOP = IntPtr.Zero;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(int dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClass(ref WNDCLASS lpWndClass);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

    [DllImport("user32.dll")]
    private static extern IntPtr SetCursor(IntPtr hCursor);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadCursor(IntPtr hInstance, int lpCursorName);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern IntPtr BeginPaint(IntPtr hwnd, out PAINTSTRUCT lpPaint);

    [DllImport("user32.dll")]
    private static extern bool EndPaint(IntPtr hwnd, ref PAINTSTRUCT lpPaint);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("gdi32.dll")]
    private static extern IntPtr GetStockObject(int fnObject);

    private const int NULL_BRUSH = 5;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PAINTSTRUCT
    {
        public IntPtr hdc;
        public bool fErase;
        public RECT rcPaint;
        public bool fRestore;
        public bool fIncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[]? rgbReserved;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASS
    {
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
    }

    public WindowResizeHelper(IntPtr parentHwnd)
    {
        _parentHwnd = parentHwnd;
        EnsureClassRegistered();
        CreateGripWindows();
        UpdatePositions();
    }

    private static void EnsureClassRegistered()
    {
        if (_classRegistered) return;

        _wndProcDelegate = GripWndProc;
        var wc = new WNDCLASS
        {
            style = 0,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
            hInstance = GetModuleHandle(null),
            lpszClassName = ClassName,
            hbrBackground = GetStockObject(NULL_BRUSH),
        };

        RegisterClass(ref wc);
        _classRegistered = true;
    }

    private void CreateGripWindows()
    {
        for (int i = 0; i < 4; i++)
        {
            var hwnd = CreateWindowEx(
                0, ClassName, "",
                WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS,
                0, 0, 1, 1,
                _parentHwnd, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);

            if (hwnd == IntPtr.Zero) continue;

            // Make layered with alpha=1 so the window is visually invisible
            // but still receives mouse input
            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_LAYERED);
            SetLayeredWindowAttributes(hwnd, 0, 1, LWA_ALPHA);

            _gripHwnds[i] = hwnd;
            _hwndMap[hwnd] = (this, i);

            SetWindowPos(hwnd, HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
    }

    /// <summary>
    /// Repositions the grip windows to match the parent window's current client area.
    /// Call this when the parent window is resized.
    /// </summary>
    public void UpdatePositions()
    {
        GetClientRect(_parentHwnd, out var rect);
        int w = rect.Right;
        int h = rect.Bottom;

        if (w <= 0 || h <= 0) return;

        if (_gripHwnds[LEFT] != IntPtr.Zero)
            MoveWindow(_gripHwnds[LEFT], 0, 0, GripSize, h, true);

        if (_gripHwnds[RIGHT] != IntPtr.Zero)
            MoveWindow(_gripHwnds[RIGHT], w - GripSize, 0, GripSize, h, true);

        if (_gripHwnds[TOP] != IntPtr.Zero)
            MoveWindow(_gripHwnds[TOP], 0, 0, w, GripSize, true);

        if (_gripHwnds[BOTTOM] != IntPtr.Zero)
            MoveWindow(_gripHwnds[BOTTOM], 0, h - GripSize, w, GripSize, true);

        BringToTop();
    }

    /// <summary>
    /// Brings the grip windows to the top of the Z-order among siblings.
    /// Call this after a new HwndHost is embedded to ensure grips remain on top.
    /// </summary>
    public void BringToTop()
    {
        if (!_visible) return;

        for (int i = 0; i < 4; i++)
        {
            if (_gripHwnds[i] != IntPtr.Zero)
                SetWindowPos(_gripHwnds[i], HWND_TOP, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        if (_blockerHwnd != IntPtr.Zero)
            SetWindowPos(_blockerHwnd, HWND_TOP, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    /// <summary>
    /// Shows or hides the grip windows. Hide when maximized or when overlays are active.
    /// </summary>
    public void SetVisible(bool visible)
    {
        _visible = visible;
        const int SW_SHOW = 5;
        const int SW_HIDE = 0;

        for (int i = 0; i < 4; i++)
        {
            if (_gripHwnds[i] != IntPtr.Zero)
                ShowWindow(_gripHwnds[i], visible ? SW_SHOW : SW_HIDE);
        }

        if (_blockerHwnd != IntPtr.Zero)
            ShowWindow(_blockerHwnd, visible ? SW_SHOW : SW_HIDE);

        if (visible) BringToTop();
    }

    /// <summary>
    /// Places a blocker overlay at the internal boundary between tab bar and content.
    /// This overlay absorbs mouse events with a default arrow cursor and does not resize.
    /// Coordinates are in physical (Win32) pixels relative to the parent window's client area.
    /// </summary>
    public void SetBlocker(int x, int y, int width, int height)
    {
        if (_blockerHwnd == IntPtr.Zero)
        {
            _blockerHwnd = CreateWindowEx(
                0, ClassName, "",
                WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS,
                x, y, width, height,
                _parentHwnd, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);

            if (_blockerHwnd == IntPtr.Zero) return;

            int exStyle = GetWindowLong(_blockerHwnd, GWL_EXSTYLE);
            SetWindowLong(_blockerHwnd, GWL_EXSTYLE, exStyle | WS_EX_LAYERED);
            SetLayeredWindowAttributes(_blockerHwnd, 0, 0, LWA_ALPHA);

            _hwndMap[_blockerHwnd] = (this, BLOCKER);
        }
        else
        {
            MoveWindow(_blockerHwnd, x, y, width, height, false);
        }

        if (_visible)
            SetWindowPos(_blockerHwnd, HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    /// <summary>
    /// Removes the blocker overlay (e.g. when switching to Top/Bottom tab layout).
    /// </summary>
    public void ClearBlocker()
    {
        if (_blockerHwnd != IntPtr.Zero)
        {
            _hwndMap.Remove(_blockerHwnd);
            DestroyWindow(_blockerHwnd);
            _blockerHwnd = IntPtr.Zero;
        }
    }

    private static IntPtr GripWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (!_hwndMap.TryGetValue(hWnd, out var info))
            return DefWindowProc(hWnd, msg, wParam, lParam);

        // Blocker overlay: absorb mouse events with default cursor, no resize
        if (info.Edge == BLOCKER)
        {
            switch (msg)
            {
                case WM_SETCURSOR:
                    SetCursor(LoadCursor(IntPtr.Zero, IDC_ARROW));
                    return (IntPtr)1;

                case WM_LBUTTONDOWN:
                    return IntPtr.Zero;

                case WM_ERASEBKGND:
                    return (IntPtr)1;

                case WM_PAINT:
                    BeginPaint(hWnd, out var bps);
                    EndPaint(hWnd, ref bps);
                    return IntPtr.Zero;

                case WM_NCDESTROY:
                    _hwndMap.Remove(hWnd);
                    return DefWindowProc(hWnd, msg, wParam, lParam);
            }
            return DefWindowProc(hWnd, msg, wParam, lParam);
        }

        switch (msg)
        {
            case WM_SETCURSOR:
            {
                var dir = info.Helper.GetResizeDirection(hWnd, info.Edge);
                var cursor = GetCursorForDirection(dir);
                SetCursor(cursor);
                return (IntPtr)1;
            }

            case WM_LBUTTONDOWN:
            {
                var dir = info.Helper.GetResizeDirection(hWnd, info.Edge);
                if (dir > 0)
                {
                    ReleaseCapture();
                    SendMessage(info.Helper._parentHwnd, WM_SYSCOMMAND, (IntPtr)(SC_SIZE + dir), IntPtr.Zero);
                }
                return IntPtr.Zero;
            }

            case WM_ERASEBKGND:
                return (IntPtr)1;

            case WM_PAINT:
            {
                BeginPaint(hWnd, out var ps);
                EndPaint(hWnd, ref ps);
                return IntPtr.Zero;
            }

            case WM_NCDESTROY:
            {
                _hwndMap.Remove(hWnd);
                return DefWindowProc(hWnd, msg, wParam, lParam);
            }
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private int GetResizeDirection(IntPtr hWnd, int edge)
    {
        GetCursorPos(out var pt);
        ScreenToClient(hWnd, ref pt);
        GetClientRect(hWnd, out var rect);

        int w = rect.Right;
        int h = rect.Bottom;

        // SC_SIZE + direction values:
        // 1=Left, 2=Right, 3=Top, 4=TopLeft, 5=TopRight, 6=Bottom, 7=BottomLeft, 8=BottomRight
        return edge switch
        {
            LEFT => pt.Y < CornerSize ? 4 : pt.Y > h - CornerSize ? 7 : 1,
            RIGHT => pt.Y < CornerSize ? 5 : pt.Y > h - CornerSize ? 8 : 2,
            TOP => pt.X < CornerSize ? 4 : pt.X > w - CornerSize ? 5 : 3,
            BOTTOM => pt.X < CornerSize ? 7 : pt.X > w - CornerSize ? 8 : 6,
            _ => 0,
        };
    }

    private static IntPtr GetCursorForDirection(int direction)
    {
        return direction switch
        {
            1 or 2 => LoadCursor(IntPtr.Zero, IDC_SIZEWE),
            3 or 6 => LoadCursor(IntPtr.Zero, IDC_SIZENS),
            4 or 8 => LoadCursor(IntPtr.Zero, IDC_SIZENWSE),
            5 or 7 => LoadCursor(IntPtr.Zero, IDC_SIZENESW),
            _ => LoadCursor(IntPtr.Zero, IDC_ARROW),
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        for (int i = 0; i < 4; i++)
        {
            if (_gripHwnds[i] != IntPtr.Zero)
            {
                _hwndMap.Remove(_gripHwnds[i]);
                DestroyWindow(_gripHwnds[i]);
                _gripHwnds[i] = IntPtr.Zero;
            }
        }

        ClearBlocker();
    }
}
