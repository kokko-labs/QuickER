using System.Globalization;
using AwesomeAssertions;
using QuickER.Provider;

namespace QuickER.Tests.Provider;

/// <summary>
/// 取込警告の文言が Provider の <see cref="SchemaImportWarningText"/> に 1 つだけあり、全種別を英日で整形できることを検証する
/// </summary>
/// <remarks>
/// GUI（DB 取込の内訳）と CLI（<c>quicker scaffold</c> の stderr）は同じ文言を出す。表示側に種別ごとの文言を
/// 持たせない（持たせると片方だけ直して文言が食い違う）ことは、ソースの走査で固定する（SourceRuleTests の規則
/// <c>schema-import-warning-kind-branches</c>）。
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
