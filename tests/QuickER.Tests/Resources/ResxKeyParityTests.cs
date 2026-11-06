using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Xml.Linq;
using AwesomeAssertions;
using QuickER.Resources;
using Xunit;

namespace QuickER.Tests.Resources;

/// <summary>
/// src 配下の resx について、中立リソース（<c>Strings.resx</c>＝英語）と日本語サテライト（<c>.ja.resx</c>）の
/// 書式指定子の一致を検証するガードテストと、resx の照合の行が使う読み取り器。新しい resx が増えても走査で自動的にカバーされる。
/// </summary>
/// <remarks>
/// <para>
/// 中立カルチャは英語（国際標準構成）。日本語は <c>Strings.ja.resx</c> サテライトで賦与する。
/// キー集合の一致・日本語サテライトの欠落・厳密型アクセサとキーの一致は照合の表（<c>ParityTests</c> の行
/// <c>resx-ja-key-sets</c>・<c>resx-ja-satellites</c>・<c>resx-accessor-properties</c>）が見る。
/// 新しい中立 resx を追加したら対応する日本語サテライトも必ず用意する必要がある。
/// </para>
/// </remarks>
public class ResxKeyParityTests
{
    /// <summary>
    /// QuickER 系アセンブリから、厳密型リソースアクセサ（静的 <c>ResourceManager</c> プロパティと
    /// 1 つ以上の public static string プロパティを持つ型）を列挙する。
    /// </summary>
    internal static IEnumerable<Type> FindResourceAccessorTypes()
    {
        // Strings を参照済みのため QuickER.Gui はロード済み。将来 resx を持つ他アセンブリが
        // 増えてもロード済みなら自動的に対象へ含める。
        // ただし本走査はアセンブリのロード有無に依存するため、resx を持つ機能 UI アセンブリは
        // typeof(...) で明示シード（参照）してロードを保証する（単独実行でも取りこぼさない）。
        var seededAssemblies = new[]
        {
            typeof(Strings).Assembly,
            typeof(QuickER.Gui.Common.Resources.Strings).Assembly,
            typeof(QuickER.AI.UI.Resources.Strings).Assembly,
            typeof(QuickER.AI.Chat.Resources.Strings).Assembly,
            typeof(QuickER.AI.Mock.Resources.Strings).Assembly,
            typeof(QuickER.CodeGen.UI.Resources.Strings).Assembly,
            typeof(QuickER.CodeReverse.CSharp.Resources.Strings).Assembly,
            typeof(QuickER.Db.UI.Resources.Strings).Assembly,
            typeof(QuickER.AI.Resources.Strings).Assembly,
            typeof(QuickER.CodeGen.CSharp.Resources.Strings).Assembly,
            typeof(QuickER.Provider.Resources.Strings).Assembly,
            typeof(QuickER.Cli.Resources.Strings).Assembly,
            typeof(QuickER.Platform.Resources.Strings).Assembly,
        };

        var assemblies = AppDomain
            .CurrentDomain.GetAssemblies()
            .Concat(seededAssemblies)
            .Where(a => a.GetName().Name?.StartsWith("QuickER", StringComparison.Ordinal) == true)
            .Distinct();

        foreach (var assembly in assemblies)
        {
            foreach (var type in assembly.GetTypes())
            {
                var managerProperty = type.GetProperty(
                    "ResourceManager",
                    BindingFlags.Public | BindingFlags.Static
                );

                if (managerProperty?.PropertyType != typeof(ResourceManager))
                {
                    continue;
                }

                var hasStringProperty = type.GetProperties(
                        BindingFlags.Public | BindingFlags.Static
                    )
                    .Any(p => p.PropertyType == typeof(string));

                if (hasStringProperty)
                {
                    yield return type;
                }
            }
        }
    }

    /// <summary>ResourceManager の不変カルチャ ResourceSet から文字列リソースのキー集合を読み出す</summary>
    /// <remarks>
    /// ResourceSet は ResourceManager が内部でキャッシュ・所有するため <c>Dispose</c> しない
    /// （破棄すると以降の <see cref="Strings"/> 参照が「closed resource set」で失敗する）。
    /// </remarks>
    internal static HashSet<string> ReadStringResourceKeys(ResourceManager resourceManager)
    {
        var set = resourceManager.GetResourceSet(
            CultureInfo.InvariantCulture,
            createIfNotExists: true,
            tryParents: true
        )!;

        return set.Cast<DictionaryEntry>()
            .Where(entry => entry.Value is string)
            .Select(entry => (string)entry.Key)
            .ToHashSet();
    }

