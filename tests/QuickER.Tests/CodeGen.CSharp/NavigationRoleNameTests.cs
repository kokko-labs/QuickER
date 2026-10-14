using System.Reflection;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using QuickER.CodeGen.CSharp;
using QuickER.CodeReverse.CSharp;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.SqlServer;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// 同じ 2 表の間に複数のリレーションがある図のナビゲーション名（役割名）を検証する。
/// </summary>
/// <remarks>
/// <para>
/// ナビゲーション名を相手のテーブル名だけから作ると、2 本目のリレーションで同じ名前のメンバーが 2 つでき、
/// 生成がエラーで止まっていた（列名を変えても解消しない）。同じ（親表, 子表）の組に 2 本以上あるときだけ、
/// その組の全リレーションを外部キー列由来の役割名で区別する＝子側 <c>ShipCustomer</c>・
/// 親側 <c>OrdersByShipCustomer</c>（1 対 1 は <c>OrderByShipCustomer</c>）。
/// </para>
/// <para>
/// 名前は Entity / EditModel / Mapper と EF Core の Fluent 構成が同じ結果を読む必要がある。ここでは生成→
/// 実コンパイル→EF Core のモデル検証（<c>DbContext.Model</c>）→インメモリの Include まで実際に動かす。
/// 1 本だけの組の名前が変わらないことは、全フィクスチャの Drift が固定する。
/// </para>
/// </remarks>
public sealed class NavigationRoleNameTests
{
    private const string Namespace = "Test.Nav";

    /// <summary>customers を orders が 2 本（ship / bill）で参照する図</summary>
    private static ErDiagram BuildShipBillDiagram(RelationshipType type)
    {
        var customerId = Column("customer_id", "int", primaryKey: true);
        var customers = new Entity
        {
            TableName = "customers",
            Columns = { customerId, Column("name", "nvarchar(50)") },
        };

        var shipCustomerId = Column("ship_customer_id", "int", nullable: true);
        var billCustomerId = Column("bill_customer_id", "int", nullable: true);
        var orders = new Entity
        {
            TableName = "orders",
            Columns =
            {
                Column("order_id", "int", primaryKey: true),
                shipCustomerId,
                billCustomerId,
            },
        };

        return new ErDiagram
        {
            Entities = { customers, orders },
            Relationships =
            {
                Reference(customers, customerId, orders, shipCustomerId, type),
                Reference(customers, customerId, orders, billCustomerId, type),
            },
        };
    }

    /// <summary>nodes が自分自身を 2 本（parent / origin）で参照する図</summary>
    private static ErDiagram BuildSelfReferenceDiagram()
    {
        var nodeId = Column("node_id", "int", primaryKey: true);
        var parentId = Column("parent_id", "int", nullable: true);
        var originId = Column("origin_id", "int", nullable: true);
        var nodes = new Entity { TableName = "nodes", Columns = { nodeId, parentId, originId } };

        return new ErDiagram
        {
            Entities = { nodes },
            Relationships =
            {
                Reference(nodes, nodeId, nodes, parentId, RelationshipType.OneToMany),
                Reference(nodes, nodeId, nodes, originId, RelationshipType.OneToMany),
            },
        };
    }

    private static Column Column(
        string name,
        string dataType,
        bool primaryKey = false,
        bool nullable = false
    ) =>
        new()
        {
            Name = name,
            DataType = dataType,
            IsPrimaryKey = primaryKey,
            IsForeignKey = !primaryKey && name.EndsWith("_id", StringComparison.Ordinal),
            IsNullable = nullable,
        };

    private static Relationship Reference(
        Entity parent,
        Column parentColumn,
        Entity child,
        Column childColumn,
        RelationshipType type
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            Type = type,
            SourceEntityId = parent.Id,
            TargetEntityId = child.Id,
            ColumnPairs = { new(parentColumn.Id, childColumn.Id) },
        };

    /// <summary>QuickER 版 Repository・EF Core・インメモリ・EditModel・Mapper を全部出す構成で生成する</summary>
    private static CodeGenerationResult Generate(ErDiagram diagram)
    {
        var provider = new SqlServerProvider();
        var result = DiagramCodeGenerator.Generate(
            provider.TypeMapper,
            provider.TypeCatalog,
            diagram,
            new CodeGenerationOptions
            {
                RootNamespace = Namespace,
                GenerateRepositories = true,
                GenerateEfCoreRepositories = true,
                GenerateInMemoryRepositories = true,
                IncludeDataAnnotations = true,
            }
        );

        result
            .HasErrors.Should()
            .BeFalse(string.Join(" / ", result.Diagnostics.Select(d => d.Message)));

        return result;
    }

