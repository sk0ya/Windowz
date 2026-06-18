using System.IO;
using System.Text;

namespace WindowzTabManager;

/// <summary>
/// タスクバー復帰・フォアグラウンド昇格まわりの挙動を追跡するための軽量ファイルロガー。
/// <para>
/// GUI アプリのためデバッガを接続しないと <see cref="System.Diagnostics.Debug.WriteLine(string)"/>
/// は観測できない。本ロガーは通常実行時でも
/// <c>%LOCALAPPDATA%\WindowzTabManager\logs\activation.log</c> に追記するため、
/// 実機で「managed アプリが前面に出ないケース」を再現してから内容を確認できる。
/// </para>
/// <para>
/// 既定で有効。環境変数 <c>WINDOWZ_ACTIVATION_LOG=0</c>（または <c>false</c>）で無効化する。
/// ロギングは決して本処理を妨げない（例外は握りつぶす）。
/// </para>
/// </summary>
internal static class ActivationLog
{
    private static readonly object Gate = new();
    private static readonly bool EnabledInternal;
    private static readonly string? LogFilePath;
    private static readonly long StartTick;

    static ActivationLog()
    {
        try
        {
            var flag = Environment.GetEnvironmentVariable("WINDOWZ_ACTIVATION_LOG");
            EnabledInternal =
                !string.Equals(flag, "0", StringComparison.Ordinal) &&
                !string.Equals(flag, "false", StringComparison.OrdinalIgnoreCase);

            if (EnabledInternal)
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "WindowzTabManager",
                    "logs");
                Directory.CreateDirectory(dir);
                LogFilePath = Path.Combine(dir, "activation.log");
                StartTick = Environment.TickCount64;

                Write("Session",
                    $"==== activation log started {DateTime.Now:yyyy-MM-dd HH:mm:ss} pid={Environment.ProcessId} ====");
            }
        }
        catch
        {
            EnabledInternal = false;
        }
    }

    public static bool Enabled => EnabledInternal;

    public static void Write(string category, string message)
    {
        if (!EnabledInternal || LogFilePath == null)
            return;

        try
        {
            long elapsed = Environment.TickCount64 - StartTick;
            string line =
                $"[{elapsed,8}ms][T{Environment.CurrentManagedThreadId:D2}][{category,-14}] {message}{Environment.NewLine}";

            lock (Gate)
            {
                File.AppendAllText(LogFilePath, line, Encoding.UTF8);
            }
        }
        catch
        {
            // ロギングは本処理を妨げない
        }
    }

    /// <summary>ウィンドウハンドルをクラス名・タイトル付きで読みやすく整形する。</summary>
    public static string Describe(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return "0x0";

        try
        {
            string title = NativeMethods.GetWindowTitle(hwnd);
            if (title.Length > 24)
                title = title[..24] + "…";

            string cls = NativeMethods.GetWindowClassName(hwnd);
            return $"0x{hwnd.ToInt64():X}('{title}' {cls})";
        }
        catch
        {
            return $"0x{hwnd.ToInt64():X}";
        }
    }
}
