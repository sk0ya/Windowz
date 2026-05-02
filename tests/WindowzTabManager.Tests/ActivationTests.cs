using WindowzTabManager.Models;
using WindowzTabManager.Services;

namespace WindowzTabManager.Tests;

/// <summary>
/// タブ・アクティブ状態の遷移ロジックに関するテスト。
/// Win32 ウィンドウを使わず TabManager の状態機械だけを検証する。
/// </summary>
internal static class ActivationTests
{
    // ─────────────────────────────────────────
    // IsSelected 遷移
    // ─────────────────────────────────────────

    internal static void ActiveTab_SetNewTab_OldTabIsDeselected()
    {
        using var scope = new TempSettingsScope();
        var mgr = CreateTabManager(scope.Manager);
        try
        {
            var a = ContentTab("A");
            var b = ContentTab("B");
            mgr.Tabs.Add(a);
            mgr.Tabs.Add(b);

            mgr.ActiveTab = a;
            mgr.ActiveTab = b;

            Assert(!a.IsSelected, "旧アクティブタブの IsSelected は false になるべき。");
            Assert(b.IsSelected, "新アクティブタブの IsSelected は true になるべき。");
        }
        finally { mgr.StopCleanupTimer(); }
    }

    internal static void ActiveTab_ExactlyOneTabIsSelected_AfterSwitch()
    {
        using var scope = new TempSettingsScope();
        var mgr = CreateTabManager(scope.Manager);
        try
        {
            var a = ContentTab("A");
            var b = ContentTab("B");
            var c = ContentTab("C");
            mgr.Tabs.Add(a);
            mgr.Tabs.Add(b);
            mgr.Tabs.Add(c);

            mgr.ActiveTab = a;
            mgr.ActiveTab = c;

            var selectedCount = mgr.Tabs.Count(t => t.IsSelected);
            Assert(selectedCount == 1, $"アクティブ切替後、IsSelected==true のタブはちょうど1つであるべき。実際: {selectedCount}");
            Assert(c.IsSelected, "切替先タブが選択されているべき。");
        }
        finally { mgr.StopCleanupTimer(); }
    }

    internal static void ActiveTab_SetSameTabTwice_NoExtraEventFired()
    {
        using var scope = new TempSettingsScope();
        var mgr = CreateTabManager(scope.Manager);
        try
        {
            var a = ContentTab("A");
            mgr.Tabs.Add(a);
            mgr.ActiveTab = a;

            int eventCount = 0;
            mgr.ActiveTabChanged += (_, _) => eventCount++;

            mgr.ActiveTab = a; // 同じタブを再設定

            Assert(eventCount == 0, "同じタブを再設定してもイベントは発火しないべき。");
        }
        finally { mgr.StopCleanupTimer(); }
    }

    // ─────────────────────────────────────────
    // ActiveTabChanged イベント
    // ─────────────────────────────────────────

    internal static void ActiveTabChanged_Event_FiresWithCorrectTab()
    {
        using var scope = new TempSettingsScope();
        var mgr = CreateTabManager(scope.Manager);
        try
        {
            var a = ContentTab("A");
            var b = ContentTab("B");
            mgr.Tabs.Add(a);
            mgr.Tabs.Add(b);
            mgr.ActiveTab = a;

            TabItem? received = null;
            mgr.ActiveTabChanged += (_, tab) => received = tab;

            mgr.ActiveTab = b;

            Assert(received == b, "ActiveTabChanged のイベント引数は新しいアクティブタブであるべき。");
        }
        finally { mgr.StopCleanupTimer(); }
    }

    internal static void ActiveTabChanged_Event_FiresOncePerChange()
    {
        using var scope = new TempSettingsScope();
        var mgr = CreateTabManager(scope.Manager);
        try
        {
            var a = ContentTab("A");
            var b = ContentTab("B");
            mgr.Tabs.Add(a);
            mgr.Tabs.Add(b);

            int count = 0;
            mgr.ActiveTabChanged += (_, _) => count++;

            mgr.ActiveTab = a;
            mgr.ActiveTab = b;
            mgr.ActiveTab = a;

            Assert(count == 3, $"3回の切替でイベントはちょうど3回発火すべき。実際: {count}");
        }
        finally { mgr.StopCleanupTimer(); }
    }

