using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WindowzTabManager.ViewModels;

namespace WindowzTabManager.Views.Settings;

public partial class StartupSettingsPage : UserControl
{
    // ─── ドラッグ元（2種類）─────────────────────────────────────────────────
    //
    //   AppDrag   … アプリカード（スタンドアロン / グループ内は問わない）
    //   GroupDrag … タイルグループカード全体
    //
    //   スタンドアロンかグループ内かは DragOver/Drop 時に vm.IsLaunchItem() で判定する。
    //
    private abstract class DragCtx { }
    private sealed class AppDrag(StartupAppItem app) : DragCtx { public StartupAppItem App { get; } = app; }
    private sealed class GroupDrag(StartupTileGroupItem group) : DragCtx { public StartupTileGroupItem Group { get; } = group; }

    private DragCtx? _drag;

    // ─── ビジュアルフィードバック ─────────────────────────────────────────────

    private enum IndicatorMode { None, Reorder, Overlay }

    private Border? _reorderBorder;
    private Border? _overlayBorder;
    private readonly Dictionary<Border, (Thickness Thickness, Brush? Brush)> _savedBorders = new();
    private readonly SolidColorBrush _reorderBrush = new((Color)ColorConverter.ConvertFromString("#2F80ED"));
    private readonly SolidColorBrush _overlayBrush  = new((Color)ColorConverter.ConvertFromString("#2FA36B"));
    private const double EdgeRatio = 0.2;

    public StartupSettingsPage(StartupSettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.BrowseDone += () =>
        {
            StartupPathBox.Focus();
            StartupPathBox.CaretIndex = viewModel.NewStartupPath.Length;
        };
    }

