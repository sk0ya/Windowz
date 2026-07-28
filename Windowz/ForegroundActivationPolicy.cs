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

        // Windowz は Activated を通知しないことがある。タスクバークリック直後の
        // Windowz 前景イベントなら、到着時に managed window へ前景を渡し終えて
        // 食い違っていてもユーザー操作として処理する。
        if (eventIsWindowz && followsRecentTaskbarClick)
            return foregroundIsWindowz || foregroundIsActiveManagedWindow;

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
