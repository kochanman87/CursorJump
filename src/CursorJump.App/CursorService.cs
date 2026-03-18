using System.Windows.Forms;

namespace CursorJump.App;

internal static class CursorService
{
    /// <summary>
    /// カーソルが現在存在するモニターの中央へカーソルをジャンプさせる。
    /// </summary>
    internal static void JumpToCentreOfCurrentScreen()
    {
        // Cursor.Position は物理ピクセル座標を返す（SetCursorPos と同じ座標系）
        var currentPosition = Cursor.Position;

        // カーソルが存在するモニターを取得（範囲外の場合は最近傍モニター）
        var screen = Screen.FromPoint(currentPosition);

        var bounds = screen.Bounds;
        int targetX = bounds.Left + bounds.Width  / 2;
        int targetY = bounds.Top  + bounds.Height / 2;

        NativeMethods.SetCursorPos(targetX, targetY);
    }
}