    private void StartupPath_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is StartupSettingsViewModel vm)
        {
            vm.AddStartupApplicationCommand.Execute(null);
            e.Handled = true;
        }
    }

    // ─── ドラッグ開始 ─────────────────────────────────────────────────────────

    private void AppCard_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (sender is not FrameworkElement fe || fe.DataContext is not StartupAppItem app) return;

        _drag = new AppDrag(app);
        DragDrop.DoDragDrop(fe, app, DragDropEffects.Move);
        EndDrag();
    }

    private void TileGroupCard_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (sender is not FrameworkElement fe || fe.DataContext is not StartupTileGroupItem group) return;

        _drag = new GroupDrag(group);
        DragDrop.DoDragDrop(fe, group, DragDropEffects.Move);
        EndDrag();
    }

    // ─── DragOver（ビジュアル判定）──────────────────────────────────────────

    // ドロップ先 = アプリカード（トップレベルの単独アプリ or タイルグループ内アプリ）
    private void AppCard_DragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (_drag == null || DataContext is not StartupSettingsViewModel vm
            || sender is not FrameworkElement fe || fe.DataContext is not StartupAppItem target)
        {
            e.Effects = DragDropEffects.None;
            ClearIndicators();
            return;
        }

        bool isEdge     = IsEdge(fe, e);
        bool targetIsTopLevel = vm.IsLaunchItem(target);

        var mode = _drag switch
        {
            AppDrag d when ReferenceEquals(d.App, target)
                => IndicatorMode.None,

            // App → 単独アプリ：端=並び替え or 切り離して挿入 / 中央=グループ化
            AppDrag when targetIsTopLevel
                => isEdge ? IndicatorMode.Reorder : IndicatorMode.Overlay,

            // App → グループ内アプリ：そのグループに追加
            AppDrag when !targetIsTopLevel
                => IndicatorMode.Overlay,

            // TileGroup → 単独アプリカード：並び替え
            GroupDrag when targetIsTopLevel
                => IndicatorMode.Reorder,

            _ => IndicatorMode.None
        };

        e.Effects = mode != IndicatorMode.None ? DragDropEffects.Move : DragDropEffects.None;
        if (sender is Border b) UpdateIndicator(b, mode, e);
        else ClearIndicators();
    }

    // ドロップ先 = タイルグループカード
    private void TileGroupCard_DragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (_drag == null || DataContext is not StartupSettingsViewModel vm
            || sender is not FrameworkElement fe || fe.DataContext is not StartupTileGroupItem target)
        {
            e.Effects = DragDropEffects.None;
            ClearIndicators();
            return;
        }

        bool isEdge = IsEdge(fe, e);

        var mode = _drag switch
        {
            // TileGroup → TileGroup：常に並び替え
            GroupDrag d when !ReferenceEquals(d.Group, target)
                => IndicatorMode.Reorder,

            // App → TileGroup：端=並び替え or 切り離して挿入 / 中央=グループに追加
            AppDrag d when isEdge
                => IndicatorMode.Reorder,

            AppDrag d when !isEdge && target.CanAddApp && !target.ContainsApp(d.App)
                => IndicatorMode.Overlay,

            _ => IndicatorMode.None
        };

        e.Effects = mode != IndicatorMode.None ? DragDropEffects.Move : DragDropEffects.None;
        if (sender is Border b) UpdateIndicator(b, mode, e);
        else ClearIndicators();
    }

    // ─── Drop（実処理）──────────────────────────────────────────────────────

    private void AppCard_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (_drag == null || DataContext is not StartupSettingsViewModel vm
            || sender is not FrameworkElement fe || fe.DataContext is not StartupAppItem target)
        {
            EndDrag(); return;
        }

        bool isEdge           = IsEdge(fe, e);
        bool after            = InsertAfter(fe, e);
        bool targetIsTopLevel = vm.IsLaunchItem(target);

        switch (_drag)
        {
            case AppDrag d when ReferenceEquals(d.App, target):
                break;

            // App → 単独アプリ 端：並び替え or グループから切り離して挿入
            case AppDrag d when targetIsTopLevel && isEdge:
                if (vm.IsLaunchItem(d.App))
                    vm.TryReorderLaunchItems(d.App, target, after);
                else
                    vm.MoveAppToStandaloneAtDrop(d.App, target, after);
                break;

            // App → 任意のアプリカード 中央：グループ化 or 既存グループに追加
            // （CreateTileGroupFromDrop が「target が単独 → 新規グループ / グループ内 → 追加」に振り分け）
            case AppDrag d:
                vm.CreateTileGroupFromDrop(d.App, target);
                break;

            // TileGroup → 単独アプリカード：並び替え
            case GroupDrag d when targetIsTopLevel:
                vm.TryReorderLaunchItems(d.Group, target, after);
                break;
        }

        EndDrag();
    }

    private void TileGroupCard_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (_drag == null || DataContext is not StartupSettingsViewModel vm
            || sender is not FrameworkElement fe || fe.DataContext is not StartupTileGroupItem target)
        {
            EndDrag(); return;
        }

        bool isEdge = IsEdge(fe, e);
        bool after  = InsertAfter(fe, e);

        switch (_drag)
        {
            // TileGroup → TileGroup：並び替え
            case GroupDrag d when !ReferenceEquals(d.Group, target):
                vm.TryReorderLaunchItems(d.Group, target, after);
                break;

            // App → TileGroup 端：並び替え or グループから切り離して挿入
            case AppDrag d when isEdge:
                if (vm.IsLaunchItem(d.App))
                    vm.TryReorderLaunchItems(d.App, target, after);
                else
                    vm.MoveAppToStandaloneAtDrop(d.App, target, after);
                break;

            // App → TileGroup 中央：グループに追加
            case AppDrag d:
                vm.AddAppToTileGroupFromDrop(d.App, target);
                break;
        }

        EndDrag();
    }

    private void DropTarget_DragLeave(object sender, DragEventArgs e) => ClearIndicators();

    // ─── ヘルパー ─────────────────────────────────────────────────────────────

    private void EndDrag()
    {
        ClearIndicators();
        _drag = null;
    }

    private static bool InsertAfter(FrameworkElement fe, DragEventArgs e) =>
        e.GetPosition(fe).Y >= fe.ActualHeight / 2;

    private static bool IsEdge(FrameworkElement fe, DragEventArgs e)
    {
        if (fe.ActualHeight <= 0) return false;
        double y = e.GetPosition(fe).Y;
        double t = fe.ActualHeight * EdgeRatio;
        return y <= t || y >= fe.ActualHeight - t;
    }

    // ─── ビジュアルインジケーター管理 ────────────────────────────────────────

    private void UpdateIndicator(Border border, IndicatorMode mode, DragEventArgs e)
    {
        if (mode == IndicatorMode.None) { ClearIndicators(); return; }

        if (mode == IndicatorMode.Reorder)
        {
            if (_overlayBorder != null && _overlayBorder != border)
            {
                RestoreBorder(_overlayBorder);
                _overlayBorder = null;
            }
            SaveBorder(border);
            _reorderBorder = border;
            bool after = e.GetPosition(border).Y >= border.ActualHeight / 2;
            border.BorderBrush = _reorderBrush;
            border.BorderThickness = after ? new Thickness(0, 0, 0, 3) : new Thickness(0, 3, 0, 0);
        }
        else // Overlay
        {
            if (_reorderBorder != null && _reorderBorder != border)
            {
                RestoreBorder(_reorderBorder);
                _reorderBorder = null;
            }
            SaveBorder(border);
            _overlayBorder = border;
            border.BorderBrush = _overlayBrush;
            border.BorderThickness = new Thickness(2);
        }
    }

    private void ClearIndicators()
    {
        if (_reorderBorder != null) { RestoreBorder(_reorderBorder); _reorderBorder = null; }
        if (_overlayBorder  != null) { RestoreBorder(_overlayBorder);  _overlayBorder  = null; }
    }

    private void SaveBorder(Border border)
    {
        if (!_savedBorders.ContainsKey(border))
            _savedBorders[border] = (border.BorderThickness, border.BorderBrush);
    }

    private void RestoreBorder(Border border)
    {
        if (!_savedBorders.TryGetValue(border, out var saved)) return;
        border.BorderThickness = saved.Thickness;
        border.BorderBrush = saved.Brush;
        _savedBorders.Remove(border);
    }
}
