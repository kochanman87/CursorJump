using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
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
        // 既定の JsonStringEnumConverter は未知の文字列で例外を投げ、Load が設定全体（ライセンスキー含む）を
        // 初期化してしまう。削除・改名された enum 値が settings.json に残っていても安全に読み込めるよう、
        // 未知値を既定値として読み飛ばす寛容なコンバータを使う（v1.9.1 恒久対策）。
        Converters = { new TolerantEnumConverterFactory() }
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

/// <summary>
/// enum を文字列で読み書きする寛容なコンバータ。読み込み時、未知・削除された値に出会っても
/// 例外を投げず既定値(default)にフォールバックする。これにより settings.json の 1 フィールドが
/// 古い enum 値でも、設定全体（ライセンスキー・座標など）が失われない（v1.9.1）。
/// 書き込み形式は標準の JsonStringEnumConverter と同じ（Flags はカンマ区切り）。
/// </summary>
internal sealed class TolerantEnumConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        => (JsonConverter)Activator.CreateInstance(
            typeof(TolerantEnumConverter<>).MakeGenericType(typeToConvert))!;
}

internal sealed class TolerantEnumConverter<T> : JsonConverter<T> where T : struct, Enum
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                string? s = reader.GetString();
                // "Mouse, Keyboard" のような Flags 結合も Enum.TryParse が解釈する。
                // 未知値（削除された enum 名等）は false → 既定値にフォールバック。
                return !string.IsNullOrEmpty(s) && Enum.TryParse<T>(s, ignoreCase: true, out var v)
                    ? v
                    : default;
            case JsonTokenType.Number when reader.TryGetInt64(out long n):
                return (T)Enum.ToObject(typeof(T), n); // 数値表現も後方互換で許容
            default:
                reader.Skip();
                return default;
        }
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString());
}
