using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace WindowzTabManager.Views;

public partial class TileSplitterOverlayWindow : Window
{
    public TileSplitterOverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
    }

    public Canvas SplitterCanvas => SplitterCanvasRoot;

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(
            hwnd,
            NativeMethods.GWL_EXSTYLE,
            exStyle | (int)NativeMethods.WS_EX_NOACTIVATE);
    }
}
