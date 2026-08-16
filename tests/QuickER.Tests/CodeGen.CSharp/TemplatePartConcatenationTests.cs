using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using AwesomeAssertions;
using QuickER.CodeGen.CSharp;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// テンプレート部品（<c>Templates/CSharpRuntime/*.scriban</c>）の連結メカニズムそのものを直接表明するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// 連結順・部品集合・改行コード（CRLF 固定）は、これまで「ドリフト検知が赤くなる」ことでしか守られていなかった。
/// ドリフトは検知はするが、失敗メッセージは「コミット済みと差分が出た」としか言わず、19 万行の差分から原因を人が
/// 突き止めることになる。ここは<b>原因を名指しする層</b>で、部品を足し忘れた・番号を重複させた・LF で保存した、
/// のいずれも部品名つきで落ちる。
/// </para>
/// <para>
/// 検証は 3 点:
/// (1) <see cref="CSharpTemplateParts.OrderedResourceNames"/> が埋め込みリソースの実集合と 1:1（足し忘れ・余りの検知）、
/// (2) 数値プレフィックスが昇順・重複なし（連結順＝分割前の行順であることの必要条件）、
/// (3) 各部品の本文が素の <c>\n</c> を含まない（＝CRLF のみ。<c>.gitattributes</c> の <c>eol=crlf</c> が効いているか）。
/// </para>
/// </remarks>
public class TemplatePartConcatenationTests
{
    /// <summary>テンプレート部品を保持するアセンブリ（生成器アセンブリ）</summary>
    private static readonly System.Reflection.Assembly TemplateAssembly =
        typeof(CSharpTemplateParts).Assembly;

    /// <summary>埋め込みリソースに実在する CSharpRuntime テンプレート部品のリソース名（宣言順に依らない実集合）</summary>
    private static IReadOnlyList<string> ActualPartResourceNames() =>
        TemplateAssembly
            .GetManifestResourceNames()
            .Where(name =>
                name.StartsWith(CSharpTemplateParts.ResourceNamePrefix, StringComparison.Ordinal)
                && name.EndsWith(CSharpTemplateParts.ResourceNameSuffix, StringComparison.Ordinal)
            )
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// 連結順の宣言が、埋め込みリソースに実在する部品集合と 1:1 で対応することを検証する
    /// （部品ファイルを追加して宣言し忘れた／宣言に残ったまま削除した、のどちらも落ちる）。
    /// </summary>
    [Fact(DisplayName = "テンプレート部品の連結順宣言が埋め込みリソースの実集合と 1:1 で対応する")]
    public void OrderedResourceNames_ShouldMatchEmbeddedResourceSet()
    {
        var declared = CSharpTemplateParts.OrderedResourceNames;
        var actual = ActualPartResourceNames();

        declared.Should().OnlyHaveUniqueItems("同じ部品を 2 回連結すると出力が二重になる");

        var missing = actual.Except(declared, StringComparer.Ordinal).ToList();
        var extra = declared.Except(actual, StringComparer.Ordinal).ToList();

        missing
            .Should()
            .BeEmpty(
                "埋め込みリソースに実在するが連結順へ宣言されていない部品がある（この部品の内容は生成に反映されない）: "
                    + string.Join(", ", missing)
            );
        extra
            .Should()
            .BeEmpty(
                "連結順に宣言されているが埋め込みリソースに実在しない部品がある（レンダラの初期化が例外になる）: "
                    + string.Join(", ", extra)
            );
    }

    /// <summary>
    /// 連結順が数値プレフィックスの昇順であり、番号の重複が無いことを検証する
    /// （連結結果が分割前テンプレートの行順と一致するための必要条件）。
    /// </summary>
    [Fact(DisplayName = "テンプレート部品の連結順が番号昇順・番号重複なし")]
    public void OrderedResourceNames_ShouldBeAscendingByNumericPrefix()
    {
        var numbers = CSharpTemplateParts
            .OrderedResourceNames.Select(name => (Name: name, Number: ParsePartNumber(name)))
            .ToList();

        foreach (var (name, number) in numbers)
        {
            number
                .Should()
                .NotBeNull(
                    $"部品 '{name}' のリソース名が『_NN_名前.scriban』の規約から外れている（連結順の判定ができない）"
                );
        }

        var ordered = numbers.Select(part => part.Number!.Value).ToList();

        ordered
            .Should()
            .OnlyHaveUniqueItems(
                "部品番号が重複している（連結順が一意に決まらない）: "
                    + string.Join(
                        ", ",
                        numbers
                            .GroupBy(part => part.Number)
                            .Where(group => group.Count() > 1)
                            .SelectMany(group => group.Select(part => part.Name))
                    )
            );

        for (var index = 1; index < ordered.Count; index++)
        {
            ordered[index]
                .Should()
                .BeGreaterThan(
                    ordered[index - 1],
                    $"連結順が番号昇順でない（'{numbers[index - 1].Name}' の直後に '{numbers[index].Name}' が来ている）"
                );
        }
    }

    /// <summary>
    /// 各部品の本文が CRLF のみで構成される（素の <c>\n</c> を含まない）ことを検証する。
    /// </summary>
    /// <remarks>
    /// 部品は <c>.gitattributes</c> の <c>*.scriban text eol=crlf</c> で CRLF 固定されており、1 つでも LF が混ざると
    /// 連結結果の改行が方言のように混在し、生成コードのバイト一致（ドリフト 28 件）が全滅する。
    /// </remarks>
    [Fact(DisplayName = "テンプレート部品の本文が CRLF のみで構成される（素の LF を含まない）")]
    public void TemplateParts_ShouldUseCrlfOnly()
    {
        foreach (var resourceName in CSharpTemplateParts.OrderedResourceNames)
        {
            var text = ReadPart(resourceName);
            var bareLineFeeds = CountBareLineFeeds(text);

            bareLineFeeds
                .Should()
                .Be(
                    0,
                    $"部品 '{resourceName}' に CR を伴わない LF が {bareLineFeeds} 個ある"
                        + "（*.scriban は CRLF 固定。LF が混ざると生成コードのバイト一致が崩れる）"
                );
        }
    }

    /// <summary>リソース名の数値プレフィックス（<c>_NN_</c>）を取り出す。規約から外れていれば <c>null</c></summary>
    private static int? ParsePartNumber(string resourceName)
    {
        var localName = resourceName[CSharpTemplateParts.ResourceNamePrefix.Length..];

        if (localName.Length < 4 || localName[0] != '_')
        {
            return null;
        }

        var separator = localName.IndexOf('_', 1);

        if (separator <= 1)
        {
            return null;
        }

        return int.TryParse(
            localName[1..separator],
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var number
        )
            ? number
            : null;
    }

    /// <summary>部品リソースの本文を読み出す（レンダラの <c>LoadTemplate</c> と同じ UTF-8 読み出し）</summary>
    private static string ReadPart(string resourceName)
    {
        using var stream =
            TemplateAssembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"埋め込みリソース '{resourceName}' が見つかりません"
            );
        using var reader = new StreamReader(stream, Encoding.UTF8);

        return reader.ReadToEnd();
    }

    /// <summary>直前が CR でない LF の個数を数える</summary>
    private static int CountBareLineFeeds(string text)
    {
        var count = 0;

        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\n' && (index == 0 || text[index - 1] != '\r'))
            {
                count++;
            }
        }

        return count;
    }
}
