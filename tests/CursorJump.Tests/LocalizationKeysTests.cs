using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace CursorJump.Tests;

/// <summary>
/// UI で参照している文言キー（Str.*）が日本語・英語の両方の辞書に存在するかを確認する。
/// v1.10.1 で Str.About.AutoStart / AutoStartHint が英語辞書に無く、英語 UI で見出しが空欄になっていた再発防止。
/// </summary>
public class LocalizationKeysTests
{
    // ソース中の説明文に出てくる例示キー（実在しない）
    private static readonly HashSet<string> IgnoredKeys = new(StringComparer.Ordinal) { "Str.Xxx" };

    private static string AppDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "CursorJump.App")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src", "CursorJump.App");
    }

    private static HashSet<string> DictionaryKeys(string fileName)
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var doc = XDocument.Load(Path.Combine(AppDir(), "Localization", fileName));
        return doc.Root!.Elements()
            .Select(e => (string?)e.Attribute(x + "Key"))
            .Where(k => k is not null)
            .ToHashSet(StringComparer.Ordinal)!;
    }

    private static HashSet<string> ReferencedKeys()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var xamlRef = new Regex(@"DynamicResource\s+(Str\.[A-Za-z0-9_.]+)");
        var csRef = new Regex("\"(Str\\.[A-Za-z0-9_.]+)\"");
        foreach (var file in Directory.EnumerateFiles(AppDir(), "*.*", SearchOption.AllDirectories))
        {
            if (file.Contains(Path.DirectorySeparatorChar + "Localization" + Path.DirectorySeparatorChar)) continue;
            if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)) continue;
            if (file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)) continue;
            var regex = file.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase) ? xamlRef
                      : file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ? csRef
                      : null;
            if (regex is null) continue;
            foreach (Match m in regex.Matches(File.ReadAllText(file)))
            {
                var key = m.Groups[1].Value.TrimEnd('.');
                if (!IgnoredKeys.Contains(key)) keys.Add(key);
            }
        }
        return keys;
    }

    [Theory]
    [InlineData("StringsJa.xaml")]
    [InlineData("StringsEn.xaml")]
    public void AllReferencedKeys_ExistInDictionary(string fileName)
    {
        var dict = DictionaryKeys(fileName);
        // 末尾が "." のものは文字列連結で組み立てるプレフィックスなので除外済み（TrimEnd 後に辞書の前方一致で確認）
        var missing = ReferencedKeys()
            .Where(k => !dict.Contains(k) && !dict.Any(d => d.StartsWith(k + ".", StringComparison.Ordinal)))
            .OrderBy(k => k)
            .ToList();
        Assert.True(missing.Count == 0, $"{fileName} に無いキー: {string.Join(", ", missing)}");
    }
}
