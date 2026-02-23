using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using Wind.ViewModels;

namespace Wind.Views;

public partial class CommandPalette : UserControl
{
    private int _focusRequestId;

    public CommandPalette()
    {
        InitializeComponent();
        IsVisibleChanged += OnIsVisibleChanged;
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            RequestSearchBoxFocus();
        }
        else
        {
            _focusRequestId++;
        }
    }

    public void RequestSearchBoxFocus()
    {
        if (!IsVisible)
            return;

        int requestId = ++_focusRequestId;
        _ = FocusSearchBoxWithRetryAsync(requestId);
    }

    private async Task FocusSearchBoxWithRetryAsync(int requestId)
    {
        var priorities = new[]
        {
            DispatcherPriority.Loaded,
            DispatcherPriority.Input,
            DispatcherPriority.Render,
            DispatcherPriority.ContextIdle,
            DispatcherPriority.ApplicationIdle
        };

        foreach (var priority in priorities)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                if (requestId != _focusRequestId || !IsVisible)
                    return;

                TryFocusSearchBox();
            }, priority);

            if (requestId != _focusRequestId || !IsVisible)
                return;

            if (IsSearchBoxFocused())
                return;
        }
    }

    private void TryFocusSearchBox()
    {
        SearchBox.ApplyTemplate();
        SearchBox.UpdateLayout();
        UpdateLayout();

        var inner = FindVisualChild<System.Windows.Controls.TextBox>(SearchBox);
        var target = inner as IInputElement ?? SearchBox;
        Keyboard.Focus(target);

        if (inner != null)
        {
            inner.SelectAll();
            inner.CaretIndex = inner.Text?.Length ?? 0;
        }
    }

    private bool IsSearchBoxFocused()
    {
        if (Keyboard.FocusedElement is not DependencyObject focusedElement)
            return false;

        return IsDescendantOf(focusedElement, SearchBox);
    }

    private static bool IsDescendantOf(DependencyObject candidate, DependencyObject ancestor)
    {
        DependencyObject? current = candidate;
        while (current != null)
        {
            if (ReferenceEquals(current, ancestor))
                return true;

            current = GetParent(current);
        }

        return false;
    }

    private static DependencyObject? GetParent(DependencyObject current)
    {
        if (current is Visual || current is Visual3D)
            return VisualTreeHelper.GetParent(current);

        return LogicalTreeHelper.GetParent(current);
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not CommandPaletteViewModel vm) return;

        switch (e.Key)
        {
            case Key.Down:
                vm.MoveSelectionDown();
                ScrollToSelected();
                e.Handled = true;
                break;
            case Key.Up:
                vm.MoveSelectionUp();
                ScrollToSelected();
                e.Handled = true;
                break;
            case Key.Enter:
                vm.ExecuteCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Escape:
                vm.CancelCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void ScrollToSelected()
    {
        if (ResultsList.SelectedItem != null)
            ResultsList.ScrollIntoView(ResultsList.SelectedItem);
    }

    private void ListBox_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is CommandPaletteViewModel vm && ResultsList.SelectedItem != null)
            vm.ExecuteCommand.Execute(null);
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found)
                return found;
            var result = FindVisualChild<T>(child);
            if (result != null)
                return result;
        }
        return null;
    }
}
