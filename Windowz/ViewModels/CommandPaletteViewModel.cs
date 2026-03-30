using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using WindowzTabManager.Models;
using WindowzTabManager.Services;
using Wpf.Ui.Controls;
using static WindowzTabManager.SettingsManager;

namespace WindowzTabManager.ViewModels;

public partial class FilterTab : ObservableObject
{
    [ObservableProperty]
    private bool _isActive;

    public required string Label { get; init; }
    public required string Key { get; init; }
}

public partial class CommandPaletteViewModel : ObservableObject
{
    private readonly TabManager _tabManager;
    private readonly SettingsManager _settingsManager;
    private readonly ICollectionView _itemsView;

    public ObservableCollection<CommandPaletteItem> Items { get; } = new();
    public ICollectionView ItemsView => _itemsView;

    public IReadOnlyList<FilterTab> Filters { get; } = new List<FilterTab>
    {
        new FilterTab { Label = "すべて",        Key = "All",               IsActive = true },
        new FilterTab { Label = "操作",          Key = "Action" },
        new FilterTab { Label = "Tab",           Key = "Tab" },
        new FilterTab { Label = "QuickLaunch",   Key = "QuickLaunch" },
        new FilterTab { Label = "AppLaunch",     Key = "AppLaunch" },
    };

    private string _activeFilter = "All";

    public string ActiveFilter
    {
        get => _activeFilter;
        private set
        {
            if (_activeFilter == value) return;
            _activeFilter = value;
            foreach (var f in Filters)
                f.IsActive = f.Key == value;
            _itemsView.Refresh();
            _itemsView.MoveCurrentToFirst();
            SelectedItem = _itemsView.CurrentItem as CommandPaletteItem;
        }
    }

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private CommandPaletteItem? _selectedItem;

    public event EventHandler<CommandPaletteItem>? ItemExecuted;
    public event EventHandler? Cancelled;

    public CommandPaletteViewModel(TabManager tabManager, SettingsManager settingsManager)
    {
        _tabManager = tabManager;
        _settingsManager = settingsManager;
        _itemsView = CollectionViewSource.GetDefaultView(Items);
        _itemsView.Filter = FilterItems;
    }

    public void Open()
    {
        SearchText = string.Empty;
        _activeFilter = "All";
        foreach (var f in Filters)
            f.IsActive = f.Key == "All";

        RebuildItems();
        _itemsView.MoveCurrentToFirst();
        SelectedItem = _itemsView.CurrentItem as CommandPaletteItem;
    }

    [RelayCommand]
    private void SetFilter(string? key)
    {
        if (key != null)
            ActiveFilter = key;
    }

    public void CycleFilterForward()
    {
        int idx = ActiveFilterIndex();
        ActiveFilter = Filters[(idx + 1) % Filters.Count].Key;
    }

    public void CycleFilterBackward()
    {
        int idx = ActiveFilterIndex();
        ActiveFilter = Filters[(idx - 1 + Filters.Count) % Filters.Count].Key;
    }

    private int ActiveFilterIndex()
    {
        for (int i = 0; i < Filters.Count; i++)
            if (Filters[i].Key == _activeFilter) return i;
        return 0;
    }

    private void RebuildItems()
    {
        Items.Clear();

        // QuickLaunch apps and tile groups (in settings order)
        var tiledPathToGroup = new Dictionary<string, QuickLaunchTileGroupSetting>(StringComparer.OrdinalIgnoreCase);
        foreach (var tg in _settingsManager.Settings.QuickLaunchTileGroups)
            foreach (var p in tg.AppPaths)
                tiledPathToGroup.TryAdd(p, tg);

        var emittedTileGroupIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var app in _settingsManager.Settings.QuickLaunchApps)
        {
            if (tiledPathToGroup.TryGetValue(app.Path, out var tileGroup))
            {
                if (emittedTileGroupIds.Add(tileGroup.Id))
                {
                    Items.Add(new CommandPaletteItem
                    {
                        Name = tileGroup.DisplayName,
                        Category = "QuickLaunch",
                        Description = $"タイル起動 ({tileGroup.AppPaths.Count}個)",
                        Tag = tileGroup,
                        Icon = SymbolRegular.Grid24
                    });
                }
                continue;
            }

            Items.Add(new CommandPaletteItem
            {
                Name = app.Name,
                Category = "QuickLaunch",
                Description = app.Path,
                Tag = app,
                Icon = IsUrl(app.Path) ? SymbolRegular.Globe24 : SymbolRegular.WindowConsole20
            });
        }

        // Tile groups not reachable via app order (safety net)
        foreach (var tileGroup in _settingsManager.Settings.QuickLaunchTileGroups)
        {
            if (emittedTileGroupIds.Add(tileGroup.Id))
            {
                Items.Add(new CommandPaletteItem
                {
                    Name = tileGroup.DisplayName,
                    Category = "QuickLaunch",
                    Description = $"タイル起動 ({tileGroup.AppPaths.Count}個)",
                    Tag = tileGroup,
                    Icon = SymbolRegular.Grid24
                });
            }
        }

