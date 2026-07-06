using FluentAssertions;
using QuickER.AI;
using QuickER.AI.Mock;
using QuickER.Model;

namespace QuickER.Tests.Services.Chat;

/// <summary><see cref="MockSchemaSerializer"/> の ER 図 → スキーマ記述テキスト直列化を検証するテストクラス</summary>
public class MockSchemaSerializerTests
{
    /// <summary>顧客・注文（FK あり）＋商品の 3 テーブルを持つ代表的な ER 図を構築する</summary>
    private static ErDiagram BuildSampleDiagram()
    {
        var customerPk = new Column
        {
            Name = "CustomerId",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
            Description = "顧客ID",
        };
        var customer = new Entity
        {
            TableName = "Customer",
            Description = "顧客",
            Columns =
            {
                customerPk,
                new Column
                {
                    Name = "Name",
                    DataType = "nvarchar(50)",
                    IsNullable = false,
                    Description = "顧客名",
                },
            },
        };

        var orderFk = new Column
        {
            Name = "CustomerId",
            DataType = "int",
            IsForeignKey = true,
            IsNullable = false,
            Description = "顧客ID",
        };
        var order = new Entity
        {
            TableName = "Order",
            Description = "注文",
            Columns =
            {
                new Column
                {
                    Name = "OrderId",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                orderFk,
            },
        };

        var product = new Entity
        {
            TableName = "Product",
            Columns =
            {
                new Column
                {
                    Name = "ProductId",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
            },
        };

        var diagram = new ErDiagram
        {
            Entities = { customer, order, product },
            Relationships =
            {
                new Relationship
                {
                    SourceEntityId = customer.Id,
                    TargetEntityId = order.Id,
                    SourceColumnId = customerPk.Id,
                    TargetColumnId = orderFk.Id,
                    Type = RelationshipType.OneToMany,
                },
            },
        };

        return diagram;
    }

    /// <summary>直列化結果にテーブル名・列・型・制約が含まれることを検証する</summary>
    [Fact(DisplayName = "直列化にテーブル名・列・型・制約が含まれる")]
    public void Serialize_ContainsTablesColumnsAndTypes()
    {
        var text = MockSchemaSerializer.Serialize(BuildSampleDiagram());

        text.Should().Contain("Customer");
        text.Should().Contain("Order");
        text.Should().Contain("Product");

        // 列名・型・表示名（Description 由来）
        text.Should().Contain("CustomerId");
        text.Should().Contain("nvarchar(50)");
        text.Should().Contain("顧客名");

        // 制約表記
        text.Should().Contain("主キー");
        text.Should().Contain("外部キー");
        text.Should().Contain("必須");
    }

    /// <summary>直列化結果にリレーション（親→子・参照列・多重度）が含まれることを検証する</summary>
    [Fact(DisplayName = "直列化にリレーション（親→子・参照列・多重度）が含まれる")]
    public void Serialize_ContainsRelationships()
    {
        var text = MockSchemaSerializer.Serialize(BuildSampleDiagram());

        text.Should().Contain("リレーション");
        text.Should().Contain("Customer.CustomerId → Order.CustomerId");
        text.Should().Contain("1 対 多");
    }

    /// <summary>空の ER 図でも例外なく「テーブル未定義」の記述を返すことを検証する</summary>
    [Fact(DisplayName = "空の ER 図はテーブル未定義の記述を返す")]
    public void Serialize_EmptyDiagram_DescribesNoTables()
    {
        var text = MockSchemaSerializer.Serialize(new ErDiagram());

        text.Should().Contain("テーブルは定義されていません");
    }
}
