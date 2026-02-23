using System.Windows;

namespace Wind.Views;

public partial class RenameDialog : Window
{
    public string ResultName { get; private set; } = "";

    public RenameDialog(string currentName)
    {
        InitializeComponent();
        NameBox.Text = currentName;
        Loaded += (s, e) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
        NameBox.PreviewKeyDown += (s, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                ResultName = NameBox.Text;
                DialogResult = true;
            }
        };
    }

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        ResultName = NameBox.Text;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
