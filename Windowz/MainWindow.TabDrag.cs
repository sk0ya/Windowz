using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WindowzTabManager;

public partial class MainWindow
{
    private Models.TabItem? _dragTab;
    private Models.TabItem? _preDragActiveTab;
    private Point? _tabDragStartPoint;
    private bool _isDraggingTab;
    private bool _isTileDropTarget;
    private int _tabDragInsertionPoint = -1; // -1 = 未計算
    private Views.TileDragOverlayWindow? _tileOverlayWindow;

    /// <summary>
    /// ドラッグ中のマウス位置からタブ挿入点インジケーターを更新する。
    /// タブの実際の移動はドロップ時まで行わない。
    /// </summary>
    private void UpdateTabDragIndicator(Point mousePos)
    {
        if (_dragTab == null || !_isDraggingTab)
        {
            TabDragIndicatorCanvas.Visibility = Visibility.Collapsed;
            return;
        }

        var borders = GetTabBorderElements();
        if (borders.Count == 0)
        {
            TabDragIndicatorCanvas.Visibility = Visibility.Collapsed;
            return;
        }

        bool isVertical = _currentTabPosition is "Left" or "Right";

        // マウス位置からタブバー内の挿入点を計算（0 = 先頭の前、n = 末尾の後）
        int insertionPoint = borders.Count;
        for (int i = 0; i < borders.Count; i++)
        {
            var border = borders[i];
            if (!border.IsLoaded) continue;
            try
            {
                var topLeft = border.TransformToAncestor(this).Transform(new Point(0, 0));
                double mid = isVertical
                    ? topLeft.Y + border.ActualHeight / 2
                    : topLeft.X + border.ActualWidth / 2;
                double pos = isVertical ? mousePos.Y : mousePos.X;
                if (pos < mid) { insertionPoint = i; break; }
            }
            catch (InvalidOperationException) { break; }
        }

        _tabDragInsertionPoint = insertionPoint;

        // インジケーター線の位置を TabBarArea 座標系で計算
        try
        {
            var tabBarOrigin = TabBarArea.TransformToAncestor(this).Transform(new Point(0, 0));
            double lineX, lineY, lineW, lineH;

            if (isVertical)
            {
                lineX = 0;
                lineW = TabBarArea.ActualWidth;
                lineH = 2;
                if (insertionPoint == 0)
                {
                    var first = borders[0].TransformToAncestor(this).Transform(new Point(0, 0));
                    lineY = first.Y - tabBarOrigin.Y;
                }
                else
                {
                    int refIdx = Math.Min(insertionPoint, borders.Count) - 1;
                    var refBorder = borders[refIdx];
                    var refPos = refBorder.TransformToAncestor(this).Transform(new Point(0, 0));
                    lineY = refPos.Y + refBorder.ActualHeight - tabBarOrigin.Y;
                }
            }
            else
            {
                lineY = 0;
                lineW = 2;
                lineH = TabBarArea.ActualHeight;
                if (insertionPoint == 0)
                {
                    var first = borders[0].TransformToAncestor(this).Transform(new Point(0, 0));
                    lineX = first.X - tabBarOrigin.X;
                }
                else
                {
                    int refIdx = Math.Min(insertionPoint, borders.Count) - 1;
                    var refBorder = borders[refIdx];
                    var refPos = refBorder.TransformToAncestor(this).Transform(new Point(0, 0));
                    lineX = refPos.X + refBorder.ActualWidth - tabBarOrigin.X;
                }
            }

            Canvas.SetLeft(TabDragIndicatorLine, lineX);
            Canvas.SetTop(TabDragIndicatorLine, lineY);
            TabDragIndicatorLine.Width = lineW;
            TabDragIndicatorLine.Height = lineH;
            TabDragIndicatorCanvas.Visibility = Visibility.Visible;
        }
        catch
        {
            TabDragIndicatorCanvas.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// ドラッグ中のタブがコンテンツエリア上にあるかを判定し、別ウィンドウオーバーレイを更新する。
    /// </summary>
    private void UpdateTileDropOverlay(Point mousePos)
    {
        if (_dragTab == null || !_isDraggingTab || _preDragActiveTab == null || _preDragActiveTab == _dragTab)
        {
            HideTileDropOverlay();
            return;
        }

        var anchor = _preDragActiveTab;

        // 既存タイルが満杯なら無効
        if (anchor.TileLayout != null && anchor.TileLayout.Tabs.Count >= 4
            && !anchor.TileLayout.Tabs.Contains(_dragTab))
        {
            HideTileDropOverlay();
            return;
        }

        // ドラッグタブが既に同じタイルのメンバーなら無効
        if (_dragTab.TileLayout != null && _dragTab.TileLayout == anchor.TileLayout)
        {
            HideTileDropOverlay();
            return;
        }

        try
        {
            var panelPos = ContentPanel.TransformToAncestor(this).Transform(new Point(0, 0));
            bool over = mousePos.X >= panelPos.X && mousePos.X <= panelPos.X + ContentPanel.ActualWidth
                     && mousePos.Y >= panelPos.Y && mousePos.Y <= panelPos.Y + ContentPanel.ActualHeight;

            _isTileDropTarget = over;
            if (over)
                ShowTileDropOverlay();
            else
                HideTileDropOverlay();
        }
        catch
        {
            _isTileDropTarget = false;
            HideTileDropOverlay();
        }
    }

    /// <summary>
    /// タイルドロップオーバーレイウィンドウを ContentPanel の画面位置に合わせて表示する。
    /// </summary>
    private void ShowTileDropOverlay()
    {
        if (_tileOverlayWindow == null)
        {
            _tileOverlayWindow = new Views.TileDragOverlayWindow { Owner = this };
        }

        // ContentPanel の画面座標を取得し、DPI 変換してウィンドウ位置を設定
        var source = PresentationSource.FromVisual(this);
        double dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

        var screenPt = ContentPanel.PointToScreen(new Point(0, 0));
        _tileOverlayWindow.Left = screenPt.X / dpiX;
        _tileOverlayWindow.Top = screenPt.Y / dpiY;
        _tileOverlayWindow.Width = ContentPanel.ActualWidth;
        _tileOverlayWindow.Height = ContentPanel.ActualHeight;

        if (!_tileOverlayWindow.IsVisible)
            _tileOverlayWindow.Show();
    }

    private void HideTileDropOverlay()
    {
        _isTileDropTarget = false;
        _tileOverlayWindow?.Hide();
    }

    /// <summary>
    /// コンテンツエリアへのドロップ時にタイルを作成する。
    /// </summary>
    private void HandleTabDropOnContent(Models.TabItem droppedTab, Models.TabItem? anchor)
    {
        anchor ??= _tabManager.ActiveTab;
        if (anchor == null || anchor == droppedTab) return;

        var tile = _tabManager.AddTabToTile(droppedTab, anchor);
        if (tile != null)
        {
            UpdateManagedWindowLayout(activate: true);
            _viewModel.StatusMessage = $"タイル表示: {tile.Tabs.Count} タブ";
        }
        else
        {
            _viewModel.StatusMessage = "タイル表示できませんでした";
        }
    }

    private List<Border> GetTabBorderElements()
    {
        var result = new List<Border>();
        var generator = TabItemsControl.ItemContainerGenerator;

        for (int i = 0; i < TabItemsControl.Items.Count; i++)
        {
            if (generator.ContainerFromIndex(i) is ContentPresenter cp &&
                VisualTreeHelper.GetChildrenCount(cp) > 0 &&
                VisualTreeHelper.GetChild(cp, 0) is Border border)
            {
                result.Add(border);
            }
        }

        return result;
    }
}
