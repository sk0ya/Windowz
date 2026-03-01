using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using WindowzTabManager.Converters;

namespace WindowzTabManager.ViewModels;

public partial class StartupAppItem : ObservableObject
{
    private readonly StartupApplicationSetting _app;
    private readonly SettingsManager _settingsManager;
    private readonly ImageSource? _icon;

    public StartupAppItem(StartupApplicationSetting app, SettingsManager settingsManager)
    {
        _app = app;
        _settingsManager = settingsManager;
        _icon = PathToIconConverter.GetIconForPath(app.Path);
    }

    public StartupApplicationSetting Model => _app;

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
                _settingsManager.SaveStartupApplication();
            }
        }
    }

    public string Path => _app.Path;

    public bool IsUrl => SettingsManager.IsUrl(_app.Path);

    public string Arguments
    {
        get => _app.Arguments;
        set
        {
            if (_app.Arguments != value)
            {
                _app.Arguments = value;
                OnPropertyChanged();
                _settingsManager.SaveStartupApplication();
            }
        }
    }

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>
/// スタートアップ タイルグループの VM。
/// 解散が必要になった場合は <see cref="DissolutionRequested"/> を発火する。
/// </summary>
public partial class StartupTileGroupItem : ObservableObject
{
    private readonly StartupTileGroupSetting _model;
    private readonly SettingsManager _settingsManager;

    /// <summary>残りアプリが 1 個以下になり解散が必要なときに発火する。</summary>
    public event Action<StartupTileGroupItem, StartupAppItem?>? DissolutionRequested;

    public StartupTileGroupItem(
        StartupTileGroupSetting model,
        IEnumerable<StartupAppItem> apps,
        SettingsManager settingsManager)
    {
        _model = model;
        _settingsManager = settingsManager;
        foreach (var app in apps)
            Apps.Add(app);
    }

    public StartupTileGroupSetting Model => _model;

    public ObservableCollection<StartupAppItem> Apps { get; } = new();

    public int SlotCount => Apps.Count;

    public bool Is2Slot => Apps.Count == 2;
    public bool Is3Slot => Apps.Count == 3;
    public bool Is4Slot => Apps.Count == 4;
    public bool CanAddApp => Apps.Count < 4;

    private void RefreshSlotProperties()
    {
        OnPropertyChanged(nameof(SlotCount));
        OnPropertyChanged(nameof(Is2Slot));
        OnPropertyChanged(nameof(Is3Slot));
        OnPropertyChanged(nameof(Is4Slot));
        OnPropertyChanged(nameof(CanAddApp));
    }

    private void SyncModel()
    {
        _model.AppPaths = Apps.Select(a => a.Path).ToList();
        _settingsManager.UpdateStartupTileGroup(_model);
    }

    public void AddApp(StartupAppItem app)
    {
        if (Apps.Count >= 4) return;
        Apps.Add(app);
        SyncModel();
        RefreshSlotProperties();
    }

    public bool ContainsApp(StartupAppItem app)
    {
        return Apps.Contains(app);
    }

    public void DetachAppForMove(StartupAppItem app)
    {
        RemoveAppCore(app, includeRemovedAppWhenDissolving: false);
    }

    [RelayCommand]
    private void RemoveApp(StartupAppItem? app)
    {
        if (app == null) return;
        RemoveAppCore(app, includeRemovedAppWhenDissolving: true);
    }

    [RelayCommand]
    private void MoveUp(StartupAppItem? app)
    {
        if (app == null) return;
        int idx = Apps.IndexOf(app);
        if (idx <= 0) return;
        Apps.Move(idx, idx - 1);
        SyncModel();
    }

    [RelayCommand]
    private void MoveDown(StartupAppItem? app)
    {
        if (app == null) return;
        int idx = Apps.IndexOf(app);
        if (idx < 0 || idx >= Apps.Count - 1) return;
        Apps.Move(idx, idx + 1);
        SyncModel();
    }

    private void RemoveAppCore(StartupAppItem app, bool includeRemovedAppWhenDissolving)
    {
        if (!Apps.Remove(app)) return;

        SyncModel();
        RefreshSlotProperties();

        if (Apps.Count < 2)
        {
            DissolutionRequested?.Invoke(this, includeRemovedAppWhenDissolving ? app : null);
        }
    }
}

