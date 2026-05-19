using System;
using System.IO;
using System.Text.Json;
using CursorJump.App.Models;

namespace CursorJump.App;

public sealed class SettingsService
{
    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CursorJump");

    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private volatile AppSettings _current = new();
    public AppSettings Current => _current;

    public void Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                string json = File.ReadAllText(SettingsPath);
                _current = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"SettingsService.Load failed: {ex.GetType().Name}: {ex.Message}");
            _current = new AppSettings();
        }

        // v1.6.1: 旧 WheelUp/WheelDown 割当を新 MouseWheel に自動移行（上下方向中立で前後ナビできるように）。
        // 移行が発生した場合は即座に書き戻して以降のアプリ起動でも維持する。
        if (MigrateWheelShortcuts(_current))
        {
            DebugLog.Write("SettingsService.Load: migrated WheelUp/WheelDown shortcuts to MouseWheel");
            Save(_current);
        }
    }

    /// <summary>
    /// 旧 WheelUp / WheelDown ボタン割当を新 MouseWheel に書き換える。
    /// </summary>
    private static bool MigrateWheelShortcuts(AppSettings s)
    {
        bool changed = false;
        changed |= MigrateShortcut(s.SaveShortcut);
        changed |= MigrateShortcut(s.NavigateShortcut);
        changed |= MigrateShortcut(s.NavigateCurrentMonitorShortcut);
        changed |= MigrateShortcut(s.DisplayDeleteShortcut);
        changed |= MigrateShortcut(s.SaveShortcutB);
        changed |= MigrateShortcut(s.NavigateShortcutB);
        return changed;
    }

    private static bool MigrateShortcut(ActionShortcut sc)
    {
        if (sc.MouseButton is MouseButtonType.WheelUp or MouseButtonType.WheelDown)
        {
            sc.MouseButton = MouseButtonType.MouseWheel;
            return true;
        }
        return false;
    }

    public bool Save(AppSettings settings)
    {
        string tempPath = SettingsPath + ".tmp";
        try
        {
            Directory.CreateDirectory(SettingsDir);
            string json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, SettingsPath, overwrite: true);
            DebugLog.Write($"SettingsService.Save: OK → {SettingsPath}");
        }
        catch (Exception ex)
        {
            DebugLog.Write($"SettingsService.Save failed: {ex.GetType().Name}: {ex.Message}");
            try { File.Delete(tempPath); } catch { }
            return false;
        }

        _current = settings;
        return true;
    }
}
