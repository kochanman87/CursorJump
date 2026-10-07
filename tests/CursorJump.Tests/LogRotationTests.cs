using System;
using System.IO;
using CursorJump.App;
using Xunit;

namespace CursorJump.Tests;

// 本物の %APPDATA% のログには触れず、テストごとに作る一時フォルダだけで検証する。
public sealed class LogRotationTests : IDisposable
{
    private const long Max = 100;
    private readonly string _dir;
    private readonly string _log;
    private readonly string _old;

    public LogRotationTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "CursorJump.Tests.LogRotation." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _log = Path.Combine(_dir, "debug.log");
        _old = Path.Combine(_dir, "debug.log.old");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private static string Text(int length, char c) => new(c, length);

    [Fact]
    public void Below_limit_does_nothing()
    {
        File.WriteAllText(_log, Text(50, 'a'));

        long size = LogRotation.RotateIfNeeded(_log, _old, 50, Max);

        Assert.Equal(50, size);
        Assert.Equal(Text(50, 'a'), File.ReadAllText(_log));
        Assert.False(File.Exists(_old));
    }

    [Fact]
    public void Over_limit_moves_log_to_old_and_resets_size()
    {
        File.WriteAllText(_log, Text(120, 'a'));

        long size = LogRotation.RotateIfNeeded(_log, _old, 120, Max);

        Assert.Equal(0, size);
        Assert.False(File.Exists(_log));
        Assert.Equal(Text(120, 'a'), File.ReadAllText(_old));
    }

    [Fact]
    public void Existing_old_is_replaced_and_other_files_are_untouched()
    {
        File.WriteAllText(_log, Text(120, 'n'));
        File.WriteAllText(_old, "previous generation");
        // 同じフォルダの他ファイル (名前が似ているものを含む) は一切変わらないこと
        var settings = Path.Combine(_dir, "settings.json");
        var similar = Path.Combine(_dir, "debug.log.bak");
        var similar2 = Path.Combine(_dir, "debug.log.old.keep");
        File.WriteAllText(settings, "{\"LicenseKey\":\"x\"}");
        File.WriteAllText(similar, "bak");
        File.WriteAllText(similar2, "keep");

        LogRotation.RotateIfNeeded(_log, _old, 120, Max);

        Assert.Equal(Text(120, 'n'), File.ReadAllText(_old));
        Assert.Equal("{\"LicenseKey\":\"x\"}", File.ReadAllText(settings));
        Assert.Equal("bak", File.ReadAllText(similar));
        Assert.Equal("keep", File.ReadAllText(similar2));
        Assert.False(File.Exists(_log));
        Assert.Equal(4, Directory.GetFiles(_dir).Length);
    }

    [Fact]
    public void Stale_counter_rechecks_actual_size_and_does_not_rotate()
    {
        // 集計上は上限超えだが、実ファイルは外部で小さくなっている (別プロセスが先にローテートした等)
        File.WriteAllText(_log, Text(10, 'a'));
        File.WriteAllText(_old, "keep me");

        long size = LogRotation.RotateIfNeeded(_log, _old, 9_000_000, Max);

        Assert.Equal(10, size);
        Assert.Equal(Text(10, 'a'), File.ReadAllText(_log));
        Assert.Equal("keep me", File.ReadAllText(_old));
    }

    [Fact]
    public void Missing_log_does_not_throw_or_touch_old()
    {
        File.WriteAllText(_old, "keep me");

        long size = LogRotation.RotateIfNeeded(_log, _old, 500, Max);

        Assert.Equal(0, size);
        Assert.Equal("keep me", File.ReadAllText(_old));
    }

    [Fact]
    public void Same_path_for_log_and_old_is_refused()
    {
        File.WriteAllText(_log, Text(120, 'a'));

        long size = LogRotation.RotateIfNeeded(_log, _log, 120, Max);

        Assert.Equal(120, size);
        Assert.Equal(Text(120, 'a'), File.ReadAllText(_log));
    }

    [Fact]
    public void Missing_file_size_is_zero()
    {
        Assert.Equal(0, LogRotation.GetFileSize(_log));
    }

    [Fact]
    public void Limit_is_one_megabyte()
    {
        Assert.Equal(1L * 1024 * 1024, DebugLog.MaxLogBytes);
    }
}
