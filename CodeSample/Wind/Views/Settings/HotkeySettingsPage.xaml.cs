using System.Windows.Controls;
using System.Windows.Input;
using Wind.ViewModels;

namespace Wind.Views.Settings;

public partial class HotkeySettingsPage : UserControl
{
    public HotkeySettingsPage(HotkeySettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void HotkeyButton_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var vm = DataContext as HotkeySettingsViewModel;
        if (vm?.RecordingHotkey == null) return;

        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;

        vm.ApplyRecordedKey(modifiers, key);
    }
}
