# Repository Guidelines

## Project Structure & Module Organization
This repository contains a single WPF desktop app: `WindowzTabManager.csproj` (`net8.0-windows`) at the root.

- `MainWindow.xaml` + `MainWindow.xaml.cs`: main tab-host window and interaction logic.
- `TabItemControl.xaml` + `TabItemControl.xaml.cs`: reusable tab UI component.
- `WindowPickerWindow.xaml` + `WindowPickerWindow.xaml.cs`: picker dialog for external windows.
- `ManagedWindow.cs`: tracked window state.
- `NativeMethods.cs`: Win32 P/Invoke declarations and constants.
- Generated output is in `bin/` and `obj/` (do not edit/commit manually).

## Build, Test, and Development Commands
- `dotnet restore`: restore NuGet packages.
- `dotnet build WindowzTabManager.csproj`: compile the app (verified in this repo).
- `dotnet run --project WindowzTabManager.csproj`: launch locally for manual verification.
- `dotnet test`: run tests; currently there is no dedicated test project, so this is mainly for future additions.

## Coding Style & Naming Conventions
- Use 4-space indentation and standard C# brace style consistent with existing files.
- Keep file-scoped namespace style (`namespace WindowzTabManager;`).
- Use PascalCase for types/methods/properties/events.
- Use camelCase for locals/parameters.
- Use `_camelCase` for private fields (for example, `_tabs`, `_activeIndex`).
- Keep Win32 interop centralized in `NativeMethods.cs`; keep UI behavior in the related XAML code-behind file.
- Prefer concise comments; preserve nearby Japanese comments when editing those areas.

## Testing Guidelines
- No automated tests are currently present in this snapshot.
- For new logic, add a separate test project (for example, `tests/WindowzTabManager.Tests`) and wire it into the solution.
- Suggested test naming: `MethodOrScenario_Condition_ExpectedResult`.
- For UI/interop changes, include manual test notes covering attach/switch/minimize/restore/close flows.

## Commit & Pull Request Guidelines
- Git history is not available in this workspace snapshot, so follow a clear imperative commit style (for example, `Fix tab activation after minimize`).
- Keep commits focused by concern and avoid mixing refactors with behavior changes.
- In each PR, include what changed and why.
- In each PR, include validation steps (`dotnet build`, manual checks).
- Include screenshot(s) for UI updates.
- Link the related issue/task when applicable.
