using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace WindowzTabManager.ViewModels;

public partial class ApplicationLaunchFolderItem : ObservableObject
{
    private readonly ApplicationLaunchFolderSetting _model;
    private readonly SettingsManager _settingsManager;

    public ApplicationLaunchFolderItem(ApplicationLaunchFolderSetting model, SettingsManager settingsManager)
    {
        _model = model;
        _settingsManager = settingsManager;
    }

    public ApplicationLaunchFolderSetting Model => _model;

    public string FolderPath => _model.FolderPath;

    public string Name
    {
        get => _model.Name;
        set
        {
            if (_model.Name != value)
            {
                _model.Name = value;
                OnPropertyChanged();
                _settingsManager.SaveApplicationLaunchFolder();
            }
        }
    }
}

public partial class ApplicationLaunchSettingsViewModel : ObservableObject
{
    public event Action? BrowseDone;

    private readonly SettingsManager _settingsManager;

    [ObservableProperty]
    private ObservableCollection<ApplicationLaunchFolderItem> _folders = new();

    [ObservableProperty]
    private string _newFolderPath = string.Empty;

    public bool HasNoFolders => Folders.Count == 0;

    public ApplicationLaunchSettingsViewModel(SettingsManager settingsManager)
    {
        _settingsManager = settingsManager;
    }

    public void Reload()
    {
        Folders.Clear();
        foreach (var folder in _settingsManager.Settings.ApplicationLaunchFolders)
            Folders.Add(new ApplicationLaunchFolderItem(folder, _settingsManager));
        OnPropertyChanged(nameof(HasNoFolders));
    }

    [RelayCommand]
    private void BrowseFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "フォルダを選択"
        };

        if (dialog.ShowDialog() == true)
        {
            NewFolderPath = dialog.FolderName;
            AddFolderCommand.Execute(null);
        }

        BrowseDone?.Invoke();
    }

    [RelayCommand]
    private void AddFolder()
    {
        string path = NewFolderPath.Trim();
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return;

        if (Folders.Any(f => f.FolderPath.Equals(path, StringComparison.OrdinalIgnoreCase)))
            return;

        var setting = _settingsManager.AddApplicationLaunchFolder(path);
        Folders.Add(new ApplicationLaunchFolderItem(setting, _settingsManager));
        NewFolderPath = string.Empty;
        OnPropertyChanged(nameof(HasNoFolders));
    }

    [RelayCommand]
    private void RemoveFolder(ApplicationLaunchFolderItem? item)
    {
        if (item == null) return;
        _settingsManager.RemoveApplicationLaunchFolder(item.Model);
        Folders.Remove(item);
        OnPropertyChanged(nameof(HasNoFolders));
    }
}
