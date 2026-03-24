using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace WindowzTabManager;

public partial class MainWindow
{
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            // Double-click on title bar empty area to maximize/restore
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
        else
        {
            StartDragTracking(e);
        }
    }

    private void TabScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_currentTabPosition is "Top" or "Bottom")
        {
            // Horizontal tabs: convert vertical wheel to horizontal scroll
            TabScrollViewer.ScrollToHorizontalOffset(TabScrollViewer.HorizontalOffset - e.Delta);
            e.Handled = true;
        }
        // Vertical tabs (Left/Right): default vertical scroll works
    }

    private void TabArea_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Allow drag from empty areas in the tab bar (not on buttons or tabs)
        if (e.OriginalSource is ScrollViewer ||
            e.OriginalSource is ScrollContentPresenter ||
            e.OriginalSource is Grid ||
            e.OriginalSource is StackPanel)
        {
            if (e.ClickCount == 2)
            {
                // Double-click on tab area empty space to maximize/restore
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                e.Handled = true;
            }
            else
            {
                StartDragTracking(e);
            }
        }
    }

    private void TabBarEmptyArea_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not (ScrollViewer or ScrollContentPresenter or Grid or StackPanel))
            return;

        var menu = new ContextMenu();
        menu.Style = (Style)FindResource(typeof(ContextMenu));

        // Show fold/unfold option for vertical tab bar
        if (_currentTabPosition is "Left" or "Right")
        {
            if (_isTabBarCollapsed)
            {
                var expandItem = new MenuItem { Header = "折りたたみ解除" };
                expandItem.Click += (s, args) => ToggleTabBarCollapsed();
                menu.Items.Add(expandItem);
            }
            else
            {
                var collapseItem = new MenuItem { Header = "折りたたみ" };
                collapseItem.Click += (s, args) => ToggleTabBarCollapsed();
                menu.Items.Add(collapseItem);
            }
            menu.Items.Add(new Separator());
        }

        var minimizeItem = new MenuItem { Header = "最小化" };
        minimizeItem.Click += (s, args) => WindowState = WindowState.Minimized;
        menu.Items.Add(minimizeItem);

        var maximizeItem = new MenuItem
        {
            Header = WindowState == WindowState.Maximized ? "元に戻す" : "最大化"
        };
        maximizeItem.Click += (s, args) =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        menu.Items.Add(maximizeItem);

        menu.Items.Add(new Separator());

        var closeItem = new MenuItem { Header = "閉じる" };
        closeItem.Click += (s, args) => Close();
        menu.Items.Add(closeItem);

        menu.PlacementTarget = TabScrollViewer;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void StartDragTracking(MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(this);
        _isDragging = false;
        CaptureMouse();
    }

    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        base.OnPreviewMouseMove(e);

        // タブドラッグ処理（ウィンドウドラッグより優先）
        if (_dragTab != null)
        {
            var currentPos = e.GetPosition(this);

            if (!_isDraggingTab && _tabDragStartPoint.HasValue)
            {
                var diff = currentPos - _tabDragStartPoint.Value;
                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    _isDraggingTab = true;
                }
            }

            if (_isDraggingTab)
            {
                // コンテンツエリア上かどうかを先に判定
                UpdateTileDropOverlay(currentPos);
                if (_isTileDropTarget)
                {
                    // タイルドロップ対象：タブインジケーターは非表示
                    TabDragIndicatorCanvas.Visibility = Visibility.Collapsed;
                }
                else
                {
                    // タブバー内：挿入位置インジケーターのみ更新（実際の移動はドロップ時）
                    UpdateTabDragIndicator(currentPos);
                }
            }

            return; // タブドラッグ中はウィンドウドラッグを無効化
        }

        // ウィンドウドラッグ処理
        // DragMove() はブロッキングモーダルループのため AllowsTransparency ウィンドウでは
        // ドラッグ中に描画が更新されない。GetCursorPos で手動追跡することで
        // Left/Top をリアルタイムに更新し、視覚的なズレをなくす。
        if (_dragStartPoint.HasValue)
        {
            // マウスキャプチャが別ウィンドウに奪われた場合などに MouseLeftButtonUp が
            // 届かず _isDragging が残ることがある。GetAsyncKeyState で実際のボタン状態を
            // チェックし、離されていたらここでドラッグを終了する。
            if ((NativeMethods.GetAsyncKeyState(NativeMethods.VK_LBUTTON) & 0x8000) == 0)
            {
                EndWindowDrag();
                return;
            }

            if (!_isDragging)
            {
                var currentPos = e.GetPosition(this);
                var diff = currentPos - _dragStartPoint.Value;

                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    _isDragging = true;
                    NativeMethods.GetCursorPos(out var cursorOrigin);

                    if (WindowState == WindowState.Maximized)
                    {
                        var src = PresentationSource.FromVisual(this);
                        double dpiX = src?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
                        double dpiY = src?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;
                        // マウスを押した位置（最大化時ウィンドウ内の論理座標）
                        var pressPos   = _dragStartPoint ?? e.GetPosition(this);
                        var relativeX  = pressPos.X / ActualWidth;
                        var dragOffsetY = pressPos.Y;
                        // RestoreBounds で復元後サイズを取得（最大化状態でも正確）
                        var restoreWidth = RestoreBounds.Width;
                        WindowState = WindowState.Normal;
                        Left = cursorOrigin.X / dpiX - restoreWidth * relativeX;
                        Top  = cursorOrigin.Y / dpiY - dragOffsetY;
                    }

                    _dragWindowOriginX  = Left;
                    _dragWindowOriginY  = Top;
                    _dragCursorOriginX  = cursorOrigin.X;
                    _dragCursorOriginY  = cursorOrigin.Y;

                    // RDP 等イベント間引き環境でも追従をスムーズにするためポーリングタイマーを起動
                    if (_dragPollTimer == null)
                    {
                        _dragPollTimer = new DispatcherTimer(
                            TimeSpan.FromMilliseconds(16),
                            DispatcherPriority.Input,
                            DragPollTimer_Tick,
                            Dispatcher);
                    }
                    _dragPollTimer.Start();
                }
            }

            if (_isDragging && NativeMethods.GetCursorPos(out var cursor))
                UpdateDragPosition(cursor.X, cursor.Y);
        }
    }

    private void EndWindowDrag()
    {
        _dragPollTimer?.Stop();
        bool wasDragging = _isDragging;
        _dragStartPoint = null;
        _isDragging = false;
        ReleaseMouseCapture();

        if (wasDragging)
            UpdateManagedWindowLayout(activate: false, positionOnlyUpdate: true);
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonUp(e);

        if (_pendingWindowControlAction != WindowControlAction.None)
        {
            var action = _pendingWindowControlAction;
            var button = _pendingWindowControlButton;

            ReleaseMouseCapture();
            ClearWindowControlAction(keepManagedWindowPromotionSuppressed: true);

            if (button != null && IsPointInsideElement(button, e.GetPosition(this)))
            {
                ExecuteWindowControlAction(action);
            }
            else
            {
                ClearWindowControlAction();
            }

            e.Handled = true;
            return;
        }

        if (_dragTab != null)
        {
            if (NativeMethods.GetCursorPos(out var cursorPt))
            {
                CompleteTabDrag(cursorPt.X, cursorPt.Y);
            }
            else
            {
                var dropPoint = e.GetPosition(this);
                var screenPoint = PointToScreen(dropPoint);
                CompleteTabDrag(
                    (int)Math.Round(screenPoint.X),
                    (int)Math.Round(screenPoint.Y));
            }

            return;
        }

        if (_dragStartPoint.HasValue)
        {
            EndWindowDrag();
        }
    }

    private void UpdateDragPosition(int cursorX, int cursorY)
    {
        var (dpiX, dpiY) = GetDpiScaleForPoint(cursorX, cursorY);
        double newLeft = _dragWindowOriginX + (cursorX - _dragCursorOriginX) / dpiX;
        double newTop  = _dragWindowOriginY + (cursorY - _dragCursorOriginY) / dpiY;

        const double epsilon = 0.5;
        if (Math.Abs(Left - newLeft) < epsilon && Math.Abs(Top - newTop) < epsilon)
            return;

        Left = newLeft;
        Top  = newTop;
    }

    private (double dpiX, double dpiY) GetDpiScaleForPoint(int x, int y)
    {
        var pt = new NativeMethods.POINT { X = x, Y = y };
        var hMonitor = NativeMethods.MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (hMonitor != IntPtr.Zero &&
            NativeMethods.GetDpiForMonitor(hMonitor, NativeMethods.MDT_EFFECTIVE_DPI, out uint dpiX, out uint dpiY) == 0)
        {
            return (dpiX / 96.0, dpiY / 96.0);
        }
        // フォールバック: ウィンドウが現在いるモニターの DPI
        var src = PresentationSource.FromVisual(this);
        return (
            src?.CompositionTarget?.TransformToDevice.M11 ?? 1.0,
            src?.CompositionTarget?.TransformToDevice.M22 ?? 1.0);
    }

    private void DragPollTimer_Tick(object? sender, EventArgs e)
    {
        if (!_isDragging)
        {
            _dragPollTimer?.Stop();
            return;
        }
        // マウスボタンが離されていたらドラッグ終了
        if ((NativeMethods.GetAsyncKeyState(NativeMethods.VK_LBUTTON) & 0x8000) == 0)
        {
            EndWindowDrag();
            return;
        }
        if (NativeMethods.GetCursorPos(out var cursor))
            UpdateDragPosition(cursor.X, cursor.Y);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _viewModel.IsWindowPickerOpen)
        {
            _viewModel.CloseWindowPickerCommand.Execute(null);
            RestoreEmbeddedWindow();
            e.Handled = true;
        }
    }

    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.XButton1 && _viewModel.IsWindowPickerOpen)
        {
            _viewModel.CloseWindowPickerCommand.Execute(null);
            RestoreEmbeddedWindow();
            e.Handled = true;
        }
    }
}
