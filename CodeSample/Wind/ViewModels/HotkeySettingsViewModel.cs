using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wind.Models;
using Wind.Services;

namespace Wind.ViewModels;

public partial class HotkeyBindingItem : ObservableObject
{
    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _displayString = "";

    [ObservableProperty]
    private bool _isRecording;

    public HotkeyAction Action { get; set; }
    public ModifierKeys Modifiers { get; set; }
    public Key Key { get; set; }
}

public partial class HotkeySettingsViewModel : ObservableObject
{
    private readonly HotkeyManager _hotkeyManager;

    [ObservableProperty]
    private ObservableCollection<HotkeyBindingItem> _hotkeyBindings = new();

    [ObservableProperty]
    private HotkeyBindingItem? _recordingHotkey;

    public HotkeySettingsViewModel(HotkeyManager hotkeyManager)
    {
        _hotkeyManager = hotkeyManager;
        LoadHotkeyBindings();
    }

    private void LoadHotkeyBindings()
    {
        HotkeyBindings.Clear();

        // すべてのデフォルトバインディングを表示（登録成功/失敗に関係なく）
        foreach (var (name, modifiers, key, action) in HotkeyManager.GetDefaultBindings())
        {
            // 現在登録されているバインディングがあればその値を使用
            var registered = _hotkeyManager.Hotkeys.FirstOrDefault(h => h.Action == action);
            var currentModifiers = registered?.Modifiers ?? modifiers;
            var currentKey = registered?.Key ?? key;

            HotkeyBindings.Add(new HotkeyBindingItem
            {
                Name = name,
                DisplayString = FormatHotkeyDisplay(currentModifiers, currentKey),
                Action = action,
                Modifiers = currentModifiers,
                Key = currentKey
            });
        }
    }

    [RelayCommand]
    private void StartRecording(HotkeyBindingItem item)
    {
        if (RecordingHotkey is not null)
        {
            RecordingHotkey.IsRecording = false;
        }

        item.IsRecording = true;
        RecordingHotkey = item;
    }

    [RelayCommand]
    private void CancelRecording()
    {
        if (RecordingHotkey is not null)
        {
            RecordingHotkey.IsRecording = false;
            RecordingHotkey = null;
        }
    }

    public bool ApplyRecordedKey(ModifierKeys modifiers, Key key)
    {
        if (RecordingHotkey is null) return false;

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System)
        {
            return false;
        }

        if (key == Key.Escape)
        {
            CancelRecording();
            return true;
        }

        var duplicate = HotkeyBindings.FirstOrDefault(h =>
            h != RecordingHotkey && h.Modifiers == modifiers && h.Key == key);

        if (duplicate is not null)
        {
            return false;
        }

        var item = RecordingHotkey;
        var success = _hotkeyManager.UpdateHotkey(item.Action, modifiers, key);

        if (success)
        {
            item.Modifiers = modifiers;
            item.Key = key;
            item.DisplayString = FormatHotkeyDisplay(modifiers, key);
        }

        item.IsRecording = false;
        RecordingHotkey = null;

        return success;
    }

    [RelayCommand]
    private void ResetHotkeys()
    {
        _hotkeyManager.ResetToDefaults();
        LoadHotkeyBindings();
    }

    private static string FormatHotkeyDisplay(ModifierKeys modifiers, Key key)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key.ToString());
        return string.Join(" + ", parts);
    }
}
