using System.Collections.Generic;
using CursorJump.App.Models;

namespace CursorJump.App;

internal static class MonitorFilter
{
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
