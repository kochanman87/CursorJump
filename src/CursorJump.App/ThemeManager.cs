using System;
using System.Linq;
using System.Windows;
using CursorJump.App.Models;

namespace CursorJump.App;

/// <summary>
/// ライト/ダークテーマを ResourceDictionary 差し替えで切替する。
/// App.xaml の MergedDictionaries 先頭 (index 0) をテーマ辞書と想定。
/// </summary>
public static class ThemeManager
{
    private const string LightThemeUri = "Themes/LightTheme.xaml";
    private const string DarkThemeUri = "Themes/DarkTheme.xaml";

    public static UiTheme Current { get; private set; } = UiTheme.Light;

    public static void Apply(UiTheme theme)
    {
        var app = Application.Current;
        if (app is null) return;

        var merged = app.Resources.MergedDictionaries;
        if (merged.Count == 0) return;

        string uri = theme == UiTheme.Dark ? DarkThemeUri : LightThemeUri;
        var newDict = new ResourceDictionary
        {
            Source = new Uri(uri, UriKind.Relative)
        };

        // 先頭の辞書がテーマ辞書（App.xaml の定義順で index 0）
        merged[0] = newDict;
        Current = theme;
    }
}
