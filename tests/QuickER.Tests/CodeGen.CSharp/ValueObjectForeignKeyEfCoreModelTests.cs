using System.Reflection;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider;
using QuickER.SqlServer;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// 「FK 列名 ≠ 参照先 PK 列名」× 値オブジェクト × EF Core の図で、生成 <c>QuickErDbContext</c> の
/// <b>モデル検証が通る</b>ことを実際にモデルを組み立てて確認するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// EF Core のモデル検証は FK プロパティの CLR 型が主キーの CLR 型と互換であることを要求し、値コンバータは
/// 判定に関与しない。VO を列名ごとに別型で作ると、この図では <c>DbContext</c> が丸ごと使用不能になる
/// （<c>cannot target the primary key … because it is not compatible</c>）。Fluent の書き方では直せないため、
/// 「子側の列は親側の VO 型を共有する」という型解決の統一そのものがこの検証の対象になる。
/// </para>
/// <para>
/// 生成テキストの表明（<see cref="ValueObjectForeignKeyUnificationTests"/>）ではモデル検証まで届かないため、
/// ここでは生成→Roslyn コンパイル→アセンブリロード→<c>DbContext.Model</c> の実アクセスまで踏み込む
/// （実 DB は不要。プロバイダは Sqlite をモデル構築のためだけに構成する）。
/// </para>
/// </remarks>
public class ValueObjectForeignKeyEfCoreModelTests
{
    /// <summary>自己参照・FK 列名不一致の両方を含む図（VO 有効＋EF Core 生成）</summary>
    private static ErDiagram BuildDiagram()
    {
        var customerId = new Column
        {
            Name = "customer_id",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var customers = new Entity { TableName = "customers", Columns = { customerId } };

        var orderId = new Column
        {
            Name = "order_id",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var shipCustomerId = new Column
        {
            Name = "ship_customer_id",
            DataType = "int",
            IsForeignKey = true,
            IsNullable = true,
        };
        var orders = new Entity { TableName = "orders", Columns = { orderId, shipCustomerId } };

        var nodeId = new Column
        {
            Name = "node_id",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var parentNodeId = new Column
        {
            Name = "parent_node_id",
            DataType = "int",
            IsForeignKey = true,
            IsNullable = true,
        };
        var nodes = new Entity { TableName = "nodes", Columns = { nodeId, parentNodeId } };

        return new ErDiagram
        {
            Entities = { customers, orders, nodes },
            Relationships =
            {
                new Relationship
                {
                    Id = Guid.NewGuid(),
                    Type = RelationshipType.OneToMany,
                    SourceEntityId = customers.Id,
                    TargetEntityId = orders.Id,
                    ColumnPairs = { new(customerId.Id, shipCustomerId.Id) },
                },
                new Relationship
                {
                    Id = Guid.NewGuid(),
                    Type = RelationshipType.OneToMany,
                    SourceEntityId = nodes.Id,
                    TargetEntityId = nodes.Id,
                    ColumnPairs = { new(nodeId.Id, parentNodeId.Id) },
                },
            },
        };
    }

    /// <summary>生成 DbContext のモデル検証が通る（FK 列名不一致・自己参照とも）</summary>
    [Fact(DisplayName = "FK 列名 ≠ PK 列名 × VO × EF Core: 生成 DbContext のモデル検証が通る")]
    public void 生成DbContextのモデル検証が通る()
    {
        var provider = new SqlServerProvider();
        var options = new CodeGenerationOptions
        {
            RootNamespace = "Test.EfCoreModel",
            GenerateValueObjects = true,
            GenerateEfCore = true,
            // EF Core（DbContext・Fluent）だけを対象にし、ADO 実装は生成しない
            GenerateRepositories = false,
            GenerateEditModels = false,
            GenerateMappers = false,
        };

        var result = DiagramCodeGenerator.Generate(
            provider.TypeMapper,
            provider.TypeCatalog,
            BuildDiagram(),
            options
        );

        result.HasErrors.Should().BeFalse();

        var assembly = GeneratedCodeCompiler.CompileAndLoad(
            result,
            $"QuickER.EfCoreModel.Tests.{Guid.NewGuid():N}"
        );

        using var context = CreateContext(assembly);

        // Model へのアクセスでモデル構築＋検証が走る（不整合な FK はここで InvalidOperationException）
        var model = context.Model;

        model.FindEntityType("Test.EfCoreModel.OrderEntity").Should().NotBeNull();
        model.FindEntityType("Test.EfCoreModel.NodeEntity").Should().NotBeNull();
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
