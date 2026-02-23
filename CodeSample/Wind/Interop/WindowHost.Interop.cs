using System.Runtime.InteropServices;
using System.Windows.Media;

namespace Wind.Interop;

public partial class WindowHost
{
    private const string HostClassName = "WindWindowHost";
    private static bool _classRegistered;
    private static IntPtr _backgroundBrush = IntPtr.Zero;

    private const int WM_PARENTNOTIFY = 0x0210;
    private const int WM_DESTROY = 0x0002;

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
    private static extern IntPtr GetStockObject(int fnObject);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateSolidBrush(uint crColor);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    private const int NULL_BRUSH = 5;

    [DllImport("user32.dll")]
    private static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern int FillRect(IntPtr hDC, ref RECT lprc, IntPtr hbr);

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    private static WndProcDelegate? _wndProcDelegate;

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
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private static readonly Dictionary<IntPtr, WindowHost> _instances = new();

    private static void EnsureClassRegistered()
    {
        if (_classRegistered) return;

        _wndProcDelegate = WndProc;

        // Create default background brush (black)
        if (_backgroundBrush == IntPtr.Zero)
        {
            _backgroundBrush = CreateSolidBrush(0x000000); // Black
        }

        var wndClass = new WNDCLASS
        {
            style = 0,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
            cbClsExtra = 0,
            cbWndExtra = 0,
            hInstance = GetModuleHandle(null),
            hIcon = IntPtr.Zero,
            hCursor = IntPtr.Zero,
            hbrBackground = _backgroundBrush,
            lpszMenuName = null,
            lpszClassName = HostClassName
        };

        RegisterClass(ref wndClass);
        _classRegistered = true;
    }

    private static void UpdateBackgroundBrush()
    {
        // This method will be called when background color changes
        // Since the window class is already registered, we need to handle
        // background painting in WndProc instead
    }

    private static WindowHost? GetWindowHost(IntPtr hWnd)
    {
        _instances.TryGetValue(hWnd, out var host);
        return host;
    }

    private static void FillBackground(IntPtr hdc, IntPtr hWnd, Color color)
    {
        GetClientRect(hWnd, out RECT rect);

        // Convert WPF Color to Win32 COLORREF (BGR format)
        uint colorRef = (uint)((color.B << 16) | (color.G << 8) | color.R);
        IntPtr brush = CreateSolidBrush(colorRef);

        if (brush != IntPtr.Zero)
        {
            FillRect(hdc, ref rect, brush);
            DeleteObject(brush);
        }
    }

    private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        const uint WM_ERASEBKGND = 0x0014;

        if (msg == WM_ERASEBKGND)
        {
            // Handle background erasing to fill with our background color
            var hdc = wParam;
            if (hdc != IntPtr.Zero)
            {
                // Get the window instance to access background color
                var windowHost = GetWindowHost(hWnd);
                if (windowHost != null)
                {
                    FillBackground(hdc, hWnd, windowHost._backgroundColor);
                    return new IntPtr(1); // Return non-zero to indicate we handled it
                }
            }
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

}
