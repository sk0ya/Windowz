namespace WindowzTabManager.Tests;

/// <summary>
/// タスクバー操作・フォアグラウンド昇格の判定ロジック (<see cref="ForegroundActivationPolicy"/>) のテスト。
///
/// 再現していた不具合:
///   「表示していない managed アプリのタスクバーボタンをクリックしても、
///     Windowz でそのタブが選択・表示されないことがある」
///
/// 実機ログ (activation.log) に残っていた失敗痕跡:
///   skip stale event=0x6A0516('Editor' Editor.App) current=0x70704('Builder' Builder)
///   → Editor の前景化イベントが届いているのに、その時点の前景が Builder
///     (＝現在表示中のタブ) だったため stale として破棄され、タブが切り替わらなかった。
///     前景を Builder に戻していたのは Windowz 自身の昇格処理 (ForceForegroundWindow)。
/// </summary>
internal static class ForegroundActivationPolicyTests
{
    // ─────────────────────────────────────────
    // stale イベント判定 (ShouldProcessForegroundEvent)
    // ─────────────────────────────────────────

    /// <summary>
    /// 本命の回帰テスト: 別タブの managed ウィンドウが前景化したイベントが、
    /// Windowz 自身の昇格で現アクティブタブに前景を奪い返された後に届いた場合、
    /// 破棄せず処理してタブを切り替えなければならない。
    /// </summary>
    internal static void ForegroundEvent_ManagedTabStolenBackByActiveManagedWindow_IsProcessed()
    {
        bool process = ForegroundActivationPolicy.ShouldProcessForegroundEvent(
            foregroundMatchesEvent: false,
            eventMatchesManagedTab: true,
            foregroundIsWindowz: false,
            foregroundIsActiveManagedWindow: true,
            eventAgeMs: 30);

        Assert(process,
            "現アクティブタブのウィンドウが前景を奪い返しただけなら、" +
            "別タブ managed ウィンドウの前景化イベントは処理されるべき。");
    }

    /// <summary>
    /// Windowz 本体が前景を取り返したケース (復元直後の Activated 昇格経路)。
    /// これも自分が原因なのでイベントを処理する。
    /// </summary>
    internal static void ForegroundEvent_ManagedTabStolenBackByWindowz_IsProcessed()
    {
        bool process = ForegroundActivationPolicy.ShouldProcessForegroundEvent(
            foregroundMatchesEvent: false,
            eventMatchesManagedTab: true,
            foregroundIsWindowz: true,
            foregroundIsActiveManagedWindow: false,
            eventAgeMs: 120);

        Assert(process,
            "Windowz 本体が前景を持っている場合も、managed タブの前景化イベントは処理されるべき。");
    }

    /// <summary>
    /// ユーザーが第三のアプリ (Chrome 等) へ移った後に届いた古いイベントは、
    /// 従来どおり破棄しなければならない。ここで処理すると Windowz が勝手に前へ出る。
    /// </summary>
    internal static void ForegroundEvent_UnrelatedAppTookForeground_IsSkipped()
    {
        bool process = ForegroundActivationPolicy.ShouldProcessForegroundEvent(
            foregroundMatchesEvent: false,
            eventMatchesManagedTab: true,
            foregroundIsWindowz: false,
            foregroundIsActiveManagedWindow: false,
            eventAgeMs: 30);

        Assert(!process,
            "無関係なアプリが前景を取った後の managed イベントは破棄されるべき " +
            "(ユーザーはもうそのアプリを見ていない)。");
    }

    /// <summary>
    /// managed タブに紐づかないウィンドウの古いイベントは従来どおり破棄する。
    /// (最小化中の Windowz を古いイベントで復元してタスクバー操作が反転するのを防ぐ既存動作)
    /// </summary>
    internal static void ForegroundEvent_NonManagedStaleEvent_IsSkipped()
    {
        bool process = ForegroundActivationPolicy.ShouldProcessForegroundEvent(
            foregroundMatchesEvent: false,
            eventMatchesManagedTab: false,
            foregroundIsWindowz: true,
            foregroundIsActiveManagedWindow: false,
            eventAgeMs: 10);

        Assert(!process,
            "managed タブに紐づかないウィンドウの stale イベントは破棄されるべき。");
    }

