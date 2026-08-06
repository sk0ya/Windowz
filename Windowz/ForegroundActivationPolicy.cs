namespace WindowzTabManager;

/// <summary>
/// フォアグラウンド変化イベントとタスクバー操作の判定ロジック。
/// <para>
/// MainWindow から Win32 / WPF 依存を切り離して単体テストできるようにするため、
/// 判定だけをここへ抽出している。呼び出し側が GetForegroundWindow 等で収集した
/// 事実を引数で渡す。
/// </para>
/// </summary>
public static class ForegroundActivationPolicy
{
    /// <summary>
    /// 最小化からの復元直後、タスクバー再クリック (＝最小化操作) とみなさない猶予時間。
    /// WPF は復元時に Activated を複数回発火するため、ワンショットのフラグでは
    /// 2回目以降が「再クリック」と誤判定されて復元⇄最小化のフラッピングが起きる。
    /// </summary>
    public const int RestoreGraceMs = 400;

    /// <summary>
    /// managed ウィンドウの前景化イベントを「自分が前景を奪い返しただけ」として
    /// 復活させる上限のイベント経過時間。これを超えた古いイベントは破棄する。
    /// </summary>
    public const int StaleManagedEventReviveWindowMs = 750;

    /// <summary>
    /// タスクバークリック後に届いた Windowz の前景イベントをユーザー操作として
    /// 相関できる上限。
    /// </summary>
    public const int TaskbarClickCorrelationMs = 1500;

    /// <summary>
    /// OUTOFCONTEXT の WinEvent は Dispatcher に届くまでに古くなることがあるため、
    /// 到着時点の前景と食い違うイベントは原則破棄する。
    /// ただし managed タブのウィンドウの前景化は、ユーザーがタスクバー等で明示的に
    /// そのアプリを選んだ操作である。Windowz 自身の昇格処理 (ForceForegroundWindow) が
    /// 前景を奪い返しただけの場合に破棄すると、その選択操作が無視されてしまうため処理する。
    /// </summary>
    /// <param name="foregroundMatchesEvent">現在の前景がイベントの HWND と同一グループか。</param>
    /// <param name="eventMatchesManagedTab">イベントの HWND が managed タブに紐づくか。</param>
    /// <param name="foregroundIsWindowz">現在の前景が Windowz 本体か。</param>
    /// <param name="foregroundIsActiveManagedWindow">現在の前景が現アクティブタブの managed ウィンドウか。</param>
    /// <param name="eventAgeMs">イベント発生から現在までの経過時間 (ms)。</param>
    public static bool ShouldProcessForegroundEvent(
        bool foregroundMatchesEvent,
        bool eventMatchesManagedTab,
        bool foregroundIsWindowz,
        bool foregroundIsActiveManagedWindow,
        long eventAgeMs)
    {
        return ShouldProcessForegroundEvent(
            foregroundMatchesEvent,
            eventMatchesManagedTab,
            eventIsWindowz: false,
            followsRecentTaskbarClick: false,
            foregroundIsWindowz,
            foregroundIsActiveManagedWindow,
            eventAgeMs);
    }

