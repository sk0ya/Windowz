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
}

public partial class StartupSettingsViewModel : ObservableObject
{
    private readonly SettingsManager _settingsManager;

    [ObservableProperty]
    private ObservableCollection<StartupAppItem> _startupApplications = new();

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

        OnPropertyChanged(nameof(HasNoStartupApplications));
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

        _settingsManager.RemoveStartupApplication(item.Model);
        StartupApplications.Remove(item);
        OnPropertyChanged(nameof(HasNoStartupApplications));
    }
}