        // ApplicationLaunch items from configured folders
        foreach (var launchItem in _settingsManager.GetApplicationLaunchItems())
        {
            bool isShortcut = Path.GetExtension(launchItem.FilePath)
                .Equals(".lnk", StringComparison.OrdinalIgnoreCase);

            Items.Add(new CommandPaletteItem
            {
                Name = launchItem.Name,
                Category = "ApplicationLaunch",
                Description = launchItem.FilePath,
                Tag = launchItem,
                Icon = isShortcut ? SymbolRegular.Link24 : SymbolRegular.WindowConsole20
            });
        }

        // Open tabs
        foreach (var tab in _tabManager.Tabs)
        {
            Items.Add(new CommandPaletteItem
            {
                Name = tab.DisplayTitle,
                Category = "Tab",
                Description = "Switch to tab",
                Tag = tab,
                Icon = SymbolRegular.Window24
            });
        }

        // Built-in actions
        Items.Add(new CommandPaletteItem
        {
            Name = "New Tab",
            Category = "Action",
            Description = "Open window picker",
            Tag = HotkeyAction.NewTab,
            Icon = SymbolRegular.Add24
        });
        Items.Add(new CommandPaletteItem
        {
            Name = "Close Tab",
            Category = "Action",
            Description = "Close current tab",
            Tag = HotkeyAction.CloseTab,
            Icon = SymbolRegular.Dismiss24
        });
        Items.Add(new CommandPaletteItem
        {
            Name = "Settings",
            Category = "Action",
            Description = "Open settings",
            Tag = "GeneralSettings",
            Icon = SymbolRegular.Settings24
        });

        // Window management actions
        Items.Add(new CommandPaletteItem
        {
            Name = "ウィンドウを閉じる",
            Category = "Window",
            Description = "Close Wind",
            Tag = "WindowClose",
            Icon = SymbolRegular.Dismiss24
        });
        Items.Add(new CommandPaletteItem
        {
            Name = "ウィンドウを最小化",
            Category = "Window",
            Description = "Minimize window",
            Tag = "WindowMinimize",
            Icon = SymbolRegular.Subtract24
        });
        Items.Add(new CommandPaletteItem
        {
            Name = "ウィンドウを最大化 / 元に戻す",
            Category = "Window",
            Description = "Maximize or restore window",
            Tag = "WindowMaximize",
            Icon = SymbolRegular.Maximize24
        });

        // Tab bar collapse/expand (vertical tab bar only)
        if (_settingsManager.Settings.TabHeaderPosition is "Left" or "Right")
        {
            Items.Add(new CommandPaletteItem
            {
                Name = "タブバーの折りたたみ / 解除",
                Category = "Action",
                Description = "Toggle tab bar collapse",
                Tag = "TabBarToggleCollapse",
                Icon = SymbolRegular.PanelLeftContract24
            });
        }

        // Topmost window arrangement
        Items.Add(new CommandPaletteItem
        {
            Name = "Topmostウィンドウを整列",
            Category = "Window",
            Description = "Arrange topmost windows from top-right",
            Tag = "ArrangeTopmostWindows",
            Icon = SymbolRegular.AppsList24
        });

        // Group expand/collapse
        if (_tabManager.Groups.Count > 0)
        {
            Items.Add(new CommandPaletteItem
            {
                Name = "全グループを展開",
                Category = "Group",
                Description = "Expand all tab groups",
                Tag = "GroupExpandAll",
                Icon = SymbolRegular.ChevronDown24
            });
            Items.Add(new CommandPaletteItem
            {
                Name = "全グループを折りたたみ",
                Category = "Group",
                Description = "Collapse all tab groups",
                Tag = "GroupCollapseAll",
                Icon = SymbolRegular.ChevronRight24
            });
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        _itemsView.Refresh();
        _itemsView.MoveCurrentToFirst();
        SelectedItem = _itemsView.CurrentItem as CommandPaletteItem;
    }

    private bool FilterItems(object obj)
    {
        if (obj is not CommandPaletteItem item) return false;

        // カテゴリフィルター
        if (_activeFilter != "All")
        {
            bool categoryMatch = _activeFilter switch
            {
                "Action" => item.Category is "Action" or "Window" or "Group",
                "Tab" => item.Category == "Tab",
                "QuickLaunch" => item.Category == "QuickLaunch",
                "AppLaunch" => item.Category == "ApplicationLaunch",
                _ => true
            };
            if (!categoryMatch) return false;
        }

        // テキストフィルター
        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        return item.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
               (item.Description?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
               item.Category.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand]
    private void Execute(CommandPaletteItem? item)
    {
        var target = item ?? SelectedItem;
        if (target != null)
            ItemExecuted?.Invoke(this, target);
    }

    [RelayCommand]
    private void Cancel()
    {
        Cancelled?.Invoke(this, EventArgs.Empty);
    }

    public void MoveSelectionUp()
    {
        _itemsView.MoveCurrentToPrevious();
        if (_itemsView.IsCurrentBeforeFirst)
            _itemsView.MoveCurrentToFirst();
        SelectedItem = _itemsView.CurrentItem as CommandPaletteItem;
    }

    public void MoveSelectionDown()
    {
        _itemsView.MoveCurrentToNext();
        if (_itemsView.IsCurrentAfterLast)
            _itemsView.MoveCurrentToLast();
        SelectedItem = _itemsView.CurrentItem as CommandPaletteItem;
    }
}
