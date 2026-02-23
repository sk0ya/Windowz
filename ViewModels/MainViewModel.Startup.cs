using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using WindowzTabManager.Models;
using WindowzTabManager.Services;

namespace WindowzTabManager.ViewModels;

public partial class MainViewModel
{
    public async Task EmbedStartupProcessesAsync(
        List<(Process Process, StartupApplication Config)> processConfigs,
        AppSettings settings,
        HashSet<IntPtr>? preExistingWindows = null)
    {
        if (processConfigs.Count == 0) return;

        // Wait for windows to be created
        await Task.Delay(1500);

        // Embed each process and track the mapping from config to tab
        var configTabPairs = new List<(StartupApplication Config, TabItem Tab)>();

        foreach (var (process, config) in processConfigs)
        {
            try
            {
                var windowInfo = await FindStartupWindowAsync(process, config, preExistingWindows);

                if (windowInfo != null)
                {
                    var tab = _tabManager.AddTab(windowInfo, activate: false);
                    if (tab != null)
                    {
                        tab.IsLaunchedAtStartup = true;
                        StatusMessage = $"Added: {tab.Title}";
                        configTabPairs.Add((config, tab));
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to embed process: {ex.Message}");
            }
        }

        // Apply groups from settings
        ApplyStartupGroups(configTabPairs, settings);

        // Apply tile layout from settings
        ApplyStartupTile(configTabPairs);

        // Activate the correct tab now that all tabs, groups, and tiles are set up.
        // During the loop above, tabs were added without activation to avoid
        // rapid ActiveTab changes that cause display inconsistencies
        // (each change triggers Dispatcher.BeginInvoke for UpdateWindowHost,
        // but only the last one's window actually gets embedded via BuildWindowCore).
        if (IsTiled)
        {
            // If a tile layout is active, select the first tiled tab
            // so OnActiveTabChanged shows the tile view.
            var firstTiledTab = _tabManager.Tabs.FirstOrDefault(t => t.IsTiled);
            if (firstTiledTab != null)
                _tabManager.ActiveTab = firstTiledTab;
        }
        else if (_tabManager.Tabs.Count > 0)
        {
            _tabManager.ActiveTab = _tabManager.Tabs.Last();
        }
    }

    private void ApplyStartupGroups(
        List<(StartupApplication Config, TabItem Tab)> configTabPairs,
        AppSettings settings)
    {
        // Build a lookup of group definitions
        var groupDefs = settings.StartupGroups
            .Where(g => !string.IsNullOrEmpty(g.Name))
            .ToDictionary(g => g.Name, StringComparer.OrdinalIgnoreCase);

        // Collect which group names are actually used
        var usedGroupNames = configTabPairs
            .Where(p => !string.IsNullOrEmpty(p.Config.Group))
            .Select(p => p.Config.Group!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Create TabGroup objects for each used group name
        var createdGroups = new Dictionary<string, TabGroup>(StringComparer.OrdinalIgnoreCase);
        foreach (var groupName in usedGroupNames)
        {
            var color = Colors.CornflowerBlue;
            if (groupDefs.TryGetValue(groupName, out var def))
            {
                color = TryParseColor(def.Color) ?? Colors.CornflowerBlue;
            }

            var group = _tabManager.CreateGroup(groupName, color);
            createdGroups[groupName] = group;
        }

        // Assign tabs to groups
        foreach (var (config, tab) in configTabPairs)
        {
            if (!string.IsNullOrEmpty(config.Group) && createdGroups.TryGetValue(config.Group, out var group))
            {
                _tabManager.AddTabToGroup(tab, group);
            }
        }
    }

    private void ApplyStartupTile(List<(StartupApplication Config, TabItem Tab)> configTabPairs)
    {
        // Group tabs by tile name
        var tileGroups = configTabPairs
            .Where(p => !string.IsNullOrEmpty(p.Config.Tile))
            .GroupBy(p => p.Config.Tile!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() >= 2);

        foreach (var tileGroup in tileGroups)
        {
            // Sort by TilePosition, then by original order
            var orderedTabs = tileGroup
                .OrderBy(p => p.Config.TilePosition ?? int.MaxValue)
                .Select(p => p.Tab)
                .ToList();

            _tabManager.StartTile(orderedTabs);
            StatusMessage = $"Tiled {orderedTabs.Count} tabs";
            break; // Only one tile layout can be active at a time
        }
    }

    /// <summary>
    /// 設定の StartupApplications の順番通りにスタートアップタブを並び替える。
    /// </summary>
    public void ApplyStartupTabOrder(List<StartupApplication> startupApps)
    {
        var orderedTabs = new List<TabItem>();

        foreach (var app in startupApps)
        {
            TabItem? match;

            if (SettingsManager.IsUrl(app.Path))
            {
                // URL タブはURLで照合
                match = _tabManager.Tabs.FirstOrDefault(t =>
                    t.IsWebTab &&
                    t.IsLaunchedAtStartup &&
                    string.Equals(t.WebUrl, app.Path, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                // ウィンドウタブは実行ファイルパスで照合
                match = _tabManager.Tabs.FirstOrDefault(t =>
                    !t.IsContentTab && !t.IsWebTab &&
                    t.IsLaunchedAtStartup &&
                    string.Equals(t.Window?.ExecutablePath, app.Path, StringComparison.OrdinalIgnoreCase));
            }

            if (match != null && !orderedTabs.Contains(match))
                orderedTabs.Add(match);
        }

        // 設定順にタブを並び替える
        for (int i = 0; i < orderedTabs.Count; i++)
        {
            _tabManager.MoveTab(orderedTabs[i], i);
        }
    }

    private static bool IsExplorerPath(string path)
    {
        var fileName = Path.GetFileName(path);
        return fileName.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<WindowInfo?> FindStartupWindowAsync(
        Process process,
        StartupApplication config,
        HashSet<IntPtr>? preExistingWindows)
    {
        string? targetProcessName = TryGetProcessName(config.Path);
        IntPtr processMainWindowHandle = IntPtr.Zero;

        for (int i = 0; i < 40; i++)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Refresh();
                    if (process.MainWindowHandle != IntPtr.Zero)
                    {
                        processMainWindowHandle = process.MainWindowHandle;
                    }
                }
            }
            catch
            {
                // Ignore process query failures and fallback to window enumeration.
            }

            _windowManager.RefreshWindowList();
            var candidate = FindStartupCandidate(
                _windowManager.AvailableWindows,
                config.Path,
                targetProcessName,
                preExistingWindows,
                processMainWindowHandle,
                preferNewWindow: true);
            if (candidate != null)
            {
                preExistingWindows?.Add(candidate.Handle);
                return candidate;
            }

            await Task.Delay(250);
        }

        _windowManager.RefreshWindowList();
        var fallback = FindStartupCandidate(
            _windowManager.AvailableWindows,
            config.Path,
            targetProcessName,
            preExistingWindows,
            processMainWindowHandle,
            preferNewWindow: false);
        if (fallback != null)
        {
            preExistingWindows?.Add(fallback.Handle);
        }

        return fallback;
    }

    private static WindowInfo? FindStartupCandidate(
        IEnumerable<WindowInfo> windows,
        string configuredPath,
        string? targetProcessName,
        HashSet<IntPtr>? preExistingWindows,
        IntPtr processMainWindowHandle,
        bool preferNewWindow)
    {
        var candidates = windows.Where(w => w.Handle != IntPtr.Zero);

        if (processMainWindowHandle != IntPtr.Zero)
        {
            var byHandle = candidates.FirstOrDefault(w => w.Handle == processMainWindowHandle);
            if (byHandle != null)
                return byHandle;
        }

        if (preferNewWindow && preExistingWindows != null)
            candidates = candidates.Where(w => !preExistingWindows.Contains(w.Handle));

        var byPath = candidates.FirstOrDefault(w =>
            !string.IsNullOrWhiteSpace(w.ExecutablePath) &&
            PathEquals(w.ExecutablePath!, configuredPath));
        if (byPath != null)
            return byPath;

        if (!string.IsNullOrWhiteSpace(targetProcessName))
        {
            var byProcessName = candidates.FirstOrDefault(w =>
                string.Equals(w.ProcessName, targetProcessName, StringComparison.OrdinalIgnoreCase));
            if (byProcessName != null)
                return byProcessName;
        }

        if (IsExplorerPath(configuredPath))
        {
            var byExplorer = candidates.FirstOrDefault(w => w.IsExplorer);
            if (byExplorer != null)
                return byExplorer;
        }

        if (preferNewWindow)
            return candidates.FirstOrDefault();

        return null;
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
            var processName = Path.GetFileNameWithoutExtension(path);
            return string.IsNullOrWhiteSpace(processName) ? null : processName;
        }
        catch
        {
            return null;
        }
    }

    private static Color? TryParseColor(string colorString)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(colorString);
        }
        catch
        {
            return null;
        }
    }
}
