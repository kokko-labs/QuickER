using System;
using System.Linq;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.Oracle;

namespace QuickER.Tests.Provider.Oracle;

/// <summary>
/// Oracle の説明（<c>COMMENT ON</c>）の出力形を、DDL 生成と同期スクリプトの両方で検証する。
/// </summary>
/// <remarks>
/// SQL*Plus は文字列リテラルの内側でも行単位で読むため、説明の改行で文が途中で切れる。また <c>&amp;</c> は置換変数になる。
/// LF・CR・<c>&amp;</c> を含む説明だけを PL/SQL ブロックで包み、それらを <c>CHR(n)</c> で連結して出力に生のまま残さない。
/// 含まない説明は従来どおり 1 行の文のまま。
/// </remarks>
public class OracleCommentStatementTests
{
    /// <summary>説明の設定項目 1 件から同期スクリプトを組み立て、改行を LF へ揃えて返す</summary>
    private static string SyncScript(string table, string? column, string description)
    {
        var item = new SchemaDiffItem
        {
            Kind = column is null
                ? SchemaDiffKind.SetTableDescription
                : SchemaDiffKind.SetColumnDescription,
            TableName = table,
            ColumnName = column,
            NewDescription = description,
            IsSelected = true,
        };

        var plan = new SyncPlanner().BuildPlan([item], new SyncDialectCapabilities());
        return new OracleSyncScriptBuilder().Build(plan).ReplaceLineEndings("\n");
    }

    /// <summary>説明付きのテーブル 1 つの図から DDL を組み立て、改行を LF へ揃えて返す</summary>
    private static string Ddl(string tableDescription, string columnDescription = "")
    {
        var entity = new Entity { TableName = "customer", Description = tableDescription };
        entity.Columns.Add(
            new Column
            {
                Name = "id",
                DataType = "NUMBER(10)",
                IsPrimaryKey = true,
                IsNullable = false,
                Description = columnDescription,
            }
        );

        var diagram = new ErDiagram();
        diagram.Entities.Add(entity);
        return new OracleDdlGenerator().Build(diagram).ReplaceLineEndings("\n");
    }

    /// <summary>
    /// LF・CR・<c>&amp;</c> を含む説明が PL/SQL ブロックになり、それらが <c>CHR(n)</c> で連結されることを検証する。
    /// </summary>
    [Fact(DisplayName = "改行・CR・& を含む説明は CHR で連結した PL/SQL ブロックになる")]
    public void Description_WithLineBreakAndAmpersand_BecomesBlock()
    {
        var sql = SyncScript("customer", null, "a\r\nb&c");

        sql.Should()
            .Contain(
                """
                BEGIN
                    EXECUTE IMMEDIATE 'COMMENT ON TABLE "customer" IS '''
                        || 'a'
                        || CHR(13)
                        || CHR(10)
                        || 'b'
                        || CHR(38)
                        || 'c'
                        || '''';
                END;
                /
                """.ReplaceLineEndings("\n")
            );
    }

    /// <summary>
    /// 包んだ出力の各行で文字列リテラルが閉じていること（＝リテラルの中に生の改行が無いこと）と、
    /// <c>&amp;</c> が 1 つも残らないことを検証する。
    /// </summary>
    /// <remarks>
    /// 1 行の中の <c>'</c> の数が偶数なら、その行の終わりでリテラルは閉じている（<c>''</c> の二重化も偶数のまま）。
    /// </remarks>
    [Theory(DisplayName = "包んだ出力はどの行でもリテラルが閉じ & が残らない")]
    [InlineData("line1\nline2")]
    [InlineData("line1\r\nline2\r\n")]
    [InlineData("cr\ronly")]
    [InlineData("R&D")]
    [InlineData("It's\n\nINSERT INTO MARKER VALUES (1);\nx;\n'&'")]
    public void BlockOutput_HasNoRawLineBreakInLiteral_AndNoAmpersand(string description)
    {
        foreach (
            var sql in new[]
            {
                SyncScript("customer", null, description),
                SyncScript("customer", "name", description),
                Ddl(description, description),
            }
        )
        {
            sql.Should().NotContain("&");
            sql.Should().NotContain("\r");

            foreach (var line in sql.Split('\n'))
            {
                (line.Count(c => c == '\'') % 2)
                    .Should()
                    .Be(0, $"行の終わりでリテラルが閉じていない: {line}\n全体:\n{sql}");
            }
        }
    }

