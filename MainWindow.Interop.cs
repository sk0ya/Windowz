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
        const int WM_GETMINMAXINFO = 0x0024;

        if (msg == WM_GETMINMAXINFO)
        {
            WmGetMinMaxInfo(hwnd, lParam);
            handled = true;
        }

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

    private void SuppressBorder()
    {
        // Windowz behavior: no extra native border suppression.
    }

    private void UpdateBackdropVisibility()
    {
        // Windowz behavior: no backdrop helper window.
    }

    private void UpdateBackdropPosition()
    {
        // Windowz behavior: no backdrop helper window.
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
