using System.Windows.Controls;
using Wind.ViewModels;

namespace Wind.Views.Settings;

public partial class ProcessInfoPage : UserControl
{
    private static readonly Dictionary<string, string> HeaderToProperty = new()
    {
        ["Title"] = "DisplayTitle",
        ["Process"] = "ProcessName",
        ["PID"] = "ProcessId",
        ["Memory"] = "MemoryBytes",
        ["Started"] = "StartTimeValue",
        ["Path"] = "ExecutablePath",
    };

    public ProcessInfoPage(ProcessInfoViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void GridViewColumnHeader_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (e.OriginalSource is not GridViewColumnHeader header) return;
        if (header.Column == null) return;

        var headerText = header.Column.Header?.ToString();
        if (headerText == null || !HeaderToProperty.TryGetValue(headerText, out var propertyName)) return;

        if (DataContext is ProcessInfoViewModel vm)
        {
            vm.SortCommand.Execute(propertyName);

            // Update header text with sort indicator
            var (sortProp, sortDir) = vm.CurrentSort;
            foreach (var col in ((GridView)ProcessListView.View).Columns)
            {
                var original = col.Header?.ToString()?.TrimEnd(' ', '\u25B2', '\u25BC');
                if (original == null) continue;

                if (HeaderToProperty.TryGetValue(original, out var prop) && prop == sortProp)
                {
                    var arrow = sortDir == System.ComponentModel.ListSortDirection.Ascending ? " \u25B2" : " \u25BC";
                    col.Header = original + arrow;
                }
                else
                {
                    col.Header = original;
                }
            }
        }
    }
}
