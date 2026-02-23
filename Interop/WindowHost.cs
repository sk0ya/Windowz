using System.Windows.Controls;

namespace WindowzTabManager.Interop;

// Windowz mode stub: no SetParent-based hosting.
public class WindowHost : Border
{
    public IntPtr HostedWindowHandle { get; }

    public int HostedProcessId { get; }

    public event EventHandler? HostedWindowClosed;
    public event EventHandler? MinimizeRequested;
    public event EventHandler? MaximizeRequested;
    public event EventHandler? BringToFrontRequested;
    public event Action<int, int>? MoveRequested;
    public event Action<IntPtr>? NewWindowDetected;

    public WindowHost(IntPtr windowHandle, bool hideFromTaskbar)
    {
        _ = hideFromTaskbar;
        HostedWindowHandle = windowHandle;
    }

    public void SetHideFromTaskbar(bool hideFromTaskbar)
    {
        _ = hideFromTaskbar;
    }

    public void ReleaseWindow()
    {
        HostedWindowClosed?.Invoke(this, EventArgs.Empty);
    }

    public void ResizeHostedWindow(int width, int height)
    {
        _ = width;
        _ = height;
    }

    public void FocusHostedWindow()
    {
    }

    public void ForceRedraw()
    {
    }
}