    /// <summary>前景がイベントの HWND と一致していれば常に処理する (通常経路)。</summary>
    internal static void ForegroundEvent_ForegroundMatchesEvent_IsAlwaysProcessed()
    {
        Assert(
            ForegroundActivationPolicy.ShouldProcessForegroundEvent(
                foregroundMatchesEvent: true,
                eventMatchesManagedTab: true,
                foregroundIsWindowz: false,
                foregroundIsActiveManagedWindow: false,
                eventAgeMs: 5),
            "前景とイベントが一致する managed イベントは処理されるべき。");

        Assert(
            ForegroundActivationPolicy.ShouldProcessForegroundEvent(
                foregroundMatchesEvent: true,
                eventMatchesManagedTab: false,
                foregroundIsWindowz: false,
                foregroundIsActiveManagedWindow: false,
                eventAgeMs: 5000),
            "前景と一致していれば、経過時間や managed 判定に関わらず処理されるべき。");
    }

    /// <summary>
    /// 十分に古い managed イベントは「奪い返し」ではなく本当に陳腐化したものとして破棄する。
    /// これがないと、数秒前のイベントで勝手にタブが切り替わりうる。
    /// </summary>
    internal static void ForegroundEvent_OldManagedEvent_IsNotRevived()
    {
        bool process = ForegroundActivationPolicy.ShouldProcessForegroundEvent(
            foregroundMatchesEvent: false,
            eventMatchesManagedTab: true,
            foregroundIsWindowz: false,
            foregroundIsActiveManagedWindow: true,
            eventAgeMs: ForegroundActivationPolicy.StaleManagedEventReviveWindowMs + 1);

        Assert(!process,
            "復活ウィンドウを超えて古い managed イベントは破棄されるべき。");
    }

    /// <summary>復活ウィンドウの境界値 (ちょうど上限) は処理する。</summary>
    internal static void ForegroundEvent_ManagedEventAtReviveBoundary_IsProcessed()
    {
        bool process = ForegroundActivationPolicy.ShouldProcessForegroundEvent(
            foregroundMatchesEvent: false,
            eventMatchesManagedTab: true,
            foregroundIsWindowz: false,
            foregroundIsActiveManagedWindow: true,
            eventAgeMs: ForegroundActivationPolicy.StaleManagedEventReviveWindowMs);

        Assert(process, "復活ウィンドウの境界値ちょうどのイベントは処理されるべき。");
    }

    // ─────────────────────────────────────────
    // 昇格の中止判定 (ShouldAbortPromotion)
    // ─────────────────────────────────────────

    /// <summary>
    /// 別の managed タブのウィンドウが前景なら、昇格 (＝旧タブを前面に戻す処理) を中止する。
    /// 中止しないと、ユーザーがタスクバーで選んだアプリが再最小化されてしまう。
    /// </summary>
    internal static void Promotion_OtherManagedWindowInForeground_IsAborted()
    {
        bool abort = ForegroundActivationPolicy.ShouldAbortPromotion(
            foregroundIsWindowz: false,
            foregroundIsPromotionTarget: false,
            foregroundIsOtherManagedWindow: true);

        Assert(abort, "別の managed ウィンドウが前景なら昇格は中止されるべき。");
    }

    /// <summary>昇格対象自身が前景なら中止しない (通常の成功パス)。</summary>
    internal static void Promotion_TargetInForeground_IsNotAborted()
    {
        bool abort = ForegroundActivationPolicy.ShouldAbortPromotion(
            foregroundIsWindowz: false,
            foregroundIsPromotionTarget: true,
            foregroundIsOtherManagedWindow: false);

        Assert(!abort, "昇格対象自身が前景なら中止しないべき。");
    }

    /// <summary>Windowz 本体が前景なら中止しない (Activated 起点の通常昇格)。</summary>
    internal static void Promotion_WindowzInForeground_IsNotAborted()
    {
        bool abort = ForegroundActivationPolicy.ShouldAbortPromotion(
            foregroundIsWindowz: true,
            foregroundIsPromotionTarget: false,
            foregroundIsOtherManagedWindow: false);

        Assert(!abort, "Windowz 本体が前景なら昇格は継続されるべき。");
    }

    /// <summary>
    /// 無関係なアプリが前景の場合は中止しない。長時間アイドルだったアプリが
    /// 応答するまで前景が定まらないため、リトライを続ける必要がある。
    /// </summary>
    internal static void Promotion_UnrelatedAppInForeground_IsNotAborted()
    {
        bool abort = ForegroundActivationPolicy.ShouldAbortPromotion(
            foregroundIsWindowz: false,
            foregroundIsPromotionTarget: false,
            foregroundIsOtherManagedWindow: false);

        Assert(!abort,
            "非 managed ウィンドウが前景でも、アイドル復帰リトライを止めないべき。");
    }

