using System.Diagnostics;
using System.Windows;
using System.Windows.Media.Imaging;
using WindowzTabManager.Models;
using WindowzTabManager.Services;
using WindowzTabManager.ViewModels;

namespace WindowzTabManager;

public partial class WindowPickerWindow : Window
{
    private readonly WindowPickerViewModel _viewModel;

    internal ManagedWindow? SelectedWindow { get; private set; }

    public WindowPickerWindow(IEnumerable<IntPtr> excludedHandles)
    {
        var windowManager = new WindowManager(App.SettingsManager, excludedHandles);
        _viewModel = new WindowPickerViewModel(windowManager, App.SettingsManager);

        InitializeComponent();
        WindowPickerControl.DataContext = _viewModel;

        Loaded += OnLoaded;
        Closed += OnClosed;

        _viewModel.WindowSelected += OnWindowSelected;
        _viewModel.Cancelled += OnCancelled;
        _viewModel.WebTabRequested += OnWebTabRequested;
        _viewModel.QuickLaunchSettingsRequested += OnQuickLaunchSettingsRequested;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _viewModel.Start();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.WindowSelected -= OnWindowSelected;
        _viewModel.Cancelled -= OnCancelled;
        _viewModel.WebTabRequested -= OnWebTabRequested;
        _viewModel.QuickLaunchSettingsRequested -= OnQuickLaunchSettingsRequested;
        _viewModel.Stop();
    }

    private void OnWindowSelected(object? sender, WindowInfo window)
    {
        var managedWindow = new ManagedWindow
        {
            Handle = window.Handle,
            Title = window.Title,
            Icon = window.Icon as BitmapSource,
            ProcessId = window.ProcessId,
            ProcessName = window.ProcessName,
            ExecutablePath = window.ExecutablePath
        };
        if (string.IsNullOrWhiteSpace(managedWindow.Title))
            managedWindow.RefreshTitle();
        if (managedWindow.ProcessId == 0)
            managedWindow.RefreshProcessInfo();

        SelectedWindow = managedWindow;
        DialogResult = true;
        Close();
    }

    private void OnCancelled(object? sender, EventArgs e)
    {
        if (DialogResult == null)
            DialogResult = false;
        Close();
    }

    private void OnWebTabRequested(object? sender, string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore launch failures.
        }

        DialogResult = false;
        Close();
    }

    private void OnQuickLaunchSettingsRequested(object? sender, EventArgs e)
    {
        var settingsWindow = new SettingsWindow(App.SettingsManager)
        {
            Owner = this
        };
        settingsWindow.ShowDialog();
        _viewModel.Start();
    }
}
