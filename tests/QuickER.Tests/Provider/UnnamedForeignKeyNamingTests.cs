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
/// 同じテーブルの組に名前の無いリレーションが 2 本ある図で、DDL・同期の差分・同期スクリプトが
/// 同じ一意な外部キー名を使うことを 5 方言で検証するテストクラス
/// </summary>
/// <remarks>
/// <para>
/// 既定名 <c>FK_{子}_{親}</c> だけでは 2 本が同じ名前になり、DDL の 2 本目が失敗する。名前は
/// <see cref="ConstraintNames.ResolveForeignKeyNames"/> で図を書き換えずに求め、2 本目に子の列名を足す。
/// </para>
/// <para>
/// DDL だけを直すと、差分は既定名で探して live の 2 本目を別物とみなし、追加と削除を出し続ける。
/// 同期スクリプトも同じ名前で付けないと、図から作った DB と同期で作った DB の名前が食い違う。
/// </para>
/// </remarks>
public class UnnamedForeignKeyNamingTests
{
    private const string BillName = "FK_orders_customers";
    private const string ShipName = "FK_orders_customers_ship_customer_id";

    private static readonly IDatabaseProvider[] Providers =
    [
        new SqlServerProvider(),
        new PostgreSqlProvider(),
        new MySqlProvider(),
        new OracleProvider(),
        new SqliteProvider(),
    ];

    /// <summary>customers と、customers を 2 回参照する orders（名前は指定したもの）の図</summary>
    private static ErDiagram BuildDiagram(string? billName = null, string? shipName = null)
    {
        var customers = new Entity
        {
            TableName = "customers",
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
        var orders = new Entity
        {
            TableName = "orders",
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
                    Name = "bill_customer_id",
                    DataType = "int",
                    IsNullable = false,
                },
                new Column
                {
                    Name = "ship_customer_id",
                    DataType = "int",
                    IsNullable = false,
                },
            },
        };

        Relationship Link(int childColumn, string? name) =>
            new()
            {
                SourceEntityId = customers.Id,
                TargetEntityId = orders.Id,
                ConstraintName = name,
                ColumnPairs = [new(customers.Columns[0].Id, orders.Columns[childColumn].Id)],
            };

