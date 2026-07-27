namespace WindowzTabManager;

/// <summary>
/// 画面座標のヒットテスト。
/// <para>
/// 判定は必ず物理ピクセルの矩形で行う。WPF の <c>ActualWidth</c> / <c>ActualHeight</c> は
/// DIP なので、<c>PointToScreen</c> (物理ピクセル) で得た原点にそのまま足すと、
/// 判定矩形が実ウィンドウの 1/スケール に縮む。100% 以外の表示スケールでは
/// ウィンドウ右側・下側 (1 - 1/スケール の領域) がウィンドウ外と誤判定される。
/// </para>
/// </summary>
public static class WindowHitTest
{
    /// <summary>
    /// 物理ピクセルの矩形に画面座標の点が含まれるかを判定する。
    /// 右辺・下辺は含まない (Win32 の RECT と同じ半開区間)。
    /// </summary>
    public static bool IsScreenPointInsideRect(
        int left,
        int top,
        int right,
        int bottom,
        int screenX,
        int screenY)
    {
        if (right <= left || bottom <= top)
            return false;

        return screenX >= left && screenX < right &&
               screenY >= top && screenY < bottom;
    }
}
