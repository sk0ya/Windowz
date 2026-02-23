using System.Windows.Input;

namespace Wind.Models;

public class HotkeyBinding
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public System.Windows.Input.ModifierKeys Modifiers { get; set; }
    public Key Key { get; set; }
    public HotkeyAction Action { get; set; }
    public string? Parameter { get; set; }

    public string DisplayString
    {
        get
        {
            var parts = new List<string>();
            if (Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control)) parts.Add("Ctrl");
            if (Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Alt)) parts.Add("Alt");
            if (Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift)) parts.Add("Shift");
            if (Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Windows)) parts.Add("Win");
            parts.Add(Key.ToString());
            return string.Join(" + ", parts);
        }
    }
}

public enum HotkeyAction
{
    NextTab,
    PreviousTab,
    CloseTab,
    NewTab,
    SwitchToTab1,
    SwitchToTab2,
    SwitchToTab3,
    SwitchToTab4,
    SwitchToTab5,
    SwitchToTab6,
    SwitchToTab7,
    SwitchToTab8,
    SwitchToTab9,
    ToggleWindow,
    CommandPalette
}
