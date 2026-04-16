using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WindowzTabManager.Models;

namespace WindowzTabManager;

public partial class MainWindow
{
    private enum TileSplitterKind
    {
        Vertical,
        Horizontal,
        Both
    }

    private enum TileSplitterSegment
    {
        Full,
        Top,
        Bottom,
        Left,
        Right,
        Center
    }

    private readonly record struct TileSplitterTag(TileSplitterKind Kind, TileSplitterSegment Segment);

    private const double TileSplitterGapDip = 8.0;
    private const double TileSplitterVisibleThicknessDip = 2.0;
    private const double TileSplitterMinimumPaneDip = 120.0;
    private const double TileSplitterChangeEpsilon = 0.001;

    private bool _isDraggingTileSplitter;
    private TileSplitterKind _dragTileSplitterKind;
    private TileLayout? _dragTileSplitterLayout;
    private FrameworkElement? _dragTileSplitterElement;
    private double _dragTileSplitterVerticalSplit;
    private double _dragTileSplitterHorizontalSplit;

    private void UpdateTileSplitterOverlay(
        TileLayout tile,
        (double Left, double Top, double Width, double Height)[] fractions)
    {
        if (tile.Tabs.Count < 2 ||
            fractions.Length < 2 ||
            WindowHostContainer.ActualWidth <= 0 ||
            WindowHostContainer.ActualHeight <= 0 ||
            _viewModel.IsWindowPickerOpen ||
            _viewModel.IsCommandPaletteOpen)
        {
            HideTileSplitterOverlay();
            return;
        }

        TileSplitterCanvas.Visibility = Visibility.Visible;

        int expectedSplitterCount = GetExpectedTileSplitterCount(tile.Tabs.Count);
        if (!_isDraggingTileSplitter ||
            TileSplitterCanvas.Children.Count != expectedSplitterCount)
        {
            RebuildTileSplitterOverlay(tile.Tabs.Count);
        }

        if (_isDraggingTileSplitter && ReferenceEquals(tile, _dragTileSplitterLayout))
        {
            PositionTileSplitterOverlay(
                tile,
                _dragTileSplitterVerticalSplit,
                _dragTileSplitterHorizontalSplit);
            return;
        }

        PositionTileSplitterOverlay(tile);
    }

    private void HideTileSplitterOverlay()
    {
        if (!IsInitialized)
            return;

        TileSplitterCanvas.Children.Clear();
        TileSplitterCanvas.Visibility = Visibility.Collapsed;
    }

    private static int GetExpectedTileSplitterCount(int tileCount)
    {
        return tileCount switch
        {
            2 => 1,
            3 => 2,
            >= 4 => 5,
            _ => 0
        };
    }

    private void RebuildTileSplitterOverlay(int tileCount)
    {
        TileSplitterCanvas.Children.Clear();

        switch (tileCount)
        {
            case 2:
                AddTileSplitter(TileSplitterKind.Vertical, TileSplitterSegment.Full);
                break;
            case 3:
                AddTileSplitter(TileSplitterKind.Vertical, TileSplitterSegment.Full);
                AddTileSplitter(TileSplitterKind.Horizontal, TileSplitterSegment.Right);
                break;
            default:
                AddTileSplitter(TileSplitterKind.Vertical, TileSplitterSegment.Top);
                AddTileSplitter(TileSplitterKind.Vertical, TileSplitterSegment.Bottom);
                AddTileSplitter(TileSplitterKind.Horizontal, TileSplitterSegment.Left);
                AddTileSplitter(TileSplitterKind.Horizontal, TileSplitterSegment.Right);
                AddTileSplitter(TileSplitterKind.Both, TileSplitterSegment.Center);
                break;
        }
    }

    private void AddTileSplitter(TileSplitterKind kind, TileSplitterSegment segment)
    {
        TileSplitterCanvas.Children.Add(CreateTileSplitter(kind, segment));
    }

    private void PositionTileSplitterOverlay(TileLayout tile)
    {
        PositionTileSplitterOverlay(tile, tile.VerticalSplit, tile.HorizontalSplit);
    }