    internal static void ActiveTabChanged_Event_FiresWithNullWhenCleared()
    {
        using var scope = new TempSettingsScope();
        var mgr = CreateTabManager(scope.Manager);
        try
        {
            var a = ContentTab("A");
            mgr.Tabs.Add(a);
            mgr.ActiveTab = a;

            TabItem? received = ContentTab("Sentinel");
            mgr.ActiveTabChanged += (_, tab) => received = tab;

            mgr.ActiveTab = null;

            Assert(received == null, "ActiveTab を null に設定したとき、イベント引数は null であるべき。");
        }
        finally { mgr.StopCleanupTimer(); }
    }

    // ─────────────────────────────────────────
    // RemoveTab 後の隣接タブ自動選択
    // ─────────────────────────────────────────

    internal static void RemoveActiveTab_FirstOfThree_SelectsSecondTab()
    {
        using var scope = new TempSettingsScope();
        var mgr = CreateTabManager(scope.Manager);
        try
        {
            var a = ContentTab("A");
            var b = ContentTab("B");
            var c = ContentTab("C");
            mgr.Tabs.Add(a);
            mgr.Tabs.Add(b);
            mgr.Tabs.Add(c);
            mgr.ActiveTab = a;

            mgr.RemoveTab(a);

            Assert(mgr.ActiveTab == b,
                $"先頭タブ削除後は次のタブ(B)がアクティブになるべき。実際: {mgr.ActiveTab?.Title}");
        }
        finally { mgr.StopCleanupTimer(); }
    }

    internal static void RemoveActiveTab_LastOfThree_SelectsNewLastTab()
    {
        using var scope = new TempSettingsScope();
        var mgr = CreateTabManager(scope.Manager);
        try
        {
            var a = ContentTab("A");
            var b = ContentTab("B");
            var c = ContentTab("C");
            mgr.Tabs.Add(a);
            mgr.Tabs.Add(b);
            mgr.Tabs.Add(c);
            mgr.ActiveTab = c;

            mgr.RemoveTab(c);

            Assert(mgr.ActiveTab == b,
                $"末尾タブ削除後は新しい末尾(B)がアクティブになるべき。実際: {mgr.ActiveTab?.Title}");
        }
        finally { mgr.StopCleanupTimer(); }
    }

    internal static void RemoveActiveTab_MiddleTab_SelectsNextTab()
    {
        using var scope = new TempSettingsScope();
        var mgr = CreateTabManager(scope.Manager);
        try
        {
            var a = ContentTab("A");
            var b = ContentTab("B");
            var c = ContentTab("C");
            mgr.Tabs.Add(a);
            mgr.Tabs.Add(b);
            mgr.Tabs.Add(c);
            mgr.ActiveTab = b;

            mgr.RemoveTab(b);

            // index 1 だった B 削除後、Math.Min(1, 1) = 1 → C (新 index 1)
            Assert(mgr.ActiveTab == c,
                $"中間タブ削除後は後続タブ(C)がアクティブになるべき。実際: {mgr.ActiveTab?.Title}");
        }
        finally { mgr.StopCleanupTimer(); }
    }

    internal static void RemoveActiveTab_OnlyTab_ActiveBecomesNull()
    {
        using var scope = new TempSettingsScope();
        var mgr = CreateTabManager(scope.Manager);
        try
        {
            var a = ContentTab("A");
            mgr.Tabs.Add(a);
            mgr.ActiveTab = a;

            mgr.RemoveTab(a);

            Assert(mgr.ActiveTab == null, "全タブ削除後、ActiveTab は null になるべき。");
        }
        finally { mgr.StopCleanupTimer(); }
    }

    internal static void RemoveNonActiveTab_DoesNotChangeActiveTab()
    {
        using var scope = new TempSettingsScope();
        var mgr = CreateTabManager(scope.Manager);
        try
        {
            var a = ContentTab("A");
            var b = ContentTab("B");
            mgr.Tabs.Add(a);
            mgr.Tabs.Add(b);
            mgr.ActiveTab = a;

            mgr.RemoveTab(b);

            Assert(mgr.ActiveTab == a, "非アクティブタブ削除後もアクティブタブは変わらないべき。");
        }
        finally { mgr.StopCleanupTimer(); }
    }

