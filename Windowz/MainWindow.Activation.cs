using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace WindowzTabManager;

/// <summary>
/// Windowz と管理対象ウィンドウを一つの論理ウィンドウとしてアクティブ化する処理。
/// Activated と WinEvent の要求をここで調停し、古い遅延処理がフォーカスを奪うのを防ぐ。
/// </summary>
public partial class MainWindow
{
    private const int MaxManagedPromotionRetries = 2;
    private const int ManagedPromotionRetryDelayMs = 90;

    private DispatcherOperation? _pendingManagedPromotion;
    private DispatcherTimer? _managedPromotionRetryTimer;
    private long _managedPromotionGeneration;
    private int _managedPromotionRetryCount;
    private IntPtr _managedPromotionTarget;
    private string _managedPromotionReason = string.Empty;

    // WindowState の StateChanged は Activated より後に届く場合があるため、管理対象の
    // タスクバー操作で復元を開始する時点で先に立てる。
    private bool _wasJustRestoredFromMinimize;
    private bool _managedForegroundRestoreInProgress;
    private long _lastWindowzForegroundFallbackTick;
    private IntPtr _taskbarMouseHook;
    private NativeMethods.LowLevelMouseProc? _taskbarMouseHookProc;

    private void MainWindow_Activated(object? sender, EventArgs e)
    {
        ActivationLog.Write("Activated",
            $"state={WindowState} active={IsActive} justRestored={_wasJustRestoredFromMinimize} " +
            $"contentTab={_viewModel.IsContentTabActive} webTab={_viewModel.IsWebTabActive} " +
            $"lastNonTaskbarFg={ActivationLog.Describe(_lastNonTaskbarForegroundWindow)}");

        // WPF は最小化中にも Activated を通知することがある。この段階でレイアウトや
        // 前面化を行うと、最小化した管理対象を即座に復元してしまう。
        if (WindowState == WindowState.Minimized)
            return;

        if (_viewModel.IsCommandPaletteOpen)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
            {
                _commandPaletteWindow?.RequestSearchBoxFocus();
            });
            return;
        }

        if (_viewModel.IsWindowPickerOpen)
            return;

        if (TryMinimizeWindowzFromTaskbarActivation())
            return;

        if (_viewModel.IsContentTabActive || _viewModel.IsWebTabActive)
        {
            UpdateManagedWindowLayout(activate: false);
            return;
        }

        RequestManagedWindowPromotion(
            "Activated",
            DispatcherPriority.Background,
            requireWindowzActive: true);
    }

    private void MainWindow_Deactivated(object? sender, EventArgs e)
    {
        _activeManagedWindowHandle = IntPtr.Zero;
    }

    private void SetupTaskbarActivationHook()
    {
        if (_taskbarMouseHook != IntPtr.Zero)
            return;

        _taskbarMouseHookProc = OnTaskbarMouseHook;
        _taskbarMouseHook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_MOUSE_LL,
            _taskbarMouseHookProc,
            NativeMethods.GetModuleHandle(null),
            0);
    }

    private void RemoveTaskbarActivationHook()
    {
        if (_taskbarMouseHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_taskbarMouseHook);
            _taskbarMouseHook = IntPtr.Zero;
        }

        _taskbarMouseHookProc = null;
    }

    private IntPtr OnTaskbarMouseHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam.ToInt32() == NativeMethods.WM_LBUTTONUP)
        {
            var point = Marshal.PtrToStructure<NativeMethods.POINT>(lParam);
            Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                () => HandleTaskbarClickAfterShellAsync(point));
        }

        return NativeMethods.CallNextHookEx(_taskbarMouseHook, nCode, wParam, lParam);
    }

    private async void HandleTaskbarClickAfterShellAsync(NativeMethods.POINT point)
    {
        await Task.Delay(100);

        if (IsTaskbarWindowAtScreenPoint(point.X, point.Y))
            HandleWindowzForegroundEvent();
    }

    /// <summary>
    /// WinEvent では Windowz が前景化しても WPF Activated が発火しない場合のフォールバック。
    /// 既に Activated 側で管理対象を前景化できていれば、その WinEvent は stale 判定で除外される。
    /// </summary>
    private void HandleWindowzForegroundEvent()
    {
        if (NativeMethods.GetForegroundWindow() != _mainWindowHandle ||
            WindowState == WindowState.Minimized ||
            _viewModel.IsCommandPaletteOpen ||
            _viewModel.IsWindowPickerOpen)
        {
            return;
        }

        long now = Environment.TickCount64;
        if (now - _lastWindowzForegroundFallbackTick < 100)
            return;
        _lastWindowzForegroundFallbackTick = now;

        ActivationLog.Write("Activated", "handling WinEvent fallback");

        if (TryMinimizeWindowzFromTaskbarActivation())
            return;

        if (_viewModel.IsContentTabActive || _viewModel.IsWebTabActive)
        {
            UpdateManagedWindowLayout(activate: false);
            return;
        }

        RequestManagedWindowPromotion("WindowzForegroundEvent", DispatcherPriority.ApplicationIdle);
    }

    private void RequestManagedWindowPromotion(
        string reason,
        DispatcherPriority priority,
        bool requireWindowzActive = false)
    {
        var target = GetCurrentActiveManagedWindowHandle();
        if (target == IntPtr.Zero || !CanPromoteManagedWindowToForeground())
            return;

        CancelManagedWindowPromotion();

        long generation = _managedPromotionGeneration;
        _managedPromotionTarget = target;
        _managedPromotionReason = reason;
        _pendingManagedPromotion = Dispatcher.BeginInvoke(priority, () =>
        {
            _pendingManagedPromotion = null;
            if (generation != _managedPromotionGeneration ||
                (requireWindowzActive && !IsActive))
            {
                return;
            }

            PromoteManagedWindowToForeground(generation);
        });
    }

    private bool CanPromoteManagedWindowToForeground()
    {
        return WindowState != WindowState.Minimized &&
               !_suppressManagedWindowPromotion &&
               !_isDragging &&
               !_viewModel.IsWindowPickerOpen &&
               !_viewModel.IsCommandPaletteOpen &&
               !_viewModel.IsContentTabActive &&
               !_viewModel.IsWebTabActive;
    }

    private void PromoteManagedWindowToForeground(long generation)
    {
        if (generation != _managedPromotionGeneration ||
            !CanPromoteManagedWindowToForeground() ||
            _managedPromotionTarget != GetCurrentActiveManagedWindowHandle() ||
            !CanContinueManagedWindowPromotion())
        {
            CancelManagedWindowPromotion();
            return;
        }

        ActivationLog.Write("Promote",
            $"begin ({_managedPromotionReason}) generation={generation} " +
            $"retry={_managedPromotionRetryCount} target={ActivationLog.Describe(_managedPromotionTarget)}");

        UpdateManagedWindowLayout(activate: true);
        VerifyManagedWindowForegroundOrRetry(generation);
    }

    private bool CanContinueManagedWindowPromotion()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero || foreground == _mainWindowHandle)
            return true;

        if (IsInSameWindowGroup(foreground, _managedPromotionTarget))
            return true;

        return IsTaskbarClassName(NativeMethods.GetWindowClassName(foreground));
    }

    private void VerifyManagedWindowForegroundOrRetry(long generation)
    {
        var foreground = NativeMethods.GetForegroundWindow();
        bool managedIsForeground = IsInSameWindowGroup(foreground, _managedPromotionTarget);

        ActivationLog.Write("Promote",
            $"verify ({_managedPromotionReason}) generation={generation} " +
            $"fg={ActivationLog.Describe(foreground)} target={ActivationLog.Describe(_managedPromotionTarget)} " +
            $"ok={managedIsForeground} retry={_managedPromotionRetryCount}");

        if (managedIsForeground || _managedPromotionRetryCount >= MaxManagedPromotionRetries)
        {
            CancelManagedWindowPromotion();
            return;
        }

        _managedPromotionRetryCount++;
        _managedPromotionRetryTimer ??= new DispatcherTimer(DispatcherPriority.Normal, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(ManagedPromotionRetryDelayMs)
        };
        _managedPromotionRetryTimer.Tick -= ManagedPromotionRetryTimer_Tick;
        _managedPromotionRetryTimer.Tick += ManagedPromotionRetryTimer_Tick;
        _managedPromotionRetryTimer.Start();
    }

    private void ManagedPromotionRetryTimer_Tick(object? sender, EventArgs e)
    {
        _managedPromotionRetryTimer?.Stop();
        PromoteManagedWindowToForeground(_managedPromotionGeneration);
    }

    private void CancelManagedWindowPromotion()
    {
        _managedPromotionGeneration++;
        _managedPromotionRetryCount = 0;
        _managedPromotionTarget = IntPtr.Zero;
        _managedPromotionReason = string.Empty;
        _managedPromotionRetryTimer?.Stop();

        if (_pendingManagedPromotion?.Status == DispatcherOperationStatus.Pending)
            _pendingManagedPromotion.Abort();
        _pendingManagedPromotion = null;
    }

    private void RestoreWindowzForManagedForeground()
    {
        if (WindowState != WindowState.Minimized)
            return;

        // StateChanged より先に Activated が発生しても、タスクバー再クリックの最小化と
        // 誤判定しないよう復元元を先に記録する。
        _managedForegroundRestoreInProgress = true;
        _wasJustRestoredFromMinimize = true;
        _activeManagedWindowHandle = IntPtr.Zero;
        WindowState = WindowState.Normal;
    }

    private bool TryMinimizeWindowzFromTaskbarActivation()
    {
        if (_suppressManagedWindowPromotion ||
            _viewModel.IsCommandPaletteOpen ||
            _viewModel.IsWindowPickerOpen ||
            _viewModel.IsContentTabActive ||
            _viewModel.IsWebTabActive)
        {
            return false;
        }

        // 管理対象のタスクバーボタンから復元した Activated は、再クリックによる
        // 最小化ではない。RestoreWindowzForManagedForeground が StateChanged より先に立てる。
        if (_wasJustRestoredFromMinimize)
        {
            _wasJustRestoredFromMinimize = false;
            ActivationLog.Write("TaskbarMin", "skip: just restored from minimize");
            return false;
        }

        var currentManagedHandle = GetCurrentActiveManagedWindowHandle();
        if (currentManagedHandle == IntPtr.Zero)
            return false;

        // Shell_TrayWnd が一瞬フォアグラウンドを取得するため、タスクバー以外で
        // 最後に前景化したウィンドウが現在の管理対象かを使って再クリックを判定する。
        if (!IsInSameWindowGroup(_lastNonTaskbarForegroundWindow, currentManagedHandle))
        {
            ActivationLog.Write("TaskbarMin",
                $"skip: lastNonTaskbarFg={ActivationLog.Describe(_lastNonTaskbarForegroundWindow)} " +
                $"not in group of managed={ActivationLog.Describe(currentManagedHandle)}");
            return false;
        }

        if (!IsTaskbarPointerActivation())
        {
            ActivationLog.Write("TaskbarMin", "skip: pointer not on taskbar");
            return false;
        }

        ActivationLog.Write("TaskbarMin", "MATCH -> minimizing Windowz (taskbar re-click on active managed app)");
        CancelManagedWindowPromotion();
        Dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
        {
            if (WindowState != WindowState.Minimized)
                WindowState = WindowState.Minimized;
        });
        return true;
    }

    private static bool IsInSameWindowGroup(IntPtr hwnd, IntPtr managed)
    {
        if (hwnd == IntPtr.Zero || managed == IntPtr.Zero)
            return false;
        if (hwnd == managed)
            return true;

        var root1 = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
        var root2 = NativeMethods.GetAncestor(managed, NativeMethods.GA_ROOT);
        if (root1 == IntPtr.Zero) root1 = hwnd;
        if (root2 == IntPtr.Zero) root2 = managed;
        if (root1 == root2)
            return true;

        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid1);
        NativeMethods.GetWindowThreadProcessId(managed, out uint pid2);
        return pid1 != 0 && pid2 != 0 && pid1 == pid2;
    }

    private IntPtr GetCurrentActiveManagedWindowHandle()
    {
        var selectedTab = _viewModel.SelectedTab;
        if (selectedTab == null ||
            !_viewModel.TryGetExternallyManagedWindowHandle(selectedTab, out var handle))
        {
            return IntPtr.Zero;
        }

        return handle;
    }

    private bool IsTaskbarPointerActivation()
    {
        if (!NativeMethods.GetCursorPos(out var cursorPos))
            return false;

        if (IsScreenPointInsideWindow(cursorPos.X, cursorPos.Y))
            return false;

        return IsTaskbarWindowAtScreenPoint(cursorPos.X, cursorPos.Y);
    }

    private static bool IsTaskbarWindowAtScreenPoint(int screenX, int screenY)
    {
        var pointedWindow = NativeMethods.WindowFromPoint(new NativeMethods.POINT
        {
            X = screenX,
            Y = screenY
        });
        if (pointedWindow == IntPtr.Zero)
            return false;

        var root = NativeMethods.GetAncestor(pointedWindow, NativeMethods.GA_ROOT);
        if (root == IntPtr.Zero)
            root = pointedWindow;

        return IsTaskbarClassName(NativeMethods.GetWindowClassName(pointedWindow)) ||
               IsTaskbarClassName(NativeMethods.GetWindowClassName(root));
    }

    private static bool IsTaskbarClassName(string className)
    {
        return className is "Shell_TrayWnd" or
               "Shell_SecondaryTrayWnd" or
               "MSTaskListWClass" or
               "MSTaskSwWClass" or
               "TaskListThumbnailWnd";
    }
}
