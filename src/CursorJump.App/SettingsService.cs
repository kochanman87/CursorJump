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

    public AppSettings Current { get; private set; } = new();

    public event Action? SettingsChanged;

    public void Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                string json = File.ReadAllText(SettingsPath);
                Current = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"SettingsService.Load failed: {ex.GetType().Name}: {ex.Message}");
            Current = new AppSettings();
        }
    }

    public bool Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            string json = JsonSerializer.Serialize(settings, JsonOptions);
            string tempPath = SettingsPath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, SettingsPath, overwrite: true);
            DebugLog.Write($"SettingsService.Save: OK → {SettingsPath}");
        }
        catch (Exception ex)
        {
            DebugLog.Write($"SettingsService.Save failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }

        Current = settings;
        SettingsChanged?.Invoke();
        return true;
    }
}
