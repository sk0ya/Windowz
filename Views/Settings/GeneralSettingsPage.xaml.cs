using System.Windows;
using System.Windows.Controls;
using WindowzTabManager.ViewModels;

namespace WindowzTabManager.Views.Settings;

public partial class GeneralSettingsPage : UserControl
{
    public GeneralSettingsPage(GeneralSettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void PresetColorButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string colorCode && DataContext is GeneralSettingsViewModel vm)
        {
            vm.SelectPresetColor(colorCode);
        }
    }

    private void BackgroundPresetColorButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string colorCode && DataContext is GeneralSettingsViewModel vm)
        {
            vm.SelectBackgroundPresetColor(colorCode);
        }
    }
}
