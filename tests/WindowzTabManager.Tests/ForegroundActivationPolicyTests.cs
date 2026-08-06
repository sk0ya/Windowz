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

    internal static void ForegroundEvent_WindowzAfterTaskbarClick_IsProcessedWhenForegroundAlreadyMoved()
    {
        bool process = ForegroundActivationPolicy.ShouldProcessForegroundEvent(
            foregroundMatchesEvent: false,
            eventMatchesManagedTab: false,
            eventIsWindowz: true,
            followsRecentTaskbarClick: true,
            foregroundIsWindowz: false,
            foregroundIsActiveManagedWindow: true,
            eventAgeMs: 80);

        Assert(process,
            "タスクバークリック由来の Windowz 前景イベントは、managed window へ前景を渡した後でも処理されるべき。");
    }

    internal static void ForegroundEvent_NonWindowzAfterTaskbarClick_IsSkipped()
    {
        bool process = ForegroundActivationPolicy.ShouldProcessForegroundEvent(
            foregroundMatchesEvent: false,
            eventMatchesManagedTab: false,
            eventIsWindowz: false,
            followsRecentTaskbarClick: true,
            foregroundIsWindowz: false,
            foregroundIsActiveManagedWindow: false,
            eventAgeMs: 20);

        Assert(!process, "タスクバークリックは無関係な stale イベントを復活させないべき。");
    }

    internal static void ForegroundEvent_WindowzAfterTaskbarClickButUnrelatedAppIsForeground_IsSkipped()
    {
        bool process = ForegroundActivationPolicy.ShouldProcessForegroundEvent(
            foregroundMatchesEvent: false,
            eventMatchesManagedTab: false,
            eventIsWindowz: true,
            followsRecentTaskbarClick: true,
            foregroundIsWindowz: false,
            foregroundIsActiveManagedWindow: false,
            eventAgeMs: 20);

        Assert(!process,
            "クリック後にユーザーが無関係なアプリへ移った場合は Windowz を前面へ戻さないべき。");
    }

    internal static void ForegroundEvent_WindowzWithoutRecentTaskbarClick_IsSkipped()
    {
        bool process = ForegroundActivationPolicy.ShouldProcessForegroundEvent(
            foregroundMatchesEvent: false,
            eventMatchesManagedTab: false,
            eventIsWindowz: true,
            followsRecentTaskbarClick: false,
            foregroundIsWindowz: false,
            foregroundIsActiveManagedWindow: true,
            eventAgeMs: 20);

        Assert(!process, "ユーザー操作と相関できない Windowz の stale イベントは破棄されるべき。");
    }

    internal static void TaskbarCorrelation_BoundariesAndInvalidOrder_AreHandled()
    {
        const long clickTick = 10_000;

        Assert(ForegroundActivationPolicy.FollowsRecentTaskbarClick(clickTick, clickTick),
            "クリックと同時刻のイベントは相関するべき。");
        Assert(ForegroundActivationPolicy.FollowsRecentTaskbarClick(
                clickTick + ForegroundActivationPolicy.TaskbarClickCorrelationMs,
                clickTick),
            "相関時間窓の上限ちょうどは相関するべき。");
        Assert(!ForegroundActivationPolicy.FollowsRecentTaskbarClick(
                clickTick + ForegroundActivationPolicy.TaskbarClickCorrelationMs + 1,
                clickTick),
            "相関時間窓を超えたイベントは相関しないべき。");
        Assert(!ForegroundActivationPolicy.FollowsRecentTaskbarClick(clickTick - 1, clickTick),
            "クリックより前のイベントは相関しないべき。");
        Assert(!ForegroundActivationPolicy.FollowsRecentTaskbarClick(clickTick, 0),
            "クリック未記録時は相関しないべき。");
    }

    /// <summary>
    /// タスクバークリック相関で復活させる Windowz イベントにも上限を設ける。
    /// Dispatcher に数秒滞留したイベントで Windowz が勝手に前へ出てはいけない。
    /// </summary>
    internal static void ForegroundEvent_StaleWindowzAfterTaskbarClick_IsNotRevived()
    {
        bool process = ForegroundActivationPolicy.ShouldProcessForegroundEvent(
            foregroundMatchesEvent: false,
            eventMatchesManagedTab: false,
            eventIsWindowz: true,
            followsRecentTaskbarClick: true,
            foregroundIsWindowz: false,
            foregroundIsActiveManagedWindow: true,
            eventAgeMs: ForegroundActivationPolicy.TaskbarClickCorrelationMs + 1);

        Assert(!process,
            "タスクバークリックと相関していても、上限を超えて滞留した " +
            "Windowz イベントは破棄されるべき。");
    }

    /// <summary>Windowz イベントの復活は境界値ちょうどまで許可する。</summary>
    internal static void ForegroundEvent_WindowzAfterTaskbarClickAtReviveBoundary_IsProcessed()
    {
        bool process = ForegroundActivationPolicy.ShouldProcessForegroundEvent(
            foregroundMatchesEvent: false,
            eventMatchesManagedTab: false,
            eventIsWindowz: true,
            followsRecentTaskbarClick: true,
            foregroundIsWindowz: false,
            foregroundIsActiveManagedWindow: true,
            eventAgeMs: ForegroundActivationPolicy.TaskbarClickCorrelationMs);

        Assert(process, "上限ちょうどまで滞留した Windowz イベントは処理されるべき。");
    }

    /// <summary>
    /// 回帰テスト: Windowz 前景イベントの復活上限は managed 経路より緩い。
    ///
    /// この経路のイベントは、昇格処理が応答の遅い managed プロセスを待つ間
    /// (最大 1000ms のリトライ) に滞留しやすい。managed 経路と同じ 750ms で
    /// 打ち切ると、タスクバーで Windowz を選んでも前へ出てこなくなる。
    /// 復活条件が「前景が Windowz か現アクティブタブ」なので、第三のアプリから
    /// 前景を奪う危険はない。
    /// </summary>
    internal static void ForegroundEvent_WindowzAfterTaskbarClick_ToleratesLongerDelayThanManaged()
    {
        const long ageMs = ForegroundActivationPolicy.StaleManagedEventReviveWindowMs + 200;

        Assert(ageMs < ForegroundActivationPolicy.TaskbarClickCorrelationMs,
            "テスト前提: managed の上限と相関窓の間にある経過時間を使う。");

        Assert(
            ForegroundActivationPolicy.ShouldProcessForegroundEvent(
                foregroundMatchesEvent: false,
                eventMatchesManagedTab: false,
                eventIsWindowz: true,
                followsRecentTaskbarClick: true,
                foregroundIsWindowz: false,
                foregroundIsActiveManagedWindow: true,
                eventAgeMs: ageMs),
            "昇格処理で UI スレッドが塞がって滞留した Windowz イベントも処理されるべき。");

        Assert(
            !ForegroundActivationPolicy.ShouldProcessForegroundEvent(
                foregroundMatchesEvent: false,
                eventMatchesManagedTab: true,
                eventIsWindowz: false,
                followsRecentTaskbarClick: true,
                foregroundIsWindowz: false,
                foregroundIsActiveManagedWindow: true,
                eventAgeMs: ageMs),
            "managed 経路は従来どおり短い上限で打ち切られるべき。");
    }

    /// <summary>前景一致イベントは経過時間の上限を受けない (通常経路を塞がない)。</summary>
    internal static void ForegroundEvent_MatchingEvent_IsNotAffectedByReviveWindow()
    {
        bool process = ForegroundActivationPolicy.ShouldProcessForegroundEvent(
            foregroundMatchesEvent: true,
            eventMatchesManagedTab: true,
            eventIsWindowz: false,
            followsRecentTaskbarClick: false,
            foregroundIsWindowz: false,
            foregroundIsActiveManagedWindow: false,
            eventAgeMs: 60_000);

        Assert(process,
            "到着時の前景とイベントが一致していれば、どれだけ古くても処理されるべき。");
    }

    // ─────────────────────────────────────────
    // タスクバー再クリックによる最小化 (EvaluateTaskbarMinimize)
    //
    // 既定値は「最小化する」状態。各テストは 1 条件だけ崩して、
    // その条件が最小化を止めることを確認する。
    // ─────────────────────────────────────────

    /// <summary>
    /// 通常経路: 表示中の managed アプリのタスクバーボタンを再クリックしたら、
    /// Windowz ごと最小化する。
    /// </summary>
    internal static void TaskbarMinimize_ReclickOnVisibleManagedApp_Minimizes()
    {
        Assert(EvaluateMinimize() == ForegroundActivationPolicy.TaskbarMinimizeSkipReason.None,
            "表示中の managed アプリのタスクバー再クリックは Windowz を最小化すべき。");
    }

    /// <summary>
    /// 回帰テスト: カーソルをタスクバー上に置いたまま Alt+Tab やホットキーで
    /// Windowz を前面化した場合、最小化してはいけない。
    ///
    /// 修正前: 判定がポインタ位置だけだったため、クリックしていないのに
    ///         「タスクバー再クリック」と誤判定し、アクティブにしたつもりの
    ///         ウィンドウがそのまま引っ込んでいた。
    /// </summary>
    internal static void TaskbarMinimize_PointerOnTaskbarWithoutClick_IsNotMinimized()
    {
        var reason = EvaluateMinimize(followsRecentTaskbarClick: false);

        Assert(reason == ForegroundActivationPolicy.TaskbarMinimizeSkipReason.NoRecentTaskbarClick,
            $"クリックと相関しない前面化は最小化しないべき。実際: {reason}");
    }

    /// <summary>クリックが相関時間窓を超えて古い場合も、その前面化は別要因。</summary>
    internal static void TaskbarMinimize_ClickOutsideCorrelationWindow_IsNotMinimized()
    {
        const long clickTick = 500_000;
        long nowTick = clickTick + ForegroundActivationPolicy.TaskbarClickCorrelationMs + 1;

        var reason = EvaluateMinimize(
            followsRecentTaskbarClick:
                ForegroundActivationPolicy.FollowsRecentTaskbarClick(nowTick, clickTick),
            nowTick: nowTick);

        Assert(reason == ForegroundActivationPolicy.TaskbarMinimizeSkipReason.NoRecentTaskbarClick,
            $"相関時間窓を過ぎたクリックでは最小化しないべき。実際: {reason}");
    }

    /// <summary>ポインタがタスクバー上にない前面化は再クリックではない。</summary>
    internal static void TaskbarMinimize_PointerNotOnTaskbar_IsNotMinimized()
    {
        var reason = EvaluateMinimize(pointerOnTaskbar: false);

        Assert(reason == ForegroundActivationPolicy.TaskbarMinimizeSkipReason.PointerNotOnTaskbar,
            $"ポインタがタスクバー上にないなら最小化しないべき。実際: {reason}");
    }

    /// <summary>
    /// 直前の前景が無関係なアプリ (Chrome 等) なら、これは「非表示アプリを選んで
    /// 前面化した」操作であって再クリックではない。ここで最小化すると、
    /// ユーザーが選んだアプリが即座に引っ込む。
    /// </summary>
    internal static void TaskbarMinimize_LastForegroundWasUnrelatedApp_IsNotMinimized()
    {
        var reason = EvaluateMinimize(visibleManagedAppWasActiveAtClick: false);

        Assert(reason == ForegroundActivationPolicy.TaskbarMinimizeSkipReason.OtherAppWasActiveAtClick,
            $"直前の前景が表示中の managed ウィンドウでないなら最小化しないべき。実際: {reason}");
    }

    /// <summary>
    /// 回帰テスト: 復元直後の Activated は再クリックではない。
    /// WPF は 1 回の復元で Activated を複数回発火するため、猶予時間内は抑止する。
    /// </summary>
    internal static void TaskbarMinimize_JustRestoredFromMinimize_IsNotMinimized()
    {
        const long restoredAt = 800_000;

        Assert(EvaluateMinimize(nowTick: restoredAt, restoredFromMinimizeTick: restoredAt)
                   == ForegroundActivationPolicy.TaskbarMinimizeSkipReason.JustRestored,
            "復元と同時刻の Activated は最小化しないべき。");
        Assert(EvaluateMinimize(nowTick: restoredAt + 200, restoredFromMinimizeTick: restoredAt)
                   == ForegroundActivationPolicy.TaskbarMinimizeSkipReason.JustRestored,
            "猶予時間内の 2 回目以降の Activated も最小化しないべき (フラッピング防止)。");
    }

    /// <summary>猶予が明けたあとの再クリックは、本来どおり最小化する。</summary>
    internal static void TaskbarMinimize_AfterRestoreGraceElapsed_Minimizes()
    {
        const long restoredAt = 800_000;
        long nowTick = restoredAt + ForegroundActivationPolicy.RestoreGraceMs;

        Assert(EvaluateMinimize(nowTick: nowTick, restoredFromMinimizeTick: restoredAt)
                   == ForegroundActivationPolicy.TaskbarMinimizeSkipReason.None,
            "猶予時間を過ぎた再クリックは最小化すべき。");
    }

    /// <summary>
    /// 設定タブ / Web タブ表示中は managed ウィンドウを表示していないので、
    /// タスクバー操作で Windowz を最小化しない (現仕様の固定)。
    /// </summary>
    internal static void TaskbarMinimize_ContentOrWebTabActive_IsNotMinimized()
    {
        var reason = EvaluateMinimize(contentOrWebTabActive: true);

        Assert(reason == ForegroundActivationPolicy.TaskbarMinimizeSkipReason.ContentOrWebTab,
            $"設定 / Web タブ表示中は最小化しないべき。実際: {reason}");
    }

    /// <summary>アクティブタブに managed ウィンドウがなければ再クリック判定は成立しない。</summary>
    internal static void TaskbarMinimize_NoManagedWindow_IsNotMinimized()
    {
        var reason = EvaluateMinimize(hasActiveManagedWindow: false);

        Assert(reason == ForegroundActivationPolicy.TaskbarMinimizeSkipReason.NoManagedWindow,
            $"managed ウィンドウがないなら最小化しないべき。実際: {reason}");
    }

    /// <summary>タブドラッグ中・オーバーレイ表示中は前面化の調停自体を止める。</summary>
    internal static void TaskbarMinimize_Suppressed_IsNotMinimized()
    {
        var reason = EvaluateMinimize(suppressed: true);

        Assert(reason == ForegroundActivationPolicy.TaskbarMinimizeSkipReason.Suppressed,
            $"調停停止中は最小化しないべき。実際: {reason}");
    }

    /// <summary>
    /// 複数条件が同時に崩れているときの判定順序を固定する。
    /// ログに出る理由が安定しないと、実機ログからの原因追跡ができなくなる。
    /// </summary>
    internal static void TaskbarMinimize_SkipReasonPriority_IsStable()
    {
        Assert(EvaluateMinimize(
                   suppressed: true,
                   contentOrWebTabActive: true,
                   hasActiveManagedWindow: false,
                   visibleManagedAppWasActiveAtClick: false,
                   pointerOnTaskbar: false,
                   followsRecentTaskbarClick: false)
               == ForegroundActivationPolicy.TaskbarMinimizeSkipReason.Suppressed,
            "調停停止が最優先で報告されるべき。");

        Assert(EvaluateMinimize(
                   contentOrWebTabActive: true,
                   hasActiveManagedWindow: false,
                   pointerOnTaskbar: false)
               == ForegroundActivationPolicy.TaskbarMinimizeSkipReason.ContentOrWebTab,
            "content / web タブは managed ウィンドウ有無より先に報告されるべき。");

        Assert(EvaluateMinimize(
                   hasActiveManagedWindow: false,
                   visibleManagedAppWasActiveAtClick: false,
                   pointerOnTaskbar: false,
                   nowTick: 800_000,
                   restoredFromMinimizeTick: 800_000)
               == ForegroundActivationPolicy.TaskbarMinimizeSkipReason.JustRestored,
            "復元直後は他のどの条件より先に報告されるべき。");
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

    /// <summary>
    /// 回帰テスト: カーソルがタスクバー上にある状態で Alt+Tab / ホットキーから
    /// Windowz を前面化するシナリオ。
    ///
    ///   1. managed アプリが前景 (= _lastNonTaskbarForegroundWindow はそのウィンドウ)
    ///   2. ユーザーがカーソルをタスクバー上に置いたまま Alt+Tab で Windowz を選ぶ
    ///   3. Activated が発火する
    ///
    /// 修正前: 1 と「ポインタがタスクバー上」だけで再クリックと判定し、最小化していた。
    /// 修正後: 直近のタスクバークリックと相関しないため最小化しない。
    /// </summary>
    internal static void Scenario_AltTabWhileCursorRestsOnTaskbar_DoesNotMinimize()
    {
        const long lastClickTick = 100_000;
        // 直近のタスクバークリックはずっと前 (相関窓の外)
        long nowTick = lastClickTick + ForegroundActivationPolicy.TaskbarClickCorrelationMs + 5_000;

        var reason = EvaluateMinimize(
            visibleManagedAppWasActiveAtClick: true,  // 1: managed アプリが前景だった
            pointerOnTaskbar: true,                      // 2: カーソルはたまたまタスクバー上
            followsRecentTaskbarClick:
                ForegroundActivationPolicy.FollowsRecentTaskbarClick(nowTick, lastClickTick),
            nowTick: nowTick);

        Assert(reason == ForegroundActivationPolicy.TaskbarMinimizeSkipReason.NoRecentTaskbarClick,
            $"Alt+Tab での前面化を再クリック最小化と誤判定してはいけない。実際: {reason}");
    }

    /// <summary>
    /// 回帰テスト: managed ではないアプリのタスクバーボタンをクリックしたシナリオ。
    ///
    ///   1. 非 managed アプリ (Chrome 等) が前面で、その裏に Windowz + managed アプリ
    ///   2. ユーザーが Chrome のタスクバーボタンをクリックして Chrome を引っ込める
    ///   3. Chrome が最小化され、裏にいた managed ウィンドウが前景に上がる
    ///   4. Windowz に Activated が届く
    ///
    /// 修正前: 4 の時点で「直前の前景」を見ていたため、3 で上がってきた managed
    ///         ウィンドウを「managed アプリがアクティブだった」と解釈し、
    ///         全条件が成立して Windowz まで一緒に引っ込んでいた。
    ///         Chrome をどかして裏を見ようとしたのに、裏ごと消える挙動になる。
    /// 修正後: クリックした瞬間にアクティブだったのは Chrome なので最小化しない。
    /// </summary>
    internal static void Scenario_TaskbarClickOnNonManagedApp_DoesNotMinimizeWindowz()
    {
        // 2 の時点でアクティブだったのは Chrome (managed ではない)
        var reason = EvaluateMinimize(
            visibleManagedAppWasActiveAtClick: false,
            pointerOnTaskbar: true,           // クリックしたのでカーソルはタスクバー上
            followsRecentTaskbarClick: true); // 実際にクリックしている

        Assert(reason == ForegroundActivationPolicy.TaskbarMinimizeSkipReason.OtherAppWasActiveAtClick,
            $"非 managed アプリのタスクバークリックで Windowz を最小化してはいけない。実際: {reason}");
    }

    /// <summary>
    /// 上のシナリオとの対比: クリック時点でアクティブだったのが表示中の managed
    /// アプリなら、同じ「クリック後に managed ウィンドウが前景」という状態でも
    /// 最小化する。判定を分けているのがクリック時点の情報だけであることを示す。
    /// </summary>
    internal static void Scenario_TaskbarReclickOnActiveManagedApp_MinimizesWindowz()
    {
        var reason = EvaluateMinimize(
            visibleManagedAppWasActiveAtClick: true,
            pointerOnTaskbar: true,
            followsRecentTaskbarClick: true);

        Assert(reason == ForegroundActivationPolicy.TaskbarMinimizeSkipReason.None,
            $"表示中の managed アプリの再クリックは最小化すべき。実際: {reason}");
    }

    /// <summary>
    /// 回帰テスト: タイル表示中に、アクティブタブではない側のスロットを触ってから
    /// そのアプリのタスクバーボタンを再クリックしたシナリオ。
    ///
    /// 修正前: 直前の前景をアクティブタブのウィンドウとだけ突き合わせていたため、
    ///         タイルの別スロットだと不一致になり最小化が効かなかった。
    /// 修正後: 同時表示中のウィンドウも「今表示している managed ウィンドウ」に含める。
    /// </summary>
    internal static void Scenario_TaskbarReclickOnTileMember_MinimizesWindowz()
    {
        using var scope = new TempSettingsScope();
        var mgr = CreateTabManager(scope.Manager);
        try
        {
            var active = ContentTab("Active");
            var member = ContentTab("TileMember");
            var outside = ContentTab("Outside");
            mgr.Tabs.Add(active);
            mgr.Tabs.Add(member);
            mgr.Tabs.Add(outside);

            mgr.TileSpecificTabs(new[] { active, member });
            mgr.ActiveTab = active;

            // MainWindow.IsLastForegroundVisibleManagedWindow と同じ合成:
            // アクティブタブ本体でなくても、同時表示中なら「表示中」とみなす。
            bool memberIsVisible = mgr.IsCoVisibleWithActiveTab(member);
            Assert(memberIsVisible, "タイルの別スロットは同時表示中と判定されるべき。");

            Assert(EvaluateMinimize(visibleManagedAppWasActiveAtClick: memberIsVisible)
                       == ForegroundActivationPolicy.TaskbarMinimizeSkipReason.None,
                "タイルの別スロットのアプリを再クリックしても Windowz は最小化されるべき。");

            // タイル外のタブは同時表示されていないので、そちらの前面化は切り替え操作。
            bool outsideIsVisible = mgr.IsCoVisibleWithActiveTab(outside);
            Assert(EvaluateMinimize(visibleManagedAppWasActiveAtClick: outsideIsVisible)
                       == ForegroundActivationPolicy.TaskbarMinimizeSkipReason.OtherAppWasActiveAtClick,
                "タイル外のアプリからの前面化は最小化ではなくタブ切り替えとして扱うべき。");
        }
        finally { mgr.StopCleanupTimer(); }
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

    /// <summary>
    /// <see cref="ForegroundActivationPolicy.EvaluateTaskbarMinimize"/> の呼び出しヘルパー。
    /// 既定値は「表示中の managed アプリのタスクバーボタンを再クリックした」状態
    /// (＝最小化する) なので、各テストは検証したい条件だけを崩して渡す。
    /// </summary>
    private static ForegroundActivationPolicy.TaskbarMinimizeSkipReason EvaluateMinimize(
        bool suppressed = false,
        bool contentOrWebTabActive = false,
        bool hasActiveManagedWindow = true,
        bool visibleManagedAppWasActiveAtClick = true,
        bool pointerOnTaskbar = true,
        bool followsRecentTaskbarClick = true,
        long nowTick = 1_000_000,
        long restoredFromMinimizeTick = 0)
    {
        return ForegroundActivationPolicy.EvaluateTaskbarMinimize(
            suppressed,
            contentOrWebTabActive,
            hasActiveManagedWindow,
            visibleManagedAppWasActiveAtClick,
            pointerOnTaskbar,
            followsRecentTaskbarClick,
            nowTick,
            restoredFromMinimizeTick);
    }

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
