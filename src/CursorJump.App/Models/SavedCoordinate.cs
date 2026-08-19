namespace CursorJump.App.Models;

/// <summary>
/// 保存座標。物理絶対座標 (X, Y) に加えて、所属モニタ内の相対座標 (MonitorRelativeX/Y) を持つ。
/// 再生時はモニタの現在 Bounds から相対座標を介して絶対座標を再計算することで、
/// PerMonitorV2 + マルチ DPI 環境で SetCursorPos / SendInput が DPI 仮想化により誤動作する問題を回避する。
/// MonitorRelativeX/Y == -1 は旧 settings.json 互換 (未設定) を示し、CoordinateStore.Load で自動補完される。
///
/// モニタの identity は 3 段で持つ (v1.9.3+):
///   - MonitorKey        : EDID 由来のデバイスインターフェースパス。ドック着脱を跨いで安定。第1照合キー
///   - MonitorFingerprint: フレンドリ名 + 解像度。ポート変更等で MonitorKey が変わったときの第2照合キー
///   - MonitorDeviceName : \.\DISPLAYn。着脱で振り直されるため第3照合キー (旧データ互換)
/// MonitorKey / MonitorFingerprint が空文字なのは旧 settings.json 互換で、CoordinateStore.Load で補完される。
/// </summary>
public record SavedCoordinate(
    int X,
    int Y,
    string MonitorDeviceName = "",
    int MonitorRelativeX = -1,
    int MonitorRelativeY = -1,
    string MonitorKey = "",
    string MonitorFingerprint = "");