    internal static void RemoveActiveTab_IsSelectedClearedOnRemovedTab()
    {
        using var scope = new TempSettingsScope();
        var mgr = CreateTabManager(scope.Manager);
        try
        {
            var a = ContentTab("A");
            var b = ContentTab("B");
            mgr.Tabs.Add(a);
            mgr.Tabs.Add(b);
            mgr.ActiveTab = a;

            mgr.RemoveTab(a);

            // 削除後、残りのタブの中でちょうど1つだけが IsSelected であるべき
            var selectedCount = mgr.Tabs.Count(t => t.IsSelected);
            Assert(selectedCount == 1, $"タブ削除後も IsSelected==true はちょうど1つであるべき。実際: {selectedCount}");
        }
        finally { mgr.StopCleanupTimer(); }
    }

    // ─────────────────────────────────────────
    // SelectNextTab / SelectPreviousTab
    // ─────────────────────────────────────────

    internal static void SelectNextTab_WrapsAroundFromLastToFirst()
    {
        using var scope = new TempSettingsScope();
        var mgr = CreateTabManager(scope.Manager);
        try
        {
            var a = ContentTab("A");
            var b = ContentTab("B");
            var c = ContentTab("C");
            mgr.Tabs.Add(a);
            mgr.Tabs.Add(b);
            mgr.Tabs.Add(c);
            mgr.ActiveTab = c;

            mgr.SelectNextTab();

            Assert(mgr.ActiveTab == a, "末尾から SelectNextTab するとラップして先頭(A)になるべき。");
        }
        finally { mgr.StopCleanupTimer(); }
    }

    internal static void SelectPreviousTab_WrapsAroundFromFirstToLast()
    {
        using var scope = new TempSettingsScope();
        var mgr = CreateTabManager(scope.Manager);
        try
        {
            var a = ContentTab("A");
            var b = ContentTab("B");
            var c = ContentTab("C");
            mgr.Tabs.Add(a);
            mgr.Tabs.Add(b);
            mgr.Tabs.Add(c);
            mgr.ActiveTab = a;

            mgr.SelectPreviousTab();

            Assert(mgr.ActiveTab == c, "先頭から SelectPreviousTab するとラップして末尾(C)になるべき。");
        }
        finally { mgr.StopCleanupTimer(); }
    }

    internal static void SelectNextTab_SingleTab_StaysOnSameTab()
    {
        using var scope = new TempSettingsScope();
        var mgr = CreateTabManager(scope.Manager);
        try
        {
            var a = ContentTab("A");
            mgr.Tabs.Add(a);
            mgr.ActiveTab = a;

            mgr.SelectNextTab();

            Assert(mgr.ActiveTab == a, "タブが1つのとき SelectNextTab しても同じタブのままであるべき。");
        }
        finally { mgr.StopCleanupTimer(); }
    }

    internal static void SelectPreviousTab_SingleTab_StaysOnSameTab()
    {
        using var scope = new TempSettingsScope();
        var mgr = CreateTabManager(scope.Manager);
        try
        {
            var a = ContentTab("A");
            mgr.Tabs.Add(a);
            mgr.ActiveTab = a;

            mgr.SelectPreviousTab();

            Assert(mgr.ActiveTab == a, "タブが1つのとき SelectPreviousTab しても同じタブのままであるべき。");
        }
        finally { mgr.StopCleanupTimer(); }
    }

    internal static void SelectTab_ByIndex_ActivatesCorrectTab()
    {
        using var scope = new TempSettingsScope();
        var mgr = CreateTabManager(scope.Manager);
        try
        {
            var a = ContentTab("A");
            var b = ContentTab("B");
            var c = ContentTab("C");
            mgr.Tabs.Add(a);
            mgr.Tabs.Add(b);
            mgr.Tabs.Add(c);

            mgr.SelectTab(1);
            Assert(mgr.ActiveTab == b, "SelectTab(1) は2番目のタブ(B)をアクティブにするべき。");

            mgr.SelectTab(2);
            Assert(mgr.ActiveTab == c, "SelectTab(2) は3番目のタブ(C)をアクティブにするべき。");

            mgr.SelectTab(0);
            Assert(mgr.ActiveTab == a, "SelectTab(0) は先頭タブ(A)をアクティブにするべき。");
        }
        finally { mgr.StopCleanupTimer(); }
    }

