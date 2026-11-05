using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.SqlServer;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// 親列が主キーでない FK（UNIQUE 列参照・複合主キーの一部参照）で、生成 <c>QuickErDbContext</c> の
/// FK が <b>親の主キーでなくその親列へ結合する</b>ことを実際にモデルを組み立てて確認するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// EF Core の既定は「FK は親の主キーへ結合」で、<c>HasPrincipalKey</c> を出さないと親列が主キーでない
/// リレーションは主キーへ黙って結合される（CLR 型が同じなら Include の結果が誤ったまま緑・違えば
/// モデル検証例外で EF Core 経路が丸ごと使用不能）。ADO 側は列ペアどおりに結合するため、
/// 出さないままだと実装先で結果が食い違う。
/// </para>
/// <para>
/// モデルの結合先（<c>fk.PrincipalKey.Properties</c>）と参照アクションの <c>DeleteBehavior</c> を実モデルで確かめるテストは、
/// <c>CodegenCaseTests</c> の行 <c>probe-efcore-model-principal-key-not-primary</c> / <c>probe-efcore-model-delete-behavior</c>
/// （探針 EfCoreModel。<c>CodegenCases.Execution.cs</c>）へ移した。ここには、親列が主キーそのものの外部キーへ
/// <c>HasPrincipalKey</c> を出さないこと（生成テキスト）を残す。
/// </para>
/// </remarks>
public class EfCorePrincipalKeyModelTests
{
    /// <summary>EF Core 単独出力のオプション（モデル構築の検証対象を EF Core に絞る）</summary>
    private static CodeGenerationOptions CreateOptions() =>
        new()
        {
            RootNamespace = "Test.EfCorePk",
            GenerateEfCoreRepositories = true,
            GenerateRepositories = false,
            GenerateEditModels = false,
            GenerateMappers = false,
        };

    /// <summary>親列が単一主キーそのものの FK には HasPrincipalKey を出さない（従来出力の不変）</summary>
    [Fact(DisplayName = "親列が主キーそのものの FK には HasPrincipalKey を出さない")]
    public void 親列が主キーのFKにはHasPrincipalKeyを出さない()
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
        var orderCustomerId = new Column
        {
            Name = "customer_id",
            DataType = "int",
            IsForeignKey = true,
            IsNullable = false,
        };
        var orders = new Entity { TableName = "orders", Columns = { orderId, orderCustomerId } };
        var diagram = new ErDiagram
        {
            Entities = { customers, orders },
            Relationships =
            {
                new Relationship
                {
                    Id = Guid.NewGuid(),
                    Type = RelationshipType.OneToMany,
                    SourceEntityId = customers.Id,
                    TargetEntityId = orders.Id,
                    ColumnPairs = { new(customerId.Id, orderCustomerId.Id) },
                },
            },
        };

        var provider = new SqlServerProvider();
        var result = DiagramCodeGenerator.Generate(
            provider.TypeMapper,
            provider.TypeCatalog,
            diagram,
            CreateOptions()
        );

        result.HasErrors.Should().BeFalse();
        string.Join("\n", result.Files.Select(f => f.Content))
            .Should()
            .NotContain("HasPrincipalKey");
    }
}
