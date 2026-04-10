using System;
using System.IO;
using System.Windows.Forms;

namespace CursorJump.App;

internal static class DebugLog
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CursorJump", "debug.log");

    internal static void Write(string message)
    {
        try
        {
            var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
            File.AppendAllText(LogPath, line + Environment.NewLine);
        }
        catch { }
    }

    /// <summary>
    /// 接続中のモニター情報をログ出力する。
    /// </summary>
    internal static void WriteMonitorInfo()
    {
        try
        {
            var screens = Screen.AllScreens;
            Write($"MonitorInfo: count={screens.Length}");
            foreach (var s in screens)
            {
                Write($"  {s.DeviceName}: Bounds={s.Bounds}, WorkingArea={s.WorkingArea}, Primary={s.Primary}");
            }
            Write($"  VirtualScreen: Left={System.Windows.SystemParameters.VirtualScreenLeft}, Top={System.Windows.SystemParameters.VirtualScreenTop}, Width={System.Windows.SystemParameters.VirtualScreenWidth}, Height={System.Windows.SystemParameters.VirtualScreenHeight}");
        }
        catch { }
    }
}
