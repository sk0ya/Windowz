using System.Runtime.InteropServices;

namespace WindowzTabManager.Tests;

/// <summary>
/// 画面座標ヒットテスト (<see cref="WindowHitTest"/>) のテスト。
///
/// 再現していた不具合:
///   IsScreenPointInsideWindow が PointToScreen (物理ピクセル) の原点に
///   ActualWidth / ActualHeight (DIP) を足していたため、判定矩形が実ウィンドウの
///   1/スケール に縮んでいた。表示スケール 150% ではウィンドウ右 1/3・下 1/3 が
///   「ウィンドウ外」と誤判定され、タブをそこにドロップすると並び替え・タイル化ではなく
///   ReleaseEmbeddedTab (埋め込み解除) が走っていた (MainWindow.TabDrag.cs)。
///
/// スケールに依存せず検証できるよう、物理ピクセル矩形を DIP × スケール で組み立てて
/// 各スケールの右下領域がウィンドウ内と判定されることを確認する。
/// </summary>
internal static class WindowHitTestTests
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    private const uint WS_OVERLAPPEDWINDOW = 0x00CF0000;

    // ─────────────────────────────────────────
    // 表示スケール別の判定
    // ─────────────────────────────────────────

    /// <summary>
    /// 本命の回帰テスト: 100% 以外のスケールでも、ウィンドウ右下寄りの点が
    /// 「ウィンドウ内」と判定されなければならない。
    /// 旧実装はスケール 1.5 で幅の 66.7% より右を外側と誤判定していた。
    /// </summary>
    internal static void HitTest_BottomRightRegion_IsInsideAtEveryScale()
    {
        foreach (double scale in new[] { 1.0, 1.25, 1.5, 1.75, 2.0 })
        {
            // Windowz の既定サイズ 1200x700 DIP のウィンドウを (100,100) に置いた場合の物理矩形
            const double widthDip = 1200;
            const double heightDip = 700;
            int left = 100;
            int top = 100;
            int right = left + (int)(widthDip * scale);
            int bottom = top + (int)(heightDip * scale);

            // 右下 90% の位置 (旧実装ではスケール 1.25 以上で「外側」と誤判定された)
            int x = left + (int)((right - left) * 0.9);
            int y = top + (int)((bottom - top) * 0.9);

            Assert(WindowHitTest.IsScreenPointInsideRect(left, top, right, bottom, x, y),
                $"スケール {scale:0.##} でウィンドウ右下 90% の点はウィンドウ内と判定されるべき。");

            // 旧実装が使っていた矩形 (原点は物理・サイズは DIP) では外側になることを確認し、
            // このテストが実際にバグを捕まえる条件を満たしていることを示す
            bool insideBuggyRect = WindowHitTest.IsScreenPointInsideRect(
                left, top, left + (int)widthDip, top + (int)heightDip, x, y);

            if (scale > 1.0)
            {
                Assert(!insideBuggyRect,
                    $"スケール {scale:0.##} では旧実装の矩形だと外側になるはず (テストの前提確認)。");
            }
        }
    }

    /// <summary>ウィンドウ中央・左上は当然ウィンドウ内。</summary>
    internal static void HitTest_CenterAndTopLeft_AreInside()
    {
        Assert(WindowHitTest.IsScreenPointInsideRect(100, 100, 1900, 1150, 1000, 600),
            "中央の点はウィンドウ内と判定されるべき。");
        Assert(WindowHitTest.IsScreenPointInsideRect(100, 100, 1900, 1150, 100, 100),
            "左上の頂点はウィンドウ内と判定されるべき。");
    }

    /// <summary>右辺・下辺は含まない (Win32 RECT と同じ半開区間)。</summary>
    internal static void HitTest_RightAndBottomEdges_AreExclusive()
    {
        Assert(!WindowHitTest.IsScreenPointInsideRect(100, 100, 500, 400, 500, 300),
            "右辺上の点はウィンドウ外と判定されるべき。");
        Assert(!WindowHitTest.IsScreenPointInsideRect(100, 100, 500, 400, 300, 400),
            "下辺上の点はウィンドウ外と判定されるべき。");
        Assert(WindowHitTest.IsScreenPointInsideRect(100, 100, 500, 400, 499, 399),
            "右辺・下辺の 1px 内側はウィンドウ内と判定されるべき。");
    }

    /// <summary>ウィンドウ外の点は当然外側。</summary>
    internal static void HitTest_OutsidePoints_AreOutside()
    {
        Assert(!WindowHitTest.IsScreenPointInsideRect(100, 100, 500, 400, 99, 300),
            "左外の点はウィンドウ外と判定されるべき。");
        Assert(!WindowHitTest.IsScreenPointInsideRect(100, 100, 500, 400, 300, 99),
            "上外の点はウィンドウ外と判定されるべき。");
        Assert(!WindowHitTest.IsScreenPointInsideRect(100, 100, 500, 400, 800, 800),
            "右下に大きく外れた点はウィンドウ外と判定されるべき。");
    }

    /// <summary>
    /// 最小化中のウィンドウは GetWindowRect が退避座標 (-32000 等) を返す。
    /// 空・不正な矩形では常に false を返す。
    /// </summary>
    internal static void HitTest_EmptyOrInvertedRect_IsAlwaysOutside()
    {
        Assert(!WindowHitTest.IsScreenPointInsideRect(100, 100, 100, 400, 100, 200),
            "幅 0 の矩形は常に外側と判定されるべき。");
        Assert(!WindowHitTest.IsScreenPointInsideRect(100, 100, 500, 100, 200, 100),
            "高さ 0 の矩形は常に外側と判定されるべき。");
        Assert(!WindowHitTest.IsScreenPointInsideRect(500, 400, 100, 100, 300, 200),
            "左右・上下が反転した矩形は常に外側と判定されるべき。");
    }

    // ─────────────────────────────────────────
    // 実 Win32 ウィンドウでの検証 (本番と同じ経路)
    // ─────────────────────────────────────────

    /// <summary>
    /// 本番 (MainWindow.IsScreenPointInsideWindow) と同じ組み合わせ
    /// GetWindowRect → WindowHitTest を実ウィンドウで検証する。
    /// GetCursorPos 由来の物理座標と同じ座標系で突き合わせられることを確認する。
    /// </summary>
    internal static void HitTest_RealWindowRect_MatchesWindowBounds()
    {
        IntPtr hwnd = CreateWindowEx(
            0, "STATIC", "HitTestProbe", WS_OVERLAPPEDWINDOW,
            120, 140, 640, 480,
            IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

        Assert(hwnd != IntPtr.Zero, "テスト用ウィンドウを作成できるべき。");

        try
        {
            Assert(GetWindowRect(hwnd, out var rect), "GetWindowRect は成功すべき。");

            int centerX = (rect.Left + rect.Right) / 2;
            int centerY = (rect.Top + rect.Bottom) / 2;
            Assert(WindowHitTest.IsScreenPointInsideRect(
                       rect.Left, rect.Top, rect.Right, rect.Bottom, centerX, centerY),
                "実ウィンドウ矩形の中心はウィンドウ内と判定されるべき。");

            // 右下 1px 内側 — 旧実装が取りこぼしていた領域
            Assert(WindowHitTest.IsScreenPointInsideRect(
                       rect.Left, rect.Top, rect.Right, rect.Bottom, rect.Right - 1, rect.Bottom - 1),
                "実ウィンドウ矩形の右下 1px 内側はウィンドウ内と判定されるべき。");

            Assert(!WindowHitTest.IsScreenPointInsideRect(
                       rect.Left, rect.Top, rect.Right, rect.Bottom, rect.Right + 10, centerY),
                "実ウィンドウ矩形の右外はウィンドウ外と判定されるべき。");
        }
        finally
        {
            DestroyWindow(hwnd);
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
