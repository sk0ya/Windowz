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
    // 長時間未使用だったプロセスは、ウィンドウスレッドが操作可能になるまで
    // 数百 ms 以上かかることがある。ユーザー操作から始まった同じ generation の間だけ、
    // 段階的に間隔を延ばして再試行する。
    private static readonly int[] ManagedPromotionRetryDelaysMs = [100, 250, 500, 1000];

    private DispatcherOperation? _pendingManagedPromotion;
    private DispatcherTimer? _managedPromotionRetryTimer;
    private long _managedPromotionGeneration;
    private int _managedPromotionRetryCount;
    private IntPtr _managedPromotionTarget;
    private string _managedPromotionReason = string.Empty;

    // WindowState の StateChanged は Activated より後に届く場合があるため、管理対象の
    // タスクバー操作で復元を開始する時点で先に記録する。
    // WPF は 1 回の復元で Activated を複数回発火するため、ワンショットのフラグではなく
    // 時間窓 (ForegroundActivationPolicy.RestoreGraceMs) で判定する。
    private long _restoredFromMinimizeTick;
    private long _lastWindowzForegroundFallbackTick;
    private long _lastTaskbarClickTick;

    // タスクバーをクリックした瞬間にアクティブだったアプリ。
    // 再クリック最小化は「今表示しているアプリのボタンをもう一度押した」操作なので、
    // クリック時点でそのアプリがアクティブだったことが条件になる。
    // Activated 時点の _lastNonTaskbarForegroundWindow を見てはいけない。クリックで
    // 別アプリが引っ込むと、その裏の managed ウィンドウが前景に上がって
    // 「managed アプリがアクティブだった」ように見えてしまう。
    private IntPtr _taskbarClickPreviousForeground;

    private IntPtr _taskbarMouseHook;
    private NativeMethods.LowLevelMouseProc? _taskbarMouseHookProc;

    private void MainWindow_Activated(object? sender, EventArgs e)
    {
        ActivationLog.Write("Activated",
            $"state={WindowState} active={IsActive} " +
            $"justRestored={ForegroundActivationPolicy.IsWithinRestoreGrace(Environment.TickCount64, _restoredFromMinimizeTick)} " +
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
        // BUTTONUP だけを見る。タスクバーボタンは UP で動作し、LL フックは
        // シェルにメッセージが届く前に呼ばれるので、記録は必ず Activated に先行する。
        // BUTTONDOWN でも記録すると、消費済みのクリックが同じクリックの UP で
        // 再武装されてしまい、_lastTaskbarClickTick のクリアが無意味になる。
        if (nCode >= 0 && wParam.ToInt32() == NativeMethods.WM_LBUTTONUP)
        {
            var point = Marshal.PtrToStructure<NativeMethods.POINT>(lParam);
            if (IsTaskbarWindowAtScreenPoint(point.X, point.Y))
            {
                _lastTaskbarClickTick = Environment.TickCount64;

                // シェルがクリックを処理する前なので、ここでの値がクリック直前の
                // アクティブアプリ (タスクバー系は除外済み)。
                _taskbarClickPreviousForeground = _lastNonTaskbarForegroundWindow;
            }

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
        HandleWindowzForegroundEvent(allowForegroundOnActiveManagedWindow: false);
    }

    private void HandleWindowzForegroundEvent(bool allowForegroundOnActiveManagedWindow)
    {
        var foreground = NativeMethods.GetForegroundWindow();
        bool foregroundMovedToActiveManagedWindow =
            allowForegroundOnActiveManagedWindow &&
            IsInSameWindowGroup(foreground, GetCurrentActiveManagedWindowHandle());

        if ((foreground != _mainWindowHandle && !foregroundMovedToActiveManagedWindow) ||
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

        // 別の managed タブのウィンドウが前景を取っているなら、ユーザーがタスクバー等で
        // そのアプリを選んだ直後。旧タブを前景へ引き戻さず、そちらの選択を処理させる。
        if (IsOtherManagedWindowInForeground(target))
        {
            ActivationLog.Write("Promote", $"skip ({reason}): other managed window is foreground");
            CancelManagedWindowPromotion();
            ScheduleForegroundWindowRecheck();
            return;
        }

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
            (_managedPromotionRetryCount == 0 && !CanContinueManagedWindowPromotion()))
        {
            CancelManagedWindowPromotion();
            return;
        }

        // リトライ中は CanContinueManagedWindowPromotion を評価しない (対象アプリが
        // 応答するまで前景が定まらないため)。ただし別の managed タブのウィンドウが
        // 前景を取った場合はユーザーの明示的な切り替えなので、リトライ中でも中止する。
        if (IsOtherManagedWindowInForeground(_managedPromotionTarget))
        {
            ActivationLog.Write("Promote",
                $"abort ({_managedPromotionReason}) generation={generation} " +
                $"retry={_managedPromotionRetryCount}: other managed window is foreground");
            CancelManagedWindowPromotion();
            ScheduleForegroundWindowRecheck();
            return;
        }

        ActivationLog.Write("Promote",
            $"begin ({_managedPromotionReason}) generation={generation} " +
            $"retry={_managedPromotionRetryCount} target={ActivationLog.Describe(_managedPromotionTarget)}");

        UpdateManagedWindowLayout(activate: true);
        VerifyManagedWindowForegroundOrRetry(generation);
    }

    /// <summary>
    /// 昇格対象とは別の managed タブのウィンドウが前景を取っているかを判定する。
    /// タスクバーやタスクスイッチャーで別の管理アプリが選ばれた状態を表す。
    /// </summary>
    private bool IsOtherManagedWindowInForeground(IntPtr promotionTarget)
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero)
            return false;

        var foregroundTab = FindExternallyManagedTabForForegroundWindow(foreground);

        return ForegroundActivationPolicy.ShouldAbortPromotion(
            foreground == _mainWindowHandle,
            IsInSameWindowGroup(foreground, promotionTarget),
            // タイル・ピン留めで同時表示中のウィンドウ間の前景移動は切り替えではない
            foregroundTab != null && !_tabManager.IsCoVisibleWithActiveTab(foregroundTab));
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

        if (managedIsForeground ||
            _managedPromotionRetryCount >= ManagedPromotionRetryDelaysMs.Length)
        {
            CancelManagedWindowPromotion();
            return;
        }

        int retryDelayMs = ManagedPromotionRetryDelaysMs[_managedPromotionRetryCount];
        _managedPromotionRetryCount++;
        _managedPromotionRetryTimer ??= new DispatcherTimer(DispatcherPriority.Normal, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(retryDelayMs)
        };
        _managedPromotionRetryTimer.Interval = TimeSpan.FromMilliseconds(retryDelayMs);
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
        // 誤判定しないよう復元時刻を先に記録する。
        _restoredFromMinimizeTick = Environment.TickCount64;
        _activeManagedWindowHandle = IntPtr.Zero;
        WindowState = WindowState.Normal;
    }

    private bool TryMinimizeWindowzFromTaskbarActivation()
    {
        long now = Environment.TickCount64;
        var currentManagedHandle = GetCurrentActiveManagedWindowHandle();

        var reason = ForegroundActivationPolicy.EvaluateTaskbarMinimize(
            suppressed: _suppressManagedWindowPromotion ||
                        _viewModel.IsCommandPaletteOpen ||
                        _viewModel.IsWindowPickerOpen,
            contentOrWebTabActive: _viewModel.IsContentTabActive || _viewModel.IsWebTabActive,
            hasActiveManagedWindow: currentManagedHandle != IntPtr.Zero,
            visibleManagedAppWasActiveAtClick: WasVisibleManagedAppActiveAtTaskbarClick(),
            pointerOnTaskbar: IsTaskbarPointerActivation(),
            followsRecentTaskbarClick: ForegroundActivationPolicy.FollowsRecentTaskbarClick(
                now,
                _lastTaskbarClickTick),
            nowTick: now,
            restoredFromMinimizeTick: _restoredFromMinimizeTick);

        if (reason != ForegroundActivationPolicy.TaskbarMinimizeSkipReason.None)
        {
            ActivationLog.Write("TaskbarMin",
                $"skip ({reason}): activeAtClick={ActivationLog.Describe(_taskbarClickPreviousForeground)} " +
                $"lastNonTaskbarFg={ActivationLog.Describe(_lastNonTaskbarForegroundWindow)} " +
                $"managed={ActivationLog.Describe(currentManagedHandle)}");
            return false;
        }

        ActivationLog.Write("TaskbarMin", "MATCH -> minimizing Windowz (taskbar re-click on visible managed app)");

        // 同じクリックを 2 度消費しないよう相関を切る。
        _lastTaskbarClickTick = 0;
        _taskbarClickPreviousForeground = IntPtr.Zero;
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

    /// <summary>
    /// タスクバーをクリックした瞬間にアクティブだったアプリが、今画面に見えている
    /// managed ウィンドウかを判定する。タイル・ピン留めで同時表示中のウィンドウも
    /// 含める (アクティブタブのウィンドウとだけ突き合わせると、タイルの別スロットを
    /// 触っていた場合に再クリック最小化が効かなくなる)。
    /// </summary>
    private bool WasVisibleManagedAppActiveAtTaskbarClick()
    {
        var activeAtClick = _taskbarClickPreviousForeground;
        if (activeAtClick == IntPtr.Zero)
            return false;

        if (IsInSameWindowGroup(activeAtClick, GetCurrentActiveManagedWindowHandle()))
            return true;

        var tab = FindExternallyManagedTabForForegroundWindow(activeAtClick);
        return tab != null && _tabManager.IsCoVisibleWithActiveTab(tab);
    }

    private bool IsTaskbarPointerActivation()
    {
        if (!NativeMethods.GetCursorPos(out var cursorPos))
            return false;

        // Windowz の矩形内かは見ない。自動非表示のタスクバーは Windowz に重なって
        // 手前に表示されるため、矩形で弾くと再クリック最小化が一切効かなくなる。
        // WindowFromPoint はその座標で最前面のウィンドウを返すので、
        // タスクバーが返るなら実際にタスクバーが手前にある。
        if (!TryGetPointedWindowClassNames(cursorPos.X, cursorPos.Y, out var className, out var rootClassName))
            return false;

        // サムネイルプレビューも Windowz の上に重なって表示されるが、そこでのクリックは
        // Windows の挙動としては「アクティブにする」であって最小化トグルではない。
        // 再クリック最小化の対象からは外す (クリック記録の側では従来どおり数える)。
        if (IsTaskbarThumbnailClassName(className) || IsTaskbarThumbnailClassName(rootClassName))
            return false;

        return IsTaskbarClassName(className) || IsTaskbarClassName(rootClassName);
    }

    private static bool IsTaskbarWindowAtScreenPoint(int screenX, int screenY)
    {
        return TryGetPointedWindowClassNames(screenX, screenY, out var className, out var rootClassName) &&
               (IsTaskbarClassName(className) || IsTaskbarClassName(rootClassName));
    }

    private static bool TryGetPointedWindowClassNames(
        int screenX,
        int screenY,
        out string className,
        out string rootClassName)
    {
        className = string.Empty;
        rootClassName = string.Empty;

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

        className = NativeMethods.GetWindowClassName(pointedWindow);
        rootClassName = NativeMethods.GetWindowClassName(root);
        return true;
    }

    private static bool IsTaskbarThumbnailClassName(string className)
    {
        return className is "TaskListThumbnailWnd";
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
