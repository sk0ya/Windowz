using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Wind.Views;

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
                UpdateTabDragPosition(currentPos);
            }

            return; // タブドラッグ中はウィンドウドラッグを無効化
        }

        // ウィンドウドラッグ処理
        if (_dragStartPoint.HasValue && !_isDragging)
        {
            var currentPos = e.GetPosition(this);
            var diff = currentPos - _dragStartPoint.Value;

            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                _isDragging = true;
                ReleaseMouseCapture();

                if (WindowState == WindowState.Maximized)
                {
                    // Get mouse position relative to screen
                    var mousePos = PointToScreen(e.GetPosition(this));

                    // Calculate relative position within window (as percentage)
                    var relativeX = e.GetPosition(this).X / ActualWidth;

                    // Restore window
                    WindowState = WindowState.Normal;

                    // Position window so mouse is at same relative position
                    Left = mousePos.X - (Width * relativeX);
                    Top = mousePos.Y - 18; // Half of title bar height
                }

                DragMove();
                _dragStartPoint = null;
            }
        }
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonUp(e);

        if (_dragTab != null)
        {
            _dragTab = null;
            _tabDragStartPoint = null;
            _isDraggingTab = false;
            return;
        }

        if (_dragStartPoint.HasValue)
        {
            _dragStartPoint = null;
            _isDragging = false;
            ReleaseMouseCapture();
        }
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
