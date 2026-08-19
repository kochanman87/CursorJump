using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CursorJump.App;

/// <summary>
/// 1 枚の物理モニタの識別情報。
/// <paramref name="GdiDeviceName"/> は <c>\.\DISPLAYn</c>（セッション内の並び順で振り直される＝不安定）、
/// <paramref name="StableKey"/> は EDID 由来のデバイスインターフェースパス（ドック着脱を跨いで安定）。
/// <paramref name="Fingerprint"/> は「フレンドリ名 + 解像度」で、
/// ポート変更等で StableKey が変わったときの二次照合に使う。
/// 取得に失敗した場合 StableKey / Fingerprint は空文字（従来のデバイス名照合へフォールバック）。
/// </summary>
internal readonly record struct MonitorInfo(
    string GdiDeviceName,
    string StableKey,
    string FriendlyName,
    Rectangle Bounds)
{
    /// <summary>フレンドリ名 + 解像度。StableKey が一致しないときの二次照合キー。</summary>
    public string Fingerprint => BuildFingerprint(FriendlyName, Bounds.Width, Bounds.Height);

    public static string BuildFingerprint(string friendlyName, int width, int height)
    {
        if (string.IsNullOrEmpty(friendlyName) || width <= 0 || height <= 0) return string.Empty;
        return $"{friendlyName}|{width}x{height}";
    }

    /// <summary>循環インデックス等のグルーピングに使う安定キー（無ければデバイス名）。</summary>
    public string GroupKey => string.IsNullOrEmpty(StableKey) ? GdiDeviceName : StableKey;
}

/// <summary>
/// 接続中モニタのスナップショット（デバイス名 ↔ 安定キー ↔ フレンドリ名 ↔ Bounds）を提供する。
/// <see cref="SystemEvents.DisplaySettingsChanged"/> でキャッシュを破棄し、次回 <see cref="Snapshot"/> で再取得する。
/// </summary>
internal static class MonitorIdentity
{
    private static readonly object s_lock = new();
    private static IReadOnlyList<MonitorInfo>? s_cache;
    private static bool s_subscribed;

    /// <summary>
    /// 接続中の全モニタの識別情報を返す。キャッシュ済みならそれを返す。
    /// 安定キーの取得に失敗したモニタは StableKey が空文字になる（従来動作へフォールバック）。
    /// </summary>
    public static IReadOnlyList<MonitorInfo> Snapshot()
    {
        lock (s_lock)
        {
            EnsureSubscribed();
            if (s_cache is not null) return s_cache;
            s_cache = Build();
            return s_cache;
        }
    }

    /// <summary>キャッシュを破棄する（次回 <see cref="Snapshot"/> で再取得）。</summary>
    public static void Invalidate()
    {
        lock (s_lock) { s_cache = null; }
    }

    /// <summary>指定した物理座標を含むモニタを返す。含むものが無ければ null。</summary>
    public static MonitorInfo? FromPoint(IReadOnlyList<MonitorInfo> monitors, int x, int y)
    {
        for (int i = 0; i < monitors.Count; i++)
        {
            if (monitors[i].Bounds.Contains(x, y)) return monitors[i];
        }
        return null;
    }

    /// <summary>診断用: デバイス名 ↔ 安定キー ↔ フレンドリ名 ↔ Bounds の対応表を debug.log に出力する。</summary>
    public static void LogTable(string reason)
    {
        try
        {
            var monitors = Snapshot();
            DebugLog.Write($"MonitorIdentity ({reason}): count={monitors.Count}");
            foreach (var m in monitors)
            {
                string key = string.IsNullOrEmpty(m.StableKey) ? "<none>" : m.StableKey;
                DebugLog.Write($"  {m.GdiDeviceName}: key={key} friendly={m.FriendlyName} bounds={m.Bounds}");
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"MonitorIdentity.LogTable failed: {ex.GetType().Name}");
        }
    }

    private static void EnsureSubscribed()
    {
        if (s_subscribed) return;
        try
        {
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            s_subscribed = true;
        }
        catch (Exception ex)
        {
            // メッセージポンプの無いホスト（テスト等）では購読できないことがある。
            // キャッシュが古くなるだけなので致命的ではない。
            DebugLog.Write($"MonitorIdentity: DisplaySettingsChanged subscribe failed: {ex.GetType().Name}");
            s_subscribed = true; // 毎回試行しない
        }
    }

    private static void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        Invalidate();
        LogTable("DisplaySettingsChanged");
    }

    private static IReadOnlyList<MonitorInfo> Build()
    {
        var result = new List<MonitorInfo>();
        try
        {
            foreach (var screen in Screen.AllScreens)
            {
                var (key, friendly) = TryGetStableKey(screen.DeviceName);
                result.Add(new MonitorInfo(screen.DeviceName, key, friendly, screen.Bounds));
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"MonitorIdentity.Build failed: {ex.GetType().Name}: {ex.Message}");
        }
        return result;
    }

    /// <summary>
    /// <c>\.\DISPLAYn</c> に紐づくモニタのデバイスインターフェースパスとフレンドリ名を取得する。
    /// 失敗時は両方空文字（呼出側は従来のデバイス名照合へフォールバックする）。
    /// </summary>
    private static (string Key, string FriendlyName) TryGetStableKey(string gdiDeviceName)
    {
        try
        {
            var dd = new NativeMethods.DISPLAY_DEVICE();
            dd.cb = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.DISPLAY_DEVICE>();

            // iDevNum=0: そのアダプタ出力に接続されている最初のモニタ
            if (!NativeMethods.EnumDisplayDevices(
                    gdiDeviceName, 0, ref dd, NativeMethods.EDD_GET_DEVICE_INTERFACE_NAME))
            {
                return (string.Empty, string.Empty);
            }

            string key = dd.DeviceID ?? string.Empty;
            string friendly = dd.DeviceString ?? string.Empty;
            return (key, friendly);
        }
        catch (Exception ex)
        {
            DebugLog.Write($"MonitorIdentity.TryGetStableKey({gdiDeviceName}) failed: {ex.GetType().Name}");
            return (string.Empty, string.Empty);
        }
    }
}
