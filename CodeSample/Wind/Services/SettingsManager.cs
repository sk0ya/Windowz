using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using Wind.Models;

namespace Wind.Services;

public class SettingsManager
{
    private const string RegistryKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "Wind";

    private readonly string _settingsFilePath;
    private readonly JsonSerializerOptions _jsonOptions;
    private AppSettings _settings;

    public AppSettings Settings => _settings;

    public event Action<string>? TabHeaderPositionChanged;
    public event Action<bool>? HideEmbeddedFromTaskbarChanged;
    public event Action<bool>? AutoEmbedNewWindowsChanged;
    public event Action? AutoEmbedExclusionsChanged;

    public SettingsManager()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Wind");

        Directory.CreateDirectory(appDataPath);

        _settingsFilePath = Path.Combine(appDataPath, "settings.json");

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        _settings = LoadSettings();
    }

    private AppSettings LoadSettings()
    {
        if (!File.Exists(_settingsFilePath))
            return new AppSettings();

        try
        {
            var json = File.ReadAllText(_settingsFilePath);
            return JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void SaveSettings()
    {
        try
        {
            var json = JsonSerializer.Serialize(_settings, _jsonOptions);
            File.WriteAllText(_settingsFilePath, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to save settings: {ex.Message}");
        }
    }

    public bool IsRunAtWindowsStartup()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, false);
            return key?.GetValue(AppName) != null;
        }
        catch
        {
            return false;
        }
    }

    public void SetRunAtWindowsStartup(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, true);
            if (key == null) return;

            if (enable)
            {
                var exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath))
                {
                    key.SetValue(AppName, $"\"{exePath}\"");
                }
            }
            else
            {
                key.DeleteValue(AppName, false);
            }

            _settings.RunAtWindowsStartup = enable;
            SaveSettings();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to set startup: {ex.Message}");
        }
    }

    public StartupApplication AddStartupApplication(string path, string arguments = "", string? name = null)
    {
        if (string.IsNullOrWhiteSpace(path)) return null!;

        var appName = name ?? (IsUrl(path) ? path : Path.GetFileNameWithoutExtension(path));

        var app = new StartupApplication
        {
            Path = path,
            Arguments = arguments,
            Name = appName
        };

        _settings.StartupApplications.Add(app);
        SaveSettings();

        return app;
    }

    public void RemoveStartupApplication(StartupApplication app)
    {
        if (_settings.StartupApplications.Remove(app))
        {
            SaveSettings();
        }
    }

    public void SaveStartupApplication()
    {
        SaveSettings();
    }

    public void AddStartupGroup(string name, string color)
    {
        if (string.IsNullOrWhiteSpace(name)) return;

        if (_settings.StartupGroups.Any(g => g.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            return;

        _settings.StartupGroups.Add(new StartupGroup
        {
            Name = name,
            Color = color
        });

        SaveSettings();
    }

    public void RemoveStartupGroup(string name)
    {
        var group = _settings.StartupGroups
            .FirstOrDefault(g => g.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        if (group != null)
        {
            _settings.StartupGroups.Remove(group);
            SaveSettings();
        }
    }

    public QuickLaunchApp AddQuickLaunchApp(string path, string arguments = "", string? name = null)
    {
        if (string.IsNullOrWhiteSpace(path)) return null!;

        string appName;
        if (name != null)
            appName = name;
        else if (IsUrl(path))
            appName = path;
        else
        {
            appName = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrEmpty(appName))
                appName = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        }

        var app = new QuickLaunchApp
        {
            Path = path,
            Arguments = arguments,
            Name = appName
        };

        _settings.QuickLaunchApps.Add(app);
        SaveSettings();

        return app;
    }

    public void RemoveQuickLaunchApp(QuickLaunchApp app)
    {
        if (_settings.QuickLaunchApps.Remove(app))
        {
            SaveSettings();
        }
    }

    public void SaveQuickLaunchApp()
    {
        SaveSettings();
    }

    public bool IsInStartupApplications(string? executablePath)
    {
        if (string.IsNullOrEmpty(executablePath)) return false;
        return _settings.StartupApplications.Any(a =>
            a.Path.Equals(executablePath, StringComparison.OrdinalIgnoreCase));
    }

    public bool IsInQuickLaunchApps(string? executablePath)
    {
        if (string.IsNullOrEmpty(executablePath)) return false;
        return _settings.QuickLaunchApps.Any(a =>
            a.Path.Equals(executablePath, StringComparison.OrdinalIgnoreCase));
    }

    public void RemoveStartupApplicationByPath(string path)
    {
        var app = _settings.StartupApplications.FirstOrDefault(a =>
            a.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (app != null) RemoveStartupApplication(app);
    }

    public void RemoveQuickLaunchAppByPath(string path)
    {
        var app = _settings.QuickLaunchApps.FirstOrDefault(a =>
            a.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (app != null) RemoveQuickLaunchApp(app);
    }

    public void SetTabHeaderPosition(string position)
    {
        _settings.TabHeaderPosition = position;
        SaveSettings();
        TabHeaderPositionChanged?.Invoke(position);
    }

    public void SetHideEmbeddedFromTaskbar(bool hideFromTaskbar)
    {
        if (_settings.HideEmbeddedFromTaskbar == hideFromTaskbar)
            return;

        _settings.HideEmbeddedFromTaskbar = hideFromTaskbar;
        SaveSettings();
        HideEmbeddedFromTaskbarChanged?.Invoke(hideFromTaskbar);
    }

    public void SetAutoEmbedNewWindows(bool enable)
    {
        if (_settings.AutoEmbedNewWindows == enable)
            return;

        _settings.AutoEmbedNewWindows = enable;
        SaveSettings();
        AutoEmbedNewWindowsChanged?.Invoke(enable);
    }

    public bool IsAutoEmbedExcluded(string? executablePath)
    {
        if (string.IsNullOrEmpty(executablePath)) return false;
        return _settings.AutoEmbedExcludedExecutables.Any(e =>
            e.Equals(executablePath, StringComparison.OrdinalIgnoreCase));
    }

    public void AddAutoEmbedExclusion(string executablePath)
    {
        if (string.IsNullOrEmpty(executablePath)) return;
        if (IsAutoEmbedExcluded(executablePath)) return;
        _settings.AutoEmbedExcludedExecutables.Add(executablePath);
        SaveSettings();
        AutoEmbedExclusionsChanged?.Invoke();
    }

    public void RemoveAutoEmbedExclusion(string executablePath)
    {
        var item = _settings.AutoEmbedExcludedExecutables
            .FirstOrDefault(e => e.Equals(executablePath, StringComparison.OrdinalIgnoreCase));
        if (item != null)
        {
            _settings.AutoEmbedExcludedExecutables.Remove(item);
            SaveSettings();
            AutoEmbedExclusionsChanged?.Invoke();
        }
    }

    public static bool IsUrl(string path)
    {
        return Uri.TryCreate(path, UriKind.Absolute, out var uri) &&
               (uri.Scheme == "http" || uri.Scheme == "https");
    }

    public (List<(Process Process, StartupApplication Config)> Processes, List<StartupApplication> UrlApps) LaunchStartupApplications()
    {
        var results = new List<(Process, StartupApplication)>();
        var urlApps = new List<StartupApplication>();

        foreach (var app in _settings.StartupApplications)
        {
            try
            {
                if (IsUrl(app.Path))
                {
                    urlApps.Add(app);
                    continue;
                }

                if (!File.Exists(app.Path)) continue;

                var startInfo = new ProcessStartInfo
                {
                    FileName = app.Path,
                    Arguments = app.Arguments,
                    UseShellExecute = true
                };

                var process = Process.Start(startInfo);
                if (process != null)
                {
                    results.Add((process, app));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to launch {app.Name}: {ex.Message}");
            }
        }

        return (results, urlApps);
    }
}