    internal static void SelectTab_OutOfRangeIndex_DoesNotChangeActiveTab()
    {
        using var scope = new TempSettingsScope();
        var mgr = CreateTabManager(scope.Manager);
        try
        {
            var a = ContentTab("A");
            mgr.Tabs.Add(a);
            mgr.ActiveTab = a;

            mgr.SelectTab(-1);
            Assert(mgr.ActiveTab == a, "負インデックスではアクティブタブが変わらないべき。");

            mgr.SelectTab(99);
            Assert(mgr.ActiveTab == a, "範囲外インデックスではアクティブタブが変わらないべき。");
        }
        finally { mgr.StopCleanupTimer(); }
    }

    // ─────────────────────────────────────────
    // AddContentTab の activate フラグ
    // ─────────────────────────────────────────

    internal static void AddContentTab_WithActivateTrue_NewTabIsActive()
    {
        using var scope = new TempSettingsScope();
        var mgr = CreateTabManager(scope.Manager);
        try
        {
            var existing = ContentTab("Existing");
            mgr.Tabs.Add(existing);
            mgr.ActiveTab = existing;

            var added = mgr.AddContentTab("Settings", "GeneralSettings", activate: true);

            Assert(mgr.ActiveTab == added,
                "activate:true で AddContentTab したとき、新しいタブがアクティブになるべき。");
        }
        finally { mgr.StopCleanupTimer(); }
    }

    internal static void AddContentTab_WithActivateFalse_ExistingTabStaysActive()
    {
        using var scope = new TempSettingsScope();
        var mgr = CreateTabManager(scope.Manager);
        try
        {
            var existing = ContentTab("Existing");
            mgr.Tabs.Add(existing);
            mgr.ActiveTab = existing;

            mgr.AddContentTab("Settings", "GeneralSettings", activate: false);

            Assert(mgr.ActiveTab == existing,
                "activate:false で AddContentTab しても既存のアクティブタブが変わらないべき。");
        }
        finally { mgr.StopCleanupTimer(); }
    }

    internal static void AddContentTab_DuplicateKey_ActivatesExistingTab()
    {
        using var scope = new TempSettingsScope();
        var mgr = CreateTabManager(scope.Manager);
        try
        {
            var first = mgr.AddContentTab("Settings", "GeneralSettings", activate: false);

            var other = ContentTab("Other");
            mgr.Tabs.Add(other);
            mgr.ActiveTab = other;

            var second = mgr.AddContentTab("Settings", "GeneralSettings", activate: true);

            Assert(ReferenceEquals(first, second),
                "同じ ContentKey を持つタブを追加すると既存のタブインスタンスが返るべき。");
            Assert(mgr.ActiveTab == first,
                "既存タブの再アクティブ化で ActiveTab がそのタブになるべき。");
            Assert(mgr.Tabs.Count(t => t.ContentKey == "GeneralSettings") == 1,
                "重複タブは追加されないべき (タブ数は1のまま)。");
        }
        finally { mgr.StopCleanupTimer(); }
    }

    // ─────────────────────────────────────────
    // MoveTab 後のアクティブ状態維持
    // ─────────────────────────────────────────

    internal static void MoveTab_ActiveTabRemainsSameAfterReorder()
    {
        using var scope = new TempSettingsScope();
        var mgr = CreateTabManager(scope.Manager);
        try
        {
            var a = ContentTab("A");
            var b = ContentTab("B");
            var c = ContentTab("C");
            mgr.Tabs.Add(a);
            mgr.Tabs.Add(b);
            mgr.Tabs.Add(c);
            mgr.ActiveTab = b;

            mgr.MoveTab(b, 2); // B を末尾へ

            Assert(mgr.ActiveTab == b, "MoveTab 後もアクティブタブは同じタブであるべき。");
            Assert(b.IsSelected, "移動後もアクティブタブの IsSelected は true であるべき。");
        }
        finally { mgr.StopCleanupTimer(); }
    }

    // ─────────────────────────────────────────
    // ヘルパー
    // ─────────────────────────────────────────

    private static TabManager CreateTabManager(SettingsManager settingsManager) =>
        new(new WindowManager(settingsManager), settingsManager, null!);

    private static TabItem ContentTab(string title) =>
        new() { Title = title, ContentKey = title };

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
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
            catch { }
        }
    }
}
