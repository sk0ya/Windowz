using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WindowzTabManager.Services;
using WindowzTabManager.Views;
namespace WindowzTabManager;

public partial class MainWindow
{
    private void MenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.ContextMenu != null)
        {
            btn.ContextMenu.PlacementTarget = btn;
            btn.ContextMenu.IsOpen = true;
        }
    }

    private void OpenGeneralSettings_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.OpenContentTabCommand.Execute("GeneralSettings");
    }

    private void OpenHotkeySettings_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.OpenContentTabCommand.Execute("HotkeySettings");
    }

    private void OpenProcessInfo_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.OpenContentTabCommand.Execute("ProcessInfo");
    }

    private void OpenStartupSettings_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.OpenContentTabCommand.Execute("StartupSettings");
    }

    private void OpenQuickLaunchSettings_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.OpenContentTabCommand.Execute("QuickLaunchSettings");
    }

    private void OpenWebTab_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.OpenWebTabCommand.Execute(null);
    }

    private void TabItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e == null) throw new ArgumentNullException(nameof(e));
        if (sender is FrameworkElement element && element.Tag is Models.TabItem tab)
        {
            if (e.ClickCount == 2 && _currentTabPosition is "Left" or "Right")
            {
                ToggleTabBarCollapsed();
                e.Handled = true;
                return;
            }

            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                _viewModel.ToggleMultiSelectCommand.Execute(tab);
            }
            else
            {
                _tabManager.ClearMultiSelection();
                _viewModel.SelectTabCommand.Execute(tab);

                // ドラッグ開始を追跡
                _dragTab = tab;
                _tabDragStartPoint = e.GetPosition(this);
                _isDraggingTab = false;
            }

            e.Handled = true;
        }
    }

    private void TabItem_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle && sender is FrameworkElement element && element.Tag is Models.TabItem tab)
        {
            _viewModel.CloseTabCommand.Execute(tab);
            e.Handled = true;
        }
    }

    private void TabItem_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is Models.TabItem tab)
        {
            // If right-clicked tab is not multi-selected and no other tabs are multi-selected,
            // auto-select it for the context menu
            var selected = _tabManager.GetMultiSelectedTabs();
            if (selected.Count == 0)
            {
                tab.IsMultiSelected = true;
            }
        }
    }

    private void CloseTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is Models.TabItem tab)
        {
            _viewModel.CloseTabCommand.Execute(tab);
        }

        e.Handled = true;
    }

    private void TabContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu || menu.Tag is not Models.TabItem tab) return;

        bool isWindowTab = !tab.IsContentTab && !tab.IsWebTab && tab.Window != null;
        string? exePath = tab.Window?.ExecutablePath;
        string? path = tab.IsWebTab ? tab.WebUrl : exePath;

        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            var header = item.Header?.ToString() ?? "";

            if (header.StartsWith("Startup"))
            {
                if (!string.IsNullOrEmpty(path) && (isWindowTab || tab.IsWebTab))
                {
                    item.Visibility = Visibility.Visible;
                    bool isRegistered = _settingsManager.IsInStartupApplications(path);
                    item.Header = isRegistered ? "Startup から削除" : "Startup に登録";
                }
                else
                {
                    item.Visibility = Visibility.Collapsed;
                }
            }
            else if (header.StartsWith("QuickLaunch"))
            {
                if (!string.IsNullOrEmpty(path) && (isWindowTab || tab.IsWebTab))
                {
                    item.Visibility = Visibility.Visible;
                    bool isRegistered = _settingsManager.IsInQuickLaunchApps(path);
                    item.Header = isRegistered ? "QuickLaunch から削除" : "QuickLaunch に登録";
                }
                else
                {
                    item.Visibility = Visibility.Collapsed;
                }
            }
            else if (header is "ファイルパスをコピー" or "エクスプローラーで開く" or "タブ名を変更" or "タブ管理を解除")
            {
                item.Visibility = isWindowTab ? Visibility.Visible : Visibility.Collapsed;
            }
            else if (header.StartsWith("自動埋め込み"))
            {
                bool autoEmbedEnabled = _settingsManager.Settings.AutoEmbedNewWindows;
                if (isWindowTab && !string.IsNullOrEmpty(exePath) && autoEmbedEnabled)
                {
                    item.Visibility = Visibility.Visible;
                    bool isExcluded = _settingsManager.IsAutoEmbedExcluded(exePath);
                    item.Header = isExcluded ? "自動埋め込みの除外を解除" : "自動埋め込みから除外";
                }
                else
                {
                    item.Visibility = Visibility.Collapsed;
                }
            }
        }
    }

    private void ReleaseEmbed_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement el || el.Tag is not Models.TabItem tab) return;
        if (tab.IsContentTab || tab.IsWebTab) return;

        _tabManager.RemoveTab(tab);
        _viewModel.StatusMessage = $"タブ管理を解除: {tab.DisplayTitle}";
    }

    private void ToggleStartup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement el || el.Tag is not Models.TabItem tab) return;
        var path = tab.IsWebTab ? tab.WebUrl : tab.Window?.ExecutablePath;
        if (string.IsNullOrEmpty(path)) return;

        if (_settingsManager.IsInStartupApplications(path))
        {
            _settingsManager.RemoveStartupApplicationByPath(path);
            _viewModel.StatusMessage = $"Startup から削除: {tab.DisplayTitle}";
        }
        else
        {
            _settingsManager.AddStartupApplication(path, "", tab.DisplayTitle);
            _viewModel.StatusMessage = $"Startup に登録: {tab.DisplayTitle}";
        }
    }

    private void ToggleQuickLaunch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement el || el.Tag is not Models.TabItem tab) return;
        var path = tab.IsWebTab ? tab.WebUrl : tab.Window?.ExecutablePath;
        if (string.IsNullOrEmpty(path)) return;

        if (_settingsManager.IsInQuickLaunchApps(path))
        {
            _settingsManager.RemoveQuickLaunchAppByPath(path);
            _viewModel.StatusMessage = $"QuickLaunch から削除: {tab.DisplayTitle}";
        }
        else
        {
            _settingsManager.AddQuickLaunchApp(path, "", tab.DisplayTitle);
            _viewModel.StatusMessage = $"QuickLaunch に登録: {tab.DisplayTitle}";
        }
    }

    private void CopyExePath_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement el && el.Tag is Models.TabItem tab && !string.IsNullOrEmpty(tab.Window?.ExecutablePath))
        {
            Clipboard.SetText(tab.Window.ExecutablePath);
            _viewModel.StatusMessage = "パスをコピーしました";
        }
    }

    private void OpenInExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement el && el.Tag is Models.TabItem tab && !string.IsNullOrEmpty(tab.Window?.ExecutablePath))
        {
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{tab.Window.ExecutablePath}\"");
        }
    }

    private void ToggleAutoEmbedExclusion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement el || el.Tag is not Models.TabItem tab) return;
        var exePath = tab.Window?.ExecutablePath;
        if (string.IsNullOrEmpty(exePath)) return;

        if (_settingsManager.IsAutoEmbedExcluded(exePath))
        {
            _settingsManager.RemoveAutoEmbedExclusion(exePath);
            _viewModel.StatusMessage = $"自動埋め込みの除外を解除: {tab.DisplayTitle}";
        }
        else
        {
            _settingsManager.AddAutoEmbedExclusion(exePath);
            _viewModel.StatusMessage = $"自動埋め込みから除外: {tab.DisplayTitle}";
        }
    }

    private void RenameTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement el && el.Tag is Models.TabItem tab)
        {
            var dialog = new RenameDialog(tab.DisplayTitle)
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.ResultName))
            {
                tab.CustomTitle = dialog.ResultName;
                _viewModel.StatusMessage = $"タブ名を変更: {tab.DisplayTitle}";
            }
        }
    }
}
