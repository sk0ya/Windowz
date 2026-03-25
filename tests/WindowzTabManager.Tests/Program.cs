using WindowzTabManager;
using WindowzTabManager.Models;
using WindowzTabManager.Services;
using WindowzTabManager.ViewModels;

namespace WindowzTabManager.Tests;

internal static class Program
{
    private static int _failed;

    private static int Main()
    {
        Run("OverlayStandaloneOnStandalone_CreatesTileGroup", OverlayStandaloneOnStandalone_CreatesTileGroup);
        Run("OverlayStandaloneOnTileApp_AddsToExistingTileGroup", OverlayStandaloneOnTileApp_AddsToExistingTileGroup);
        Run("OverlayTileAppOnStandalone_CreatesTileGroupAndKeepsSourceGroup", OverlayTileAppOnStandalone_CreatesTileGroupAndKeepsSourceGroup);
        Run("OverlayTileAppOnStandalone_DissolvesSourceGroupWhenNeeded", OverlayTileAppOnStandalone_DissolvesSourceGroupWhenNeeded);
        Run("MoveTileAppToStandalone_KeepsAppAndGroupState", MoveTileAppToStandalone_KeepsAppAndGroupState);
        Run("MoveTileAppToStandaloneAtDrop_BetweenTiles_InsertsAtTargetPosition", MoveTileAppToStandaloneAtDrop_BetweenTiles_InsertsAtTargetPosition);
        Run("MoveTileAppToStandaloneAtDrop_InsertAfterTarget_Works", MoveTileAppToStandaloneAtDrop_InsertAfterTarget_Works);
        Run("MoveAppBetweenTileGroups_DissolvesSourceAndMoves", MoveAppBetweenTileGroups_DissolvesSourceAndMoves);
        Run("ReorderMixedLaunchItems_InsertAfter_ChangesOrder", ReorderMixedLaunchItems_InsertAfter_ChangesOrder);
        Run("ReorderTileGroupBeforeStandalone_PersistsStartupOrder", ReorderTileGroupBeforeStandalone_PersistsStartupOrder);
        Run("ReorderSameLaunchItem_ReturnsFalseAndKeepsOrder", ReorderSameLaunchItem_ReturnsFalseAndKeepsOrder);
        Run("AddAppToFullTileGroup_DoesNothing", AddAppToFullTileGroup_DoesNothing);
        Run("GetDefaultBindings_CloseTabUsesCtrlShiftW", GetDefaultBindings_CloseTabUsesCtrlShiftW);

        Console.WriteLine(_failed == 0
            ? "All tests passed."
            : $"{_failed} test(s) failed.");

        return _failed == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            Console.WriteLine($"PASS: {name}");
        }
        catch (Exception ex)
        {
            _failed++;
            Console.WriteLine($"FAIL: {name}");
            Console.WriteLine($"  {ex.Message}");
        }
    }

    private static void MoveTileAppToStandalone_KeepsAppAndGroupState()
    {
        using var scope = new TempSettingsScope();

        string a = @"C:\Apps\A.exe";
        string b = @"C:\Apps\B.exe";
        string c = @"C:\Apps\C.exe";
        string d = @"C:\Apps\D.exe";

        scope.Manager.SetStartupApplications(new[]
        {
            App(a), App(b), App(c), App(d)
        });
        scope.Manager.AddStartupTileGroup(new List<string> { b, c, d });

        var vm = new StartupSettingsViewModel(scope.Manager);
        var group = SingleTileGroup(vm);
        var moved = group.Apps.Single(x => PathEquals(x.Path, c));

        vm.MoveAppToStandaloneFromDrop(moved);

        Assert(vm.StartupApplications.Any(x => PathEquals(x.Path, c)),
            "Moved app should be in standalone applications.");
        Assert(!vm.IsInTileGroup(moved),
            "Moved app should no longer belong to a tile group.");
        Assert(vm.TileGroups.Count == 1,
            "Tile group should remain when it still has at least 2 apps.");
        AssertSequence(
            vm.TileGroups.Single().Apps.Select(x => x.Path),
            new[] { b, d },
            "Remaining tile app order should be preserved.");
        AssertSequence(
            scope.Manager.Settings.StartupTileGroups.Single().AppPaths,
            new[] { b, d },
            "Tile group setting should be updated after moving app to standalone.");
    }

    private static void OverlayStandaloneOnStandalone_CreatesTileGroup()
    {
        using var scope = new TempSettingsScope();

        string a = @"C:\Apps\A.exe";
        string b = @"C:\Apps\B.exe";
        string c = @"C:\Apps\C.exe";

        scope.Manager.SetStartupApplications(new[]
        {
            App(a), App(b), App(c)
        });

        var vm = new StartupSettingsViewModel(scope.Manager);
        var source = vm.StartupApplications.Single(x => PathEquals(x.Path, a));
        var target = vm.StartupApplications.Single(x => PathEquals(x.Path, b));

        vm.CreateTileGroupFromDrop(source, target);

        Assert(vm.TileGroups.Count == 1, "Exactly one tile group should be created.");
        Assert(!vm.StartupApplications.Any(x => PathEquals(x.Path, a)),
            "Source standalone app should leave standalone list.");
        Assert(!vm.StartupApplications.Any(x => PathEquals(x.Path, b)),
            "Target standalone app should leave standalone list.");
        Assert(vm.StartupApplications.Any(x => PathEquals(x.Path, c)),
            "Unrelated standalone app should remain.");
        AssertSequence(
            vm.TileGroups.Single().Apps.Select(x => x.Path),
            new[] { a, b },
            "New tile group should keep overlay order as source -> target.");
        AssertSequence(
            vm.LaunchItems.Select(GetLaunchItemKey),
            new[] { $"G:{a}|{b}", $"A:{c}" },
            "Launch list should show new tile and remaining standalone app.");
    }

    private static void GetDefaultBindings_CloseTabUsesCtrlShiftW()
    {
        var binding = HotkeyManager.GetDefaultBindings()
            .Single(x => x.Action == HotkeyAction.CloseTab);

        Assert(binding.Modifiers == (System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift),
            "Close Tab default modifiers should be Ctrl+Shift.");
        Assert(binding.Key == System.Windows.Input.Key.W,
            "Close Tab default key should remain W.");
    }

    private static void OverlayStandaloneOnTileApp_AddsToExistingTileGroup()
    {
        using var scope = new TempSettingsScope();

        string a = @"C:\Apps\A.exe";
        string b = @"C:\Apps\B.exe";
        string c = @"C:\Apps\C.exe";
        string d = @"C:\Apps\D.exe";

        scope.Manager.SetStartupApplications(new[]
        {
            App(a), App(b), App(c), App(d)
        });
        scope.Manager.AddStartupTileGroup(new List<string> { b, c });

        var vm = new StartupSettingsViewModel(scope.Manager);
        var source = vm.StartupApplications.Single(x => PathEquals(x.Path, a));
        var target = vm.TileGroups.Single().Apps.Single(x => PathEquals(x.Path, c));

        vm.CreateTileGroupFromDrop(source, target);

        var group = vm.TileGroups.Single();
        AssertSequence(
            group.Apps.Select(x => x.Path),
            new[] { b, c, a },
            "Overlay onto tile app should append standalone app to that tile group.");
        Assert(!vm.StartupApplications.Any(x => PathEquals(x.Path, a)),
            "Added app should be removed from standalone list.");
        AssertSequence(
            scope.Manager.Settings.StartupTileGroups.Single().AppPaths,
            new[] { b, c, a },
            "Tile group setting should persist appended app order.");
    }

    private static void OverlayTileAppOnStandalone_CreatesTileGroupAndKeepsSourceGroup()
    {
        using var scope = new TempSettingsScope();

        string a = @"C:\Apps\A.exe";
        string b = @"C:\Apps\B.exe";
        string c = @"C:\Apps\C.exe";
        string d = @"C:\Apps\D.exe";
        string e = @"C:\Apps\E.exe";

        scope.Manager.SetStartupApplications(new[]
        {
            App(a), App(b), App(c), App(d), App(e)
        });
        scope.Manager.AddStartupTileGroup(new List<string> { b, c, d });

        var vm = new StartupSettingsViewModel(scope.Manager);
        var source = vm.TileGroups.Single().Apps.Single(x => PathEquals(x.Path, c));
        var target = vm.StartupApplications.Single(x => PathEquals(x.Path, a));

        vm.CreateTileGroupFromDrop(source, target);

        Assert(vm.TileGroups.Count == 2,
            "New tile group should be created while source group remains.");
        var sourceGroup = vm.TileGroups.Single(g => g.Apps.Any(x => PathEquals(x.Path, b)));
        var newGroup = vm.TileGroups.Single(g => g.Apps.Any(x => PathEquals(x.Path, c)));

        AssertSequence(
            sourceGroup.Apps.Select(x => x.Path),
            new[] { b, d },
            "Source group should keep remaining apps in order.");
        AssertSequence(
            newGroup.Apps.Select(x => x.Path),
            new[] { c, a },
            "New tile group should contain dragged tile app and target standalone app.");
        Assert(!vm.StartupApplications.Any(x => PathEquals(x.Path, a)),
            "Target standalone app should be removed from standalone list.");
        Assert(!vm.StartupApplications.Any(x => PathEquals(x.Path, c)),
            "Dragged tile app should not remain standalone.");
    }

    private static void OverlayTileAppOnStandalone_DissolvesSourceGroupWhenNeeded()
    {
        using var scope = new TempSettingsScope();

        string a = @"C:\Apps\A.exe";
        string b = @"C:\Apps\B.exe";
        string c = @"C:\Apps\C.exe";
        string d = @"C:\Apps\D.exe";

        scope.Manager.SetStartupApplications(new[]
        {
            App(a), App(b), App(c), App(d)
        });
        scope.Manager.AddStartupTileGroup(new List<string> { b, c });

        var vm = new StartupSettingsViewModel(scope.Manager);
        var source = vm.TileGroups.Single().Apps.Single(x => PathEquals(x.Path, c));
        var target = vm.StartupApplications.Single(x => PathEquals(x.Path, a));

        vm.CreateTileGroupFromDrop(source, target);

        Assert(vm.TileGroups.Count == 1,
            "Source group with 2 apps should dissolve after one app is dragged out.");
        AssertSequence(
            vm.TileGroups.Single().Apps.Select(x => x.Path),
            new[] { c, a },
            "Remaining tile should be the new one created by overlay.");
        Assert(vm.StartupApplications.Any(x => PathEquals(x.Path, b)),
            "Leftover app from dissolved source group should return standalone.");
        AssertSequence(
            vm.LaunchItems.Select(GetLaunchItemKey),
            new[] { $"G:{c}|{a}", $"A:{b}", $"A:{d}" },
            "Launch order should include new tile then remaining standalones.");
    }

    private static void MoveAppBetweenTileGroups_DissolvesSourceAndMoves()
    {
        using var scope = new TempSettingsScope();

        string a = @"C:\Apps\A.exe";
        string b = @"C:\Apps\B.exe";
        string c = @"C:\Apps\C.exe";
        string d = @"C:\Apps\D.exe";
        string e = @"C:\Apps\E.exe";

        scope.Manager.SetStartupApplications(new[]
        {
            App(a), App(b), App(c), App(d), App(e)
        });
        scope.Manager.AddStartupTileGroup(new List<string> { b, c });
        scope.Manager.AddStartupTileGroup(new List<string> { d, e });

        var vm = new StartupSettingsViewModel(scope.Manager);
        var sourceGroup = vm.TileGroups.Single(g => g.Apps.Any(x => PathEquals(x.Path, c)));
        var targetGroup = vm.TileGroups.Single(g => g.Apps.Any(x => PathEquals(x.Path, d)));
        var moved = sourceGroup.Apps.Single(x => PathEquals(x.Path, c));

        vm.AddAppToTileGroupFromDrop(moved, targetGroup);

        Assert(vm.TileGroups.Count == 1,
            "Source group should dissolve when it drops below 2 apps.");
        Assert(vm.StartupApplications.Any(x => PathEquals(x.Path, b)),
            "Remaining app from dissolved source group should return to standalone.");
        Assert(!vm.StartupApplications.Any(x => PathEquals(x.Path, c)),
            "Moved app should not remain in standalone list.");
        Assert(vm.IsInTileGroup(moved),
            "Moved app should belong to the target tile group.");
        AssertSequence(
            targetGroup.Apps.Select(x => x.Path),
            new[] { d, e, c },
            "Moved app should be appended to the target tile group.");
        Assert(scope.Manager.Settings.StartupTileGroups.Count == 1,
            "Settings should remove dissolved source tile group.");
        AssertSequence(
            scope.Manager.Settings.StartupTileGroups.Single().AppPaths,
            new[] { d, e, c },
            "Settings should persist the updated target tile group order.");
    }

    private static void MoveTileAppToStandaloneAtDrop_BetweenTiles_InsertsAtTargetPosition()
    {
        using var scope = new TempSettingsScope();

        string a = @"C:\Apps\A.exe";
        string b = @"C:\Apps\B.exe";
        string c = @"C:\Apps\C.exe";
        string d = @"C:\Apps\D.exe";
        string e = @"C:\Apps\E.exe";
        string f = @"C:\Apps\F.exe";

        scope.Manager.SetStartupApplications(new[]
        {
            App(a), App(b), App(c), App(d), App(e), App(f)
        });
        scope.Manager.AddStartupTileGroup(new List<string> { b, c, d });
        scope.Manager.AddStartupTileGroup(new List<string> { e, f });

        var vm = new StartupSettingsViewModel(scope.Manager);
        var sourceGroup = vm.TileGroups.Single(g => g.Apps.Any(x => PathEquals(x.Path, c)));
        var targetGroup = vm.TileGroups.Single(g => g.Apps.Any(x => PathEquals(x.Path, e)));
        var moved = sourceGroup.Apps.Single(x => PathEquals(x.Path, c));

        bool changed = vm.MoveAppToStandaloneAtDrop(moved, targetGroup, insertAfter: false);

        Assert(changed, "Move to standalone at drop should succeed.");
        Assert(vm.StartupApplications.Any(x => PathEquals(x.Path, c)),
            "Moved app should become standalone.");
        Assert(!vm.IsInTileGroup(moved),
            "Moved app should no longer belong to a tile group.");
        AssertSequence(
            sourceGroup.Apps.Select(x => x.Path),
            new[] { b, d },
            "Source tile group should keep remaining app order.");
        AssertSequence(
            vm.LaunchItems.Select(GetLaunchItemKey),
            new[] { $"A:{a}", $"G:{b}|{d}", $"A:{c}", $"G:{e}|{f}" },
            "Moved app should be inserted between launch tiles at the drop position.");
        AssertSequence(
            scope.Manager.Settings.StartupApplications.Select(x => x.Path),
            new[] { a, b, d, c, e, f },
            "Settings should persist insertion order after detaching from tile.");
    }

    private static void MoveTileAppToStandaloneAtDrop_InsertAfterTarget_Works()
    {
        using var scope = new TempSettingsScope();

        string a = @"C:\Apps\A.exe";
        string b = @"C:\Apps\B.exe";
        string c = @"C:\Apps\C.exe";
        string d = @"C:\Apps\D.exe";

        scope.Manager.SetStartupApplications(new[]
        {
            App(a), App(b), App(c), App(d)
        });
        scope.Manager.AddStartupTileGroup(new List<string> { b, c });

        var vm = new StartupSettingsViewModel(scope.Manager);
        var moved = vm.TileGroups.Single().Apps.Single(x => PathEquals(x.Path, c));
        var target = vm.StartupApplications.Single(x => PathEquals(x.Path, a));

        bool changed = vm.MoveAppToStandaloneAtDrop(moved, target, insertAfter: true);

        Assert(changed, "Move to standalone at drop should succeed.");
        AssertSequence(
            vm.LaunchItems.Select(GetLaunchItemKey),
            new[] { $"A:{a}", $"A:{c}", $"A:{b}", $"A:{d}" },
            "Dragged app should be inserted after target app.");
        AssertSequence(
            scope.Manager.Settings.StartupApplications.Select(x => x.Path),
            new[] { a, c, b, d },
            "Settings should persist insert-after order.");
    }

    private static void ReorderMixedLaunchItems_InsertAfter_ChangesOrder()
    {
        using var scope = new TempSettingsScope();

        string a = @"C:\Apps\A.exe";
        string b = @"C:\Apps\B.exe";
        string c = @"C:\Apps\C.exe";
        string d = @"C:\Apps\D.exe";

        scope.Manager.SetStartupApplications(new[]
        {
            App(a), App(b), App(c), App(d)
        });
        scope.Manager.AddStartupTileGroup(new List<string> { b, c });

        var vm = new StartupSettingsViewModel(scope.Manager);
        var standaloneA = vm.LaunchItems.OfType<StartupAppItem>().Single(x => PathEquals(x.Path, a));
        var tile = vm.LaunchItems.OfType<StartupTileGroupItem>().Single();

        bool changed = vm.TryReorderLaunchItems(standaloneA, tile, insertAfter: true);

        Assert(changed, "Reorder should return true for valid source/target.");
        AssertSequence(
            vm.LaunchItems.Select(GetLaunchItemKey),
            new[] { $"G:{b}|{c}", $"A:{a}", $"A:{d}" },
            "Top-level launch item order should move standalone app after target tile.");
        AssertSequence(
            scope.Manager.Settings.StartupApplications.Select(x => x.Path),
            new[] { b, c, a, d },
            "StartupApplications setting should persist reordered launch sequence.");
    }

    private static void ReorderTileGroupBeforeStandalone_PersistsStartupOrder()
    {
        using var scope = new TempSettingsScope();

        string a = @"C:\Apps\A.exe";
        string b = @"C:\Apps\B.exe";
        string c = @"C:\Apps\C.exe";
        string d = @"C:\Apps\D.exe";
        string e = @"C:\Apps\E.exe";

        scope.Manager.SetStartupApplications(new[]
        {
            App(a), App(b), App(c), App(d), App(e)
        });
        scope.Manager.AddStartupTileGroup(new List<string> { b, c });
        scope.Manager.AddStartupTileGroup(new List<string> { d, e });

        var vm = new StartupSettingsViewModel(scope.Manager);
        var targetStandalone = vm.LaunchItems.OfType<StartupAppItem>().Single(x => PathEquals(x.Path, a));
        var sourceGroup = vm.LaunchItems.OfType<StartupTileGroupItem>().Single(g => g.Apps.Any(x => PathEquals(x.Path, d)));

        bool changed = vm.TryReorderLaunchItems(sourceGroup, targetStandalone, insertAfter: false);

        Assert(changed, "Reorder should succeed for tile-to-standalone move.");
        AssertSequence(
            vm.LaunchItems.Select(GetLaunchItemKey),
            new[] { $"G:{d}|{e}", $"A:{a}", $"G:{b}|{c}" },
            "Tile group should be inserted before standalone app.");
        AssertSequence(
            scope.Manager.Settings.StartupApplications.Select(x => x.Path),
            new[] { d, e, a, b, c },
            "Settings order should match top-level launch item reorder.");
    }

    private static void ReorderSameLaunchItem_ReturnsFalseAndKeepsOrder()
    {
        using var scope = new TempSettingsScope();

        string a = @"C:\Apps\A.exe";
        string b = @"C:\Apps\B.exe";

        scope.Manager.SetStartupApplications(new[]
        {
            App(a), App(b)
        });

        var vm = new StartupSettingsViewModel(scope.Manager);
        var item = vm.LaunchItems.OfType<StartupAppItem>().Single(x => PathEquals(x.Path, a));

        bool changed = vm.TryReorderLaunchItems(item, item, insertAfter: true);

        Assert(!changed, "Reorder with same source and target should return false.");
        AssertSequence(
            vm.LaunchItems.Select(GetLaunchItemKey),
            new[] { $"A:{a}", $"A:{b}" },
            "Launch order should not change.");
        AssertSequence(
            scope.Manager.Settings.StartupApplications.Select(x => x.Path),
            new[] { a, b },
            "Settings order should remain unchanged.");
    }

    private static void AddAppToFullTileGroup_DoesNothing()
    {
        using var scope = new TempSettingsScope();

        string a = @"C:\Apps\A.exe";
        string b = @"C:\Apps\B.exe";
        string c = @"C:\Apps\C.exe";
        string d = @"C:\Apps\D.exe";
        string e = @"C:\Apps\E.exe";

        scope.Manager.SetStartupApplications(new[]
        {
            App(a), App(b), App(c), App(d), App(e)
        });
        scope.Manager.AddStartupTileGroup(new List<string> { b, c, d, e });

        var vm = new StartupSettingsViewModel(scope.Manager);
        var source = vm.StartupApplications.Single(x => PathEquals(x.Path, a));
        var fullGroup = vm.TileGroups.Single();

        vm.AddAppToTileGroupFromDrop(source, fullGroup);

        AssertSequence(
            fullGroup.Apps.Select(x => x.Path),
            new[] { b, c, d, e },
            "Full tile group should not accept additional apps.");
        Assert(vm.StartupApplications.Any(x => PathEquals(x.Path, a)),
            "Source app should remain standalone when tile is full.");
        AssertSequence(
            scope.Manager.Settings.StartupTileGroups.Single().AppPaths,
            new[] { b, c, d, e },
            "Settings should keep original full tile order.");
    }

    private static StartupTileGroupItem SingleTileGroup(StartupSettingsViewModel vm)
    {
        Assert(vm.TileGroups.Count == 1, "Expected exactly one tile group.");
        return vm.TileGroups.Single();
    }

    private static StartupApplicationSetting App(string path)
    {
        return new StartupApplicationSetting
        {
            Path = path,
            Arguments = string.Empty,
            Name = Path.GetFileNameWithoutExtension(path)
        };
    }

    private static string GetLaunchItemKey(object item)
    {
        return item switch
        {
            StartupAppItem app => $"A:{app.Path}",
            StartupTileGroupItem group => $"G:{string.Join("|", group.Apps.Select(x => x.Path))}",
            _ => throw new InvalidOperationException($"Unexpected launch item type: {item.GetType().FullName}")
        };
    }

    private static bool PathEquals(string left, string right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void AssertSequence(IEnumerable<string> actual, IEnumerable<string> expected, string message)
    {
        var actualList = actual.ToList();
        var expectedList = expected.ToList();

        if (actualList.SequenceEqual(expectedList, StringComparer.OrdinalIgnoreCase))
            return;

        throw new InvalidOperationException(
            $"{message}{Environment.NewLine}" +
            $"Expected: [{string.Join(", ", expectedList)}]{Environment.NewLine}" +
            $"Actual:   [{string.Join(", ", actualList)}]");
    }

    private sealed class TempSettingsScope : IDisposable
    {
        public string DirectoryPath { get; }
        public SettingsManager Manager { get; }

        public TempSettingsScope()
        {
            DirectoryPath = Path.Combine(
                Path.GetTempPath(),
                "WindowzTabManager.Tests",
                Guid.NewGuid().ToString("N"));
            Manager = new SettingsManager(DirectoryPath);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(DirectoryPath))
                    Directory.Delete(DirectoryPath, recursive: true);
            }
            catch
            {
                // Ignore cleanup failures in test harness.
            }
        }
    }
}
