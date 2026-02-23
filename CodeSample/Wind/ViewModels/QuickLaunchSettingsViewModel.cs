using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Wind.Converters;
using Wind.Models;
using Wind.Services;

namespace Wind.ViewModels;

public partial class QuickLaunchAppItem : ObservableObject
{
    private readonly QuickLaunchApp _app;
    private readonly SettingsManager _settingsManager;
    private ImageSource? _icon;

    public QuickLaunchAppItem(QuickLaunchApp app, SettingsManager settingsManager)
    {
        _app = app;
        _settingsManager = settingsManager;
        _icon = PathToIconConverter.GetIconForPath(app.Path);
    }

    public QuickLaunchApp Model => _app;

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

public partial class QuickLaunchSettingsViewModel : ObservableObject
{
    private readonly SettingsManager _settingsManager;

    [ObservableProperty]
    private ObservableCollection<QuickLaunchAppItem> _quickLaunchApps = new();

    [ObservableProperty]
    private string _newQuickLaunchPath = string.Empty;

    [ObservableProperty]
    private ObservableCollection<string> _pathSuggestions = new();

    [ObservableProperty]
    private bool _isSuggestionsOpen;

    [ObservableProperty]
    private string? _selectedSuggestion;

    public bool HasNoQuickLaunchApps => QuickLaunchApps.Count == 0;

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
        QuickLaunchApps.Clear();
        foreach (var app in _settingsManager.Settings.QuickLaunchApps)
        {
            QuickLaunchApps.Add(new QuickLaunchAppItem(app, _settingsManager));
        }
        OnPropertyChanged(nameof(HasNoQuickLaunchApps));
    }

    public void Reload()
    {
        LoadSettings();
    }

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
                    {
                        names.Add(Path.GetFileNameWithoutExtension(file));
                    }
                }
            }
            catch
            {
                // skip inaccessible directories
            }
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
        {
            PathSuggestions.Add(m);
        }

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
                {
                    results.Add(d + "\\");
                }
                if (results.Count >= 15) break;
            }

            foreach (var f in Directory.EnumerateFiles(dir))
            {
                var name = Path.GetFileName(f);
                if (name.Contains(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(f);
                }
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
        {
            OnNewQuickLaunchPathChanged(value);
        }
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
        {
            NewQuickLaunchPath = dialog.FileName;
        }
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
        OnPropertyChanged(nameof(HasNoQuickLaunchApps));
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

    [RelayCommand]
    private void RemoveQuickLaunchApp(QuickLaunchAppItem? item)
    {
        if (item == null) return;

        _settingsManager.RemoveQuickLaunchApp(item.Model);
        QuickLaunchApps.Remove(item);
        OnPropertyChanged(nameof(HasNoQuickLaunchApps));
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
}
