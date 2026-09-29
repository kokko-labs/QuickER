using System.IO;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using QuickER.Tests.Resources;

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
        var src = Path.Combine(NeutralResxFiles.FindRepositoryRoot(), "src");

        foreach (var path in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(src, path).Replace('\\', '/');
            var segments = relative.Split('/');

            if (
                segments.Any(segment => segment is "bin" or "obj")
                || relative.EndsWith(".g.cs", StringComparison.Ordinal)
                || relative.EndsWith(".Designer.cs", StringComparison.Ordinal)
            )
            {
                continue;
            }

            yield return (relative, File.ReadAllText(path));
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
}
