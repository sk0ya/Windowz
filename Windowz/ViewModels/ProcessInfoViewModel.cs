using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WindowzTabManager.ViewModels;

public class ProcessInfoItem
{
    public string DisplayTitle { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public int ProcessId { get; set; }
    public string ExecutablePath { get; set; } = string.Empty;
    public string MemoryUsage { get; set; } = string.Empty;
    public long MemoryBytes { get; set; }
    public string StartTime { get; set; } = string.Empty;
    public DateTime? StartTimeValue { get; set; }
}

public partial class ProcessInfoViewModel : ObservableObject
{
    private readonly Func<IEnumerable<ManagedWindow>> _getManagedWindows;
    private readonly ObservableCollection<ProcessInfoItem> _processes = new();
    private readonly ICollectionView _processesView;
    private string _currentSortProperty = string.Empty;
    private ListSortDirection _currentSortDirection = ListSortDirection.Ascending;

    public ICollectionView ProcessesView => _processesView;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _hasProcesses;

    public ProcessInfoViewModel(Func<IEnumerable<ManagedWindow>> getManagedWindows)
    {
        _getManagedWindows = getManagedWindows;
        _processesView = CollectionViewSource.GetDefaultView(_processes);
        _processesView.Filter = FilterProcesses;
    }

    partial void OnSearchTextChanged(string value)
    {
        _processesView.Refresh();
    }

    private bool FilterProcesses(object obj)
    {
        if (obj is not ProcessInfoItem item)
            return false;

        if (string.IsNullOrWhiteSpace(SearchText))
            return true;

        return item.DisplayTitle.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
               || item.ProcessName.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
               || item.ExecutablePath.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
               || item.ProcessId.ToString().Contains(SearchText, StringComparison.OrdinalIgnoreCase);
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

        foreach (ManagedWindow window in _getManagedWindows())
        {
            if (!window.IsAlive)
                continue;

            if (window.ProcessId == 0)
                window.RefreshProcessInfo();

            var item = new ProcessInfoItem
            {
                DisplayTitle = string.IsNullOrWhiteSpace(window.Title) ? "(untitled)" : window.Title,
                ProcessName = string.IsNullOrWhiteSpace(window.ProcessName) ? "(unknown)" : window.ProcessName,
                ProcessId = window.ProcessId,
                ExecutablePath = window.ExecutablePath ?? "(access denied)"
            };

            if (window.ProcessId > 0)
            {
                try
                {
                    using Process process = Process.GetProcessById(window.ProcessId);
                    item.MemoryBytes = process.WorkingSet64;
                    item.MemoryUsage = $"{process.WorkingSet64 / 1024 / 1024} MB";
                    try
                    {
                        item.StartTimeValue = process.StartTime;
                        item.StartTime = process.StartTime.ToString("yyyy-MM-dd HH:mm:ss");
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
            }
            else
            {
                item.MemoryUsage = "N/A";
                item.StartTime = "N/A";
            }

            _processes.Add(item);
        }

        HasProcesses = _processes.Count > 0;

        if (!string.IsNullOrEmpty(_currentSortProperty))
        {
            _processesView.SortDescriptions.Clear();
            _processesView.SortDescriptions.Add(new SortDescription(_currentSortProperty, _currentSortDirection));
        }
    }
}
