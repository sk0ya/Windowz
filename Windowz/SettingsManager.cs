using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace WindowzTabManager;

public sealed class SettingsManager
{
    private const string RegistryKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "WindowzTabManager";

    private readonly string _settingsFilePath;
    private readonly JsonSerializerOptions _jsonOptions;
    private AppSettings _settings;

    public AppSettings Settings => _settings;

    public event Action<string>? TabHeaderPositionChanged;
    public event Action<bool>? AutoEmbedNewWindowsChanged;
    public event Action? AutoEmbedExclusionsChanged;

    public SettingsManager()
        : this(null)
    {
    }

    public SettingsManager(string? settingsDirectory)
    {
        string appDataPath = string.IsNullOrWhiteSpace(settingsDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WindowzTabManager")
            : settingsDirectory;

        Directory.CreateDirectory(appDataPath);
        _settingsFilePath = Path.Combine(appDataPath, "settings.json");

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        _settings = LoadSettings();
        _settings.RunAtWindowsStartup = IsRunAtWindowsStartup();
    }

    private AppSettings LoadSettings()
    {
        if (!File.Exists(_settingsFilePath))
            return new AppSettings();

        try
        {
            string json = File.ReadAllText(_settingsFilePath);
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
            string json = JsonSerializer.Serialize(_settings, _jsonOptions);
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
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, false);
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
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, true);
            if (key == null)
                return;

            if (enable)
            {
                string? exePath = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(exePath))
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
            Debug.WriteLine($"Failed to set startup registration: {ex.Message}");
        }
    }

    public void SetStartupApplications(IEnumerable<StartupApplicationSetting> applications)
    {
        _settings.StartupApplications = applications
            .Where(a => !string.IsNullOrWhiteSpace(a.Path))
            .Select(a => new StartupApplicationSetting
            {
                Name = string.IsNullOrWhiteSpace(a.Name)
                    ? Path.GetFileNameWithoutExtension(a.Path)
                    : a.Name.Trim(),
                Path = a.Path.Trim(),
                Arguments = a.Arguments?.Trim() ?? string.Empty,
                Group = a.Group
            })
            .ToList();

        SaveSettings();
    }

    public void SetQuickLaunchApplications(IEnumerable<QuickLaunchAppSetting> applications)
    {
        _settings.QuickLaunchApps = applications
            .Where(a => !string.IsNullOrWhiteSpace(a.Path))
            .Select(a => new QuickLaunchAppSetting
            {
                Name = string.IsNullOrWhiteSpace(a.Name)
                    ? Path.GetFileNameWithoutExtension(a.Path)
                    : a.Name.Trim(),
                Path = a.Path.Trim(),
                Arguments = a.Arguments?.Trim() ?? string.Empty,
                ShouldEmbed = a.ShouldEmbed
            })
            .ToList();

        SaveSettings();
    }

    public StartupApplicationSetting AddStartupApplication(string path, string arguments = "", string? name = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null!;

        string appName = name ?? (IsUrl(path) ? path : Path.GetFileNameWithoutExtension(path));

        var app = new StartupApplicationSetting
        {
            Path = path,
            Arguments = arguments,
            Name = appName
        };

        _settings.StartupApplications.Add(app);
        SaveSettings();

        return app;
    }

    public void RemoveStartupApplication(StartupApplicationSetting app)
    {
        if (_settings.StartupApplications.Remove(app))
            SaveSettings();
    }

    public void SaveStartupApplication()
    {
        SaveSettings();
    }

    public void AddStartupGroup(string name, string color)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;

        if (_settings.StartupGroups.Any(g => g.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            return;

        _settings.StartupGroups.Add(new StartupGroupSetting
        {
            Name = name,
            Color = color
        });

        SaveSettings();
    }

    public void RemoveStartupGroup(string name)
    {
        StartupGroupSetting? group = _settings.StartupGroups
            .FirstOrDefault(g => g.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        if (group == null)
            return;

        _settings.StartupGroups.Remove(group);
        SaveSettings();
    }

    public StartupTileGroupSetting AddStartupTileGroup(List<string> appPaths)
    {
        var group = new StartupTileGroupSetting { AppPaths = appPaths };
        _settings.StartupTileGroups.Add(group);
        SaveSettings();
        return group;
    }

    public void RemoveStartupTileGroup(string id)
    {
        var group = _settings.StartupTileGroups.FirstOrDefault(g => g.Id == id);
        if (group != null)
        {
            _settings.StartupTileGroups.Remove(group);
            SaveSettings();
        }
    }

    public void UpdateStartupTileGroup(StartupTileGroupSetting group)
    {
        SaveSettings();
    }

    public bool IsInStartupTileGroup(string path)
    {
        return _settings.StartupTileGroups.Any(g =>
            g.AppPaths.Any(p => p.Equals(path, StringComparison.OrdinalIgnoreCase)));
    }

    public QuickLaunchAppSetting AddQuickLaunchApp(string path, string arguments = "", string? name = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null!;

        string appName;
        if (!string.IsNullOrWhiteSpace(name))
        {
            appName = name;
        }
        else if (IsUrl(path))
        {
            appName = path;
        }
        else
        {
            appName = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrWhiteSpace(appName))
                appName = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        }

        var app = new QuickLaunchAppSetting
        {
            Path = path,
            Arguments = arguments,
            Name = appName
        };

        _settings.QuickLaunchApps.Add(app);
        SaveSettings();

        return app;
    }

    public void RemoveQuickLaunchApp(QuickLaunchAppSetting app)
    {
        if (_settings.QuickLaunchApps.Remove(app))
            SaveSettings();
    }

    public void SaveQuickLaunchApp()
    {
        SaveSettings();
    }

    public bool IsInStartupApplications(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            return false;

        return _settings.StartupApplications.Any(a =>
            a.Path.Equals(executablePath, StringComparison.OrdinalIgnoreCase));
    }

    public bool IsInQuickLaunchApps(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            return false;

        return _settings.QuickLaunchApps.Any(a =>
            a.Path.Equals(executablePath, StringComparison.OrdinalIgnoreCase));
    }

    public void RemoveStartupApplicationByPath(string path)
    {
        StartupApplicationSetting? app = _settings.StartupApplications.FirstOrDefault(a =>
            a.Path.Equals(path, StringComparison.OrdinalIgnoreCase));

        if (app != null)
            RemoveStartupApplication(app);
    }

    public void RemoveQuickLaunchAppByPath(string path)
    {
        QuickLaunchAppSetting? app = _settings.QuickLaunchApps.FirstOrDefault(a =>
            a.Path.Equals(path, StringComparison.OrdinalIgnoreCase));

        if (app != null)
            RemoveQuickLaunchApp(app);
    }

    public void SetTabHeaderPosition(string position)
    {
        _settings.TabHeaderPosition = position;
        SaveSettings();
        TabHeaderPositionChanged?.Invoke(position);
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
        if (string.IsNullOrWhiteSpace(executablePath))
            return false;

        return _settings.AutoEmbedExcludedExecutables.Any(e =>
            e.Equals(executablePath, StringComparison.OrdinalIgnoreCase));
    }

    public void AddAutoEmbedExclusion(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            return;
        if (IsAutoEmbedExcluded(executablePath))
            return;

        _settings.AutoEmbedExcludedExecutables.Add(executablePath);
        SaveSettings();
        AutoEmbedExclusionsChanged?.Invoke();
    }

    public void RemoveAutoEmbedExclusion(string executablePath)
    {
        string? item = _settings.AutoEmbedExcludedExecutables
            .FirstOrDefault(e => e.Equals(executablePath, StringComparison.OrdinalIgnoreCase));

        if (item == null)
            return;

        _settings.AutoEmbedExcludedExecutables.Remove(item);
        SaveSettings();
        AutoEmbedExclusionsChanged?.Invoke();
    }

    public static bool IsUrl(string path)
    {
        return Uri.TryCreate(path, UriKind.Absolute, out Uri? uri)
               && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    public (List<(Process Process, StartupApplicationSetting Config)> Processes,
        List<StartupApplicationSetting> UrlApps) LaunchStartupApplications()
    {
        var processes = new List<(Process, StartupApplicationSetting)>();
        var urlApps = new List<StartupApplicationSetting>();

        foreach (var app in _settings.StartupApplications)
        {
            try
            {
                if (IsUrl(app.Path))
                {
                    urlApps.Add(app);
                    continue;
                }

                if (!File.Exists(app.Path))
                    continue;

                var startInfo = new ProcessStartInfo
                {
                    FileName = app.Path,
                    Arguments = app.Arguments,
                    UseShellExecute = true
                };

                Process? process = Process.Start(startInfo);
                if (process != null)
                    processes.Add((process, app));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to launch {app.Name}: {ex.Message}");
            }
        }

        return (processes, urlApps);
    }
}
