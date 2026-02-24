using System.Windows.Controls;
using WindowzTabManager.ViewModels;

namespace WindowzTabManager.Views.Settings;

public partial class SettingsTabsPage : UserControl
{
    public SettingsTabsPage(
        GeneralSettingsViewModel generalSettingsViewModel,
        HotkeySettingsViewModel hotkeySettingsViewModel,
        StartupSettingsViewModel startupSettingsViewModel,
        QuickLaunchSettingsViewModel quickLaunchSettingsViewModel,
        ProcessInfoViewModel processInfoViewModel)
    {
        InitializeComponent();

        GeneralTab.Content = new GeneralSettingsPage(generalSettingsViewModel);
        HotkeyTab.Content = new HotkeySettingsPage(hotkeySettingsViewModel);
        StartupTab.Content = new StartupSettingsPage(startupSettingsViewModel);
        QuickLaunchTab.Content = new QuickLaunchSettingsPage(quickLaunchSettingsViewModel);
        ProcessInfoTab.Content = new ProcessInfoPage(processInfoViewModel);
    }

    public void SelectTab(string? contentKey)
    {
        SettingsTabControl.SelectedItem = contentKey switch
        {
            "HotkeySettings" => HotkeyTab,
            "StartupSettings" => StartupTab,
            "QuickLaunchSettings" => QuickLaunchTab,
            "ProcessInfo" => ProcessInfoTab,
            _ => GeneralTab
        };

        RefreshSelectedTab();
    }

    private void SettingsTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, e.OriginalSource))
            return;

        RefreshSelectedTab();
    }

    private void RefreshSelectedTab()
    {
        if (ReferenceEquals(SettingsTabControl.SelectedItem, StartupTab) &&
            StartupTab.Content is StartupSettingsPage startupPage &&
            startupPage.DataContext is StartupSettingsViewModel startupVm)
        {
            startupVm.Reload();
        }
        else if (ReferenceEquals(SettingsTabControl.SelectedItem, QuickLaunchTab) &&
                 QuickLaunchTab.Content is QuickLaunchSettingsPage quickLaunchPage &&
                 quickLaunchPage.DataContext is QuickLaunchSettingsViewModel quickLaunchVm)
        {
            quickLaunchVm.Reload();
        }
        else if (ReferenceEquals(SettingsTabControl.SelectedItem, ProcessInfoTab) &&
                 ProcessInfoTab.Content is ProcessInfoPage processInfoPage &&
                 processInfoPage.DataContext is ProcessInfoViewModel processInfoVm)
        {
            processInfoVm.Refresh();
        }
    }
}
