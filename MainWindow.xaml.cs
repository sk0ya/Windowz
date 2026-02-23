using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace WindowzTabManager;

public partial class MainWindow : Window
{
    private readonly List<(ManagedWindow Window, TabItemControl Control)> _tabs = new();
    private int _activeIndex = -1;
    private readonly DispatcherTimer _timer;

    public MainWindow()
    {
        InitializeComponent();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTimerTick;
        _timer.Start();

        LocationChanged  += (_, _) => RepositionActiveWindow();
        SizeChanged      += (_, _) => RepositionActiveWindow();
        StateChanged     += OnStateChanged;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // WM_NCHITTEST フックを登録 (ウィンドウ枠でのリサイズを有効化)
        var hwndSource = HwndSource.FromVisual(this) as HwndSource;
        hwndSource?.AddHook(WndProc);

        KeyDown += OnKeyDown;
    }

    // ===== コンテンツエリアの物理ピクセル座標を取得 =====
    private (int x, int y, int w, int h) GetContentAreaPixels()
    {
        var pos = ContentAreaBorder.PointToScreen(new Point(0, 0));
        var dpi = VisualTreeHelper.GetDpi(this);
        int w = (int)(ContentAreaBorder.ActualWidth  * dpi.DpiScaleX);
        int h = (int)(ContentAreaBorder.ActualHeight * dpi.DpiScaleY);
        return ((int)pos.X, (int)pos.Y, w, h);
    }

    // ===== WM_NCHITTEST: ウィンドウ枠リサイズの有効化 =====
    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_NCHITTEST   = 0x0084;
        const int HTTOPLEFT      = 13;
        const int HTTOPRIGHT     = 14;
        const int HTBOTTOMLEFT   = 16;
        const int HTBOTTOMRIGHT  = 17;
        const int HTTOP          = 12;
        const int HTBOTTOM       = 15;
        const int HTLEFT         = 10;
        const int HTRIGHT        = 11;

        if (msg == WM_NCHITTEST)
        {
            int x = (short)(lParam.ToInt64() & 0xFFFF);
            int y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);

            NativeMethods.GetWindowRect(hwnd, out var wr);
            const int b = 6; // リサイズ感知幅 (物理ピクセル)

            bool onLeft   = x < wr.Left   + b;
            bool onRight  = x > wr.Right  - b;
            bool onTop    = y < wr.Top    + b;
            bool onBottom = y > wr.Bottom - b;

