using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CursorJump.App;

/// <summary>
/// 非同期ログ書き込み。Write() はキューに積むだけで即 return する (フックコールバック内でも数 us)。
/// バックグラウンドスレッドが順次ファイルへ吐き出す。
/// </summary>
internal static class DebugLog
{
    private static readonly string s_logPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CursorJump", "debug.log");

    // ローテーション先。消える(上書きされる)のはこの 1 ファイルだけ。
    private static readonly string s_oldLogPath = s_logPath + ".old";

    // debug.log の上限サイズ。超えたら debug.log.old へ退避して新しく書き始める (ディスク上は最大約 2 倍)。
    // 1MB の理由: 実測で稼働 1 分あたり約 170B (うち MemDiag 定期ログ約 137B)。1 日 8 時間稼働で約 13 日分、
    //   .old と合わせて約 2〜4 週間分が残り、不具合報告後の調査に足りる。メモ帳でも重くならずに開ける。
    // 変更履歴: 2026-10-07 新設 (v1.10.1)。それまでは無制限で、約半年で 9MB まで増えていた。
    internal const long MaxLogBytes = 1L * 1024 * 1024;

    private static readonly BlockingCollection<string> s_queue = new(boundedCapacity: 4096);
    private static readonly Thread s_worker;

    internal static string LogFilePath => s_logPath;

    static DebugLog()
    {
        try
        {
            var dir = Path.GetDirectoryName(s_logPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }
        catch { }

        s_worker = new Thread(WorkerLoop)
        {
            Name = "CursorJump.DebugLog",
            IsBackground = true,
        };
        s_worker.Start();
    }

    internal static void Write(string message)
    {
        try
        {
            var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
            // フル時は古い書き込みより優先度を下げて捨てる (フックを止めない)
            if (!s_queue.TryAdd(line)) { /* drop */ }
        }
        catch { }
    }

    /// <summary>
    /// アプリ終了時にキューを flush して終了するまで最大 timeout 待つ。
    /// </summary>
    internal static void Flush(TimeSpan timeout)
    {
        try
        {
            s_queue.CompleteAdding();
            s_worker.Join(timeout);
        }
        catch { }
    }

    private static void WorkerLoop()
    {
        try
        {
            // ローテーションはこのスレッドだけが行う (書き込みと競合しない)
            long size = LogRotation.GetFileSize(s_logPath);
            foreach (var line in s_queue.GetConsumingEnumerable())
            {
                try
                {
                    size = LogRotation.RotateIfNeeded(s_logPath, s_oldLogPath, size, MaxLogBytes);
                    var text = line + Environment.NewLine;
                    File.AppendAllText(s_logPath, text);
                    size += Encoding.UTF8.GetByteCount(text);
                }
                catch { }
            }
        }
        catch { }
    }

    /// <summary>
    /// 起動時のモニタ・DPI・プロセス DPI 認識レベルなどの診断情報を出力する。
    /// </summary>
    internal static void WriteMonitorInfo()
    {
        try
        {
            // プロセスの DPI 認識レベル
            try
            {
                using var proc = Process.GetCurrentProcess();
                if (NativeDpi.GetProcessDpiAwareness(proc.Handle, out var awareness) == 0)
                {
                    Write($"ProcessDpiAwareness: {awareness}");
                }
            }
            catch (Exception ex) { Write($"ProcessDpiAwareness query failed: {ex.GetType().Name}"); }

            var screens = Screen.AllScreens;
            Write($"MonitorInfo: count={screens.Length}");
            foreach (var s in screens)
            {
                var dpi = TryGetMonitorDpi(s.Bounds.Left + s.Bounds.Width / 2, s.Bounds.Top + s.Bounds.Height / 2);
                Write($"  {s.DeviceName}: Bounds={s.Bounds}, WorkingArea={s.WorkingArea}, Primary={s.Primary}, Dpi={dpi}");
            }
            Write($"  VirtualScreen: Left={System.Windows.SystemParameters.VirtualScreenLeft}, Top={System.Windows.SystemParameters.VirtualScreenTop}, Width={System.Windows.SystemParameters.VirtualScreenWidth}, Height={System.Windows.SystemParameters.VirtualScreenHeight}");

            // デバイス名 ↔ 安定キー ↔ フレンドリ名 ↔ Bounds の対応表 (v1.9.3)。
            // ドック着脱で \.\DISPLAYn が振り直されても、キーで物理モニタを追跡できるようにする。
            // 以降は SystemEvents.DisplaySettingsChanged のたびに MonitorIdentity 側が同じ表を出力する。
            MonitorIdentity.LogTable("startup");

            // 物理インチ (WMI、非同期。失敗は無視)
            Task.Run(WriteMonitorPhysicalSize);
        }
        catch { }
    }

    private static string TryGetMonitorDpi(int x, int y)
    {
        try
        {
            var pt = new NativeDpi.POINT { X = x, Y = y };
            var hMon = NativeDpi.MonitorFromPoint(pt, NativeDpi.MONITOR_DEFAULTTONEAREST);
            if (hMon == IntPtr.Zero) return "?";
            if (NativeDpi.GetDpiForMonitor(hMon, NativeDpi.MDT_EFFECTIVE_DPI, out uint dx, out uint dy) == 0)
            {
                int scale = (int)Math.Round(dx * 100.0 / 96.0);
                return $"{dx}x{dy} ({scale}%)";
            }
        }
        catch { }
        return "?";
    }

    private static void WriteMonitorPhysicalSize()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM WmiMonitorBasicDisplayParams");
            using var collection = searcher.Get();
            int idx = 0;
            foreach (ManagementObject m in collection)
            {
                using (m)
                {
                    try
                    {
                        var hCm = Convert.ToDouble(m["MaxHorizontalImageSize"]);
                        var vCm = Convert.ToDouble(m["MaxVerticalImageSize"]);
                        if (hCm > 0 && vCm > 0)
                        {
                            var diagCm = Math.Sqrt(hCm * hCm + vCm * vCm);
                            var diagInch = diagCm / 2.54;
                            Write($"  PhysicalMonitor[{idx}]: {hCm:F1}x{vCm:F1} cm, diagonal {diagInch:F1}\"");
                        }
                    }
                    catch { }
                }
                idx++;
            }
        }
        catch (Exception ex)
        {
            Write($"WriteMonitorPhysicalSize failed: {ex.GetType().Name}");
        }
    }
}

