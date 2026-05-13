using System;

namespace CursorJump.App;

internal static class CursorService
{
    /// <summary>
    /// カーソルを指定した物理ピクセル座標へジャンプさせる。
    /// 既定では SendInput(MOUSEEVENTF_ABSOLUTE | VIRTUALDESK) を使う。
    /// この経路は仮想デスクトップ全体を 0..65535 の正規化座標で表すため、
    /// PerMonitorV2 環境で SetCursorPos が DPI 仮想化により別座標へ飛んでしまう
    /// バグ (Win11 + マルチ DPI モニタで報告) を回避できる。
    /// 退避路として SetCursorPos も残してある (AppSettings.UseSendInputForJump=false で切替)。
    /// </summary>
    internal static void JumpTo(int physicalX, int physicalY, bool useSendInput = true)
    {
        if (!useSendInput)
        {
            NativeMethods.SetCursorPos(physicalX, physicalY);
            return;
        }

        int virtualLeft = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
        int virtualTop  = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
        int virtualW    = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
        int virtualH    = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);

        // 0..65535 正規化。仮想デスクトップ幅が 1px の極端ケースでもゼロ除算しないよう Max(1, ..) で保護。
        // 端を 65535 に丸めるため (W-1) で割る。
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
            // SendInput 失敗時は SetCursorPos にフォールバックして致命傷を避ける
            NativeMethods.SetCursorPos(physicalX, physicalY);
        }
    }
}
