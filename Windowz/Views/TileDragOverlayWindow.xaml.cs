using System.Windows;
using System.Windows.Interop;

namespace WindowzTabManager.Views;

public partial class TileDragOverlayWindow : Window
{
    public TileDragOverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        // WS_EX_TRANSPARENT を付与してマウスイベントを下のウィンドウに貫通させる
        var hwnd = new WindowInteropHelper(this).Handle;
        int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE,
            exStyle | (int)NativeMethods.WS_EX_TRANSPARENT | (int)NativeMethods.WS_EX_LAYERED);
    }
}
