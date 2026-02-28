using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using WindowzTabManager.Models;

namespace WindowzTabManager;

/// <summary>
/// 別アプリのウィンドウをタブバー上にドラッグ&ドロップしてタブ管理に追加する機能。
/// グローバル WinEventHook で全プロセスの MOVESIZESTART / MOVESIZEEND を監視し、
/// ドラッグ中はタイマーでカーソル位置をポーリングしてドロップゾーンの表示を制御する。
/// </summary>
public partial class MainWindow
{
    // WINEVENT_SKIPOWNPROCESS: 自プロセスのイベントをスキップ
    private const uint WinEventSkipOwnProcess = 0x0002;

    // カーソルがウィンドウ上端からこの範囲内にあればタイトルバードラッグと判定（リサイズと区別）
    private const int TitleBarGuessHeightPx = 60;

    // ── State ────────────────────────────────────────────────────────────

    private IntPtr _externalDragHook;
    private ManagedWinEventDelegate? _externalDragEventProc;
    private IntPtr _externalDraggedWindowHandle;
    private DispatcherTimer? _externalDragPollTimer;
    private bool _isDropZoneActive;

    // ── Hook lifecycle ────────────────────────────────────────────────────

    private void SetupExternalDragHooks()
    {
        _externalDragEventProc = ExternalDragWinEventCallback;

        // 全プロセス・全スレッドを対象に MOVESIZESTART〜MOVESIZEEND を監視
        _externalDragHook = SetWinEventHook(
            EVENT_SYSTEM_MOVESIZESTART_M,
            EVENT_SYSTEM_MOVESIZEEND_M,
            IntPtr.Zero,
            _externalDragEventProc,
            0,  // 全プロセス
            0,  // 全スレッド
            WINEVENT_OUTOFCONTEXT_M | WinEventSkipOwnProcess);
    }

    private void RemoveExternalDragHooks()
    {
        if (_externalDragHook != IntPtr.Zero)
        {
            UnhookWinEvent(_externalDragHook);
            _externalDragHook = IntPtr.Zero;
        }

        _externalDragEventProc = null;
        StopExternalDragPoll();
        _externalDraggedWindowHandle = IntPtr.Zero;
    }

    // ── WinEvent callback ─────────────────────────────────────────────────

    private void ExternalDragWinEventCallback(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime)
    {
        if (hwnd == IntPtr.Zero) return;

        if (eventType == EVENT_SYSTEM_MOVESIZESTART_M)
        {
            // Windowz 自身のウィンドウは除外
            var windHwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == windHwnd) return;

            // すでに管理済みのウィンドウは除外
            if (_tabManager.Tabs.Any(t => t.Window?.Handle == hwnd)) return;

            // カーソルがタイトルバー付近にあればドラッグ（移動）と判定し、リサイズと区別
            if (!NativeMethods.GetCursorPos(out var cursorPt)) return;
            if (!NativeMethods.GetWindowRect(hwnd, out var windowRect)) return;

            bool inTitleBarRegion =
                cursorPt.X >= windowRect.Left &&
                cursorPt.X <= windowRect.Right &&
                cursorPt.Y >= windowRect.Top &&
                cursorPt.Y <= windowRect.Top + TitleBarGuessHeightPx;

            if (!inTitleBarRegion) return;

            _externalDraggedWindowHandle = hwnd;
            Dispatcher.BeginInvoke(DispatcherPriority.Normal, StartExternalDragPoll);
        }
        else if (eventType == EVENT_SYSTEM_MOVESIZEEND_M)
        {
            if (_externalDraggedWindowHandle != hwnd) return;

            // ドロップ時点のカーソル位置をコールバック内でキャプチャ（最も正確）
            NativeMethods.GetCursorPos(out var endCursorPt);
            IntPtr capturedHwnd = hwnd;
            int capturedX = endCursorPt.X;
            int capturedY = endCursorPt.Y;

            _externalDraggedWindowHandle = IntPtr.Zero;

            Dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
            {
                StopExternalDragPoll();
                HideDropZone();

                // ドロップ位置がタブバー上であれば埋め込む
                if (IsCursorOverTabBar(capturedX, capturedY) && NativeMethods.IsWindow(capturedHwnd))
                    TryEmbedExternalWindow(capturedHwnd);
            });
        }
    }

    // ── Polling timer ─────────────────────────────────────────────────────

    private void StartExternalDragPoll()
    {
        if (_externalDragPollTimer != null) return;

        _externalDragPollTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _externalDragPollTimer.Tick += ExternalDragPollTick;
        _externalDragPollTimer.Start();
    }

    private void StopExternalDragPoll()
    {
        if (_externalDragPollTimer == null) return;
        _externalDragPollTimer.Stop();
        _externalDragPollTimer.Tick -= ExternalDragPollTick;
        _externalDragPollTimer = null;
    }

    private void ExternalDragPollTick(object? sender, EventArgs e)
    {
        if (_externalDraggedWindowHandle == IntPtr.Zero)
        {
            StopExternalDragPoll();
            HideDropZone();
            return;
        }

        if (!NativeMethods.GetCursorPos(out var cursorPt)) return;

        bool overTabBar = IsCursorOverTabBar(cursorPt.X, cursorPt.Y);
        if (overTabBar && !_isDropZoneActive)
            ShowDropZone();
        else if (!overTabBar && _isDropZoneActive)
            HideDropZone();
    }

    // ── Hit testing ───────────────────────────────────────────────────────

    private bool IsCursorOverTabBar(int screenX, int screenY)
    {
        if (!TabBarArea.IsLoaded) return false;

        try
        {
            var topLeft = TabBarArea.PointToScreen(new Point(0, 0));
            var bottomRight = TabBarArea.PointToScreen(
                new Point(TabBarArea.ActualWidth, TabBarArea.ActualHeight));

            return screenX >= topLeft.X && screenX < bottomRight.X &&
                   screenY >= topLeft.Y && screenY < bottomRight.Y;
        }
        catch
        {
            return false;
        }
    }

    // ── Drop zone UI ──────────────────────────────────────────────────────

    private void ShowDropZone()
    {
        _isDropZoneActive = true;
        ExternalDropZoneOverlay.Visibility = Visibility.Visible;
    }

    private void HideDropZone()
    {
        _isDropZoneActive = false;
        ExternalDropZoneOverlay.Visibility = Visibility.Collapsed;
    }

    // ── Embedding ─────────────────────────────────────────────────────────

    private void TryEmbedExternalWindow(IntPtr hwnd)
    {
        var windowInfo = WindowInfo.FromHandle(hwnd);
        if (windowInfo == null) return;

        if (windowInfo.IsElevated)
        {
            _viewModel.StatusMessage = $"管理者権限が必要なためタブに追加できません: {windowInfo.Title}";
            return;
        }

        _viewModel.AddWindowCommand.Execute(windowInfo);
        _viewModel.StatusMessage = $"タブに追加: {windowInfo.Title}";
    }
}
