using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using WindowzTabManager.Converters;

namespace WindowzTabManager.ViewModels;

public partial class QuickLaunchAppItem : ObservableObject
{
    private readonly QuickLaunchAppSetting _app;
    private readonly SettingsManager _settingsManager;
    private ImageSource? _icon;

    public QuickLaunchAppItem(QuickLaunchAppSetting app, SettingsManager settingsManager)
    {
        _app = app;
        _settingsManager = settingsManager;
        _icon = PathToIconConverter.GetIconForPath(app.Path);
    }

    public QuickLaunchAppSetting Model => _app;

    public ImageSource? Icon => _icon;

    public string Name
    {
        get => _app.Name;
        set
        {
            if (_app.Name != value)
            {
                _app.Name = value;
                OnPropertyChanged();
                _settingsManager.SaveQuickLaunchApp();
            }
        }
    }

    public string Path => _app.Path;

    public string Arguments
    {
        get => _app.Arguments;
        set
        {
            if (_app.Arguments != value)
            {
                _app.Arguments = value;
                OnPropertyChanged();
                _settingsManager.SaveQuickLaunchApp();
            }
        }
    }

    public bool ShouldEmbed
    {
        get => _app.ShouldEmbed;
        set
        {
            if (_app.ShouldEmbed != value)
            {
                _app.ShouldEmbed = value;
                OnPropertyChanged();
                _settingsManager.SaveQuickLaunchApp();
            }
        }
    }
}

/// <summary>
/// QuickLaunch タイルグループの VM。
/// 残りアプリが 1 個以下になり解散が必要なときは <see cref="DissolutionRequested"/> を発火する。
/// </summary>
public partial class QuickLaunchTileGroupItem : ObservableObject
{
    private readonly QuickLaunchTileGroupSetting _model;
    private readonly SettingsManager _settingsManager;

    public event Action<QuickLaunchTileGroupItem, QuickLaunchAppItem?>? DissolutionRequested;

    public QuickLaunchTileGroupItem(
        QuickLaunchTileGroupSetting model,
        IEnumerable<QuickLaunchAppItem> apps,
        SettingsManager settingsManager)
    {
        _model = model;
        _settingsManager = settingsManager;
        foreach (var app in apps)
            Apps.Add(app);
    }

    public QuickLaunchTileGroupSetting Model => _model;

    public string Name
    {
        get => _model.Name;
        set
        {
            if (_model.Name != value)
            {
                _model.Name = value;
                OnPropertyChanged();
                _settingsManager.UpdateQuickLaunchTileGroup(_model);
            }
        }
    }

    public ObservableCollection<QuickLaunchAppItem> Apps { get; } = new();

    public bool Is2Slot => Apps.Count == 2;
    public bool Is3Slot => Apps.Count == 3;
    public bool Is4Slot => Apps.Count == 4;
    public bool CanAddApp => Apps.Count < 4;

    private void RefreshSlotProperties()
    {
        OnPropertyChanged(nameof(Is2Slot));
        OnPropertyChanged(nameof(Is3Slot));
        OnPropertyChanged(nameof(Is4Slot));
        OnPropertyChanged(nameof(CanAddApp));
    }

    private void SyncModel()
    {
        _model.AppPaths = Apps.Select(a => a.Path).ToList();
        _settingsManager.UpdateQuickLaunchTileGroup(_model);
    }

    public void AddApp(QuickLaunchAppItem app)
    {
        if (Apps.Count >= 4) return;
        Apps.Add(app);
        SyncModel();
        RefreshSlotProperties();
    }

    public bool ContainsApp(QuickLaunchAppItem app) => Apps.Contains(app);

    public void DetachAppForMove(QuickLaunchAppItem app)
    {
        RemoveAppCore(app, includeRemovedAppWhenDissolving: false);
    }

    [RelayCommand]
    private void RemoveApp(QuickLaunchAppItem? app)
    {
        if (app == null) return;
        RemoveAppCore(app, includeRemovedAppWhenDissolving: true);
    }

