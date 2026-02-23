using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.IO;
using Microsoft.Win32;
using Wind.Converters;
using Wind.Models;
using Wind.Services;

namespace Wind.ViewModels;

public partial class StartupAppItem : ObservableObject
{
    private readonly StartupApplication _app;
    private readonly SettingsManager _settingsManager;
    private ImageSource? _icon;

    public StartupAppItem(StartupApplication app, SettingsManager settingsManager)
    {
        _app = app;
        _settingsManager = settingsManager;
        _icon = PathToIconConverter.GetIconForPath(app.Path);
    }

    public StartupApplication Model => _app;

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
}

public partial class TileSetItem : ObservableObject
{
    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private ObservableCollection<StartupApplication> _apps = new();

    [ObservableProperty]
    private StartupAppItem? _appToAdd;

    public bool HasNoApps => Apps.Count == 0;

    public TileSetItem(string name)
    {
        _name = name;
        _apps.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoApps));
    }
}

public partial class StartupSettingsViewModel : ObservableObject
{
    private readonly SettingsManager _settingsManager;

    [ObservableProperty]
    private ObservableCollection<StartupAppItem> _startupApplications = new();

    // Tile Sets
    [ObservableProperty]
    private ObservableCollection<TileSetItem> _tileSets = new();

    [ObservableProperty]
    private string _newTileSetName = string.Empty;

    [ObservableProperty]
    private string _newStartupPath = string.Empty;

    public bool HasNoStartupApplications => StartupApplications.Count == 0;

    public StartupSettingsViewModel(SettingsManager settingsManager)
    {
        _settingsManager = settingsManager;
        LoadSettings();
    }

    private void LoadSettings()
    {
        var settings = _settingsManager.Settings;

        StartupApplications.Clear();
        foreach (var app in settings.StartupApplications)
        {
            StartupApplications.Add(new StartupAppItem(app, _settingsManager));
        }

        RebuildTileSets();
        OnPropertyChanged(nameof(HasNoStartupApplications));
    }

    public void Reload()
    {
        LoadSettings();
    }

    private void RebuildTileSets()
    {
        TileSets.Clear();
        var tileGroups = StartupApplications
            .Select(item => item.Model)
            .Where(a => !string.IsNullOrEmpty(a.Tile))
            .GroupBy(a => a.Tile!)
            .OrderBy(g => g.Key);

        foreach (var group in tileGroups)
        {
            var tileSet = new TileSetItem(group.Key);
            foreach (var app in group.OrderBy(a => a.TilePosition ?? int.MaxValue))
            {
                tileSet.Apps.Add(app);
            }
            TileSets.Add(tileSet);
        }
    }

    [RelayCommand]
    private void AddStartupApplication()
    {
        if (string.IsNullOrWhiteSpace(NewStartupPath)) return;

        var path = NewStartupPath.Trim();
        var app = _settingsManager.AddStartupApplication(path);
        StartupApplications.Add(new StartupAppItem(app, _settingsManager));
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

        foreach (var ts in TileSets)
        {
            ts.Apps.Remove(item.Model);
        }

        _settingsManager.RemoveStartupApplication(item.Model);
        StartupApplications.Remove(item);
        OnPropertyChanged(nameof(HasNoStartupApplications));
    }

    // --- Tile Set commands ---

    public ObservableCollection<StartupApplication> GetAvailableAppsForTileSet()
    {
        var usedApps = TileSets.SelectMany(ts => ts.Apps).ToHashSet();
        var available = new ObservableCollection<StartupApplication>();
        foreach (var item in StartupApplications)
        {
            if (!usedApps.Contains(item.Model))
            {
                available.Add(item.Model);
            }
        }
        return available;
    }

    [RelayCommand]
    private void AddTileSet()
    {
        if (string.IsNullOrWhiteSpace(NewTileSetName)) return;
        if (TileSets.Any(ts => ts.Name.Equals(NewTileSetName, StringComparison.OrdinalIgnoreCase))) return;

        TileSets.Add(new TileSetItem(NewTileSetName));
        NewTileSetName = string.Empty;
    }

    [RelayCommand]
    private void RemoveTileSet(TileSetItem tileSet)
    {
        foreach (var app in tileSet.Apps)
        {
            app.Tile = null;
            app.TilePosition = null;
            _settingsManager.SaveStartupApplication();
        }
        TileSets.Remove(tileSet);
    }

    [RelayCommand]
    private void AddAppToTileSet(TileSetItem tileSet)
    {
        if (tileSet.AppToAdd == null) return;

        var app = tileSet.AppToAdd.Model;
        app.Tile = tileSet.Name;
        app.TilePosition = tileSet.Apps.Count;
        tileSet.Apps.Add(app);
        tileSet.AppToAdd = null;
        _settingsManager.SaveStartupApplication();
    }

    public void AddAppToTileSetDirect(TileSetItem tileSet, StartupAppItem appItem)
    {
        var app = appItem.Model;
        app.Tile = tileSet.Name;
        app.TilePosition = tileSet.Apps.Count;
        tileSet.Apps.Add(app);
        _settingsManager.SaveStartupApplication();
    }

    [RelayCommand]
    private void RemoveAppFromTileSet(StartupApplication app)
    {
        var tileSet = TileSets.FirstOrDefault(ts => ts.Apps.Contains(app));
        if (tileSet == null) return;

        app.Tile = null;
        app.TilePosition = null;
        tileSet.Apps.Remove(app);
        UpdateTilePositions(tileSet);
        _settingsManager.SaveStartupApplication();
    }

    [RelayCommand]
    private void MoveAppUp(StartupApplication app)
    {
        var tileSet = TileSets.FirstOrDefault(ts => ts.Apps.Contains(app));
        if (tileSet == null) return;

        var index = tileSet.Apps.IndexOf(app);
        if (index <= 0) return;

        tileSet.Apps.Move(index, index - 1);
        UpdateTilePositions(tileSet);
    }

    [RelayCommand]
    private void MoveAppDown(StartupApplication app)
    {
        var tileSet = TileSets.FirstOrDefault(ts => ts.Apps.Contains(app));
        if (tileSet == null) return;

        var index = tileSet.Apps.IndexOf(app);
        if (index < 0 || index >= tileSet.Apps.Count - 1) return;

        tileSet.Apps.Move(index, index + 1);
        UpdateTilePositions(tileSet);
    }

    private void UpdateTilePositions(TileSetItem tileSet)
    {
        for (int i = 0; i < tileSet.Apps.Count; i++)
        {
            tileSet.Apps[i].TilePosition = i;
        }
        _settingsManager.SaveStartupApplication();
    }
}
