using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
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
/// 作り直しの差分項目（<see cref="SchemaDiffKind.AlterForeignKey"/> / <see cref="SchemaDiffKind.AlterUniqueConstraint"/>）が、
/// 計画の入口で削除と追加の 2 項目へ展開され、計画のどこにも作り直しの種別のまま残らないことを 5 方言で固定する。
/// </summary>
/// <remarks>
/// 計画のセクション（<c>SyncPlanner.SectionOrder</c>）にも方言別レンダラーにも作り直しの種別は無い。展開を外すと
/// 例外にはならず、選んだ作り直しが黙って計画から消える（実行しても何も変わらない）ため、「残らない」ことと
/// 「選んだ作り直しが削除と追加として現れる」ことの両方を表明する。
/// </remarks>
public class SyncPlanAlterExpansionTests
{
    /// <summary>5 方言の同期ケーパビリティ</summary>
    public static TheoryData<string, bool> Cases()
    {
        var data = new TheoryData<string, bool>();

        foreach (var dialect in Capabilities.Keys)
        {
            data.Add(dialect, true);
            data.Add(dialect, false);
        }

        return data;
    }

    /// <summary>5 方言の同期ケーパビリティ</summary>
    private static readonly Dictionary<string, SyncDialectCapabilities> Capabilities = new()
    {
        ["SqlServer"] = new SqlServerProvider().SyncCapabilities,
        ["PostgreSql"] = new PostgreSqlProvider().SyncCapabilities,
        ["MySql"] = new MySqlProvider().SyncCapabilities,
        ["Oracle"] = new OracleProvider().SyncCapabilities,
        ["Sqlite"] = new SqliteProvider().SyncCapabilities,
    };

    /// <summary>
    /// 外部キー（参照アクションの変更）と、名前付き一意制約（構成列の変更）の作り直しを 1 つずつ含む差分から計画を作り、
    /// 作り直しの種別が残らないこと・選んだときだけ削除と追加が現れることを検証する。
    /// </summary>
    [Theory(
        DisplayName = "作り直しの展開: 計画に作り直しの種別は残らず、選んだ作り直しは削除と追加として現れる"
    )]
    [MemberData(nameof(Cases))]
    public void AlterItems_AreExpandedAndNeverReachThePlan(string dialect, bool selectAlters)
    {
        var capabilities = Capabilities[dialect];
        var (live, liveRelationships) = Build(
            onDelete: ForeignKeyReferentialAction.NoAction,
            uniqueColumns: ["code"]
        );
        var (target, targetRelationships) = Build(
            onDelete: ForeignKeyReferentialAction.Cascade,
            uniqueColumns: ["code", "customer_id"]
        );

        var items = new SchemaDiffService()
            .Compute(live, liveRelationships, target, targetRelationships, capabilities)
            .Items;
        items
            .Select(i => i.Kind)
            .Should()
            .BeEquivalentTo([SchemaDiffKind.AlterForeignKey, SchemaDiffKind.AlterUniqueConstraint]);

        foreach (var item in items)
        {
            item.IsSelected = selectAlters;
        }

        var plan = new SyncPlanner().BuildPlan(
            items,
            capabilities,
            new SyncPlanContext { LiveEntities = live, LiveRelationships = liveRelationships }
        );
        var planned = plan
            .Sections.SelectMany(s => s.Items)
            .Concat(plan.Rebuilds.SelectMany(r => r.SourceItems))
            .Concat(plan.Reorders.SelectMany(r => r.SourceItems))
            .ToList();

        plan.Sections.Select(s => s.Kind)
            .Concat(planned.Select(i => i.Kind))
            .Should()
            .NotContain([SchemaDiffKind.AlterForeignKey, SchemaDiffKind.AlterUniqueConstraint]);

        if (!selectAlters)
        {
            planned.Should().BeEmpty();
            return;
        }

        planned
            .Should()
            .ContainSingle(i =>
                i.Kind == SchemaDiffKind.DropForeignKey
                && i.Relationship!.OnDelete == ForeignKeyReferentialAction.NoAction
            );
        planned
            .Should()
            .ContainSingle(i =>
                i.Kind == SchemaDiffKind.AddForeignKey
                && i.Relationship!.OnDelete == ForeignKeyReferentialAction.Cascade
            );
        planned
            .Should()
            .ContainSingle(i =>
                i.Kind == SchemaDiffKind.DropUniqueConstraint
                && i.UniqueConstraintName == "UQ_orders_code"
                && i.UniqueConstraintColumns.SequenceEqual(new[] { "code" })
            );
        planned
            .Should()
            .ContainSingle(i =>
                i.Kind == SchemaDiffKind.AddUniqueConstraint
                && i.UniqueConstraintName == "UQ_orders_code"
                && i.UniqueConstraintColumns.SequenceEqual(new[] { "code", "customer_id" })
            );
    }

    /// <summary>customer(id) ← orders(id, customer_id, code) と、名前付き外部キー・一意制約 UQ_orders_code を持つ図を作る</summary>
    private static (List<Entity> Entities, List<Relationship> Relationships) Build(
        ForeignKeyReferentialAction onDelete,
        string[] uniqueColumns
    )
    {
        var customer = new Entity
        {
            TableName = "customer",
            Columns =
            {
                new Column
                {
                    Name = "id",
                    DataType = "INTEGER",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
            },
        };
        var orders = new Entity
        {
            TableName = "orders",
            Columns =
            {
                new Column
                {
                    Name = "id",
                    DataType = "INTEGER",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Name = "customer_id",
                    DataType = "INTEGER",
                    IsNullable = false,
                },
                new Column
                {
                    Name = "code",
                    DataType = "INTEGER",
                    IsNullable = false,
                },
            },
        };
        orders.UniqueConstraints.Add(
            new UniqueConstraint
            {
                Name = "UQ_orders_code",
                ColumnIds =
                [
                    .. uniqueColumns.Select(n => orders.Columns.Single(c => c.Name == n).Id),
                ],
            }
        );

        return (
            [customer, orders],
            [
                new Relationship
                {
                    SourceEntityId = customer.Id,
                    TargetEntityId = orders.Id,
                    ColumnPairs = [new(customer.Columns[0].Id, orders.Columns[1].Id)],
                    ConstraintName = "FK_orders_customer",
                    OnDelete = onDelete,
                },
            ]
        );
    }
}