            if (onTop    && onLeft)  { handled = true; return (IntPtr)HTTOPLEFT;     }
            if (onTop    && onRight) { handled = true; return (IntPtr)HTTOPRIGHT;    }
            if (onBottom && onLeft)  { handled = true; return (IntPtr)HTBOTTOMLEFT;  }
            if (onBottom && onRight) { handled = true; return (IntPtr)HTBOTTOMRIGHT; }
            if (onTop)    { handled = true; return (IntPtr)HTTOP;    }
            if (onBottom) { handled = true; return (IntPtr)HTBOTTOM; }
            if (onLeft)   { handled = true; return (IntPtr)HTLEFT;   }
            if (onRight)  { handled = true; return (IntPtr)HTRIGHT;  }
        }
        return IntPtr.Zero;
    }

    // ===== タイトルバー: ドラッグ・最大化 =====
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal : WindowState.Maximized;
            return;
        }
        DragMove();
    }

    // ===== ウィンドウコントロールボタン =====
    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal : WindowState.Maximized;
    }

    private void CloseWindowButton_Click(object sender, RoutedEventArgs e)
    {
        ReleaseAllWindows();
        Application.Current.Shutdown();
    }

    // 最大化/復元でボタンアイコンを切り替え
    private void OnStateChanged(object? sender, EventArgs e)
    {
        MaxRestoreButton.Content = WindowState == WindowState.Maximized ? "❐" : "□";

        if (WindowState == WindowState.Minimized)
        {
            // 管理ウィンドウを非表示
            if (_activeIndex >= 0 && _activeIndex < _tabs.Count)
            {
                var (win, _) = _tabs[_activeIndex];
                if (win.IsAlive) NativeMethods.ShowWindow(win.Handle, NativeMethods.SW_HIDE);
            }
        }
        else
        {
            RepositionActiveWindow(forceShow: true);
        }
    }

    // ===== ハンバーガーメニュー =====
    private void HamburgerButton_Click(object sender, RoutedEventArgs e)
        => ContextMenu!.IsOpen = true;

    // ===== キーボードショートカット =====
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        switch (e.Key)
        {
            case Key.T:  OpenWindowPicker(); e.Handled = true; break;
            case Key.W:  CloseActiveTab();   e.Handled = true; break;
            case Key.Tab:
                SwitchTab(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1);
                e.Handled = true;
                break;
        }
    }

    // ===== タブ追加 =====
    private void AddButton_Click(object sender, RoutedEventArgs e) => OpenWindowPicker();

    private void OpenWindowPicker()
    {
        var picker = new WindowPickerWindow(_tabs.Select(t => t.Window.Handle));
        picker.Owner = this;
        if (picker.ShowDialog() == true && picker.SelectedWindow != null)
            AddWindow(picker.SelectedWindow);
    }

    private void AddWindow(ManagedWindow window)
    {
        NativeMethods.GetWindowRect(window.Handle, out var rect);
        window.OriginalRect  = rect;
        window.WasMinimized  = NativeMethods.IsIconic(window.Handle);

        var ctrl = new TabItemControl();
        ctrl.SetWindow(window);
        ctrl.HorizontalAlignment = HorizontalAlignment.Stretch;
        ctrl.TabClicked     += (_, _) => SwitchToTab(_tabs.FindIndex(t => t.Control == ctrl));
        ctrl.CloseRequested += (_, _) => RemoveTab(_tabs.FindIndex(t => t.Control == ctrl));

        _tabs.Add((window, ctrl));
        TabsPanel.Children.Add(ctrl);

        EmptyHint.Visibility = Visibility.Collapsed;
        SwitchToTab(_tabs.Count - 1);
    }

    // ===== タブ切り替え =====
    private void SwitchToTab(int index)
    {
        if (index < 0 || index >= _tabs.Count) return;

        // 現在アクティブなウィンドウを非表示
        if (_activeIndex >= 0 && _activeIndex < _tabs.Count && _activeIndex != index)
        {
            var (prevWin, prevCtrl) = _tabs[_activeIndex];
            prevCtrl.IsActive = false;
            if (prevWin.IsAlive)
                NativeMethods.ShowWindow(prevWin.Handle, NativeMethods.SW_HIDE);
        }

        _activeIndex = index;
        var (win, ctrl2) = _tabs[index];
        ctrl2.IsActive = true;

        if (win.IsAlive && WindowState != WindowState.Minimized)
        {
            PositionManagedWindow(win.Handle);
            NativeMethods.ShowWindow(win.Handle, NativeMethods.SW_SHOW);
            NativeMethods.SetForegroundWindow(win.Handle);
        }
    }

    private void SwitchTab(int delta)
    {
        if (_tabs.Count == 0) return;
        SwitchToTab((_activeIndex + delta + _tabs.Count) % _tabs.Count);
    }

    private void CloseActiveTab()
    {
        if (_activeIndex >= 0 && _activeIndex < _tabs.Count)
            RemoveTab(_activeIndex);
    }

    // ===== 管理ウィンドウの位置調整 =====
    private void PositionManagedWindow(IntPtr hWnd)
    {
        if (NativeMethods.IsIconic(hWnd))
            NativeMethods.ShowWindow(hWnd, NativeMethods.SW_RESTORE);

        var (x, y, w, h) = GetContentAreaPixels();
        if (w <= 0 || h <= 0) return;

        NativeMethods.SetWindowPos(
            hWnd, NativeMethods.HWND_TOP,
            x, y, w, h,
            NativeMethods.SWP_NOACTIVATE);
    }

    private void RepositionActiveWindow(bool forceShow = false)
    {
        if (_activeIndex < 0 || _activeIndex >= _tabs.Count) return;
        if (WindowState == WindowState.Minimized) return;

        var (win, _) = _tabs[_activeIndex];
        if (!win.IsAlive) return;

        PositionManagedWindow(win.Handle);

        if (forceShow)
        {
            NativeMethods.ShowWindow(win.Handle, NativeMethods.SW_SHOW);
            NativeMethods.SetForegroundWindow(win.Handle);
        }
    }

    // ===== コンテンツエリアのサイズ変更 (リサイズ / スプリッター) =====
    private void ContentArea_SizeChanged(object sender, SizeChangedEventArgs e)
        => RepositionActiveWindow();

    // ===== ウィンドウ移動 =====
    private void Window_LocationChanged(object sender, EventArgs e)
        => RepositionActiveWindow();

    // ===== タブを閉じる =====
    private void RemoveTab(int index)
    {
        if (index < 0 || index >= _tabs.Count) return;

        var (win, ctrl) = _tabs[index];

        // 元の位置に復元
        if (win.IsAlive)
        {
            var orig = win.OriginalRect;
            NativeMethods.SetWindowPos(win.Handle, NativeMethods.HWND_TOP,
                orig.Left, orig.Top, orig.Width, orig.Height,
                NativeMethods.SWP_SHOWWINDOW);
            if (!win.WasMinimized)
                NativeMethods.ShowWindow(win.Handle, NativeMethods.SW_SHOWNORMAL);
        }

        TabsPanel.Children.Remove(ctrl);
        _tabs.RemoveAt(index);

        if (_activeIndex >= _tabs.Count)
            _activeIndex = _tabs.Count - 1;

        if (_activeIndex >= 0)
            SwitchToTab(_activeIndex);
        else
        {
            _activeIndex = -1;
            EmptyHint.Visibility = Visibility.Visible;
        }
    }

    // ===== 全ウィンドウ解放 =====
    private void ReleaseAllWindows()
    {
        for (int i = _tabs.Count - 1; i >= 0; i--)
            RemoveTab(i);
    }

    private void ReleaseAllMenuItem_Click(object sender, RoutedEventArgs e)
        => ReleaseAllWindows();

    // ===== タイマー: 閉じたウィンドウ検出 & タイトル更新 =====
    private void OnTimerTick(object? sender, EventArgs e)
    {
        var deadIndices = _tabs
            .Select((t, i) => (t, i))
            .Where(x => !x.t.Window.IsAlive)
            .Select(x => x.i)
            .OrderByDescending(i => i)
            .ToList();

        foreach (var idx in deadIndices)
        {
            TabsPanel.Children.Remove(_tabs[idx].Control);
            _tabs.RemoveAt(idx);
            if (_activeIndex >= _tabs.Count)
                _activeIndex = _tabs.Count - 1;
        }

        if (deadIndices.Count > 0)
        {
            if (_activeIndex >= 0) SwitchToTab(_activeIndex);
            else EmptyHint.Visibility = Visibility.Visible;
        }

        // タイトル更新
        foreach (var (win, ctrl) in _tabs)
        {
            if (!win.IsAlive) continue;
            var old = win.Title;
            win.RefreshTitle();
            if (win.Title != old) ctrl.UpdateTitle(win.Title);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        ReleaseAllWindows();
        base.OnClosed(e);
    }
}
