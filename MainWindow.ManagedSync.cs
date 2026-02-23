namespace WindowzTabManager;

public partial class MainWindow
{
    private void EnsureManagedWindowSyncHooks(IntPtr handle)
    {
        // Windowz behavior: do not install WinEvent hooks for managed windows.
    }

    private void RemoveManagedWindowSyncHooks()
    {
        // no-op
    }
}