    /// <summary>LF・CR・<c>&amp;</c> を含まない説明は、従来どおり 1 行の <c>COMMENT ON</c> 文のままであることを検証する</summary>
    [Fact(DisplayName = "改行・CR・& を含まない説明は従来どおり 1 行の文のまま")]
    public void Description_WithoutSpecialCharacters_StaysSingleLine()
    {
        const string description = "It's 顧客\tCHR(10) | 🙂";

        SyncScript("customer", null, description)
            .Should()
            .Contain("COMMENT ON TABLE \"customer\" IS 'It''s 顧客\tCHR(10) | 🙂';\n")
            .And.NotContain("\n/\n")
            .And.NotContain("BEGIN");
        SyncScript("customer", "name", description)
            .Should()
            .Contain("COMMENT ON COLUMN \"customer\".\"name\" IS 'It''s 顧客\tCHR(10) | 🙂';");

        var ddl = Ddl(description);
        ddl.Should().Contain("COMMENT ON TABLE \"customer\" IS 'It''s 顧客\tCHR(10) | 🙂';");
        ddl.Should().NotContain("BEGIN");
        // 1 行の文は ; 終端のまま。/ 行は付かない
        ddl.Split('\n').Should().NotContain("/");
    }

    /// <summary>DDL では、ブロックになった説明の後ろにだけ <c>/</c> だけの行が付くことを検証する</summary>
    [Fact(DisplayName = "DDL ではブロックになった説明の後ろにだけ / 行が付く")]
    public void Ddl_Block_IsFollowedBySlashLine()
    {
        var ddl = Ddl("multi\nline", "plain");

        ddl.Should().Contain("        || '''';\nEND;\n/\n");
        ddl.Should().Contain("COMMENT ON COLUMN \"customer\".\"id\" IS 'plain';");
        ddl.Split('\n').Count(l => l == "/").Should().Be(1);
    }

    /// <summary>
    /// 説明の <c>'</c> は 2 段（動的 SQL のリテラル・PL/SQL のリテラル）でエスケープされ、
    /// <c>'</c> を含む表名・列名もリテラル用にエスケープされることを検証する。
    /// </summary>
    [Fact(DisplayName = "ブロックでは説明の ' を 2 段でエスケープし表名・列名の ' も二重化する")]
    public void Block_EscapesQuotesTwice_AndEscapesNames()
    {
        var sql = SyncScript("o'rders", "na'me", "It's\nok");

        sql.Should().Contain("EXECUTE IMMEDIATE 'COMMENT ON COLUMN \"o''rders\".\"na''me\" IS '''");
        sql.Should().Contain("        || 'It''''s'\n");
        sql.Should().Contain("        || 'ok'\n");
    }

    /// <summary>
    /// 包んだ説明の文字列部分が 200 文字ごとに切られ、サロゲートペアの途中では切られないことを検証する。
    /// </summary>
    [Fact(
        DisplayName = "ブロックの文字列部分は 200 文字ごとに切りサロゲートペアの途中では切らない"
    )]
    public void Block_SplitsLongSegments_WithoutBreakingSurrogatePairs()
    {
        var emoji = char.ConvertFromUtf32(0x1F600);
        var description = "\n" + new string('a', 199) + emoji + "b" + new string('x', 450);

        var parts = SyncScript("customer", null, description)
            .Split('\n')
            .Where(l => l.StartsWith("        || ", StringComparison.Ordinal))
            .Select(l => l["        || ".Length..])
            .ToList();

        parts
            .Should()
            .Equal(
                "CHR(10)",
                "'" + new string('a', 199) + emoji + "'",
                "'b" + new string('x', 199) + "'",
                "'" + new string('x', 200) + "'",
                "'" + new string('x', 51) + "'",
                "'''';"
            );
    }
}
