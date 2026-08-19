using System.Collections.Generic;
using CursorJump.App.Models;

namespace CursorJump.App;

internal static class MonitorFilter
{
    /// <summary>
    /// 座標が現在接続中のモニタ上にあるか。判定は <see cref="JumpTargetResolver.FindMonitor"/> と共通で、
    /// 「ナビゲート対象になる座標」と「削除モードで描画される座標」を必ず一致させる。
    /// モニタ情報を一切持たない完全な旧データは常に対象とする（旧 settings.json 互換）。
    /// </summary>
    public static bool IsCoordinateOnConnectedMonitor(
        SavedCoordinate coordinate,
        IReadOnlyList<MonitorInfo> monitors)
    {
        if (string.IsNullOrEmpty(coordinate.MonitorKey)
            && string.IsNullOrEmpty(coordinate.MonitorDeviceName)) return true;

        return JumpTargetResolver.FindMonitor(coordinate, monitors).Monitor is not null;
    }

    /// <summary>
    /// デバイス名一覧のみで判定する簡易版（安定キーを持たないため従来動作＝名前照合になる）。
    /// </summary>
    public static bool IsCoordinateOnConnectedMonitor(
        SavedCoordinate coordinate,
        IReadOnlyList<string> connectedDeviceNames)
    {
        if (string.IsNullOrEmpty(coordinate.MonitorDeviceName)) return true;
        for (int i = 0; i < connectedDeviceNames.Count; i++)
        {
            if (connectedDeviceNames[i] == coordinate.MonitorDeviceName) return true;
        }
        return false;
    }
}
