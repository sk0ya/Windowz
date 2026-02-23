using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Wind.ViewModels;

namespace Wind.Views.Settings;

public partial class StartupSettingsPage : UserControl
{
    public StartupSettingsPage(StartupSettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void StartupPath_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is StartupSettingsViewModel vm)
        {
            vm.AddStartupApplicationCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void AddAppToTileSet_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement button) return;
        if (button.Tag is not TileSetItem tileSet) return;
        if (DataContext is not StartupSettingsViewModel vm) return;

        // Find the ComboBox by walking up and then searching siblings
        var comboBox = FindSiblingComboBox(button);
        if (comboBox?.SelectedItem is StartupAppItem selectedApp)
        {
            vm.AddAppToTileSetDirect(tileSet, selectedApp);
            comboBox.SelectedItem = null;
        }
    }

    private static ComboBox? FindSiblingComboBox(DependencyObject element)
    {
        // Walk up to find a parent that contains a ComboBox
        var current = element;
        for (int i = 0; i < 10; i++) // Limit depth
        {
            var parent = VisualTreeHelper.GetParent(current);
            if (parent == null) break;

            // Search children of parent for ComboBox
            var comboBox = FindChildComboBox(parent);
            if (comboBox != null) return comboBox;

            current = parent;
        }
        return null;
    }

    private static ComboBox? FindChildComboBox(DependencyObject parent)
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ComboBox comboBox) return comboBox;

            var result = FindChildComboBox(child);
            if (result != null) return result;
        }
        return null;
    }
}
