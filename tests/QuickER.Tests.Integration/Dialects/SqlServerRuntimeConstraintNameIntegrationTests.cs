using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Data.SqlClient;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.SqlServer;
using QuickER.Tests.Integration;

namespace QuickER.Tests.Integration.Dialects;

/// <summary>
/// 同期スクリプトが実行時にカタログから読む制約名（旧主キー名・名前不明の外部キー名）の扱いを、実 SQL Server で検証する統合テスト。
/// </summary>
/// <remarks>
/// <para>
/// 主キーの付け替えと、名前の分からない外部キーの削除は、制約名を実行時に <c>sys.key_constraints</c> /
/// <c>sys.foreign_keys</c> から読んで動的 SQL で外す。ここでは差分から作れない項目（名前の分からない外部キーの削除・
/// 存在しない表の主キーの解除）を手で組んで、次の 3 つを確かめる（差分から作れる主キーの付け替えは同期計画の期待の行の実 DB の段が持つ）。
/// </para>
/// <list type="bullet">
///   <item><c>]</c> や <c>'</c> を含む正当な制約名でも同期が成功する（読んだ名前を <c>QUOTENAME</c> で埋めている）</item>
///   <item>別スキーマにある同名テーブルの制約を拾わない（逆引きを <c>OBJECT_ID</c> で 1 テーブルに絞っている）</item>
///   <item>対象のテーブルが無ければ何もせず、エラーにもならない</item>
/// </list>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(SqlServerContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class SqlServerRuntimeConstraintNameIntegrationTests(
    SqlServerContainerFixture fixture
)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>テストで使う別スキーマ（ResetSchemaAsync はテーブルだけを消すので、スキーマは作ったまま使い回す）</summary>
    private const string OtherSchema = "other";

    /// <summary>
    /// 名前の分からない外部キーの削除で、<c>]</c> と <c>'</c> を含む外部キー名が外れ、
    /// 別スキーマの同名の子テーブルの外部キーは残ることを検証する。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] 名前不明の外部キー削除は特殊文字を含む名前を外し別スキーマの同名表は触らない"
    )]
    public async Task DropForeignKey_WithoutName_DropsOnlyTargetTable()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);
        await EnsureOtherSchemaAsync();

        await fixture.ExecuteAsync(
            "CREATE TABLE [dbo].[Parent] ([id] int NOT NULL, CONSTRAINT [PK_Parent] PRIMARY KEY ([id]));"
                + "CREATE TABLE [dbo].[Child] ([id] int NOT NULL, [parent_id] int NOT NULL, "
                + "CONSTRAINT [PK_Child] PRIMARY KEY ([id]), "
                + "CONSTRAINT [FK_odd]]name'x] FOREIGN KEY ([parent_id]) REFERENCES [dbo].[Parent] ([id]));"
                + $"CREATE TABLE [{OtherSchema}].[Child] ([id] int NOT NULL, [parent_id] int NOT NULL, "
                + "CONSTRAINT [PK_other_Child] PRIMARY KEY ([id]), "
                + "CONSTRAINT [FK_other_Child] FOREIGN KEY ([parent_id]) REFERENCES [dbo].[Parent] ([id]));",
            Ct
        );

        var script = await ApplyAsync(DropForeignKeyWithoutName("Child", "Parent"));

        (await ForeignKeysAsync())
            .Should()
            .Equal(
                ["other.Child -> FK_other_Child"],
                $"dbo.Child の外部キーだけが外れるはず\nSQL:\n{script}"
            );
    }

    /// <summary>対象のテーブルが無いとき、主キー・外部キーの動的 DROP が何もせずエラーにもならないことを検証する</summary>
    /// <remarks><c>OBJECT_ID</c> が NULL を返す経路（逆引きが 0 行＝変数が NULL のまま）を通す</remarks>
    [Fact(DisplayName = "[Integration] 対象の表が無いとき動的 DROP は何もせず成功する")]
    public async Task DynamicDrop_MissingTable_IsNoOp()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        // 主キーの解除のみ（新しい主キー列なし）＝付与文は出ず、動的 DROP だけが走る
        var dropPrimaryKey = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AlterPrimaryKey,
            TableName = "missing_table",
            Entity = new Entity { TableName = "missing_table" },
            IsSelected = true,
        };

        await ApplyAsync(
            dropPrimaryKey,
            DropForeignKeyWithoutName("missing_child", "missing_parent")
        );
    }

    // ---------------- ヘルパー ----------------

    /// <summary>差分項目を計画→スクリプト化して実行し、使ったスクリプトを返す</summary>
    private async Task<string> ApplyAsync(params SchemaDiffItem[] items)
    {
        var plan = new SyncPlanner().BuildPlan(items, new SqlServerProvider().SyncCapabilities);
        return await ExecuteAsync(new SqlServerSyncScriptBuilder().Build(plan));
    }

    /// <summary>スクリプトを同期実行器で流し、コミットされたことを表明する</summary>
    private async Task<string> ExecuteAsync(string script)
    {
        var result = await new SqlServerSchemaSyncExecutor().ExecuteAsync(
            fixture.ToDbConnectionSettings(),
            script,
            Ct
        );
        result.Committed.Should().BeTrue($"同期に失敗: {result.Error}\nSQL:\n{script}");
        return script;
    }

    /// <summary>制約名を持たない外部キー削除の差分項目（名前不明パスを通す）を作る</summary>
    private static SchemaDiffItem DropForeignKeyWithoutName(string child, string parent) =>
        new()
        {
            Kind = SchemaDiffKind.DropForeignKey,
            TableName = child,
            ChildEntity = new Entity { TableName = child },
            ParentEntity = new Entity { TableName = parent },
            IsSelected = true,
        };

    /// <summary>別スキーマが無ければ作る</summary>
    private Task EnsureOtherSchemaAsync() =>
        fixture.ExecuteAsync(
            $"IF SCHEMA_ID(N'{OtherSchema}') IS NULL EXEC(N'CREATE SCHEMA [{OtherSchema}]');",
            Ct
        );

    /// <summary>全外部キーを「スキーマ.子テーブル -&gt; 制約名」の形で並べて返す</summary>
    private Task<List<string>> ForeignKeysAsync() =>
        QueryStringsAsync(
            "SELECT SCHEMA_NAME(t.schema_id) + '.' + t.name + ' -> ' + fk.name "
                + "FROM sys.foreign_keys fk JOIN sys.tables t ON fk.parent_object_id = t.object_id "
                + "ORDER BY 1;"
        );

    /// <summary>1 列の文字列結果を読み出す</summary>
    private async Task<List<string>> QueryStringsAsync(string sql)
    {
        var rows = new List<string>();
        await using var conn = await fixture.OpenConnectionAsync(Ct);
        await using var cmd = new SqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(Ct);

        while (await reader.ReadAsync(Ct))
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }
}
