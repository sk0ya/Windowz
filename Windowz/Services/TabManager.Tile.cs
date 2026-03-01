using System.Collections.ObjectModel;
using WindowzTabManager.Models;

namespace WindowzTabManager.Services;

public partial class TabManager
{
    public ObservableCollection<TileLayout> TileLayouts { get; } = new();

    /// <summary>
    /// 現在の複数選択タブ（2〜4 個）をタイル表示グループとして登録する。
    /// タブはタブバー上で隣接するよう並び替える。
    /// </summary>
    public TileLayout? TileSelectedTabs()
    {
        // ウィンドウタブ・Webタブ・コンテンツタブを対象
        var candidates = GetMultiSelectedTabs()
            .Where(t => t.IsContentTab || t.IsWebTab || t.Window != null)
            .ToList();

        if (candidates.Count < 2 || candidates.Count > 4)
            return null;

        // 既存タイルから解除
        foreach (var tab in candidates)
        {
            if (tab.TileLayout != null)
                ReleaseTileInternal(tab.TileLayout);
        }

        // タイル作成
        var tile = new TileLayout();
        foreach (var tab in candidates)
        {
            tile.Tabs.Add(tab);
            tab.TileLayout = tile;
        }

        // タブを隣接させる
        ReorderTabsForTile(tile);

        TileLayouts.Add(tile);
        ClearMultiSelection();

        // 最初のタブをアクティブに
        ActiveTab = tile.Tabs[0];

        return tile;
    }

    /// <summary>
    /// 指定したタブ群（2〜4個）をタイル表示グループとして登録する。
    /// 多選択状態を前提としないため、スタートアップ時の自動タイル適用に使用する。
    /// </summary>
    public TileLayout? TileSpecificTabs(IEnumerable<TabItem> tabs)
    {
        var candidates = tabs
            .Where(t => t.IsContentTab || t.IsWebTab || t.Window != null)
            .ToList();

        if (candidates.Count < 2 || candidates.Count > 4)
            return null;

        // 既存タイルから解除
        foreach (var tab in candidates)
        {
            if (tab.TileLayout != null)
                ReleaseTileInternal(tab.TileLayout);
        }

        // タイル作成
        var tile = new TileLayout();
        foreach (var tab in candidates)
        {
            tile.Tabs.Add(tab);
            tab.TileLayout = tile;
        }

        ReorderTabsForTile(tile);
        TileLayouts.Add(tile);

        return tile;
    }

    /// <summary>
    /// タイルグループを解除して各タブを独立状態に戻す。
    /// </summary>
    public void ReleaseTile(TileLayout tile)
    {
        ReleaseTileInternal(tile);
    }

    /// <summary>
    /// タブ削除時にタイルから除去する。残り 1 タブになる場合はタイルを解散する。
    /// </summary>
    public void CleanupTileForRemovedTab(TabItem tab)
    {
        if (tab.TileLayout == null) return;

        var tile = tab.TileLayout;
        tab.TileLayout = null;
        tile.Tabs.Remove(tab);

        // 残り 1 タブ以下になったらタイルを解散
        if (tile.Tabs.Count < 2)
        {
            foreach (var remaining in tile.Tabs.ToList())
                remaining.TileLayout = null;
            tile.Tabs.Clear();
            TileLayouts.Remove(tile);
        }
    }

    private void ReleaseTileInternal(TileLayout tile)
    {
        foreach (var tab in tile.Tabs.ToList())
            tab.TileLayout = null;
        TileLayouts.Remove(tile);
    }

    private void ReorderTabsForTile(TileLayout tile)
    {
        // タイルメンバーの最も若いインデックスを基点とする
        int insertIndex = tile.Tabs
            .Select(t => Tabs.IndexOf(t))
            .Where(i => i >= 0)
            .DefaultIfEmpty(Tabs.Count)
            .Min();

        for (int i = 0; i < tile.Tabs.Count; i++)
        {
            var tab = tile.Tabs[i];
            int currentIdx = Tabs.IndexOf(tab);
            int targetIdx = Math.Min(insertIndex + i, Tabs.Count - 1);
            if (currentIdx >= 0 && currentIdx != targetIdx)
                Tabs.Move(currentIdx, targetIdx);
        }
    }
}
