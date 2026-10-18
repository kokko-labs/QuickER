using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

namespace QuickER.Tests.Integration.Dialects;

/// <summary>
/// 表の名前を変える同期（古い表の削除＋新しい表の作成）で、新しい表が古い表の一意制約の名前を引き継いでも通ることを
/// 5 方言で検証する統合テストの共通基底
/// </summary>
/// <remarks>
/// 逐次 DDL の方言の同期スクリプトは表の削除を表の作成より前に置く。作成を先にすると、一意制約の名前がスキーマの中で
/// 一意な方言（SQL Server・PostgreSQL・Oracle）では、新しい表の一意制約が古い表の同じ名前とぶつかって失敗していた。
/// SQLite は新しい表を作る再構築を先に、表の削除を最後に置くが、一意制約の名前はスキーマの中の名前を取らないので
/// ぶつからない（この順序のままで通ることを同じテストで確かめる）。
/// </remarks>
[Trait("Category", "Integration")]
public abstract class SyncRenameTableIntegrationTestsBase
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>DB が使えないときの理由（使えるなら null）</summary>
    protected abstract string? UnavailableReason { get; }

    /// <summary>方言の部品</summary>
    internal abstract SyncPlanFuzzDialect Dialect { get; }

    /// <summary>
    /// 表 old_items（一意制約 UQ_items_code）を、同じ一意制約の名前を持つ表 items へ変える同期が成功し、取り込み直すと
    /// 差分が出ないことを検証する。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] 同期: 表の名前を変えて一意制約の名前を引き継いでも、古い表を先に消して成功する"
    )]
    public async Task RenameTable_KeepingUniqueConstraintName_Succeeds()
    {
        Assert.SkipWhen(UnavailableReason is not null, UnavailableReason ?? string.Empty);
        await Dialect.ResetAsync(Ct);
        var diagram = new ErDiagram { TargetDbms = Dialect.Provider.Name };
        diagram.Entities.Add(Items("old_items"));
        await Dialect.ExecuteSetupAsync(Dialect.Provider.DdlGenerator.Build(diagram), Ct);

        var live = await ImportAsync();
        var target = Items("items");
        var capabilities = Dialect.Provider.SyncCapabilities;
        var diff = new SchemaDiffService().Compute(
            live.Entities,
            live.Relationships,
            [target],
            [],
            capabilities
        );

        foreach (var item in diff.Items)
        {
            item.IsSelected = item.IsSelectable;
        }

        var plan = new SyncPlanner().BuildPlan(
            diff.Items,
            capabilities,
            new SyncPlanContext
            {
                LiveEntities = live.Entities,
                LiveRelationships = live.Relationships,
                AuxiliaryObjects = live.AuxiliaryObjects,
                TableCreateSql = live.TableCreateSql,
            }
        );
        var script = Dialect.Provider.SyncScriptBuilder.Build(plan);
        var result = await Dialect.Provider.SyncExecutor.ExecuteAsync(Dialect.Settings, script, Ct);

        result.Committed.Should().BeTrue($"{result.Error}\nSQL:\n{script}");
        var after = await ImportAsync();
        after.Entities.Select(e => e.TableName.ToLowerInvariant()).Should().Equal("items");
        new SchemaDiffService()
            .Compute(after.Entities, after.Relationships, [Items("items")], [], capabilities)
            .Items.Where(i => i.IsSelectable)
            .Select(i => i.Description)
            .Should()
            .BeEmpty();
    }

    /// <summary>表 (id PK, code, UNIQUE UQ_items_code (code))。型は方言の型名で書く（取り込み直した綴りと比べるため）</summary>
    private Entity Items(string table)
    {
        var type = Dialect.TypeName(FuzzTypeKind.Int, 0);
        var entity = new Entity
        {
            TableName = table,
            Columns =
            {
                new Column
                {
                    Name = "id",
                    DataType = type,
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Name = "code",
                    DataType = type,
                    IsNullable = false,
                },
            },
        };
        entity.UniqueConstraints.Add(
            new UniqueConstraint { Name = "UQ_items_code", ColumnIds = [entity.Columns[1].Id] }
        );
        return entity;
    }

    private Task<SchemaImportResult> ImportAsync() =>
        Dialect.Provider.SchemaImporter.ImportAsync(
            Dialect.ImportConnectionString,
            DbCommands.DefaultTimeoutSeconds,
            Ct
        );
}

/// <summary>SQLite（実ファイル・Docker 不要）</summary>
public sealed class SqliteSyncRenameTableIntegrationTests
    : SyncRenameTableIntegrationTestsBase,
        IDisposable
{
    private readonly SqliteSyncPlanFuzzDialect _dialect = new();

    protected override string? UnavailableReason => null;

    internal override SyncPlanFuzzDialect Dialect => _dialect;

    public void Dispose() => _dialect.Dispose();
}

/// <summary>SQL Server（Testcontainers）</summary>
[Trait("RequiresDocker", "true")]
[Collection(SqlServerContainerCollection.Name)]
public sealed class SqlServerSyncRenameTableIntegrationTests(SqlServerContainerFixture fixture)
    : SyncRenameTableIntegrationTestsBase
{
    protected override string? UnavailableReason =>
        fixture.IsAvailable ? null : fixture.UnavailableReason;

    internal override SyncPlanFuzzDialect Dialect { get; } =
        new SqlServerSyncPlanFuzzDialect(fixture);
}

/// <summary>PostgreSQL（Testcontainers）</summary>
[Trait("RequiresDocker", "true")]
[Collection(PostgreSqlContainerCollection.Name)]
public sealed class PostgreSqlSyncRenameTableIntegrationTests(PostgreSqlContainerFixture fixture)
    : SyncRenameTableIntegrationTestsBase
{
    protected override string? UnavailableReason =>
        fixture.IsAvailable ? null : fixture.UnavailableReason;

    internal override SyncPlanFuzzDialect Dialect { get; } =
        new PostgreSqlSyncPlanFuzzDialect(fixture);
}

/// <summary>MySQL（Testcontainers）</summary>
[Trait("RequiresDocker", "true")]
[Collection(MySqlContainerCollection.Name)]
public sealed class MySqlSyncRenameTableIntegrationTests(MySqlContainerFixture fixture)
    : SyncRenameTableIntegrationTestsBase
{
    protected override string? UnavailableReason =>
        fixture.IsAvailable ? null : fixture.UnavailableReason;

    internal override SyncPlanFuzzDialect Dialect { get; } = new MySqlSyncPlanFuzzDialect(fixture);
}

/// <summary>Oracle（Testcontainers）</summary>
[Trait("RequiresDocker", "true")]
[Collection(OracleContainerCollection.Name)]
public sealed class OracleSyncRenameTableIntegrationTests(OracleContainerFixture fixture)
    : SyncRenameTableIntegrationTestsBase
{
    protected override string? UnavailableReason =>
        fixture.IsAvailable ? null : fixture.UnavailableReason;

    internal override SyncPlanFuzzDialect Dialect { get; } = new OracleSyncPlanFuzzDialect(fixture);
}
