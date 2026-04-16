namespace WindowzTabManager.Models;

/// <summary>
/// タイル表示グループ。2〜4 個の埋め込みウィンドウタブを同時にコンテンツエリアへ配置する。
/// </summary>
public class TileLayout
{
    private const double MinimumSplitRatio = 0.1;
    private const double MaximumSplitRatio = 0.9;

    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>タイルに含まれるタブ（表示順）</summary>
    public List<TabItem> Tabs { get; } = new();

    public double VerticalSplit { get; private set; } = 0.5;

    public double HorizontalSplit { get; private set; } = 0.5;

    public void SetVerticalSplit(double value)
    {
        VerticalSplit = ClampSplit(value);
    }

    public void SetHorizontalSplit(double value)
    {
        HorizontalSplit = ClampSplit(value);
    }

    /// <summary>
    /// 各タブの占有領域を (左比率, 上比率, 幅比率, 高さ比率) で返す。
    /// 2枚: 左右均等
    /// 3枚: 左半分 + 右上 + 右下
    /// 4枚: 2×2 グリッド
    /// </summary>
    public (double Left, double Top, double Width, double Height)[] GetLayoutFractions()
    {
        double vertical = VerticalSplit;
        double horizontal = HorizontalSplit;

        return Tabs.Count switch
        {
            2 =>
            [
                (0.0, 0.0, vertical, 1.0),
                (vertical, 0.0, 1.0 - vertical, 1.0),
            ],
            3 =>
            [
                (0.0, 0.0, vertical, 1.0),
                (vertical, 0.0, 1.0 - vertical, horizontal),
                (vertical, horizontal, 1.0 - vertical, 1.0 - horizontal),
            ],
            4 =>
            [
                (0.0, 0.0, vertical, horizontal),
                (vertical, 0.0, 1.0 - vertical, horizontal),
                (0.0, horizontal, vertical, 1.0 - horizontal),
                (vertical, horizontal, 1.0 - vertical, 1.0 - horizontal),
            ],
            _ => [],
        };
    }

    private static double ClampSplit(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return 0.5;

        return Math.Clamp(value, MinimumSplitRatio, MaximumSplitRatio);
    }
}
