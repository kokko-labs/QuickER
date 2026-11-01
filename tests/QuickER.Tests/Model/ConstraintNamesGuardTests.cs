using System.IO;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using QuickER.Tests.Resources;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Model;

/// <summary>
/// 制約名の安全化と既定名の組み立てが <c>ConstraintNames</c> の外に写されていないことを、ソースを走査して確かめるガード
/// </summary>
/// <remarks>
/// <para>
/// 同じ規則が 6 か所に写されていた結果、同期の計画だけが前後の空白を除くようになり、DDL との名前が食い違った。
/// 写しが無ければ規則を変えたときにずれようがないので、写しそのものを検出する。
/// </para>
/// <para>
/// <b>検出する形</b>（完全な網ではない）:
/// (1) 置換規則の写し＝<c>.Replace(".", "_")</c> の直後に <c>.Replace(" ", "_")</c> が続く連鎖。
/// (2) 既定名の組み立て＝補間文字列の <c>$"FK_{</c> / <c>$"PK_{</c> と、連結の <c>"FK_" +</c> / <c>"PK_" +</c>。
/// <c>string.Concat</c> や書式指定などの別の形はすり抜けるので、既定名は必ず <c>ConstraintNames</c> のメソッドで
/// 組み立てること（正本の XmlDoc にも同じことを書いてある）。
/// </para>
/// <para>
/// 走査対象は <c>src/</c> の <c>*.cs</c> で、<c>bin</c> / <c>obj</c> と生成物（<c>*.g.cs</c>・<c>*.Designer.cs</c>）を除く。
/// 除外は 1 つだけ＝<c>SqliteSchemaImporter.cs</c> の <c>$"FK_{テーブル}_{参照先}_{id}"</c>。名前を持たない SQLite の
/// 外部キーへ<b>取込時に</b>付ける識別用の名前で（<c>id</c> を含む）、既定名の規則とは別物のため。
/// </para>
/// </remarks>
public class ConstraintNamesGuardTests
{
    private static readonly Regex RuleCopy = new(
        @"\.Replace\(\s*""\.""\s*,\s*""_""\s*\)\s*\.Replace\(\s*"" ""\s*,\s*""_""\s*\)",
        RegexOptions.Compiled
    );

    private static readonly Regex NameComposition = new(
        @"\$""(FK|PK)_\{|""(FK|PK)_""\s*\+",
        RegexOptions.Compiled
    );

    /// <summary>取込時の識別用の名前（既定名の規則とは別物）</summary>
    private const string SqliteImportNaming = "SqliteSchemaImporter.cs";

    private static IEnumerable<(string Path, string Text)> SourceFiles()
    {
        foreach (var file in TestSupport.SourceFiles.Enumerate("src"))
        {
            var relative = file.AreaRelativePath;

            if (
                relative.EndsWith(".g.cs", StringComparison.Ordinal)
                || relative.EndsWith(".Designer.cs", StringComparison.Ordinal)
            )
            {
                continue;
            }

            yield return (relative, File.ReadAllText(file.FullPath));
        }
    }

    [Fact(DisplayName = "制約名の置換規則は ConstraintNames にしか書かれていない")]
    public void SafeNameRule_LivesOnlyInConstraintNames()
    {
        var copies = SourceFiles()
            .Where(file => !file.Path.EndsWith("/ConstraintNames.cs", StringComparison.Ordinal))
            .Where(file => RuleCopy.IsMatch(file.Text))
            .Select(file => file.Path)
            .ToList();

        copies
            .Should()
            .BeEmpty(
                "規則の写しは変更のたびにずれる。ConstraintNames.SafeName を呼ぶこと"
                    + $"（見つかった写し: {string.Join(", ", copies)}）"
            );
    }

