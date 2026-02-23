using System.Diagnostics;
using System.Drawing;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wind.Interop;

namespace Wind.Models;

public class WindowInfo
{
    public IntPtr Handle { get; set; }
    public string Title { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public int ProcessId { get; set; }
    public ImageSource? Icon { get; set; }
    public string? ExecutablePath { get; set; }
    public bool IsExplorer { get; set; }
    public bool IsElevated { get; set; }

    public static WindowInfo? FromHandle(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return null;

        string title = NativeMethods.GetWindowTitle(handle);
        if (string.IsNullOrWhiteSpace(title)) return null;

        NativeMethods.GetWindowThreadProcessId(handle, out uint processId);

        string processName = string.Empty;
        string? executablePath = null;
        ImageSource? icon = null;

        try
        {
            using var process = Process.GetProcessById((int)processId);
            processName = process.ProcessName;

            try
            {
                string? fileName = process.MainModule?.FileName;
                if (!string.IsNullOrEmpty(fileName))
                {
                    executablePath = fileName;
                    icon = GetIconFromFile(fileName);
                }
            }
            catch
            {
                // Access denied to some processes
            }
        }
        catch
        {
            // Process may have exited
        }

        // Detect Explorer folder windows (CabinetWClass)
        string className = NativeMethods.GetWindowClassName(handle);
        bool isExplorer = string.Equals(processName, "explorer", StringComparison.OrdinalIgnoreCase)
                          && string.Equals(className, "CabinetWClass", StringComparison.Ordinal);

        // Check elevation only when Wind is not running as admin
        bool isElevated = !App.IsRunningAsAdmin() && NativeMethods.IsProcessElevated(handle);

        return new WindowInfo
        {
            Handle = handle,
            Title = title,
            ProcessName = processName,
            ProcessId = (int)processId,
            Icon = icon,
            ExecutablePath = executablePath,
            IsExplorer = isExplorer,
            IsElevated = isElevated
        };
    }

    private static ImageSource? GetIconFromFile(string filePath)
    {
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(filePath);
            if (icon == null) return null;

            using var bitmap = icon.ToBitmap();
            var hBitmap = bitmap.GetHbitmap();

            try
            {
                return Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap,
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
            }
            finally
            {
                DeleteObject(hBitmap);
            }
        }
        catch
        {
            return null;
        }
    }

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    public override string ToString() => $"{Title} ({ProcessName})";
}
