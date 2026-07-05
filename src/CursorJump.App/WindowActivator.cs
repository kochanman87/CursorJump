using System;
using System.Diagnostics;

namespace CursorJump.App;

/// <summary>
/// 座標ジャンプ後、カーソル直下のウィンドウを前面化（アクティブ化）するヘルパー。
/// CursorJump はトレイ常駐＝非フォアグラウンドのため、素の SetForegroundWindow は
/// フォアグラウンド奪取制限で失敗（タスクバー点滅のみ）になりがち。
/// 現フォアグラウンドスレッドへ AttachThreadInput で一時接続してから前面化する定石を使う。
/// 失敗・例外はすべて DebugLog に記録して握りつぶす（ジャンプ本体を妨げない）。
/// </summary>
internal static class WindowActivator
{
    // 自プロセス（オーバーレイ等）を前面化しないための比較用。
    private static readonly uint OwnProcessId = (uint)Process.GetCurrentProcess().Id;

    /// <summary>
    /// 物理ピクセル座標 (physX, physY) の直下にあるトップレベルウィンドウを前面化する。
    /// 直下にウィンドウが無い／自プロセスのウィンドウならば何もしない。
    /// </summary>
    public static void Activate(int physX, int physY)
    {
        try
        {
            var pt = new NativeMethods.POINT { X = physX, Y = physY };
            IntPtr hwnd = NativeMethods.WindowFromPoint(pt);
            if (hwnd == IntPtr.Zero) return;

            // 最上位トップレベルウィンドウへ正規化（子コントロールではなくウィンドウ本体を前面化）
            IntPtr root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
            if (root != IntPtr.Zero) hwnd = root;

            // 自プロセス（透明オーバーレイ・不可視メインウィンドウ等）は前面化しない
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint targetPid);
            if (targetPid == OwnProcessId) return;

            ActivateWindow(hwnd, physX, physY);
        }
        catch (Exception ex)
        {
            DebugLog.Write($"WindowActivator.Activate exception: {ex.Message}");
        }
    }

    /// <summary>
    /// 現在フォアグラウンドのウィンドウの中心物理座標を返す（v1.9.0+）。
    /// 取得失敗・自プロセス・最小化/不正矩形の場合は null。
    /// <paramref name="verboseLog"/> が true のとき GetWindowRect の生値を DebugLog に記録する
    /// （DPI 仮想化の切り分け用。座標が視覚位置の定数倍になっていれば仮想化を疑う）。
    /// </summary>
    public static (int X, int Y)? GetForegroundWindowCenter(bool verboseLog = false)
    {
        try
        {
            IntPtr hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return null;

            IntPtr root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
            if (root != IntPtr.Zero) hwnd = root;

            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == OwnProcessId) return null; // 自プロセスは対象外

            if (!NativeMethods.GetWindowRect(hwnd, out var rc)) return null;
            int w = rc.Right - rc.Left;
            int h = rc.Bottom - rc.Top;
            if (w <= 0 || h <= 0) return null; // 最小化・不正矩形

            if (verboseLog)
                DebugLog.Write($"GetForegroundWindowCenter: hwnd={hwnd}, rect=({rc.Left},{rc.Top})-({rc.Right},{rc.Bottom}), center=({rc.Left + w / 2},{rc.Top + h / 2})");

            return (rc.Left + w / 2, rc.Top + h / 2);
        }
        catch (Exception ex)
        {
            DebugLog.Write($"WindowActivator.GetForegroundWindowCenter exception: {ex.Message}");
            return null;
        }
    }

    /// <summary>指定 HWND を AttachThreadInput 経由で前面化する内部実装。</summary>
    private static void ActivateWindow(IntPtr hwnd, int physX, int physY)
    {
        try
        {
            IntPtr foreground = NativeMethods.GetForegroundWindow();
            if (foreground == hwnd) return; // 既に前面

            uint targetThread = NativeMethods.GetWindowThreadProcessId(hwnd, out _);
            uint foregroundThread = NativeMethods.GetWindowThreadProcessId(foreground, out _);
            uint currentThread = NativeMethods.GetCurrentThreadId();

            // 現フォアグラウンドスレッドと自スレッドを入力的に接続してから前面化する。
            bool attachedFg = false;
            bool attachedTarget = false;
            try
            {
                if (foregroundThread != 0 && foregroundThread != currentThread)
                    attachedFg = NativeMethods.AttachThreadInput(currentThread, foregroundThread, true);
                if (targetThread != 0 && targetThread != currentThread && targetThread != foregroundThread)
                    attachedTarget = NativeMethods.AttachThreadInput(currentThread, targetThread, true);

                bool ok = NativeMethods.SetForegroundWindow(hwnd);
                if (!ok)
                    DebugLog.Write($"WindowActivator: SetForegroundWindow returned false (hwnd={hwnd}, at=({physX},{physY}))");
            }
            finally
            {
                if (attachedFg) NativeMethods.AttachThreadInput(currentThread, foregroundThread, false);
                if (attachedTarget) NativeMethods.AttachThreadInput(currentThread, targetThread, false);
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"WindowActivator.Activate exception: {ex.Message}");
        }
    }
}
