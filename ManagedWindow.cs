using System;
using System.Diagnostics;
using System.Text;
using System.Windows.Media.Imaging;

namespace WindowzTabManager;

public class ManagedWindow
{
    public IntPtr Handle { get; set; }
    public string Title { get; set; } = "";
    public BitmapSource? Icon { get; set; }
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string? ExecutablePath { get; set; }
    internal NativeMethods.RECT OriginalRect { get; set; }
    public bool WasMinimized { get; set; }

    public bool IsAlive => NativeMethods.IsWindow(Handle);

    public void RefreshTitle()
    {
        var sb = new StringBuilder(512);
        NativeMethods.GetWindowText(Handle, sb, sb.Capacity);
        Title = sb.ToString();
    }

    public void RefreshProcessInfo()
    {
        try
        {
            NativeMethods.GetWindowThreadProcessId(Handle, out uint pid);
            if (pid == 0)
                return;

            ProcessId = (int)pid;
            using Process process = Process.GetProcessById((int)pid);
            ProcessName = process.ProcessName;

            try
            {
                ExecutablePath = process.MainModule?.FileName;
            }
            catch
            {
                ExecutablePath = null;
            }
        }
        catch
        {
            // Ignore process access failures.
        }
    }
}
