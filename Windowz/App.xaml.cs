using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using System.Security.Principal;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using WindowzTabManager.Services;
using WindowzTabManager.ViewModels;

namespace WindowzTabManager;

public partial class App : Application
{
    private readonly IServiceProvider _serviceProvider;

    internal static SettingsManager SettingsManager => GetService<SettingsManager>();

    public App()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // Services
        services.AddSingleton<WindowManager>();
        services.AddSingleton<ProcessTracker>();
        services.AddSingleton<TabManager>();
        services.AddSingleton<SettingsManager>();
        services.AddSingleton<HotkeyManager>();
        services.AddSingleton<WebViewEnvironmentService>();

        // ViewModels
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<WindowPickerViewModel>();
        services.AddSingleton<GeneralSettingsViewModel>();
        services.AddSingleton<HotkeySettingsViewModel>();
        services.AddSingleton<StartupSettingsViewModel>();
        services.AddSingleton<QuickLaunchSettingsViewModel>();
        services.AddSingleton<ProcessInfoViewModel>(sp =>
        {
            var tabManager = sp.GetRequiredService<TabManager>();
            return new ProcessInfoViewModel(() =>
                tabManager.Tabs
                    .Where(t => !t.IsContentTab && !t.IsWebTab && t.Window != null)
                    .Select(t => new ManagedWindow
                    {
                        Handle = t.Window!.Handle,
                        Title = t.Window!.Title,
                        ProcessId = t.Window!.ProcessId,
                        ProcessName = t.Window!.ProcessName,
                        ExecutablePath = t.Window!.ExecutablePath
                    })
                    .ToList());
        });
        services.AddSingleton<CommandPaletteViewModel>();

        // Views
        services.AddSingleton<MainWindow>();
        services.AddSingleton<Views.Settings.GeneralSettingsPage>();
        services.AddSingleton<Views.Settings.HotkeySettingsPage>();
        services.AddSingleton<Views.Settings.StartupSettingsPage>();
        services.AddSingleton<Views.Settings.QuickLaunchSettingsPage>();
        services.AddSingleton<Views.Settings.ProcessInfoPage>();
        services.AddSingleton<Views.Settings.SettingsTabsPage>();
    }

    public static bool IsRunningAsAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Register last-resort cleanup for abnormal exit (e.g. unhandled exceptions).
        // Note: This does NOT fire when the process is killed via Process.Kill/taskkill.
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

        var settingsManager = _serviceProvider.GetRequiredService<SettingsManager>();
        var settings = settingsManager.Settings;

        // Re-launch as admin if the setting is enabled and we're not already elevated
        if (settings.RunAsAdmin && !IsRunningAsAdmin())
        {
            try
            {
                var exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath))
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = exePath,
                        UseShellExecute = true,
                        Verb = "runas"
                    };
                    Process.Start(startInfo);
                }
            }
            catch
            {
                // User cancelled UAC prompt - continue without admin.
            }

            Shutdown();
            return;
        }

        Wpf.Ui.Appearance.ApplicationThemeManager.Apply(Wpf.Ui.Appearance.ApplicationTheme.Dark);

        if (settings.UseSystemAccent)
        {
            Wpf.Ui.Appearance.ApplicationAccentColorManager.ApplySystemAccent();
        }
        else
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(settings.AccentColor);
                Wpf.Ui.Appearance.ApplicationAccentColorManager.Apply(color, Wpf.Ui.Appearance.ApplicationTheme.Dark);
            }
            catch
            {
                // Ignore invalid colors and keep default accent.
            }
        }

        GeneralSettingsViewModel.ApplyBackgroundColorStatic(settings.BackgroundColor);

        var processTracker = _serviceProvider.GetRequiredService<ProcessTracker>();
        processTracker.CleanupZombies();

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();

        // Snapshot existing handles before startup launches, to detect new windows.
        var windowManager = _serviceProvider.GetRequiredService<WindowManager>();
        var preExistingWindows = new HashSet<IntPtr>(
            windowManager.EnumerateWindows().Select(w => w.Handle));

        var (processConfigs, urlApps) = settingsManager.LaunchStartupApplications();

        var viewModel = _serviceProvider.GetRequiredService<MainViewModel>();
        var tabManager = _serviceProvider.GetRequiredService<TabManager>();

        foreach (var urlApp in urlApps)
        {
            var webTab = tabManager.AddWebTab(urlApp.Path, activate: false);
            webTab.IsLaunchedAtStartup = true;
        }

        if (urlApps.Count > 0 || processConfigs.Count > 0)
        {
            await viewModel.EmbedStartupProcessesAsync(
                processConfigs,
                urlApps,
                settingsManager.Settings,
                preExistingWindows);
            viewModel.ApplyStartupTabOrder(settingsManager.Settings.StartupApplications);
        }

        var windowHandle = new WindowInteropHelper(mainWindow).Handle;
        if (windowHandle != IntPtr.Zero)
        {
            NativeMethods.ForceForegroundWindow(windowHandle);
        }

        mainWindow.Activate();
        mainWindow.Focus();
    }

    private void OnProcessExit(object? sender, EventArgs e)
    {
        try
        {
            var tabManager = _serviceProvider.GetService<TabManager>();
            if (tabManager == null)
                return;

            var pids = tabManager.GetTrackedProcessIds();
            foreach (var pid in pids)
            {
                try
                {
                    using var proc = Process.GetProcessById(pid);
                    if (!proc.HasExited)
                    {
                        proc.Kill();
                    }
                }
                catch
                {
                    // Process already exited or access denied.
                }
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    public static T GetService<T>() where T : class
    {
        var app = (App)Current;
        return app._serviceProvider.GetRequiredService<T>();
    }
}
