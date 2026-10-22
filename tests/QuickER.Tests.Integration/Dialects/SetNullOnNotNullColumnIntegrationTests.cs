using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using Npgsql;
using Oracle.ManagedDataAccess.Client;
using QuickER.Model;
using QuickER.Provider.MySql;
using QuickER.Provider.Oracle;
using QuickER.Provider.PostgreSql;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;

namespace QuickER.Tests.Integration.Dialects;

/// <summary>
/// 参照アクション SET NULL の外部キーを NOT NULL の子の列へ張る DDL が、方言ごとにどうなるかを実 DB で固定する
/// （作れない方言＝SQL Server・MySQL は DDL の適用が断られ、作れる方言＝PostgreSQL・Oracle・SQLite は適用できるが、
/// 参照される親の行の削除が NOT NULL の違反で失敗する）。どの方言でも DDL は外部キーの前に注記する。
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

    /// <summary>親と、それを参照する子の行を 1 行ずつ入れる文（識別子は DDL と同じく引用する＝Oracle は小文字の名前で作る）</summary>
    public const string InsertRows =
        "INSERT INTO \"sn_parent\" (\"id\") VALUES (1);\n"
        + "INSERT INTO \"sn_child\" (\"id\", \"parent_id\") VALUES (1, 1);";

    /// <summary>参照される親の行を消す文</summary>
    public const string DeleteParent = "DELETE FROM \"sn_parent\" WHERE \"id\" = 1";
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

[Trait("Category", "Integration")]
[Collection(PostgreSqlContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class PostgreSqlSetNullOnNotNullColumnIntegrationTests(
    PostgreSqlContainerFixture fixture
)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>
    /// PostgreSQL は NOT NULL の子の列への SET NULL の外部キーを作れるが、参照される親の行の削除は NOT NULL の違反
    /// （23502）で失敗する
    /// </summary>
    [Fact(
        DisplayName = "[Integration] PostgreSQL: NOT NULL の子の列への SET NULL の外部キーは作れるが、親の削除は 23502"
    )]
    public async Task SetNullOnNotNullColumn_IsCreatedButDeleteFails()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        var ddl = new PostgreSqlDdlGenerator().Build(SetNullOnNotNullColumnSchema.Build("integer"));

        ddl.Should().Contain(SetNullOnNotNullColumnSchema.Note);
        await fixture.ExecuteAsync(ddl, Ct);
        await fixture.ExecuteAsync(SetNullOnNotNullColumnSchema.InsertRows, Ct);

        var delete = () => fixture.ExecuteAsync(SetNullOnNotNullColumnSchema.DeleteParent, Ct);
        (await delete.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("23502");
    }
}

[Trait("Category", "Integration")]
[Collection(OracleContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class OracleSetNullOnNotNullColumnIntegrationTests(OracleContainerFixture fixture)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>
    /// Oracle は NOT NULL の子の列への ON DELETE SET NULL の外部キーを作れるが、参照される親の行の削除は ORA-01407 で
    /// 失敗する
    /// </summary>
    [Fact(
        DisplayName = "[Integration] Oracle: NOT NULL の子の列への SET NULL の外部キーは作れるが、親の削除は ORA-01407"
    )]
    public async Task SetNullOnNotNullColumn_IsCreatedButDeleteFails()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        var ddl = new OracleDdlGenerator().Build(SetNullOnNotNullColumnSchema.Build("NUMBER(10)"));

        ddl.Should().Contain(SetNullOnNotNullColumnSchema.Note);
        await fixture.ExecuteAsync(ddl, Ct);
        await fixture.ExecuteAsync(SetNullOnNotNullColumnSchema.InsertRows, Ct);

        var delete = () => fixture.ExecuteAsync(SetNullOnNotNullColumnSchema.DeleteParent, Ct);
        (await delete.Should().ThrowAsync<OracleException>()).Which.Number.Should().Be(1407);
    }
}

[Trait("Category", "Integration")]
public sealed class SqliteSetNullOnNotNullColumnIntegrationTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>
    /// SQLite は NOT NULL の子の列への SET NULL の外部キーを作れるが、参照される親の行の削除は NOT NULL の違反
    /// （SQLITE_CONSTRAINT＝19）で失敗する
    /// </summary>
    [Fact(
        DisplayName = "[Integration] SQLite: NOT NULL の子の列への SET NULL の外部キーは作れるが、親の削除は 19"
    )]
    public async Task SetNullOnNotNullColumn_IsCreatedButDeleteFails()
    {
        using var db = SqliteTempDatabase.Create();
        var ddl = new SqliteDdlGenerator().Build(SetNullOnNotNullColumnSchema.Build("INTEGER"));

        ddl.Should().Contain(SetNullOnNotNullColumnSchema.Note);
        await db.ApplyDdlAsync(ddl, Ct);

        await using var connection = new SqliteConnection(
            db.ReadWriteConnectionString + ";Foreign Keys=True"
        );
        await connection.OpenAsync(Ct);
        await using (var insert = connection.CreateCommand())
        {
            insert.CommandText = SetNullOnNotNullColumnSchema.InsertRows;
            await insert.ExecuteNonQueryAsync(Ct);
        }

        await using var delete = connection.CreateCommand();
        delete.CommandText = SetNullOnNotNullColumnSchema.DeleteParent;
        var run = () => delete.ExecuteNonQueryAsync(Ct);
        (await run.Should().ThrowAsync<SqliteException>()).Which.SqliteErrorCode.Should().Be(19);
        SqliteConnection.ClearPool(connection);
    }
}
