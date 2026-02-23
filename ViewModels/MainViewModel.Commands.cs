using CommunityToolkit.Mvvm.Input;
using System.Windows.Media;
using WindowzTabManager.Models;

namespace WindowzTabManager.ViewModels;

public partial class MainViewModel
{
    [RelayCommand]
    private void OpenCommandPalette()
    {
        IsCommandPaletteOpen = true;
    }

    [RelayCommand]
    private void CloseCommandPalette()
    {
        IsCommandPaletteOpen = false;
    }

    [RelayCommand]
    private void OpenSettings()
    {
        OpenContentTab("GeneralSettings");
    }

    [RelayCommand]
    private void OpenContentTab(string contentKey)
    {
        var title = contentKey switch
        {
            "GeneralSettings" => "設定",
            "HotkeySettings" => "HotKey設定",
            "StartupSettings" => "Startup設定",
            "QuickLaunchSettings" => "QuickLaunch設定",
            "ProcessInfo" => "プロセス情報",
            _ => contentKey
        };
        _tabManager.AddContentTab(title, contentKey);
    }

    [RelayCommand]
    private void OpenWebTab(string? url)
    {
        var targetUrl = string.IsNullOrWhiteSpace(url) ? "https://www.google.com" : url;
        _tabManager.AddWebTab(targetUrl);
    }

    [RelayCommand]
    private void SelectTab(TabItem tab)
    {
        _tabManager.ActiveTab = tab;
    }

    [RelayCommand]
    private void CloseTab(TabItem tab)
    {
        _tabManager.CloseTab(tab);
        StatusMessage = $"Closed: {tab.Title}";
    }

    [RelayCommand]
    private void OpenWindowPicker()
    {
        _windowPickerViewModel.Start();
        IsWindowPickerOpen = true;
    }

    [RelayCommand]
    private void CloseWindowPicker()
    {
        _windowPickerViewModel.Stop();
        IsWindowPickerOpen = false;
    }

    [RelayCommand]
    private void AddWindow(WindowInfo? windowInfo)
    {
        if (windowInfo == null) return;

        var tab = _tabManager.AddTab(windowInfo);
        if (tab != null)
        {
            StatusMessage = $"Added: {tab.Title}";
            _windowPickerViewModel.Stop();
            IsWindowPickerOpen = false;
        }
        else
        {
            StatusMessage = _windowManager.LastEmbedFailureMessage ?? "Failed to add window";
        }
    }

    [RelayCommand]
    private void RefreshWindows()
    {
        _windowManager.RefreshWindowList();
    }

    [RelayCommand]
    private void CreateGroup()
    {
        var colors = new[] { Colors.CornflowerBlue, Colors.Coral, Colors.MediumSeaGreen, Colors.MediumPurple, Colors.Goldenrod };
        var color = colors[Groups.Count % colors.Length];
        var group = _tabManager.CreateGroup($"Group {Groups.Count + 1}", color);
        StatusMessage = $"Created: {group.Name}";
    }

    [RelayCommand]
    private void DeleteGroup(TabGroup group)
    {
        _tabManager.DeleteGroup(group);
        StatusMessage = $"Deleted: {group.Name}";
    }

    [RelayCommand]
    private void ToggleMultiSelect(TabItem tab)
    {
        _tabManager.ToggleMultiSelect(tab);
    }

    [RelayCommand]
    private void TileSelectedTabs()
    {
        var selectedTabs = _tabManager.GetMultiSelectedTabs();
        if (selectedTabs.Count < 2)
        {
            StatusMessage = "Select 2 or more tabs to tile";
            return;
        }

        if (selectedTabs.Any(t => !t.IsContentTab && !t.IsWebTab && !_tabManager.CanTileTab(t)))
        {
            StatusMessage = "外部管理タブを含むためタイル表示できません";
            return;
        }

        _tabManager.StartTile(selectedTabs);
        StatusMessage = $"Tiled {selectedTabs.Count} tabs";
    }

    [RelayCommand]
    private void StopTile()
    {
        _tabManager.StopTile();
        IsTileVisible = false;
        // Restore single tab view
        if (SelectedTab != null)
        {
            CurrentWindowHost = _tabManager.GetWindowHost(SelectedTab);
        }
        StatusMessage = "Tile layout stopped";
    }

    [RelayCommand]
    private void ReleaseAllWindows()
    {
        _tabManager.ReleaseAllTabs();
        StatusMessage = "All windows released";
    }
}
