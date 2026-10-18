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
/// 主キー・一意制約の構成列の削除を、それを前提にする変更（主キーの変更・一意制約の削除）を選ばずに選んだ同期が、
/// 実 DB で 5 方言とも同じ結果になることを検証する統合テストの共通基底
/// </summary>
/// <remarks>
/// 主キーの列の削除は、その列を外す主キーの変更を選んだときだけ行い、選ばなければ外して名指しする。一意制約の構成列の
/// 削除は、その一意制約を暗黙に外して名指しする。外さないと、SQL Server は列の削除を断り（エラー 5074）、Oracle は
/// 複数列の制約の列の削除を断り（ORA-12991）、PostgreSQL は主キー・一意制約を黙って消し、MySQL は残りの列の主キー・
/// 一意制約へ縮め、SQLite の再構築は選んでいない主キーの縮みを適用していた。単体の対応は
/// <c>SyncPlannerDropMemberColumnTests</c>。
/// </remarks>
[Trait("Category", "Integration")]
public abstract class SyncDropMemberColumnIntegrationTestsBase
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>DB が使えないときの理由（使えるなら null）</summary>
    protected abstract string? UnavailableReason { get; }

    /// <summary>方言の部品</summary>
    internal abstract SyncPlanFuzzDialect Dialect { get; }

    /// <summary>
    /// 列 b（主キー）と d（一意制約 (c, d) の構成列）の削除だけを選ぶと、b の削除は外れ、一意制約は外れて d が消え、
    /// 主キー (a, b) と行は残ることを検証する。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] 同期: 主キーの列の削除は外し、一意制約の構成列の削除は一意制約ごと外して成功する"
    )]
    public async Task DropColumnsOnly_KeepsPrimaryKeyAndDropsUniqueConstraint()
    {
        Assert.SkipWhen(UnavailableReason is not null, UnavailableReason ?? string.Empty);
        await SetUpAsync();

        var (plan, script, result) = await SyncAsync(i => i.Kind == SchemaDiffKind.DropColumn);

        result.Committed.Should().BeTrue($"{result.Error}\nSQL:\n{script}");
        plan.Warnings.Should()
            .ContainSingle(w => w.Kind == SyncPlanWarningKind.SelectedItemRequiresUnselectedItem)
            .Which.Detail.Should()
            .Be("b (PRIMARY KEY)");
        plan.Warnings.Should()
            .ContainSingle(w => w.Kind == SyncPlanWarningKind.UniqueConstraintDroppedWithColumn)
            .Which.Detail.Should()
            .EndWith("(c, d)");

        var after = Table(await ImportAsync());
        after.Columns.Select(c => c.Name.ToLowerInvariant()).Should().Equal("a", "b", "c");
        after
            .GetPrimaryKeyColumnsInOrder()
            .Select(c => c.Name.ToLowerInvariant())
            .Should()
            .Equal("a", "b");
        after.UniqueConstraints.Should().BeEmpty();
        (await CountRowsAsync()).Should().Be(1);
    }

    /// <summary>
    /// b を外す主キーの変更も選べば、b の削除は計画に残って主キーが (a) になり、選んでいない一意制約 (c, d) は残ることを
    /// 検証する。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] 同期: その列を外す主キーの変更も選べば、主キーの列を削除して成功する"
    )]
    public async Task DropPrimaryKeyColumnWithPrimaryKeyChange_Succeeds()
    {
        Assert.SkipWhen(UnavailableReason is not null, UnavailableReason ?? string.Empty);
        await SetUpAsync();

        var (plan, script, result) = await SyncAsync(i =>
            i.Kind == SchemaDiffKind.AlterPrimaryKey
            || i is { Kind: SchemaDiffKind.DropColumn, ColumnName: "b" }
        );

        result.Committed.Should().BeTrue($"{result.Error}\nSQL:\n{script}");
        plan.Warnings.Should().BeEmpty();

        var after = Table(await ImportAsync());
        after.Columns.Select(c => c.Name.ToLowerInvariant()).Should().Equal("a", "c", "d");
        after
            .GetPrimaryKeyColumnsInOrder()
            .Select(c => c.Name.ToLowerInvariant())
            .Should()
            .Equal("a");
        after.UniqueConstraints.Should().ContainSingle();
        (await CountRowsAsync()).Should().Be(1);
    }

    /// <summary>live の表 t(a PK, b PK, c, d, UNIQUE UQ_t_cd (c, d)) を作り、1 行入れる</summary>
    private async Task SetUpAsync()
    {
        await Dialect.ResetAsync(Ct);
        var live = new Entity
        {
            TableName = "t",
            Columns = { Key("a"), Key("b"), Plain("c"), Plain("d") },
        };
        live.UniqueConstraints.Add(
            new UniqueConstraint
            {
                Name = "UQ_t_cd",
                ColumnIds = [live.Columns[2].Id, live.Columns[3].Id],
            }
        );
        var diagram = new ErDiagram { TargetDbms = Dialect.Provider.Name };
        diagram.Entities.Add(live);
        var q = Dialect.Quote;
        await Dialect.ExecuteSetupAsync(Dialect.Provider.DdlGenerator.Build(diagram), Ct);
        await Dialect.ExecuteSetupAsync(
            $"INSERT INTO {q("t")} ({q("a")}, {q("b")}, {q("c")}, {q("d")}) VALUES (1, 1, 1, 1);",
            Ct
        );
    }

    /// <summary>目標の図 t(a PK, c) との差分から <paramref name="select"/> の項目だけを選んで同期する</summary>
    private async Task<(SyncPlan Plan, string Script, SchemaSyncResult Result)> SyncAsync(
        Func<SchemaDiffItem, bool> select
    )
    {
        var live = await ImportAsync();
        var target = new Entity { TableName = "t", Columns = { Key("a"), Plain("c") } };
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
            item.IsSelected = select(item);
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
        return (plan, script, result);
    }

    private Task<SchemaImportResult> ImportAsync() =>
        Dialect.Provider.SchemaImporter.ImportAsync(
            Dialect.ImportConnectionString,
            DbCommands.DefaultTimeoutSeconds,
            Ct
        );

    private static Entity Table(SchemaImportResult schema) =>
        schema.Entities.Single(e => e.TableName.Equals("t", StringComparison.OrdinalIgnoreCase));

    private async Task<long> CountRowsAsync()
    {
        await using var connection = Dialect.CreateConnection();
        await connection.OpenAsync(Ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {Dialect.Quote("t")}";
        return Convert.ToInt64(await command.ExecuteScalarAsync(Ct));
    }

    private static Column Key(string name) =>
        new()
        {
            Name = name,
            DataType = "INT",
            IsPrimaryKey = true,
            IsNullable = false,
        };

    private static Column Plain(string name) =>
        new()
        {
            Name = name,
            DataType = "INT",
            IsNullable = true,
        };
}

