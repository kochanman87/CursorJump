using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace CursorJump.App;

/// <summary>
/// Windows サインイン時の自動起動を管理する。
/// レジストリ HKCU\Software\Microsoft\Windows\CurrentVersion\Run に値名 <see cref="ValueName"/> で
/// 現在の exe パスを書き込む / 削除する（管理者権限不要・per-user）。
/// Velopack インストール版のみ有効。dev 実行 (UpdateManager.IsInstalled == false) では何もしない。
/// 例外は全て握りつぶし DebugLog にのみ記録する（起動・保存処理を妨げないため）。
/// </summary>
internal static class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "CursorJump";

    /// <summary>レジストリ Run キーに登録済みかどうか。</summary>
    public static bool IsRegistered()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string s && !string.IsNullOrWhiteSpace(s);
        }
        catch (Exception ex)
        {
            DebugLog.Write($"StartupService.IsRegistered failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>現在の exe パスを Run キーに書き込む。dev 実行（インストール版でない）なら何もしない。</summary>
    public static void Register()
    {
        if (!IsInstalledByVelopack())
        {
            DebugLog.Write("StartupService.Register skipped: not installed by Velopack (dev run)");
            return;
        }
        string? exePath = GetCurrentExePath();
        if (string.IsNullOrEmpty(exePath))
        {
            DebugLog.Write("StartupService.Register skipped: failed to resolve exe path");
            return;
        }
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            // パスは引用符でくくる（スペース入りパス対応）
            key.SetValue(ValueName, $"\"{exePath}\"", RegistryValueKind.String);
            DebugLog.Write($"StartupService.Register: {exePath}");
        }
        catch (Exception ex)
        {
            DebugLog.Write($"StartupService.Register failed: {ex.Message}");
        }
    }

    /// <summary>Run キーから値を削除。存在しない場合は何もしない。</summary>
    public static void Unregister()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key?.GetValue(ValueName) is not null)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                DebugLog.Write("StartupService.Unregister: removed");
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"StartupService.Unregister failed: {ex.Message}");
        }
    }

    /// <summary>設定の AutoStartEnabled に応じてレジストリを同期する。設定保存後に呼ぶ。</summary>
    public static void ApplyAutoStart(bool enabled)
    {
        if (enabled) Register(); else Unregister();
    }

    /// <summary>
    /// 起動時に呼び出す同期処理。AutoStartEnabled == true の場合に、現在の exe パスで
    /// レジストリ値を上書きする（Velopack 更新で exe パスが変わったときに自動追従するため）。
    /// </summary>
    public static void SyncWithExePath(bool autoStartEnabled)
    {
        if (!autoStartEnabled)
        {
            // 設定 OFF だが過去に登録された値が残っている可能性をクリーンアップ
            if (IsRegistered()) Unregister();
            return;
        }
        Register();
    }

    private static string? GetCurrentExePath()
    {
        try
        {
            string? path = Process.GetCurrentProcess().MainModule?.FileName;
            return string.IsNullOrEmpty(path) ? null : path;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"StartupService.GetCurrentExePath failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Velopack インストール版かどうかの判定。exe パスが Velopack の current フォルダ配下にあるかで判定する。
    /// UpdateManager 経由でも判定できるが、StartupService はそれ自体の依存を避け軽量にする。
    /// </summary>
    private static bool IsInstalledByVelopack()
    {
        try
        {
            string? path = GetCurrentExePath();
            if (string.IsNullOrEmpty(path)) return false;
            // Velopack の標準配置: %LocalAppData%\CursorJump\current\CursorJump.App.exe
            return path.Contains(@"\current\", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
