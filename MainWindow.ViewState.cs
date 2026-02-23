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
        if (e.PropertyName == nameof(MainViewModel.CurrentWindowHost))
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                // Skip if returning from content tab — the IsContentTabActive handler
                // will call UpdateWindowHost after restoring container visibility.
                if (ContentTabContainer.Visibility == Visibility.Visible)
                    return;

                // Skip if returning from web tab — the IsWebTabActive handler
                // will call UpdateWindowHost after restoring container visibility.
                // Without this guard, UpdateWindowHost is called twice (once here and
                // once from the IsWebTabActive handler), causing a detach/re-attach
                // cycle that breaks WS_CHILD window rendering.
                if (WebTabContainer.Visibility == Visibility.Visible)
                    return;

                UpdateWindowHost(_viewModel.CurrentWindowHost);
                UpdateManagedWindowLayout(activate: false);
            });
        }
        else if (e.PropertyName == nameof(MainViewModel.ActiveContentKey))
        {
            // Content tab switched (e.g. Settings → Startup Settings)
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
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
                if (_viewModel.IsContentTabActive)
                {
                    // Hide window host, tile, and web tabs, show content tab
                    WindowHostContainer.Visibility = Visibility.Collapsed;
                    if (_currentHost != null)
                    {
                        WindowHostContent.Content = null;
                        _currentHost = null;
                    }

                    TileContainer.Visibility = Visibility.Collapsed;
                    HideAllWebTabs();
                    WebTabContainer.Visibility = Visibility.Collapsed;
                    _currentWebTabId = null;
                    UpdateBackdropVisibility();
                    ShowContentTab(_viewModel.ActiveContentKey);
                    UpdateManagedWindowLayout(activate: false);
                }
                else
                {
                    ContentTabContainer.Visibility = Visibility.Collapsed;
                    ContentTabContent.Content = null;

                    // Restore WindowHostContainer and re-embed the window host in one place
                    // to avoid race conditions with the CurrentWindowHost handler.
                    WindowHostContainer.Visibility = _viewModel.SelectedTab != null
                        ? Visibility.Visible
                        : Visibility.Collapsed;

                    if (_viewModel.CurrentWindowHost != null)
                    {
                        UpdateWindowHost(_viewModel.CurrentWindowHost);
                    }

                    UpdateManagedWindowLayout(activate: false);
                }
            });
        }
        else if (e.PropertyName == nameof(MainViewModel.IsWebTabActive))
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                if (_viewModel.IsWebTabActive)
                {
                    // Hide other containers
                    WindowHostContainer.Visibility = Visibility.Collapsed;
                    if (_currentHost != null)
                    {
                        WindowHostContent.Content = null;
                        _currentHost = null;
                    }
                    TileContainer.Visibility = Visibility.Collapsed;
                    ContentTabContainer.Visibility = Visibility.Collapsed;
                    ContentTabContent.Content = null;

                    ShowWebTab(_viewModel.ActiveWebTabId);
                    UpdateBackdropVisibility();
                    UpdateManagedWindowLayout(activate: false);
                }
                else
                {
                    HideAllWebTabs();
                    WebTabContainer.Visibility = Visibility.Collapsed;
                    _currentWebTabId = null;

                    if (!_viewModel.IsContentTabActive && !_viewModel.IsTileVisible)
                    {
                        WindowHostContainer.Visibility = _viewModel.SelectedTab != null
                            ? Visibility.Visible : Visibility.Collapsed;
                        if (_viewModel.CurrentWindowHost != null)
                            UpdateWindowHost(_viewModel.CurrentWindowHost);
                    }

                    UpdateManagedWindowLayout(activate: false);
                }
            });
        }
        else if (e.PropertyName == nameof(MainViewModel.ActiveWebTabId))
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                if (_viewModel.IsWebTabActive)
                {
                    ShowWebTab(_viewModel.ActiveWebTabId);
                    UpdateManagedWindowLayout(activate: false);
                }
            });
        }
        else if (e.PropertyName == nameof(MainViewModel.IsTileVisible))
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                if (_viewModel.IsTileVisible)
                {
                    // Switch to tile view
                    WindowHostContainer.Visibility = Visibility.Collapsed;
                    if (_currentHost != null)
                    {
                        WindowHostContent.Content = null;
                        _currentHost = null;
                    }
                    HideAllWebTabs();
                    WebTabContainer.Visibility = Visibility.Collapsed;
                    _currentWebTabId = null;

                    // Only rebuild if tile container is empty (first time or after full stop)
                    if (_tiledHosts.Count == 0 && _viewModel.CurrentTileLayout != null)
                    {
                        BuildTileLayout(_viewModel.CurrentTileLayout);
                    }

                    // Ensure all tiled hosts are visible
                    foreach (var host in _tiledHosts)
                        host.Visibility = Visibility.Visible;

                    TileContainer.Visibility = Visibility.Visible;

                    // Trigger resize for all tiled hosts and suppress border
                    Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
                    {
                        foreach (var host in _tiledHosts)
                        {
                            if (host.Parent is ContentControl cc && cc.Parent is Border b)
                            {
                                var w = (int) b.ActualWidth;
                                var h = (int) b.ActualHeight;
                                if (w > 0 && h > 0)
                                    host.ResizeHostedWindow(w, h);
                            }
                        }
                        SuppressBorder();
                        UpdateManagedWindowLayout(activate: false);
                    });
                }
                else
                {
                    // Hide tile view but keep hosts attached
                    // Restore visibility of tiled hosts first (they may have been hidden by picker)
                    foreach (var host in _tiledHosts)
                        host.Visibility = Visibility.Visible;

                    TileContainer.Visibility = Visibility.Collapsed;
                    WindowHostContainer.Visibility = Visibility.Visible;
                    UpdateManagedWindowLayout(activate: false);
                }
            });
        }
        else if (e.PropertyName == nameof(MainViewModel.IsCommandPaletteOpen))
        {
            if (_viewModel.IsCommandPaletteOpen)
            {
                UpdateBackdropVisibility();
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
        else if (e.PropertyName == nameof(MainViewModel.IsTiled))
        {
            if (!_viewModel.IsTiled)
            {
                // Tile layout fully destroyed — clean up hosts
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => { ClearTileLayout(); });
            }
        }
        else if (e.PropertyName == nameof(MainViewModel.SelectedTab))
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
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

    #region Content Tabs

    private void ShowContentTab(string? contentKey)
    {
        UserControl? page = contentKey switch
        {
            "GeneralSettings" => _generalSettingsPage ??= App.GetService<GeneralSettingsPage>(),
            "HotkeySettings" => _hotkeySettingsPage ??= App.GetService<HotkeySettingsPage>(),
            "StartupSettings" => GetStartupSettingsPage(),
            "QuickLaunchSettings" => GetQuickLaunchSettingsPage(),
            "ProcessInfo" => GetProcessInfoPage(),
            _ => null
        };

        if (page != null)
        {
            ContentTabContent.Content = page;
            ContentTabContainer.Visibility = Visibility.Visible;
        }
    }

    private StartupSettingsPage GetStartupSettingsPage()
    {
        if (_startupSettingsPage == null)
        {
            _startupSettingsPage = App.GetService<StartupSettingsPage>();
        }
        else
        {
            ((StartupSettingsViewModel)_startupSettingsPage.DataContext).Reload();
        }
        return _startupSettingsPage;
    }

    private QuickLaunchSettingsPage GetQuickLaunchSettingsPage()
    {
        if (_quickLaunchSettingsPage == null)
        {
            _quickLaunchSettingsPage = App.GetService<QuickLaunchSettingsPage>();
        }
        else
        {
            ((QuickLaunchSettingsViewModel)_quickLaunchSettingsPage.DataContext).Reload();
        }
        return _quickLaunchSettingsPage;
    }

    private ProcessInfoPage GetProcessInfoPage()
    {
        _processInfoPage ??= App.GetService<ProcessInfoPage>();
        ((ProcessInfoViewModel)_processInfoPage.DataContext).Refresh();
        return _processInfoPage;
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