    private void PositionTileSplitterOverlay(
        TileLayout tile,
        double verticalSplit,
        double horizontalSplit)
    {
        double width = WindowHostContainer.ActualWidth;
        double height = WindowHostContainer.ActualHeight;
        double verticalX = verticalSplit * width;
        double horizontalY = horizontalSplit * height;

        foreach (UIElement child in TileSplitterCanvas.Children)
        {
            if (child is not FrameworkElement element ||
                element.Tag is not TileSplitterTag tag)
            {
                continue;
            }

            switch (tag.Segment)
            {
                case TileSplitterSegment.Full:
                    SetTileSplitterBounds(element, verticalX - TileSplitterGapDip / 2.0, 0, TileSplitterGapDip, height);
                    break;
                case TileSplitterSegment.Top:
                    SetTileSplitterBounds(element, verticalX - TileSplitterGapDip / 2.0, 0, TileSplitterGapDip, horizontalY - TileSplitterGapDip / 2.0);
                    break;
                case TileSplitterSegment.Bottom:
                    SetTileSplitterBounds(
                        element,
                        verticalX - TileSplitterGapDip / 2.0,
                        horizontalY + TileSplitterGapDip / 2.0,
                        TileSplitterGapDip,
                        height - horizontalY - TileSplitterGapDip / 2.0);
                    break;
                case TileSplitterSegment.Left:
                    SetTileSplitterBounds(element, 0, horizontalY - TileSplitterGapDip / 2.0, verticalX - TileSplitterGapDip / 2.0, TileSplitterGapDip);
                    break;
                case TileSplitterSegment.Right:
                    double left = verticalX + TileSplitterGapDip / 2.0;
                    SetTileSplitterBounds(element, left, horizontalY - TileSplitterGapDip / 2.0, width - left, TileSplitterGapDip);
                    break;
                case TileSplitterSegment.Center:
                    SetTileSplitterBounds(
                        element,
                        verticalX - TileSplitterGapDip / 2.0,
                        horizontalY - TileSplitterGapDip / 2.0,
                        TileSplitterGapDip,
                        TileSplitterGapDip);
                    break;
            }
        }
    }

    private static void SetTileSplitterBounds(FrameworkElement element, double left, double top, double width, double height)
    {
        element.Width = Math.Max(1, width);
        element.Height = Math.Max(1, height);
        Canvas.SetLeft(element, left);
        Canvas.SetTop(element, top);
    }

    private Border CreateTileSplitter(TileSplitterKind kind, TileSplitterSegment segment)
    {
        var grip = new Border
        {
            Background = Brushes.Transparent,
            Tag = new TileSplitterTag(kind, segment),
            SnapsToDevicePixels = true
        };

        grip.Cursor = kind switch
        {
            TileSplitterKind.Vertical => Cursors.SizeWE,
            TileSplitterKind.Horizontal => Cursors.SizeNS,
            _ => Cursors.SizeAll
        };

        if (kind == TileSplitterKind.Both)
        {
            grip.Child = CreateTileSplitterCenterVisual();
        }
        else
        {
            grip.Child = CreateTileSplitterLineVisual(kind);
        }

        grip.MouseLeftButtonDown += TileSplitter_MouseLeftButtonDown;
        grip.MouseMove += TileSplitter_MouseMove;
        grip.MouseLeftButtonUp += TileSplitter_MouseLeftButtonUp;
        grip.LostMouseCapture += TileSplitter_LostMouseCapture;

        return grip;
    }

    private Border CreateTileSplitterLineVisual(TileSplitterKind kind)
    {
        var line = new Border
        {
            Opacity = 0.85,
            CornerRadius = new CornerRadius(1)
        };
        line.SetResourceReference(Border.BackgroundProperty, "AccentFillColorDefaultBrush");

        if (kind == TileSplitterKind.Vertical)
        {
            line.Width = TileSplitterVisibleThicknessDip;
            line.HorizontalAlignment = HorizontalAlignment.Center;
            line.VerticalAlignment = VerticalAlignment.Stretch;
        }
        else
        {
            line.Height = TileSplitterVisibleThicknessDip;
            line.HorizontalAlignment = HorizontalAlignment.Stretch;
            line.VerticalAlignment = VerticalAlignment.Center;
        }

        return line;
    }