    [RelayCommand]
    private void MoveUp(QuickLaunchAppItem? app)
    {
        if (app == null) return;
        int idx = Apps.IndexOf(app);
        if (idx <= 0) return;
        Apps.Move(idx, idx - 1);
        SyncModel();
    }

    [RelayCommand]
    private void MoveDown(QuickLaunchAppItem? app)
    {
        if (app == null) return;
        int idx = Apps.IndexOf(app);
        if (idx < 0 || idx >= Apps.Count - 1) return;
        Apps.Move(idx, idx + 1);
        SyncModel();
    }

    private void RemoveAppCore(QuickLaunchAppItem app, bool includeRemovedAppWhenDissolving)
    {
        if (!Apps.Remove(app)) return;
        SyncModel();
        RefreshSlotProperties();
        if (Apps.Count < 2)
            DissolutionRequested?.Invoke(this, includeRemovedAppWhenDissolving ? app : null);
    }
}

public partial class QuickLaunchSettingsViewModel : ObservableObject
{
    private readonly SettingsManager _settingsManager;

    [ObservableProperty]
    private ObservableCollection<object> _launchItems = new();

    [ObservableProperty]
    private ObservableCollection<QuickLaunchAppItem> _quickLaunchApps = new();

    [ObservableProperty]
    private ObservableCollection<QuickLaunchTileGroupItem> _tileGroups = new();

    [ObservableProperty]
    private string _newQuickLaunchPath = string.Empty;

    [ObservableProperty]
    private ObservableCollection<string> _pathSuggestions = new();

    [ObservableProperty]
    private bool _isSuggestionsOpen;

    [ObservableProperty]
    private string? _selectedSuggestion;

    public bool HasNoQuickLaunchApps => LaunchItems.Count == 0;

    private List<string> _allPathExecutables = new();
    private bool _suppressSuggestions;

    public QuickLaunchSettingsViewModel(SettingsManager settingsManager)
    {
        _settingsManager = settingsManager;
        LoadSettings();
        ScanPathExecutables();
    }