    public static bool ShouldProcessForegroundEvent(
        bool foregroundMatchesEvent,
        bool eventMatchesManagedTab,
        bool eventIsWindowz,
        bool followsRecentTaskbarClick,
        bool foregroundIsWindowz,
        bool foregroundIsActiveManagedWindow,
        long eventAgeMs)
    {
        if (foregroundMatchesEvent)
            return true;

        // ここから先は「到着時の前景と食い違うイベントを復活させる」経路。
        // どの経路にも上限を設けて、Dispatcher に数秒滞留したイベントで勝手に
        // タブが切り替わったり Windowz が前へ出たりしないようにする。
        //
        // なお eventAgeMs (Dispatcher の滞留時間) と followsRecentTaskbarClick が
        // 見ている間隔 (クリック→イベント発生) は別の量である。クリックから
        // 1400ms 後に発生したイベントでも、滞留していなければ age は数 ms になる。

        // Windowz は Activated を通知しないことがある。タスクバークリック直後の
        // Windowz 前景イベントなら、到着時に managed window へ前景を渡し終えて
        // 食い違っていてもユーザー操作として処理する。
        //
        // この経路のイベントは、昇格処理が応答の遅い managed プロセスを待つ間に
        // 滞留しやすい (ManagedPromotionRetryDelaysMs は最大 1000ms 待つ)。
        // また復活の条件が「前景が Windowz か現アクティブタブ」なので、
        // 第三のアプリから前景を奪う危険がない。managed 経路より緩い上限を使う。
        if (eventIsWindowz && followsRecentTaskbarClick)
        {
            return eventAgeMs <= TaskbarClickCorrelationMs &&
                   (foregroundIsWindowz || foregroundIsActiveManagedWindow);
        }

        // managed タブに紐づかないウィンドウの古いイベントは破棄する。
        // (古いイベントで最小化中の Windowz を復元してタスクバー操作が反転するのを防ぐ)
        if (!eventMatchesManagedTab)
            return false;

        // 十分に古いイベントは、奪い返しではなく本当に陳腐化したものとして破棄する。
        if (eventAgeMs > StaleManagedEventReviveWindowMs)
            return false;

        // 前景を持っているのが Windowz 自身か現アクティブタブのウィンドウなら、
        // 前景の奪い返しは Windowz 側の昇格処理が原因。ユーザーの選択を優先する。
        return foregroundIsWindowz || foregroundIsActiveManagedWindow;
    }

    /// <summary>
    /// タスクバー再クリックによる Windowz 最小化を見送る理由。
    /// <see cref="TaskbarMinimizeSkipReason.None"/> のときだけ最小化する。
    /// </summary>
    public enum TaskbarMinimizeSkipReason
    {
        /// <summary>見送る理由なし = 最小化する。</summary>
        None = 0,

        /// <summary>タブドラッグ中やオーバーレイ表示中で、前面化の調停自体を止めている。</summary>
        Suppressed,

        /// <summary>設定タブ・Web タブ表示中で managed ウィンドウを表示していない。</summary>
        ContentOrWebTab,

        /// <summary>最小化からの復元直後。この Activated は再クリックではない。</summary>
        JustRestored,

        /// <summary>アクティブタブに managed ウィンドウがない。</summary>
        NoManagedWindow,

        /// <summary>
        /// クリックした時点でアクティブだったのが、今表示している managed アプリではない。
        /// 別アプリのタスクバーボタンを押した操作なので、Windowz は動かさない。
        /// </summary>
        OtherAppWasActiveAtClick,

        /// <summary>ポインタがタスクバー上にない。</summary>
        PointerNotOnTaskbar,

        /// <summary>タスクバーのクリックと相関しない前面化 (Alt+Tab・ホットキー等)。</summary>
        NoRecentTaskbarClick,
    }

