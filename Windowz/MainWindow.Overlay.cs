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

        if (_viewModel.IsWebTabActive && _currentWebTabId.HasValue)
        {
            if (_webTabControls.TryGetValue(_currentWebTabId.Value, out var webControl))
                webControl.Visibility = Visibility.Hidden;
        }

        UpdateManagedWindowLayout(activate: false);
    }

    private void RestoreEmbeddedWindow()
    {
        if (_viewModel.IsWebTabActive && _currentWebTabId.HasValue)
        {
            if (_webTabControls.TryGetValue(_currentWebTabId.Value, out var webControl))
                webControl.Visibility = Visibility.Visible;
        }

        UpdateManagedWindowLayout(activate: true);

        RequestEmbeddedContentRedraw();
    }

    private void RequestEmbeddedContentRedraw()
    {
        // Overlay close直後の再描画ゆらぎを2回のディスパッチで吸収する。
        Dispatcher.BeginInvoke(DispatcherPriority.Render, ForceEmbeddedContentRedraw);
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, ForceEmbeddedContentRedraw);
    }

    private void ForceEmbeddedContentRedraw()
    {
        if (_viewModel.IsCommandPaletteOpen || _viewModel.IsWindowPickerOpen)
            return;

        if (_viewModel.IsWebTabActive && _currentWebTabId.HasValue)
        {
            if (_webTabControls.TryGetValue(_currentWebTabId.Value, out var webControl))
            {
                webControl.InvalidateVisual();
                webControl.UpdateLayout();
            }
            return;
        }
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

    private void CommandPaletteOverlay_BackgroundClick(object sender, MouseButtonEventArgs e)
    {
        if (!CommandPaletteControl.IsMouseOver)
        {
            _viewModel.CloseCommandPaletteCommand.Execute(null);
        }
    }
}
