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
                _commandPaletteWindow?.RequestSearchBoxFocus();
            });
            return;
        }

        if (_viewModel.IsWindowPickerOpen)
            return;

        if (TryMinimizeWindowzFromTaskbarActivation())
            return;

        bool shouldPromoteManagedWindow =
            !_viewModel.IsContentTabActive &&
            !_viewModel.IsWebTabActive;

        if (shouldPromoteManagedWindow)
        {
            // Run after the activation input has settled so Windowz controls do
            // not lose their first click while still restoring the hosted app.
            Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            {
                if (IsActive &&
                    WindowState != WindowState.Minimized &&
                    !_suppressManagedWindowPromotion &&
                    !_viewModel.IsWindowPickerOpen &&
                    !_viewModel.IsCommandPaletteOpen &&
                    !_viewModel.IsContentTabActive &&
                    !_viewModel.IsWebTabActive)
                {
                    UpdateManagedWindowLayout(activate: true);
                }
            });
            return;
        }

        UpdateManagedWindowLayout(activate: false);
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

        // When the window is not active and the user clicks anywhere on it,
        // return MA_ACTIVATE so that Windows both activates Windowz AND passes
        // the click through to WPF on the very first click.  This covers all
        // hit-test zones including the top/corner resize borders (HTTOP,
        // HTTOPRIGHT, …) that overlap with the title-bar control buttons.
        // Resize drags are unaffected: the resize begins via WM_NCLBUTTONDOWN
        // after activation regardless of this return value.
        if (msg == WM_MOUSEACTIVATE)
        {
            handled = true;
            return (IntPtr)MA_ACTIVATE;
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

    private static IntPtr NormalizeRootWindowHandle(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return IntPtr.Zero;

        var root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
        return root == IntPtr.Zero ? hwnd : root;
    }

    private bool TryMinimizeWindowzFromTaskbarActivation()
    {
        if (WindowState == WindowState.Minimized ||
            _suppressManagedWindowPromotion ||
            _viewModel.IsCommandPaletteOpen ||
            _viewModel.IsWindowPickerOpen ||
            _viewModel.IsContentTabActive ||
            _viewModel.IsWebTabActive)
        {
            return false;
        }

        var currentManagedHandle = GetCurrentActiveManagedWindowHandle();
        if (currentManagedHandle == IntPtr.Zero)
            return false;

        // タスクバー系ウィンドウと Windowz 自プロセスを除外した最後のフォアグラウンドで比較する。
        // Shell_TrayWnd がタスクバークリック時に一瞬フォアグラウンドを取得するため、
        // _lastForegroundWindow を直接使うと常に Shell_TrayWnd となり判定が失敗する。
        // この変数は WM_ACTIVATE / OUTOFCONTEXT の到達順に依存しない。
        if (!IsInSameWindowGroup(_lastNonTaskbarForegroundWindow, currentManagedHandle))
            return false;

        if (!IsTaskbarPointerActivation())
            return false;

        Dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
        {
            if (WindowState != WindowState.Minimized)
                WindowState = WindowState.Minimized;
        });
        return true;
    }

    private static bool IsInSameWindowGroup(IntPtr hwnd, IntPtr managed)
    {
        if (hwnd == IntPtr.Zero || managed == IntPtr.Zero)
            return false;
        if (hwnd == managed)
            return true;

        var root1 = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
        var root2 = NativeMethods.GetAncestor(managed, NativeMethods.GA_ROOT);
        if (root1 == IntPtr.Zero) root1 = hwnd;
        if (root2 == IntPtr.Zero) root2 = managed;
        if (root1 == root2)
            return true;

        // ルートが異なっても同一プロセスのウィンドウ（ポップアップ等）は同グループとみなす
        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid1);
        NativeMethods.GetWindowThreadProcessId(managed, out uint pid2);
        return pid1 != 0 && pid2 != 0 && pid1 == pid2;
    }

    private IntPtr GetCurrentActiveManagedWindowHandle()
    {
        var selectedTab = _viewModel.SelectedTab;
        if (selectedTab == null ||
            !_viewModel.TryGetExternallyManagedWindowHandle(selectedTab, out var handle))
        {
            return IntPtr.Zero;
        }

        return handle;
    }

    private bool IsTaskbarPointerActivation()
    {
        if (!NativeMethods.GetCursorPos(out var cursorPos))
            return false;

        if (IsScreenPointInsideWindow(cursorPos.X, cursorPos.Y))
            return false;

        return IsTaskbarWindowAtScreenPoint(cursorPos.X, cursorPos.Y);
    }

    private bool IsTaskbarWindowAtScreenPoint(int screenX, int screenY)
    {
        var pointedWindow = NativeMethods.WindowFromPoint(new NativeMethods.POINT
        {
            X = screenX,
            Y = screenY
        });
        if (pointedWindow == IntPtr.Zero)
            return false;

        var root = NativeMethods.GetAncestor(pointedWindow, NativeMethods.GA_ROOT);
        if (root == IntPtr.Zero)
            root = pointedWindow;

        return IsTaskbarClassName(NativeMethods.GetWindowClassName(pointedWindow)) ||
               IsTaskbarClassName(NativeMethods.GetWindowClassName(root));
    }

    private static bool IsTaskbarClassName(string className)
    {
        return className is "Shell_TrayWnd" or
               "Shell_SecondaryTrayWnd" or
               "MSTaskListWClass" or
               "MSTaskSwWClass" or
               "TaskListThumbnailWnd";
    }

    private bool IsPointInsideElement(FrameworkElement element, Point windowPoint)
    {
        if (!element.IsVisible || element.ActualWidth <= 0 || element.ActualHeight <= 0)
            return false;

        var topLeft = element.TranslatePoint(new Point(0, 0), this);
        return windowPoint.X >= topLeft.X &&
               windowPoint.X < topLeft.X + element.ActualWidth &&
               windowPoint.Y >= topLeft.Y &&
               windowPoint.Y < topLeft.Y + element.ActualHeight;
    }

    private bool IsScreenPointInsideWindow(int screenX, int screenY)
    {
        if (!IsLoaded || ActualWidth <= 0 || ActualHeight <= 0)
            return false;

        try
        {
            var topLeft = PointToScreen(new Point(0, 0));
            return screenX >= topLeft.X && screenX < topLeft.X + ActualWidth &&
                   screenY >= topLeft.Y && screenY < topLeft.Y + ActualHeight;
        }
        catch
        {
            return false;
        }
    }

    private bool IsScreenPointInsideWindowControlButton(int screenX, int screenY)
    {
        if (!IsLoaded)
            return false;

        try
        {
            var windowPoint = PointFromScreen(new Point(screenX, screenY));
            FrameworkElement[] buttons =
            [
                AddWindowButton,
                MenuButton,
                MinimizeButton,
                MaximizeButton,
                CloseButton
            ];

            foreach (var button in buttons)
            {
                if (IsPointInsideElement(button, windowPoint))
                    return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
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

        // Keep title-bar buttons clickable even when they overlap the custom
        // resize border.
        if (IsScreenPointInsideWindowControlButton(mouseX, mouseY))
            return IntPtr.Zero;

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
