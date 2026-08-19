using System;
using System.Collections.Generic;
using CursorJump.App.Models;

namespace CursorJump.App;

/// <summary>
/// 保存座標を解決した結果。<see cref="MatchedBy"/> は照合に使った段（診断ログ用）。
/// </summary>
internal readonly record struct JumpTarget(int X, int Y, string MatchedBy);

/// <summary>
/// 保存座標 (<see cref="SavedCoordinate"/>) と現在のモニタ構成 (<see cref="MonitorInfo"/> のスナップショット) から
/// 実際にカーソルを置く物理絶対座標を解決する純粋関数。
///
/// 照合は多段:
///   1. key         : 安定キー (EDID 由来のデバイスインターフェースパス) 一致
///   2. fingerprint : フレンドリ名 + 解像度が「一意に」一致（ドックのポート変更等で UID が変わった場合の受け皿）
///   3. name        : <c>\.\DISPLAYn</c> 一致（旧データ互換。着脱で振り直されるため最後段）
///   4. absolute    : どのモニタにも解決できず保存時の絶対座標をそのまま使う
///
/// モニタが特定できた場合は「そのモニタの現 Bounds + 保存された相対座標」で再計算し、
/// 最後に Bounds 内へクランプする（構成変更で解像度が縮んだ場合に画面外へ飛ばさない）。
/// </summary>
internal static class JumpTargetResolver
{
    public const string MatchKey         = "key";
    public const string MatchFingerprint = "fingerprint";
    public const string MatchName        = "name";
    public const string MatchAbsolute    = "absolute";

    /// <summary>
    /// 保存座標に対応する現在のモニタを探す。見つからなければ Monitor は null。
    /// </summary>
    public static (MonitorInfo? Monitor, string MatchedBy) FindMonitor(
        SavedCoordinate coord, IReadOnlyList<MonitorInfo> monitors)
    {
        if (monitors is null || monitors.Count == 0) return (null, MatchAbsolute);

        // 1段目: 安定キー一致
        if (!string.IsNullOrEmpty(coord.MonitorKey))
        {
            for (int i = 0; i < monitors.Count; i++)
            {
                if (!string.IsNullOrEmpty(monitors[i].StableKey)
                    && string.Equals(monitors[i].StableKey, coord.MonitorKey, StringComparison.OrdinalIgnoreCase))
                {
                    return (monitors[i], MatchKey);
                }
            }
        }

        // 2段目: フレンドリ名 + 解像度が「一意に」一致するモニタ
        // （同型番 2 枚のような曖昧なケースでは採用しない = 誤ったモニタへ飛ばさない）
        if (!string.IsNullOrEmpty(coord.MonitorFingerprint))
        {
            int matchIndex = -1;
            int matchCount = 0;
            for (int i = 0; i < monitors.Count; i++)
            {
                if (string.Equals(monitors[i].Fingerprint, coord.MonitorFingerprint, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrEmpty(monitors[i].Fingerprint))
                {
                    matchIndex = i;
                    matchCount++;
                }
            }
            if (matchCount == 1) return (monitors[matchIndex], MatchFingerprint);
        }

        // 3段目: デバイス名一致（従来動作）
        // 安定キーを持つ座標に対しては使わない。\.\DISPLAYn は着脱で振り直されるため、
        // 「キーは持っているのに一致しなかった」= そのモニタは繋がっていないと判断する方が安全
        // （名前で拾うと、まさに本バグ「1 枚隣のモニタに飛ぶ」を再現してしまう）。
        // ただしスナップショット全体で安定キーが 1 つも取れていない場合
        // （EnumDisplayDevices 失敗等）は従来動作へ完全フォールバックする。
        bool coordHasKey = !string.IsNullOrEmpty(coord.MonitorKey);
        bool snapshotHasAnyKey = false;
        for (int i = 0; i < monitors.Count; i++)
        {
            if (!string.IsNullOrEmpty(monitors[i].StableKey)) { snapshotHasAnyKey = true; break; }
        }

        if (!string.IsNullOrEmpty(coord.MonitorDeviceName) && (!coordHasKey || !snapshotHasAnyKey))
        {
            for (int i = 0; i < monitors.Count; i++)
            {
                if (string.Equals(monitors[i].GdiDeviceName, coord.MonitorDeviceName, StringComparison.OrdinalIgnoreCase))
                    return (monitors[i], MatchName);
            }
        }

        return (null, MatchAbsolute);
    }

    /// <summary>
    /// 保存座標から実際にカーソルを置く物理絶対座標を解決する。
    /// </summary>
    public static JumpTarget Resolve(SavedCoordinate coord, IReadOnlyList<MonitorInfo> monitors)
    {
        var (monitor, matchedBy) = FindMonitor(coord, monitors);

        if (monitor is null) return new JumpTarget(coord.X, coord.Y, MatchAbsolute);

        // 相対座標が無い旧データはモニタが特定できても再計算できない → 保存時の絶対座標を使う
        if (coord.MonitorRelativeX < 0 || coord.MonitorRelativeY < 0)
            return new JumpTarget(coord.X, coord.Y, MatchAbsolute);

        var bounds = monitor.Value.Bounds;
        int x = bounds.Left + coord.MonitorRelativeX;
        int y = bounds.Top + coord.MonitorRelativeY;

        // 解像度が縮んだ構成でも画面外へ飛ばさない
        if (bounds.Width > 0) x = Math.Clamp(x, bounds.Left, bounds.Right - 1);
        if (bounds.Height > 0) y = Math.Clamp(y, bounds.Top, bounds.Bottom - 1);

        return new JumpTarget(x, y, matchedBy);
    }
}
