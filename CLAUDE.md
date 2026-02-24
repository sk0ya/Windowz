# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run

```bash
# Restore packages
dotnet restore Windowz/WindowzTabManager.csproj

# Build
dotnet build Windowz/WindowzTabManager.csproj

# Run (manual testing only — no automated tests exist)
dotnet run --project Windowz/WindowzTabManager.csproj
```

All source lives under `Windowz/`. The `CodeSample/` directory is excluded from the build (see `.csproj`).

## Architecture Overview

Windowz is a WPF (.NET 8) tab manager that embeds arbitrary Windows applications as "tabs" **without** using `SetParent`. Instead it uses `SetWindowPos` to move and resize managed windows so they visually align with the content area of the Windowz container window.

### Dependency Injection

`App.xaml.cs` sets up a `Microsoft.Extensions.DependencyInjection` container. All services and view-models are registered as singletons. Use `App.GetService<T>()` to resolve from outside the DI graph.

Key singletons registered:

| Type | Role |
|---|---|
| `WindowManager` | Enumerate desktop windows; move/show/hide/restore managed windows via Win32 |
| `TabManager` | Owns `ObservableCollection<TabItem>` and `TabGroup` state; routes add/remove/close operations |
| `SettingsManager` | Reads and writes `AppSettings` (JSON on disk); raises change events |
| `HotkeyManager` | Registers/unregisters global hotkeys via `WM_HOTKEY` |
| `ProcessTracker` | Tracks PIDs launched by startup config for cleanup on exit |
| `WebViewEnvironmentService` | Shared WebView2 environment for all web tabs |
| `MainViewModel` | Bridge between `TabManager`/`HotkeyManager` and the UI; owns overlay open/close state |

### Tab Types

`TabItem` has three mutually exclusive modes, determined by its properties:

- **Embedded window tab** — `Window != null`, `ContentKey == null`, `WebUrl == null`. The real Win32 window is moved/shown over the `WindowHostContainer` area.
- **Content tab** — `ContentKey != null` (e.g. `"GeneralSettings"`). A WPF `Page` is displayed inside the container. Settings tab is the primary example.
- **Web tab** — `WebUrl != null`. A `WebTabControl` (WebView2 wrapper) is displayed.

### Window Embedding Strategy

`WindowManager.TryManageWindow` saves the target window's original rect and state. When a tab is activated, `MainWindow.UpdateManagedWindowLayout` calls `WindowManager.ActivateManagedWindow` with pixel-precise coordinates derived from `WindowHostContainer.TranslatePoint` + DPI scale. Inactive managed windows are minimized (not hidden) so the taskbar still shows them.

`MainWindow` hooks `WndProc` via `HwndSource` to:
- Remove the DWM accent border (`WM_NCCALCSIZE`).
- Enable full 8-direction native resize hit-testing (`WM_NCHITTEST`).
- Detect when a managed window moves/resizes itself and sync Windowz's position back.

### MainWindow Partial Classes

`MainWindow` is split across several partial files to keep concerns separate:

| File | Contents |
|---|---|
| `MainWindow.xaml.cs` | Core lifecycle, shutdown logic, `UpdateManagedWindowLayout` |
| `MainWindow.Layout.cs` | `ApplyTabHeaderPosition` — dynamically rewires the Grid for Top/Bottom/Left/Right tab bar positions |
| `MainWindow.Interop.cs` | `WndProc` hook, `WM_NCHITTEST` resize logic |
| `MainWindow.ManagedSync.cs` | `WinEventHook` that detects when a managed window moves and re-syncs Windowz |
| `MainWindow.TabHandlers.cs` | Tab click, close, drag-drop handlers |
| `MainWindow.TabDrag.cs` | Tab drag-reorder logic |
| `MainWindow.Input.cs` | Mouse/keyboard input handlers |
| `MainWindow.Overlay.cs` | Window Picker and Command Palette overlay open/close |
| `MainWindow.ViewState.cs` | View state helpers (content tab display, web tab display) |
| `MainWindow.SettingsTab.cs` | Settings tab navigation |

### Services Partial Classes

`TabManager` is also split:
- `TabManager.cs` — core add/remove/close/select logic.
- `TabManager.Lifecycle.cs` — startup embedding, cleanup on exit.
- `TabManager.Groups.cs` — tab group management.
- `TabManager.GlobalHook.cs` — auto-embed new windows option.

### Settings

`AppSettings` (JSON, managed by `SettingsManager`) is the single settings model. Notable fields:
- `TabHeaderPosition` — `"Top"` / `"Bottom"` / `"Left"` / `"Right"`
- `EmbedCloseAction` — `"CloseApp"` / `"ReleaseEmbed"` / `"CloseWind"`
- `CloseWindowsOnExit` — `"None"` / `"All"` / `"StartupOnly"`
- `AutoEmbedNewWindows` — install a global `WinEventHook` to auto-capture new windows
- `StartupApplications` — list of executables to launch and embed at startup

### Coding Conventions

- File-scoped namespaces (`namespace WindowzTabManager;`).
- `_camelCase` for private fields; PascalCase for everything public.
- All Win32 P/Invoke centralized in `NativeMethods.cs`.
- CommunityToolkit.Mvvm `[ObservableProperty]` generates observable properties via source generators — avoid manually writing `INotifyPropertyChanged` boilerplate.
- WPF-UI (`Wpf.Ui`) provides the dark-theme controls and `SymbolRegular` icon enum.
- Preserve existing Japanese comments when editing files that contain them.
