using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Npgsql;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.PostgreSql;
using QuickER.Tests.Integration;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Integration.Dialects;

/// <summary>
/// PostgreSQL の列の変更（<c>ALTER COLUMN … TYPE</c>）が、型の既定と違う照合順序を保つことを実 DB で検証する。
/// </summary>
/// <remarks>
/// <c>ALTER COLUMN … TYPE</c> は <c>COLLATE</c> を書かなければ照合順序を新しい型の既定へ戻す。同期は実行時に
/// <c>pg_attribute.attcollation</c> を読み、型の既定と違うときだけ書き添える。Docker 必須。
/// ここに残すのは、取込の範囲（public）の外の別スキーマの表＝差分から作れず手で組んだ項目でしか通らない経路だけ。public の表は
/// 同期計画の期待の行の実 DB の段（<c>SyncPlanExpectationRows.RealPostgreSql.cs</c>）が持つ。
/// </remarks>
[Trait("Category", "Integration")]
[Collection(PostgreSqlContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class PostgreSqlColumnAttributeSyncIntegrationTests(
    PostgreSqlContainerFixture fixture
)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>
    /// 型の既定と違う照合順序の列は型を変えても照合順序を保ち、既定の照合順序の列は既定のままであることを検証する
    /// （以前は COLLATE を書かず、型の既定へ戻していた）。ドット付きの表名でも ALTER TABLE と同じ表を読む。
    /// </summary>
    [Theory(DisplayName = "[Integration] PostgreSQL 同期: 列の変更は型の既定と違う照合順序を保つ")]
    [InlineData("sales.cl")]
    public async Task AlterColumn_KeepsNonDefaultCollation(string table)
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);
        var quoted = table.Contains('.') ? "\"sales\".\"cl\"" : "\"cl\"";
        await fixture.ExecuteAsync(
            "DROP SCHEMA IF EXISTS \"sales\" CASCADE; CREATE SCHEMA \"sales\";"
                + $"CREATE TABLE {quoted} (\"id\" integer NOT NULL PRIMARY KEY, "
                + "\"c\" varchar(20) COLLATE \"C\" NOT NULL, \"d\" varchar(20) NOT NULL);",
            Ct
        );

        try
        {
            var script = await AlterAsync(table, ("c", "varchar(40)"), ("d", "text"));

            (await CollationAsync(quoted, "c")).Should().Be("C", script);
            (await CollationAsync(quoted, "d")).Should().Be("default", script);
        }
        finally
        {
            await fixture.ExecuteAsync("DROP SCHEMA IF EXISTS \"sales\" CASCADE;", Ct);
        }
    }

    /// <summary>列の変更を同期として実行し、使ったスクリプトを返す</summary>
    private async Task<string> AlterAsync(
        string table,
        params (string Column, string Type)[] changes
    )
    {
        var items = new SchemaDiffItem[changes.Length];

        for (var i = 0; i < changes.Length; i++)
        {
            items[i] = new SchemaDiffItem
            {
                Kind = SchemaDiffKind.AlterColumn,
                TableName = table,
                ColumnName = changes[i].Column,
                Column = new Column
                {
                    Name = changes[i].Column,
                    DataType = changes[i].Type,
                    IsNullable = false,
                },
                OldColumn = new Column { Name = changes[i].Column, DataType = "varchar(20)" },
                IsSelected = true,
            };
        }

        var script = new PostgreSqlSyncScriptBuilder().Build(
            new SyncPlanner().BuildPlan(items, TestDialectCapabilities.Neutral)
        );
        var result = await new PostgreSqlSchemaSyncExecutor().ExecuteAsync(
            fixture.ToDbConnectionSettings(),
            script,
            Ct
        );
        result.Committed.Should().BeTrue($"{result.Error}\nSQL:\n{script}");
        return script;
    }

    /// <summary>列の照合順序の名前を読む（照合順序を持たなければ null）</summary>
    private async Task<string?> CollationAsync(string quotedTable, string column)
    {
        await using var conn = await fixture.OpenConnectionAsync(Ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT co.collname FROM pg_attribute a LEFT JOIN pg_collation co ON co.oid = a.attcollation "
                + "WHERE a.attrelid = to_regclass(@t) AND a.attname = @c;",
            conn
        );
        cmd.Parameters.AddWithValue("t", quotedTable);
        cmd.Parameters.AddWithValue("c", column);
        return await cmd.ExecuteScalarAsync(Ct) as string;
    }
}
