using AwesomeAssertions;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.MySql;
using QuickER.Provider.Oracle;
using QuickER.Provider.PostgreSql;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;

namespace QuickER.Tests.Provider;

/// <summary>
/// DDL が作る制約の既定名と、DB 同期が外す・付ける制約の名前が 5 方言とも一致することを検証するテストクラス
/// </summary>
/// <remarks>
/// <para>
/// 同期は「DDL が作ったはずの名前」で制約を探して外す。同期の計画はテーブル名を <c>Trim</c> してから名前を組み立てる
/// ため、前後に空白のあるテーブル名（JSON を手で書き換えた図で起きる）では、DDL が <c>FK__Orders_…</c>、
/// 同期が <c>FK_Orders_…</c> と食い違い、存在しない名前の制約を外そうとして失敗していた。
/// 規則は <see cref="ConstraintNames"/> 1 か所にまとめ、安全化の最初に前後の空白を除く。
/// </para>
/// </remarks>
public class ConstraintNameConsistencyTests
{
    private const string ChildTable = " Orders";
    private const string ParentTable = "Customers";

    /// <summary>期待する外部キー名・主キー名（前後の空白を除いた名前から作る）</summary>
    private const string ExpectedForeignKey = "FK_Orders_Customers";
    private const string ExpectedPrimaryKey = "PK_Orders";

    private sealed record Dialect(
        string Name,
        IDdlGenerator Ddl,
        ISyncScriptBuilder Sync,
        bool NamesPrimaryKey
    );

    /// <summary>逐次 DDL で同期する 4 方言（SQLite は再構築方式のため別に検証する）</summary>
    private static readonly Dialect[] SequentialDialects =
    [
        new("SqlServer", new SqlServerDdlGenerator(), new SqlServerSyncScriptBuilder(), true),
        new("PostgreSql", new PostgreSqlDdlGenerator(), new PostgreSqlSyncScriptBuilder(), true),
        new("MySql", new MySqlDdlGenerator(), new MySqlSyncScriptBuilder(), false),
        new("Oracle", new OracleDdlGenerator(), new OracleSyncScriptBuilder(), true),
    ];

    private static Entity Parent() =>
        new()
        {
            TableName = ParentTable,
            Columns =
            {
                new Column
                {
                    Name = "id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
            },
        };

    private static Entity Child() =>
        new()
        {
            TableName = ChildTable,
            Columns =
            {
                new Column
                {
                    Name = "id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Name = "customer_id",
                    DataType = "int",
                    IsNullable = false,
                },
            },
        };

    /// <summary>名前を持たない外部キー 1 本でつながる親子の図</summary>
    private static (
        ErDiagram Diagram,
        Entity Parent,
        Entity Child,
        Relationship Relationship
    ) Diagram()
    {
        var parent = Parent();
        var child = Child();
        var relationship = new Relationship
        {
            SourceEntityId = parent.Id,
            TargetEntityId = child.Id,
            Type = RelationshipType.OneToMany,
            ColumnPairs = [new(parent.Columns[0].Id, child.Columns[1].Id)],
        };

        return (
            new ErDiagram { Entities = { parent, child }, Relationships = { relationship } },
            parent,
            child,
            relationship
        );
    }

    [Fact(
        DisplayName = "前後に空白のあるテーブル名で、DDL の外部キー名と同期が付ける名前が一致する（4 方言）"
    )]
    public void ForeignKeyName_MatchesBetweenDdlAndSync()
    {
        foreach (var dialect in SequentialDialects)
        {
            var (diagram, parent, child, relationship) = Diagram();

            var ddl = dialect.Ddl.Build(diagram);
            var sync = dialect.Sync.Build(
                new SyncPlanner().BuildPlan(
                    [
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.AddForeignKey,
                            TableName = ChildTable,
                            ParentEntity = parent,
                            ChildEntity = child,
                            Relationship = relationship,
                            ForeignKeyColumnPairs = [new("id", "customer_id")],
                            IsSelected = true,
                        },
                    ],
                    new SyncDialectCapabilities()
                )
            );

            ddl.Should().Contain(ExpectedForeignKey, $"{dialect.Name} の DDL");
            sync.Should()
                .Contain(ExpectedForeignKey, $"{dialect.Name} の同期は DDL と同じ名前で付ける");
        }
    }

    [Fact(
        DisplayName = "前後に空白のあるテーブル名で、DDL の主キー名と同期が付ける名前が一致する（3 方言）"
    )]
    public void PrimaryKeyName_MatchesBetweenDdlAndSync()
    {
        foreach (var dialect in SequentialDialects.Where(d => d.NamesPrimaryKey))
        {
            var (diagram, _, child, _) = Diagram();

            var ddl = dialect.Ddl.Build(diagram);
            var sync = dialect.Sync.Build(
                new SyncPlanner().BuildPlan(
                    [
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.AlterPrimaryKey,
                            TableName = ChildTable,
                            Entity = child,
                            IsSelected = true,
                        },
                    ],
                    new SyncDialectCapabilities()
                )
            );

            ddl.Should().Contain(ExpectedPrimaryKey, $"{dialect.Name} の DDL");
            sync.Should()
                .Contain(ExpectedPrimaryKey, $"{dialect.Name} の同期は DDL と同じ名前で付ける");
        }
    }

    /// <summary>
    /// SQLite は再構築の <c>CREATE TABLE</c> に制約名が載る。DDL と、同期の計画が組み立てる再構築が同じ名前を使う
    /// </summary>
    [Fact(DisplayName = "前後に空白のあるテーブル名で、SQLite の DDL と再構築が同じ制約名を使う")]
    public void Sqlite_DdlAndRebuildAgreeOnConstraintNames()
    {
        var (diagram, parent, child, relationship) = Diagram();
        var ddl = new SqliteDdlGenerator().Build(diagram);

        // 子テーブルの列定義の変更で再構築を起こし、live の外部キー（名前なし）を再構築の CREATE TABLE へ載せる
        // （列の追加は ALTER TABLE ADD COLUMN で済むので再構築しない）
        var provider = new SqliteProvider();
        var plan = new SyncPlanner().BuildPlan(
            [
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AlterColumn,
                    TableName = ChildTable,
                    ColumnName = "customer_id",
                    Column = new Column
                    {
                        Name = "customer_id",
                        DataType = "int",
                        IsNullable = true,
                    },
                    IsSelected = true,
                },
            ],
            provider.SyncCapabilities,
            new SyncPlanContext
            {
                LiveEntities = [parent, child],
                LiveRelationships = [relationship],
            }
        );
        var sync = provider.SyncScriptBuilder.Build(plan);

        ddl.Should().Contain(ExpectedForeignKey).And.Contain(ExpectedPrimaryKey);
        sync.Should().Contain(ExpectedForeignKey).And.Contain(ExpectedPrimaryKey);
    }
}
