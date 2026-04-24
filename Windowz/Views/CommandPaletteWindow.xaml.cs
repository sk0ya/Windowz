using System.Windows;

namespace WindowzTabManager.Views;

public partial class CommandPaletteWindow : Window
{
    public CommandPaletteWindow()
    {
        InitializeComponent();
    }

    public CommandPalette PaletteControl => CommandPaletteHost;

    public void RequestSearchBoxFocus()
    {
        PaletteControl.RequestSearchBoxFocus();
    }
}
