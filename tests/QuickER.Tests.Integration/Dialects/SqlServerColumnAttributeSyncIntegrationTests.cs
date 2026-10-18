using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Data.SqlClient;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.SqlServer;
using QuickER.Tests.Integration;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Integration.Dialects;

/// <summary>
/// SQL Server の列の変更（<c>ALTER COLUMN</c>）が、図に無い列属性（照合順序・DEFAULT 制約）を保つことを実 DB で検証する。
/// </summary>
/// <remarks>
/// <c>ALTER COLUMN</c> は照合順序を書かなければ DB の既定へ戻し、DEFAULT 制約のある列の型の変更はエラー 5074 で断る。
/// 同期は実行時に照合順序と DEFAULT 制約を読み直し、照合順序は書き添え、DEFAULT 制約は外して型を変えてから同じ名前・
/// 同じ定義で付け直す。Docker 必須（<see cref="SqlServerContainerFixture"/>）。
/// </remarks>
[Trait("Category", "Integration")]
[Collection(SqlServerContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class SqlServerColumnAttributeSyncIntegrationTests(SqlServerContainerFixture fixture)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>
    /// 文字列の列の型・NULL 許容の変更が、DB の既定と違う照合順序を保つことを検証する（以前は DB の既定へ戻していた）。
    /// </summary>
    [Fact(DisplayName = "[Integration] SQL Server 同期: 列の変更は照合順序を保つ")]
    public async Task AlterColumn_KeepsCollation()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);
        await fixture.ExecuteAsync(
            "CREATE TABLE [ca] ([id] int NOT NULL PRIMARY KEY, "
                + "[name] varchar(20) COLLATE Japanese_CI_AS NOT NULL);",
            Ct
        );

        var (result, script) = await AlterAsync("ca", "name", "varchar(40)", nullable: true);

        result.Committed.Should().BeTrue($"{result.Error}\nSQL:\n{script}");
        var column = await ColumnAsync("ca", "name");
        column.Type.Should().Be("varchar");
        column.Length.Should().Be(40);
        column.Nullable.Should().BeTrue();
        column.Collation.Should().Be("Japanese_CI_AS");
    }

    /// <summary>
    /// DEFAULT 制約のある列の型の変更が、制約を外して型を変え、同じ名前・同じ定義で付け直して成功することを検証する。
    /// 制約名に <c>]</c> と <c>'</c>、定義に <c>'</c> を含めて、動的 SQL への埋め込みを確かめる（以前はエラー 5074 で失敗した）。
    /// </summary>
    [Theory(
        DisplayName = "[Integration] SQL Server 同期: DEFAULT 制約のある列の型の変更は、同じ名前と定義で付け直して成功する"
    )]
    [InlineData("varchar(20)", "('it''s')", "nvarchar(40)")]
    [InlineData("int", "((5))", "bigint")]
    public async Task AlterColumn_WithDefaultConstraint_ReaddsSameDefault(
        string oldType,
        string definition,
        string newType
    )
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);
        await fixture.ExecuteAsync(
            $"CREATE TABLE [cd] ([id] int NOT NULL PRIMARY KEY, [v] {oldType} NOT NULL "
                + $"CONSTRAINT [DF_a]]b'c] DEFAULT {definition});",
            Ct
        );

        var (result, script) = await AlterAsync("cd", "v", newType, nullable: false);

        result.Committed.Should().BeTrue($"{result.Error}\nSQL:\n{script}");
        var column = await ColumnAsync("cd", "v");
        column.DefaultName.Should().Be("DF_a]b'c");
        column.DefaultDefinition.Should().Be(definition);
        column.Type.Should().Be(newType.Split('(')[0]);
    }

    /// <summary>
    /// 既定値が新しい型に合わない型の変更は、SQL Server では同期が通り（DEFAULT 制約の付け直しでは既定値を確かめない）、
    /// 既定値を使う行の追加で初めて失敗することを検証する（計画は事前に警告する＝J2）。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] SQL Server 同期: 既定値が新しい型に合わない型の変更は通るが、既定値を使う追加が失敗する"
    )]
    public async Task AlterColumn_IncompatibleDefault_SucceedsButInsertFails()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);
        await fixture.ExecuteAsync(
            "CREATE TABLE [cx] ([id] int NOT NULL PRIMARY KEY, [v] varchar(20) NOT NULL "
                + "CONSTRAINT [DF_cx_v] DEFAULT ('abc'));",
            Ct
        );

        var (result, script) = await AlterAsync("cx", "v", "int", nullable: false);

        result.Committed.Should().BeTrue($"{result.Error}\nSQL:\n{script}");
        var column = await ColumnAsync("cx", "v");
        column.Type.Should().Be("int");
        column.DefaultName.Should().Be("DF_cx_v");
        column.DefaultDefinition.Should().Be("('abc')");

        var insert = async () =>
            await fixture.ExecuteAsync("INSERT INTO [cx] ([id]) VALUES (1);", Ct);
        await insert.Should().ThrowAsync<SqlException>();
    }

    /// <summary>
    /// 既定値のある列の型の種類を変える同期で、取込の既定値が計画の文脈へ渡り、計画が事前に警告することを検証する（J2）。
    /// 実行したときの結果も固定する（SQL Server は DEFAULT 制約の付け直しで既定値を確かめないので、同期は通る＝失敗は既定値を使う追加で起きる）。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] SQL Server 同期: 既定値のある列の型の種類の変更は事前に警告される"
    )]
    public async Task TypeKindChangeWithDefault_IsWarned()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);
        await fixture.ExecuteAsync(
            "CREATE TABLE [jd] ([id] int NOT NULL PRIMARY KEY, [v] varchar(20) NOT NULL CONSTRAINT [DF_jd_v] DEFAULT ('abc'));",
            Ct
        );

        var provider = new SqlServerProvider();
        var live = await provider.SchemaImporter.ImportAsync(fixture.ConnectionString, 120, Ct);
        var target = live.Entities.Select(e => e.Clone(preserveId: true)).ToList();
        target.Single(e => e.TableName == "jd").Columns.Single(c => c.Name == "v").DataType = "int";
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
        result.Committed.Should().Be(true, $"{result.Error}{System.Environment.NewLine}{script}");
    }

    /// <summary>
    /// DEFAULT 制約のある列の削除が、制約を外してから列を消して成功することを検証する（以前はエラー 5074 で失敗した）。
    /// 制約名に <c>]</c> と <c>'</c> を含め、同じ名前の列を持つ別の表の DEFAULT 制約は残ることも確かめる（表は OBJECT_ID
    /// で絞る）。DEFAULT 制約の無い列の削除も同じ文で通る。
    /// </summary>
    [Theory(
        DisplayName = "[Integration] SQL Server 同期: DEFAULT 制約のある列の削除は、制約を外して成功する"
    )]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DropColumn_WithDefaultConstraint_Succeeds(bool withDefault)
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);
        var constraint = withDefault ? " CONSTRAINT [DF_a]]b'c] DEFAULT ((5))" : "";
        await fixture.ExecuteAsync(
            $"CREATE TABLE [dd] ([id] int NOT NULL PRIMARY KEY, [v] int NOT NULL{constraint});"
                + "CREATE TABLE [dd2] ([id] int NOT NULL PRIMARY KEY, "
                + "[v] int NOT NULL CONSTRAINT [DF_other] DEFAULT ((7)));",
            Ct
        );

        var plan = new SyncPlanner().BuildPlan(
            [
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.DropColumn,
                    TableName = "dd",
                    ColumnName = "v",
                    IsSelected = true,
                },
            ],
            TestDialectCapabilities.Neutral
        );
        var script = new SqlServerSyncScriptBuilder().Build(plan);
        var result = await new SqlServerSchemaSyncExecutor().ExecuteAsync(
            fixture.ToDbConnectionSettings(),
            script,
            Ct
        );

        result.Committed.Should().BeTrue($"{result.Error}\nSQL:\n{script}");
        (await ColumnCountAsync("dd", "v")).Should().Be(0);
        (await ColumnAsync("dd2", "v"))
            .DefaultName.Should()
            .Be("DF_other", "別の表の DEFAULT 制約は残る");
    }

    /// <summary>表の列の数を数える（列が無ければ 0）</summary>
    private async Task<int> ColumnCountAsync(string table, string column)
    {
        await using var conn = await fixture.OpenConnectionAsync(Ct);
        await using var cmd = new SqlCommand(
            "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(@t) AND name = @c;",
            conn
        );
        cmd.Parameters.AddWithValue("@t", table);
        cmd.Parameters.AddWithValue("@c", column);

        return (int)(await cmd.ExecuteScalarAsync(Ct))!;
    }

    /// <summary>1 列の変更を同期として実行する（変更前の列は DB の型を持つ）</summary>
    private async Task<(SchemaSyncResult Result, string Script)> AlterAsync(
        string table,
        string column,
        string type,
        bool nullable
    )
    {
        var plan = new SyncPlanner().BuildPlan(
            [
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AlterColumn,
                    TableName = table,
                    ColumnName = column,
                    Column = new Column
                    {
                        Name = column,
                        DataType = type,
                        IsNullable = nullable,
                    },
                    OldColumn = new Column { Name = column, DataType = "(live)" },
                    IsSelected = true,
                },
            ],
            TestDialectCapabilities.Neutral
        );
        var script = new SqlServerSyncScriptBuilder().Build(plan);
        var result = await new SqlServerSchemaSyncExecutor().ExecuteAsync(
            fixture.ToDbConnectionSettings(),
            script,
            Ct
        );
        return (result, script);
    }

    /// <summary>列の型名・長さ・NULL 許容・照合順序・DEFAULT 制約の名前と定義を読む</summary>
    private async Task<(
        string Type,
        int Length,
        bool Nullable,
        string? Collation,
        string? DefaultName,
        string? DefaultDefinition
    )> ColumnAsync(string table, string column)
    {
        await using var conn = await fixture.OpenConnectionAsync(Ct);
        await using var cmd = new SqlCommand(
            """
            SELECT t.name, c.max_length, c.is_nullable, c.collation_name, dc.name, dc.definition
            FROM sys.columns c
            JOIN sys.types t ON t.user_type_id = c.user_type_id
            LEFT JOIN sys.default_constraints dc
              ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
            WHERE c.object_id = OBJECT_ID(@t) AND c.name = @c;
            """,
            conn
        );
        cmd.Parameters.AddWithValue("@t", table);
        cmd.Parameters.AddWithValue("@c", column);
        await using var reader = await cmd.ExecuteReaderAsync(Ct);
        (await reader.ReadAsync(Ct)).Should().BeTrue();
        var type = reader.GetString(0);
        var length = reader.GetInt16(1);

        return (
            type,
            type.StartsWith('n') ? length / 2 : length,
            reader.GetBoolean(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5)
        );
    }
}