    /// <summary>
    /// 「すでに前面にある managed アプリのタスクバーボタンを再クリックした」＝
    /// Windowz ごと最小化すべき操作か判定する。
    /// <para>
    /// Windowz と managed ウィンドウは 1 つの論理ウィンドウとして振る舞うため、
    /// managed 側のボタン再クリックでは Windowz も一緒に引っ込める必要がある。
    /// 一方で Alt+Tab やホットキーによる前面化を最小化と誤判定すると、
    /// アクティブにしたつもりのウィンドウが消える最悪の挙動になる。
    /// </para>
    /// </summary>
    /// <param name="suppressed">前面化の調停を停止中か (ドラッグ中・オーバーレイ表示中)。</param>
    /// <param name="contentOrWebTabActive">設定タブ / Web タブを表示中か。</param>
    /// <param name="hasActiveManagedWindow">アクティブタブに managed ウィンドウがあるか。</param>
    /// <param name="visibleManagedAppWasActiveAtClick">
    /// タスクバーをクリックした時点でアクティブだったのが、今表示している managed
    /// アプリか。タイル・ピン留めで同時表示中のウィンドウも「今表示している」に含める。
    /// <para>
    /// Activated 時点の前景ではなく、クリック時点のアクティブアプリで判定すること。
    /// 非 managed アプリのボタンを押してそのアプリが引っ込むと、裏の managed
    /// ウィンドウが前景に上がるため、後から見ると再クリックと区別できなくなる。
    /// </para>
    /// </param>
    /// <param name="pointerOnTaskbar">ポインタがタスクバー上にあるか。</param>
    /// <param name="followsRecentTaskbarClick">直近のタスクバークリックと相関する前面化か。</param>
    /// <param name="nowTick">現在の TickCount64。</param>
    /// <param name="restoredFromMinimizeTick">最小化から復元した時刻 (未復元なら 0)。</param>
    public static TaskbarMinimizeSkipReason EvaluateTaskbarMinimize(
        bool suppressed,
        bool contentOrWebTabActive,
        bool hasActiveManagedWindow,
        bool visibleManagedAppWasActiveAtClick,
        bool pointerOnTaskbar,
        bool followsRecentTaskbarClick,
        long nowTick,
        long restoredFromMinimizeTick)
    {
        if (suppressed)
            return TaskbarMinimizeSkipReason.Suppressed;

        if (contentOrWebTabActive)
            return TaskbarMinimizeSkipReason.ContentOrWebTab;

        // 管理対象のタスクバーボタンから復元した直後の Activated は、再クリックによる
        // 最小化ではない。WPF は 1 回の復元で Activated を複数回発火するため、
        // フラグを消費せず猶予時間内かどうかで判定する (消費すると 2 回目の Activated が
        // 再クリックと誤判定され、復元⇄最小化のフラッピングになる)。
        if (IsWithinRestoreGrace(nowTick, restoredFromMinimizeTick))
            return TaskbarMinimizeSkipReason.JustRestored;

        if (!hasActiveManagedWindow)
            return TaskbarMinimizeSkipReason.NoManagedWindow;

        // 再クリック最小化は「今表示しているアプリのボタンをもう一度押した」操作。
        // クリック時点でそのアプリがアクティブでなかったなら、別アプリのボタンを
        // 押したか、非表示のアプリを呼び出した操作であって最小化ではない。
        if (!visibleManagedAppWasActiveAtClick)
            return TaskbarMinimizeSkipReason.OtherAppWasActiveAtClick;

        if (!pointerOnTaskbar)
            return TaskbarMinimizeSkipReason.PointerNotOnTaskbar;

        // ポインタ位置だけでは、タスクバー上にカーソルを置いたまま Alt+Tab や
        // ホットキーで前面化した場合と区別できない。実際のクリックと相関させる。
        if (!followsRecentTaskbarClick)
            return TaskbarMinimizeSkipReason.NoRecentTaskbarClick;

        return TaskbarMinimizeSkipReason.None;
    }

    /// <summary>
    /// 前景イベントが直前のタスクバークリックから生じたものかを判定する。
    /// イベントより後のクリックや、未記録値は相関させない。
    /// </summary>
    public static bool FollowsRecentTaskbarClick(long eventTick, long taskbarClickTick)
    {
        if (taskbarClickTick <= 0)
            return false;

        long elapsed = eventTick - taskbarClickTick;
        return elapsed >= 0 && elapsed <= TaskbarClickCorrelationMs;
    }

    /// <summary>
    /// 昇格処理を中止すべきかを判定する。別の managed タブのウィンドウが前景を
    /// 取っている場合、ユーザーがタスクバー等でそのアプリを選んだということなので、
    /// 旧タブを前景へ引き戻してはならない (リトライ中も同じ)。
    /// </summary>
    public static bool ShouldAbortPromotion(
        bool foregroundIsWindowz,
        bool foregroundIsPromotionTarget,
        bool foregroundIsOtherManagedWindow)
    {
        if (foregroundIsWindowz || foregroundIsPromotionTarget)
            return false;

        return foregroundIsOtherManagedWindow;
    }

    /// <summary>最小化からの復元直後の猶予時間内かを判定する。</summary>
    public static bool IsWithinRestoreGrace(long nowTick, long restoredFromMinimizeTick)
    {
        if (restoredFromMinimizeTick <= 0)
            return false;

        long elapsed = nowTick - restoredFromMinimizeTick;
        return elapsed >= 0 && elapsed < RestoreGraceMs;
    }
}