public partial class StartupSettingsViewModel : ObservableObject
{
    // ファイル選択ダイアログで選択後、View 側でテキストボックスにフォーカスを当てるために使用
    public event Action? BrowseDone;

    private readonly SettingsManager _settingsManager;

    [ObservableProperty]
    private ObservableCollection<object> _launchItems = new();

    [ObservableProperty]
    private ObservableCollection<StartupAppItem> _startupApplications = new();

    [ObservableProperty]
    private ObservableCollection<StartupTileGroupItem> _tileGroups = new();

    [ObservableProperty]
    private string _newStartupPath = string.Empty;

    public bool HasNoStartupApplications => LaunchItems.Count == 0;

    public int SelectedCount => StartupApplications.Count(a => a.IsSelected);

    public bool CanCreateTile => SelectedCount is >= 2 and <= 4;

    public string CreateTileButtonLabel =>
        SelectedCount >= 2 ? $"タイル表示にする ({SelectedCount} apps)" : "タイル表示にする";

    public StartupSettingsViewModel(SettingsManager settingsManager)
    {
        _settingsManager = settingsManager;
        LoadSettings();
    }

    private void LoadSettings()
    {
        var settings = _settingsManager.Settings;

        // タイルグループ済みのパスをセットで保持
        var tiledPaths = settings.StartupTileGroups
            .SelectMany(g => g.AppPaths)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        StartupApplications.Clear();
        foreach (var app in settings.StartupApplications)
        {
            if (tiledPaths.Contains(app.Path)) continue;
            StartupApplications.Add(CreateAppItem(app));
        }

        TileGroups.Clear();
        foreach (var tileGroup in settings.StartupTileGroups)
        {
            var apps = tileGroup.AppPaths
                .Select(path => settings.StartupApplications
                    .FirstOrDefault(a => a.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
                .Where(a => a != null)
                .Select(a => CreateAppItem(a!))
                .ToList();

            if (apps.Count >= 2)
                AddTileGroupItem(new StartupTileGroupItem(tileGroup, apps, _settingsManager), refreshLaunchItems: false);
        }

        RefreshLaunchItems();
        RefreshSelectionState();
    }

    private StartupAppItem CreateAppItem(StartupApplicationSetting app)
    {
        var item = new StartupAppItem(app, _settingsManager);
        item.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(StartupAppItem.IsSelected))
                RefreshSelectionState();
        };
        return item;
    }

    private void AddTileGroupItem(StartupTileGroupItem group, bool refreshLaunchItems = true)
    {
        group.DissolutionRequested += OnTileGroupDissolutionRequested;
        TileGroups.Add(group);
        if (refreshLaunchItems)
            RefreshLaunchItems();
    }

    private StartupTileGroupItem? FindGroupContainingApp(StartupAppItem app)
    {
        return TileGroups.FirstOrDefault(g => g.ContainsApp(app));
    }

    private void DetachAppFromCurrentLocation(StartupAppItem app, StartupTileGroupItem? skipGroup = null)
    {
        if (StartupApplications.Contains(app))
        {
            StartupApplications.Remove(app);
            return;
        }

        var sourceGroup = FindGroupContainingApp(app);
        if (sourceGroup == null || sourceGroup == skipGroup)
            return;

        sourceGroup.DetachAppForMove(app);
    }

    public bool IsInTileGroup(StartupAppItem app)
    {
        return FindGroupContainingApp(app) != null;
    }

    private void RefreshLaunchItems()
    {
        LaunchItems.Clear();

        var groupByPath = new Dictionary<string, StartupTileGroupItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in TileGroups)
        {
            foreach (var path in group.Model.AppPaths)
            {
                if (!groupByPath.ContainsKey(path))
                    groupByPath[path] = group;
            }
        }

        var standaloneByPath = StartupApplications
            .GroupBy(a => a.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var emittedGroupIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var app in _settingsManager.Settings.StartupApplications)
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

        // 設定順に現れなかった項目（整合性ずれの保険）も末尾に表示する。
        foreach (var group in TileGroups)
        {
            if (emittedGroupIds.Add(group.Model.Id))
                LaunchItems.Add(group);
        }

        foreach (var app in StartupApplications)
        {
            if (!LaunchItems.Contains(app))
                LaunchItems.Add(app);
        }

