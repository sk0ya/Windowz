using System.Windows.Controls;
using System.Windows.Input;
using Wind.ViewModels;

namespace Wind.Views.Settings;

public partial class QuickLaunchSettingsPage : UserControl
{
    public QuickLaunchSettingsPage(QuickLaunchSettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void QuickLaunchPath_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var vm = DataContext as QuickLaunchSettingsViewModel;
        if (vm == null) return;

        switch (e.Key)
        {
            case Key.Down:
                if (vm.IsSuggestionsOpen && SuggestionsList.Items.Count > 0)
                {
                    var idx = SuggestionsList.SelectedIndex;
                    if (idx < SuggestionsList.Items.Count - 1)
                        SuggestionsList.SelectedIndex = idx + 1;
                    e.Handled = true;
                }
                break;

            case Key.Up:
                if (vm.IsSuggestionsOpen && SuggestionsList.Items.Count > 0)
                {
                    var idx = SuggestionsList.SelectedIndex;
                    if (idx > 0)
                        SuggestionsList.SelectedIndex = idx - 1;
                    e.Handled = true;
                }
                break;

            case Key.Tab:
                if (vm.IsSuggestionsOpen && vm.SelectedSuggestion is not null)
                {
                    vm.ApplySuggestion(vm.SelectedSuggestion);
                    e.Handled = true;
                }
                break;

            case Key.Enter:
                if (vm.IsSuggestionsOpen && vm.SelectedSuggestion is not null)
                {
                    vm.ApplySuggestion(vm.SelectedSuggestion);
                }
                vm.AddQuickLaunchAppCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Escape:
                if (vm.IsSuggestionsOpen)
                {
                    vm.IsSuggestionsOpen = false;
                    e.Handled = true;
                }
                break;
        }
    }

    private void Suggestions_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var vm = DataContext as QuickLaunchSettingsViewModel;
        if (vm?.SelectedSuggestion is not null)
        {
            vm.ApplySuggestion(vm.SelectedSuggestion);
            QuickLaunchPathBox.Focus();
            QuickLaunchPathBox.CaretIndex = vm.NewQuickLaunchPath.Length;
        }
    }
}
