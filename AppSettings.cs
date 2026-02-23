namespace WindowzTabManager;

public sealed class AppSettings
{
    public bool RunAtWindowsStartup { get; set; }

    public bool RunAsAdmin { get; set; }

    public List<StartupApplicationSetting> StartupApplications { get; set; } = new();

    public List<StartupGroupSetting> StartupGroups { get; set; } = new();

    /// <summary>
    /// "None" = release windows to desktop (default),
    /// "All" = close all tab windows,
    /// "StartupOnly" = close only windows launched at startup
    /// </summary>
    public string CloseWindowsOnExit { get; set; } = "None";

    public List<QuickLaunchAppSetting> QuickLaunchApps { get; set; } = new();

    public string TabHeaderPosition { get; set; } = "Top";

    /// <summary>
    /// "CloseApp" = close the embedded application (default),
    /// "ReleaseEmbed" = release embedding and restore to desktop,
    /// "CloseWind" = close Wind application
    /// </summary>
    public string EmbedCloseAction { get; set; } = "CloseApp";

    /// <summary>
    /// True = hide embedded apps from taskbar while hosted in Wind (default),
    /// False = keep embedded apps visible in taskbar.
    /// </summary>
    public bool HideEmbeddedFromTaskbar { get; set; } = true;

    /// <summary>
    /// True = automatically embed any new top-level window that appears on the system,
    /// False = do not auto-embed (default).
    /// </summary>
    public bool AutoEmbedNewWindows { get; set; }

    /// <summary>
    /// Executable paths excluded from auto-embedding.
    /// </summary>
    public List<string> AutoEmbedExcludedExecutables { get; set; } = new();

    public string AccentColor { get; set; } = "#0078D4";

    public bool UseSystemAccent { get; set; }

    public string BackgroundColor { get; set; } = string.Empty;

    public List<HotkeyBindingSetting> CustomHotkeys { get; set; } = new();
}

public sealed class StartupApplicationSetting
{
    public string Path { get; set; } = string.Empty;

    public string Arguments { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Group { get; set; }

    public string? Tile { get; set; }

    public int? TilePosition { get; set; }
}

public sealed class StartupGroupSetting
{
    public string Name { get; set; } = string.Empty;

    public string Color { get; set; } = "#6495ED";
}

public sealed class QuickLaunchAppSetting
{
    public string Path { get; set; } = string.Empty;

    public string Arguments { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public bool ShouldEmbed { get; set; } = true;
}

public sealed class HotkeyBindingSetting
{
    public string Action { get; set; } = string.Empty;

    public string Modifiers { get; set; } = string.Empty;

    public string Key { get; set; } = string.Empty;
}
