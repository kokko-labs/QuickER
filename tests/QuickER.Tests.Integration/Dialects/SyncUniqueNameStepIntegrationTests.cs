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
/// 一意制約の差分が、図で名前を付けた制約を先に DB の同じ名前の制約と組むことを 5 方言の実 DB で検証する統合テストの共通基底
/// </summary>
/// <remarks>
/// 名前を保った一意制約の列を変え、元の列に名前の無い一意制約を足した図（同期計画の総当たりの種 75 の形）を同期する。
/// 列集合だけで組むと、名前の無い制約が DB の制約と組み、名前を保った制約の追加が同じ名前で失敗していた
/// （SQL Server・MySQL）。名前の段で組むと、名前を保った制約の作り直しと名前の無い制約の追加になる。SQLite は
/// 取り込んだ一意制約が名前を持たないので名前の段で組まず、再構築が図のとおりに作る。単体の対応は
/// <c>SchemaDiffServiceTests.UniqueConstraints_PairByDiagramNameFirst</c>。
/// </remarks>
[Trait("Category", "Integration")]
public abstract class SyncUniqueNameStepIntegrationTestsBase
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>DB が使えないときの理由（使えるなら null）</summary>
    protected abstract string? UnavailableReason { get; }

    /// <summary>方言の部品</summary>
    internal abstract SyncPlanFuzzDialect Dialect { get; }

    /// <summary>
    /// 表 t（UQ_x (a)）を、UQ_x (b) と名前の無い (a) の図へ同期すると成功し、取り込み直すと差分が出ないことを検証する
    /// </summary>
    [Fact(
        DisplayName = "[Integration] 同期: 名前を保って一意制約の列を変え、元の列に名前の無い一意制約を足しても成功する"
    )]
    public async Task ChangeNamedUniqueAndAddUnnamedOnOldColumns_Succeeds()
    {
        Assert.SkipWhen(UnavailableReason is not null, UnavailableReason ?? string.Empty);
        await Dialect.ResetAsync(Ct);
        var diagram = new ErDiagram { TargetDbms = Dialect.Provider.Name };
        diagram.Entities.Add(Table(("UQ_x", "a")));
        await Dialect.ExecuteSetupAsync(Dialect.Provider.DdlGenerator.Build(diagram), Ct);
        var q = Dialect.Quote;
        await Dialect.ExecuteSetupAsync(
            $"INSERT INTO {q("t")} ({q("id")}, {q("a")}, {q("b")}) VALUES (1, 1, 1);",
            Ct
        );

        var live = await ImportAsync();
        var target = Table(("UQ_x", "b"), (null, "a"));
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
        var table = after.Entities.Single();
        table
            .UniqueConstraints.Select(u =>
                string.Join(
                    ",",
                    u.ColumnIds.Select(id =>
                        table.Columns.Single(c => c.Id == id).Name.ToLowerInvariant()
                    )
                )
            )
            .Should()
            .BeEquivalentTo("a", "b");
        new SchemaDiffService()
            .Compute(
                after.Entities,
                after.Relationships,
                [Table(("UQ_x", "b"), (null, "a"))],
                [],
                capabilities
            )
            .Items.Where(i => i.IsSelectable)
            .Select(i => i.Description)
            .Should()
            .BeEmpty();
    }

    /// <summary>表 t (id PK, a, b) に一意制約を付けたもの。型は方言の型名で書く（取り込み直した綴りと比べるため）</summary>
    private Entity Table(params (string? Name, string Column)[] uniques)
    {
        var type = Dialect.TypeName(FuzzTypeKind.Int, 0);
        var entity = new Entity
        {
            TableName = "t",
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
                    Name = "a",
                    DataType = type,
                    IsNullable = false,
                },
                new Column
                {
                    Name = "b",
                    DataType = type,
                    IsNullable = false,
                },
            },
        };

        foreach (var (name, column) in uniques)
        {
            entity.UniqueConstraints.Add(
                new UniqueConstraint
                {
                    Name = name,
                    ColumnIds = [entity.Columns.Single(c => c.Name == column).Id],
                }
            );
        }

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
public sealed class SqliteSyncUniqueNameStepIntegrationTests
    : SyncUniqueNameStepIntegrationTestsBase,
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
public sealed class SqlServerSyncUniqueNameStepIntegrationTests(SqlServerContainerFixture fixture)
    : SyncUniqueNameStepIntegrationTestsBase
{
    protected override string? UnavailableReason =>
        fixture.IsAvailable ? null : fixture.UnavailableReason;

    internal override SyncPlanFuzzDialect Dialect { get; } =
        new SqlServerSyncPlanFuzzDialect(fixture);
}

/// <summary>PostgreSQL（Testcontainers）</summary>
[Trait("RequiresDocker", "true")]
[Collection(PostgreSqlContainerCollection.Name)]
public sealed class PostgreSqlSyncUniqueNameStepIntegrationTests(PostgreSqlContainerFixture fixture)
    : SyncUniqueNameStepIntegrationTestsBase
{
    protected override string? UnavailableReason =>
        fixture.IsAvailable ? null : fixture.UnavailableReason;

    internal override SyncPlanFuzzDialect Dialect { get; } =
        new PostgreSqlSyncPlanFuzzDialect(fixture);
}

/// <summary>MySQL（Testcontainers）</summary>
[Trait("RequiresDocker", "true")]
[Collection(MySqlContainerCollection.Name)]
public sealed class MySqlSyncUniqueNameStepIntegrationTests(MySqlContainerFixture fixture)
    : SyncUniqueNameStepIntegrationTestsBase
{
    protected override string? UnavailableReason =>
        fixture.IsAvailable ? null : fixture.UnavailableReason;

    internal override SyncPlanFuzzDialect Dialect { get; } = new MySqlSyncPlanFuzzDialect(fixture);
}

/// <summary>Oracle（Testcontainers）</summary>
[Trait("RequiresDocker", "true")]
[Collection(OracleContainerCollection.Name)]
public sealed class OracleSyncUniqueNameStepIntegrationTests(OracleContainerFixture fixture)
    : SyncUniqueNameStepIntegrationTestsBase
{
    protected override string? UnavailableReason =>
        fixture.IsAvailable ? null : fixture.UnavailableReason;

    internal override SyncPlanFuzzDialect Dialect { get; } = new OracleSyncPlanFuzzDialect(fixture);
}
