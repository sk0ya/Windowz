using System.Windows.Controls;
using System.Windows.Input;
using WindowzTabManager.ViewModels;

namespace WindowzTabManager.Views.Settings;

public partial class ApplicationLaunchSettingsPage : UserControl
{
    public ApplicationLaunchSettingsPage(ApplicationLaunchSettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.BrowseDone += () =>
        {
            FolderPathBox.Focus();
        };
    }

    private void FolderPath_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is ApplicationLaunchSettingsViewModel vm)
        {
            vm.AddFolderCommand.Execute(null);
            e.Handled = true;
        }
    }
}