        return new ErDiagram
        {
            Entities = { customers, orders },
            Relationships = { Link(1, billName), Link(2, shipName) },
        };
    }

    [Fact(DisplayName = "名前の無い同じ組の外部キー 2 本に、DDL は別々の名前を付ける（5 方言）")]
    public void Ddl_SamePairTwice_GetsDistinctNames()
    {
        foreach (var provider in Providers)
        {
            var ddl = provider.DdlGenerator.Build(BuildDiagram());

            ddl.Should().Contain(ShipName, provider.Name);
            CountOf(ddl, BillName)
                .Should()
                .Be(2, $"{provider.Name}: 既定名は 1 本目だけ（2 本目の名前の接頭辞として 1 回）");
        }
    }

    [Fact(
        DisplayName = "名前の無い同じ組の外部キー 2 本を、同期スクリプトは DDL と同じ名前で付ける（5 方言）"
    )]
    public void Sync_SamePairTwice_UsesDdlNames()
    {
        foreach (var provider in Providers)
        {
            var diagram = BuildDiagram();
            var diff = new SchemaDiffService().Compute(
                [],
                [],
                diagram.Entities,
                diagram.Relationships,
                provider.SyncCapabilities
            );
            var plan = new SyncPlanner().BuildPlan(
                diff.Items,
                provider.SyncCapabilities,
                new SyncPlanContext()
            );

            var script = provider.SyncScriptBuilder.Build(plan);

            script.Should().Contain(ShipName, provider.Name);
            CountOf(script, BillName).Should().Be(2, provider.Name);
        }
    }

    [Fact(
        DisplayName = "名前の無い同じ組の外部キー 2 本の図は、DDL が付けた名前の live と外部キーの差分を出さない（名前を保存する 4 方言）"
    )]
    public void Diff_SamePairTwice_MatchesLiveNamedByDdl()
    {
        foreach (
            var provider in Providers.Where(p =>
                p.SyncCapabilities.PersistsForeignKeyConstraintNames
            )
        )
        {
            // live は DDL が付けた名前をそのまま持ち帰った図（取込の結果に当たる）
            var live = BuildDiagram(BillName, ShipName);
            var target = BuildDiagram();

            var diff = new SchemaDiffService().Compute(
                live.Entities,
                live.Relationships,
                target.Entities,
                target.Relationships,
                provider.SyncCapabilities
            );

            diff.Items.Should()
                .NotContain(
                    i =>
                        i.Kind == SchemaDiffKind.AddForeignKey
                        || i.Kind == SchemaDiffKind.DropForeignKey
                        || i.Kind == SchemaDiffKind.AlterForeignKey,
                    provider.Name
                );
        }
    }

    [Fact(
        DisplayName = "名前の無い 2 本目の参照アクションを変えると、同じ名前で外して付け直す外部キー変更になる（名前を保存する 4 方言）"
    )]
    public void Alter_SecondUnnamed_ReplacesUnderSameName()
    {
        foreach (
            var provider in Providers.Where(p =>
                p.SyncCapabilities.PersistsForeignKeyConstraintNames
            )
        )
        {
            var live = BuildDiagram(BillName, ShipName);
            var target = BuildDiagram();
            target.Relationships[1].OnDelete = ForeignKeyReferentialAction.Cascade;

            var diff = new SchemaDiffService().Compute(
                live.Entities,
                live.Relationships,
                target.Entities,
                target.Relationships,
                provider.SyncCapabilities
            );

            var alter = diff
                .Items.Should()
                .ContainSingle(
                    i =>
                        i.Kind == SchemaDiffKind.AddForeignKey
                        || i.Kind == SchemaDiffKind.DropForeignKey
                        || i.Kind == SchemaDiffKind.AlterForeignKey,
                    provider.Name
                )
                .Which;
            alter.Kind.Should().Be(SchemaDiffKind.AlterForeignKey, provider.Name);
            alter.IsSelected = true;

            var script = provider.SyncScriptBuilder.Build(
                new SyncPlanner().BuildPlan(
                    [alter],
                    provider.SyncCapabilities,
                    new SyncPlanContext
                    {
                        LiveEntities = live.Entities,
                        LiveRelationships = live.Relationships,
                    }
                )
            );

            // 外すのも付けるのも 2 本目の名前（既定名で付け直すと 1 本目と同じ名前になる）
            CountOf(script, ShipName).Should().BeGreaterThanOrEqualTo(2, provider.Name);
            CountOf(script, BillName).Should().Be(CountOf(script, ShipName), provider.Name);
        }
    }

    /// <summary>
    /// <see cref="BuildDiagram"/> の先頭に、同じ組の名前の無い多対多を足した図
    /// （DDL はこの関係に外部キーを作らないので、既定名を取らせてはいけない）
    /// </summary>
    private static ErDiagram BuildDiagramWithManyToManyFirst()
    {
        var diagram = BuildDiagram();
        diagram.Relationships.Insert(
            0,
            new Relationship
            {
                SourceEntityId = diagram.Entities[0].Id,
                TargetEntityId = diagram.Entities[1].Id,
                Type = RelationshipType.ManyToMany,
            }
        );

        return diagram;
    }

    [Fact(
        DisplayName = "名前の無い多対多が先に並んでも、同じ組の外部キーの名前は変わらない（DDL・5 方言）"
    )]
    public void Ddl_ManyToManyFirst_DoesNotShiftForeignKeyNames()
    {
        foreach (var provider in Providers)
        {
            var ddl = provider.DdlGenerator.Build(BuildDiagramWithManyToManyFirst());

            ddl.Should().NotContain("FK_orders_customers_bill_customer_id", provider.Name);
            ddl.Should().Contain(ShipName, provider.Name);
            CountOf(ddl, BillName).Should().Be(2, provider.Name);
        }
    }

    [Fact(
        DisplayName = "名前の無い多対多が先に並ぶ図は、既定名で外部キーを作った live と外部キーの差分を出さない（名前を保存する 4 方言）"
    )]
    public void Diff_ManyToManyFirst_MatchesLiveWithDefaultName()
    {
        foreach (
            var provider in Providers.Where(p =>
                p.SyncCapabilities.PersistsForeignKeyConstraintNames
            )
        )
        {
            var live = BuildDiagram(BillName, ShipName);
            var target = BuildDiagramWithManyToManyFirst();

            var diff = new SchemaDiffService().Compute(
                live.Entities,
                live.Relationships,
                target.Entities,
                target.Relationships,
                provider.SyncCapabilities
            );

            diff.Items.Should()
                .NotContain(
                    i =>
                        i.Kind == SchemaDiffKind.AddForeignKey
                        || i.Kind == SchemaDiffKind.DropForeignKey
                        || i.Kind == SchemaDiffKind.AlterForeignKey,
                    provider.Name
                );
        }
    }

    private static int CountOf(string text, string value)
    {
        var count = 0;

        for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; count++)
        {
            index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal);
        }

        return count;
    }
}