    /// <summary>
    /// 昇格対象のダイアログ等 (同一ウィンドウグループ) は「別 managed ウィンドウ」より優先し、
    /// 中止しない。ダイアログ表示中に昇格が止まると再前面化が効かなくなる。
    /// </summary>
    internal static void Promotion_TargetGroupWindowWins_OverOtherManagedFlag()
    {
        bool abort = ForegroundActivationPolicy.ShouldAbortPromotion(
            foregroundIsWindowz: false,
            foregroundIsPromotionTarget: true,
            foregroundIsOtherManagedWindow: true);

        Assert(!abort, "昇格対象と同一グループのウィンドウが前景なら中止しないべき。");
    }

    // ─────────────────────────────────────────
    // 復元直後の猶予 (IsWithinRestoreGrace)
    // ─────────────────────────────────────────

    /// <summary>
    /// 回帰テスト: WPF は 1 回の復元で Activated を複数回発火する。
    /// ワンショットのフラグだと 2 回目の Activated が「タスクバー再クリック」と
    /// 誤判定され、復元⇄最小化のフラッピングが起きていた
    /// (activation.log 54811640〜54811953: 313ms で 3 往復)。
    /// 猶予時間内なら何回 Activated が来ても再クリック扱いにしない。
    /// </summary>
    internal static void RestoreGrace_MultipleActivatedWithinGrace_AllSuppressed()
    {
        long restoredAt = 1_000_000;

        Assert(ForegroundActivationPolicy.IsWithinRestoreGrace(restoredAt, restoredAt),
            "復元と同時刻の Activated は再クリック扱いされないべき。");
        Assert(ForegroundActivationPolicy.IsWithinRestoreGrace(restoredAt + 60, restoredAt),
            "復元 60ms 後の 2 回目の Activated も再クリック扱いされないべき。");
        Assert(ForegroundActivationPolicy.IsWithinRestoreGrace(restoredAt + 200, restoredAt),
            "復元 200ms 後の Activated も再クリック扱いされないべき。");
    }

    /// <summary>猶予時間が過ぎれば、本来のタスクバー再クリックによる最小化は有効。</summary>
    internal static void RestoreGrace_AfterGraceElapsed_ReclickIsAllowed()
    {
        long restoredAt = 1_000_000;

        Assert(!ForegroundActivationPolicy.IsWithinRestoreGrace(
                   restoredAt + ForegroundActivationPolicy.RestoreGraceMs, restoredAt),
            "猶予時間ちょうどで猶予は終了すべき。");
        Assert(!ForegroundActivationPolicy.IsWithinRestoreGrace(restoredAt + 3000, restoredAt),
            "十分時間が経った後のタスクバー再クリックは最小化として扱われるべき。");
    }

    /// <summary>一度も復元していない状態では猶予は効かない。</summary>
    internal static void RestoreGrace_NeverRestored_IsNotSuppressed()
    {
        Assert(!ForegroundActivationPolicy.IsWithinRestoreGrace(1_000_000, 0),
            "復元記録がない場合は猶予なしと判定されるべき。");
        Assert(!ForegroundActivationPolicy.IsWithinRestoreGrace(1_000_000, -1),
            "不正な復元時刻は猶予なしと判定されるべき。");
    }

    // ─────────────────────────────────────────
    // シナリオテスト
    // ─────────────────────────────────────────

    /// <summary>
    /// 実機ログの失敗シナリオをそのまま再現する。
    ///
    ///   1. Windowz は Builder タブを表示中 (Editor タブは最小化されて非表示)
    ///   2. ユーザーが Editor のタスクバーボタンをクリック → Editor が前景化 (イベント発生)
    ///   3. イベントが Dispatcher に届く前に、Windowz の昇格処理が Builder を前景へ戻す
    ///   4. Editor の前景化イベントが届く
    ///
    /// 修正前: 3 のせいで 4 が stale 破棄され、タブが Editor に切り替わらなかった。
    /// 修正後: 4 は処理され、かつ 3 の昇格自体が中止される。
    /// </summary>
    internal static void Scenario_TaskbarClickOnHiddenManagedApp_SwitchesTab()
    {
        // 3: Editor が前景を取っている時点で Builder の昇格要求が走ろうとする
        bool promotionAborted = ForegroundActivationPolicy.ShouldAbortPromotion(
            foregroundIsWindowz: false,
            foregroundIsPromotionTarget: false,   // 昇格対象は Builder、前景は Editor
            foregroundIsOtherManagedWindow: true); // Editor は別の managed タブ

        Assert(promotionAborted,
            "タスクバーで選ばれた別 managed アプリが前景なら、旧タブの昇格は中止されるべき。");

        // 4: 昇格が先に走ってしまい Builder が前景を奪い返した場合でも、イベントは処理される
        bool eventProcessed = ForegroundActivationPolicy.ShouldProcessForegroundEvent(
            foregroundMatchesEvent: false,        // 前景は Builder、イベントは Editor
            eventMatchesManagedTab: true,         // Editor は managed タブ
            foregroundIsWindowz: false,
            foregroundIsActiveManagedWindow: true, // Builder は現アクティブタブのウィンドウ
            eventAgeMs: 45);

        Assert(eventProcessed,
            "昇格に先を越されても、Editor の前景化イベントは処理されてタブが切り替わるべき。");
    }