/// <summary>SQLite（実ファイル・Docker 不要）</summary>
public sealed class SqliteSyncDropMemberColumnIntegrationTests
    : SyncDropMemberColumnIntegrationTestsBase,
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
public sealed class SqlServerSyncDropMemberColumnIntegrationTests(SqlServerContainerFixture fixture)
    : SyncDropMemberColumnIntegrationTestsBase
{
    protected override string? UnavailableReason =>
        fixture.IsAvailable ? null : fixture.UnavailableReason;

    internal override SyncPlanFuzzDialect Dialect { get; } =
        new SqlServerSyncPlanFuzzDialect(fixture);
}

/// <summary>PostgreSQL（Testcontainers）</summary>
[Trait("RequiresDocker", "true")]
[Collection(PostgreSqlContainerCollection.Name)]
public sealed class PostgreSqlSyncDropMemberColumnIntegrationTests(
    PostgreSqlContainerFixture fixture
) : SyncDropMemberColumnIntegrationTestsBase
{
    protected override string? UnavailableReason =>
        fixture.IsAvailable ? null : fixture.UnavailableReason;

    internal override SyncPlanFuzzDialect Dialect { get; } =
        new PostgreSqlSyncPlanFuzzDialect(fixture);
}

/// <summary>MySQL（Testcontainers）</summary>
[Trait("RequiresDocker", "true")]
[Collection(MySqlContainerCollection.Name)]
public sealed class MySqlSyncDropMemberColumnIntegrationTests(MySqlContainerFixture fixture)
    : SyncDropMemberColumnIntegrationTestsBase
{
    protected override string? UnavailableReason =>
        fixture.IsAvailable ? null : fixture.UnavailableReason;

    internal override SyncPlanFuzzDialect Dialect { get; } = new MySqlSyncPlanFuzzDialect(fixture);
}

/// <summary>Oracle（Testcontainers）</summary>
[Trait("RequiresDocker", "true")]
[Collection(OracleContainerCollection.Name)]
public sealed class OracleSyncDropMemberColumnIntegrationTests(OracleContainerFixture fixture)
    : SyncDropMemberColumnIntegrationTestsBase
{
    protected override string? UnavailableReason =>
        fixture.IsAvailable ? null : fixture.UnavailableReason;

    internal override SyncPlanFuzzDialect Dialect { get; } = new OracleSyncPlanFuzzDialect(fixture);
}