    private void LoadSettings()
    {
        var settings = _settingsManager.Settings;

        var tiledPaths = settings.QuickLaunchTileGroups
            .SelectMany(g => g.AppPaths)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        QuickLaunchApps.Clear();
        foreach (var app in settings.QuickLaunchApps)
        {
            if (tiledPaths.Contains(app.Path)) continue;
            QuickLaunchApps.Add(new QuickLaunchAppItem(app, _settingsManager));
        }

        TileGroups.Clear();
        foreach (var tileGroup in settings.QuickLaunchTileGroups)
        {
            var apps = tileGroup.AppPaths
                .Select(path => settings.QuickLaunchApps
                    .FirstOrDefault(a => a.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
                .Where(a => a != null)
                .Select(a => new QuickLaunchAppItem(a!, _settingsManager))
                .ToList();

            if (apps.Count >= 2)
                AddTileGroupItem(new QuickLaunchTileGroupItem(tileGroup, apps, _settingsManager), refreshLaunchItems: false);
        }

        RefreshLaunchItems();
    }

    public void Reload()
    {
        LoadSettings();
    }

    private void AddTileGroupItem(QuickLaunchTileGroupItem group, bool refreshLaunchItems = true)
    {
        group.DissolutionRequested += OnTileGroupDissolutionRequested;
        TileGroups.Add(group);
        if (refreshLaunchItems)
            RefreshLaunchItems();
    }

    private QuickLaunchTileGroupItem? FindGroupContainingApp(QuickLaunchAppItem app)
    {
        return TileGroups.FirstOrDefault(g => g.ContainsApp(app));
    }

    private void DetachAppFromCurrentLocation(QuickLaunchAppItem app, QuickLaunchTileGroupItem? skipGroup = null)
    {
        if (QuickLaunchApps.Contains(app))
        {
            QuickLaunchApps.Remove(app);
            return;
        }

        var sourceGroup = FindGroupContainingApp(app);
        if (sourceGroup == null || sourceGroup == skipGroup)
            return;

        sourceGroup.DetachAppForMove(app);
    }

    private void RefreshLaunchItems()
    {
        LaunchItems.Clear();

        var groupByPath = new Dictionary<string, QuickLaunchTileGroupItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in TileGroups)
            foreach (var path in group.Model.AppPaths)
                groupByPath.TryAdd(path, group);

        var standaloneByPath = QuickLaunchApps
            .GroupBy(a => a.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var emittedGroupIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var app in _settingsManager.Settings.QuickLaunchApps)
        {
            if (groupByPath.TryGetValue(app.Path, out var group))
            {
                if (emittedGroupIds.Add(group.Model.Id))
                    LaunchItems.Add(group);
                continue;
            }

            if (standaloneByPath.TryGetValue(app.Path, out var standalone))
                LaunchItems.Add(standalone);
        }

        // 整合性ずれの保険
        foreach (var group in TileGroups)
            if (emittedGroupIds.Add(group.Model.Id))
                LaunchItems.Add(group);

        foreach (var app in QuickLaunchApps)
            if (!LaunchItems.Contains(app))
                LaunchItems.Add(app);

        OnPropertyChanged(nameof(HasNoQuickLaunchApps));
    }

    public bool IsLaunchItem(object? item) => item != null && LaunchItems.Contains(item);

    public bool TryReorderLaunchItems(object sourceItem, object targetItem, bool insertAfter)
    {
        if (ReferenceEquals(sourceItem, targetItem)) return false;

        int sourceIndex = LaunchItems.IndexOf(sourceItem);
        int targetIndex = LaunchItems.IndexOf(targetItem);
        if (sourceIndex < 0 || targetIndex < 0) return false;

        LaunchItems.RemoveAt(sourceIndex);
        if (sourceIndex < targetIndex) targetIndex--;

        int insertIndex = Math.Clamp(insertAfter ? targetIndex + 1 : targetIndex, 0, LaunchItems.Count);
        LaunchItems.Insert(insertIndex, sourceItem);

        PersistOrderFromLaunchItems();
        RefreshLaunchItems();
        return true;
    }

    private void PersistOrderFromLaunchItems()
    {
        var settingsApps = _settingsManager.Settings.QuickLaunchApps;
        var ordered = new List<QuickLaunchAppSetting>();
        var added = new HashSet<QuickLaunchAppSetting>();

        foreach (var item in LaunchItems)
        {
            switch (item)
            {
                case QuickLaunchAppItem app when QuickLaunchApps.Contains(app):
                    if (added.Add(app.Model)) ordered.Add(app.Model);
                    break;

                case QuickLaunchTileGroupItem group when TileGroups.Contains(group):
                    foreach (var groupApp in group.Apps)
                        if (added.Add(groupApp.Model)) ordered.Add(groupApp.Model);
                    break;
            }
        }

        foreach (var app in settingsApps)
            if (added.Add(app)) ordered.Add(app);

        settingsApps.Clear();
        foreach (var app in ordered) settingsApps.Add(app);
        _settingsManager.SaveQuickLaunchApp();
    }

    public bool MoveAppToStandaloneAtDrop(QuickLaunchAppItem app, object targetLaunchItem, bool insertAfter)
    {
        var sourceGroup = FindGroupContainingApp(app);
        if (sourceGroup == null) return false;

        sourceGroup.DetachAppForMove(app);

        if (!QuickLaunchApps.Contains(app))
            QuickLaunchApps.Add(app);

        RefreshLaunchItems();

        var sourceLaunchItem = LaunchItems.OfType<QuickLaunchAppItem>()
            .FirstOrDefault(x => ReferenceEquals(x, app));
        var target = targetLaunchItem switch
        {
            QuickLaunchAppItem a => LaunchItems.OfType<QuickLaunchAppItem>()
                .FirstOrDefault(x => ReferenceEquals(x, a)),
            QuickLaunchTileGroupItem g => LaunchItems.OfType<QuickLaunchTileGroupItem>()
                .FirstOrDefault(x => ReferenceEquals(x, g)),
            _ => LaunchItems.Contains(targetLaunchItem) ? targetLaunchItem : null
        };

        if (sourceLaunchItem == null) return false;
        if (target == null || ReferenceEquals(sourceLaunchItem, target)) return true;

        return TryReorderLaunchItems(sourceLaunchItem, target, insertAfter);
    }

    private void OnTileGroupDissolutionRequested(QuickLaunchTileGroupItem group, QuickLaunchAppItem? removedApp)
    {
        foreach (var app in group.Apps.ToList())
            QuickLaunchApps.Add(app);

        if (removedApp != null && !QuickLaunchApps.Contains(removedApp))
            QuickLaunchApps.Add(removedApp);

        _settingsManager.RemoveQuickLaunchTileGroup(group.Model.Id);
        TileGroups.Remove(group);

        RefreshLaunchItems();
    }

    public void CreateTileGroupFromDrop(QuickLaunchAppItem source, QuickLaunchAppItem target)
    {
        if (source == target) return;

        var targetGroup = FindGroupContainingApp(target);
        if (targetGroup != null)
        {
            AddAppToTileGroupFromDrop(source, targetGroup);
            return;
        }

        DetachAppFromCurrentLocation(source);
        DetachAppFromCurrentLocation(target);

        if (QuickLaunchApps.Contains(source)) QuickLaunchApps.Remove(source);
        if (QuickLaunchApps.Contains(target)) QuickLaunchApps.Remove(target);

        source.Model.Name = source.Name;
        target.Model.Name = target.Name;

        var paths = new List<string> { source.Path, target.Path };
        var model = _settingsManager.AddQuickLaunchTileGroup(string.Empty, paths);
        var groupItem = new QuickLaunchTileGroupItem(model, new[] { source, target }, _settingsManager);
        AddTileGroupItem(groupItem, refreshLaunchItems: false);

        RefreshLaunchItems();
    }

    public void AddAppToTileGroupFromDrop(QuickLaunchAppItem app, QuickLaunchTileGroupItem group)
    {
        if (!group.CanAddApp) return;
        var sourceGroup = FindGroupContainingApp(app);
        if (sourceGroup == group) return;

        DetachAppFromCurrentLocation(app, skipGroup: group);
        if (QuickLaunchApps.Contains(app)) QuickLaunchApps.Remove(app);
        group.AddApp(app);

        RefreshLaunchItems();
    }

    // ─── 既存コマンド ─────────────────────────────────────────────────────────

    [RelayCommand]
    private void RemoveTileGroup(QuickLaunchTileGroupItem? group)
    {
        if (group == null) return;

        foreach (var app in group.Apps)
            QuickLaunchApps.Add(app);

        _settingsManager.RemoveQuickLaunchTileGroup(group.Model.Id);
        TileGroups.Remove(group);

        RefreshLaunchItems();
    }

    [RelayCommand]
    private void AddQuickLaunchApp()
    {
        if (string.IsNullOrWhiteSpace(NewQuickLaunchPath)) return;

        var input = NewQuickLaunchPath.Trim();
        ParsePathAndArguments(input, out var path, out var arguments);
        var name = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrEmpty(name))
            name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));

        var app = _settingsManager.AddQuickLaunchApp(path, arguments, name);
        QuickLaunchApps.Add(new QuickLaunchAppItem(app, _settingsManager));
        NewQuickLaunchPath = string.Empty;
        RefreshLaunchItems();
    }

    [RelayCommand]
    private void BrowseQuickLaunchApp()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Application",
            Filter = "Executable Files (*.exe)|*.exe|All Files (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() == true)
            NewQuickLaunchPath = dialog.FileName;
    }

    [RelayCommand]
    private void RemoveQuickLaunchApp(QuickLaunchAppItem? item)
    {
        if (item == null) return;

        _settingsManager.RemoveQuickLaunchApp(item.Model);
        QuickLaunchApps.Remove(item);
        RefreshLaunchItems();
    }

    [RelayCommand]
    private void RunQuickLaunchApp(QuickLaunchAppItem? item)
    {
        if (item == null) return;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = item.Path,
                Arguments = item.Model.Arguments,
                UseShellExecute = true
            };
            Process.Start(psi);
        }
        catch
        {
            // Ignore launch errors
        }
    }

    // ─── パスサジェスト ────────────────────────────────────────────────────────

    private void ScanPathExecutables()
    {
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".exe", ".cmd", ".bat", ".com" };

        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        var dirs = pathVar.Split(';', StringSplitOptions.RemoveEmptyEntries);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in dirs)
        {
            try
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var file in Directory.EnumerateFiles(dir))
                {
                    var ext = Path.GetExtension(file);
                    if (extensions.Contains(ext))
                        names.Add(Path.GetFileNameWithoutExtension(file));
                }
            }
            catch { }
        }

        _allPathExecutables = names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
    }

    partial void OnNewQuickLaunchPathChanged(string value)
    {
        if (_suppressSuggestions) return;

        if (string.IsNullOrEmpty(value))
        {
            IsSuggestionsOpen = false;
            return;
        }

        List<string> matches;

        if (value.Contains('\\') || value.Contains('/'))
        {
            matches = GetFilePathSuggestions(value);
        }
        else
        {
            var query = value.Trim();
            matches = _allPathExecutables
                .Where(n => n.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Take(10)
                .ToList();
        }

        PathSuggestions.Clear();
        foreach (var m in matches)
            PathSuggestions.Add(m);

        IsSuggestionsOpen = PathSuggestions.Count > 0;
    }

    private static List<string> GetFilePathSuggestions(string input)
    {
        try
        {
            var lastSep = input.LastIndexOfAny(['\\', '/']);
            var dir = input[..(lastSep + 1)];
            var prefix = input[(lastSep + 1)..];

            if (!Directory.Exists(dir)) return [];

            var results = new List<string>();

            foreach (var d in Directory.EnumerateDirectories(dir))
            {
                var name = Path.GetFileName(d);
                if (name.Contains(prefix, StringComparison.OrdinalIgnoreCase))
                    results.Add(d + "\\");
                if (results.Count >= 15) break;
            }

            foreach (var f in Directory.EnumerateFiles(dir))
            {
                var name = Path.GetFileName(f);
                if (name.Contains(prefix, StringComparison.OrdinalIgnoreCase))
                    results.Add(f);
                if (results.Count >= 15) break;
            }

            return results;
        }
        catch
        {
            return [];
        }
    }

    public void ApplySuggestion(string value)
    {
        _suppressSuggestions = true;
        NewQuickLaunchPath = value;
        IsSuggestionsOpen = false;
        SelectedSuggestion = null;
        _suppressSuggestions = false;

        if (value.EndsWith('\\'))
            OnNewQuickLaunchPathChanged(value);
    }

    private static void ParsePathAndArguments(string input, out string path, out string arguments)
    {
        if (input.StartsWith('"'))
        {
            var closeQuote = input.IndexOf('"', 1);
            if (closeQuote > 0)
            {
                path = input[1..closeQuote];
                arguments = input[(closeQuote + 1)..].TrimStart();
                return;
            }
        }

        var extPattern = new[] { ".exe ", ".cmd ", ".bat ", ".com " };
        foreach (var ext in extPattern)
        {
            var idx = input.IndexOf(ext, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                var splitAt = idx + ext.Length - 1;
                path = input[..splitAt].Trim();
                arguments = input[(splitAt + 1)..].TrimStart();
                return;
            }
        }

        var spaceIdx = input.IndexOf(' ');
        if (spaceIdx > 0)
        {
            path = input[..spaceIdx];
            arguments = input[(spaceIdx + 1)..].TrimStart();
            return;
        }

        path = input;
        arguments = string.Empty;
    }
}
