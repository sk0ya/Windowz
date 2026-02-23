using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace Wind.Models;

public partial class TileLayout : ObservableObject
{
    [ObservableProperty]
    private bool _isActive;

    public ObservableCollection<TabItem> TiledTabs { get; } = new();

    public TileLayout(IEnumerable<TabItem> tabs)
    {
        foreach (var tab in tabs)
        {
            tab.IsTiled = true;
            TiledTabs.Add(tab);
        }
        IsActive = true;
    }

    public bool RemoveTab(TabItem tab)
    {
        tab.IsTiled = false;
        TiledTabs.Remove(tab);
        return TiledTabs.Count >= 2;
    }

    public void Deactivate()
    {
        foreach (var tab in TiledTabs)
        {
            tab.IsTiled = false;
        }
        TiledTabs.Clear();
        IsActive = false;
    }
}
