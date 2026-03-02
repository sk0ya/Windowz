using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace WindowzTabManager;

public partial class MainWindow
{
    private void MainWindow_Activated(object? sender, EventArgs e)
    {
        if (_viewModel.IsCommandPaletteOpen)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
            {
                CommandPaletteControl.RequestSearchBoxFocus();
            });
            return;
        }

        if (_viewModel.IsWindowPickerOpen)
            return;

        UpdateManagedWindowLayout(activate: false);
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => UpdateManagedWindowLayout(activate: false));
    }

    private void MainWindow_Deactivated(object? sender, EventArgs e)
    {
        _activeManagedWindowHandle = IntPtr.Zero;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_MOUSEACTIVATE = 0x0021;
        const int WM_NCHITTEST = 0x0084;
        const int WM_NCPAINT   = 0x0085;
        const int WM_GETMINMAXINFO = 0x0024;
        const int HTCLIENT = 1;
        const int MA_ACTIVATE = 1;

        // When the window is not active and the user clicks a UI element (e.g. close
        // button), returning MA_ACTIVATE activates the window AND passes the click
        // through to WPF without consuming it — so the control responds on the very
        // first click.  We only override HTCLIENT; resize-border hits are left to
        // default so the resize drag behaves normally.
        if (msg == WM_MOUSEACTIVATE)
        {
            int hitTest = unchecked((short)(lParam.ToInt64() & 0xFFFF));
            if (hitTest == HTCLIENT)
            {
                handled = true;
                return (IntPtr)MA_ACTIVATE;
            }
        }

        // Suppress non-client area painting. Without this, events such as
        // positioning a managed (embedded) window cause Windows to repaint
        // the NC area with a white background.
        if (msg == WM_NCPAINT)
        {
            handled = true;
            return IntPtr.Zero;
        }

        if (msg == WM_NCHITTEST)
        {
            handled = true;
            var hit = HitTestResizeBorder(hwnd, lParam);
            return hit != IntPtr.Zero ? hit : (IntPtr)HTCLIENT;
        }

        if (msg == WM_GETMINMAXINFO)
        {
            WmGetMinMaxInfo(hwnd, lParam);
            handled = true;
        }

        return IntPtr.Zero;
    }

    private IntPtr HitTestResizeBorder(IntPtr hwnd, IntPtr lParam)
    {
        const int SideResizeBorderThicknessPx = 8;
        const int TopResizeBorderThicknessPx = 8;
        const int BottomResizeBorderThicknessPx = 8;
        const int HTLEFT = 10;
        const int HTRIGHT = 11;
        const int HTTOP = 12;
        const int HTTOPLEFT = 13;
        const int HTTOPRIGHT = 14;
        const int HTBOTTOM = 15;
        const int HTBOTTOMLEFT = 16;
        const int HTBOTTOMRIGHT = 17;

        if (WindowState == WindowState.Maximized)
            return IntPtr.Zero;

        if (!NativeMethods.GetWindowRect(hwnd, out var windowRect))
            return IntPtr.Zero;

        long lParamValue = lParam.ToInt64();
        int mouseX = unchecked((short)(lParamValue & 0xFFFF));
        int mouseY = unchecked((short)((lParamValue >> 16) & 0xFFFF));

        if (mouseX < windowRect.Left || mouseX > windowRect.Right ||
            mouseY < windowRect.Top || mouseY > windowRect.Bottom)
        {
            return IntPtr.Zero;
        }

        bool onLeft = mouseX - windowRect.Left <= SideResizeBorderThicknessPx;
        bool onRight = windowRect.Right - mouseX <= SideResizeBorderThicknessPx;
        bool onTop = TopResizeBorderThicknessPx > 0 &&
                     mouseY - windowRect.Top <= TopResizeBorderThicknessPx;
        bool onBottom = windowRect.Bottom - mouseY <= BottomResizeBorderThicknessPx;

        if (onTop && onLeft) return (IntPtr)HTTOPLEFT;
        if (onTop && onRight) return (IntPtr)HTTOPRIGHT;
        if (onBottom && onLeft) return (IntPtr)HTBOTTOMLEFT;
        if (onBottom && onRight) return (IntPtr)HTBOTTOMRIGHT;
        if (onLeft) return (IntPtr)HTLEFT;
        if (onRight) return (IntPtr)HTRIGHT;
        if (onTop) return (IntPtr)HTTOP;
        if (onBottom) return (IntPtr)HTBOTTOM;

        return IntPtr.Zero;
    }

    private static void WmGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
    {
        var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);

        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (monitor != IntPtr.Zero)
        {
            var monitorInfo = new MONITORINFO
            {
                cbSize = Marshal.SizeOf(typeof(MONITORINFO))
            };

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

    private const int MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

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
}
