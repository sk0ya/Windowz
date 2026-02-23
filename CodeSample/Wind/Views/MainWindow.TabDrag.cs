using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Wind.Views;

public partial class MainWindow
{
    private Models.TabItem? _dragTab;
    private Point? _tabDragStartPoint;
    private bool _isDraggingTab;

    private void UpdateTabDragPosition(Point mousePos)
    {
        if (_dragTab == null) return;

        bool isVertical = _currentTabPosition is "Left" or "Right";
        var borders = GetTabBorderElements();
        if (borders.Count == 0) return;

        int targetIndex = _tabManager.Tabs.Count - 1;

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

                if (pos < mid)
                {
                    targetIndex = i;
                    break;
                }
                targetIndex = i;
            }
            catch (InvalidOperationException)
            {
                // ドラッグ中にビジュアルツリーが変化した場合は無視
                break;
            }
        }

        int currentIndex = _tabManager.Tabs.IndexOf(_dragTab);
        if (currentIndex >= 0 && targetIndex != currentIndex)
        {
            _tabManager.MoveTab(_dragTab, targetIndex);
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
