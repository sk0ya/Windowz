using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Wind.Views;

public partial class MainWindow
{
    private void OnTabHeaderPositionChanged(string position)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
        {
            ApplyTabHeaderPosition(position);
            UpdateWindowHostSize();
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, UpdateBlockerPosition);
        });
    }

    private void ResetLayoutProperties()
    {
        // Clear all grid definitions
        RootGrid.RowDefinitions.Clear();
        RootGrid.ColumnDefinitions.Clear();
        TabBarArea.RowDefinitions.Clear();
        TabBarArea.ColumnDefinitions.Clear();

        // Reset Grid attached properties for all major elements to defaults
        UIElement[] elements =
        [
            TabBarArea, TabBarSeparator, ContentPanel, WindowPickerOverlay, CommandPaletteOverlay,
            TabScrollViewer, WindowControlsPanel
        ];
        foreach (var el in elements)
        {
            Grid.SetRow(el, 0);
            Grid.SetColumn(el, 0);
            Grid.SetRowSpan(el, 1);
            Grid.SetColumnSpan(el, 1);
        }

        // Reset TabBarArea sizing (set by Left/Right layouts)
        TabBarArea.ClearValue(MinWidthProperty);
        TabBarArea.ClearValue(MaxWidthProperty);
        TabBarArea.ClearValue(MinHeightProperty);
        TabBarArea.ClearValue(MaxHeightProperty);
        TabBarArea.ClearValue(WidthProperty);
        TabBarArea.ClearValue(HeightProperty);

        // Reset WindowControlsPanel alignment
        WindowControlsPanel.ClearValue(HorizontalAlignmentProperty);
        WindowControlsPanel.ClearValue(VerticalAlignmentProperty);

        // Reset DragBar and Separator
        TabBarSeparator.Visibility = Visibility.Collapsed;
        TabBarSeparator.ClearValue(WidthProperty);
        TabBarSeparator.ClearValue(HeightProperty);

        // Reset collapsed state
        _isTabBarCollapsed = false;
        AddWindowButton.Visibility = Visibility.Visible;
    }

    private void ApplyTabHeaderPosition(string position)
    {
        _currentTabPosition = position;
        bool isVertical = position is "Left" or "Right";

        // Full reset of all layout properties from previous layout
        ResetLayoutProperties();

        // Configure tab items and scroll for orientation
        // Reset AddWindowButton local values
        AddWindowButton.ClearValue(WidthProperty);
        AddWindowButton.ClearValue(HeightProperty);
        AddWindowButton.ClearValue(HorizontalAlignmentProperty);

        if (isVertical)
        {
            TabItemsControl.ItemsPanel = (ItemsPanelTemplate) FindResource("VerticalTabPanel");
            TabItemsControl.ItemTemplate = (DataTemplate) FindResource("VerticalTabItemTemplate");
            TabsPanel.Orientation = Orientation.Vertical;
            TabScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            TabScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
            WindowControlsPanel.Orientation = Orientation.Horizontal;
            AddWindowButton.Width = double.NaN;
            AddWindowButton.Height = 36;
            AddWindowButton.HorizontalAlignment = HorizontalAlignment.Stretch;

            // Set accent bar side: Left position → right accent, Right position → left accent
            Resources["VerticalTabAccentThickness"] = position == "Left"
                ? new Thickness(0, 0, 2, 0)
                : new Thickness(2, 0, 0, 0);
        }
        else
        {
            TabItemsControl.ItemsPanel = (ItemsPanelTemplate) FindResource("HorizontalTabPanel");
            TabItemsControl.ItemTemplate = (DataTemplate) FindResource("HorizontalTabItemTemplate");
            TabsPanel.Orientation = Orientation.Horizontal;
            TabScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden;
            TabScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            WindowControlsPanel.Orientation = Orientation.Horizontal;
            AddWindowButton.Width = 36;
            AddWindowButton.Height = 36;

            // Set accent bar side: Bottom position → top accent, Top position → bottom accent
            Resources["HorizontalTabAccentThickness"] = position == "Bottom"
                ? new Thickness(0, 2, 0, 0)
                : new Thickness(0, 0, 0, 2);
        }

        // Set button sizes for vertical/horizontal mode
        SetButtonSizesForMode(isVertical);

        switch (position)
        {
            case "Top":
                ApplyTopLayout();
                break;
            case "Bottom":
                ApplyBottomLayout();
                break;
            case "Left":
                ApplyLeftLayout();
                break;
            case "Right":
                ApplyRightLayout();
                break;
            default:
                ApplyTopLayout();
                break;
        }
    }

    private void SetButtonSizesForMode(bool isVertical)
    {
        // The TitleBarButtonStyle sets Width=44, Height=34.
        // For vertical mode, we clear the local Width so buttons share the row evenly.
        // For horizontal mode, we clear local values to let the Style apply.
        Button[] buttons = [MenuButton, MinimizeButton, MaximizeButton, CloseButton];

        foreach (var btn in buttons)
        {
            if (isVertical)
            {
                btn.ClearValue(WidthProperty);
                btn.Height = 34;
                btn.HorizontalAlignment = HorizontalAlignment.Stretch;
            }
            else
            {
                btn.ClearValue(WidthProperty);
                btn.ClearValue(HeightProperty);
                btn.ClearValue(HorizontalAlignmentProperty);
            }
        }
    }

    private void ApplyTopLayout()
    {
        // Grid: 2 rows (36px, *)
        RootGrid.RowDefinitions.Add(new RowDefinition {Height = new GridLength(36)});
        RootGrid.RowDefinitions.Add(new RowDefinition {Height = new GridLength(1, GridUnitType.Star)});

        // TabBarArea: Row 0
        Grid.SetRow(TabBarArea, 0);
        Grid.SetColumn(TabBarArea, 0);
        Grid.SetColumnSpan(TabBarArea, 1);

        // TabBarArea internal: 2 columns [ScrollViewer | Controls]
        TabBarArea.ColumnDefinitions.Add(new ColumnDefinition {Width = new GridLength(1, GridUnitType.Star)});
        TabBarArea.ColumnDefinitions.Add(new ColumnDefinition {Width = GridLength.Auto});
        Grid.SetRow(TabScrollViewer, 0);
        Grid.SetColumn(TabScrollViewer, 0);
        Grid.SetRow(WindowControlsPanel, 0);
        Grid.SetColumn(WindowControlsPanel, 1);

        // ContentPanel: Row 1
        Grid.SetRow(ContentPanel, 1);
        Grid.SetColumn(ContentPanel, 0);
        Grid.SetColumnSpan(ContentPanel, 1);

        // Overlay spans all rows
        Grid.SetRowSpan(WindowPickerOverlay, 2);
        Grid.SetColumnSpan(WindowPickerOverlay, 1);
        Grid.SetRowSpan(CommandPaletteOverlay, 2);
        Grid.SetColumnSpan(CommandPaletteOverlay, 1);
    }

    private void ApplyBottomLayout()
    {
        // Grid: 3 rows (6px drag, *, 36px tabs)
        RootGrid.RowDefinitions.Add(new RowDefinition {Height = new GridLength(0)});
        RootGrid.RowDefinitions.Add(new RowDefinition {Height = new GridLength(1, GridUnitType.Star)});
        RootGrid.RowDefinitions.Add(new RowDefinition {Height = new GridLength(36)});

        // ContentPanel: Row 1
        Grid.SetRow(ContentPanel, 1);
        Grid.SetColumn(ContentPanel, 0);
        Grid.SetColumnSpan(ContentPanel, 1);

        // TabBarArea: Row 2
        Grid.SetRow(TabBarArea, 2);
        Grid.SetColumn(TabBarArea, 0);
        Grid.SetColumnSpan(TabBarArea, 1);

        // TabBarArea internal: 2 columns [ScrollViewer | Controls]
        TabBarArea.ColumnDefinitions.Add(new ColumnDefinition {Width = new GridLength(1, GridUnitType.Star)});
        TabBarArea.ColumnDefinitions.Add(new ColumnDefinition {Width = GridLength.Auto});
        Grid.SetRow(TabScrollViewer, 0);
        Grid.SetColumn(TabScrollViewer, 0);
        Grid.SetRow(WindowControlsPanel, 0);
        Grid.SetColumn(WindowControlsPanel, 1);

        // Overlay spans all rows
        Grid.SetRowSpan(WindowPickerOverlay, 3);
        Grid.SetColumnSpan(WindowPickerOverlay, 1);
        Grid.SetRowSpan(CommandPaletteOverlay, 3);
        Grid.SetColumnSpan(CommandPaletteOverlay, 1);
    }

    private void ApplyLeftLayout()
    {
        // Grid: 1 row, 3 columns (Auto tabbar, 1px separator, * content)
        RootGrid.RowDefinitions.Add(new RowDefinition {Height = new GridLength(1, GridUnitType.Star)});
        RootGrid.ColumnDefinitions.Add(new ColumnDefinition {Width = GridLength.Auto});
        RootGrid.ColumnDefinitions.Add(new ColumnDefinition {Width = new GridLength(1)});
        RootGrid.ColumnDefinitions.Add(new ColumnDefinition {Width = new GridLength(1, GridUnitType.Star)});

        // TabBarArea: Row 0, Column 0 (vertical)
        Grid.SetRow(TabBarArea, 0);
        Grid.SetColumn(TabBarArea, 0);
        TabBarArea.MinWidth = 180;
        TabBarArea.MaxWidth = 300;

        // Separator: Row 0, Column 1
        TabBarSeparator.Visibility = Visibility.Visible;
        TabBarSeparator.Width = 1;
        Grid.SetRow(TabBarSeparator, 0);
        Grid.SetColumn(TabBarSeparator, 1);

        // TabBarArea internal: 2 rows [ButtonsPanel | ScrollViewer]
        TabBarArea.RowDefinitions.Add(new RowDefinition {Height = GridLength.Auto});
        TabBarArea.RowDefinitions.Add(new RowDefinition {Height = new GridLength(1, GridUnitType.Star)});
        Grid.SetRow(WindowControlsPanel, 0);
        Grid.SetColumn(WindowControlsPanel, 0);
        WindowControlsPanel.HorizontalAlignment = HorizontalAlignment.Stretch;
        Grid.SetRow(TabScrollViewer, 1);
        Grid.SetColumn(TabScrollViewer, 0);

        // ContentPanel: Row 0, Column 2
        Grid.SetRow(ContentPanel, 0);
        Grid.SetColumn(ContentPanel, 2);

        // Overlay spans everything
        Grid.SetRowSpan(WindowPickerOverlay, 1);
        Grid.SetColumnSpan(WindowPickerOverlay, 3);
        Grid.SetRowSpan(CommandPaletteOverlay, 1);
        Grid.SetColumnSpan(CommandPaletteOverlay, 3);
    }

    private void ApplyRightLayout()
    {
        // Grid: 1 row, 3 columns (* content, 1px separator, Auto tabbar)
        RootGrid.RowDefinitions.Add(new RowDefinition {Height = new GridLength(1, GridUnitType.Star)});
        RootGrid.ColumnDefinitions.Add(new ColumnDefinition {Width = new GridLength(1, GridUnitType.Star)});
        RootGrid.ColumnDefinitions.Add(new ColumnDefinition {Width = new GridLength(1)});
        RootGrid.ColumnDefinitions.Add(new ColumnDefinition {Width = GridLength.Auto});

        // ContentPanel: Row 0, Column 0
        Grid.SetRow(ContentPanel, 0);
        Grid.SetColumn(ContentPanel, 0);

        // Separator: Row 0, Column 1
        TabBarSeparator.Visibility = Visibility.Visible;
        TabBarSeparator.Width = 1;
        Grid.SetRow(TabBarSeparator, 0);
        Grid.SetColumn(TabBarSeparator, 1);

        // TabBarArea: Row 0, Column 2 (vertical)
        Grid.SetRow(TabBarArea, 0);
        Grid.SetColumn(TabBarArea, 2);
        TabBarArea.MinWidth = 180;
        TabBarArea.MaxWidth = 300;

        // TabBarArea internal: 2 rows [ButtonsPanel | ScrollViewer]
        TabBarArea.RowDefinitions.Add(new RowDefinition {Height = GridLength.Auto});
        TabBarArea.RowDefinitions.Add(new RowDefinition {Height = new GridLength(1, GridUnitType.Star)});
        Grid.SetRow(WindowControlsPanel, 0);
        Grid.SetColumn(WindowControlsPanel, 0);
        WindowControlsPanel.HorizontalAlignment = HorizontalAlignment.Stretch;
        Grid.SetRow(TabScrollViewer, 1);
        Grid.SetColumn(TabScrollViewer, 0);

        // Overlay spans everything
        Grid.SetRowSpan(WindowPickerOverlay, 1);
        Grid.SetColumnSpan(WindowPickerOverlay, 3);
        Grid.SetRowSpan(CommandPaletteOverlay, 1);
        Grid.SetColumnSpan(CommandPaletteOverlay, 3);
    }

    private void ToggleTabBarCollapsed()
    {
        if (_currentTabPosition is not ("Left" or "Right"))
            return;

        _isTabBarCollapsed = !_isTabBarCollapsed;

        if (_isTabBarCollapsed)
        {
            // Collapse: icon-only mode
            TabItemsControl.ItemTemplate = (DataTemplate) FindResource("CollapsedVerticalTabItemTemplate");
            TabBarArea.MinWidth = 0;
            TabBarArea.MaxWidth = double.PositiveInfinity;
            TabBarArea.Width = 40;

            // Hide window control button text, keep icons compact
            foreach (UIElement child in WindowControlsPanel.Children)
            {
                if (child is Button btn)
                {
                    btn.Width = 36;
                    btn.Height = 26;
                }
            }
        }
        else
        {
            // Expand: restore full vertical mode
            TabItemsControl.ItemTemplate = (DataTemplate) FindResource("VerticalTabItemTemplate");
            TabBarArea.ClearValue(WidthProperty);
            TabBarArea.MinWidth = 180;
            TabBarArea.MaxWidth = 300;

            SetButtonSizesForMode(isVertical: true);
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            UpdateWindowHostSize();
            UpdateBlockerPosition();
        });
    }
}
