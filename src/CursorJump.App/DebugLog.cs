using System;
using System.IO;

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
}
