using System.Windows.Threading;
using WindowzTabManager.Models;

namespace WindowzTabManager.Services;

public partial class TabManager
{
    private const long AutoEmbedSuppressionDurationMs = 1500;

    private DispatcherTimer? _autoEmbedPollTimer;
    private readonly HashSet<IntPtr> _knownWindowHandles = new();
    private readonly Dictionary<IntPtr, long> _autoEmbedSuppressedUntil = new();

    private void SuppressAutoEmbedForWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return;

        _autoEmbedSuppressedUntil[hwnd] = Environment.TickCount64 + AutoEmbedSuppressionDurationMs;
    }

    private bool IsAutoEmbedSuppressed(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return false;

        if (!_autoEmbedSuppressedUntil.TryGetValue(hwnd, out long until))
            return false;

        if (Environment.TickCount64 <= until)
            return true;

        _autoEmbedSuppressedUntil.Remove(hwnd);
        return false;
    }

    private void CleanupExpiredAutoEmbedSuppressions()
    {
        if (_autoEmbedSuppressedUntil.Count == 0)
            return;

        long now = Environment.TickCount64;
        var expired = _autoEmbedSuppressedUntil
            .Where(pair => pair.Value < now)
            .Select(pair => pair.Key)
            .ToList();

        foreach (var handle in expired)
            _autoEmbedSuppressedUntil.Remove(handle);
    }

    private void OnAutoEmbedPollTick(object? sender, EventArgs e)
    {
        if (!_settingsManager.Settings.AutoEmbedNewWindows)
            return;

        CleanupExpiredAutoEmbedSuppressions();

        // ほとんどのティックでは既知ウィンドウのみのため、まず軽量スキャンで判定し、
        // 本当に新規のウィンドウだけをフル解決する（アイコン抽出等を毎回全件で行わない）。
        var windows = _windowManager.EnumerateWindows(resolveIconAndElevation: false);
        var visibleHandles = new HashSet<IntPtr>(windows.Select(w => w.Handle));

        foreach (var tab in Tabs)
        {
            if (tab.Window?.Handle is IntPtr handle && handle != IntPtr.Zero)
                visibleHandles.Add(handle);
        }

        _knownWindowHandles.RemoveWhere(h => !visibleHandles.Contains(h));

        foreach (var window in windows)
        {
            IntPtr handle = window.Handle;
            if (handle == IntPtr.Zero)
                continue;

            if (_knownWindowHandles.Contains(handle))
                continue;

            _knownWindowHandles.Add(handle);

            if (IsAutoEmbedSuppressed(handle))
                continue;

            if (_settingsManager.IsAutoEmbedExcluded(window.ExecutablePath))
                continue;

            var fullWindow = WindowInfo.WithResolvedDisplayInfo(window);
            AddTab(fullWindow, activate: true);
        }
    }

    private void StartAutoEmbedPolling()
    {
        if (_autoEmbedPollTimer != null)
            return;

        _knownWindowHandles.Clear();
        foreach (var window in _windowManager.EnumerateWindows(resolveIconAndElevation: false))
            _knownWindowHandles.Add(window.Handle);
        foreach (var tab in Tabs)
        {
            if (tab.Window?.Handle is IntPtr handle && handle != IntPtr.Zero)
                _knownWindowHandles.Add(handle);
        }

        _autoEmbedPollTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(800)
        };
        _autoEmbedPollTimer.Tick += OnAutoEmbedPollTick;
        _autoEmbedPollTimer.Start();
    }

    private void StopAutoEmbedPolling()
    {
        if (_autoEmbedPollTimer == null)
            return;

        _autoEmbedPollTimer.Stop();
        _autoEmbedPollTimer.Tick -= OnAutoEmbedPollTick;
        _autoEmbedPollTimer = null;
        _knownWindowHandles.Clear();
    }

    public void UpdateGlobalWindowHook()
    {
        if (_settingsManager.Settings.AutoEmbedNewWindows)
            StartAutoEmbedPolling();
        else
            StopAutoEmbedPolling();
    }
}
