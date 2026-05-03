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
    // PinnedHalf とアクティブタブの遷移
    // ─────────────────────────────────────────

    internal static void PinTab_WithDifferentActiveTab_DoesNotChangeActiveTab()
    {
        using var scope = new TempSettingsScope();
        var mgr = CreateTabManager(scope.Manager);
        try
        {
            var appA = ContentTab("AppA");
            var appB = ContentTab("AppB");
            mgr.Tabs.Add(appA);
            mgr.Tabs.Add(appB);
            mgr.ActiveTab = appB;

            mgr.PinTab(appA, Models.PinnedSide.Left);

            Assert(mgr.ActiveTab == appB,
                "PinTab 後もアクティブタブは変わらないべき (別タブが active な場合)。");
            Assert(mgr.PinnedHalf?.PinnedTab == appA,
                "PinnedHalf.PinnedTab は固定されたタブであるべき。");
        }
        finally { mgr.StopCleanupTimer(); }
    }

    internal static void PinnedHalf_SwitchActiveToWebTab_ActiveTabBecomesWebTab()
    {
        // バグ再現シナリオ: AppA をピン留め → AppB アクティブ → webTab に切り替え
        // 修正前: AppB 最小化後に AppA がフォアグラウンドを取得し、UI 層が
        //         ActiveTab = appA を誤設定して webTab 表示が失われていた。
        // 修正後: OnForegroundWindowChanged で PinnedHalf.PinnedTab == matchingTab の
        //         場合は ActiveTab 変更をスキップする。
        using var scope = new TempSettingsScope();
        var mgr = CreateTabManager(scope.Manager);
        try
        {
            var appA = ContentTab("AppA");
            var appB = ContentTab("AppB");
            var webTab = new Models.TabItem { WebUrl = "https://example.com", Title = "Web" };
            mgr.Tabs.Add(appA);
            mgr.Tabs.Add(appB);
            mgr.Tabs.Add(webTab);

            mgr.ActiveTab = appB;
            mgr.PinTab(appA, Models.PinnedSide.Left);

            mgr.ActiveTab = webTab;

            Assert(mgr.ActiveTab == webTab,
                "ピン留め中に webTab に切り替えた後、ActiveTab は webTab であるべき。");
            Assert(mgr.PinnedHalf?.PinnedTab == appA,
                "ピン留めは解除されていないべき。");
            Assert(!appA.IsSelected,
                "ピン留めタブ AppA は選択状態でないべき。");
            Assert(webTab.IsSelected,
                "webTab は選択状態であるべき。");
        }
        finally { mgr.StopCleanupTimer(); }
    }

    internal static void PinnedHalf_SelectPinnedTabItself_SwitchesActiveToIt()
    {
        using var scope = new TempSettingsScope();
        var mgr = CreateTabManager(scope.Manager);
        try
        {
            var appA = ContentTab("AppA");
            var appB = ContentTab("AppB");
            mgr.Tabs.Add(appA);
            mgr.Tabs.Add(appB);

            mgr.ActiveTab = appB;
            mgr.PinTab(appA, Models.PinnedSide.Left);

            // ピン留めタブ自身を明示的に選択できる (シングルウィンドウ全画面モード)
            mgr.ActiveTab = appA;

            Assert(mgr.ActiveTab == appA,
                "ピン留めタブ自身をアクティブにできるべき。");
            Assert(appA.IsSelected, "AppA は IsSelected になるべき。");
            Assert(!appB.IsSelected, "AppB は IsSelected でなくなるべき。");
        }
        finally { mgr.StopCleanupTimer(); }
    }

    // ─────────────────────────────────────────
    // ドラッグ中の逆算条件 (SyncWindFromManagedWindow)
    // ─────────────────────────────────────────

    /// <summary>
    /// ピン留め半面モードでは PinnedHalf.PinnedTab と ActiveTab の両方が確定している。
    /// SyncWindFromManagedWindow はアクティブスロット (fractions[1]) を使って Windowz 位置を
    /// 逆算しなければならない。else ブランチに落ちると activeWindow の幅 (半分) を
    /// フルコンテンツ幅と誤認して Windowz が縮小・誤移動するバグの前提条件を確認するテスト。
    /// </summary>
    internal static void PinnedHalf_ActiveTabDiffersFromPinnedTab_ConditionForFractionInverseCalc()
    {
        using var scope = new TempSettingsScope();
        var mgr = CreateTabManager(scope.Manager);
        try
        {
            var appA = ContentTab("AppA");
            var appB = ContentTab("AppB");
            mgr.Tabs.Add(appA);
            mgr.Tabs.Add(appB);

            mgr.ActiveTab = appB;
            mgr.PinTab(appA, Models.PinnedSide.Left);

            // SyncWindFromManagedWindow で isPinnedHalfActive=true になる条件:
            //   PinnedHalf != null && SelectedTab != PinnedHalf.PinnedTab
            Assert(mgr.PinnedHalf != null,
                "PinTab 後 PinnedHalf は非 null であるべき。");
            Assert(mgr.PinnedHalf!.PinnedTab == appA,
                "PinnedTab は appA であるべき。");
            Assert(mgr.ActiveTab == appB,
                "ActiveTab は appB のままであるべき。");
            Assert(mgr.ActiveTab != mgr.PinnedHalf.PinnedTab,
                "ActiveTab != PinnedTab → ピン留め半面のフラクション逆算ルートに入る条件が成立する。");

            // fractions[1] が right-half を示すことを確認
            var fractions = mgr.PinnedHalf.GetLayoutFractions();
            Assert(fractions.Length == 2, "ピン留め半面のフラクション数は 2 であるべき。");
            Assert(fractions[0].Left == 0.0 && fractions[0].Width == 0.5,
                "fractions[0] は左半分 (Left=0, Width=0.5) であるべき。");
            Assert(fractions[1].Left == 0.5 && fractions[1].Width == 0.5,
                "fractions[1] は右半分 (Left=0.5, Width=0.5) であるべき。アクティブスロットの逆算に使う。");
        }
        finally { mgr.StopCleanupTimer(); }
    }

    /// <summary>
    /// 右半分ピン留めの場合、fractions[0] が右半分、fractions[1] が左半分になることを確認。
    /// </summary>
    internal static void PinnedHalf_RightSide_FractionsAreCorrect()
    {
        using var scope = new TempSettingsScope();
        var mgr = CreateTabManager(scope.Manager);
        try
        {
            var appA = ContentTab("AppA");
            var appB = ContentTab("AppB");
            mgr.Tabs.Add(appA);
            mgr.Tabs.Add(appB);

            mgr.ActiveTab = appB;
            mgr.PinTab(appA, Models.PinnedSide.Right);

            var fractions = mgr.PinnedHalf!.GetLayoutFractions();
            Assert(fractions[0].Left == 0.5 && fractions[0].Width == 0.5,
                "右ピン留めの fractions[0] は右半分 (Left=0.5, Width=0.5) であるべき。");
            Assert(fractions[1].Left == 0.0 && fractions[1].Width == 0.5,
                "右ピン留めの fractions[1] は左半分 (Left=0, Width=0.5) であるべき。");
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
