using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WindowzTabManager.ViewModels;

namespace WindowzTabManager.Views.Settings;

public partial class StartupSettingsPage : UserControl
{
    private StartupAppItem? _dragSource;

    public StartupSettingsPage(StartupSettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void StartupPath_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is StartupSettingsViewModel vm)
        {
            vm.AddStartupApplicationCommand.Execute(null);
            e.Handled = true;
        }
    }

    // ─── ドラッグ&ドロップ ───────────────────────────────────

    private void AppCard_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (sender is FrameworkElement fe && fe.DataContext is StartupAppItem item)
        {
            _dragSource = item;
            DragDrop.DoDragDrop(fe, item, DragDropEffects.Move);
        }
    }

    private void AppCard_Drop(object sender, DragEventArgs e)
    {
        if (DataContext is not StartupSettingsViewModel vm) return;
        if (_dragSource == null) return;

        if (sender is FrameworkElement fe && fe.DataContext is StartupAppItem target && target != _dragSource)
        {
            vm.CreateTileGroupFromDrop(_dragSource, target);
        }

        _dragSource = null;
        e.Handled = true;
    }

    private void AppCard_DragOver(object sender, DragEventArgs e)
    {
        if (_dragSource != null && sender is FrameworkElement fe && fe.DataContext is StartupAppItem target && target != _dragSource)
            e.Effects = DragDropEffects.Move;
        else
            e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void TileGroupCard_Drop(object sender, DragEventArgs e)
    {
        if (DataContext is not StartupSettingsViewModel vm) return;
        if (_dragSource == null) return;

        if (sender is FrameworkElement fe && fe.DataContext is StartupTileGroupItem group)
        {
            vm.AddAppToTileGroupFromDrop(_dragSource, group);
        }

        _dragSource = null;
        e.Handled = true;
    }

    private void TileGroupCard_DragOver(object sender, DragEventArgs e)
    {
        if (_dragSource != null && sender is FrameworkElement fe && fe.DataContext is StartupTileGroupItem group && group.CanAddApp)
            e.Effects = DragDropEffects.Move;
        else
            e.Effects = DragDropEffects.None;
        e.Handled = true;
    }
}