    [Fact(
        DisplayName = "同じ 2 表の間の 2 本のリレーション（1 対多）は役割名になり、EF Core のモデル検証とインメモリの Include が通る"
    )]
    public void TwoRelationshipsBetweenSameTables_UseRoleNames()
    {
        var result = Generate(BuildShipBillDiagram(RelationshipType.OneToMany));
        var content = string.Join("\n", result.Files.Select(file => file.Content));

        content.Should().Contain("public CustomerEntity? ShipCustomer { get; set; }");
        content.Should().Contain("public CustomerEntity? BillCustomer { get; set; }");
        content
            .Should()
            .Contain("public ICollection<OrderEntity> OrdersByShipCustomer { get; set; }");
        content
            .Should()
            .Contain("public ICollection<OrderEntity> OrdersByBillCustomer { get; set; }");

        // EF Core の Fluent 構成も同じ名前で両端を結ぶ（違う名前だと別のリレーションとして組まれる）
        content.Should().Contain("HasMany(e => e.OrdersByShipCustomer)");
        content.Should().Contain("WithOne(e => e.ShipCustomer)");
        content.Should().Contain("HasMany(e => e.OrdersByBillCustomer)");
        content.Should().Contain("WithOne(e => e.BillCustomer)");

        var probe = $$"""
            using System.Linq;

            namespace {{Namespace}};

            internal static class NavigationProbe
            {
                public static string Run()
                {
                    var store = new InMemoryDataStore();
                    store.Put(new CustomerEntity { CustomerId = 1, Name = "Ship" });
                    store.Put(new CustomerEntity { CustomerId = 2, Name = "Bill" });
                    store.Put(new OrderEntity { OrderId = 10, ShipCustomerId = 1, BillCustomerId = 2 });

                    var order = new InMemoryOrderRepository(store)
                        .Query()
                        .Include(o => o.ShipCustomer)
                        .Include(o => o.BillCustomer)
                        .FirstOrDefaultAsync()
                        .GetAwaiter()
                        .GetResult()!;

                    var customers = new InMemoryCustomerRepository(store)
                        .Query()
                        .Include(c => c.OrdersByShipCustomer)
                        .Include(c => c.OrdersByBillCustomer)
                        .ToListAsync()
                        .GetAwaiter()
                        .GetResult();
                    var ship = customers.Single(c => c.CustomerId == 1);

                    return $"{order.ShipCustomer!.Name}/{order.BillCustomer!.Name}/"
                        + $"{ship.OrdersByShipCustomer.Count}/{ship.OrdersByBillCustomer.Count}";
                }
            }
            """;

        var assembly = GeneratedCodeCompiler.CompileAndLoad(
            result,
            $"NavigationRoleName.{Guid.NewGuid():N}",
            probe
        );

        // 2 本のナビゲーションがそれぞれ別の行を指す（片方の外部キーで両方が組まれていない）
        assembly
            .GetType($"{Namespace}.NavigationProbe", throwOnError: true)!
            .GetMethod("Run")!
            .Invoke(null, null)
            .Should()
            .Be("Ship/Bill/1/0");

        using var context = CreateContext(assembly);
        var orderType = context.Model.FindEntityType($"{Namespace}.OrderEntity")!;
        orderType
            .GetNavigations()
            .Select(n => n.Name)
            .Should()
            .BeEquivalentTo("ShipCustomer", "BillCustomer");
        orderType.GetForeignKeys().Should().HaveCount(2);
    }

    [Fact(
        DisplayName = "同じ 2 表の間の 2 本のリレーション（1 対 1）は親側が子の単数形＋By＋役割名になる"
    )]
    public void TwoOneToOneRelationshipsBetweenSameTables_UseSingularRoleNames()
    {
        var result = Generate(BuildShipBillDiagram(RelationshipType.OneToOne));
        var content = string.Join("\n", result.Files.Select(file => file.Content));

        content.Should().Contain("public OrderEntity? OrderByShipCustomer { get; set; }");
        content.Should().Contain("public OrderEntity? OrderByBillCustomer { get; set; }");
        content.Should().Contain("public CustomerEntity? ShipCustomer { get; set; }");

        var compilation = GeneratedCodeCompiler.Compile(result, "NavigationRoleNameOneToOne");
        compilation.Success.Should().BeTrue(compilation.DescribeErrors());

        using var context = CreateContext(
            GeneratedCodeCompiler.CompileAndLoad(result, $"NavigationRoleName.{Guid.NewGuid():N}")
        );
        context
            .Model.FindEntityType($"{Namespace}.OrderEntity")!
            .GetForeignKeys()
            .Should()
            .HaveCount(2);
    }

