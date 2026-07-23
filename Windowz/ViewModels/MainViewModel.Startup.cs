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
        List<StartupApplication> urlConfigs,
        AppSettings settings,
        HashSet<IntPtr>? preExistingWindows = null)
    {
        var configTabPairs = new List<(StartupApplication Config, TabItem Tab)>();
        if (processConfigs.Count == 0 && urlConfigs.Count == 0)
            return;

        if (processConfigs.Count > 0)
        {
            // プロセスが起動するまで少し待つ（固定1500ms → 200ms に短縮し、
            // 残りはポーリングループで柔軟に対応する）
            await Task.Delay(200);

            // 全プロセスのウィンドウを検出。以前は起動アプリごとに独立したポーリング
            // ループを並列実行しており、ティックごとに EnumerateWindows を N 回重複して
            // 呼んでいた（デスクトップ全体の走査を N 倍に増幅していた）。
            // 1ティック1回の共有スキャンで全アプリ分をまとめて判定する形に変更。
            var windowInfos = await FindStartupWindowsAsync(processConfigs, preExistingWindows);

            // 検出結果を元の順番でUIスレッドに反映
            for (int i = 0; i < processConfigs.Count; i++)
            {
                var (_, config) = processConfigs[i];
                var windowInfo = windowInfos[i];
                if (windowInfo == null) continue;

                try
                {
                    var tab = _tabManager.AddTab(windowInfo, activate: false);
                    if (tab != null)
                    {
                        tab.IsLaunchedAtStartup = true;
                        if (config.HideFromTaskbar && tab.Window?.Handle is IntPtr h && h != IntPtr.Zero)
                        {
                            _windowManager.ApplyTaskbarVisibility(h, hide: true);
                            _windowManager.MinimizeManagedWindow(h);
                        }
                        StatusMessage = $"Added: {tab.Title}";
                        configTabPairs.Add((config, tab));
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to embed process: {ex.Message}");
                }
            }
        }

        if (urlConfigs.Count > 0)
        {
            // URL が重複している場合でも 1:1 で対応付けできるようにする。
            var startupWebTabs = _tabManager.Tabs
                .Where(t => t.IsWebTab && t.IsLaunchedAtStartup)
                .ToList();
            var usedWebTabIds = new HashSet<Guid>();

            foreach (var config in urlConfigs)
            {
                var webTab = startupWebTabs.FirstOrDefault(t =>
                    !usedWebTabIds.Contains(t.Id) &&
                    string.Equals(t.WebUrl, config.Path, StringComparison.OrdinalIgnoreCase));

                if (webTab == null)
                    continue;

                usedWebTabIds.Add(webTab.Id);
                configTabPairs.Add((config, webTab));
            }
        }

        // Apply groups from settings
        ApplyStartupGroups(configTabPairs, settings);

        // スタートアップタイルグループを適用
        ApplyStartupTileGroups(configTabPairs, settings);

        // Activate the correct tab now that all tabs and groups are set up.
        // During the loop above, tabs were added without activation to avoid
        // rapid ActiveTab changes that cause display inconsistencies
        // (each change triggers Dispatcher.BeginInvoke for layout updates).
        if (_tabManager.Tabs.Count > 0)
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

    /// <summary>
    /// 設定の StartupTileGroups に従いスタートアップタブをタイル表示にする。
    /// </summary>
    private void ApplyStartupTileGroups(
        List<(StartupApplication Config, TabItem Tab)> configTabPairs,
        AppSettings settings)
    {
        foreach (var tileGroup in settings.StartupTileGroups)
        {
            var tabs = tileGroup.AppPaths
                .Select(path => configTabPairs
                    .FirstOrDefault(p => p.Config.Path.Equals(path, StringComparison.OrdinalIgnoreCase))
                    .Tab)
                .Where(t => t != null)
                .ToList();

            if (tabs.Count >= 2)
                _tabManager.TileSpecificTabs(tabs!);
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

    /// <summary>
    /// 複数の起動アプリのウィンドウをまとめて検出する。
    /// 以前はアプリごとに独立したポーリングループが並列に EnumerateWindows を呼んでおり、
    /// N個同時起動時にはデスクトップ全体の走査が毎ティック N 回重複していた。
    /// ここでは1ティックにつき共有スキャンを1回だけ行い、未検出の全アプリをまとめて判定する。
    /// </summary>
    private async Task<WindowInfo?[]> FindStartupWindowsAsync(
        List<(Process Process, StartupApplication Config)> processConfigs,
        HashSet<IntPtr>? preExistingWindows)
    {
        int count = processConfigs.Count;
        var results = new WindowInfo?[count];
        var pending = new HashSet<int>(Enumerable.Range(0, count));

        var targetProcessNames = processConfigs
            .Select(pair => TryGetProcessName(pair.Config.Path))
            .ToArray();
        var launchedProcessIds = new int[count];
        var processMainWindowHandles = new IntPtr[count];
        for (int i = 0; i < count; i++)
        {
            try { launchedProcessIds[i] = processConfigs[i].Process.Id; } catch { }
        }

        // 最大 50回 × 100ms = 5秒待機
        for (int i = 0; i < 50 && pending.Count > 0; i++)
        {
            foreach (var idx in pending)
            {
                var process = processConfigs[idx].Process;
                try
                {
                    if (!process.HasExited)
                    {
                        process.Refresh();
                        if (process.MainWindowHandle != IntPtr.Zero)
                        {
                            processMainWindowHandles[idx] = process.MainWindowHandle;
                        }
                    }
                }
                catch
                {
                    // Ignore process query failures and fallback to window enumeration.
                }
            }

            // RefreshWindowList() は AvailableWindows ObservableCollection を毎回
            // クリア・再構築するためUI更新コストが高い。
            // 起動検出には EnumerateWindows() を直接呼び出してコストを削減する。
            // さらに、この時点ではマッチング用のタイトル/PID/実行パスだけが必要で
            // アイコン抽出や昇格チェックは不要なため resolveIconAndElevation: false で
            // 画面上の全ウィンドウ分の重い処理（ディスクI/O・GDI変換・トークン照会）を省く。
            var windows = _windowManager.EnumerateWindows(resolveIconAndElevation: false);

            foreach (var idx in pending.ToList())
            {
                var config = processConfigs[idx].Config;
                var candidate = FindStartupCandidate(
                    windows,
                    config.Path,
                    targetProcessNames[idx],
                    preExistingWindows,
                    processMainWindowHandles[idx],
                    launchedProcessIds[idx],
                    preferNewWindow: true);
                if (candidate == null)
                    continue;

                preExistingWindows?.Add(candidate.Handle);
                // マッチが確定したウィンドウ1件だけアイコン/昇格情報を追加解決する。
                // ExecutablePath ベースのキャッシュから引くため、対象ウィンドウが
                // この直後に閉じてもアイコン取得に失敗しない。
                results[idx] = WindowInfo.WithResolvedDisplayInfo(candidate);
                pending.Remove(idx);
            }

            if (pending.Count == 0)
                break;

            await Task.Delay(100);
        }

        if (pending.Count > 0)
        {
            var finalWindows = _windowManager.EnumerateWindows(resolveIconAndElevation: false);
            foreach (var idx in pending)
            {
                var config = processConfigs[idx].Config;
                var fallback = FindStartupCandidate(
                    finalWindows,
                    config.Path,
                    targetProcessNames[idx],
                    preExistingWindows,
                    processMainWindowHandles[idx],
                    launchedProcessIds[idx],
                    preferNewWindow: false);
                if (fallback == null)
                    continue;

                preExistingWindows?.Add(fallback.Handle);
                results[idx] = WindowInfo.WithResolvedDisplayInfo(fallback);
            }
        }

        return results;
    }

    private static WindowInfo? FindStartupCandidate(
        IEnumerable<WindowInfo> windows,
        string configuredPath,
        string? targetProcessName,
        HashSet<IntPtr>? preExistingWindows,
        IntPtr processMainWindowHandle,
        int launchedProcessId,
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

        // PIDで直接照合（パス取得が失敗するケースでも確実に一致させる）
        if (launchedProcessId != 0)
        {
            var byPid = candidates.FirstOrDefault(w => w.ProcessId == launchedProcessId);
            if (byPid != null)
                return byPid;
        }

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

        // candidates.FirstOrDefault() は意図しないウィンドウ横取りを引き起こすため削除。
        // 複数アプリ並列起動時にタスクBが未発見のアプリAのウィンドウを先取りしてしまう
        // レースコンディションを防ぐ。
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
