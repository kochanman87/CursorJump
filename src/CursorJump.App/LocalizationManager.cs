using System;
using System.Globalization;
using System.Windows;
using CursorJump.App.Models;

namespace CursorJump.App;

/// <summary>
/// 日本語/英語の言語リソース辞書を ResourceDictionary 差し替えで切替する。
/// App.xaml の MergedDictionaries の index 1 を言語辞書と想定。
/// 全 UI は {DynamicResource Str.Xxx} で参照することで言語切替時に自動再評価される。
/// </summary>
public static class LocalizationManager
{
    private const string JaUri = "Localization/StringsJa.xaml";
    private const string EnUri = "Localization/StringsEn.xaml";
    private const int LocalizationDictionaryIndex = 1;

    /// <summary>現在実際に表示している言語（Auto を解決済み）。</summary>
    public static UiLanguage CurrentResolved { get; private set; } = UiLanguage.Japanese;

    /// <summary>ユーザー設定値（Auto を含む）。</summary>
    public static UiLanguage CurrentSetting { get; private set; } = UiLanguage.Auto;

    /// <summary>言語切替後に発火（Apply 呼び出し時、変更の有無にかかわらず通知）。</summary>
    public static event Action? LanguageChanged;

    /// <summary>UiLanguage.Auto を OS の UI 言語に応じて Japanese / English に解決する。</summary>
    public static UiLanguage Resolve(UiLanguage setting)
    {
        if (setting == UiLanguage.Japanese) return UiLanguage.Japanese;
        if (setting == UiLanguage.English)  return UiLanguage.English;

        // Auto: ja-JP 等を日本語、それ以外を英語
        var culture = CultureInfo.CurrentUICulture;
        if (string.Equals(culture.TwoLetterISOLanguageName, "ja", StringComparison.OrdinalIgnoreCase))
            return UiLanguage.Japanese;
        return UiLanguage.English;
    }

    public static void Apply(UiLanguage setting)
    {
        var app = Application.Current;
        if (app is null) return;

        var resolved = Resolve(setting);
        string uri = resolved == UiLanguage.English ? EnUri : JaUri;

        var merged = app.Resources.MergedDictionaries;
        if (merged.Count <= LocalizationDictionaryIndex) return;

        var newDict = new ResourceDictionary { Source = new Uri(uri, UriKind.Relative) };
        merged[LocalizationDictionaryIndex] = newDict;

        CurrentSetting = setting;
        CurrentResolved = resolved;

        LanguageChanged?.Invoke();
    }
}

/// <summary>
/// C# コードから現在言語の文字列を取得するヘルパー。
/// XAML では DynamicResource で直接参照するので不要。
/// </summary>
public static class Loc
{
    /// <summary>キーに対応する文字列を取得。見つからない場合はキー自体を返す（デバッグ用）。</summary>
    public static string Get(string key)
    {
        var app = Application.Current;
        if (app is null) return key;

        var dispatcher = app.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            return GetCore(key);
        }

        return dispatcher.Invoke(() => GetCore(key));
    }

    private static string GetCore(string key)
    {
        var app = Application.Current;
        if (app is null) return key;
        var value = app.TryFindResource(key);
        return value as string ?? key;
    }
}
