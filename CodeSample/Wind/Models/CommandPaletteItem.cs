using Wpf.Ui.Controls;

namespace Wind.Models;

public class CommandPaletteItem
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string? Description { get; set; }
    public object? Tag { get; set; }
    public SymbolRegular Icon { get; set; } = SymbolRegular.Empty;
}
