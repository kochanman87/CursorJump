using System;
using System.Drawing;
using CursorJump.App;
using Xunit;

namespace CursorJump.Tests;

public class SnapTargetSelectorTests
{
    private static SnapCandidate Box(int x, int y, int w = 20, int h = 20,
        SnapElementKind kind = SnapElementKind.Other, Point? cp = null)
        => new SnapCandidate(new Rectangle(x, y, w, h), kind, cp, IntPtr.Zero);

    private static readonly Point Origin = new(500, 500);

    [Fact]
    public void Center_IsDefaultTarget()
    {
        Assert.Equal(new Point(110, 220), SnapTargetSelector.TargetPoint(Box(100, 200, 20, 40)));
    }

    [Fact]
    public void ClickablePoint_InsideBounds_IsUsed()
    {
        var c = Box(100, 100, 50, 20, cp: new Point(105, 108));
        Assert.Equal(new Point(105, 108), SnapTargetSelector.TargetPoint(c));
    }

    [Fact]
    public void ClickablePoint_OutsideBounds_IsIgnored()
    {
        var c = Box(100, 100, 50, 20, cp: new Point(10, 10));
        Assert.Equal(new Point(125, 110), SnapTargetSelector.TargetPoint(c));
    }

    [Fact]
    public void WideRadioWithoutClickablePoint_TargetsLeftSquare()
    {
        // WinForms 実測: ラベル込みで 105x16、クリック可能点なし
        var c = Box(200, 300, 105, 16, SnapElementKind.RadioButton);
        Assert.Equal(new Point(208, 308), SnapTargetSelector.TargetPoint(c));
    }

    [Fact]
    public void SquareRadio_TargetsCenter()
    {
        var c = Box(200, 300, 22, 23, SnapElementKind.RadioButton);
        Assert.Equal(new Point(211, 311), SnapTargetSelector.TargetPoint(c));
    }

    [Fact]
    public void WideButton_IsNotLeftAligned()
    {
        // 横長補正はラジオ/チェックボックスだけ
        Assert.Equal(new Point(250, 308), SnapTargetSelector.TargetPoint(Box(200, 300, 100, 16)));
    }

    [Fact]
    public void PicksNearestInDirection()
    {
        var near = Box(590, 490);   // 中心 (600,500)
        var far = Box(790, 490);    // 中心 (800,500)
        var ranked = SnapTargetSelector.RankInDirection(Origin, 1, 0, new[] { far, near });
        Assert.Equal(new[] { near, far }, ranked);
    }

    [Fact]
    public void ExcludesOppositeDirection()
    {
        var left = Box(390, 490);
        Assert.Empty(SnapTargetSelector.RankInDirection(Origin, 1, 0, new[] { left }));
    }

    [Fact]
    public void ExcludesOutsideCone()
    {
        // 右へ 100、上へ 150 = 方向から 56° ずれ（45° の外）
        var offAxis = Box(590, 340);
        Assert.Empty(SnapTargetSelector.RankInDirection(Origin, 1, 0, new[] { offAxis }));
    }

    [Fact]
    public void PrefersAlignedOverSlightlyCloserOffAxis()
    {
        var aligned = Box(710, 490);   // 中心 (720,500): 前方 220, 横 0
        var offAxis = Box(670, 590);   // 中心 (680,600): 前方 180, 横 100 → 180+200=380
        var ranked = SnapTargetSelector.RankInDirection(Origin, 1, 0, new[] { offAxis, aligned });
        Assert.Equal(aligned, ranked[0]);
    }

    [Fact]
    public void ExcludesElementUnderCursor()
    {
        // カーソルが乗っている横長要素（中心は右側にあるが除外する）
        var under = Box(450, 490, 300, 20);
        var next = Box(800, 490);
        var ranked = SnapTargetSelector.RankInDirection(Origin, 1, 0, new[] { under, next });
        Assert.Equal(new[] { next }, ranked);
    }

    [Fact]
    public void WorksInAnyDirection()
    {
        var up = Box(490, 290);      // 真上
        var ranked = SnapTargetSelector.RankInDirection(Origin, 0, -1, new[] { up });
        Assert.Single(ranked);
    }

    [Fact]
    public void LearnedWeight_PrefersFrequentlyClickedFartherElement()
    {
        var near = Box(590, 490);                         // 前方 100
        var far = Box(790, 490) with { LearnKey = "fav" }; // 前方 300、よく押す
        var ranked = SnapTargetSelector.RankInDirection(Origin, 1, 0, new[] { near, far },
            c => c.LearnKey == "fav" ? 0.3 : 1.0);
        Assert.Equal(far, ranked[0]);
    }

    [Fact]
    public void BaseWeight_PushesElementBack()
    {
        var ext = Box(590, 490) with { BaseWeight = 1.5 }; // 前方 100 → 150 扱い
        var next = Box(710, 490);                           // 前方 220
        var plain = Box(670, 490);                          // 前方 180
        var ranked = SnapTargetSelector.RankInDirection(Origin, 1, 0, new[] { next, plain, ext });
        Assert.Equal(new[] { ext, plain, next }, ranked);   // 150 < 180 < 220

        var heavier = Box(590, 490) with { BaseWeight = 2.0 }; // 200 扱い
        ranked = SnapTargetSelector.RankInDirection(Origin, 1, 0, new[] { next, plain, heavier });
        Assert.Equal(new[] { plain, heavier, next }, ranked);
    }

    [Fact]
    public void ZeroVectorOrEmpty_ReturnsEmpty()
    {
        Assert.Empty(SnapTargetSelector.RankInDirection(Origin, 0, 0, new[] { Box(600, 490) }));
        Assert.Empty(SnapTargetSelector.RankInDirection(Origin, 1, 0, Array.Empty<SnapCandidate>()));
    }
}
