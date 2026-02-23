using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using System.Security.Principal;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Wind.Interop;
using Wind.Services;
using Wind.ViewModels;
namespace Wind;

public partial class App : Application
{
    private readonly IServiceProvider _serviceProvider;

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
        services.AddSingleton<ProcessInfoViewModel>();
        services.AddSingleton<CommandPaletteViewModel>();

        // Views
        services.AddSingleton<Views.MainWindow>();
        services.AddSingleton<Views.Settings.GeneralSettingsPage>();
        services.AddSingleton<Views.Settings.HotkeySettingsPage>();
        services.AddSingleton<Views.Settings.StartupSettingsPage>();
        services.AddSingleton<Views.Settings.QuickLaunchSettingsPage>();
        services.AddSingleton<Views.Settings.ProcessInfoPage>();
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
                // User cancelled UAC prompt — continue without admin
            }
            Shutdown();
            return;
        }

        // Apply dark theme as base
        Wpf.Ui.Appearance.ApplicationThemeManager.Apply(Wpf.Ui.Appearance.ApplicationTheme.Dark);

        // Apply accent color
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
                // Invalid color, use default
            }
        }

        // Apply background color
        GeneralSettingsViewModel.ApplyBackgroundColorStatic(settings.BackgroundColor);

        // Kill zombie processes left over from a previous session that was force-killed.
        var processTracker = _serviceProvider.GetRequiredService<ProcessTracker>();
        processTracker.CleanupZombies();

        // Show main window first
        var mainWindow = _serviceProvider.GetRequiredService<Views.MainWindow>();
        mainWindow.Show();

        // Snapshot existing window handles before launching, so we can detect
        // newly created windows for processes like explorer.exe that delegate
        // to an already-running instance and exit immediately.
        var windowManager = _serviceProvider.GetRequiredService<WindowManager>();
        var preExistingWindows = new HashSet<IntPtr>(
            windowManager.EnumerateWindows().Select(w => w.Handle));

        // Launch startup applications and embed them
        var (processConfigs, urlApps) = settingsManager.LaunchStartupApplications();

        var viewModel = _serviceProvider.GetRequiredService<MainViewModel>();

        var tabManager = _serviceProvider.GetRequiredService<TabManager>();

        // URL startup items をスタートアップフラグ付きで Webタブとして開く
        foreach (var urlApp in urlApps)
        {
            var webTab = tabManager.AddWebTab(urlApp.Path, activate: false);
            webTab.IsLaunchedAtStartup = true;
        }

        if (processConfigs.Count > 0)
        {
            await viewModel.EmbedStartupProcessesAsync(processConfigs, settingsManager.Settings, preExistingWindows);
        }
        else if (urlApps.Count > 0)
        {
            // プロセス埋め込みがない場合は最後のタブをアクティブ化
            if (tabManager.Tabs.Count > 0)
                tabManager.ActiveTab = tabManager.Tabs.Last();
        }

        // 設定の順番通りにスタートアップタブを並び替える
        if (urlApps.Count > 0 || processConfigs.Count > 0)
        {
            viewModel.ApplyStartupTabOrder(settingsManager.Settings.StartupApplications);
        }

        // Startup apps may have stolen foreground focus.
        // Force Wind back to the foreground after embedding completes.
        var windHandle = new WindowInteropHelper(mainWindow).Handle;
        if (windHandle != IntPtr.Zero)
        {
            NativeMethods.ForceForegroundWindow(windHandle);
        }
        mainWindow.Activate();
        mainWindow.Focus();
    }

    private void OnProcessExit(object? sender, EventArgs e)
    {
        // Last-resort cleanup: if Wind exits without going through MainWindow_Closing
        // (e.g. Environment.FailFast, StackOverflow, or unhandled native exception),
        // try to release or kill embedded processes so they don't become invisible zombies.
        try
        {
            var tabManager = _serviceProvider.GetService<TabManager>();
            if (tabManager == null) return;

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
                    // Process already exited or access denied
                }
            }
        }
        catch
        {
            // Best-effort cleanup; ignore failures
        }
    }

    public static T GetService<T>() where T : class
    {
        var app = (App)Current;
        return app._serviceProvider.GetRequiredService<T>();
    }
}
