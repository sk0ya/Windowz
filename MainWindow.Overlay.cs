using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using WindowzTabManager.Models;
using WindowzTabManager.Services;
using WindowzTabManager.ViewModels;

namespace WindowzTabManager;

public partial class MainWindow
{
    private void AddWindowButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.OpenWindowPickerCommand.Execute(null);
        UpdateBackdropVisibility();
        // Hide embedded window(s) / web tab while picker is open
        if (_viewModel.IsTileVisible)
        {
            foreach (var host in _tiledHosts)
                host.Visibility = Visibility.Hidden;
        }
        else if (_viewModel.IsWebTabActive && _currentWebTabId.HasValue)
        {
            if (_webTabControls.TryGetValue(_currentWebTabId.Value, out var webControl))
                webControl.Visibility = Visibility.Hidden;
        }
        else if (_currentHost != null)
        {
            _currentHost.Visibility = Visibility.Hidden;
        }

        UpdateManagedWindowLayout(activate: false);
    }

    private void RestoreEmbeddedWindow()
    {
        if (_viewModel.IsTileVisible)
        {
            foreach (var host in _tiledHosts)
                host.Visibility = Visibility.Visible;
        }
        else if (_viewModel.IsWebTabActive && _currentWebTabId.HasValue)
        {
            if (_webTabControls.TryGetValue(_currentWebTabId.Value, out var webControl))
                webControl.Visibility = Visibility.Visible;
        }
        else if (_currentHost != null)
        {
            _currentHost.Visibility = Visibility.Visible;
        }

        UpdateBackdropVisibility();
        UpdateManagedWindowLayout(activate: true);

        RequestEmbeddedContentRedraw();
    }

    private void RequestEmbeddedContentRedraw()
    {
        // WS_POPUP + SetParent の埋め込みウィンドウは、親 HWND の再表示直後に
        // 再描画が間に合わないことがあるため、Render と ApplicationIdle の2回で補償する。
        Dispatcher.BeginInvoke(DispatcherPriority.Render, ForceEmbeddedContentRedraw);
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, ForceEmbeddedContentRedraw);
    }

    private void ForceEmbeddedContentRedraw()
    {
        if (_viewModel.IsCommandPaletteOpen || _viewModel.IsWindowPickerOpen)
            return;

        if (_viewModel.IsTileVisible)
        {
            foreach (var host in _tiledHosts)
                host.ForceRedraw();
            return;
        }

        if (_viewModel.IsWebTabActive && _currentWebTabId.HasValue)
        {
            if (_webTabControls.TryGetValue(_currentWebTabId.Value, out var webControl))
            {
                webControl.InvalidateVisual();
                webControl.UpdateLayout();
            }
            return;
        }

        _currentHost?.ForceRedraw();
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
                _viewModel.OpenContentTabCommand.Execute("GeneralSettings");
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

    private void CommandPaletteOverlay_BackgroundClick(object sender, MouseButtonEventArgs e)
    {
        if (!CommandPaletteControl.IsMouseOver)
        {
            _viewModel.CloseCommandPaletteCommand.Execute(null);
        }
    }
}
