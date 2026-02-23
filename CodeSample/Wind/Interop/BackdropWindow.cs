using System.Runtime.InteropServices;
using System.Windows.Media;
using Microsoft.Win32;

namespace Wind.Interop;

/// <summary>
/// A native Win32 window that sits directly behind the main Wind window,
/// providing an opaque backdrop to work around Explorer's transparent
/// background when embedded.
/// </summary>
public class BackdropWindow
{
    private IntPtr _hwnd;
    private IntPtr _backgroundBrush;
    private static bool _classRegistered;
    private const string ClassName = "WindBackdropWindow";

    private static WndProcDelegate? _wndProcDelegate;
    private static BackdropWindow? _instance;

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(
        uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClass(ref WNDCLASS lpWndClass);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateSolidBrush(uint crColor);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy,
        uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern int FillRect(IntPtr hDC, ref RECT lprc, IntPtr hbr);

    [DllImport("gdi32.dll")]
    private static extern IntPtr GetStockObject(int fnObject);

    private const uint WS_POPUP = 0x80000000;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;
    private const uint WS_EX_NOACTIVATE = 0x08000000;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const int SW_HIDE = 0;
    private const int NULL_BRUSH = 5;
    private const uint WM_ERASEBKGND = 0x0014;

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

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    // Explorer dark mode: #191919, light mode: #FFFFFF
    private const uint ExplorerDarkColor = 0x191919; // BGR: 0x191919
    private const uint ExplorerLightColor = 0xFFFFFF; // BGR: 0xFFFFFF

    public BackdropWindow()
    {
        _instance = this;
        _backgroundBrush = CreateSolidBrush(GetExplorerBackgroundColor());
    }

    /// <summary>
    /// Returns the Explorer background COLORREF based on the Windows theme registry.
    /// </summary>
    public static uint GetExplorerBackgroundColor()
    {
        return IsAppDarkMode() ? ExplorerDarkColor : ExplorerLightColor;
    }

    /// <summary>
    /// Sets the backdrop color from a WPF Color.
    /// </summary>
    public void SetColor(Color color)
    {
        if (_backgroundBrush != IntPtr.Zero)
            DeleteObject(_backgroundBrush);

        uint colorRef = (uint) ((color.B << 16) | (color.G << 8) | color.R);
        _backgroundBrush = CreateSolidBrush(colorRef);

        if (_hwnd != IntPtr.Zero)
            InvalidateRect(_hwnd, IntPtr.Zero, true);
    }

    /// <summary>
    /// Sets the backdrop color to the Explorer background (from registry).
    /// </summary>
    public void SetExplorerColor()
    {
        if (_backgroundBrush != IntPtr.Zero)
            DeleteObject(_backgroundBrush);

        _backgroundBrush = CreateSolidBrush(GetExplorerBackgroundColor());

        if (_hwnd != IntPtr.Zero)
            InvalidateRect(_hwnd, IntPtr.Zero, true);
    }

    private static bool IsAppDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int i && i == 0;
        }
        catch
        {
            return true; // default to dark
        }
    }

    private void EnsureClassRegistered()
    {
        if (_classRegistered) return;

        _wndProcDelegate = WndProc;

        var wndClass = new WNDCLASS
        {
            style = 0,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
            cbClsExtra = 0,
            cbWndExtra = 0,
            hInstance = GetModuleHandle(null),
            hIcon = IntPtr.Zero,
            hCursor = IntPtr.Zero,
            hbrBackground = IntPtr.Zero, // We handle painting in WndProc
            lpszMenuName = null,
            lpszClassName = ClassName
        };

        RegisterClass(ref wndClass);
        _classRegistered = true;
    }

    private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_ERASEBKGND && _instance != null && _instance._backgroundBrush != IntPtr.Zero)
        {
            var hdc = wParam;
            GetClientRect(hWnd, out RECT rect);
            FillRect(hdc, ref rect, _instance._backgroundBrush);
            return new IntPtr(1);
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    /// <summary>
    /// Creates the backdrop window (hidden initially).
    /// </summary>
    public void Create()
    {
        if (_hwnd != IntPtr.Zero) return;

        EnsureClassRegistered();

        _hwnd = CreateWindowEx(
            WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
            ClassName,
            "WindBackdrop",
            WS_POPUP,
            0, 0, 1, 1,
            IntPtr.Zero,
            IntPtr.Zero,
            GetModuleHandle(null),
            IntPtr.Zero);
    }

    /// <summary>
    /// Shows the backdrop window directly behind the specified owner window.
    /// </summary>
    public void Show(IntPtr ownerHwnd, int x, int y, int width, int height)
    {
        if (_hwnd == IntPtr.Zero) return;

        SetWindowPos(_hwnd, ownerHwnd, x, y, width, height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    /// <summary>
    /// Hides the backdrop window.
    /// </summary>
    public void Hide()
    {
        if (_hwnd == IntPtr.Zero) return;

        ShowWindow(_hwnd, SW_HIDE);
    }

    /// <summary>
    /// Updates the position and size to match the owner window.
    /// </summary>
    public void UpdatePosition(IntPtr ownerHwnd, int x, int y, int width, int height)
    {
        if (_hwnd == IntPtr.Zero || !IsWindowVisible(_hwnd)) return;

        SetWindowPos(_hwnd, ownerHwnd, x + 1, y + 1, width - 1, height - 1, SWP_NOACTIVATE);
    }

    /// <summary>
    /// Destroys the backdrop window and frees resources.
    /// </summary>
    public void Destroy()
    {
        if (_hwnd != IntPtr.Zero)
        {
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }

        if (_backgroundBrush != IntPtr.Zero)
        {
            DeleteObject(_backgroundBrush);
            _backgroundBrush = IntPtr.Zero;
        }

        if (_instance == this)
            _instance = null;
    }

    public bool IsVisible => _hwnd != IntPtr.Zero && IsWindowVisible(_hwnd);
}