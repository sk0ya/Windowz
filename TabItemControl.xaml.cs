using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WindowzTabManager;

public partial class TabItemControl : UserControl
{
    public event EventHandler? TabClicked;
    public event EventHandler? CloseRequested;

    private bool _isActive;

    private static readonly SolidColorBrush ActiveBg  = new(Color.FromRgb(0x2D, 0x2D, 0x2D));
    private static readonly SolidColorBrush HoverBg   = new(Color.FromRgb(0x25, 0x25, 0x26));
    private static readonly SolidColorBrush NormalBg  = new(Color.FromRgb(0x1E, 0x1E, 0x1E));
    private static readonly SolidColorBrush ActiveText = new(Colors.White);
    private static readonly SolidColorBrush NormalText = new(Color.FromRgb(0xCC, 0xCC, 0xCC));

    internal ManagedWindow? ManagedWindow { get; private set; }

    public TabItemControl()
    {
        InitializeComponent();
    }

    internal void SetWindow(ManagedWindow window)
    {
        ManagedWindow = window;
        TitleText.Text = window.Title;
        if (window.Icon != null)
            AppIcon.Source = window.Icon;
        else
            AppIcon.Visibility = Visibility.Hidden;
    }

    public void UpdateTitle(string title)
    {
        TitleText.Text = title;
    }

    public bool IsActive
    {
        get => _isActive;
        set
        {
            _isActive = value;
            ApplyState(false);
        }
    }

    private void ApplyState(bool hover)
    {
        if (_isActive)
        {
            RootGrid.Background = ActiveBg;
            ActiveBorder.Visibility = Visibility.Visible;
            TitleText.Foreground = ActiveText;
            TitleText.FontWeight = FontWeights.SemiBold;
            CloseButton.Visibility = Visibility.Visible;
        }
        else if (hover)
        {
            RootGrid.Background = HoverBg;
            ActiveBorder.Visibility = Visibility.Collapsed;
            TitleText.Foreground = NormalText;
            TitleText.FontWeight = FontWeights.Normal;
            CloseButton.Visibility = Visibility.Visible;
        }
        else
        {
            RootGrid.Background = NormalBg;
            ActiveBorder.Visibility = Visibility.Collapsed;
            TitleText.Foreground = NormalText;
            TitleText.FontWeight = FontWeights.Normal;
            CloseButton.Visibility = Visibility.Hidden;
        }
    }

    private void OnMouseEnter(object sender, MouseEventArgs e) => ApplyState(true);
    private void OnMouseLeave(object sender, MouseEventArgs e) => ApplyState(false);

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // × ボタン以外のクリックでタブ切り替え
        if (!IsMouseOverCloseButton(e.GetPosition(CloseButton)))
            TabClicked?.Invoke(this, EventArgs.Empty);
    }

    private static bool IsMouseOverCloseButton(Point p) =>
        p.X >= 0 && p.Y >= 0 && p.X <= 18 && p.Y <= 18;

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
