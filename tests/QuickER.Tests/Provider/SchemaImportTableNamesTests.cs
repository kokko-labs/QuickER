using System.Globalization;
using AwesomeAssertions;
using QuickER.Provider;

namespace QuickER.Tests.Provider;

/// <summary>
/// 取込が図に入れるテーブル名を、QuickER の読み方（最初の <c>.</c> でスキーマとテーブルに分ける）で元のテーブルへ戻せるかの判定
/// （<see cref="SchemaImportTableNames"/>）と、戻らないテーブルを告げる警告の文面を固定する
/// </summary>
public sealed class SchemaImportTableNamesTests
{
    [Theory(
        DisplayName = "図に入れる名前を最初の . で分けて、DB のスキーマとテーブルへ戻るものだけを取り込む"
    )]
    // スキーマを図に入れない方言（PostgreSQL・MySQL・Oracle・SQLite）
    [InlineData("orders", null, "orders", true)]
    [InlineData("Order.Lines", null, "Order.Lines", false)]
    [InlineData(".t", null, ".t", false)]
    [InlineData("t.", null, "t.", false)]
    // SQL Server の dbo（図に入れるときスキーマを省く）
    [InlineData("orders", "dbo", "orders", true)]
    [InlineData("a.b", "dbo", "a.b", false)]
    // SQL Server の dbo 以外（スキーマ.テーブル。2 つ目以降の . はテーブル名の一部として戻る）
    [InlineData("sales.orders", "sales", "orders", true)]
    [InlineData("a.b", "a", "b", true)]
    [InlineData("a.b.t", "a", "b.t", true)]
    [InlineData("a.b.t", "a.b", "t", false)]
    public void RoundTrips_OnlyWhenTheNameReadsAsTheSameTable(
        string diagramName,
        string? schema,
        string table,
        bool expected
    ) => SchemaImportTableNames.RoundTrips(diagramName, schema, table).Should().Be(expected);

    [Fact(DisplayName = "戻らないテーブルは警告を足して true、戻るテーブルは何も足さずに false")]
    public void ExcludeIfNotRoundTrip_AddsWarningOnlyForExcludedTables()
    {
        var warnings = new List<SchemaImportWarning>();

        SchemaImportTableNames
            .ExcludeIfNotRoundTrip("orders", null, "orders", warnings)
            .Should()
            .BeFalse();
        SchemaImportTableNames
            .ExcludeIfNotRoundTrip("a.b", "dbo", "a.b", warnings)
            .Should()
            .BeTrue();
        SchemaImportTableNames
            .ExcludeIfNotRoundTrip("Order.Lines", null, "Order.Lines", warnings)
            .Should()
            .BeTrue();

        warnings
            .Should()
            .Equal(
                new SchemaImportWarning(
                    SchemaImportWarningKind.DottedTableNameExcluded,
                    "a.b",
                    "dbo"
                ),
                new SchemaImportWarning(
                    SchemaImportWarningKind.DottedTableNameExcluded,
                    "Order.Lines"
                )
            );
    }

    [Theory(DisplayName = "スキーマを図に入れない方言の警告は、空のスキーマ名を書かない文面になる")]
    [InlineData("en")]
    [InlineData("ja")]
    public void Describe_WithoutSchema_DoesNotMentionAnEmptySchema(string culture)
    {
        var previous = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(culture);
            var withoutSchema = SchemaImportWarningText.Describe(
                new SchemaImportWarning(
                    SchemaImportWarningKind.DottedTableNameExcluded,
                    "Order.Lines"
                )
            );
            var withSchema = SchemaImportWarningText.Describe(
                new SchemaImportWarning(
                    SchemaImportWarningKind.DottedTableNameExcluded,
                    "Order.Lines",
                    "dbo"
                )
            );

            withoutSchema.Should().Contain("Order.Lines");
            withoutSchema.Should().NotContain("\"\"").And.NotContain("「」");
            withoutSchema.Should().NotContain("in schema").And.NotContain("スキーマ「");
            withSchema.Should().Contain("Order.Lines").And.Contain("dbo");
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
