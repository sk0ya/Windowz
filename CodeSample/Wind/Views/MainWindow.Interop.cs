using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Wind.Interop;
using Wind.ViewModels;

namespace Wind.Views;

public partial class MainWindow
{
    private void MainWindow_Activated(object? sender, EventArgs e)
    {
        // Re-establish backdrop Z-order directly behind Wind
        UpdateBackdropPosition();

        // If command palette is open, keep focus in its search box.
        if (_viewModel.IsCommandPaletteOpen)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
            {
                CommandPaletteControl.RequestSearchBoxFocus();
            });
            return;
        }

        // When Wind window is activated, forward focus to the embedded window
        // only if the mouse is over the content area (not the tab bar).
        if (_viewModel.IsWindowPickerOpen) return;

        UpdateManagedWindowLayout(activate: false);
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => UpdateManagedWindowLayout(activate: false));

        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (_currentHost == null) return;

            var pos = Mouse.GetPosition(this);
            var contentPos = ContentPanel.TranslatePoint(new Point(0, 0), this);
            var contentRect = new Rect(contentPos, new Size(ContentPanel.ActualWidth, ContentPanel.ActualHeight));

            if (contentRect.Contains(pos))
            {
                _currentHost.FocusHostedWindow();
            }
        });
    }

    private void MainWindow_Deactivated(object? sender, EventArgs e)
    {
        // Force re-foreground on the next activation so externally managed windows
        // (notably WinUI3) receive a fresh present path instead of stale black frames.
        _activeManagedWindowHandle = IntPtr.Zero;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_ERASEBKGND = 0x0014;
        const int WM_GETMINMAXINFO = 0x0024;
        const int WM_NCCALCSIZE = 0x0083;
        const int WM_NCPAINT = 0x0085;
        const int WM_NCACTIVATE = 0x0086;
        const int WM_STYLECHANGING = 0x007C;

        switch (msg)
        {
            case WM_NCCALCSIZE:
                // Return 0 to make the entire window client area (no non-client border)
                handled = true;
                return IntPtr.Zero;

            case WM_NCPAINT:
                // Suppress non-client area painting entirely
                handled = true;
                return IntPtr.Zero;

            case WM_NCACTIVATE:
                // Suppress non-client area repaint on activate/deactivate to prevent border flash
                handled = true;
                return (IntPtr)1;

            case WM_STYLECHANGING:
                // Intercept style changes and strip border-related styles before they're applied.
                // WPF/HwndHost can add WS_THICKFRAME or similar styles when embedding child HWNDs.
                if (wParam.ToInt32() == NativeMethods.GWL_STYLE)
                {
                    var styleStruct = Marshal.PtrToStructure<STYLESTRUCT>(lParam);
                    int cleaned = styleStruct.styleNew & ~(int)(
                        NativeMethods.WS_THICKFRAME | NativeMethods.WS_BORDER |
                        NativeMethods.WS_DLGFRAME | NativeMethods.WS_CAPTION);
                    if (cleaned != styleStruct.styleNew)
                    {
                        styleStruct.styleNew = cleaned;
                        Marshal.StructureToPtr(styleStruct, lParam, false);
                    }
                }
                break;

            case WM_GETMINMAXINFO:
                // Adjust maximize size to respect taskbar
                WmGetMinMaxInfo(hwnd, lParam);
                handled = true;
                break;

            case WM_ERASEBKGND:
                // Prevent black background flicker during resize
                handled = true;
                return (IntPtr)1;
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// Forces a frame recalculation on Wind's window, triggering WM_NCCALCSIZE
    /// to re-eliminate the non-client border.
    /// </summary>
    private static void RefreshWindowFrame(IntPtr hwnd)
    {
        const uint SWP_NOMOVE = 0x0002;
        const uint SWP_NOSIZE = 0x0001;
        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_FRAMECHANGED);
    }

    /// <summary>
    /// Re-applies all border suppression measures. Must be called after operations
    /// that can reset the window frame, such as embedding child HWNDs via HwndHost.
    /// </summary>
    private void SuppressBorder()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        // Strip border-related styles that WPF or Windows may have added
        int style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_STYLE);
        int cleanStyle = style & ~(int)(
            NativeMethods.WS_THICKFRAME | NativeMethods.WS_BORDER |
            NativeMethods.WS_DLGFRAME | NativeMethods.WS_CAPTION);
        if (cleanStyle != style)
            NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_STYLE, cleanStyle);

        // Force frame recalculation (triggers WM_NCCALCSIZE → returns 0)
        RefreshWindowFrame(hwnd);

        // Tell DWM to not draw the 1px accent border at all.
        // Style stripping alone is unreliable because WPF/HwndHost can re-add
        // WS_THICKFRAME faster than SuppressBorder can strip it, leaving the
        // DWM compositor in an inconsistent state.
        HideDwmBorder(hwnd);
    }

    private static void HideDwmBorder(IntPtr hwnd)
    {
        const int DWMWA_BORDER_COLOR = 34;
        int colorNone = unchecked((int)0xFFFFFFFE); // DWMWA_COLOR_NONE
        DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref colorNone, sizeof(int));
    }

    private static void WmGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
    {
        var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);

        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (monitor != IntPtr.Zero)
        {
            var monitorInfo = new MONITORINFO();
            monitorInfo.cbSize = Marshal.SizeOf(typeof(MONITORINFO));

            if (GetMonitorInfo(monitor, ref monitorInfo))
            {
                var workArea = monitorInfo.rcWork;
                var monitorArea = monitorInfo.rcMonitor;

                mmi.ptMaxPosition.X = workArea.Left - monitorArea.Left;
                mmi.ptMaxPosition.Y = workArea.Top - monitorArea.Top;
                mmi.ptMaxSize.X = workArea.Right - workArea.Left;
                mmi.ptMaxSize.Y = workArea.Bottom - workArea.Top;
            }
        }

        Marshal.StructureToPtr(mmi, lParam, true);
    }

    #region Backdrop

    private bool IsCurrentTabExplorer()
    {
        var tab = _viewModel.SelectedTab;
        return tab is { IsContentTab: false, Window.IsExplorer: true };
    }

    private void UpdateBackdropVisibility()
    {
        if (_viewModel.IsWindowPickerOpen || _viewModel.IsCommandPaletteOpen
            || WindowState == WindowState.Minimized)
        {
            _backdropWindow.Hide();
            return;
        }

        // Always show backdrop; switch color based on Explorer vs other tabs
        if (IsCurrentTabExplorer())
        {
            _backdropWindow.SetExplorerColor();
        }
        else
        {
            var bgBrush = FindResource("ApplicationBackgroundBrush") as SolidColorBrush;
            if (bgBrush != null)
                _backdropWindow.SetColor(bgBrush.Color);
        }

        ShowBackdrop();
    }

    private void ShowBackdrop()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        NativeMethods.GetWindowRect(hwnd, out var rect);
        _backdropWindow.Show(hwnd, rect.Left, rect.Top, rect.Width, rect.Height);
    }

    private void UpdateBackdropPosition()
    {
        if (!_backdropWindow.IsVisible) return;

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        NativeMethods.GetWindowRect(hwnd, out var rect);
        _backdropWindow.UpdatePosition(hwnd, rect.Left, rect.Top, rect.Width, rect.Height);
    }

    #endregion

    #region Win32 Interop for Maximize

    private const int MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    static extern IntPtr DefWindowProc(
        IntPtr hWnd,
        int msg,
        IntPtr wParam,
        IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STYLESTRUCT
    {
        public int styleOld;
        public int styleNew;
    }

    #endregion

    #region DWM Interop

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    #endregion
}
