using System;
using CursorJump.App.Models;

namespace CursorJump.App;

internal static class CursorService
{
    /// <summary>
    /// カーソルを指定した物理ピクセル座標へジャンプさせる。
    /// 戦略は <see cref="JumpStrategy"/> で切り替える。詳細は enum コメント参照。
    ///
    /// 背景:
    /// PerMonitorV2 + マルチ DPI 環境 (例: Dynabook 内蔵 150% + 外部 100%) では、
    /// SetCursorPos / SendInput VIRTUALDESK が OS 内部の DPI 仮想化により
    /// 物理座標を別の意味で解釈し、視覚的にズレた位置にカーソルが飛ぶ症状がある。
    /// v1.5.1 既定の DpiContext は SetThreadDpiAwarenessContext で対象 DPI を明示してから
    /// SetCursorPos を呼ぶことで OS キャッシュをリセットする。
    /// </summary>
    internal static void JumpTo(int physicalX, int physicalY, JumpStrategy strategy = JumpStrategy.DpiContext)
    {
        switch (strategy)
        {
            case JumpStrategy.SendInputVirtualDesk:
                JumpToViaSendInput(physicalX, physicalY);
                break;
            case JumpStrategy.LegacySetCursorPos:
                NativeMethods.SetCursorPos(physicalX, physicalY);
                break;
            case JumpStrategy.DpiContext:
            default:
                JumpToViaSetCursorPosWithDpiContext(physicalX, physicalY);
                break;
        }
    }

    /// <summary>
    /// SetThreadDpiAwarenessContext(PER_MONITOR_AWARE_V2) でスレッドの DPI コンテキストを
    /// 明示してから SetCursorPos を呼ぶ。PerMonitorV2 アプリでもこの呼び直しにより
    /// OS の DPI 仮想化キャッシュが期待通りに更新されるケースがある (v1.5.1 既定経路)。
    /// </summary>
    private static void JumpToViaSetCursorPosWithDpiContext(int physicalX, int physicalY)
    {
        IntPtr prevContext = IntPtr.Zero;
        bool contextChanged = false;
        try
        {
            prevContext = NativeMethods.SetThreadDpiAwarenessContext(
                NativeMethods.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
            // 戻り値が IntPtr.Zero (= 旧コンテキスト取得不能) なら API 失敗。
            // それでも SetCursorPos は呼ぶ (悪化はしない)。
            contextChanged = prevContext != IntPtr.Zero;

            NativeMethods.SetCursorPos(physicalX, physicalY);
        }
        finally
        {
            if (contextChanged)
            {
                try { NativeMethods.SetThreadDpiAwarenessContext(prevContext); } catch { }
            }
        }
    }

    /// <summary>
    /// v1.5.0 で導入した SendInput VIRTUALDESK 経路。Dynabook では効かなかったが、
    /// 他環境向けの退避路として残す。
    /// </summary>
    private static void JumpToViaSendInput(int physicalX, int physicalY)
    {
        int virtualLeft = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
        int virtualTop  = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
        int virtualW    = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
        int virtualH    = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);

        int nx = (int)Math.Round((physicalX - virtualLeft) * 65535.0 / Math.Max(1, virtualW - 1));
        int ny = (int)Math.Round((physicalY - virtualTop)  * 65535.0 / Math.Max(1, virtualH - 1));

        var inputs = new NativeMethods.INPUT[1];
        inputs[0].type = NativeMethods.INPUT_MOUSE;
        inputs[0].u.mi = new NativeMethods.MOUSEINPUT
        {
            dx = nx,
            dy = ny,
            mouseData = 0,
            dwFlags = NativeMethods.MOUSEEVENTF_MOVE
                    | NativeMethods.MOUSEEVENTF_ABSOLUTE
                    | NativeMethods.MOUSEEVENTF_VIRTUALDESK,
            time = 0,
            dwExtraInfo = IntPtr.Zero,
        };

        uint sent = NativeMethods.SendInput(1, inputs, System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.INPUT>());
        if (sent == 0)
        {
            // SendInput 失敗時は DpiContext 経路にフォールバック (v1.5.1 既定経路と同じ)
            JumpToViaSetCursorPosWithDpiContext(physicalX, physicalY);
        }
    }
}
