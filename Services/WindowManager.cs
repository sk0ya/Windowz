using System.Collections.ObjectModel;
using System.Diagnostics;
using WindowzTabManager.Models;

namespace WindowzTabManager.Services;

public class WindowManager
{
    private readonly HashSet<IntPtr> _excludedHandles;
    private readonly Dictionary<IntPtr, ManagedWindowState> _managedWindowStates = new();
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
            WasMaximized = NativeMethods.IsZoomed(handle)
        };

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
        IntPtr windWindowHandle = default)
    {
        if (!_managedWindowStates.ContainsKey(handle)) return;
        if (!NativeMethods.IsWindow(handle))
        {
            _managedWindowStates.Remove(handle);
            return;
        }

        width = Math.Max(1, width);
        height = Math.Max(1, height);

        if (NativeMethods.IsIconic(handle))
        {
            NativeMethods.ShowWindow(handle, NativeMethods.SW_RESTORE);
        }
        else
        {
            NativeMethods.ShowWindow(handle, NativeMethods.SW_SHOW);
        }

        bool positioned = NativeMethods.SetWindowPos(
            handle,
            NativeMethods.HWND_TOP,
            x,
            y,
            width,
            height,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);

        if (!positioned)
        {
            NativeMethods.MoveWindow(handle, x, y, width, height, true);
        }

        if (windWindowHandle != IntPtr.Zero &&
            windWindowHandle != handle &&
            NativeMethods.IsWindow(windWindowHandle))
        {
            // Keep Wind directly behind the selected managed window so the managed app
            // stays visible above Wind's content area.
            // SWP_NOREDRAW suppresses the Win32 repaint sequence (NCPAINT/ERASEBKGND/PAINT)
            // that would briefly show a white border on Wind before WPF can repaint.
            // DWM compositing updates independently so the visual result is unaffected.
            NativeMethods.SetWindowPos(
                windWindowHandle,
                handle,
                0,
                0,
                0,
                0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE |
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOREDRAW);
        }

        if (bringToFront)
        {
            NativeMethods.ForceForegroundWindow(handle);
        }
    }

    public void MinimizeManagedWindow(IntPtr handle)
    {
        if (!_managedWindowStates.ContainsKey(handle)) return;
        if (!NativeMethods.IsWindow(handle))
        {
            _managedWindowStates.Remove(handle);
            return;
        }

        NativeMethods.ShowWindow(handle, NativeMethods.SW_MINIMIZE);
    }

    public void MinimizeAllManagedWindowsExcept(IntPtr handleToKeep)
    {
        foreach (var handle in _managedWindowStates.Keys.ToList())
        {
            if (handle == handleToKeep) continue;
            MinimizeManagedWindow(handle);
        }
    }

    public void ReleaseManagedWindow(IntPtr handle)
    {
        if (!_managedWindowStates.TryGetValue(handle, out var state))
            return;

        _managedWindowStates.Remove(handle);

        if (!NativeMethods.IsWindow(handle))
            return;

        // Managed tabs are often minimized while inactive. Restore once first so
        // position/state restoration works reliably for shell windows (e.g. Explorer).
        if (NativeMethods.IsIconic(handle) || NativeMethods.IsZoomed(handle))
        {
            NativeMethods.ShowWindow(handle, NativeMethods.SW_RESTORE);
        }

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

    public void ForgetManagedWindow(IntPtr handle)
    {
        _managedWindowStates.Remove(handle);
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

    public void BringToFront(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return;
        NativeMethods.SetForegroundWindow(handle);
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
    }
}
