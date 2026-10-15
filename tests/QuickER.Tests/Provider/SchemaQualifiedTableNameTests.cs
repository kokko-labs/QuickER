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
/// ドットを含む表名を表せない方言（SQLite）だけが、DDL で注記し、同期の計画で表ごとに警告することを、
/// 5 方言の実際の宣言で検証する。
/// </summary>
public sealed class SchemaQualifiedTableNameTests
{
    private const string Note = "in the attached database";

    private static readonly IDatabaseProvider[] Providers =
    [
        new SqlServerProvider(),
        new PostgreSqlProvider(),
        new MySqlProvider(),
        new OracleProvider(),
        new SqliteProvider(),
    ];

    /// <summary>DDL は、ドットを含む表名を表せない方言でだけ、その表の CREATE TABLE の前に注記する</summary>
    [Theory(DisplayName = "DDL: ドットを含む表名は SQLite だけが注記する")]
    [InlineData(DialectNames.SqlServer, false)]
    [InlineData(DialectNames.PostgreSql, false)]
    [InlineData(DialectNames.MySql, false)]
    [InlineData(DialectNames.Oracle, false)]
    [InlineData(DialectNames.Sqlite, true)]
    public void Ddl_DottedTableName_IsAnnotatedOnlyWhereDeclared(string dialect, bool annotated)
    {
        var sql = Provider(dialect)
            .DdlGenerator.Build(
                new ErDiagram { Entities = [Table("sales.orders"), Table("plain")] }
            );

        if (annotated)
        {
            sql.Should()
                .Contain(
                    "-- Note: this database reads sales.orders as table orders in the attached database sales;"
                );
            sql.Split(Note).Should().HaveCount(2, "ドットを含まない表には注記しない");
        }
        else
        {
            sql.Should().NotContain(Note);
        }
    }

    /// <summary>
    /// 同期の計画は、ドットを含む表名を表せない方言でだけ、選んだ項目が触れるその表を表ごとに 1 回警告する
    /// （項目の表でなく、外部キーの親として触れる表も数える）
    /// </summary>
    [Theory(DisplayName = "同期: 選んだ項目が触れるドット付きの表は SQLite だけが表ごとに警告する")]
    [InlineData(DialectNames.SqlServer, false)]
    [InlineData(DialectNames.PostgreSql, false)]
    [InlineData(DialectNames.MySql, false)]
    [InlineData(DialectNames.Oracle, false)]
    [InlineData(DialectNames.Sqlite, true)]
    public void Plan_DottedTableName_WarnsOnlyWhereDeclared(string dialect, bool warns)
    {
        var parent = Table("sales.orders");
        var child = Table("plain");
        child.Columns.Add(
            new Column
            {
                Name = "order_id",
                DataType = "INTEGER",
                IsNullable = false,
            }
        );
        var relationship = new Relationship
        {
            SourceEntityId = parent.Id,
            TargetEntityId = child.Id,
            Type = RelationshipType.OneToMany,
            ColumnPairs = [new(parent.Columns[0].Id, child.Columns[1].Id)],
            ConstraintName = "FK_plain_orders",
        };
        var provider = Provider(dialect);

        var plan = new SyncPlanner().BuildPlan(
            [
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AddForeignKey,
                    TableName = child.TableName,
                    ColumnName = "order_id",
                    ForeignKeyColumnPairs = [new("id", "order_id")],
                    ParentEntity = parent,
                    ChildEntity = child,
                    Relationship = relationship,
                    IsSelected = true,
                },
            ],
            provider.SyncCapabilities,
            new SyncPlanContext
            {
                LiveEntities = [parent.Clone(preserveId: true), child.Clone(preserveId: true)],
            }
        );

        var warnings = plan
            .Warnings.Where(w => w.Kind == SyncPlanWarningKind.SchemaQualifiedTableName)
            .ToList();

        if (warns)
        {
            warnings
                .Should()
                .Equal(
                    new SyncPlanWarning(
                        SyncPlanWarningKind.SchemaQualifiedTableName,
                        "sales.orders",
                        string.Empty
                    )
                );
        }
        else
        {
            warnings.Should().BeEmpty();
        }
    }

    private static IDatabaseProvider Provider(string dialect) =>
        Providers.Single(p => p.Name == dialect);

    private static Entity Table(string name)
    {
        var entity = new Entity { TableName = name };
        entity.Columns.Add(
            new Column
            {
                Name = "id",
                DataType = "INTEGER",
                IsPrimaryKey = true,
                IsNullable = false,
            }
        );
        return entity;
    }
}
