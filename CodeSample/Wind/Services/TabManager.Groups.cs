using System.Windows.Media;
using Wind.Models;

namespace Wind.Services;

public partial class TabManager
{
    public TabGroup CreateGroup(string name, Color color)
    {
        var group = new TabGroup(name, color);
        Groups.Add(group);
        return group;
    }

    public void AddTabToGroup(TabItem tab, TabGroup group)
    {
        // Remove from existing group if any
        tab.Group?.RemoveTab(tab);

        group.AddTab(tab);
    }

    public void RemoveTabFromGroup(TabItem tab)
    {
        tab.Group?.RemoveTab(tab);
    }

    public void DeleteGroup(TabGroup group)
    {
        // Move all tabs out of the group
        foreach (var tab in group.Tabs.ToList())
        {
            tab.Group = null;
        }
        group.Tabs.Clear();
        Groups.Remove(group);
    }

    public void ToggleMultiSelect(TabItem tab)
    {
        tab.IsMultiSelected = !tab.IsMultiSelected;
    }

    public void ClearMultiSelection()
    {
        foreach (var tab in Tabs)
        {
            tab.IsMultiSelected = false;
        }
    }

    public IReadOnlyList<TabItem> GetMultiSelectedTabs()
    {
        return Tabs.Where(t => t.IsMultiSelected).ToList();
    }
}
