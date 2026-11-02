using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.MySql;
using QuickER.Provider.Oracle;
using QuickER.Provider.PostgreSql;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;
using QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

namespace QuickER.Tests.Integration.Dialects;

/// <summary>
/// 参照アクション SET NULL の外部キーを NOT NULL の子の列へ張る DDL の図（作れない方言＝SQL Server・MySQL は DDL の適用が
/// 断られることを実 DB で固定し、どの方言でも DDL は外部キーの前に注記する。作れる方言＝PostgreSQL・Oracle・SQLite で親の削除が
/// NOT NULL の違反で失敗することは、同期計画の期待の行 <c>realdb-set-null-not-null-child-ddl-applies-delete-fails</c>）。
/// </summary>
internal static class SetNullOnNotNullColumnSchema
{
    /// <summary>DDL の注記の先頭</summary>
    public const string Note = "-- Note: SET NULL on the NOT NULL referencing column(s) parent_id;";

    /// <summary>親 sn_parent(id) ← 子 sn_child(id, parent_id NOT NULL) の ON DELETE SET NULL の外部キー sn_fk</summary>
    public static ErDiagram Build(string keyType)
    {
        var parent = new Entity { TableName = "sn_parent" };
        parent.Columns.Add(
            new Column
            {
                Name = "id",
                DataType = keyType,
                IsPrimaryKey = true,
                IsNullable = false,
            }
        );
        var child = new Entity { TableName = "sn_child" };
        child.Columns.Add(
            new Column
            {
                Name = "id",
                DataType = keyType,
                IsPrimaryKey = true,
                IsNullable = false,
            }
        );
        child.Columns.Add(
            new Column
            {
                Name = "parent_id",
                DataType = keyType,
                IsNullable = false,
            }
        );

        return new ErDiagram
        {
            Entities = [parent, child],
            Relationships =
            [
                new Relationship
                {
                    SourceEntityId = parent.Id,
                    TargetEntityId = child.Id,
                    Type = RelationshipType.OneToMany,
                    ColumnPairs = [new(parent.Columns[0].Id, child.Columns[1].Id)],
                    ConstraintName = "sn_fk",
                    OnDelete = ForeignKeyReferentialAction.SetNull,
                },
            ],
        };
    }
}

[Trait("Category", "Integration")]
[Collection(SqlServerContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class SqlServerSetNullOnNotNullColumnIntegrationTests(
    SqlServerContainerFixture fixture
)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>SQL Server は NOT NULL の子の列への SET NULL の外部キーを作れない（エラー 1761）</summary>
    [Fact(
        DisplayName = "[Integration] SQL Server: NOT NULL の子の列への SET NULL の外部キーは注記つきで、適用はエラー 1761"
    )]
    public async Task SetNullOnNotNullColumn_DdlIsRefused()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        var ddl = new SqlServerDdlGenerator().Build(SetNullOnNotNullColumnSchema.Build("int"));

        ddl.Should().Contain(SetNullOnNotNullColumnSchema.Note);
        var apply = () => fixture.ExecuteAsync(ddl, Ct);
        (await apply.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(1761);
    }
}

[Trait("Category", "Integration")]
[Collection(MySqlContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class MySqlSetNullOnNotNullColumnIntegrationTests(MySqlContainerFixture fixture)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>MySQL は NOT NULL の子の列への SET NULL の外部キーを作れない（エラー 1830）</summary>
    [Fact(
        DisplayName = "[Integration] MySQL: NOT NULL の子の列への SET NULL の外部キーは注記つきで、適用はエラー 1830"
    )]
    public async Task SetNullOnNotNullColumn_DdlIsRefused()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        var ddl = new MySqlDdlGenerator().Build(SetNullOnNotNullColumnSchema.Build("int"));

        ddl.Should().Contain(SetNullOnNotNullColumnSchema.Note);
        var apply = () => fixture.ExecuteAsync(ddl, Ct);
        (await apply.Should().ThrowAsync<MySqlException>()).Which.Number.Should().Be(1830);
    }
}

/// <summary>
/// NOT NULL の子の列への SET NULL の外部キーを作れる方言（PostgreSQL・Oracle・SQLite）の DDL が、外部キーの前に注記することを
/// 確かめる（DDL の文字列だけ・DB を使わない）。DDL を当てて親の削除が失敗することは、同期計画の期待の行
/// <c>realdb-set-null-not-null-child-ddl-applies-delete-fails</c>（<see cref="SyncPlanExpectationRows"/>）が実 DB で確かめる。
/// </summary>
[Trait("Category", "Integration")]
public sealed class SetNullOnNotNullColumnDdlNoteTests
{
    [Theory(
        DisplayName = "[Integration] NOT NULL の子の列への SET NULL の外部キーを作れる方言の DDL は、外部キーの前に注記する"
    )]
    [InlineData(DialectNames.PostgreSql, "integer")]
    [InlineData(DialectNames.Oracle, "NUMBER(10)")]
    [InlineData(DialectNames.Sqlite, "INTEGER")]
    public void SetNullOnNotNullColumn_DdlHasNote(string dialect, string keyType)
    {
        IDdlGenerator generator = dialect switch
        {
            DialectNames.PostgreSql => new PostgreSqlDdlGenerator(),
            DialectNames.Oracle => new OracleDdlGenerator(),
            _ => new SqliteDdlGenerator(),
        };

        generator
            .Build(SetNullOnNotNullColumnSchema.Build(keyType))
            .Should()
            .Contain(SetNullOnNotNullColumnSchema.Note);
    }
}
