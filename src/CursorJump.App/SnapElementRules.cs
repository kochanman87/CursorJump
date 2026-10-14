using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace CursorJump.App;

/// <summary>
/// 要素スナップの「どの要素を対象にするか」「学習でどう同一視するか」を決める純粋関数群。
///
/// 部品クラス名（UIA の ClassName）は 2026-10-14 の実測に基づく:
/// Chromium 系ブラウザ（Chrome / Edge）はブラウザ自体の部品に BookmarkButton / TabCloseButton 等の
/// クラス名を公開する。Web ページ内の要素はクラス名が HTML の class 属性（長く不安定）になる。
/// </summary>
internal static class SnapElementRules
{
    /// <summary>ブックマークバー（お気に入りバー）の部品。既定でジャンプ先から外す。</summary>
    private static readonly HashSet<string> BookmarkBarClasses = new(StringComparer.Ordinal)
    {
        "BookmarkButton", "BookmarkFolderButton", "BookmarkMenuButtonBase",
    };

    /// <summary>拡張機能アイコン。外さないが選ばれにくくする。</summary>
    private static readonly HashSet<string> ExtensionClasses = new(StringComparer.Ordinal)
    {
        "ToolbarActionView", "PinnedActionToolbarButton", "ExtensionsToolbarButton",
    };

    /// <summary>
    /// 名前が中身ごとに変わる部品（タブの名前 = ページタイトル）。学習ではクラス名だけで同一視する。
    /// 対してタブの × (TabCloseButton) は全タブ同じ名前なので通常どおり扱う（実測）。
    /// </summary>
    private static readonly HashSet<string> NameVariesClasses = new(StringComparer.Ordinal)
    {
        "Tab",
    };

    private static readonly HashSet<string> BrowserProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "brave", "vivaldi", "opera",
    };

    public static bool IsBrowser(string processName) => BrowserProcesses.Contains(processName ?? "");

    public static bool IsBookmarkBar(string className) => BookmarkBarClasses.Contains(className ?? "");

    public static double BaseWeight(string className)
        => ExtensionClasses.Contains(className ?? "") ? SnapTuning.ExtensionButtonWeight : 1.0;

    /// <summary>アプリの部品クラス名らしいか（空白を含まない短い識別子）。HTML の class 属性は除外される。</summary>
    public static bool IsUiClassName(string className)
    {
        if (string.IsNullOrEmpty(className) || className.Length > 64) return false;
        foreach (char ch in className)
            if (char.IsWhiteSpace(ch)) return false;
        return true;
    }

    /// <summary>
    /// 要素名を学習キー用に正規化する: 前後空白除去・連続空白を 1 つに・数字を # に（件数表示などの変動を吸収）・長さ上限。
    /// </summary>
    public static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var sb = new StringBuilder(name.Length);
        bool prevSpace = false;
        foreach (char ch in name.Trim())
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!prevSpace) sb.Append(' ');
                prevSpace = true;
                continue;
            }
            prevSpace = false;
            sb.Append(char.IsDigit(ch) ? '#' : ch);
            if (sb.Length >= SnapTuning.MaxNameLength) break;
        }
        return sb.ToString();
    }

    /// <summary>
    /// 学習データの照合キー（SHA256 の先頭 16 バイトの hex）。同一視できる情報が無ければ null（学習しない）。
    ///
    /// - Web ページ内 (<paramref name="isWeb"/>): ブラウザ + ドメイン + 種類 + 名前（無ければ AutomationId）。
    ///   HTML の class 属性は不安定なので使わない
    /// - それ以外: アプリ + 種類 + 部品クラス名 + 名前（無ければ AutomationId）。
    ///   名前が中身で変わる部品（タブ）はクラス名だけ
    ///
    /// 名前を AutomationId より優先するのは、Web の id 属性は動的生成されることが多く再訪時に一致しないため。
    /// 平文を保存しないためにハッシュ化するが、短い名前は総当たりで推測できるので強い保護ではない。
    /// </summary>
    public static string? BuildLearnKey(
        string processName, int controlTypeId, string className, string automationId, string name,
        bool isWeb, string domain)
    {
        string label = NormalizeName(name);
        string id = label.Length > 0 ? "n:" + label
                  : !string.IsNullOrWhiteSpace(automationId) ? "a:" + automationId.Trim()
                  : "";
        string proc = (processName ?? "").ToLowerInvariant();

        string raw;
        if (isWeb)
        {
            if (id.Length == 0 || string.IsNullOrEmpty(domain)) return null;
            raw = $"web|{proc}|{domain.ToLowerInvariant()}|{controlTypeId}|{id}";
        }
        else
        {
            string cls = IsUiClassName(className) ? className : "";
            if (NameVariesClasses.Contains(cls)) id = "";
            if (cls.Length == 0 && id.Length == 0) return null;
            raw = $"app|{proc}|{controlTypeId}|{cls}|{id}";
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash, 0, 16).ToLowerInvariant();
    }

    /// <summary>URL 文字列（アドレスバーの表示値。スキーム省略あり）からホスト名を取り出す。取れなければ空。</summary>
    public static string HostFromUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "";
        var s = url.Trim();
        if (!s.Contains("://", StringComparison.Ordinal)) s = "https://" + s;
        if (!Uri.TryCreate(s, UriKind.Absolute, out var uri)) return "";
        var host = uri.Host;
        return host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
    }
}
