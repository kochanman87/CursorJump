using CursorJump.App;
using CursorJump.App.Models;
using Xunit;

namespace CursorJump.Tests;

public class ModifierGestureDetectorTests
{
    // 低レベルフックが報告する具体的な L/R 修飾キー vk。
    private const int VkLCtrl = 0xA2;
    private const int VkLShift = 0xA0;
    private const int VkLAlt = 0xA4;
    private const int VkA = 0x41; // 非修飾キー 'A'

    /// <summary>1 タップ（down→up）を投入し、完了したジェスチャ（あれば）を返す。</summary>
    private static ModifierGesture? Tap(ModifierGestureDetector d, int vk, ref long t, long hold = 50, long gapAfter = 100)
    {
        d.Feed(vk, true, t);
        var g = d.Feed(vk, false, t + hold);
        t += hold + gapAfter;
        return g;
    }

    [Fact]
    public void CtrlDoubleTap_is_detected()
    {
        var d = new ModifierGestureDetector();
        long t = 0;
        Assert.Null(Tap(d, VkLCtrl, ref t));
        var g = Tap(d, VkLCtrl, ref t);
        Assert.Equal(ModifierGesture.CtrlDoubleTap, g);
    }

    [Fact]
    public void ShiftDoubleTap_is_detected()
    {
        var d = new ModifierGestureDetector();
        long t = 0;
        Assert.Null(Tap(d, VkLShift, ref t));
        Assert.Equal(ModifierGesture.ShiftDoubleTap, Tap(d, VkLShift, ref t));
    }

    [Fact]
    public void AltDoubleTap_is_detected()
    {
        var d = new ModifierGestureDetector();
        long t = 0;
        Assert.Null(Tap(d, VkLAlt, ref t));
        Assert.Equal(ModifierGesture.AltDoubleTap, Tap(d, VkLAlt, ref t));
    }

    [Fact]
    public void Different_modifier_taps_do_not_combine()
    {
        var d = new ModifierGestureDetector();
        long t = 0;
        // Ctrl タップの直後に別の修飾キー（Shift）をタップしても、同一キーのダブルタップではないので発火しない
        Assert.Null(Tap(d, VkLCtrl, ref t));
        Assert.Null(Tap(d, VkLShift, ref t));
    }

    [Fact]
    public void Simultaneous_chord_does_not_trigger()
    {
        var d = new ModifierGestureDetector();
        // Ctrl+Shift+Alt+A を同時押し → 連続タップではないので何も発火しない
        Assert.Null(d.Feed(VkLCtrl, true, 0));
        Assert.Null(d.Feed(VkLShift, true, 10));
        Assert.Null(d.Feed(VkLAlt, true, 20));
        Assert.Null(d.Feed(VkA, true, 30));
        Assert.Null(d.Feed(VkA, false, 40));
        Assert.Null(d.Feed(VkLAlt, false, 50));
        Assert.Null(d.Feed(VkLShift, false, 60));
        Assert.Null(d.Feed(VkLCtrl, false, 70));
    }

    [Fact]
    public void NonModifier_key_resets_sequence()
    {
        var d = new ModifierGestureDetector();
        long t = 0;
        Assert.Null(Tap(d, VkLCtrl, ref t)); // [Ctrl]
        // 間に通常キーが挟まる → シーケンスがリセット
        d.Feed(VkA, true, t); d.Feed(VkA, false, t + 20); t += 120;
        // 次の Ctrl タップは 1 つ目扱い → ダブルにならない
        Assert.Null(Tap(d, VkLCtrl, ref t));
    }

    [Fact]
    public void Gap_too_long_breaks_sequence()
    {
        var d = new ModifierGestureDetector();
        long t = 0;
        Assert.Null(Tap(d, VkLCtrl, ref t, hold: 50, gapAfter: 600)); // 次タップまで 600ms 空く
        Assert.Null(Tap(d, VkLCtrl, ref t)); // 間隔超過でシーケンス切れ → 単発扱い
    }

    [Fact]
    public void Does_not_refire_on_extra_tap_after_match()
    {
        var d = new ModifierGestureDetector();
        long t = 0;
        Assert.Null(Tap(d, VkLCtrl, ref t));
        Assert.Equal(ModifierGesture.CtrlDoubleTap, Tap(d, VkLCtrl, ref t)); // 発火
        Assert.Null(Tap(d, VkLCtrl, ref t)); // 直後の 1 タップでは再発火しない
    }

    [Fact]
    public void Long_hold_is_not_a_tap()
    {
        var d = new ModifierGestureDetector();
        long t = 0;
        // 350ms 保持 = タップとみなさない
        Assert.Null(Tap(d, VkLCtrl, ref t, hold: 350));
        Assert.Null(Tap(d, VkLCtrl, ref t, hold: 350));
    }
}