    [Fact(DisplayName = "外部キー・主キーの既定名は ConstraintNames でしか組み立てていない")]
    public void DefaultNameComposition_LivesOnlyInConstraintNames()
    {
        var copies = SourceFiles()
            .Where(file =>
                !file.Path.EndsWith("/ConstraintNames.cs", StringComparison.Ordinal)
                && !file.Path.EndsWith("/" + SqliteImportNaming, StringComparison.Ordinal)
            )
            .SelectMany(file =>
                file.Text.Split('\n')
                    .Select((line, index) => (file.Path, Line: index + 1, Text: line))
                    .Where(line => NameComposition.IsMatch(line.Text))
            )
            .Select(hit => $"{hit.Path}:{hit.Line}: {hit.Text.Trim()}")
            .ToList();

        copies
            .Should()
            .BeEmpty(
                "既定名の組み立ての写しは変更のたびにずれる。ConstraintNames.ForeignKey / PrimaryKey を呼ぶこと"
                    + $"（見つかった写し: {string.Join(" / ", copies)}）"
            );
    }

    /// <summary>
    /// 一意制約の合成名（<c>UniqueConstraint.SynthesizeName</c>）を直接呼ぶのは、図全体の名前の解決
    /// （<c>ConstraintNames.SynthesizeFreeName</c>）の 1 か所だけであることを、構文木で数えて確かめる
    /// </summary>
    /// <remarks>
    /// 合成名はほかの制約の名前とぶつかり得る。呼び出し側が直接合成すると、DDL・同期・生成コード・ツールの要約のどれかだけが
    /// 番号を足さない名前を出し、ぶつかる図で DDL が断られる（同期計画の総当たりの種 386）。名前は
    /// <c>ConstraintNames.ResolveUniqueConstraintNames</c>（図全体）か <c>ResolveUniqueConstraintName</c>（1 つの制約）を通す。
    /// 呼び出しの形（修飾・<c>using static</c>）に依らず、呼ばれる名前が <c>SynthesizeName</c> の呼び出しを数える。
    /// </remarks>
    [Fact(DisplayName = "一意制約の合成名を直接作るのは、図全体の名前の解決の中の 1 か所だけ")]
    public void SynthesizeName_IsCalledOnlyByTheResolver()
    {
        var calls = SourceFiles()
            .SelectMany(file => FindSynthesizeNameCalls(file.Path, file.Text))
            .ToList();

        calls
            .Should()
            .Equal(
                ["QuickER.Model/ConstraintNames.cs: SynthesizeFreeName"],
                "合成名は ConstraintNames.ResolveUniqueConstraintNames / ResolveUniqueConstraintName を通す"
            );
    }

    /// <summary>走査の自己検査（修飾した呼び出し・using static の呼び出しを拾い、宣言と別の名前は拾わない）</summary>
    [Fact(DisplayName = "一意制約の合成名の走査は、呼び出しの形に依らず拾い、宣言は拾わない")]
    public void SynthesizeNameScan_FindsEveryCallForm()
    {
        const string probe = """
            using static QuickER.Model.UniqueConstraint;
            class Probe
            {
                static string SynthesizeName(string a) => a;
                void Qualified() => QuickER.Model.UniqueConstraint.SynthesizeName("t", ["c"], limit);
                void Static() => SynthesizeName("t", ["c"], limit);
                void Other() => UniqueConstraint.SynthesizeNameLater("t");
            }
            """;

        FindSynthesizeNameCalls("probe.cs", probe)
            .Should()
            .Equal("probe.cs: Qualified", "probe.cs: Static");
    }

    private static IEnumerable<string> FindSynthesizeNameCalls(string path, string text) =>
        ConditionalSourceParse
            .DescendantNodes(ConditionalSourceParse.Parse(text, path: path))
            .OfType<InvocationExpressionSyntax>()
            .Where(invocation =>
                invocation.Expression switch
                {
                    MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
                    IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
                    _ => null,
                } == "SynthesizeName"
            )
            .Select(invocation =>
                $"{path}: {invocation.Ancestors().OfType<MemberDeclarationSyntax>().Select(MemberName).FirstOrDefault(name => name is not null)}"
            );

    private static string? MemberName(MemberDeclarationSyntax member) =>
        member switch
        {
            MethodDeclarationSyntax method => method.Identifier.ValueText,
            PropertyDeclarationSyntax property => property.Identifier.ValueText,
            _ => null,
        };
}