/// <summary>
/// debug.log のサイズ上限ローテーション (v1.10.1)。パスを引数で受けるのは、本物のログに触れずにテストするため。
/// 削除(上書き)されうるのは呼び出し側が渡す oldPath の 1 ファイルだけで、ワイルドカードは使わない。
/// </summary>
internal static class LogRotation
{
    internal static long GetFileSize(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? info.Length : 0;
        }
        catch { return 0; }
    }

    /// <summary>
    /// currentSize が maxBytes 以上なら logPath を oldPath へ名前変更し (既存の oldPath は上書き)、0 を返す。
    /// それ以外は書き込みを続けるべき現在サイズを返す。失敗しても例外は投げない (ログが止まるよりは上限超過を許す)。
    /// </summary>
    internal static long RotateIfNeeded(string logPath, string oldPath, long currentSize, long maxBytes)
    {
        if (currentSize < maxBytes) return currentSize;
        if (string.Equals(Path.GetFullPath(logPath), Path.GetFullPath(oldPath), StringComparison.OrdinalIgnoreCase))
            return currentSize;

        // 手元の集計は概算 (外部で消された・別プロセスが先にローテートした等) なので実サイズで確かめ直す
        long actual = GetFileSize(logPath);
        if (actual < maxBytes) return actual;

        try
        {
            File.Move(logPath, oldPath, overwrite: true);
            return 0;
        }
        catch
        {
            return actual;
        }
    }
}

/// <summary>
/// DebugLog 専用の P/Invoke。NativeMethods に混ぜないことで、本体ロジックから分離する。
/// </summary>
internal static class NativeDpi
{
    internal const int MDT_EFFECTIVE_DPI = 0;
    internal const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT { public int X; public int Y; }

    internal enum PROCESS_DPI_AWARENESS
    {
        Process_DPI_Unaware = 0,
        Process_System_DPI_Aware = 1,
        Process_Per_Monitor_DPI_Aware = 2,
    }

    [DllImport("user32.dll")]
    internal static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("Shcore.dll")]
    internal static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("Shcore.dll")]
    internal static extern int GetProcessDpiAwareness(IntPtr hprocess, out PROCESS_DPI_AWARENESS value);
}
