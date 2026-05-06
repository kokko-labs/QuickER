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
              "tables": [
                {
                  "name": "Customer",
                  "description": "顧客マスタを管理するテーブル",
                  "memo": "",
                  "columns": [
                    { "name": "Id", "dataType": "int", "isPrimaryKey": true, "isForeignKey": false, "isNullable": false, "description": "顧客を一意に識別するID" },
                    { "name": "Name", "dataType": "nvarchar(50)", "isPrimaryKey": false, "isForeignKey": false, "isNullable": true, "description": "顧客名" }
                  ]
                },

                {
                  "name": "Order",
                  "description": "注文データを管理するテーブル",
                  "memo": "",
                  "columns": [
                    { "name": "Id", "dataType": "int", "isPrimaryKey": true, "isForeignKey": false, "isNullable": false, "description": "注文ID" },
                    { "name": "CustomerId", "dataType": "int", "isPrimaryKey": false, "isForeignKey": true, "isNullable": false, "description": "注文者の顧客ID" }
                  ]
                }
              ],
              "relationships": [
                { "sourceTable": "Customer", "targetTable": "Order", "type": "OneToMany" }
              ]
            }

            """;

        var parsed = JsonSerializer.Deserialize<AiSchemaJson>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var (entities, rels) = parsed.ToDomain();

        entities.Should().HaveCount(2);
        entities[0].TableName.Should().Be("Customer");
        entities[0].Description.Should().Be("顧客マスタを管理するテーブル");
        entities[0].Columns.Should().HaveCount(2);
        entities[0].Columns[0].IsPrimaryKey.Should().BeTrue();
        entities[0].Columns[0].IsNullable.Should().BeFalse();
        entities[0].Columns[0].Description.Should().Be("顧客を一意に識別するID");
        entities[1].Columns[1].IsForeignKey.Should().BeTrue();
        entities[1].Columns[1].IsNullable.Should().BeFalse();

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
            Tables =
            {
                new AiTable
                {
                    Name = "A",
                    Columns = new()
                    {
                        new AiColumn
                        {
                            Name = "Id",
                            DataType = "int",
                            IsPrimaryKey = true,
                        },
                    },
                },
            },

            Relationships =
            {
                new AiRelationship
                {
                    SourceTable = "A",
                    TargetTable = "Missing",
                    Type = "OneToMany",
                },
            },
        };

        var (_, rels) = schema.ToDomain();
        rels.Should().BeEmpty();
    }

    [Fact(DisplayName = "旧形式の tableName / columnName だけではテーブルに変換されない")]
    public void Deserialize_LegacyTableAndColumnNames_AreIgnored()
    {
        var json = """
            {
              "tables": [
                {
                  "tableName": "customers",
                  "tableDescription": "顧客情報を保持するテーブル",
                  "columns": [
                    { "columnName": "customer_id", "dataType": "int", "isPrimaryKey": true, "isForeignKey": false, "columnDescription": "顧客ID" }
                  ]
                },

                {
                  "tableName": "orders",
                  "tableDescription": "注文情報を保持するテーブル",
                  "columns": [
                    { "columnName": "order_id", "dataType": "int", "isPrimaryKey": true, "isForeignKey": false, "columnDescription": "注文ID" },
                    { "columnName": "customer_id", "dataType": "int", "isPrimaryKey": false, "isForeignKey": true, "columnDescription": "顧客ID" }
                  ]
                }
              ],
              "relationships": [
                { "fromTable": "customers", "toTable": "orders", "type": "OneToMany" }
              ]
            }

            """;

        var parsed = JsonSerializer.Deserialize<AiSchemaJson>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var (entities, rels) = parsed.ToDomain();

        entities.Should().BeEmpty();
        rels.Should().BeEmpty();
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
            Tables =
            {
                new AiTable
                {
                    Name = "A",
                    Columns = new()
                    {
                        new AiColumn { Name = "Id", DataType = "int" },
                    },
                },
                new AiTable
                {
                    Name = "B",
                    Columns = new()
                    {
                        new AiColumn { Name = "Id", DataType = "int" },
                    },
                },
            },

            Relationships =
            {
                new AiRelationship
                {
                    SourceTable = "A",
                    TargetTable = "B",
                    Type = typeStr,
                },
            },
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
                  "name": "products",
                  "description": "商品マスタ",
                  "columns": [
                    { "name": "product_id", "dataType": "int", "isPrimaryKey": true, "isForeignKey": false, "description": "商品ID" }
                  ]
                }
              ],
              "relationships": []
            }

            ```
            """;

        var parsed = OpenAiSchemaClient.ParseSchemaResponse(response);

        parsed.Tables.Should().ContainSingle();
        parsed.Tables[0].Name.Should().Be("products");
        parsed.Tables[0].Description.Should().Be("商品マスタ");
        parsed.Tables[0].Columns.Should().ContainSingle();
        parsed.Tables[0].Columns![0].Name.Should().Be("product_id");
        parsed.Tables[0].Columns![0].Description.Should().Be("商品ID");
    }

    [Fact(DisplayName = "識別子をパスカルケースへ正規化できる")]
    public void NormalizeIdentifiers_ConvertsToPascalCase()
    {
        var schema = new AiSchemaJson
        {
            Tables =
            [
                new AiTable
                {
                    Name = "customer_order",
                    Columns = [new AiColumn { Name = "order_id", DataType = "int" }, new AiColumn { Name = "created_at", DataType = "datetime2" }],
                },
            ],
            Relationships =
            [
                new AiRelationship
                {
                    SourceTable = "customer_order",
                    TargetTable = "customer_order",
                    Type = "OneToOne",
                },
            ],
        };

        schema.NormalizeIdentifiers(AiIdentifierNamingStyle.PascalCase);

        schema.Tables[0].Name.Should().Be("CustomerOrder");
        schema.Tables[0].Columns![0].Name.Should().Be("OrderId");
        schema.Tables[0].Columns![1].Name.Should().Be("CreatedAt");
        schema.Relationships[0].SourceTable.Should().Be("CustomerOrder");
        schema.Relationships[0].TargetTable.Should().Be("CustomerOrder");
    }

    [Fact(DisplayName = "識別子をスネークケースへ正規化できる")]
    public void NormalizeIdentifiers_ConvertsToSnakeCase()
    {
        var schema = new AiSchemaJson
        {
            Tables =
            [
                new AiTable
                {
                    Name = "CustomerOrder",
                    Columns = [new AiColumn { Name = "OrderId", DataType = "int" }, new AiColumn { Name = "CreatedAt", DataType = "datetime2" }],
                },
            ],
            Relationships =
            [
                new AiRelationship
                {
                    SourceTable = "CustomerOrder",
                    TargetTable = "CustomerOrder",
                    Type = "OneToOne",
                },
            ],
        };

        schema.NormalizeIdentifiers(AiIdentifierNamingStyle.SnakeCase);

        schema.Tables[0].Name.Should().Be("customer_order");
        schema.Tables[0].Columns![0].Name.Should().Be("order_id");
        schema.Tables[0].Columns![1].Name.Should().Be("created_at");
        schema.Relationships[0].SourceTable.Should().Be("customer_order");
        schema.Relationships[0].TargetTable.Should().Be("customer_order");
    }

    [Theory(DisplayName = "NULL 許容の互換プロパティも isNullable に反映される")]
    [InlineData("nullable", true, true)]
    [InlineData("nullable", false, false)]
    [InlineData("allowNull", true, true)]
    [InlineData("allowNull", false, false)]
    [InlineData("required", true, false)]
    [InlineData("required", false, true)]
    public void Deserialize_NullabilityAliases_AreApplied(string propertyName, bool propertyValue, bool expectedNullable)
    {
        var json = $$"""
            {
              "tables": [
                {
                  "name": "Customer",
                  "description": "顧客",
                  "memo": "",
                  "columns": [
                    {
                      "name": "Email",
                      "dataType": "nvarchar(256)",
                      "isPrimaryKey": false,
                      "isForeignKey": false,
                      "{{propertyName}}": {{propertyValue.ToString().ToLowerInvariant()}},
                      "description": "メールアドレス"
                    }
                  ]
                }
              ],
              "relationships": []
            }
            """;

        var parsed = JsonSerializer.Deserialize<AiSchemaJson>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        parsed.Tables[0].Columns![0].IsNullable.Should().Be(expectedNullable);
    }
}
