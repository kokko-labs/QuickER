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
/// 空白だけの説明（テーブル・列）を、DDL・差分・同期スクリプトが同じく「説明なし」として扱うことを 5 方言の実 DB で
/// 検証する統合テストの共通基底
/// </summary>
/// <remarks>
/// 判定は <see cref="DescriptionText"/> の 1 つだけ。以前は DDL が空白だけの説明を書かないのに差分は説明ありとして比べ、
/// DDL を適用した直後に偽の差分が 1 件出ていた。単体の対応は <c>BlankDescriptionTests</c>。
/// </remarks>
[Trait("Category", "Integration")]
public abstract class BlankDescriptionIntegrationTestsBase
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>DB が使えないときの理由（使えるなら null）</summary>
    protected abstract string? UnavailableReason { get; }

    /// <summary>方言の部品</summary>
    internal abstract SyncPlanFuzzDialect Dialect { get; }

    /// <summary>空白だけの説明の図を DDL で作って取り込み直すと、差分が出ないことを検証する</summary>
    [Fact(
        DisplayName = "[Integration] 空白だけの説明の図は、DDL を適用して取り込み直しても差分が出ない"
    )]
    public async Task BlankDescriptions_DdlRoundTrip_HasNoDiff()
    {
        Assert.SkipWhen(UnavailableReason is not null, UnavailableReason ?? string.Empty);
        await CreateAsync(Table("  ", " \t "));

        var live = await ImportAsync();

        Diff(live, Table("  ", " \t ")).Should().BeEmpty();
        Diff(live, Table(string.Empty, string.Empty)).Should().BeEmpty();
    }

    /// <summary>
    /// 説明のある DB を空白だけの説明の図へ同期すると説明が消え、取り込み直すと差分が出ないことを検証する
    /// </summary>
    [Fact(
        DisplayName = "[Integration] 説明のある DB を空白だけの説明の図へ同期すると、説明が消えて差分が出ない"
    )]
    public async Task SyncToBlankDescriptions_RemovesDescriptions()
    {
        Assert.SkipWhen(UnavailableReason is not null, UnavailableReason ?? string.Empty);
        await CreateAsync(Table("table note", "column note"));
        var live = await ImportAsync();
        var target = Table("  ", " \t ");
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
        DescriptionText.IsPresent(table.Description).Should().BeFalse();
        DescriptionText
            .IsPresent(table.Columns.Single(c => c.Name == "v").Description)
            .Should()
            .BeFalse();
        Diff(after, Table("  ", " \t ")).Should().BeEmpty();
    }

    /// <summary>表 t (id PK, v) に説明を付けたもの。型は方言の型名で書く（取り込み直した綴りと比べるため）</summary>
    private Entity Table(string tableDescription, string columnDescription)
    {
        var type = Dialect.TypeName(FuzzTypeKind.Int, 0);
        return new Entity
        {
            TableName = "t",
            Description = tableDescription,
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
                    Name = "v",
                    DataType = type,
                    IsNullable = true,
                    Description = columnDescription,
                },
            },
        };
    }

    private async Task CreateAsync(Entity table)
    {
        await Dialect.ResetAsync(Ct);
        var diagram = new ErDiagram { TargetDbms = Dialect.Provider.Name };
        diagram.Entities.Add(table);
        await Dialect.ExecuteSetupAsync(Dialect.Provider.DdlGenerator.Build(diagram), Ct);
    }

    private string[] Diff(SchemaImportResult live, Entity target) =>
        [
            .. new SchemaDiffService()
                .Compute(
                    live.Entities,
                    live.Relationships,
                    [target],
                    [],
                    Dialect.Provider.SyncCapabilities
                )
                .Items.Where(i => i.IsSelectable)
                .Select(i => i.Description),
        ];

    private Task<SchemaImportResult> ImportAsync() =>
        Dialect.Provider.SchemaImporter.ImportAsync(
            Dialect.ImportConnectionString,
            DbCommands.DefaultTimeoutSeconds,
            Ct
        );
}

/// <summary>SQLite（実ファイル・Docker 不要。説明を持たない方言＝差分に説明の項目が出ないことを確かめる側）</summary>
public sealed class SqliteBlankDescriptionIntegrationTests
    : BlankDescriptionIntegrationTestsBase,
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
public sealed class SqlServerBlankDescriptionIntegrationTests(SqlServerContainerFixture fixture)
    : BlankDescriptionIntegrationTestsBase
{
    protected override string? UnavailableReason =>
        fixture.IsAvailable ? null : fixture.UnavailableReason;

    internal override SyncPlanFuzzDialect Dialect { get; } =
        new SqlServerSyncPlanFuzzDialect(fixture);
}

/// <summary>PostgreSQL（Testcontainers）</summary>
[Trait("RequiresDocker", "true")]
[Collection(PostgreSqlContainerCollection.Name)]
public sealed class PostgreSqlBlankDescriptionIntegrationTests(PostgreSqlContainerFixture fixture)
    : BlankDescriptionIntegrationTestsBase
{
    protected override string? UnavailableReason =>
        fixture.IsAvailable ? null : fixture.UnavailableReason;

    internal override SyncPlanFuzzDialect Dialect { get; } =
        new PostgreSqlSyncPlanFuzzDialect(fixture);
}

/// <summary>MySQL（Testcontainers）</summary>
[Trait("RequiresDocker", "true")]
[Collection(MySqlContainerCollection.Name)]
public sealed class MySqlBlankDescriptionIntegrationTests(MySqlContainerFixture fixture)
    : BlankDescriptionIntegrationTestsBase
{
    protected override string? UnavailableReason =>
        fixture.IsAvailable ? null : fixture.UnavailableReason;

    internal override SyncPlanFuzzDialect Dialect { get; } = new MySqlSyncPlanFuzzDialect(fixture);
}

/// <summary>Oracle（Testcontainers）</summary>
[Trait("RequiresDocker", "true")]
[Collection(OracleContainerCollection.Name)]
public sealed class OracleBlankDescriptionIntegrationTests(OracleContainerFixture fixture)
    : BlankDescriptionIntegrationTestsBase
{
    protected override string? UnavailableReason =>
        fixture.IsAvailable ? null : fixture.UnavailableReason;

    internal override SyncPlanFuzzDialect Dialect { get; } = new OracleSyncPlanFuzzDialect(fixture);
}
