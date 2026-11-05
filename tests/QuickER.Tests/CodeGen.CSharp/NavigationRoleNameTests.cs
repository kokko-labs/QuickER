using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.CodeReverse.CSharp;
using QuickER.Dialects;
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
/// 名前は Entity / EditModel / Mapper と EF Core の Fluent 構成が同じ結果を読む必要がある。名前の宣言・EF Core のモデル検証（外部キーとナビゲーションの対）・
/// インメモリの Include は、決まった行（<c>CodegenCaseTests</c> の <c>exec-navigation-role-names-*</c>・<c>code-navigation-role-name-*</c>）が持つ。ここに残るのは C# リバースの往復。
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
}
