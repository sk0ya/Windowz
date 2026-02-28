namespace WindowzTabManager.Models;

/// <summary>
/// タイル表示グループ。2〜4 個の埋め込みウィンドウタブを同時にコンテンツエリアへ配置する。
/// </summary>
public class TileLayout
{
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>タイルに含まれるタブ（表示順）</summary>
    public List<TabItem> Tabs { get; } = new();

    /// <summary>
    /// 各タブの占有領域を (左比率, 上比率, 幅比率, 高さ比率) で返す。
    /// 2枚: 左右均等
    /// 3枚: 左半分 + 右上 + 右下
    /// 4枚: 2×2 グリッド
    /// </summary>
    public (double Left, double Top, double Width, double Height)[] GetLayoutFractions()
    {
        return Tabs.Count switch
        {
            2 =>
            [
                (0.0, 0.0, 0.5, 1.0),
                (0.5, 0.0, 0.5, 1.0),
            ],
            3 =>
            [
                (0.0, 0.0, 0.5, 1.0),
                (0.5, 0.0, 0.5, 0.5),
                (0.5, 0.5, 0.5, 0.5),
            ],
            4 =>
            [
                (0.0, 0.0, 0.5, 0.5),
                (0.5, 0.0, 0.5, 0.5),
                (0.0, 0.5, 0.5, 0.5),
                (0.5, 0.5, 0.5, 0.5),
            ],
            _ => [],
        };
    }
}
