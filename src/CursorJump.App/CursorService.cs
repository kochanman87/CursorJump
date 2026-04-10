namespace CursorJump.App;

internal static class CursorService
{
    /// <summary>
    /// カーソルを指定した物理ピクセル座標へジャンプさせる。
    /// </summary>
    internal static void JumpTo(int x, int y)
    {
        NativeMethods.SetCursorPos(x, y);
    }
}
