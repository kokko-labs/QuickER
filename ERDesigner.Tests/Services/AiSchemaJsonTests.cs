using System.Text.Json;
using ERDesigner.Models;
using ERDesigner.Services;
using FluentAssertions;

namespace ERDesigner.Tests.Services;

/// <summary>
/// <see cref="AiSchemaJson"/> の JSON 解析と <see cref="AiSchemaJson.ToDomain"/> 変換のテスト。
/// </summary>
public class AiSchemaJsonTests
{
    [Fact(DisplayName = "JSON から Entity / Relationship に変換できる")]
    public void ToDomain_ConvertsCorrectly()
    {
        var json = """
        {
          "entities": [
            {
              "displayName": "顧客",
              "tableName": "Customer",
              "memo": "",
              "columns": [
                { "name": "Id", "dataType": "int", "isPrimaryKey": true, "isForeignKey": false },
                { "name": "Name", "dataType": "nvarchar(50)", "isPrimaryKey": false, "isForeignKey": false }
              ]
            },
            {
              "displayName": "注文",
              "tableName": "Order",
              "memo": "",
              "columns": [
                { "name": "Id", "dataType": "int", "isPrimaryKey": true, "isForeignKey": false },
                { "name": "CustomerId", "dataType": "int", "isPrimaryKey": false, "isForeignKey": true }
              ]
            }
          ],
          "relationships": [
            { "sourceTable": "Customer", "targetTable": "Order", "type": "OneToMany" }
          ]
        }
        """;

        var parsed = JsonSerializer.Deserialize<AiSchemaJson>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var (entities, rels) = parsed.ToDomain();

        entities.Should().HaveCount(2);
        entities[0].TableName.Should().Be("Customer");
        entities[0].Columns.Should().HaveCount(2);
        entities[0].Columns[0].IsPrimaryKey.Should().BeTrue();
        entities[1].Columns[1].IsForeignKey.Should().BeTrue();

        rels.Should().HaveCount(1);
        rels[0].Type.Should().Be(RelationshipType.OneToMany);
        rels[0].SourceEntityId.Should().Be(entities[0].Id);
        rels[0].TargetEntityId.Should().Be(entities[1].Id);
    }

    [Fact(DisplayName = "存在しないテーブルを参照するリレーションは無視される")]
    public void ToDomain_IgnoresInvalidRelationships()
    {
        var schema = new AiSchemaJson
        {
            Entities =
            {
                new AiEntity { TableName = "A", Columns = new() { new AiColumn { Name = "Id", DataType = "int", IsPrimaryKey = true } } }
            },
            Relationships =
            {
                new AiRelationship { SourceTable = "A", TargetTable = "Missing", Type = "OneToMany" }
            }
        };

        var (_, rels) = schema.ToDomain();
        rels.Should().BeEmpty();
    }

    [Fact(DisplayName = "tables / columnName / fromTable / toTable 形式の JSON も解析できる")]
    public void Deserialize_SupportsAlternatePropertyNames()
    {
        var json = """
        {
          "tables": [
            {
              "tableName": "customers",
              "columns": [
                { "columnName": "customer_id", "dataType": "int", "isPrimaryKey": true, "isForeignKey": false }
              ]
            },
            {
              "tableName": "orders",
              "columns": [
                { "columnName": "order_id", "dataType": "int", "isPrimaryKey": true, "isForeignKey": false },
                { "columnName": "customer_id", "dataType": "int", "isPrimaryKey": false, "isForeignKey": true }
              ]
            }
          ],
          "relationships": [
            { "fromTable": "customers", "toTable": "orders", "type": "OneToMany" }
          ]
        }
        """;

        var parsed = JsonSerializer.Deserialize<AiSchemaJson>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var (entities, rels) = parsed.ToDomain();

        entities.Should().HaveCount(2);
        entities[0].TableName.Should().Be("customers");
        entities[0].Columns.Should().ContainSingle();
        entities[0].Columns[0].Name.Should().Be("customer_id");
        rels.Should().ContainSingle();
        rels[0].Type.Should().Be(RelationshipType.OneToMany);
    }

    [Theory(DisplayName = "type 文字列が正しい RelationshipType に変換される")]
    [InlineData("OneToOne", RelationshipType.OneToOne)]
    [InlineData("OneToMany", RelationshipType.OneToMany)]
    [InlineData("ManyToMany", RelationshipType.ManyToMany)]
    [InlineData("1:1", RelationshipType.OneToOne)]
    [InlineData("M:N", RelationshipType.ManyToMany)]
    [InlineData(null, RelationshipType.OneToMany)]
    public void ToDomain_ParsesType(string? typeStr, RelationshipType expected)
    {
        var schema = new AiSchemaJson
        {
            Entities =
            {
                new AiEntity { TableName = "A", Columns = new() { new AiColumn { Name = "Id", DataType = "int" } } },
                new AiEntity { TableName = "B", Columns = new() { new AiColumn { Name = "Id", DataType = "int" } } }
            },
            Relationships = { new AiRelationship { SourceTable = "A", TargetTable = "B", Type = typeStr } }
        };
        var (_, rels) = schema.ToDomain();
        rels.Should().ContainSingle().Which.Type.Should().Be(expected);
    }

    [Fact(DisplayName = "コードフェンス付き JSON 応答も解析できる")]
    public void ParseSchemaResponse_SupportsMarkdownCodeFence()
    {
        var response = """
        ```json
        {
          "tables": [
            {
              "tableName": "products",
              "columns": [
                { "columnName": "product_id", "dataType": "int", "isPrimaryKey": true, "isForeignKey": false }
              ]
            }
          ],
          "relationships": []
        }
        ```
        """;

        var parsed = OpenAiSchemaClient.ParseSchemaResponse(response);

        parsed.Entities.Should().ContainSingle();
        parsed.Entities[0].TableName.Should().Be("products");
        parsed.Entities[0].Columns.Should().ContainSingle();
        parsed.Entities[0].Columns![0].Name.Should().Be("product_id");
    }
}
