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
    private static readonly Regex Placeholder = new(@"\{\d+\}", RegexOptions.CultureInvariant);

    public static TheoryData<SchemaImportWarningKind, string, bool> Cases()
    {
        var data = new TheoryData<SchemaImportWarningKind, string, bool>();

        foreach (var kind in Enum.GetValues<SchemaImportWarningKind>())
        {
            foreach (var culture in new[] { "en", "ja" })
            {
                data.Add(kind, culture, true);

                // 計算列は詳細（式）が無いときに別の文面へ振り分ける
                if (kind == SchemaImportWarningKind.ComputedColumnExpressionLost)
                {
                    data.Add(kind, culture, false);
                }
            }
        }

        return data;
    }

    [Theory(
        DisplayName = "すべての取込警告の種別を英日で 1 行に整形できる（埋め込み漏れ・訳漏れが無い）"
    )]
    [MemberData(nameof(Cases))]
    public void Describe_EveryKind_FormatsInBothCultures(
        SchemaImportWarningKind kind,
        string culture,
        bool withDetail
    )
    {
        var warning = new SchemaImportWarning(kind, "tbl_x", "subj_y", withDetail ? "det_z" : "");

        var text = WithCulture(culture, () => SchemaImportWarningText.Describe(warning));
        var other = WithCulture(
            culture == "en" ? "ja" : "en",
            () => SchemaImportWarningText.Describe(warning)
        );

        text.Should().NotBeNullOrWhiteSpace();
        Placeholder.IsMatch(text).Should().BeFalse("書式の穴はすべて埋まる");
        text.Should().NotContain("\n").And.NotContain("\r");
        (text.Contains("tbl_x") || text.Contains("subj_y"))
            .Should()
            .BeTrue("対象の名前を名指しする");

        if (SchemaImportWarningKinds.IsAggregated(kind))
        {
            // 外した種別の項目は名前（と、あれば親・本表の名前）だけ。内容は見出しが言う
            text.Should().StartWith("tbl_x");
        }
        else
        {
            text.Should().NotBe(other, "英語と日本語の文言がそれぞれある");
        }
    }

    [Theory(DisplayName = "すべての種別の見出しは件数を入れて英日で書ける")]
    [MemberData(nameof(HeadingCases))]
    public void Heading_EveryKind_IncludesCountInBothCultures(SchemaImportWarningKind kind)
    {
        var en = WithCulture("en", () => SchemaImportWarningText.Heading(kind, 37));
        var ja = WithCulture("ja", () => SchemaImportWarningText.Heading(kind, 37));

        en.Should().Contain("37").And.NotBe(ja, "英語と日本語の見出しがそれぞれある");
        ja.Should().Contain("37");
        Placeholder.IsMatch(en).Should().BeFalse();
        Placeholder.IsMatch(ja).Should().BeFalse();
    }

    public static TheoryData<SchemaImportWarningKind> HeadingCases() =>
        new(Enum.GetValues<SchemaImportWarningKind>());

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
