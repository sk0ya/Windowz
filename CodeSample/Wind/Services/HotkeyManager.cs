using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Wind.Interop;
using Wind.Models;

namespace Wind.Services;

public class HotkeyManager : IDisposable
{
    private IntPtr _windowHandle;
    private HwndSource? _hwndSource;
    private int _nextHotkeyId = 1;
    private readonly Dictionary<int, HotkeyBinding> _registeredHotkeys = new();
    private bool _disposed;
    private SettingsManager? _settingsManager;

    public ObservableCollection<HotkeyBinding> Hotkeys { get; } = new();

    public event EventHandler<HotkeyBinding>? HotkeyPressed;

    public void Initialize(Window window, SettingsManager settingsManager)
    {
        _settingsManager = settingsManager;

        var helper = new WindowInteropHelper(window);
        _windowHandle = helper.Handle;

        _hwndSource = HwndSource.FromHwnd(_windowHandle);
        _hwndSource?.AddHook(WndProc);

        var customBindings = settingsManager.Settings.CustomHotkeys;
        if (customBindings.Count > 0)
        {
            RegisterFromSettings(customBindings);
        }
        else
        {
            RegisterDefaultHotkeys();
        }
    }

    public static List<(string Name, System.Windows.Input.ModifierKeys Modifiers, Key Key, HotkeyAction Action)> GetDefaultBindings()
    {
        var defaults = new List<(string, System.Windows.Input.ModifierKeys, Key, HotkeyAction)>
        {
            ("Next Tab", System.Windows.Input.ModifierKeys.Control, Key.Tab, HotkeyAction.NextTab),
            ("Previous Tab", System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift, Key.Tab, HotkeyAction.PreviousTab),
            ("Close Tab", System.Windows.Input.ModifierKeys.Control, Key.W, HotkeyAction.CloseTab),
            ("Command Palette", System.Windows.Input.ModifierKeys.Alt, Key.P, HotkeyAction.CommandPalette),
        };

        for (int i = 1; i <= 9; i++)
        {
            var action = (HotkeyAction)(HotkeyAction.SwitchToTab1 + i - 1);
            var key = (Key)(Key.D1 + i - 1);
            defaults.Add(($"Switch to Tab {i}", System.Windows.Input.ModifierKeys.Control, key, action));
        }

        return defaults;
    }

    private void RegisterDefaultHotkeys()
    {
        foreach (var (name, modifiers, key, action) in GetDefaultBindings())
        {
            RegisterHotkey(name, modifiers, key, action);
        }
    }

    private void RegisterFromSettings(List<HotkeyBindingSetting> settings)
    {
        var defaults = GetDefaultBindings();

        foreach (var setting in settings)
        {
            if (!Enum.TryParse<HotkeyAction>(setting.Action, out var action)) continue;
            if (!Enum.TryParse<System.Windows.Input.ModifierKeys>(setting.Modifiers, out var modifiers)) continue;
            if (!Enum.TryParse<Key>(setting.Key, out var key)) continue;

            // デフォルトから名前を取得
            var defaultBinding = defaults.FirstOrDefault(d => d.Action == action);
            var name = defaultBinding.Name ?? action.ToString();

            RegisterHotkey(name, modifiers, key, action);
        }

        // 設定に含まれないアクションはデフォルトで登録
        var registeredActions = settings
            .Where(s => Enum.TryParse<HotkeyAction>(s.Action, out _))
            .Select(s => Enum.Parse<HotkeyAction>(s.Action))
            .ToHashSet();

        foreach (var (name, modifiers, key, action) in defaults)
        {
            if (!registeredActions.Contains(action))
            {
                RegisterHotkey(name, modifiers, key, action);
            }
        }
    }

    public bool UpdateHotkey(HotkeyAction action, System.Windows.Input.ModifierKeys newModifiers, Key newKey)
    {
        // 既存の登録を解除
        var existing = _registeredHotkeys.Values.FirstOrDefault(h => h.Action == action);
        if (existing is not null)
        {
            UnregisterHotkey(existing);
        }

        var name = existing?.Name ?? action.ToString();
        var result = RegisterHotkey(name, newModifiers, newKey, action);

        if (result)
        {
            SaveCurrentBindings();
        }

        return result;
    }

    public void ResetToDefaults()
    {
        UnregisterAllHotkeys();
        RegisterDefaultHotkeys();

        if (_settingsManager is not null)
        {
            _settingsManager.Settings.CustomHotkeys.Clear();
            _settingsManager.SaveSettings();
        }
    }

    private void SaveCurrentBindings()
    {
        if (_settingsManager is null) return;

        var settings = _registeredHotkeys.Values.Select(h => new HotkeyBindingSetting
        {
            Action = h.Action.ToString(),
            Modifiers = h.Modifiers.ToString(),
            Key = h.Key.ToString()
        }).ToList();

        _settingsManager.Settings.CustomHotkeys = settings;
        _settingsManager.SaveSettings();
    }

    public bool RegisterHotkey(string name, System.Windows.Input.ModifierKeys modifiers, Key key, HotkeyAction action, string? parameter = null)
    {
        var binding = new HotkeyBinding
        {
            Id = _nextHotkeyId++,
            Name = name,
            Modifiers = modifiers,
            Key = key,
            Action = action,
            Parameter = parameter
        };

        uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        uint mods = ConvertModifiers(modifiers);

        if (NativeMethods.RegisterHotKey(_windowHandle, binding.Id, mods, vk))
        {
            _registeredHotkeys[binding.Id] = binding;
            Hotkeys.Add(binding);
            return true;
        }

        return false;
    }

    private uint ConvertModifiers(System.Windows.Input.ModifierKeys modifiers)
    {
        uint result = 0;
        if (modifiers.HasFlag(System.Windows.Input.ModifierKeys.Alt)) result |= NativeMethods.MOD_ALT;
        if (modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control)) result |= NativeMethods.MOD_CONTROL;
        if (modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift)) result |= NativeMethods.MOD_SHIFT;
        if (modifiers.HasFlag(System.Windows.Input.ModifierKeys.Windows)) result |= NativeMethods.MOD_WIN;
        return result;
    }

    public void UnregisterHotkey(HotkeyBinding binding)
    {
        if (_registeredHotkeys.ContainsKey(binding.Id))
        {
            NativeMethods.UnregisterHotKey(_windowHandle, binding.Id);
            _registeredHotkeys.Remove(binding.Id);
            Hotkeys.Remove(binding);
        }
    }

    public void UnregisterAllHotkeys()
    {
        foreach (var binding in _registeredHotkeys.Values.ToList())
        {
            NativeMethods.UnregisterHotKey(_windowHandle, binding.Id);
        }
        _registeredHotkeys.Clear();
        Hotkeys.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (_registeredHotkeys.TryGetValue(id, out var binding))
            {
                HotkeyPressed?.Invoke(this, binding);
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;

        UnregisterAllHotkeys();
        _hwndSource?.RemoveHook(WndProc);
        _hwndSource?.Dispose();

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
