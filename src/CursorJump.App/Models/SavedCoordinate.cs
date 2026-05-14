namespace CursorJump.App.Models;

/// <summary>
/// 保存座標。物理絶対座標 (X, Y) に加えて、所属モニタ内の相対座標 (MonitorRelativeX/Y) を持つ。
/// 再生時はモニタの現在 Bounds から相対座標を介して絶対座標を再計算することで、
/// PerMonitorV2 + マルチ DPI 環境で SetCursorPos / SendInput が DPI 仮想化により誤動作する問題を回避する。
/// MonitorRelativeX/Y == -1 は旧 settings.json 互換 (未設定) を示し、CoordinateStore.Load で自動補完される。
/// </summary>
public record SavedCoordinate(
    int X,
    int Y,
    string MonitorDeviceName = "",
    int MonitorRelativeX = -1,
    int MonitorRelativeY = -1);
