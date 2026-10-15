using System.Linq;
using AwesomeAssertions;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.MySql;
using QuickER.Provider.Oracle;
using QuickER.Provider.PostgreSql;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;
using Xunit;

namespace QuickER.Tests.Provider;

/// <summary>
/// 主キーと同じ列集合の一意制約を、持てない方言（Oracle・SQLite）の DDL・同期の <c>CREATE TABLE</c> が省いて注記することを
/// 5 方言の実際の宣言で検証する（主キーと違う列集合の一意制約は全方言で出す）。
/// </summary>
public sealed class UniqueConstraintOnPrimaryKeyNoteTests
{
    private const string Note = "cannot have a unique constraint on the primary key columns";

    private static readonly IDatabaseProvider[] Providers =
    [
        new SqlServerProvider(),
        new PostgreSqlProvider(),
        new MySqlProvider(),
        new OracleProvider(),
        new SqliteProvider(),
    ];

    /// <summary>DDL は、持てない方言でだけ主キーと同じ列集合の一意制約を省いて注記する</summary>
    [Theory(
        DisplayName = "DDL: 主キーと同じ列集合の一意制約は Oracle・SQLite だけが省いて注記する"
    )]
    [InlineData(DialectNames.SqlServer, false)]
    [InlineData(DialectNames.PostgreSql, false)]
    [InlineData(DialectNames.MySql, false)]
    [InlineData(DialectNames.Oracle, true)]
    [InlineData(DialectNames.Sqlite, true)]
    public void Ddl_OmitsUniqueConstraintOnPrimaryKeyWhereDeclared(string dialect, bool omitted)
    {
        var entity = Item();

        var sql = Provider(dialect).DdlGenerator.Build(new ErDiagram { Entities = [entity] });

        AssertOmission(sql, omitted);
    }

    /// <summary>同期の表の追加は、DDL と同じく持てない方言でだけ省いて注記する</summary>
    [Theory(
        DisplayName = "同期: 表の追加は主キーと同じ列集合の一意制約を Oracle・SQLite だけが省いて注記する"
    )]
    [InlineData(DialectNames.SqlServer, false)]
    [InlineData(DialectNames.PostgreSql, false)]
    [InlineData(DialectNames.MySql, false)]
    [InlineData(DialectNames.Oracle, true)]
    [InlineData(DialectNames.Sqlite, true)]
    public void Sync_AddTable_OmitsUniqueConstraintOnPrimaryKeyWhereDeclared(
        string dialect,
        bool omitted
    )
    {
        var entity = Item();
        var provider = Provider(dialect);
        var plan = new SyncPlanner().BuildPlan(
            [
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AddTable,
                    TableName = entity.TableName,
                    Entity = entity,
                    IsSelected = true,
                },
            ],
            provider.SyncCapabilities,
            new SyncPlanContext()
        );

        AssertOmission(provider.SyncScriptBuilder.Build(plan), omitted);
    }

    /// <summary>
    /// SQLite の再構築でも、主キーの変更で live の一意制約が主キーと同じ列集合になったら、作り直す表から省いて注記する
    /// </summary>
    [Fact(
        DisplayName = "同期: SQLite の再構築は、主キーと同じ列集合になる live の一意制約を省いて注記する"
    )]
    public void Sync_SqliteRebuild_OmitsUniqueConstraintThatBecomesPrimaryKey()
    {
        var live = new Entity { TableName = "item" };
        live.Columns.Add(
            new Column
            {
                Name = "id",
                DataType = "INTEGER",
                IsPrimaryKey = true,
                IsNullable = false,
            }
        );
        live.Columns.Add(
            new Column
            {
                Name = "code",
                DataType = "TEXT",
                IsNullable = false,
            }
        );
        live.UniqueConstraints.Add(
            new UniqueConstraint { Name = "UQ_item_code", ColumnIds = [live.Columns[1].Id] }
        );
        var target = live.Clone(preserveId: true);
        target.Columns[0].IsPrimaryKey = false;
        target.Columns[1].IsPrimaryKey = true;
        var provider = Provider(DialectNames.Sqlite);

        var plan = new SyncPlanner().BuildPlan(
            [
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AlterPrimaryKey,
                    TableName = "item",
                    Entity = target,
                    IsSelected = true,
                },
            ],
            provider.SyncCapabilities,
            new SyncPlanContext { LiveEntities = [live] }
        );
        var sql = provider.SyncScriptBuilder.Build(plan);

        sql.Should().Contain($"{Note}, so UQ_item_code (code) is omitted;");
        sql.Should().NotContain("UNIQUE (\"code\")");
    }

    private static IDatabaseProvider Provider(string dialect) =>
        Providers.Single(p => p.Name == dialect);

    /// <summary>主キー id と、id の一意制約（主キーと同じ列集合）・code の一意制約を持つ表</summary>
    private static Entity Item()
    {
        var entity = new Entity { TableName = "item" };
        entity.Columns.Add(
            new Column
            {
                Name = "id",
                DataType = "INTEGER",
                IsPrimaryKey = true,
                IsNullable = false,
            }
        );
        entity.Columns.Add(
            new Column
            {
                Name = "code",
                DataType = "INTEGER",
                IsNullable = false,
            }
        );
        entity.UniqueConstraints.Add(
            new UniqueConstraint { Name = "UQ_item_id", ColumnIds = [entity.Columns[0].Id] }
        );
        entity.UniqueConstraints.Add(
            new UniqueConstraint { Name = "UQ_item_code", ColumnIds = [entity.Columns[1].Id] }
        );

        return entity;
    }

    private static void AssertOmission(string sql, bool omitted)
    {
        // 主キーと違う列集合の一意制約は全方言で出す
        sql.Should().Contain("UQ_item_code");
        sql.Should().MatchRegex(@"UQ_item_code\W+ UNIQUE \(\W?code\W?\)");

        if (omitted)
        {
            sql.Should().Contain($"{Note}, so UQ_item_id (id) is omitted;");
            sql.Should().NotMatchRegex(@"UQ_item_id\W+ UNIQUE");
        }
        else
        {
            sql.Should().NotContain(Note);
            sql.Should().MatchRegex(@"UQ_item_id\W+ UNIQUE \(\W?id\W?\)");
        }
    }
}
