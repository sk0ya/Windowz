using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace WindowzTabManager;

public partial class WindowPickerWindow : Window
{
    private readonly HashSet<IntPtr> _excludedHandles;
    private List<WindowEntry> _allWindows = new();

    internal ManagedWindow? SelectedWindow { get; private set; }

    public WindowPickerWindow(IEnumerable<IntPtr> excludedHandles)
    {
        _excludedHandles = new HashSet<IntPtr>(excludedHandles);
        InitializeComponent();
        Loaded += (_, _) => LoadWindows();
    }

    private void LoadWindows()
    {
        _allWindows.Clear();

        try
        {
            NativeMethods.EnumWindows((hWnd, _) =>
            {
                try
                {
                    if (!NativeMethods.IsWindowVisible(hWnd)) return true;
                    if (_excludedHandles.Contains(hWnd)) return true;

                    var len = NativeMethods.GetWindowTextLength(hWnd);
                    if (len == 0) return true;

                    var exStyle = NativeMethods.GetWindowLong(hWnd, NativeMethods.GWL_EXSTYLE);
                    if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0) return true;

                    if (NativeMethods.GetAncestor(hWnd, NativeMethods.GA_ROOT) != hWnd) return true;

                    var sb = new StringBuilder(len + 1);
                    NativeMethods.GetWindowText(hWnd, sb, sb.Capacity);
                    var title = sb.ToString();
                    if (string.IsNullOrWhiteSpace(title)) return true;

                    var icon = GetWindowIcon(hWnd);
                    _allWindows.Add(new WindowEntry(hWnd, title, icon));
                }
                catch { /* 個々のウィンドウでのエラーは無視 */ }
                return true;
            }, IntPtr.Zero);
        }
        catch { }

        RefreshList(_allWindows);
        FilterBox.Focus();
    }

    private void RefreshList(IEnumerable<WindowEntry> items)
    {
        WindowList.ItemsSource = items.ToList();
    }

    private void FilterBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // プレースホルダーの表示切り替え
        PlaceholderText.Visibility = string.IsNullOrEmpty(FilterBox.Text)
            ? Visibility.Visible : Visibility.Collapsed;

        var filter = FilterBox.Text;
        var filtered = string.IsNullOrWhiteSpace(filter)
            ? _allWindows
            : _allWindows.Where(w => w.Title.Contains(filter, StringComparison.OrdinalIgnoreCase));
        RefreshList(filtered);
    }

    private void WindowList_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => Confirm();

    private void OkButton_Click(object sender, RoutedEventArgs e) => Confirm();
    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();

    private void Confirm()
    {
        if (WindowList.SelectedItem is not WindowEntry entry) return;

        SelectedWindow = new ManagedWindow
        {
            Handle = entry.Handle,
            Title  = entry.Title,
            Icon   = entry.Icon,
        };
        DialogResult = true;
        Close();
    }

    // ---- アイコン取得 (SendMessageTimeout でハングを防止) ----
    private static BitmapSource? GetWindowIcon(IntPtr hWnd)
    {
        // SendMessageTimeout で WM_GETICON を送る (50ms でタイムアウト)
        IntPtr hIcon = IntPtr.Zero;
        NativeMethods.SendMessageTimeout(hWnd, NativeMethods.WM_GETICON,
            new IntPtr(NativeMethods.ICON_SMALL2), IntPtr.Zero,
            NativeMethods.SMTO_ABORTIFHUNG, 50, out hIcon);

        if (hIcon == IntPtr.Zero)
            NativeMethods.SendMessageTimeout(hWnd, NativeMethods.WM_GETICON,
                new IntPtr(NativeMethods.ICON_SMALL), IntPtr.Zero,
                NativeMethods.SMTO_ABORTIFHUNG, 50, out hIcon);

        if (hIcon != IntPtr.Zero)
        {
            try
            {
                return Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
            }
            catch { }
        }

        // プロセスの実行ファイルからアイコンを取得
        try
        {
            NativeMethods.GetWindowThreadProcessId(hWnd, out var pid);
            using var proc = Process.GetProcessById((int)pid);
            var path = proc.MainModule?.FileName;
            if (path != null)
            {
                using var ico = System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (ico != null)
                    return Imaging.CreateBitmapSourceFromHIcon(ico.Handle,
                        Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            }
        }
        catch { }

        return null;
    }

    private record WindowEntry(IntPtr Handle, string Title, BitmapSource? Icon);
}
