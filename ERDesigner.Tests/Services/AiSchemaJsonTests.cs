using System.Text.Json;
using ERDesigner.Models;
using ERDesigner.Services;
using FluentAssertions;

namespace ERDesigner.Tests.Services;

/// <summary><see cref="AiSchemaJson"/> の JSON 解析・ドメイン変換・命名正規化・FK 推定を検証するテストクラス</summary>
public class AiSchemaJsonTests
{
    /// <summary>JSON が Entity / Relationship へ正しく変換されることを検証する</summary>
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
                { "sourceTable": "Customer", "targetTable": "Order", "type": "OneToMany", "constraintName": "FK_Order_Customer", "onDelete": "CASCADE", "onUpdate": "NO ACTION" }
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
        rels[0].ConstraintName.Should().Be("FK_Order_Customer");
        rels[0].OnDelete.Should().Be(ForeignKeyReferentialAction.Cascade);
        rels[0].OnUpdate.Should().Be(ForeignKeyReferentialAction.NoAction);
        rels[0].SourceColumnId.Should().Be(entities[0].Columns[0].Id);
        rels[0].TargetColumnId.Should().Be(entities[1].Columns[1].Id);
    }

    /// <summary>列名推定により参照元列・参照先列がリレーションへ設定されることを検証する</summary>
    [Fact(DisplayName = "リレーション列名の推定により参照元列と参照先列が設定される")]
    public void ToDomain_ResolvesRelationshipColumns()
    {
        var schema = new AiSchemaJson
        {
            Tables =
            [
                new AiTable
                {
                    Name = "Customer",
                    Columns =
                    [
                        new AiColumn
                        {
                            Name = "Id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                    ],
                },
                new AiTable
                {
                    Name = "Order",
                    Columns =
                    [
                        new AiColumn
                        {
                            Name = "Id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new AiColumn
                        {
                            Name = "CustomerId",
                            DataType = "int",
                            IsForeignKey = true,
                            IsNullable = false,
                        },
                    ],
                },
            ],
            Relationships =
            [
                new AiRelationship
                {
                    SourceTable = "Customer",
                    TargetTable = "Order",
                    Type = "OneToMany",
                    ConstraintName = "FK_Order_Customer",
                    OnDelete = "CASCADE",
                    OnUpdate = "NO ACTION",
                },
            ],
        };

        var (entities, relationships) = schema.ToDomain();
        var customer = entities.Single(entity => entity.TableName == "Customer");
        var order = entities.Single(entity => entity.TableName == "Order");
        var relationship = relationships.Single();

        relationship.SourceColumnId.Should().Be(customer.Columns.Single(column => column.IsPrimaryKey).Id);
        relationship.TargetColumnId.Should().Be(order.Columns.Single(column => column.Name == "CustomerId").Id);
    }

    /// <summary>AI が明示した sourceColumn / targetColumn が列名推定より優先され、列設定が書き換えられないことを検証する</summary>
    [Fact(DisplayName = "AI が明示したリレーション列はフラグを書き換えずそのまま採用される")]
    public void ToDomain_ExplicitRelationshipColumns_AreUsedWithoutRewriting()
    {
        // FK 列名が命名規則（CustomerId）と異なる OwnerId で、任意参照のため NULL 許容
        var schema = new AiSchemaJson
        {
            Tables =
            [
                new AiTable
                {
                    Name = "Customer",
                    Columns =
                    [
                        new AiColumn
                        {
                            Name = "Id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                    ],
                },
                new AiTable
                {
                    Name = "Order",
                    Columns =
                    [
                        new AiColumn
                        {
                            Name = "Id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new AiColumn
                        {
                            Name = "OwnerId",
                            DataType = "int",
                            IsForeignKey = true,
                            IsNullable = true,
                        },
                    ],
                },
            ],
            Relationships =
            [
                new AiRelationship
                {
                    SourceTable = "Customer",
                    SourceColumn = "Id",
                    TargetTable = "Order",
                    TargetColumn = "OwnerId",
                    Type = "OneToMany",
                },
            ],
        };

        var (entities, relationships) = schema.ToDomain();
        var customer = entities.Single(entity => entity.TableName == "Customer");
        var order = entities.Single(entity => entity.TableName == "Order");
        var ownerColumn = order.Columns.Single(column => column.Name == "OwnerId");
        var relationship = relationships.Single();

        relationship.SourceColumnId.Should().Be(customer.Columns[0].Id);
        relationship.TargetColumnId.Should().Be(ownerColumn.Id);

        // AI の出力した NULL 許容設定が NOT NULL へ書き換えられない
        ownerColumn.IsNullable.Should().BeTrue();
        ownerColumn.IsForeignKey.Should().BeTrue();
    }

    /// <summary>FK らしい列が見つからない場合に、無関係な列が FK 化されず列未割当となることを検証する</summary>
    [Fact(DisplayName = "FK らしい列が無い場合は参照先列が未割当となり無関係な列は書き換えられない")]
    public void ToDomain_NoLikelyForeignKeyColumn_LeavesTargetUnassigned()
    {
        var schema = new AiSchemaJson
        {
            Tables =
            [
                new AiTable
                {
                    Name = "Customer",
                    Columns =
                    [
                        new AiColumn
                        {
                            Name = "Id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                    ],
                },
                new AiTable
                {
                    Name = "Order",
                    Columns =
                    [
                        new AiColumn
                        {
                            Name = "Id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new AiColumn
                        {
                            // FK とは無関係の列（従来はこの列がフォールバックで FK 化されていた）
                            Name = "Quantity",
                            DataType = "int",
                            IsNullable = true,
                        },
                    ],
                },
            ],
            Relationships =
            [
                new AiRelationship
                {
                    SourceTable = "Customer",
                    TargetTable = "Order",
                    Type = "OneToMany",
                },
            ],
        };

        var (entities, relationships) = schema.ToDomain();
        var order = entities.Single(entity => entity.TableName == "Order");
        var quantityColumn = order.Columns.Single(column => column.Name == "Quantity");
        var relationship = relationships.Single();

        relationship.TargetColumnId.Should().BeNull();
        quantityColumn.IsForeignKey.Should().BeFalse();
        quantityColumn.IsNullable.Should().BeTrue();
    }

    /// <summary>存在しないテーブルを参照するリレーションが無視されることを検証する</summary>
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

    /// <summary>旧形式の tableName / columnName のみでは有効なテーブルへ変換されないことを検証する</summary>
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

    /// <summary>type 文字列が対応する RelationshipType へ変換されることを検証する</summary>
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

    /// <summary>コードフェンスで囲まれた JSON 応答も解析できることを検証する</summary>
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

    /// <summary>識別子がパスカルケースへ正規化されることを検証する</summary>
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
                    SourceColumn = "order_id",
                    TargetTable = "customer_order",
                    TargetColumn = "parent_order_id",
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

        // リレーションの列参照もカラム名の変換に追従する
        schema.Relationships[0].SourceColumn.Should().Be("OrderId");
        schema.Relationships[0].TargetColumn.Should().Be("ParentOrderId");
    }

    /// <summary>識別子がスネークケースへ正規化されることを検証する</summary>
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

    /// <summary>テーブル名が単数形へ正規化されることを検証する</summary>
    [Fact(DisplayName = "テーブル名を単数形へ正規化できる")]
    public void NormalizeTableNames_ConvertsToSingular()
    {
        var schema = new AiSchemaJson
        {
            Tables = [new AiTable { Name = "customers" }, new AiTable { Name = "order_items" }],
            Relationships =
            [
                new AiRelationship
                {
                    SourceTable = "customers",
                    TargetTable = "order_items",
                    Type = "OneToMany",
                },
            ],
        };

        schema.NormalizeTableNames(AiTableNameNumberStyle.Singular);
        schema.NormalizeIdentifiers(AiIdentifierNamingStyle.SnakeCase);

        schema.Tables[0].Name.Should().Be("customer");
        schema.Tables[1].Name.Should().Be("order_item");
        schema.Relationships[0].SourceTable.Should().Be("customer");
        schema.Relationships[0].TargetTable.Should().Be("order_item");
    }

    /// <summary>テーブル名が複数形へ正規化されることを検証する</summary>
    [Fact(DisplayName = "テーブル名を複数形へ正規化できる")]
    public void NormalizeTableNames_ConvertsToPlural()
    {
        var schema = new AiSchemaJson
        {
            Tables = [new AiTable { Name = "Customer" }, new AiTable { Name = "Category" }],
            Relationships =
            [
                new AiRelationship
                {
                    SourceTable = "Customer",
                    TargetTable = "Category",
                    Type = "OneToMany",
                },
            ],
        };

        schema.NormalizeTableNames(AiTableNameNumberStyle.Plural);
        schema.NormalizeIdentifiers(AiIdentifierNamingStyle.PascalCase);

        schema.Tables[0].Name.Should().Be("Customers");
        schema.Tables[1].Name.Should().Be("Categories");
        schema.Relationships[0].SourceTable.Should().Be("Customers");
        schema.Relationships[0].TargetTable.Should().Be("Categories");
    }

    /// <summary>NULL 許容の互換プロパティ名でも isNullable へ反映されることを検証する</summary>
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

    /// <summary>FK 列が誤って主キー指定された場合、他に PK があれば主キーを外し FK へ矯正されることを検証する</summary>
    [Fact(DisplayName = "FK 列が誤って isPrimaryKey=true で返された場合、他に PK があれば PK を降ろして FK に矯正される")]
    public void ToDomain_PreferredColumnWithWrongPkFlag_IsCorrectedToFk()
    {
        // AI が CustomerId を誤って isPrimaryKey=true で返したケース
        var schema = new AiSchemaJson
        {
            Tables =
            [
                new AiTable
                {
                    Name = "Customer",
                    Columns =
                    [
                        new AiColumn
                        {
                            Name = "CustomerId",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                    ],
                },
                new AiTable
                {
                    Name = "Order",
                    Columns =
                    [
                        new AiColumn
                        {
                            Name = "OrderId",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new AiColumn
                        {
                            // AI が誤って isPrimaryKey=true を付けたが他に PK があるので矯正される
                            Name = "CustomerId",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsForeignKey = false,
                            IsNullable = false,
                        },
                    ],
                },
            ],
            Relationships =
            [
                new AiRelationship
                {
                    SourceTable = "Customer",
                    TargetTable = "Order",
                    Type = "OneToMany",
                    ConstraintName = "FK_Order_Customer",
                    OnDelete = "NO ACTION",
                    OnUpdate = "NO ACTION",
                },
            ],
        };

        var (entities, relationships) = schema.ToDomain();
        var order = entities.Single(entity => entity.TableName == "Order");
        var customerIdColumn = order.Columns.Single(column => column.Name == "CustomerId");
        var relationship = relationships.Single();

        // CustomerId は PK から降ろされて FK に矯正されているはず
        customerIdColumn.IsPrimaryKey.Should().BeFalse();
        customerIdColumn.IsForeignKey.Should().BeTrue();
        relationship.TargetColumnId.Should().Be(customerIdColumn.Id);
    }

    /// <summary>「テーブル名+Id」形式の非主キー列が外部キー候補として自動認識されることを検証する</summary>
    [Fact(DisplayName = "テーブル名+Id 形式の非 PK 列は FK 候補として自動認識される")]
    public void ToDomain_LikelyForeignKeyName_IsRecognizedAsFK()
    {
        // isForeignKey=false だが列名が「テーブル名+Id」形式の場合に FK として採用されるケース
        var schema = new AiSchemaJson
        {
            Tables =
            [
                new AiTable
                {
                    Name = "Category",
                    Columns =
                    [
                        new AiColumn
                        {
                            Name = "CategoryId",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                    ],
                },
                new AiTable
                {
                    Name = "Product",
                    Columns =
                    [
                        new AiColumn
                        {
                            Name = "ProductId",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new AiColumn
                        {
                            // AI が isForeignKey=false のまま返したが名前から FK と判断できる
                            Name = "CategoryId",
                            DataType = "int",
                            IsPrimaryKey = false,
                            IsForeignKey = false,
                            IsNullable = false,
                        },
                    ],
                },
            ],
            Relationships =
            [
                new AiRelationship
                {
                    SourceTable = "Category",
                    TargetTable = "Product",
                    Type = "OneToMany",
                    ConstraintName = "FK_Product_Category",
                    OnDelete = "NO ACTION",
                    OnUpdate = "NO ACTION",
                },
            ],
        };

        var (entities, relationships) = schema.ToDomain();
        var product = entities.Single(entity => entity.TableName == "Product");
        var categoryIdColumn = product.Columns.Single(column => column.Name == "CategoryId");
        var relationship = relationships.Single();

        categoryIdColumn.IsForeignKey.Should().BeTrue();
        relationship.TargetColumnId.Should().Be(categoryIdColumn.Id);
    }
}
