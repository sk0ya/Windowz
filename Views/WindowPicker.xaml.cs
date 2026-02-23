using System.Windows.Controls;
using System.Windows.Input;
using WindowzTabManager.Models;
using WindowzTabManager.ViewModels;

namespace WindowzTabManager.Views;

public partial class WindowPicker : UserControl
{
    public WindowPicker()
    {
        InitializeComponent();
    }

    private WindowPickerViewModel? ViewModel => DataContext as WindowPickerViewModel;

    private void ListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel?.SelectedWindow != null)
        {
            ViewModel.SelectCommand.Execute(null);
        }
    }
}