    /// <summary>中立 resx（英語）とその日本語サテライトのペア</summary>
    internal sealed record ResxPair(string NeutralPath, string SatellitePath);

    /// <summary>src 配下の全 resx から、日本語サテライトを持つ中立 resx のペアを列挙する</summary>
    internal static IEnumerable<ResxPair> EnumerateResxPairs()
    {
        foreach (var neutral in NeutralResxFiles.EnumerateNeutral())
        {
            var japanese = NeutralResxFiles.ToJapaneseSatellitePath(neutral);

            if (File.Exists(japanese))
            {
                yield return new ResxPair(neutral, japanese);
            }
        }
    }

    /// <summary>resx の <c>&lt;data name="..."&gt;</c> エントリのキー集合を読み出す</summary>
    internal static HashSet<string> ReadKeys(string resxPath) =>
        NeutralResxFiles.ReadEntries(resxPath).Select(entry => entry.Name).ToHashSet();

    /// <summary>
    /// 同じキーの中立（英語）と日本語で、書式指定子（<c>{0}</c> / <c>{1}</c> …）の使用集合が一致することを検証する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>string.Format</c> は<b>余剰の引数を黙って捨てる</b>ため、日本語訳で <c>{2}</c> を書き忘れても
    /// 例外にならず、その項目だけ情報が欠けた文面が出る（逆に中立側だけが少ないときも同じ）。
    /// キー集合の一致では捕まらないので、指定子の集合まで突き合わせる。
    /// </para>
    /// <para>
    /// 見るのは<b>集合</b>であって出現順・回数ではない（訳文は語順を変えるのが普通で、同じ指定子を
    /// 2 回書くのも正当）。<c>{{</c> はリテラルの波括弧なので数えない。
    /// </para>
    /// </remarks>
    [Fact(DisplayName = "中立 resx と .ja.resx で書式指定子の使用集合が一致する")]
    public void NeutralAndJapaneseResx_UseTheSameFormatPlaceholders()
    {
        var pairs = EnumerateResxPairs().ToList();

        pairs.Should().NotBeEmpty("検証対象の中立 resx / .ja.resx ペアが 1 つも見つからない");

        foreach (var pair in pairs)
        {
            var neutral = NeutralResxFiles
                .ReadEntries(pair.NeutralPath)
                .ToDictionary(entry => entry.Name, entry => entry.Value, StringComparer.Ordinal);
            var japanese = NeutralResxFiles
                .ReadEntries(pair.SatellitePath)
                .ToDictionary(entry => entry.Name, entry => entry.Value, StringComparer.Ordinal);

            foreach (var (key, neutralValue) in neutral)
            {
                if (!japanese.TryGetValue(key, out var japaneseValue))
                {
                    // キー欠落は照合の表の行 resx-ja-key-sets の担当
                    continue;
                }

                FormatPlaceholders(japaneseValue)
                    .Should()
                    .BeEquivalentTo(
                        FormatPlaceholders(neutralValue),
                        $"{pair.SatellitePath} のキー '{key}' は中立と同じ書式指定子を使うべき"
                            + "（string.Format は余剰引数を黙って捨てるため、欠落は実行時エラーにならない）"
                    );
            }
        }
    }

    /// <summary>文面から書式指定子の番号の集合を取り出す（<c>{{</c> はリテラルなので数えない）</summary>
    private static HashSet<int> FormatPlaceholders(string? text)
    {
        var found = new HashSet<int>();

        if (string.IsNullOrEmpty(text))
        {
            return found;
        }

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '{')
            {
                continue;
            }

            // "{{" はリテラルの "{"。2 文字まとめて読み飛ばす
            if (i + 1 < text.Length && text[i + 1] == '{')
            {
                i++;
                continue;
            }

            var end = text.IndexOf('}', i + 1);

            if (end < 0)
            {
                continue;
            }

            // 書式指定（{0:N2} / {0,-5}）は番号だけを見る
            var body = text[(i + 1)..end];
            var numberLength = 0;

            while (numberLength < body.Length && char.IsAsciiDigit(body[numberLength]))
            {
                numberLength++;
            }

            if (
                numberLength > 0
                && (numberLength == body.Length || body[numberLength] is ':' or ',')
                && int.TryParse(body[..numberLength], out var index)
            )
            {
                found.Add(index);
            }
        }

        return found;
    }
}
