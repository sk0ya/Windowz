using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Wind.Interop;

[ComImport]
[Guid("56FDF342-FD6D-11d0-958A-006097C9A090")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITaskbarList
{
    [PreserveSig]
    int HrInit();

    [PreserveSig]
    int AddTab(IntPtr hwnd);

    [PreserveSig]
    int DeleteTab(IntPtr hwnd);

    [PreserveSig]
    int ActivateTab(IntPtr hwnd);

    [PreserveSig]
    int SetActiveAlt(IntPtr hwnd);
}

[ComImport]
[Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
internal class CTaskbarList
{
}

internal sealed class TaskbarListInterop
{
    private ITaskbarList? _taskbarList;
    private bool _initialized;

    public static TaskbarListInterop Instance { get; } = new();

    private TaskbarListInterop()
    {
    }

    private bool EnsureInitialized()
    {
        if (_initialized)
            return _taskbarList != null;

        _initialized = true;

        try
        {
            _taskbarList = (ITaskbarList)new CTaskbarList();
            int hr = _taskbarList.HrInit();
            if (hr < 0)
            {
                Debug.WriteLine($"[TaskbarListInterop] HrInit failed: 0x{hr:X8}");
                _taskbarList = null;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TaskbarListInterop] Init failed: {ex.Message}");
            _taskbarList = null;
        }

        return _taskbarList != null;
    }

    public void AddTab(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !EnsureInitialized() || _taskbarList == null)
            return;

        int hr = _taskbarList.AddTab(hwnd);
        if (hr < 0)
        {
            Debug.WriteLine($"[TaskbarListInterop] AddTab failed: 0x{hr:X8}, hwnd=0x{hwnd:X}");
        }
    }

    public void DeleteTab(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !EnsureInitialized() || _taskbarList == null)
            return;

        int hr = _taskbarList.DeleteTab(hwnd);
        if (hr < 0)
        {
            Debug.WriteLine($"[TaskbarListInterop] DeleteTab failed: 0x{hr:X8}, hwnd=0x{hwnd:X}");
        }
    }
}