    private Grid CreateTileSplitterCenterVisual()
    {
        var grid = new Grid { Opacity = 0.9 };

        var vertical = CreateTileSplitterLineVisual(TileSplitterKind.Vertical);
        var horizontal = CreateTileSplitterLineVisual(TileSplitterKind.Horizontal);

        grid.Children.Add(vertical);
        grid.Children.Add(horizontal);

        return grid;
    }

    private Rect GetTileSlotBoundsDip(
        (double Left, double Top, double Width, double Height) fraction,
        double containerWidth,
        double containerHeight)
    {
        double left = fraction.Left * containerWidth;
        double top = fraction.Top * containerHeight;
        double right = (fraction.Left + fraction.Width) * containerWidth;
        double bottom = (fraction.Top + fraction.Height) * containerHeight;

        ApplyTileSlotGap(fraction, TileSplitterGapDip, TileSplitterGapDip, ref left, ref top, ref right, ref bottom);

        return new Rect(
            left,
            top,
            Math.Max(1, right - left),
            Math.Max(1, bottom - top));
    }

    private NativeMethods.RECT GetTileSlotBoundsPx(
        (double Left, double Top, double Width, double Height) fraction,
        NativeMethods.RECT totalBounds)
    {
        var (dpiScaleX, dpiScaleY) = GetCurrentDpiScale();
        double gapX = TileSplitterGapDip * dpiScaleX;
        double gapY = TileSplitterGapDip * dpiScaleY;

        double left = totalBounds.Left + fraction.Left * totalBounds.Width;
        double top = totalBounds.Top + fraction.Top * totalBounds.Height;
        double right = totalBounds.Left + (fraction.Left + fraction.Width) * totalBounds.Width;
        double bottom = totalBounds.Top + (fraction.Top + fraction.Height) * totalBounds.Height;

        ApplyTileSlotGap(fraction, gapX, gapY, ref left, ref top, ref right, ref bottom);

        int roundedLeft = (int)Math.Round(left);
        int roundedTop = (int)Math.Round(top);
        int roundedRight = Math.Max(roundedLeft + 1, (int)Math.Round(right));
        int roundedBottom = Math.Max(roundedTop + 1, (int)Math.Round(bottom));

        return new NativeMethods.RECT
        {
            Left = roundedLeft,
            Top = roundedTop,
            Right = roundedRight,
            Bottom = roundedBottom
        };
    }

    private static void ApplyTileSlotGap(
        (double Left, double Top, double Width, double Height) fraction,
        double gapX,
        double gapY,
        ref double left,
        ref double top,
        ref double right,
        ref double bottom)
    {
        GetTileSlotGapInsets(
            fraction,
            gapX,
            gapY,
            out double leftGap,
            out double topGap,
            out double rightGap,
            out double bottomGap);

        left += leftGap;
        top += topGap;
        right -= rightGap;
        bottom -= bottomGap;
    }

    private static void GetTileSlotGapInsets(
        (double Left, double Top, double Width, double Height) fraction,
        double gapX,
        double gapY,
        out double leftGap,
        out double topGap,
        out double rightGap,
        out double bottomGap)
    {
        const double edgeEpsilon = 0.0001;

        leftGap = fraction.Left > edgeEpsilon ? gapX / 2.0 : 0;
        topGap = fraction.Top > edgeEpsilon ? gapY / 2.0 : 0;
        rightGap = fraction.Left + fraction.Width < 1.0 - edgeEpsilon ? gapX / 2.0 : 0;
        bottomGap = fraction.Top + fraction.Height < 1.0 - edgeEpsilon ? gapY / 2.0 : 0;
    }

    private (double X, double Y) GetCurrentDpiScale()
    {
        var source = PresentationSource.FromVisual(this);
        return (
            source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0,
            source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0);
    }