    [Fact(
        DisplayName = "自分自身を 2 本で参照する表は親側のコレクションが役割名で分かれ、EF Core のモデル検証が通る"
    )]
    public void TwoSelfReferences_UseRoleNames()
    {
        var result = Generate(BuildSelfReferenceDiagram());
        var content = string.Join("\n", result.Files.Select(file => file.Content));

        content.Should().Contain("public ICollection<NodeEntity> NodesByParent { get; set; }");
        content.Should().Contain("public ICollection<NodeEntity> NodesByOrigin { get; set; }");

        using var context = CreateContext(
            GeneratedCodeCompiler.CompileAndLoad(result, $"NavigationRoleName.{Guid.NewGuid():N}")
        );
        context
            .Model.FindEntityType($"{Namespace}.NodeEntity")!
            .GetForeignKeys()
            .Should()
            .HaveCount(2);
    }

    [Theory(
        DisplayName = "役割名は外部キー列の末尾の Id を除き、末尾が Id でない列は列名＋親の単数形にする"
    )]
    [InlineData("ship_customer_id", "ShipCustomer")]
    [InlineData("ShipCustomerID", "ShipCustomer")]
    [InlineData("shipper", "ShipperCustomer")]
    [InlineData("id", "IdCustomer")]
    public void RoleName_FollowsForeignKeyColumn(string foreignKeyColumn, string expectedRole)
    {
        var customerId = Column("customer_id", "int", primaryKey: true);
        var customers = new Entity { TableName = "customers", Columns = { customerId } };
        var first = Column(foreignKeyColumn, "int", nullable: true);
        var second = Column("other_customer_id", "int", nullable: true);
        var orders = new Entity
        {
            TableName = "orders",
            Columns = { Column("order_key", "int", primaryKey: true), first, second },
        };
        var diagram = new ErDiagram
        {
            Entities = { customers, orders },
            Relationships =
            {
                Reference(customers, customerId, orders, first, RelationshipType.OneToMany),
                Reference(customers, customerId, orders, second, RelationshipType.OneToMany),
            },
        };

        var content = string.Join("\n", Generate(diagram).Files.Select(file => file.Content));

        content.Should().Contain($"public CustomerEntity? {expectedRole} {{ get; set; }}");
        content.Should().Contain($"OrdersBy{expectedRole} {{ get; set; }}");
        content.Should().Contain("public CustomerEntity? OtherCustomer { get; set; }");
    }

    [Fact(
        DisplayName = "役割名のナビゲーションを持つ生成コードは C# リバースで 2 本のリレーションへ戻る"
    )]
    public void RoleNamedNavigations_RoundTripThroughReverse()
    {
        // リバースはナビゲーションの名前でなく [NavigationReference] の端点（表名・列名）から関係を復元する
        var source = string.Join(
            "\n",
            Generate(BuildShipBillDiagram(RelationshipType.OneToMany)).Files.Select(f => f.Content)
        );

        var reversed = new CSharpReverseParser().Parse(
            source,
            new SqlServerTypeCatalog(),
            IdentifierLengthLimit.Unlimited
        );

        var orders = reversed.Entities.Single(entity => entity.TableName == "orders");
        reversed
            .Relationships.Select(relationship =>
                orders
                    .Columns.Single(column =>
                        column.Id == relationship.ColumnPairs.Single().TargetColumnId
                    )
                    .Name
            )
            .Should()
            .BeEquivalentTo("ship_customer_id", "bill_customer_id");
    }

    /// <summary>生成アセンブリから QuickErDbContext を Sqlite 構成で生成する（実接続はしない）</summary>
    private static DbContext CreateContext(Assembly assembly)
    {
        var contextType =
            assembly.GetTypes().SingleOrDefault(type => type.Name == "QuickErDbContext")
            ?? throw new InvalidOperationException("QuickErDbContext が生成されていない");

        var builder = (DbContextOptionsBuilder)
            Activator.CreateInstance(
                typeof(DbContextOptionsBuilder<>).MakeGenericType(contextType)
            )!;
        builder.UseSqlite("Data Source=:memory:");

        return (DbContext)Activator.CreateInstance(contextType, builder.Options)!;
    }
}
