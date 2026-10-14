using System;
using System.IO;
using CursorJump.App;
using Xunit;

namespace CursorJump.Tests;

public class SnapLearningStoreTests
{
    private static readonly DateTime T0 = new(2026, 10, 14, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void UnknownKey_HasNeutralWeight()
    {
        var s = new SnapLearningStore();
        Assert.Equal(1.0, s.Weight("nope", T0));
        Assert.Equal(1.0, s.Weight(null, T0));
    }

    [Fact]
    public void MoreClicks_SmallerWeight()
    {
        var s = new SnapLearningStore();
        s.Record("a", T0);
        for (int i = 0; i < 7; i++) s.Record("b", T0);
        Assert.True(s.Weight("a", T0) < 1.0);
        Assert.True(s.Weight("b", T0) < s.Weight("a", T0));
        // 7 回 → 1/(1+0.5*log2(8)) = 0.4
        Assert.Equal(0.4, s.Weight("b", T0), 3);
    }

    [Fact]
    public void Score_HalvesAfterHalfLife()
    {
        var s = new SnapLearningStore();
        s.Record("a", T0, 8);
        Assert.Equal(4.0, s.Score("a", T0.AddDays(SnapTuning.LearningHalfLifeDays)), 6);
    }

    [Fact]
    public void Record_AddsToDecayedScore()
    {
        var s = new SnapLearningStore();
        s.Record("a", T0, 8);
        s.Record("a", T0.AddDays(SnapTuning.LearningHalfLifeDays));
        Assert.Equal(5.0, s.Score("a", T0.AddDays(SnapTuning.LearningHalfLifeDays)), 6);
    }

    [Fact]
    public void EmptyKey_IsIgnored()
    {
        var s = new SnapLearningStore();
        s.Record("", T0);
        s.Record(null, T0);
        Assert.Equal(0, s.Count);
    }

    [Fact]
    public void Prune_KeepsHighScores()
    {
        var s = new SnapLearningStore();
        s.Record("keep", T0, 100);
        for (int i = 0; i <= SnapTuning.LearningMaxEntries; i++) s.Record("k" + i, T0);
        Assert.True(s.Count <= SnapTuning.LearningMaxEntries);
        Assert.Equal(100, s.Score("keep", T0), 6);
    }

    [Fact]
    public void Serialize_RoundTrips()
    {
        var s = new SnapLearningStore();
        s.Record("a", T0, 3);
        var r = SnapLearningStore.Deserialize(s.Serialize());
        Assert.Equal(3.0, r.Score("a", T0), 6);
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("null")]
    [InlineData("")]
    public void Deserialize_Corrupt_ReturnsEmpty(string json)
        => Assert.Equal(0, SnapLearningStore.Deserialize(json).Count);

    [Fact]
    public void File_SaveLoad_AndCorruptFileIsBackedUp()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cj-snap-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "snap-learning.json");
            Assert.Equal(0, SnapLearningStore.LoadFile(path).Count); // 無ければ空

            var s = new SnapLearningStore();
            s.Record("a", T0, 2);
            s.SaveFile(path);
            Assert.Equal(2.0, SnapLearningStore.LoadFile(path).Score("a", T0), 6);
            Assert.False(File.Exists(path + ".tmp"));

            File.WriteAllText(path, "{broken");
            Assert.Equal(0, SnapLearningStore.LoadFile(path).Count);
            Assert.Equal("{broken", File.ReadAllText(path + ".bad"));
        }
        finally
        {
            // テスト専用に作った一時フォルダのみ削除
            Directory.Delete(dir, recursive: true);
        }
    }
}
