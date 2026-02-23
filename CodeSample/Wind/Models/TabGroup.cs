using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Windows.Media;

namespace Wind.Models;

public partial class TabGroup : ObservableObject
{
    public Guid Id { get; } = Guid.NewGuid();

    [ObservableProperty]
    private string _name = "New Group";

    [ObservableProperty]
    private Color _color = Colors.CornflowerBlue;

    [ObservableProperty]
    private bool _isExpanded = true;

    public ObservableCollection<TabItem> Tabs { get; } = new();

    public TabGroup()
    {
    }

    public TabGroup(string name, Color color)
    {
        Name = name;
        Color = color;
    }

    public void AddTab(TabItem tab)
    {
        tab.Group = this;
        Tabs.Add(tab);
    }

    public void RemoveTab(TabItem tab)
    {
        tab.Group = null;
        Tabs.Remove(tab);
    }
}
