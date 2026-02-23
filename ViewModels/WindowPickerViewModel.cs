using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Data;
using System.Windows.Threading;
using WindowzTabManager.Models;
using WindowzTabManager.Services;

namespace WindowzTabManager.ViewModels;

public partial class WindowPickerViewModel : ObservableObject
{
    private readonly WindowManager _windowManager;
    private readonly SettingsManager _settingsManager;
    private readonly ICollectionView _windowsView;
    private readonly ObservableCollection<WindowInfo> _availableWindows;
    private readonly DispatcherTimer _refreshTimer;
    private CancellationTokenSource? _launchCts;

    public ObservableCollection<WindowInfo> AvailableWindows => _availableWindows;

    public ICollectionView WindowsView => _windowsView;

    [ObservableProperty]
    private WindowInfo? _selectedWindow;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<QuickLaunchAppSetting> _quickLaunchApps = new();

    [ObservableProperty]
    private bool _hasQuickLaunchApps;

    [ObservableProperty]
    private bool _isLaunching;

    [ObservableProperty]
    private bool _canAddSelectedWindow;

    [ObservableProperty]
    private bool _isSelectedWindowElevated;

    public event EventHandler<WindowInfo>? WindowSelected;
    public event EventHandler? Cancelled;
    public event EventHandler<string>? WebTabRequested;
    public event EventHandler? QuickLaunchSettingsRequested;

    public WindowPickerViewModel(WindowManager windowManager, SettingsManager settingsManager)
    {
        _windowManager = windowManager;
        _settingsManager = settingsManager;
        _availableWindows = new ObservableCollection<WindowInfo>();
        _windowsView = CollectionViewSource.GetDefaultView(_availableWindows);
        _windowsView.Filter = FilterWindows;

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _refreshTimer.Tick += (s, e) => RefreshWindowList();
    }

    public void Start()
    {
        SearchText = string.Empty;
        SelectedWindow = null;
        RefreshWindowList();
        LoadQuickLaunchApps();
        _refreshTimer.Start();
    }

    private void LoadQuickLaunchApps()
    {
        QuickLaunchApps.Clear();
        foreach (var app in _settingsManager.Settings.QuickLaunchApps)
        {
            QuickLaunchApps.Add(app);
        }
        HasQuickLaunchApps = QuickLaunchApps.Count > 0;
    }

    public void Stop()
    {
        _refreshTimer.Stop();
        _launchCts?.Cancel();
        _launchCts = null;
        IsLaunching = false;
    }

    private void RefreshWindowList()
    {
        var currentSelection = SelectedWindow?.Handle;
        var windows = _windowManager.EnumerateWindows();

        // 追加されたウィンドウを追加
        foreach (var window in windows)
        {
            if (!_availableWindows.Any(w => w.Handle == window.Handle))
            {
                _availableWindows.Add(window);
            }
        }

        // 削除されたウィンドウを削除
        for (int i = _availableWindows.Count - 1; i >= 0; i--)
        {
            if (!windows.Any(w => w.Handle == _availableWindows[i].Handle))
            {
                _availableWindows.RemoveAt(i);
            }
        }

        // 選択を復元
        if (currentSelection != null)
        {
            SelectedWindow = _availableWindows.FirstOrDefault(w => w.Handle == currentSelection);
        }
    }

    partial void OnSelectedWindowChanged(WindowInfo? value)
    {
        CanAddSelectedWindow = value != null && !value.IsElevated;
        IsSelectedWindowElevated = value?.IsElevated == true;
    }

    partial void OnSearchTextChanged(string value)
    {
        _windowsView.Refresh();
    }

