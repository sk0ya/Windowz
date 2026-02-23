using System.Diagnostics;
using WindowzTabManager.Models;

namespace WindowzTabManager.Services;

public partial class TabManager
{
    /// <summary>
    /// Timeout in milliseconds to wait for embedded processes to exit after sending WM_CLOSE.
    /// </summary>
    private const int ForceKillTimeoutMs = 3000;

    public void CleanupInvalidTabs()
    {
        var invalidTabs = Tabs.Where(t =>
            !t.IsContentTab &&
            !t.IsWebTab &&
            (t.Window?.Handle == IntPtr.Zero ||
            !_windowManager.IsWindowValid(t.Window!.Handle))).ToList();

        foreach (var tab in invalidTabs)
        {
            // ウィンドウは既に無効なので ReleaseWindow せずに追跡だけ解除する
            OnHostedWindowClosed(tab);
        }
    }

    public void StopCleanupTimer()
    {
        _cleanupTimer.Stop();
    }

    public void CloseStartupTabs()
    {
        var processIdsToKill = new List<int>();

        foreach (var tab in Tabs.ToList())
        {
            if (tab.IsContentTab) continue;
            if (tab.IsWebTab) { RemoveWebTabControl(tab.Id); continue; }

            if (_externallyManagedWindows.TryGetValue(tab.Id, out var managedHandle))
            {
                if (tab.IsLaunchedAtStartup)
                {
                    var window = tab.Window;
                    if (window != null && window.Handle != IntPtr.Zero)
                    {
                        if (window.ProcessId != 0)
                            processIdsToKill.Add(window.ProcessId);
                        NativeMethods.PostMessage(window.Handle, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                    }
                }
                else
                {
                    SuppressAutoEmbedForWindow(managedHandle);
                    _windowManager.ReleaseManagedWindow(managedHandle);
                }

                _externallyManagedWindows.Remove(tab.Id);
            }
        }
        Tabs.Clear();
        ActiveTab = null;

        ForceKillRemainingProcesses(processIdsToKill);
        _processTracker.Clear();
    }

    /// <summary>
    /// Returns (Handle, ProcessId) pairs for all managed embedded apps that should be closed.
    /// </summary>
    public List<(IntPtr Handle, int ProcessId)> GetManagedAppsWithHandles(bool startupOnly)
    {
        var result = new List<(IntPtr Handle, int ProcessId)>();
        foreach (var tab in Tabs.ToList())
        {
            if (tab.IsContentTab || tab.IsWebTab) continue;
            if (startupOnly && !tab.IsLaunchedAtStartup) continue;
            var window = tab.Window;

            if (_externallyManagedWindows.ContainsKey(tab.Id) &&
                     window != null &&
                     window.Handle != IntPtr.Zero &&
                     window.ProcessId != 0)
            {
                result.Add((window.Handle, window.ProcessId));
            }
        }
        return result;
    }

    public void CloseAllTabs()
    {
        var processIdsToKill = new List<int>();

        foreach (var tab in Tabs.ToList())
        {
            if (tab.IsContentTab) continue;
            if (tab.IsWebTab) { RemoveWebTabControl(tab.Id); continue; }

            if (_externallyManagedWindows.TryGetValue(tab.Id, out var managedHandle))
            {
                var window = tab.Window;
                if (window != null && window.Handle != IntPtr.Zero)
                {
                    if (window.ProcessId != 0)
                        processIdsToKill.Add(window.ProcessId);
                    NativeMethods.PostMessage(window.Handle, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                }

                _windowManager.ForgetManagedWindow(managedHandle);
                _externallyManagedWindows.Remove(tab.Id);
            }
        }
        Tabs.Clear();
        ActiveTab = null;

        ForceKillRemainingProcesses(processIdsToKill);
        _processTracker.Clear();
    }

    public void ReleaseAllTabs()
    {
        // Release managed windows before clearing tab collection.
        foreach (var tab in Tabs.ToList())
        {
            if (tab.IsContentTab) continue;
            if (tab.IsWebTab) { RemoveWebTabControl(tab.Id); continue; }

            if (_externallyManagedWindows.TryGetValue(tab.Id, out var managedHandle))
            {
                SuppressAutoEmbedForWindow(managedHandle);
                _windowManager.ReleaseManagedWindow(managedHandle);
                _externallyManagedWindows.Remove(tab.Id);
            }
        }

        // Now safe to clear the collection and update UI
        Tabs.Clear();
        ActiveTab = null;

        _processTracker.Clear();
    }

    /// <summary>
    /// Returns the process IDs of all currently hosted windows.
    /// Used by the abnormal exit handler to force-kill orphaned processes.
    /// </summary>
    public List<int> GetTrackedProcessIds()
    {
        return Tabs
            .Where(t => !t.IsContentTab && !t.IsWebTab)
            .Select(t => t.Window?.ProcessId ?? 0)
            .Where(pid => pid != 0)
            .Distinct()
            .ToList();
    }

    private static void ForceKillRemainingProcesses(List<int> processIds)
    {
        if (processIds.Count == 0) return;

        // Wait for processes to exit gracefully
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ForceKillTimeoutMs)
        {
            bool allExited = true;
            foreach (var pid in processIds)
            {
                try
                {
                    using var proc = Process.GetProcessById(pid);
                    if (!proc.HasExited) { allExited = false; break; }
                }
                catch
                {
                    // Process already exited
                }
            }

            if (allExited) return;
            Thread.Sleep(100);
        }

        // Force kill any remaining processes
        foreach (var pid in processIds)
        {
            try
            {
                using var proc = Process.GetProcessById(pid);
                if (!proc.HasExited)
                {
                    proc.Kill();
                }
            }
            catch
            {
                // Process already exited or access denied
            }
        }
    }

}
