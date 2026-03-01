using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using WindowzTabManager.Services;
using WindowzTabManager.ViewModels;
using WindowzTabManager.Views;
using WindowzTabManager.Views.Settings;

namespace WindowzTabManager;

public partial class MainWindow
{
    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.ActiveContentKey))
        {
            // Content tab switched (e.g. Settings → Startup Settings)
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                // タイル表示中は通常の表示切り替えをスキップ
                if (_viewModel.SelectedTab?.TileLayout != null)
                    return;

                if (_viewModel.IsContentTabActive)
                {
                    ShowContentTab(_viewModel.ActiveContentKey);
                    UpdateManagedWindowLayout(activate: false);
                }
            });
        }
        else if (e.PropertyName == nameof(MainViewModel.IsContentTabActive))
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                // タイル表示中は通常の表示切り替えをスキップ
                if (_viewModel.SelectedTab?.TileLayout != null)
                {
                    UpdateManagedWindowLayout(activate: false);
                    return;
                }

                if (_viewModel.IsContentTabActive)
                {
                    // タイル解除後のレイアウトリセット
                    ContentTabContainer.HorizontalAlignment = HorizontalAlignment.Stretch;
                    ContentTabContainer.VerticalAlignment = VerticalAlignment.Stretch;
                    ContentTabContainer.Width = double.NaN;
                    ContentTabContainer.Height = double.NaN;
                    ContentTabContainer.Margin = new System.Windows.Thickness(0);

                    // Hide managed-window area and web tabs, then show content tab
                    WindowHostContainer.Visibility = Visibility.Collapsed;
                    HideAllWebTabs();
                    WebTabContainer.Visibility = Visibility.Collapsed;
                    _currentWebTabId = null;
                    ShowContentTab(_viewModel.ActiveContentKey);
                    UpdateManagedWindowLayout(activate: false);
                }
                else
                {
                    ContentTabContainer.Visibility = Visibility.Collapsed;
                    ContentTabContent.Content = null;

                    if (!_viewModel.IsWebTabActive)
                    {
                        WindowHostContainer.Visibility = _viewModel.SelectedTab != null
                            ? Visibility.Visible
                            : Visibility.Collapsed;
                    }

                    UpdateManagedWindowLayout(activate: false);
                }
            });
        }
        else if (e.PropertyName == nameof(MainViewModel.IsWebTabActive))
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                // タイル表示中は通常の表示切り替えをスキップ
                if (_viewModel.SelectedTab?.TileLayout != null)
                {
                    UpdateManagedWindowLayout(activate: false);
                    return;
                }

                if (_viewModel.IsWebTabActive)
                {
                    // Hide other containers
                    WindowHostContainer.Visibility = Visibility.Collapsed;
                    ContentTabContainer.Visibility = Visibility.Collapsed;
                    ContentTabContent.Content = null;

                    ShowWebTab(_viewModel.ActiveWebTabId);
                    UpdateManagedWindowLayout(activate: false);
                }
                else
                {
                    HideAllWebTabs();
                    WebTabContainer.Visibility = Visibility.Collapsed;
                    _currentWebTabId = null;

                    if (!_viewModel.IsContentTabActive)
                    {
                        WindowHostContainer.Visibility = _viewModel.SelectedTab != null
                            ? Visibility.Visible : Visibility.Collapsed;
                    }

                    UpdateManagedWindowLayout(activate: false);
                }
            });
        }
        else if (e.PropertyName == nameof(MainViewModel.ActiveWebTabId))
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                // タイル表示中は通常の表示切り替えをスキップ
                if (_viewModel.SelectedTab?.TileLayout != null)
                    return;

                if (_viewModel.IsWebTabActive)
                {
                    ShowWebTab(_viewModel.ActiveWebTabId);
                    UpdateManagedWindowLayout(activate: false);
                }
            });
        }
        else if (e.PropertyName == nameof(MainViewModel.IsCommandPaletteOpen))
        {
            if (_viewModel.IsCommandPaletteOpen)
            {
                if (_viewModel.IsWebTabActive && _currentWebTabId.HasValue)
                {
                    if (_webTabControls.TryGetValue(_currentWebTabId.Value, out var webControl))
                        webControl.Visibility = Visibility.Hidden;
                }

                UpdateManagedWindowLayout(activate: false);

                var palVm = (CommandPaletteViewModel)CommandPaletteControl.DataContext;
                palVm.Open();

                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
                {
                    CommandPaletteControl.RequestSearchBoxFocus();
                });
            }
            else
            {
                RestoreEmbeddedWindow();
                UpdateManagedWindowLayout(activate: true);
            }
        }
        else if (e.PropertyName == nameof(MainViewModel.SelectedTab))
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                if (!_viewModel.IsContentTabActive && !_viewModel.IsWebTabActive)
                {
                    WindowHostContainer.Visibility = _viewModel.SelectedTab != null
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                }
                UpdateManagedWindowLayout(activate: true);
            });
        }
        else if (e.PropertyName == nameof(MainViewModel.IsWindowPickerOpen))
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                UpdateManagedWindowLayout(activate: !_viewModel.IsWindowPickerOpen);
            });
        }
    }

    /// <summary>
    /// タイル解除直後にビュー状態（コンテナの可視性・サイズ）を通常モードへ戻す。
    /// タイル中に設定した Width/Height/Margin をリセットし、アクティブタブ種別に
    /// 応じた適切なコンテナを表示する。
    /// </summary>
    private void ResetTileViewState()
    {
        // 全 Web タブコントロールのタイル時サイズをリセットして非表示にする
        foreach (var ctrl in _webTabControls.Values)
        {
            ctrl.HorizontalAlignment = HorizontalAlignment.Stretch;
            ctrl.VerticalAlignment = VerticalAlignment.Stretch;
            ctrl.Width = double.NaN;
            ctrl.Height = double.NaN;
            ctrl.Margin = new System.Windows.Thickness(0);
            ctrl.Visibility = Visibility.Collapsed;
        }
        WebTabContainer.Visibility = Visibility.Collapsed;
        _currentWebTabId = null;

        // ContentTabContainer のタイル時サイズをリセットして非表示にする
        ContentTabContainer.HorizontalAlignment = HorizontalAlignment.Stretch;
        ContentTabContainer.VerticalAlignment = VerticalAlignment.Stretch;
        ContentTabContainer.Width = double.NaN;
        ContentTabContainer.Height = double.NaN;
        ContentTabContainer.Margin = new System.Windows.Thickness(0);
        ContentTabContainer.Visibility = Visibility.Collapsed;
        ContentTabContent.Content = null;

        // アクティブタブ種別に応じて適切なコンテナを復元する
        if (_viewModel.IsContentTabActive)
        {
            ShowContentTab(_viewModel.ActiveContentKey);
            WindowHostContainer.Visibility = Visibility.Collapsed;
        }
        else if (_viewModel.IsWebTabActive && _viewModel.ActiveWebTabId.HasValue)
        {
            ShowWebTab(_viewModel.ActiveWebTabId);
            WindowHostContainer.Visibility = Visibility.Collapsed;
        }
        else
        {
            WindowHostContainer.Visibility = _viewModel.SelectedTab != null
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        // SWP_NOREDRAW で抑制された Windowz 自身の描画を2段階の遅延で強制更新する
        var windHwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (windHwnd != IntPtr.Zero)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
            {
                NativeMethods.RedrawWindow(windHwnd, IntPtr.Zero, IntPtr.Zero,
                    NativeMethods.RDW_INVALIDATE | NativeMethods.RDW_ERASE |
                    NativeMethods.RDW_FRAME | NativeMethods.RDW_ALLCHILDREN |
                    NativeMethods.RDW_UPDATENOW);
            });
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
            {
                NativeMethods.RedrawWindow(windHwnd, IntPtr.Zero, IntPtr.Zero,
                    NativeMethods.RDW_INVALIDATE | NativeMethods.RDW_ERASE |
                    NativeMethods.RDW_FRAME | NativeMethods.RDW_ALLCHILDREN |
                    NativeMethods.RDW_UPDATENOW);
            });
        }
    }

    #region Content Tabs

    private void ShowContentTab(string? contentKey)
    {
        UserControl? page = contentKey switch
        {
            "SettingsHub" => GetSettingsTabsPage(),
            "GeneralSettings" => GetSettingsTabsPage(),
            "HotkeySettings" => GetSettingsTabsPage(),
            "StartupSettings" => GetSettingsTabsPage(),
            "QuickLaunchSettings" => GetSettingsTabsPage(),
            "ProcessInfo" => GetSettingsTabsPage(),
            _ => null
        };

        if (page != null)
        {
            if (page is SettingsTabsPage settingsTabsPage)
            {
                var targetKey = contentKey == "SettingsHub"
                    ? _pendingSettingsContentKey
                    : contentKey;
                settingsTabsPage.SelectTab(targetKey);
            }

            ContentTabContent.Content = page;
            ContentTabContainer.Visibility = Visibility.Visible;
        }
    }

    private SettingsTabsPage GetSettingsTabsPage()
    {
        _settingsTabsPage ??= App.GetService<SettingsTabsPage>();
        return _settingsTabsPage;
    }

    #endregion

    #region Web Tabs

    private async void ShowWebTab(Guid? tabId)
    {
        if (tabId == null) return;

        var tab = _tabManager.Tabs.FirstOrDefault(t => t.Id == tabId.Value);
        if (tab == null || !tab.IsWebTab) return;

        // Hide currently visible web tab (if different)
        if (_currentWebTabId.HasValue && _currentWebTabId != tabId)
        {
            if (_webTabControls.TryGetValue(_currentWebTabId.Value, out var current))
                current.Visibility = Visibility.Collapsed;
        }

        // Get or create the WebTabControl for this tab
        if (!_webTabControls.TryGetValue(tabId.Value, out var control))
        {
            var envService = App.GetService<WebViewEnvironmentService>();
            control = new WebTabControl(tabId.Value, envService);

            control.TitleChanged += (s, title) =>
            {
                tab.Title = title;
            };
            control.UrlChanged += (s, url) =>
            {
                tab.WebUrl = url;
            };
            control.FaviconChanged += (s, icon) =>
            {
                if (icon != null)
                    tab.Icon = icon;
            };

            _webTabControls[tabId.Value] = control;
            _tabManager.RegisterWebTabControl(tabId.Value, control);
            WebTabContainer.Children.Add(control);

            await control.InitializeAsync(tab.WebUrl ?? "https://www.google.com");
        }

        // タイルモードで設定された位置・サイズをリセットして全体表示に戻す
        control.HorizontalAlignment = HorizontalAlignment.Stretch;
        control.VerticalAlignment = VerticalAlignment.Stretch;
        control.Width = double.NaN;
        control.Height = double.NaN;
        control.Margin = new System.Windows.Thickness(0);
        control.Visibility = Visibility.Visible;
        WebTabContainer.Visibility = Visibility.Visible;
        _currentWebTabId = tabId.Value;
    }

    private void HideAllWebTabs()
    {
        foreach (var control in _webTabControls.Values)
        {
            control.Visibility = Visibility.Collapsed;
        }
    }

    #endregion
}
