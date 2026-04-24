using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using WindowzTabManager.Models;
using WindowzTabManager.Services;
using WindowzTabManager.ViewModels;
using WindowzTabManager.Views;

namespace WindowzTabManager;

public partial class MainWindow
{
    private void AddWindowButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.OpenWindowPickerCommand.Execute(null);
    }

    private CommandPaletteWindow EnsureCommandPaletteWindow()
    {
        if (_commandPaletteWindow != null)
        {
            return _commandPaletteWindow;
        }

        var window = new CommandPaletteWindow();
        window.PaletteControl.DataContext = _commandPaletteViewModel;
        window.Deactivated += CommandPaletteWindow_Deactivated;
        window.Closed += CommandPaletteWindow_Closed;

        _commandPaletteWindow = window;
        return window;
    }

    private void CommandPaletteWindow_Deactivated(object? sender, EventArgs e)
    {
        if (_viewModel.IsCommandPaletteOpen)
        {
            _skipManagedWindowRestoreAfterPaletteClose = true;
            _viewModel.CloseCommandPaletteCommand.Execute(null);
        }
    }

    private void CommandPaletteWindow_Closed(object? sender, EventArgs e)
    {
        if (_commandPaletteWindow == null)
        {
            return;
        }

        _commandPaletteWindow.Deactivated -= CommandPaletteWindow_Deactivated;
        _commandPaletteWindow.Closed -= CommandPaletteWindow_Closed;
        _commandPaletteWindow = null;
    }

    private void ShowCommandPaletteWindow()
    {
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;

        _skipManagedWindowRestoreAfterPaletteClose = false;
        _commandPaletteViewModel.Open();

        var window = EnsureCommandPaletteWindow();
        UpdateCommandPaletteWindowPlacement(window);

        if (!window.IsVisible)
        {
            window.Show();
        }

        window.Activate();
        UpdateCommandPaletteWindowPlacement(window);

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (!_viewModel.IsCommandPaletteOpen || _commandPaletteWindow != window)
                return;

            UpdateCommandPaletteWindowPlacement(window);
            window.RequestSearchBoxFocus();
        });
    }

    private void HideCommandPaletteWindow()
    {
        if (_commandPaletteWindow?.IsVisible == true)
        {
            _commandPaletteWindow.Hide();
        }
    }

    private void UpdateCommandPaletteWindowPlacement()
    {
        if (_commandPaletteWindow == null)
            return;

        UpdateCommandPaletteWindowPlacement(_commandPaletteWindow);
    }

    private void UpdateCommandPaletteWindowPlacement(CommandPaletteWindow window)
    {
        double paletteWidth = window.ActualWidth > 0 ? window.ActualWidth : window.Width;
        if (double.IsNaN(paletteWidth) || paletteWidth <= 0)
            paletteWidth = 500;

        window.Left = Left + Math.Max(0, (ActualWidth - paletteWidth) / 2);
        window.Top = Top + 80;
    }

    private void RestoreEmbeddedWindow()
    {
        if (_viewModel.IsWebTabActive)
            ShowWebTab(_viewModel.ActiveWebTabId);

        UpdateManagedWindowLayout(activate: true);
    }

    private void OnCommandPaletteItemExecuted(object? sender, CommandPaletteItem item)
    {
        _viewModel.CloseCommandPaletteCommand.Execute(null);
        RestoreEmbeddedWindow();

        switch (item.Tag)
        {
            case QuickLaunchApp app:
                if (SettingsManager.IsUrl(app.Path))
                {
                    _viewModel.OpenWebTabCommand.Execute(app.Path);
                }
                else
                {
                    // Launch from command palette without reopening the picker overlay.
                    _viewModel.CloseWindowPickerCommand.Execute(null);
                    var pickerVm = (WindowPickerViewModel)WindowPickerControl.DataContext;
                    pickerVm.LaunchQuickAppCommand.Execute(app);
                }
                break;

            case QuickLaunchTileGroupSetting tileGroup:
                // タイルグループ起動（ピッカーは開かずバックグラウンドで起動）
                _viewModel.CloseWindowPickerCommand.Execute(null);
                var tilePickerVm = (WindowPickerViewModel)WindowPickerControl.DataContext;
                tilePickerVm.LaunchQuickTileGroupCommand.Execute(tileGroup);
                break;

            case Models.TabItem tab:
                _viewModel.SelectTabCommand.Execute(tab);
                break;

            case HotkeyAction action:
                switch (action)
                {
                    case HotkeyAction.NewTab:
                        _viewModel.OpenWindowPickerCommand.Execute(null);
                        break;
                    case HotkeyAction.CloseTab:
                        if (_viewModel.SelectedTab != null)
                            _viewModel.CloseTabCommand.Execute(_viewModel.SelectedTab);
                        break;
                }
                break;

            case ApplicationLaunchItem launchItem:
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = launchItem.FilePath,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to launch {launchItem.Name}: {ex.Message}");
                }
                break;

            case string s:
                HandleStringCommand(s);
                break;
        }
    }

    private void HandleStringCommand(string command)
    {
        switch (command)
        {
            case "GeneralSettings":
                OpenSettingsTab("GeneralSettings");
                break;
            case "WindowClose":
                Close();
                break;
            case "WindowMinimize":
                WindowState = WindowState.Minimized;
                break;
            case "WindowMaximize":
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                break;
            case "TabBarToggleCollapse":
                ToggleTabBarCollapsed();
                break;
            case "GroupExpandAll":
                foreach (var group in _tabManager.Groups)
                    group.IsExpanded = true;
                break;
            case "GroupCollapseAll":
                foreach (var group in _tabManager.Groups)
                    group.IsExpanded = false;
                break;
            case "ArrangeTopmostWindows":
                App.GetService<WindowManager>().ArrangeTopmostWindows();
                break;
        }
    }

}
