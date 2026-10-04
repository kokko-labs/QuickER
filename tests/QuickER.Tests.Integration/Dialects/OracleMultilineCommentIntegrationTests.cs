using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.Oracle;
using QuickER.Tests.Integration;

namespace QuickER.Tests.Integration.Dialects;

/// <summary>
/// 改行・CR・<c>&amp;</c>・<c>'</c> を含む説明が、PL/SQL ブロックで出力されても実 Oracle で 1 文字も変わらず往復することを検証する。
/// </summary>
/// <remarks>
/// <para>
/// これらの文字を含む説明は、SQL*Plus で流しても文が途中で切れないよう <c>EXECUTE IMMEDIATE</c> のブロックで包み、
/// 該当する文字を <c>CHR(n)</c> で連結して出力する。ここでは DDL（<see cref="OracleDdlGenerator"/>）と同期スクリプト
/// （<see cref="OracleSyncScriptBuilder"/>）の両方を実 Oracle へ適用し、取込（<see cref="OracleSchemaImporter"/>）で
/// 説明が元と完全に一致することを確かめる。
/// </para>
/// <para>
/// DDL は <see cref="OracleContainerFixture.ExecuteAsync"/>（SQL*Plus と同じ区切り規則の分割）で、
/// 同期スクリプトは QuickER 自身の実行器（<see cref="OracleSchemaSyncExecutor"/>）で流す。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(OracleContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class OracleMultilineCommentIntegrationTests(OracleContainerFixture fixture)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary><c>'</c> を含む表名（同期スクリプトの動的 SQL の文字列リテラルにも入る）</summary>
    private const string TableName = "o'rders";

    /// <summary><c>'</c> を含む列名</summary>
    private const string ColumnName = "na'me";

    /// <summary>表の説明（LF・CRLF・CR 単独・&amp;・' ・日本語・空行と ; で終わる行を含む）</summary>
    private const string TableDescription =
        "1 行目 It's\n\n2 行目;\r\n3 行目\r4 行目 R&D 'x' &&y\n末尾の改行\n";

    /// <summary>列の説明（表の説明と別の形を混ぜる）</summary>
    private const string ColumnDescription = "&name\nIt''s\r\n";

    /// <summary>適用する経路（DDL か同期スクリプトか）</summary>
    public enum ApplyPath
    {
        /// <summary>図から DDL を生成して流す</summary>
        Ddl,

        /// <summary>説明の設定項目から同期スクリプトを生成して流す</summary>
        Sync,
    }

    /// <summary>改行・CR・&amp;・' を含む説明と、' を含む表名・列名で、説明が 1 文字も変わらず往復することを検証する</summary>
    [Theory(
        DisplayName = "[Integration] 改行・CR・&・' を含む説明が DDL と同期の両方で 1 文字も変わらず往復する"
    )]
    [InlineData(ApplyPath.Ddl)]
    [InlineData(ApplyPath.Sync)]
    public async Task Description_WithLineBreaksAndSpecialCharacters_RoundTrips(ApplyPath path)
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        var script = await ApplyAsync(path, TableDescription, ColumnDescription);

        // 包む対象の説明はブロックになっている（＝ブロックの経路を通したことの確認）
        script.Should().Contain("EXECUTE IMMEDIATE 'COMMENT ON TABLE");
        script.Should().Contain("EXECUTE IMMEDIATE 'COMMENT ON COLUMN");

        var imported = await ImportTableAsync();
        imported.Description.Should().Be(TableDescription, $"SQL:\n{script}");
        imported
            .Columns.Single(c => c.Name == ColumnName)
            .Description.Should()
            .Be(ColumnDescription, $"SQL:\n{script}");
    }

    /// <summary>
    /// COMMENT の上限（4000 バイト）近くの説明でも、ブロックの形で適用でき 1 文字も変わらず往復することを検証する。
    /// </summary>
    /// <remarks>
    /// 改行・<c>&amp;</c>・<c>'</c> が多いほど <c>CHR(n)</c> の連結とエスケープで式が長くなるので、それらを密に含めた
    /// 3,990 バイトの説明を使う（日本語 1 文字＝3 バイト＋<c>'</c>＋<c>&amp;</c>＋LF の 6 バイトを 665 回）。
    /// </remarks>
    [Theory(DisplayName = "[Integration] 4000 バイト近い説明もブロックの形で適用でき往復する")]
    [InlineData(ApplyPath.Ddl)]
    [InlineData(ApplyPath.Sync)]
    public async Task Description_NearCommentLimit_RoundTrips(ApplyPath path)
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        var description = string.Concat(Enumerable.Repeat("あ'&\n", 665));
        Encoding.UTF8.GetByteCount(description).Should().Be(3990);

        var script = await ApplyAsync(path, description, columnDescription: "plain");

        var imported = await ImportTableAsync();
        imported.Description.Should().Be(description, $"SQL の先頭:\n{Head(script)}");
    }

    // ---------------- ヘルパー ----------------

    /// <summary>表を用意して説明を経路どおりに適用し、流したスクリプトを返す</summary>
    private async Task<string> ApplyAsync(
        ApplyPath path,
        string tableDescription,
        string columnDescription
    )
    {
        var entity = new Entity { TableName = TableName, Description = tableDescription };
        entity.Columns.Add(
            new Column
            {
                Name = "id",
                DataType = "NUMBER(10)",
                IsPrimaryKey = true,
                IsNullable = false,
            }
        );
        entity.Columns.Add(
            new Column
            {
                Name = ColumnName,
                DataType = "VARCHAR2(50)",
                IsNullable = true,
                Description = columnDescription,
            }
        );

        if (path == ApplyPath.Ddl)
        {
            var diagram = new ErDiagram();
            diagram.Entities.Add(entity);
            var ddl = new OracleDdlGenerator().Build(diagram);
            await fixture.ExecuteAsync(ddl, Ct);
            return ddl;
        }

        // 同期: 表は説明なしで先に作り、説明だけを同期スクリプトで設定する
        await fixture.ExecuteAsync(
            $"CREATE TABLE {OracleIdentifier.Quote(TableName)} (\"id\" NUMBER(10) NOT NULL, "
                + $"{OracleIdentifier.QuoteSimple(ColumnName)} VARCHAR2(50) NULL);",
            Ct
        );

        var items = new List<SchemaDiffItem>
        {
            new()
            {
                Kind = SchemaDiffKind.SetTableDescription,
                TableName = TableName,
                NewDescription = tableDescription,
                IsSelected = true,
            },
            new()
            {
                Kind = SchemaDiffKind.SetColumnDescription,
                TableName = TableName,
                ColumnName = ColumnName,
                NewDescription = columnDescription,
                IsSelected = true,
            },
        };
        var script = new OracleSyncScriptBuilder().Build(
            new SyncPlanner().BuildPlan(items, new OracleProvider().SyncCapabilities)
        );
        var result = await new OracleSchemaSyncExecutor().ExecuteAsync(
            fixture.ToDbConnectionSettings(),
            script,
            Ct
        );
        result.Committed.Should().BeTrue($"同期に失敗: {result.Error}\nSQL 先頭:\n{Head(script)}");
        return script;
    }

    /// <summary>取込んで対象の表を返す</summary>
    private async Task<Entity> ImportTableAsync()
    {
        await using var conn = await fixture.OpenConnectionAsync(Ct);
        var result = await new OracleSchemaImporter().ImportAsync(conn, Ct);
        return result.Entities.Single(e => e.TableName == TableName);
    }

    /// <summary>失敗時のメッセージに載せるため、長いスクリプトの先頭だけを返す</summary>
    private static string Head(string script) =>
        script.Length <= 2000 ? script : script[..2000] + "…";
}
