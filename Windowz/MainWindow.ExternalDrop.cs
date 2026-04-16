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

                // ドロップ位置が見えているタブバー上であれば埋め込む
                if (IsCursorOverVisibleTabBar(capturedX, capturedY) && NativeMethods.IsWindow(capturedHwnd))
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

        bool overTabBar = IsCursorOverVisibleTabBar(cursorPt.X, cursorPt.Y);
        if (overTabBar && !_isDropZoneActive)
            ShowDropZone();
        else if (!overTabBar && _isDropZoneActive)
            HideDropZone();
    }

    // ── Hit testing ───────────────────────────────────────────────────────

    private bool IsCursorOverVisibleTabBar(int screenX, int screenY)
    {
        if (!TryGetTabBarScreenBounds(out var bounds)) return false;

        return screenX >= bounds.Left && screenX < bounds.Right &&
               screenY >= bounds.Top && screenY < bounds.Bottom &&
               IsTabBarHeaderVisible(bounds);
    }

    private bool TryGetTabBarScreenBounds(out Rect bounds)
    {
        bounds = Rect.Empty;

        if (!TabBarArea.IsLoaded ||
            !IsVisible ||
            WindowState == WindowState.Minimized ||
            TabBarArea.ActualWidth <= 0 ||
            TabBarArea.ActualHeight <= 0)
        {
            return false;
        }

        try
        {
            var topLeft = TabBarArea.PointToScreen(new Point(0, 0));
            var bottomRight = TabBarArea.PointToScreen(
                new Point(TabBarArea.ActualWidth, TabBarArea.ActualHeight));

            bounds = new Rect(topLeft, bottomRight);
            return bounds.Width > 0 && bounds.Height > 0;
        }
        catch
        {
            return false;
        }
    }

    private bool IsTabBarHeaderVisible(Rect bounds)
    {
        if (_mainWindowHandle == IntPtr.Zero ||
            !NativeMethods.IsWindow(_mainWindowHandle) ||
            !NativeMethods.IsWindowVisible(_mainWindowHandle) ||
            NativeMethods.IsIconic(_mainWindowHandle))
        {
            return false;
        }

        int columns = bounds.Width >= bounds.Height ? 5 : 3;
        int rows = bounds.Width >= bounds.Height ? 3 : 5;

        // WindowFromPoint でタブバーの実表示をサンプリングする。
        // 座標だけで判定すると、Windowz が背面に隠れていてもタブ化されてしまう。
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                double x = bounds.Left + bounds.Width * (column + 0.5) / columns;
                double y = bounds.Top + bounds.Height * (row + 0.5) / rows;

                if (IsWindowzTopLevelAtScreenPoint(x, y))
                    return true;
            }
        }

        return false;
    }

    private bool IsWindowzTopLevelAtScreenPoint(double screenX, double screenY)
    {
        var point = new NativeMethods.POINT
        {
            X = (int)Math.Floor(screenX),
            Y = (int)Math.Floor(screenY)
        };

        var hwnd = NativeMethods.WindowFromPoint(point);
        if (hwnd == IntPtr.Zero)
            return false;

        var root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
        return (root == IntPtr.Zero ? hwnd : root) == _mainWindowHandle;
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
