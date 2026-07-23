using System.Diagnostics;
using System.Windows.Media;
using WindowzTabManager.Converters;

namespace WindowzTabManager.Models;

public class WindowInfo
{
    public IntPtr Handle { get; set; }
    public string Title { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public int ProcessId { get; set; }
    public ImageSource? Icon { get; set; }
    public string? ExecutablePath { get; set; }
    public bool IsExplorer { get; set; }
    public bool IsElevated { get; set; }

    /// <summary>
    /// resolveIconAndElevation: false にすると、アイコン抽出（ディスクI/O + GDI変換）と
    /// 昇格権限チェック（プロセストークンのオープン）をスキップする。
    /// 起動時の候補ウィンドウ検出のように大量のウィンドウを繰り返し走査する場面で使用し、
    /// マッチが確定した後は WithResolvedDisplayInfo で1件だけフル解決すればよい。
    /// デフォルト値は用意しない: 呼び出し側に毎回どちらが必要か明示させることで、
    /// 表示用途の呼び出しが誤って軽量パスの結果（アイコン無し）を受け取る事故を防ぐ。
    /// </summary>
    public static WindowInfo? FromHandle(IntPtr handle, bool resolveIconAndElevation)
    {
        if (handle == IntPtr.Zero)
            return null;

        string title = NativeMethods.GetWindowTitle(handle);
        if (string.IsNullOrWhiteSpace(title))
            return null;

        NativeMethods.GetWindowThreadProcessId(handle, out uint processId);

        string processName = string.Empty;
        string? executablePath = null;
        ImageSource? icon = null;

        try
        {
            using var process = Process.GetProcessById((int)processId);
            processName = process.ProcessName;

            try
            {
                string? fileName = process.MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(fileName))
                {
                    executablePath = fileName;
                    if (resolveIconAndElevation)
                        icon = PathToIconConverter.GetIconForPath(fileName);
                }
            }
            catch
            {
                // Access denied to some processes.
            }
        }
        catch
        {
            // Process may have already exited.
        }

        string className = NativeMethods.GetWindowClassName(handle);
        bool isExplorer = string.Equals(processName, "explorer", StringComparison.OrdinalIgnoreCase)
                          && string.Equals(className, "CabinetWClass", StringComparison.Ordinal);

        bool isElevated = resolveIconAndElevation && !App.IsRunningAsAdmin() && NativeMethods.IsProcessElevated(handle);

        return new WindowInfo
        {
            Handle = handle,
            Title = title,
            ProcessName = processName,
            ProcessId = (int)processId,
            Icon = icon,
            ExecutablePath = executablePath,
            IsExplorer = isExplorer,
            IsElevated = isElevated
        };
    }

    /// <summary>
    /// 軽量スキャン（resolveIconAndElevation: false）で得た WindowInfo から、
    /// アイコンと昇格フラグだけを追加解決する。
    /// アイコンは ExecutablePath ベースのキャッシュ (PathToIconConverter) から取るため、
    /// 対象ウィンドウが解決までの間に閉じてしまっても取得できる（ハンドルの再走査が不要）。
    /// タイトル/PID/実行パス等は既に lite の時点で分かっているため再取得しない。
    /// </summary>
    public static WindowInfo WithResolvedDisplayInfo(WindowInfo lite)
    {
        ImageSource? icon = !string.IsNullOrWhiteSpace(lite.ExecutablePath)
            ? PathToIconConverter.GetIconForPath(lite.ExecutablePath)
            : null;

        bool isElevated = lite.Handle != IntPtr.Zero
            && !App.IsRunningAsAdmin()
            && NativeMethods.IsProcessElevated(lite.Handle);

        return new WindowInfo
        {
            Handle = lite.Handle,
            Title = lite.Title,
            ProcessName = lite.ProcessName,
            ProcessId = lite.ProcessId,
            Icon = icon,
            ExecutablePath = lite.ExecutablePath,
            IsExplorer = lite.IsExplorer,
            IsElevated = isElevated
        };
    }

    public override string ToString() => $"{Title} ({ProcessName})";
}
