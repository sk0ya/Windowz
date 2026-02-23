using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Wind.Models;

namespace Wind.Views;

public partial class MainWindow
{
    private void BuildTileLayout(TileLayout tileLayout)
    {
        ClearTileLayout();

        var tabs = tileLayout.TiledTabs.ToList();
        if (tabs.Count < 2) return;

        TileContainer.RowDefinitions.Clear();
        TileContainer.ColumnDefinitions.Clear();
        TileContainer.Children.Clear();

        // Calculate grid dimensions
        GetGridLayout(tabs.Count, out int cols, out int rows, out var cellAssignments);

        for (int r = 0; r < rows; r++)
        {
            TileContainer.RowDefinitions.Add(new RowDefinition {Height = new GridLength(1, GridUnitType.Star)});
            // Add row splitter (except after last row)
            if (r < rows - 1)
            {
                TileContainer.RowDefinitions.Add(new RowDefinition {Height = new GridLength(4)});
            }
        }

        for (int c = 0; c < cols; c++)
        {
            TileContainer.ColumnDefinitions.Add(new ColumnDefinition {Width = new GridLength(1, GridUnitType.Star)});
            // Add column splitter (except after last column)
            if (c < cols - 1)
            {
                TileContainer.ColumnDefinitions.Add(new ColumnDefinition {Width = new GridLength(4)});
            }
        }

        // Place each tab's WindowHost in its assigned cell
        for (int i = 0; i < tabs.Count && i < cellAssignments.Count; i++)
        {
            var (row, col, rowSpan, colSpan) = cellAssignments[i];
            var host = _viewModel.GetWindowHost(tabs[i]);
            if (host == null) continue;

            var container = new Border
            {
                ClipToBounds = true,
                Padding = new Thickness(0),
                Margin = new Thickness(0),
            };

            var content = new ContentControl
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
                Focusable = false,
                Content = host,
            };

            container.Child = content;

            // Map to grid positions (accounting for splitter rows/columns)
            int gridRow = row * 2;
            int gridCol = col * 2;
            int gridRowSpan = rowSpan * 2 - 1;
            int gridColSpan = colSpan * 2 - 1;

            Grid.SetRow(container, gridRow);
            Grid.SetColumn(container, gridCol);
            Grid.SetRowSpan(container, gridRowSpan);
            Grid.SetColumnSpan(container, gridColSpan);

            TileContainer.Children.Add(container);
            _tiledHosts.Add(host);

            // Listen for size changes to resize hosted windows
            container.SizeChanged += (s, e) =>
            {
                var w = (int) e.NewSize.Width;
                var h = (int) e.NewSize.Height;
                if (w > 0 && h > 0)
                {
                    host.ResizeHostedWindow(w, h);
                }
            };
        }

        // Add GridSplitters
        // Vertical splitters (between columns)
        for (int c = 0; c < cols - 1; c++)
        {
            var splitter = new GridSplitter
            {
                Width = 4,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Background = new SolidColorBrush(Color.FromArgb(40, 128, 128, 128)),
                ResizeBehavior = GridResizeBehavior.PreviousAndNext,
            };
            Grid.SetColumn(splitter, c * 2 + 1);
            Grid.SetRow(splitter, 0);
            Grid.SetRowSpan(splitter, Math.Max(1, TileContainer.RowDefinitions.Count));
            TileContainer.Children.Add(splitter);
        }

        // Horizontal splitters (between rows)
        for (int r = 0; r < rows - 1; r++)
        {
            var splitter = new GridSplitter
            {
                Height = 4,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Background = new SolidColorBrush(Color.FromArgb(40, 128, 128, 128)),
                ResizeDirection = GridResizeDirection.Rows,
                ResizeBehavior = GridResizeBehavior.PreviousAndNext,
            };
            Grid.SetRow(splitter, r * 2 + 1);
            Grid.SetColumn(splitter, 0);
            Grid.SetColumnSpan(splitter, Math.Max(1, TileContainer.ColumnDefinitions.Count));
            TileContainer.Children.Add(splitter);
        }

        // Trigger initial resize
        Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
        {
            foreach (var host in _tiledHosts)
            {
                if (host.Parent is ContentControl cc && cc.Parent is Border b)
                {
                    var w = (int) b.ActualWidth;
                    var h = (int) b.ActualHeight;
                    if (w > 0 && h > 0)
                        host.ResizeHostedWindow(w, h);
                }
            }
        });
    }

    private void ClearTileLayout()
    {
        // Detach all hosts from tile containers (without disposing them)
        foreach (var child in TileContainer.Children.OfType<Border>().ToList())
        {
            if (child.Child is ContentControl cc)
            {
                cc.Content = null;
            }
        }

        TileContainer.Children.Clear();
        TileContainer.RowDefinitions.Clear();
        TileContainer.ColumnDefinitions.Clear();
        _tiledHosts.Clear();
    }

    private static void GetGridLayout(int count, out int cols, out int rows,
        out List<(int row, int col, int rowSpan, int colSpan)> assignments)
    {
        assignments = new List<(int, int, int, int)>();

        switch (count)
        {
            case 2:
                cols = 2;
                rows = 1;
                assignments.Add((0, 0, 1, 1));
                assignments.Add((0, 1, 1, 1));
                break;
            case 3:
                cols = 2;
                rows = 2;
                assignments.Add((0, 0, 2, 1)); // left, spans 2 rows
                assignments.Add((0, 1, 1, 1)); // right top
                assignments.Add((1, 1, 1, 1)); // right bottom
                break;
            case 4:
                cols = 2;
                rows = 2;
                assignments.Add((0, 0, 1, 1));
                assignments.Add((0, 1, 1, 1));
                assignments.Add((1, 0, 1, 1));
                assignments.Add((1, 1, 1, 1));
                break;
            default:
                // For 5+, use a roughly square grid
                cols = (int) Math.Ceiling(Math.Sqrt(count));
                rows = (int) Math.Ceiling((double) count / cols);
                int idx = 0;
                for (int r = 0; r < rows && idx < count; r++)
                {
                    for (int c = 0; c < cols && idx < count; c++)
                    {
                        assignments.Add((r, c, 1, 1));
                        idx++;
                    }
                }

                // If last row is not full, let the last item span remaining columns
                if (count % cols != 0)
                {
                    var last = assignments[assignments.Count - 1];
                    int remaining = cols - (count % cols);
                    assignments[assignments.Count - 1] = (last.row, last.col, 1, 1 + remaining);
                }

                break;
        }
    }
}
