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
                    // Hide managed-window area and web tabs, then show content tab
                    WindowHostContainer.Visibility = Visibility.Collapsed;
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
                if (_viewModel.IsWebTabActive)
                {
                    // Hide other containers
                    WindowHostContainer.Visibility = Visibility.Collapsed;
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
                UpdateBackdropVisibility();
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
