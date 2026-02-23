using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Wind.ViewModels;

namespace Wind.Views;

public partial class TabBar : UserControl
{
    public TabBar()
    {
        InitializeComponent();
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void TabItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is Models.TabItem tab)
        {
            ViewModel?.SelectTabCommand.Execute(tab);
        }
    }

    private void CloseTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is Models.TabItem tab)
        {
            ViewModel?.CloseTabCommand.Execute(tab);
        }
        e.Handled = true;
    }
}
