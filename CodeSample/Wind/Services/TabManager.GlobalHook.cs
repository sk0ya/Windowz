using System.Runtime.InteropServices;
using Wind.Interop;
using Wind.Models;

namespace Wind.Services;

public partial class TabManager
{
    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(
        uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
        GlobalWinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    private delegate void GlobalWinEventDelegate(
        IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    private const uint EVENT_OBJECT_SHOW_G = 0x8002;
    private const uint WINEVENT_OUTOFCONTEXT_G = 0x0000;
    private const int OBJID_WINDOW_G = 0;
    private const long AutoEmbedSuppressionDurationMs = 1500;
    private const int AutoEmbedDelayMs = 1000;

    private static readonly HashSet<string> _globalSystemWindowClasses = new(StringComparer.Ordinal)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "Windows.UI.Core.CoreWindow",
        "#32768",           // Menu
        "#32769",           // Desktop
        "#32770",           // Dialog
        "#32771",           // Task switch
        "tooltips_class32", // Tooltip
        "SysShadow",        // Shadow
        "IME",              // IME window
        "MSCTFIME UI",      // IME UI
    };

    private IntPtr _globalWindowHook = IntPtr.Zero;
    private GlobalWinEventDelegate? _globalWinEventProc;
    private readonly Dictionary<IntPtr, long> _autoEmbedSuppressedUntil = new();

    private void SetupGlobalWindowHook()
    {
        if (_globalWindowHook != IntPtr.Zero) return;

        _globalWinEventProc = OnGlobalWindowShow;
        _globalWindowHook = SetWinEventHook(
            EVENT_OBJECT_SHOW_G, EVENT_OBJECT_SHOW_G,
            IntPtr.Zero, _globalWinEventProc, 0, 0, WINEVENT_OUTOFCONTEXT_G);
    }

    private void RemoveGlobalWindowHook()
    {
        if (_globalWindowHook == IntPtr.Zero) return;

        UnhookWinEvent(_globalWindowHook);
        _globalWindowHook = IntPtr.Zero;
        _globalWinEventProc = null;
    }

    private void OnGlobalWindowShow(
        IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (idObject != OBJID_WINDOW_G || hwnd == IntPtr.Zero) return;

        _dispatcher.BeginInvoke(() => QueueAutoEmbedAfterDelay(hwnd));
    }

    private async void QueueAutoEmbedAfterDelay(IntPtr hwnd)
    {
        await Task.Delay(AutoEmbedDelayMs);
        TryAutoEmbedWindow(hwnd);
    }

    private void TryAutoEmbedWindow(IntPtr hwnd)
    {
        if (!_settingsManager.Settings.AutoEmbedNewWindows) return;

        if (IsAutoEmbedSuppressed(hwnd)) return;

        if (_windowManager.IsEmbedded(hwnd)) return;

        if (Tabs.Any(t => t.Window?.Handle == hwnd)) return;

        if (!IsGloballyEmbeddableWindow(hwnd)) return;

        var windowInfo = WindowInfo.FromHandle(hwnd);
        if (windowInfo == null) return;

        if (_settingsManager.IsAutoEmbedExcluded(windowInfo.ExecutablePath)) return;

        AddTab(windowInfo, activate: true);
    }

    private void SuppressAutoEmbedForWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;

        _autoEmbedSuppressedUntil[hwnd] = Environment.TickCount64 + AutoEmbedSuppressionDurationMs;

        if (_autoEmbedSuppressedUntil.Count > 256)
        {
            CleanupExpiredAutoEmbedSuppressions();
        }
    }

    private bool IsAutoEmbedSuppressed(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        if (!_autoEmbedSuppressedUntil.TryGetValue(hwnd, out long suppressUntil)) return false;

        if (Environment.TickCount64 <= suppressUntil) return true;

        _autoEmbedSuppressedUntil.Remove(hwnd);
        return false;
    }

    private void CleanupExpiredAutoEmbedSuppressions()
    {
        if (_autoEmbedSuppressedUntil.Count == 0) return;

        long now = Environment.TickCount64;
        var expiredHandles = _autoEmbedSuppressedUntil
            .Where(pair => pair.Value < now)
            .Select(pair => pair.Key)
            .ToList();

        foreach (var handle in expiredHandles)
        {
            _autoEmbedSuppressedUntil.Remove(handle);
        }
    }

    private bool IsGloballyEmbeddableWindow(IntPtr hwnd)
    {
        if (!NativeMethods.IsWindowVisible(hwnd)) return false;

        int style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_STYLE);
        int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);

        if ((style & (int)NativeMethods.WS_CHILD) != 0) return false;
        if ((exStyle & (int)NativeMethods.WS_EX_TOOLWINDOW) != 0) return false;
        if ((exStyle & (int)NativeMethods.WS_EX_TOPMOST) != 0) return false;

        string title = NativeMethods.GetWindowTitle(hwnd);
        if (string.IsNullOrWhiteSpace(title)) return false;

        NativeMethods.GetWindowThreadProcessId(hwnd, out uint processId);
        if (processId == (uint)Environment.ProcessId) return false;

        string className = NativeMethods.GetWindowClassName(hwnd);
        if (_globalSystemWindowClasses.Contains(className)) return false;
        if (WindowClassFilters.IsUnsupportedForEmbedding(className)) return false;

        return true;
    }

    public void UpdateGlobalWindowHook()
    {
        if (_settingsManager.Settings.AutoEmbedNewWindows)
            SetupGlobalWindowHook();
        else
            RemoveGlobalWindowHook();
    }
}
