using System.Diagnostics;

namespace WindowzTabManager.Interop;

internal static class WindowClassFilters
{
    // WinUI 3 desktop windows rely on a top-level DirectComposition pipeline.
    // Reparenting with SetParent/WS_POPUP causes rendering corruption.
    private static readonly HashSet<string> UnsupportedEmbeddedWindowClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "WinUIDesktopWin32WindowClass",
        "Microsoft.UI.Content.DesktopChildSiteBridge",
        "Microsoft.UI.Content.PopupWindowSiteBridge",
        "Xaml_WindowedPopupClass",
        "PopupHost",            // WinUI/XAML popup host (e.g. Explorer address bar dropdown)
    };

    private static readonly string[] UnsupportedEmbeddedWindowClassPrefixes =
    {
        "WinUI",
        "Microsoft.UI.Content."
    };

    // Module-level signal used by WinUI 3 apps. This catches apps that use a custom
    // top-level class name and therefore bypass class-name-only checks.
    private static readonly HashSet<string> WinUi3ModuleNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft.UI.Xaml.dll"
    };

    private static readonly HashSet<string> UnsupportedApplicationFrameProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "ApplicationFrameHost",
        "SystemSettings"
    };

    public static bool IsUnsupportedForEmbedding(string className)
    {
        if (string.IsNullOrWhiteSpace(className))
            return false;

        if (UnsupportedEmbeddedWindowClasses.Contains(className))
            return true;

        foreach (var prefix in UnsupportedEmbeddedWindowClassPrefixes)
        {
            if (className.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static bool TryGetUnsupportedReasonForEmbedding(IntPtr hwnd, out string reason, bool deepInspection = true)
    {
        reason = string.Empty;
        if (hwnd == IntPtr.Zero)
            return false;

        string className = NativeMethods.GetWindowClassName(hwnd);
        bool hasOwningProcess = TryGetOwningProcessName(hwnd, out string owningProcess);

        if (IsUnsupportedForEmbedding(className))
        {
            reason = $"class={className}";
            return true;
        }

        // UWP/immersive windows (including Windows Settings) are typically hosted
        // by ApplicationFrameWindow and are unstable when reparented.
        if (string.Equals(className, "ApplicationFrameWindow", StringComparison.OrdinalIgnoreCase) &&
            hasOwningProcess &&
            UnsupportedApplicationFrameProcesses.Contains(owningProcess))
        {
            reason = $"class={className}, process={owningProcess}";
            return true;
        }

        if (!deepInspection)
            return false;

        if (TryFindUnsupportedChildClass(hwnd, out string childClass))
        {
            reason = $"child-class={childClass}";
            return true;
        }

        if (TryFindWinUiModule(hwnd, out string moduleName))
        {
            reason = $"module={moduleName}";
            return true;
        }

        return false;
    }

    private static bool TryFindUnsupportedChildClass(IntPtr hwnd, out string childClass)
    {
        childClass = string.Empty;
        string? matchedClass = null;

        try
        {
            NativeMethods.EnumChildWindows(hwnd, (child, _) =>
            {
                string candidate = NativeMethods.GetWindowClassName(child);
                if (!IsUnsupportedForEmbedding(candidate))
                    return true;

                matchedClass = candidate;
                return false; // stop enumeration
            }, IntPtr.Zero);
        }
        catch
        {
            return false;
        }

        if (string.IsNullOrEmpty(matchedClass))
            return false;

        childClass = matchedClass;
        return true;
    }

    private static bool TryFindWinUiModule(IntPtr hwnd, out string moduleName)
    {
        moduleName = string.Empty;

        try
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint processId);
            if (processId == 0)
                return false;

            using var process = Process.GetProcessById((int)processId);

            foreach (ProcessModule module in process.Modules)
            {
                if (!WinUi3ModuleNames.Contains(module.ModuleName))
                    continue;

                moduleName = module.ModuleName;
                return true;
            }
        }
        catch
        {
            // Access denied or cross-bitness inspection failures should not break embedding flow.
            return false;
        }

        return false;
    }

    private static bool TryGetOwningProcessName(IntPtr hwnd, out string processName)
    {
        processName = string.Empty;

        try
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint processId);
            if (processId == 0)
                return false;

            using var process = Process.GetProcessById((int)processId);
            processName = process.ProcessName;
            return !string.IsNullOrWhiteSpace(processName);
        }
        catch
        {
            return false;
        }
    }
}
