namespace WindowzTabManager.Models;

public enum PinnedSide { Left, Right }

/// <summary>
/// コンテンツエリアの左/右半分に1タブを固定し、残り半分で通常のタブ切替を行うレイアウト。
/// </summary>
public class PinnedHalfLayout
{
    private const double MinimumSplitRatio = 0.1;
    private const double MaximumSplitRatio = 0.9;

    public TabItem PinnedTab { get; }
    public PinnedSide Side { get; }

    public double SplitRatio { get; private set; } = 0.5;

    public PinnedHalfLayout(TabItem pinnedTab, PinnedSide side)
    {
        PinnedTab = pinnedTab;
        Side = side;
    }

    public void SetSplitRatio(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return;
        SplitRatio = Math.Clamp(value, MinimumSplitRatio, MaximumSplitRatio);
    }

    /// <summary>
    /// ピン留めタブと指定アクティブタブの occupancy を (Left, Top, Width, Height) 比率で返す。
    /// [0] = ピン留め側, [1] = アクティブタブ側
    /// </summary>
    public (double Left, double Top, double Width, double Height)[] GetLayoutFractions()
    {
        double split = SplitRatio;
        return Side == PinnedSide.Left
            ? [(0.0, 0.0, split, 1.0), (split, 0.0, 1.0 - split, 1.0)]
            : [(split, 0.0, 1.0 - split, 1.0), (0.0, 0.0, split, 1.0)];
    }
}