    // ─────────────────────────────────────────
    // 同時表示タブの判定 (TabManager.IsCoVisibleWithActiveTab)
    // ─────────────────────────────────────────

    /// <summary>
    /// タイル表示中は複数の managed ウィンドウが同時に見えている。メンバー間で
    /// フォアグラウンドが移っても「別アプリが選ばれた」ではないので、昇格を中止しない。
    /// </summary>
    internal static void CoVisible_TileMember_IsCoVisibleWithActiveTab()
    {
        using var scope = new TempSettingsScope();
        var mgr = CreateTabManager(scope.Manager);
        try
        {
            var a = ContentTab("A");
            var b = ContentTab("B");
            var outside = ContentTab("Outside");
            mgr.Tabs.Add(a);
            mgr.Tabs.Add(b);
            mgr.Tabs.Add(outside);

            mgr.TileSpecificTabs(new[] { a, b });
            mgr.ActiveTab = a;

            Assert(mgr.IsCoVisibleWithActiveTab(b),
                "同一タイルグループのタブは同時表示中として扱われるべき。");
            Assert(!mgr.IsCoVisibleWithActiveTab(outside),
                "タイル外のタブは同時表示中ではないべき。");
        }
        finally { mgr.StopCleanupTimer(); }
    }

    /// <summary>ピン留め側のタブはアクティブタブと同時に表示されている。</summary>
    internal static void CoVisible_PinnedTab_IsCoVisibleWithActiveTab()
    {
        using var scope = new TempSettingsScope();
        var mgr = CreateTabManager(scope.Manager);
        try
        {
            var pinned = ContentTab("Pinned");
            var active = ContentTab("Active");
            var other = ContentTab("Other");
            mgr.Tabs.Add(pinned);
            mgr.Tabs.Add(active);
            mgr.Tabs.Add(other);

            mgr.ActiveTab = active;
            mgr.PinTab(pinned, Models.PinnedSide.Left);

            Assert(mgr.IsCoVisibleWithActiveTab(pinned),
                "ピン留めタブはアクティブタブと同時表示中として扱われるべき。");
            Assert(!mgr.IsCoVisibleWithActiveTab(other),
                "ピン留めでもタイルでもないタブは同時表示中ではないべき。");
        }
        finally { mgr.StopCleanupTimer(); }
    }

    /// <summary>
    /// 通常のシングル表示では、別タブは同時表示されていない。
    /// ＝タスクバーで選ばれたら切り替えるべき対象になる。
    /// </summary>
    internal static void CoVisible_SingleWindowMode_OtherTabIsNotCoVisible()
    {
        using var scope = new TempSettingsScope();
        var mgr = CreateTabManager(scope.Manager);
        try
        {
            var builder = ContentTab("Builder");
            var editor = ContentTab("Editor");
            mgr.Tabs.Add(builder);
            mgr.Tabs.Add(editor);
            mgr.ActiveTab = builder;

            Assert(!mgr.IsCoVisibleWithActiveTab(editor),
                "シングル表示では非アクティブタブは同時表示中ではないべき " +
                "(タスクバークリックで切り替える対象)。");
        }
        finally { mgr.StopCleanupTimer(); }
    }

    // ─────────────────────────────────────────
    // ヘルパー
    // ─────────────────────────────────────────

    private static Services.TabManager CreateTabManager(SettingsManager settingsManager) =>
        new(new Services.WindowManager(settingsManager), settingsManager, null!);

    private static Models.TabItem ContentTab(string title) =>
        new() { Title = title, ContentKey = title };

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

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
