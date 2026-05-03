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
    /// 指定タブだけをタイルグループから外す。残り 1 タブになる場合はタイルを解散する。
    /// </summary>
    public bool DetachTabFromTile(TabItem tab)
    {
        if (tab.TileLayout == null)
            return false;

        DetachTabFromTileInternal(tab);
        return true;
    }

    /// <summary>
    /// タブ削除時にタイルから除去する。残り 1 タブになる場合はタイルを解散する。
    /// </summary>
    public void CleanupTileForRemovedTab(TabItem tab)
    {
        DetachTabFromTile(tab);
    }

    private void DetachTabFromTileInternal(TabItem tab)
    {
        var tile = tab.TileLayout;
        if (tile == null) return;

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

    /// <summary>
    /// tabToAdd を anchor タブのタイルに追加する。
    /// anchor がタイル未参加の場合は [anchor, tabToAdd] で新規タイルを作成する。
    /// </summary>
    public TileLayout? AddTabToTile(TabItem tabToAdd, TabItem anchor)
    {
        if (tabToAdd == anchor) return null;

        if (anchor.TileLayout != null)
        {
            var existingTile = anchor.TileLayout;
            if (existingTile.Tabs.Count >= 4) return null;
            if (existingTile.Tabs.Contains(tabToAdd)) return null;

            // 追加タブが別タイルに属していれば先に解除
            if (tabToAdd.TileLayout != null)
                ReleaseTileInternal(tabToAdd.TileLayout);

            existingTile.Tabs.Add(tabToAdd);
            tabToAdd.TileLayout = existingTile;
            ReorderTabsForTile(existingTile);
            ActiveTab = anchor;
            return existingTile;
        }

        // anchor がタイル未参加 → 新規タイルを作成
        return TileSpecificTabs([anchor, tabToAdd]);
    }

    private void ReleaseTileInternal(TileLayout tile)
    {
        foreach (var tab in tile.Tabs.ToList())
            tab.TileLayout = null;
        TileLayouts.Remove(tile);
    }

    // ─── Pinned Half ──────────────────────────────────────────────

    public PinnedHalfLayout? PinnedHalf { get; private set; }

    /// <summary>
    /// 指定タブをコンテンツエリアの左/右半分に固定表示する。
    /// 既存のタイルやピン留めは先に解除される。
    /// </summary>
    public PinnedHalfLayout? PinTab(TabItem tab, PinnedSide side)
    {
        if (tab.TileLayout != null)
            ReleaseTileInternal(tab.TileLayout);

        ClearPinnedHalfInternal();

        var layout = new PinnedHalfLayout(tab, side);
        tab.PinnedHalfLayout = layout;
        PinnedHalf = layout;

        // ピン留めタブを先頭へ移動
        int idx = Tabs.IndexOf(tab);
        if (idx > 0)
            Tabs.Move(idx, 0);

        return layout;
    }

    /// <summary>現在のピン留めを解除する。</summary>
    public void UnpinHalf()
    {
        ClearPinnedHalfInternal();
    }

    /// <summary>指定タブが削除されたときにピン留めをクリアする。</summary>
    public void CleanupPinnedHalfForRemovedTab(TabItem tab)
    {
        if (PinnedHalf?.PinnedTab == tab)
            ClearPinnedHalfInternal();
    }

    private void ClearPinnedHalfInternal()
    {
        if (PinnedHalf == null)
            return;

        PinnedHalf.PinnedTab.PinnedHalfLayout = null;
        PinnedHalf = null;
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
