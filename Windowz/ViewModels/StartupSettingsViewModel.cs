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

    [RelayCommand]
    private void RemoveApp(StartupAppItem? app)
    {
        if (app == null) return;
        Apps.Remove(app);
        SyncModel();
        RefreshSlotProperties();

        if (Apps.Count < 2)
            DissolutionRequested?.Invoke(this, app);
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
}

public partial class StartupSettingsViewModel : ObservableObject
{
    private readonly SettingsManager _settingsManager;

    [ObservableProperty]
    private ObservableCollection<StartupAppItem> _startupApplications = new();

    [ObservableProperty]
    private ObservableCollection<StartupTileGroupItem> _tileGroups = new();

    [ObservableProperty]
    private string _newStartupPath = string.Empty;

    public bool HasNoStartupApplications => StartupApplications.Count == 0;

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
                AddTileGroupItem(new StartupTileGroupItem(tileGroup, apps, _settingsManager));
        }

        OnPropertyChanged(nameof(HasNoStartupApplications));
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

    private void AddTileGroupItem(StartupTileGroupItem group)
    {
        group.DissolutionRequested += OnTileGroupDissolutionRequested;
        TileGroups.Add(group);
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

        OnPropertyChanged(nameof(HasNoStartupApplications));
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
        OnPropertyChanged(nameof(HasNoStartupApplications));
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
        }
    }

    [RelayCommand]
    private void RemoveStartupApplication(StartupAppItem? item)
    {
        if (item == null) return;

        _settingsManager.RemoveStartupApplication(item.Model);
        StartupApplications.Remove(item);
        OnPropertyChanged(nameof(HasNoStartupApplications));
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
        AddTileGroupItem(groupItem);

        foreach (var app in selected)
        {
            app.IsSelected = false;
            StartupApplications.Remove(app);
        }

        OnPropertyChanged(nameof(HasNoStartupApplications));
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

        OnPropertyChanged(nameof(HasNoStartupApplications));
        RefreshSelectionState();
    }

    /// <summary>
    /// ドラッグ&ドロップでアプリ同士を重ねてタイルグループを作成する。
    /// </summary>
    public void CreateTileGroupFromDrop(StartupAppItem source, StartupAppItem target)
    {
        if (source == target) return;
        if (!StartupApplications.Contains(source) || !StartupApplications.Contains(target)) return;

        var paths = new List<string> { source.Path, target.Path };
        var model = _settingsManager.AddStartupTileGroup(paths);
        var groupItem = new StartupTileGroupItem(model, new[] { source, target }, _settingsManager);
        AddTileGroupItem(groupItem);

        source.IsSelected = false;
        target.IsSelected = false;
        StartupApplications.Remove(source);
        StartupApplications.Remove(target);

        OnPropertyChanged(nameof(HasNoStartupApplications));
        RefreshSelectionState();
    }

    /// <summary>
    /// ドラッグ&ドロップで既存タイルグループにアプリを追加する。
    /// </summary>
    public void AddAppToTileGroupFromDrop(StartupAppItem app, StartupTileGroupItem group)
    {
        if (!group.CanAddApp) return;
        if (!StartupApplications.Contains(app)) return;

        group.AddApp(app);
        app.IsSelected = false;
        StartupApplications.Remove(app);

        OnPropertyChanged(nameof(HasNoStartupApplications));
        RefreshSelectionState();
    }
}
