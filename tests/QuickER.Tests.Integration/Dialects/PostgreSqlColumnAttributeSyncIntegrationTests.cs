using System.Linq;
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
    [InlineData("cl")]
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

    /// <summary>
    /// 既定値のある列の型の種類を変える同期で、取込の既定値が計画の文脈へ渡り、計画が事前に警告することを検証する（J2）。
    /// 実行したときの結果も固定する（PostgreSQL は型の変更で既定値を新しい型へ変換するので、合わない既定値では同期が失敗して巻き戻る）。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] PostgreSQL 同期: 既定値のある列の型の種類の変更は事前に警告される"
    )]
    public async Task TypeKindChangeWithDefault_IsWarned()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);
        await fixture.ExecuteAsync(
            "CREATE TABLE \"jd\" (\"id\" integer NOT NULL PRIMARY KEY, \"v\" varchar(20) NOT NULL DEFAULT 'abc');",
            Ct
        );

        var provider = new PostgreSqlProvider();
        var live = await provider.SchemaImporter.ImportAsync(fixture.ConnectionString, 120, Ct);
        var target = live.Entities.Select(e => e.Clone(preserveId: true)).ToList();
        target.Single(e => e.TableName == "jd").Columns.Single(c => c.Name == "v").DataType =
            "integer";
        var items = new SchemaDiffService()
            .Compute(
                live.Entities,
                live.Relationships,
                target,
                live.Relationships,
                provider.SyncCapabilities
            )
            .Items;

        foreach (var item in items)
        {
            item.IsSelected = item.Kind == SchemaDiffKind.AlterColumn;
        }

        var plan = new SyncPlanner().BuildPlan(
            items,
            provider.SyncCapabilities,
            new SyncPlanContext
            {
                LiveEntities = live.Entities,
                LiveRelationships = live.Relationships,
                ColumnDefaults = live.ColumnDefaults,
            }
        );

        plan.Warnings.Should()
            .ContainSingle(w => w.Kind == SyncPlanWarningKind.DefaultOnColumnTypeKindChange)
            .Which.Detail.Should()
            .StartWith("v (");

        var script = provider.SyncScriptBuilder.Build(plan);
        var result = await provider.SyncExecutor.ExecuteAsync(
            fixture.ToDbConnectionSettings(),
            script,
            Ct
        );
        result.Committed.Should().Be(false, $"{result.Error}{System.Environment.NewLine}{script}");
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
