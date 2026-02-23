using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Wind.Interop;

namespace Wind.Services;

/// <summary>
/// Persists embedded process info to disk so that zombie processes
/// (left behind when Wind is force-killed) can be cleaned up on next startup.
/// </summary>
public class ProcessTracker
{
    private readonly string _filePath;
    private readonly object _lock = new();
    private List<TrackedProcess> _tracked = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public ProcessTracker()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Wind");
        Directory.CreateDirectory(appDataPath);
        _filePath = Path.Combine(appDataPath, "tracked_processes.json");

        Load();
    }

    public void Add(int processId)
    {
        if (processId == 0) return;

        DateTime startTime;
        string processName;
        try
        {
            using var proc = Process.GetProcessById(processId);
            startTime = proc.StartTime;
            processName = proc.ProcessName;
        }
        catch
        {
            return;
        }

        lock (_lock)
        {
            if (_tracked.Any(t => t.ProcessId == processId && t.StartTime == startTime))
                return;

            _tracked.Add(new TrackedProcess
            {
                ProcessId = processId,
                ProcessName = processName,
                StartTime = startTime,
            });
            Save();
        }
    }

    public void Remove(int processId)
    {
        if (processId == 0) return;

        lock (_lock)
        {
            int removed = _tracked.RemoveAll(t => t.ProcessId == processId);
            if (removed > 0) Save();
        }
    }

    /// <summary>
    /// Clears the tracking file completely (called during normal shutdown after all
    /// processes have been closed or released).
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _tracked.Clear();
            Save();
        }
    }

    /// <summary>
    /// Kills zombie processes left over from a previous Wind session.
    /// A process is considered a zombie if:
    ///   1. It still exists with the same PID and start time (not a reused PID).
    ///   2. It has no visible top-level window.
    /// </summary>
    public void CleanupZombies()
    {
        List<TrackedProcess> stale;
        lock (_lock)
        {
            stale = new List<TrackedProcess>(_tracked);
        }

        if (stale.Count == 0) return;

        foreach (var entry in stale)
        {
            try
            {
                using var proc = Process.GetProcessById(entry.ProcessId);

                // Guard against PID reuse: verify process start time matches.
                if (Math.Abs((proc.StartTime - entry.StartTime).TotalSeconds) > 2)
                    continue;

                if (proc.HasExited)
                    continue;

                // Check if the process has any visible top-level window.
                if (!HasVisibleWindow(entry.ProcessId))
                {
                    proc.Kill();
                }
            }
            catch
            {
                // Process no longer exists — nothing to do.
            }
        }

        // Previous session's data is now handled; clear the file.
        Clear();
    }

    private static bool HasVisibleWindow(int processId)
    {
        bool found = false;
        NativeMethods.EnumWindows((hWnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hWnd))
                return true; // continue

            NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
            if ((int)pid != processId)
                return true; // continue

            // Skip tool windows (invisible helper windows)
            int exStyle = NativeMethods.GetWindowLong(hWnd, NativeMethods.GWL_EXSTYLE);
            if ((exStyle & (int)NativeMethods.WS_EX_TOOLWINDOW) != 0)
                return true; // continue

            found = true;
            return false; // stop enumeration
        }, IntPtr.Zero);

        return found;
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                _tracked = JsonSerializer.Deserialize<List<TrackedProcess>>(json, JsonOptions)
                           ?? new List<TrackedProcess>();
            }
        }
        catch
        {
            _tracked = new List<TrackedProcess>();
        }
    }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_tracked, JsonOptions);
            File.WriteAllText(_filePath, json);
        }
        catch
        {
            // Best-effort persistence
        }
    }

    private class TrackedProcess
    {
        public int ProcessId { get; set; }
        public string ProcessName { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
    }
}
