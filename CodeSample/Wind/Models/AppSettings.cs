namespace Wind.Models;

public class AppSettings
{
    public bool RunAtWindowsStartup { get; set; } = false;
    public bool RunAsAdmin { get; set; } = false;
    public List<StartupApplication> StartupApplications { get; set; } = new();
    public List<StartupGroup> StartupGroups { get; set; } = new();
    /// <summary>
    /// "None" = release windows to desktop (default),
    /// "All" = close all tab windows,
    /// "StartupOnly" = close only windows launched at startup
    /// </summary>
    public string CloseWindowsOnExit { get; set; } = "None";
    public List<QuickLaunchApp> QuickLaunchApps { get; set; } = new();
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
    public bool AutoEmbedNewWindows { get; set; } = false;
    /// <summary>
    /// Executable paths excluded from auto-embedding.
    /// </summary>
    public List<string> AutoEmbedExcludedExecutables { get; set; } = new();
    public string AccentColor { get; set; } = "#0078D4";
    public bool UseSystemAccent { get; set; } = false;
    public string BackgroundColor { get; set; } = "";  // Empty = use theme default
    public List<HotkeyBindingSetting> CustomHotkeys { get; set; } = new();
}

public class HotkeyBindingSetting
{
    public string Action { get; set; } = "";
    public string Modifiers { get; set; } = "";
    public string Key { get; set; } = "";
}

public class StartupApplication
{
    public string Path { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Group { get; set; }
    public string? Tile { get; set; }
    public int? TilePosition { get; set; }
}

public class StartupGroup
{
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = "#6495ED";
}

public class QuickLaunchApp
{
    public string Path { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool ShouldEmbed { get; set; } = true;
}
