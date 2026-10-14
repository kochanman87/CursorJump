using CursorJump.App;
using Xunit;

namespace CursorJump.Tests;

public class SnapElementRulesTests
{
    private const int Button = 50000;     // UIA_ButtonControlTypeId
    private const int TabItem = 50019;
    private const int Hyperlink = 50005;

    [Theory]
    [InlineData("BookmarkButton", true)]
    [InlineData("BookmarkFolderButton", true)]
    [InlineData("BookmarkMenuButtonBase", true)]
    [InlineData("BackForwardButton", false)]
    [InlineData("TabCloseButton", false)]
    [InlineData("", false)]
    public void BookmarkBar_IsDetectedByClassName(string cls, bool expected)
        => Assert.Equal(expected, SnapElementRules.IsBookmarkBar(cls));

    [Fact]
    public void ExtensionIcons_AreWeightedDown()
    {
        Assert.Equal(SnapTuning.ExtensionButtonWeight, SnapElementRules.BaseWeight("ToolbarActionView"));
        Assert.Equal(1.0, SnapElementRules.BaseWeight("BackForwardButton"));
    }

    [Theory]
    [InlineData("chrome", true)]
    [InlineData("MSEDGE", true)]
    [InlineData("WINWORD", false)]
    public void Browser_IsDetectedByProcess(string proc, bool expected)
        => Assert.Equal(expected, SnapElementRules.IsBrowser(proc));

    [Theory]
    [InlineData("NetUIRibbonButton", true)]
    [InlineData("style-scope ytd-guide-entry-renderer", false)] // HTML の class 属性
    [InlineData("", false)]
    public void UiClassName_ExcludesHtmlClassAttributes(string cls, bool expected)
        => Assert.Equal(expected, SnapElementRules.IsUiClassName(cls));

    [Fact]
    public void NormalizeName_CollapsesSpacesAndMasksDigits()
    {
        Assert.Equal("受信トレイ (#)", SnapElementRules.NormalizeName("  受信トレイ   (3) "));
        Assert.Equal("受信トレイ (##)", SnapElementRules.NormalizeName("受信トレイ (12)"));
        Assert.Equal("", SnapElementRules.NormalizeName("   "));
    }

    [Fact]
    public void NormalizeName_IsCapped()
    {
        Assert.Equal(SnapTuning.MaxNameLength, SnapElementRules.NormalizeName(new string('a', 500)).Length);
    }

    [Fact]
    public void Key_SameElementSameKey_DifferentAppsDifferentKeys()
    {
        var a = SnapElementRules.BuildLearnKey("WINWORD", Button, "NetUIRibbonButton", "", "太字", false, "");
        var b = SnapElementRules.BuildLearnKey("winword", Button, "NetUIRibbonButton", "", "太字", false, "");
        var c = SnapElementRules.BuildLearnKey("EXCEL", Button, "NetUIRibbonButton", "", "太字", false, "");
        Assert.NotNull(a);
        Assert.Equal(a, b);       // プロセス名の大小文字は区別しない
        Assert.NotEqual(a, c);
        Assert.Equal(32, a!.Length);
    }

    [Fact]
    public void Key_Tab_IgnoresNameBecauseItIsThePageTitle()
    {
        var t1 = SnapElementRules.BuildLearnKey("chrome", TabItem, "Tab", "", "YouTube", false, "");
        var t2 = SnapElementRules.BuildLearnKey("chrome", TabItem, "Tab", "", "GitHub", false, "");
        Assert.Equal(t1, t2);
    }

    [Fact]
    public void Key_BackAndForward_AreDistinct()
    {
        var back = SnapElementRules.BuildLearnKey("chrome", Button, "BackForwardButton", "", "戻る", false, "");
        var fwd = SnapElementRules.BuildLearnKey("chrome", Button, "BackForwardButton", "", "進む", false, "");
        Assert.NotEqual(back, fwd);
    }

    [Fact]
    public void Key_Web_IsPerDomain_AndIgnoresHtmlClass()
    {
        var a1 = SnapElementRules.BuildLearnKey("chrome", Hyperlink, "css-123 foo", "", "ログイン", true, "example.com");
        var a2 = SnapElementRules.BuildLearnKey("chrome", Hyperlink, "css-999 bar", "", "ログイン", true, "EXAMPLE.com");
        var other = SnapElementRules.BuildLearnKey("chrome", Hyperlink, "css-123 foo", "", "ログイン", true, "example.org");
        Assert.Equal(a1, a2);
        Assert.NotEqual(a1, other);
    }

    [Fact]
    public void Key_WebAndAppNeverCollide()
    {
        var web = SnapElementRules.BuildLearnKey("chrome", Button, "", "", "戻る", true, "example.com");
        var app = SnapElementRules.BuildLearnKey("chrome", Button, "", "", "戻る", false, "");
        Assert.NotEqual(web, app);
    }

    [Fact]
    public void Key_Null_WhenNothingIdentifies()
    {
        Assert.Null(SnapElementRules.BuildLearnKey("app", Button, "", "", "", false, ""));
        Assert.Null(SnapElementRules.BuildLearnKey("chrome", Hyperlink, "x y", "", "", true, "example.com"));
        Assert.Null(SnapElementRules.BuildLearnKey("chrome", Hyperlink, "", "", "ログイン", true, ""));
    }

    [Fact]
    public void Key_FallsBackToAutomationId_WhenNameIsEmpty()
    {
        var k1 = SnapElementRules.BuildLearnKey("app", Button, "", "btnSave", "", false, "");
        var k2 = SnapElementRules.BuildLearnKey("app", Button, "", "btnOpen", "", false, "");
        Assert.NotNull(k1);
        Assert.NotEqual(k1, k2);
    }

    [Theory]
    [InlineData("youtube.com/watch?v=x", "youtube.com")]
    [InlineData("https://www.example.com/a", "example.com")]
    [InlineData("gigazine.net", "gigazine.net")]
    [InlineData("", "")]
    [InlineData("検索語 です", "")]
    public void HostFromUrl(string url, string expected)
        => Assert.Equal(expected, SnapElementRules.HostFromUrl(url));
}
