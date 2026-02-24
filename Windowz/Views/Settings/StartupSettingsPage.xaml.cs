using System.Windows.Controls;
using System.Windows.Input;
using WindowzTabManager.ViewModels;

namespace WindowzTabManager.Views.Settings;

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
}