        OnPropertyChanged(nameof(HasNoStartupApplications));
    }

    public bool IsLaunchItem(object? item)
    {
        return item != null && LaunchItems.Contains(item);
    }

    public bool TryReorderLaunchItems(object sourceItem, object targetItem, bool insertAfter)
    {
        if (ReferenceEquals(sourceItem, targetItem))
            return false;

        int sourceIndex = LaunchItems.IndexOf(sourceItem);
        int targetIndex = LaunchItems.IndexOf(targetItem);
        if (sourceIndex < 0 || targetIndex < 0)
            return false;

        LaunchItems.RemoveAt(sourceIndex);
        if (sourceIndex < targetIndex)
            targetIndex--;

        int insertIndex = insertAfter ? targetIndex + 1 : targetIndex;
        insertIndex = Math.Clamp(insertIndex, 0, LaunchItems.Count);
        LaunchItems.Insert(insertIndex, sourceItem);

        PersistStartupOrderFromLaunchItems();
        RefreshLaunchItems();
        RefreshSelectionState();
        return true;
    }

    public bool MoveAppToStandaloneAtDrop(StartupAppItem app, object targetLaunchItem, bool insertAfter)
    {
        var sourceGroup = FindGroupContainingApp(app);
        if (sourceGroup == null)
            return false;

        sourceGroup.DetachAppForMove(app);
        app.IsSelected = false;

        if (!StartupApplications.Contains(app))
            StartupApplications.Add(app);

        RefreshLaunchItems();

        var sourceLaunchItem = ResolveLaunchItem(app) as StartupAppItem;
        var target = ResolveLaunchItem(targetLaunchItem);
        if (sourceLaunchItem == null)
        {
            RefreshSelectionState();
            return false;
        }

        if (target == null || ReferenceEquals(sourceLaunchItem, target))
        {
            RefreshSelectionState();
            return true;
        }

        return TryReorderLaunchItems(sourceLaunchItem, target, insertAfter);
    }

    private void PersistStartupOrderFromLaunchItems()
    {
        var settingsApps = _settingsManager.Settings.StartupApplications;
        var ordered = new List<StartupApplicationSetting>();
        var added = new HashSet<StartupApplicationSetting>();

        foreach (var item in LaunchItems)
        {
            switch (item)
            {
                case StartupAppItem app when StartupApplications.Contains(app):
                    if (added.Add(app.Model))
                        ordered.Add(app.Model);
                    break;

                case StartupTileGroupItem group when TileGroups.Contains(group):
                    foreach (var groupApp in group.Apps)
                    {
                        if (added.Add(groupApp.Model))
                            ordered.Add(groupApp.Model);
                    }
                    break;
            }
        }

        foreach (var app in settingsApps)
        {
            if (added.Add(app))
                ordered.Add(app);
        }

        settingsApps.Clear();
        foreach (var app in ordered)
            settingsApps.Add(app);

        _settingsManager.SaveStartupApplication();
    }

    private object? ResolveLaunchItem(object item)
    {
        return item switch
        {
            StartupAppItem app => LaunchItems.OfType<StartupAppItem>()
                .FirstOrDefault(x => ReferenceEquals(x, app) ||
                                     x.Path.Equals(app.Path, StringComparison.OrdinalIgnoreCase)),
            StartupTileGroupItem group => LaunchItems.OfType<StartupTileGroupItem>()
                .FirstOrDefault(x => ReferenceEquals(x, group) || x.Model.Id == group.Model.Id),
            _ => LaunchItems.Contains(item) ? item : null
        };
    }

    private void OnTileGroupDissolutionRequested(StartupTileGroupItem group, StartupAppItem? removedApp)
    {
        // 残りアプリをアプリ一覧に戻す
        foreach (var app in group.Apps.ToList())
        {
            app.IsSelected = false;
            StartupApplications.Add(app);
        }
        // 除去したアプリも戻す（まだリストに入っていなければ）
        if (removedApp != null && !StartupApplications.Contains(removedApp))
        {
            removedApp.IsSelected = false;
            StartupApplications.Add(removedApp);
        }

        _settingsManager.RemoveStartupTileGroup(group.Model.Id);
        TileGroups.Remove(group);

        RefreshLaunchItems();
        RefreshSelectionState();
    }

    private void RefreshSelectionState()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(CanCreateTile));
        OnPropertyChanged(nameof(CreateTileButtonLabel));
    }

    public void Reload()
    {
        LoadSettings();
    }

    [RelayCommand]
    private void AddStartupApplication()
    {
        if (string.IsNullOrWhiteSpace(NewStartupPath)) return;

        var path = NewStartupPath.Trim();
        var app = _settingsManager.AddStartupApplication(path);
        StartupApplications.Add(CreateAppItem(app));
        NewStartupPath = string.Empty;
        RefreshLaunchItems();
    }

    [RelayCommand]
    private void BrowseStartupApplication()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Application",
            Filter = "Executable Files (*.exe)|*.exe|All Files (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() == true)
        {
            NewStartupPath = dialog.FileName;
            BrowseDone?.Invoke();
        }
    }

    [RelayCommand]
    private void RemoveStartupApplication(StartupAppItem? item)
    {
        if (item == null) return;

        _settingsManager.RemoveStartupApplication(item.Model);
        StartupApplications.Remove(item);
        RefreshLaunchItems();
        RefreshSelectionState();
    }

    /// <summary>
    /// チェック済みのアプリ（2〜4個）をタイルグループとして登録する。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCreateTile))]
    private void CreateTileGroup()
    {
        var selected = StartupApplications.Where(a => a.IsSelected).ToList();
        if (selected.Count < 2 || selected.Count > 4) return;

        var paths = selected.Select(a => a.Path).ToList();
        var model = _settingsManager.AddStartupTileGroup(paths);
        var groupItem = new StartupTileGroupItem(model, selected, _settingsManager);
        AddTileGroupItem(groupItem, refreshLaunchItems: false);

        foreach (var app in selected)
        {
            app.IsSelected = false;
            StartupApplications.Remove(app);
        }

        RefreshLaunchItems();
        RefreshSelectionState();
    }

    /// <summary>
    /// タイルグループを解除してアプリを独立リストに戻す。
    /// </summary>
    [RelayCommand]
    private void RemoveTileGroup(StartupTileGroupItem? group)
    {
        if (group == null) return;

        foreach (var app in group.Apps)
        {
            app.IsSelected = false;
            StartupApplications.Add(app);
        }

        _settingsManager.RemoveStartupTileGroup(group.Model.Id);
        TileGroups.Remove(group);

        RefreshLaunchItems();
        RefreshSelectionState();
    }

    /// <summary>
    /// ドラッグ&ドロップでアプリ同士を重ねてタイルグループを作成する。
    /// </summary>
    public void CreateTileGroupFromDrop(StartupAppItem source, StartupAppItem target)
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

        if (StartupApplications.Contains(source))
            StartupApplications.Remove(source);
        if (StartupApplications.Contains(target))
            StartupApplications.Remove(target);

        source.IsSelected = false;
        target.IsSelected = false;
        var paths = new List<string> { source.Path, target.Path };
        var model = _settingsManager.AddStartupTileGroup(paths);
        var groupItem = new StartupTileGroupItem(model, new[] { source, target }, _settingsManager);
        AddTileGroupItem(groupItem, refreshLaunchItems: false);

        RefreshLaunchItems();
        RefreshSelectionState();
    }

    /// <summary>
    /// ドラッグ&ドロップで既存タイルグループにアプリを追加する。
    /// </summary>
    public void AddAppToTileGroupFromDrop(StartupAppItem app, StartupTileGroupItem group)
    {
        if (!group.CanAddApp) return;
        var sourceGroup = FindGroupContainingApp(app);
        if (sourceGroup == group) return;

        DetachAppFromCurrentLocation(app, skipGroup: group);
        if (StartupApplications.Contains(app))
            StartupApplications.Remove(app);
        group.AddApp(app);
        app.IsSelected = false;

        RefreshLaunchItems();
        RefreshSelectionState();
    }

    public void MoveAppToStandaloneFromDrop(StartupAppItem app)
    {
        var sourceGroup = FindGroupContainingApp(app);
        if (sourceGroup == null)
            return;

        sourceGroup.DetachAppForMove(app);
        app.IsSelected = false;

        if (!StartupApplications.Contains(app))
            StartupApplications.Add(app);

        RefreshLaunchItems();
        RefreshSelectionState();
    }
}
