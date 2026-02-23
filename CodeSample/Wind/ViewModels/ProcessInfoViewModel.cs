using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wind.Services;

namespace Wind.ViewModels;

public class ProcessInfoItem
{
    public string DisplayTitle { get; set; } = "";
    public string ProcessName { get; set; } = "";
    public int ProcessId { get; set; }
    public string ExecutablePath { get; set; } = "";
    public string MemoryUsage { get; set; } = "";
    public long MemoryBytes { get; set; }
    public string StartTime { get; set; } = "";
    public DateTime? StartTimeValue { get; set; }
}

public partial class ProcessInfoViewModel : ObservableObject
{
    private readonly TabManager _tabManager;
    private readonly ObservableCollection<ProcessInfoItem> _processes = new();
    private readonly ICollectionView _processesView;
    private string _currentSortProperty = "";
    private ListSortDirection _currentSortDirection = ListSortDirection.Ascending;

    public ICollectionView ProcessesView => _processesView;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _hasProcesses;

    public ProcessInfoViewModel(TabManager tabManager)
    {
        _tabManager = tabManager;
        _processesView = CollectionViewSource.GetDefaultView(_processes);
        _processesView.Filter = FilterProcesses;
    }

    partial void OnSearchTextChanged(string value)
    {
        _processesView.Refresh();
    }

    private bool FilterProcesses(object obj)
    {
        if (obj is not ProcessInfoItem item) return false;
        if (string.IsNullOrWhiteSpace(SearchText)) return true;

        return item.DisplayTitle.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
               item.ProcessName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
               item.ExecutablePath.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
               item.ProcessId.ToString().Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand]
    public void Sort(string propertyName)
    {
        if (_currentSortProperty == propertyName)
        {
            _currentSortDirection = _currentSortDirection == ListSortDirection.Ascending
                ? ListSortDirection.Descending
                : ListSortDirection.Ascending;
        }
        else
        {
            _currentSortProperty = propertyName;
            _currentSortDirection = ListSortDirection.Ascending;
        }

        _processesView.SortDescriptions.Clear();
        _processesView.SortDescriptions.Add(new SortDescription(propertyName, _currentSortDirection));
    }

    public (string Property, ListSortDirection Direction) CurrentSort
        => (_currentSortProperty, _currentSortDirection);

    [RelayCommand]
    public void Refresh()
    {
        _processes.Clear();
        foreach (var tab in _tabManager.Tabs)
        {
            if (tab.IsContentTab || tab.Window == null) continue;

            var item = new ProcessInfoItem
            {
                DisplayTitle = tab.DisplayTitle,
                ProcessName = tab.Window.ProcessName,
                ProcessId = tab.Window.ProcessId,
                ExecutablePath = tab.Window.ExecutablePath ?? "(access denied)",
            };

            try
            {
                using var p = Process.GetProcessById(tab.Window.ProcessId);
                item.MemoryBytes = p.WorkingSet64;
                item.MemoryUsage = $"{p.WorkingSet64 / 1024 / 1024} MB";
                try
                {
                    item.StartTimeValue = p.StartTime;
                    item.StartTime = p.StartTime.ToString("yyyy-MM-dd HH:mm:ss");
                }
                catch
                {
                    item.StartTime = "N/A";
                }
            }
            catch
            {
                item.MemoryUsage = "N/A";
                item.StartTime = "N/A";
            }

            _processes.Add(item);
        }

        HasProcesses = _processes.Count > 0;

        // Re-apply current sort
        if (!string.IsNullOrEmpty(_currentSortProperty))
        {
            _processesView.SortDescriptions.Clear();
            _processesView.SortDescriptions.Add(new SortDescription(_currentSortProperty, _currentSortDirection));
        }
    }
}
