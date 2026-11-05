using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using QuickER.Provider;
using QuickER.Tests.Resources;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Provider;

/// <summary>
/// 取込警告の文言が Provider の <see cref="SchemaImportWarningText"/> に 1 つだけあり、全種別を英日で整形できることを検証する
/// </summary>
/// <remarks>
/// GUI（DB 取込の内訳）と CLI（<c>quicker scaffold</c> の stderr）は同じ文言を出す。表示側に種別ごとの文言を
/// 持たせない（持たせると片方だけ直して文言が食い違う）ことを、ソースの走査で固定する。
/// カルチャはスレッドの <see cref="CultureInfo.CurrentUICulture"/> を try/finally で切り替える（静的な <c>Strings.Culture</c> は変えない）。
/// </remarks>
public sealed class SchemaImportWarningTextTests
{
    [Fact(DisplayName = "警告は種別ごとに 1 項目へまとまり、見出しに件数、その下に箇所が並ぶ")]
    public void Group_CollectsByKindInFirstAppearanceOrder()
    {
        SchemaImportWarning[] warnings =
        [
            new(SchemaImportWarningKind.ViewExcluded, "v1"),
            new(SchemaImportWarningKind.PartitionDefinitionLost, "sales"),
            new(SchemaImportWarningKind.ViewExcluded, "v2"),
            new(SchemaImportWarningKind.ChildPartitionExcluded, "sales_p0", Detail: "sales"),
            new(SchemaImportWarningKind.ViewExcluded, "v3"),
        ];

        var groups = WithCulture("en", () => SchemaImportWarningText.Group(warnings));

        groups
            .Select(g => g.Kind)
            .Should()
            .Equal(
                SchemaImportWarningKind.ViewExcluded,
                SchemaImportWarningKind.PartitionDefinitionLost,
                SchemaImportWarningKind.ChildPartitionExcluded
            );
        groups[0].Items.Should().Equal("v1", "v2", "v3");
        groups[0]
            .Heading.Should()
            .Be(
                WithCulture(
                    "en",
                    () => SchemaImportWarningText.Heading(SchemaImportWarningKind.ViewExcluded, 3)
                )
            );
        groups[1].Items.Should().ContainSingle().Which.Should().Contain("sales");
        groups[2].Items.Should().ContainSingle().Which.Should().Be("sales_p0 (partition of sales)");
    }

    [Fact(DisplayName = "名前の改行は 1 行 1 件を崩さないよう畳む")]
    public void Describe_FoldsLineBreaksInNames()
    {
        var text = SchemaImportWarningText.Describe(
            new SchemaImportWarning(SchemaImportWarningKind.PartitionDefinitionLost, "a\nb")
        );

        text.Should().Contain("a b").And.NotContain("\n");
    }

    [Fact(DisplayName = "定義されていない種別は黙って空行にせず例外にする")]
    public void Describe_UnknownKind_Throws()
    {
        var act = () =>
            SchemaImportWarningText.Describe(
                new SchemaImportWarning((SchemaImportWarningKind)99, "t")
            );

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact(
        DisplayName = "取込警告の種別で分岐する switch・パターンは src/QuickER.Provider の外に無い（文言は 1 か所）"
    )]
    public void NoSwitchOnWarningKind_OutsideProvider()
    {
        var root = NeutralResxFiles.FindRepositoryRoot();
        var hits = SourceFiles
            .Enumerate("src")
            .Where(file =>
                !file.AreaRelativePath.StartsWith(
                    "QuickER.Provider/",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            .Select(file => file.FullPath)
            .SelectMany(path =>
                ConditionalSourceParse
                    .DescendantNodes(
                        ConditionalSourceParse.Parse(
                            File.ReadAllText(path),
                            cancellationToken: TestContext.Current.CancellationToken
                        ),
                        TestContext.Current.CancellationToken
                    )
                    // 種別で分岐する形（switch のアームの定数パターン・is のパターン・case ラベル・== / != の比較）だけを見る。
                    // 種別を作る式（方言のインポータの「"view" => ViewExcluded」の右辺）は分岐ではない
                    .Where(node =>
                        node is CaseSwitchLabelSyntax or ConstantPatternSyntax
                        || node.IsKind(SyntaxKind.EqualsExpression)
                        || node.IsKind(SyntaxKind.NotEqualsExpression)
                    )
                    .Where(node =>
                        node.ToString()
                            .Contains("SchemaImportWarningKind.", StringComparison.Ordinal)
                    )
                    .Select(node =>
                        $"{Path.GetRelativePath(root, path)}: {node.ToString().Split('\n')[0].Trim()}"
                    )
            )
            .ToList();

        string.Join(Environment.NewLine, hits)
            .Should()
            .BeEmpty(
                "取込警告の文言は SchemaImportWarningText.Describe が持ち、表示側は種別で分岐しない"
            );
    }

    private static T WithCulture<T>(string culture, Func<T> body)
    {
        var previous = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(culture);

            return body();
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