    private bool FilterWindows(object obj)
    {
        if (obj is not WindowInfo window) return false;
        if (string.IsNullOrWhiteSpace(SearchText)) return true;

        return window.Title.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
               window.ProcessName.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand]
    private void Select()
    {
        if (SelectedWindow != null && !SelectedWindow.IsElevated)
        {
            WindowSelected?.Invoke(this, SelectedWindow);
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        Cancelled?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void OpenQuickLaunchSettings()
    {
        _launchCts?.Cancel();
        IsLaunching = false;
        QuickLaunchSettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void SelectWindow(WindowInfo? window)
    {
        if (window != null && !window.IsElevated)
        {
            WindowSelected?.Invoke(this, window);
        }
    }

    private enum QuickLaunchType { Url, Folder, File, GuiApp, ConsoleApp, BatchScript }
    private const int ExplorerQuickLaunchEmbedDelayMs = 1000;

    private static bool IsExplorerLaunch(QuickLaunchAppSetting app, QuickLaunchType type)
    {
        if (type == QuickLaunchType.Folder)
            return true;

        var fileName = Path.GetFileName(app.Path);
        return fileName.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Resolve a bare command name (e.g. "code", "cmd") against PATH
    /// and return the full path if found, or null.
    /// </summary>
    private static string? ResolveFromPath(string command)
    {
        var extensions = new[] { ".exe", ".com", ".cmd", ".bat" };
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";

        foreach (var dir in pathVar.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var ext in extensions)
            {
                var candidate = Path.Combine(dir, command + ext);
                if (System.IO.File.Exists(candidate))
                    return candidate;
            }
        }
        return null;
    }

    /// <summary>
    /// Read the PE header Subsystem field to determine if an exe is a console application.
    /// Returns true for CUI (IMAGE_SUBSYSTEM_WINDOWS_CUI = 3),
    /// false for GUI or on any read error.
    /// </summary>
    private static bool IsConsoleSubsystem(string exePath)
    {
        try
        {
            using var fs = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(fs);

            if (fs.Length < 0x40) return false;
            fs.Seek(0x3C, SeekOrigin.Begin);
            var peOffset = reader.ReadInt32();

            // PE signature (4) + COFF header (20) + Subsystem is at offset 68 in optional header
            var subsystemOffset = peOffset + 4 + 20 + 68;
            if (fs.Length < subsystemOffset + 2) return false;
            fs.Seek(subsystemOffset, SeekOrigin.Begin);
            var subsystem = reader.ReadUInt16();

            return subsystem == 3;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Parse a .cmd/.bat script to find the first .exe it references,
    /// resolve %~dp0 relative to the script's directory, then check PE subsystem.
    /// Falls back to GuiApp if no exe reference is found.
    /// </summary>
    private static QuickLaunchType ClassifyScript(string scriptPath)
    {
        try
        {
            var scriptDir = Path.GetDirectoryName(scriptPath) ?? "";
            var content = System.IO.File.ReadAllText(scriptPath);

            var matches = Regex.Matches(
                content,
                @"[""']?([^""'\r\n]*?\.exe)\b",
                RegexOptions.IgnoreCase);

            foreach (Match m in matches)
            {
                var raw = m.Groups[1].Value.Trim();

                // Expand %~dp0 → script's own directory
                raw = Regex.Replace(
                    raw, @"%~dp0\\?", scriptDir + "\\",
                    RegexOptions.IgnoreCase);

                // Expand %dp0% → script's own directory (SET dp0=%~dp0 pattern)
                raw = Regex.Replace(
                    raw, @"%dp0%\\?", scriptDir + "\\",
                    RegexOptions.IgnoreCase);

                // Try as full path first
                if (Path.IsPathFullyQualified(raw) && System.IO.File.Exists(raw))
                    return IsConsoleSubsystem(raw) ? QuickLaunchType.ConsoleApp : QuickLaunchType.GuiApp;

                // Try resolving bare exe name (e.g. "node") from PATH
                var name = Path.GetFileNameWithoutExtension(raw);
                var resolved = ResolveFromPath(name);
                if (resolved != null)
                    return IsConsoleSubsystem(resolved) ? QuickLaunchType.ConsoleApp : QuickLaunchType.GuiApp;
            }
        }
        catch
        {
            // If we can't read/parse, fall through
        }

        // Default: assume GUI (suppress console flash)
        return QuickLaunchType.GuiApp;
    }

    private static QuickLaunchType DetectLaunchType(string path)
    {
        // URL: http(s)://...
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) &&
            (uri.Scheme == "http" || uri.Scheme == "https"))
            return QuickLaunchType.Url;

        // Full-path targets
        if (Path.IsPathFullyQualified(path))
        {
            if (Directory.Exists(path))
                return QuickLaunchType.Folder;

            if (System.IO.File.Exists(path))
            {
                var ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext is ".exe" or ".com")
                    return IsConsoleSubsystem(path) ? QuickLaunchType.ConsoleApp : QuickLaunchType.GuiApp;
                if (ext is ".bat")
                    return QuickLaunchType.BatchScript;
                if (ext is ".cmd")
                    return ClassifyScript(path);
                return QuickLaunchType.File;
            }

            return QuickLaunchType.GuiApp;
        }

        // Bare name (e.g. "code", "cmd") — resolve via PATH to determine type
        var resolved = ResolveFromPath(path);
        if (resolved != null)
        {
            var ext = Path.GetExtension(resolved).ToLowerInvariant();
            if (ext is ".bat")
                return QuickLaunchType.BatchScript;
            if (ext is ".cmd")
                return ClassifyScript(resolved);
            return IsConsoleSubsystem(resolved) ? QuickLaunchType.ConsoleApp : QuickLaunchType.GuiApp;
        }

        return QuickLaunchType.GuiApp;
    }

    private static ProcessStartInfo BuildStartInfo(QuickLaunchAppSetting app, QuickLaunchType type)
    {
        switch (type)
        {
            case QuickLaunchType.Url:
                return new ProcessStartInfo
                {
                    FileName = app.Path,
                    UseShellExecute = true
                };

            case QuickLaunchType.Folder:
                return new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = string.IsNullOrEmpty(app.Arguments)
                        ? $"\"{app.Path}\""
                        : $"{app.Arguments} \"{app.Path}\"",
                    UseShellExecute = true
                };

            case QuickLaunchType.File:
                return new ProcessStartInfo
                {
                    FileName = app.Path,
                    UseShellExecute = true
                };

            case QuickLaunchType.ConsoleApp:
                // CUI app — UseShellExecute so the OS allocates a console window
                return new ProcessStartInfo
                {
                    FileName = app.Path,
                    Arguments = app.Arguments,
                    UseShellExecute = true
                };

            case QuickLaunchType.BatchScript:
                // Batch script — launch with shell to show console window (no embedding)
                return new ProcessStartInfo
                {
                    FileName = app.Path,
                    Arguments = app.Arguments,
                    UseShellExecute = true
                };

            case QuickLaunchType.GuiApp:
            default:
                // GUI app — launch via cmd /c + CreateNoWindow to suppress console flash
                // for .cmd/.bat wrappers (e.g. code), and direct launch for .exe
                var resolved = ResolveFromPath(app.Path);
                var ext = resolved != null
                    ? Path.GetExtension(resolved).ToLowerInvariant()
                    : Path.GetExtension(app.Path).ToLowerInvariant();

                if (ext is ".cmd" or ".bat")
                {
                    var args = string.IsNullOrEmpty(app.Arguments)
                        ? $"/c \"{app.Path}\""
                        : $"/c \"{app.Path}\" {app.Arguments}";
                    return new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = args,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                }

                return new ProcessStartInfo
                {
                    FileName = app.Path,
                    Arguments = app.Arguments,
                    UseShellExecute = true
                };
        }
    }

    [RelayCommand]
    private async Task LaunchQuickApp(QuickLaunchAppSetting? app)
    {
        if (app == null) return;

        _launchCts?.Cancel();
        _launchCts = new CancellationTokenSource();
        var ct = _launchCts.Token;

        try
        {
            IsLaunching = true;

            var type = DetectLaunchType(app.Path);
            bool expectsExplorerWindow = IsExplorerLaunch(app, type);

            // URLs open as web tabs, not external processes
            if (type == QuickLaunchType.Url)
            {
                IsLaunching = false;
                WebTabRequested?.Invoke(this, app.Path);
                Cancelled?.Invoke(this, EventArgs.Empty);
                return;
            }

            // Snapshot existing window handles before launch
            var existingHandles = new HashSet<IntPtr>(
                _windowManager.EnumerateWindows().Select(w => w.Handle));

            var startInfo = BuildStartInfo(app, type);

            Process.Start(startInfo);

            // Batch scripts are never embedded — just launch and close picker
            if (type == QuickLaunchType.BatchScript)
            {
                IsLaunching = false;
                Cancelled?.Invoke(this, EventArgs.Empty);
                return;
            }

            // Poll for a new window that didn't exist before
            WindowInfo? newWindow = null;
            WindowInfo? firstDetectedWindow = null;
            for (int i = 0; i < 100; i++)
            {
                await Task.Delay(100, ct);
                var current = _windowManager.EnumerateWindows();
                if (expectsExplorerWindow)
                {
                    var candidates = current
                        .Where(w => !existingHandles.Contains(w.Handle))
                        .ToList();

                    firstDetectedWindow ??= candidates.FirstOrDefault();
                    newWindow = candidates.FirstOrDefault(w => w.IsExplorer);
                }
                else
                {
                    newWindow = current.FirstOrDefault(w =>
                        !existingHandles.Contains(w.Handle));
                }

                if (newWindow != null)
                    break;
            }

            IsLaunching = false;

            if (newWindow == null && expectsExplorerWindow)
                newWindow = firstDetectedWindow;

            if (newWindow == null)
                newWindow = FindQuickLaunchFallbackWindow(app.Path, expectsExplorerWindow, existingHandles);

            if (newWindow == null)
                return;

            if (app.ShouldEmbed)
            {
                if (expectsExplorerWindow)
                {
                    // Explorer can surface before initial composition has stabilized.
                    await Task.Delay(ExplorerQuickLaunchEmbedDelayMs, ct);
                }

                RefreshWindowList();
                var windowInfo = _availableWindows.FirstOrDefault(w => w.Handle == newWindow.Handle)
                    ?? WindowInfo.FromHandle(newWindow.Handle);
                if (windowInfo != null)
                {
                    WindowSelected?.Invoke(this, windowInfo);
                    // QuickLaunch embed flow should close picker after a successful selection.
                    Cancelled?.Invoke(this, EventArgs.Empty);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelled by user (e.g. Cancel button or new launch)
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to launch quick app {app.Name}: {ex.Message}");
        }
        finally
        {
            IsLaunching = false;
        }
    }

    private WindowInfo? FindQuickLaunchFallbackWindow(
        string launchPath,
        bool expectsExplorerWindow,
        HashSet<IntPtr> existingHandles)
    {
        var windows = _windowManager.EnumerateWindows();
        var newCandidates = windows.Where(w => !existingHandles.Contains(w.Handle)).ToList();
        string? targetProcessName = TryGetProcessName(launchPath);

        if (expectsExplorerWindow)
        {
            return newCandidates.FirstOrDefault(w => w.IsExplorer)
                ?? windows.FirstOrDefault(w => w.IsExplorer)
                ?? newCandidates.FirstOrDefault()
                ?? windows.FirstOrDefault();
        }

        var byPath = newCandidates.FirstOrDefault(w =>
            !string.IsNullOrWhiteSpace(w.ExecutablePath) &&
            PathEquals(w.ExecutablePath!, launchPath))
            ?? windows.FirstOrDefault(w =>
                !string.IsNullOrWhiteSpace(w.ExecutablePath) &&
                PathEquals(w.ExecutablePath!, launchPath));
        if (byPath != null)
            return byPath;

        if (!string.IsNullOrWhiteSpace(targetProcessName))
        {
            return newCandidates.FirstOrDefault(w =>
                       string.Equals(w.ProcessName, targetProcessName, StringComparison.OrdinalIgnoreCase))
                   ?? windows.FirstOrDefault(w =>
                       string.Equals(w.ProcessName, targetProcessName, StringComparison.OrdinalIgnoreCase));
        }

        return newCandidates.FirstOrDefault() ?? windows.FirstOrDefault();
    }

    private static bool PathEquals(string left, string right)
    {
        try
        {
            var fullLeft = Path.GetFullPath(left);
            var fullRight = Path.GetFullPath(right);
            return string.Equals(fullLeft, fullRight, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string? TryGetProcessName(string path)
    {
        try
        {
            var name = Path.GetFileNameWithoutExtension(path);
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch
        {
            return null;
        }
    }
}