    private void TileSplitter_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element ||
            element.Tag is not TileSplitterTag tag)
        {
            return;
        }

        var tile = _viewModel.SelectedTab?.TileLayout;
        if (tile == null)
            return;

        _isDraggingTileSplitter = true;
        _dragTileSplitterKind = tag.Kind;
        _dragTileSplitterLayout = tile;
        _dragTileSplitterElement = element;
        _dragTileSplitterVerticalSplit = tile.VerticalSplit;
        _dragTileSplitterHorizontalSplit = tile.HorizontalSplit;
        element.CaptureMouse();

        e.Handled = true;
    }

    private void TileSplitter_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingTileSplitter ||
            _dragTileSplitterLayout == null)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            EndTileSplitterDrag(commitLayout: true);
            return;
        }

        if (_dragTileSplitterLayout != _viewModel.SelectedTab?.TileLayout)
        {
            EndTileSplitterDrag(commitLayout: false);
            return;
        }

        var point = e.GetPosition(WindowHostContainer);
        if (TryUpdateTileSplitPreview(_dragTileSplitterKind, point))
        {
            PositionTileSplitterOverlay(
                _dragTileSplitterLayout,
                _dragTileSplitterVerticalSplit,
                _dragTileSplitterHorizontalSplit);
        }

        e.Handled = true;
    }

    private void TileSplitter_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDraggingTileSplitter)
            return;

        EndTileSplitterDrag(commitLayout: true);
        e.Handled = true;
    }

    private void TileSplitter_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_isDraggingTileSplitter)
            EndTileSplitterDrag(commitLayout: true);
    }

    private bool TryUpdateTileSplitPreview(TileSplitterKind kind, Point point)
    {
        double width = WindowHostContainer.ActualWidth;
        double height = WindowHostContainer.ActualHeight;
        if (width <= 0 || height <= 0)
            return false;

        bool changed = false;

        if (kind is TileSplitterKind.Vertical or TileSplitterKind.Both)
        {
            double next = ClampTileSplitRatio(point.X / width, width);
            if (Math.Abs(_dragTileSplitterVerticalSplit - next) >= TileSplitterChangeEpsilon)
            {
                _dragTileSplitterVerticalSplit = next;
                changed = true;
            }
        }

        if (kind is TileSplitterKind.Horizontal or TileSplitterKind.Both)
        {
            double nextHorizontal = ClampTileSplitRatio(point.Y / height, height);
            if (Math.Abs(_dragTileSplitterHorizontalSplit - nextHorizontal) >= TileSplitterChangeEpsilon)
            {
                _dragTileSplitterHorizontalSplit = nextHorizontal;
                changed = true;
            }
        }

        return changed;
    }

    private static double ClampTileSplitRatio(double ratio, double length)
    {
        if (double.IsNaN(ratio) || double.IsInfinity(ratio))
            return 0.5;

        double minimum = length > 0
            ? Math.Min(0.45, TileSplitterMinimumPaneDip / length)
            : 0.1;

        return Math.Clamp(ratio, minimum, 1.0 - minimum);
    }

    private bool CommitTileSplitterPreview(TileLayout tile)
    {
        bool changed = false;

        if (Math.Abs(tile.VerticalSplit - _dragTileSplitterVerticalSplit) >= TileSplitterChangeEpsilon)
        {
            tile.SetVerticalSplit(_dragTileSplitterVerticalSplit);
            changed = true;
        }

        if (Math.Abs(tile.HorizontalSplit - _dragTileSplitterHorizontalSplit) >= TileSplitterChangeEpsilon)
        {
            tile.SetHorizontalSplit(_dragTileSplitterHorizontalSplit);
            changed = true;
        }

        return changed;
    }

    private void EndTileSplitterDrag(bool commitLayout)
    {
        var element = _dragTileSplitterElement;
        var tile = _dragTileSplitterLayout;
        bool changed = commitLayout && tile != null && CommitTileSplitterPreview(tile);

        _isDraggingTileSplitter = false;
        _dragTileSplitterLayout = null;
        _dragTileSplitterElement = null;
        _dragTileSplitterVerticalSplit = 0;
        _dragTileSplitterHorizontalSplit = 0;

        element?.ReleaseMouseCapture();

        if (commitLayout && changed)
        {
            UpdateManagedWindowLayout(activate: false);
        }
        else if (!commitLayout)
        {
            UpdateManagedWindowLayout(activate: false);
        }
    }
}
