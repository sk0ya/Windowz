using System;
using System.Text;
using System.Windows.Media.Imaging;

namespace WindowzTabManager;

internal class ManagedWindow
{
    public IntPtr Handle { get; set; }
    public string Title { get; set; } = "";
    public BitmapSource? Icon { get; set; }
    public NativeMethods.RECT OriginalRect { get; set; }
    public bool WasMinimized { get; set; }

    public bool IsAlive => NativeMethods.IsWindow(Handle);

    public void RefreshTitle()
    {
        var sb = new StringBuilder(512);
        NativeMethods.GetWindowText(Handle, sb, sb.Capacity);
        Title = sb.ToString();
    }
}
