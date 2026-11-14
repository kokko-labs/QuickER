using System.Text.Json.Nodes;
using QuickER.Model;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// 引数の総当たり（<see cref="ToolArgumentFuzz"/>）で使う図と、ER 図操作ツールごとの「成功する引数」
/// </summary>
/// <remarks>
/// MCP（ファイル）と内蔵チャット（GUI の図）の両方へ同じ起点を流すために共有する。
/// ベースラインはテーブル名・列名で図を指すので、図の Id は呼び出しごとに新しくてよい。
/// </remarks>
internal static class ToolArgumentFuzzSeed
{
    /// <summary>
    /// Customer(Id PK, Name, Email＋一意制約 UQ_Customer_Email) と Order(Id PK, CustomerId, BillingCustomerId, Total)、
    /// Customer → Order の外部キー FK_Order_Customer、Order の名前付きクエリ ByCustomer を持つ図を作る
    /// </summary>
    public static ErDiagram BuildDiagram()
    {
        var customerId = new Column
        {
            Name = "Id",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var email = new Column { Name = "Email", DataType = "nvarchar(100)" };
        var customer = new Entity
        {
            TableName = "Customer",
            Columns =
            {
                customerId,
                new Column { Name = "Name", DataType = "nvarchar(50)" },
                email,
            },
        };
        customer.UniqueConstraints.Add(
            new UniqueConstraint { Name = "UQ_Customer_Email", ColumnIds = [email.Id] }
        );

        var orderCustomerId = new Column
        {
            Name = "CustomerId",
            DataType = "int",
            IsForeignKey = true,
            IsNullable = false,
        };
        var order = new Entity
        {
            TableName = "Order",
            Columns =
            {
                new Column
                {
                    Name = "Id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                orderCustomerId,
                new Column { Name = "BillingCustomerId", DataType = "int" },
                new Column { Name = "Total", DataType = "decimal(10,2)" },
            },
        };

        var diagram = new ErDiagram
        {
            TargetDbms = "sqlserver",
            Entities = { customer, order },
            Relationships =
            {
                new Relationship
                {
                    SourceEntityId = customer.Id,
                    TargetEntityId = order.Id,
                    Type = RelationshipType.OneToMany,
                    ColumnPairs = [new RelationshipColumnPair(customerId.Id, orderCustomerId.Id)],
                    ConstraintName = "FK_Order_Customer",
                },
            },
        };
        diagram.Queries.Add(
            new QueryDefinition
            {
                EntityId = order.Id,
                Name = "ByCustomer",
                Returns = QueryReturnShape.List,
                Implementation = QueryImplementationKind.Dsl,
                Condition = "CustomerId = @customerId",
                Parameters =
                {
                    new QueryParameter { Name = "customerId", SourceColumnId = orderCustomerId.Id },
                },
            }
        );

        return diagram;
    }

    /// <summary>ER 図操作ツール（カタログの 15 ツール）ごとの成功する引数（ラベル → 引数）</summary>
    /// <remarks>呼び出すたびに新しいオブジェクトを返す（ケースの組み立てで書き換えないが、取り違えを防ぐ）</remarks>
    public static IReadOnlyDictionary<
        string,
        IReadOnlyList<(string Label, JsonObject Args)>
    > CatalogBaselines() =>
        new Dictionary<string, IReadOnlyList<(string, JsonObject)>>
        {
            ["get_diagram_summary"] = [("base", new JsonObject())],
            ["list_queries"] = [("base", new JsonObject())],
            ["add_entity"] =
            [
                ("base", new JsonObject { ["table_name"] = "Product", ["description"] = "d" }),
            ],
            ["remove_entity"] = [("base", new JsonObject { ["table_name"] = "Order" })],
            ["add_column"] =
            [
                (
                    "base",
                    new JsonObject
                    {
                        ["table_name"] = "Customer",
                        ["column_name"] = "Phone",
                        ["data_type"] = "nvarchar(20)",
                        ["is_primary_key"] = false,
                        ["is_nullable"] = true,
                        ["description"] = "d",
                    }
                ),
            ],
            ["remove_column"] =
            [
                ("base", new JsonObject { ["table_name"] = "Customer", ["column_name"] = "Name" }),
            ],
            ["set_entity_property"] =
            [
                (
                    "base",
                    new JsonObject
                    {
                        ["table_name"] = "Customer",
                        ["new_table_name"] = "Client",
                        ["memo"] = "m",
                        ["description"] = "d",
                    }
                ),
            ],
            ["set_column_property"] =
            [
                (
                    "base",
                    new JsonObject
                    {
                        ["table_name"] = "Customer",
                        ["column_name"] = "Name",
                        ["description"] = "d",
                        ["data_type"] = "nvarchar(60)",
                        ["is_nullable"] = false,
                    }
                ),
            ],
            ["add_relationship"] =
            [
                (
                    "base",
                    new JsonObject
                    {
                        ["source_table"] = "Customer",
                        ["target_table"] = "Order",
                        ["source_columns"] = new JsonArray("Id"),
                        ["target_columns"] = new JsonArray("BillingCustomerId"),
                        ["relationship_type"] = "OneToMany",
                        ["constraint_name"] = "FK_Order_Billing",
                    }
                ),
            ],
            ["remove_relationship"] =
            [
                (
                    "base",
                    new JsonObject
                    {
                        ["source_table"] = "Customer",
                        ["target_table"] = "Order",
                        ["constraint_name"] = "FK_Order_Customer",
                    }
                ),
            ],
            ["set_primary_key"] =
            [
                (
                    "base",
                    new JsonObject { ["table_name"] = "Order", ["columns"] = new JsonArray("Id") }
                ),
            ],
            ["set_unique_constraint"] =
            [
                (
                    "base",
                    new JsonObject
                    {
                        ["table_name"] = "Customer",
                        ["columns"] = new JsonArray("Name"),
                        ["name"] = "UQ_Customer_Name",
                    }
                ),
            ],
            ["remove_unique_constraint"] =
            [
                (
                    "base",
                    new JsonObject
                    {
                        ["table_name"] = "Customer",
                        ["columns"] = new JsonArray("Email"),
                    }
                ),
            ],
            ["set_query"] =
            [
                (
                    "list",
                    new JsonObject
                    {
                        ["table_name"] = "Order",
                        ["query_name"] = "ByBilling",
                        ["description"] = "d",
                        ["returns"] = "list",
                        ["implementation"] = "dsl",
                        ["condition"] = "BillingCustomerId = @billing",
                        ["parameters"] = new JsonArray(
                            new JsonObject
                            {
                                ["name"] = "billing",
                                ["source_column"] = "BillingCustomerId",
                                ["is_list"] = false,
                            }
                        ),
                        ["order_by"] = new JsonArray(
                            new JsonObject { ["column"] = "Id", ["descending"] = true }
                        ),
                        ["paging"] = true,
                    }
                ),
                (
                    "projection",
                    new JsonObject
                    {
                        ["table_name"] = "Order",
                        ["query_name"] = "Totals",
                        ["returns"] = "projection",
                        ["result_type_name"] = "OrderTotalRow",
                        ["fields"] = new JsonArray(
                            new JsonObject
                            {
                                ["name"] = "Amount",
                                ["source_column"] = "Total",
                                ["is_nullable"] = false,
                            }
                        ),
                    }
                ),
                (
                    "sql",
                    new JsonObject
                    {
                        ["table_name"] = "Order",
                        ["query_name"] = "CountLarge",
                        ["returns"] = "scalar",
                        ["scalar_type"] = "int32",
                        ["implementation"] = "sql",
                        ["sql"] = new JsonObject
                        {
                            ["sqlserver"] = "SELECT COUNT(*) FROM [Order] WHERE Total > @min",
                            ["sqlite"] = "SELECT COUNT(*) FROM \"Order\" WHERE Total > @min",
                        },
                        ["parameters"] = new JsonArray(
                            new JsonObject { ["name"] = "min", ["type"] = "decimal(10,2)" }
                        ),
                    }
                ),
                // オブジェクトの配列が 2 要素のベースライン（2 番目の要素の中の崩しを作る）
                (
                    "list2",
                    new JsonObject
                    {
                        ["table_name"] = "Order",
                        ["query_name"] = "ByEitherCustomer",
                        ["returns"] = "list",
                        ["implementation"] = "dsl",
                        ["condition"] = "BillingCustomerId = @billing OR CustomerId = @customer",
                        ["parameters"] = new JsonArray(
                            new JsonObject
                            {
                                ["name"] = "billing",
                                ["source_column"] = "BillingCustomerId",
                                ["is_list"] = false,
                            },
                            new JsonObject
                            {
                                ["name"] = "customer",
                                ["source_column"] = "CustomerId",
                                ["is_list"] = false,
                            }
                        ),
                        ["order_by"] = new JsonArray(
                            new JsonObject { ["column"] = "Total", ["descending"] = true },
                            new JsonObject { ["column"] = "Id", ["descending"] = false }
                        ),
                    }
                ),
                (
                    "projection2",
                    new JsonObject
                    {
                        ["table_name"] = "Order",
                        ["query_name"] = "TotalsByCustomer",
                        ["returns"] = "projection",
                        ["result_type_name"] = "OrderCustomerTotalRow",
                        ["fields"] = new JsonArray(
                            new JsonObject
                            {
                                ["name"] = "Amount",
                                ["source_column"] = "Total",
                                ["is_nullable"] = false,
                            },
                            new JsonObject
                            {
                                ["name"] = "Customer",
                                ["source_column"] = "CustomerId",
                                ["is_nullable"] = false,
                            }
                        ),
                    }
                ),
                (
                    "sql2",
                    new JsonObject
                    {
                        ["table_name"] = "Order",
                        ["query_name"] = "CountBetween",
                        ["returns"] = "scalar",
                        ["scalar_type"] = "int32",
                        ["implementation"] = "sql",
                        ["sql"] = new JsonObject
                        {
                            ["sqlserver"] =
                                "SELECT COUNT(*) FROM [Order] WHERE Total BETWEEN @min AND @max",
                            ["sqlite"] =
                                "SELECT COUNT(*) FROM \"Order\" WHERE Total BETWEEN @min AND @max",
                        },
                        ["parameters"] = new JsonArray(
                            new JsonObject { ["name"] = "min", ["type"] = "decimal(10,2)" },
                            new JsonObject { ["name"] = "max", ["type"] = "decimal(10,2)" }
                        ),
                    }
                ),
            ],
            ["remove_query"] =
            [
                (
                    "base",
                    new JsonObject { ["table_name"] = "Order", ["query_name"] = "ByCustomer" }
                ),
            ],
        };

    /// <summary>
    /// ER 図操作ツール（カタログの 15 ツール）ごとの必須の引数（手書き。配列の要素の中の必須は <c>{引数}[].{名前}</c>）
    /// </summary>
    /// <remarks>
    /// 総当たりは崩す元の必須をカタログのスキーマから読み、入口の形の検査も同じスキーマを読む。カタログから必須を外しても
    /// 両方が同じだけ緩むので、この一覧と照合して名指しする（<see cref="ToolArgumentShapeFuzzRunner"/>）。
    /// 期待は各ツールの description と、ツールの処理が断る引数（表名・列名・関係の両端・クエリの名前と戻り値）から手で書く。
    /// </remarks>
    public static IReadOnlyDictionary<string, string[]> CatalogRequired() =>
        new Dictionary<string, string[]>
        {
            ["get_diagram_summary"] = [],
            ["list_queries"] = [],
            ["add_entity"] = ["table_name"],
            ["remove_entity"] = ["table_name"],
            ["add_column"] = ["table_name", "column_name", "data_type"],
            ["remove_column"] = ["table_name", "column_name"],
            ["set_entity_property"] = ["table_name"],
            ["set_column_property"] = ["table_name", "column_name"],
            ["add_relationship"] = ["source_table", "target_table", "relationship_type"],
            ["remove_relationship"] = ["source_table", "target_table"],
            ["set_primary_key"] = ["table_name", "columns"],
            ["set_unique_constraint"] = ["table_name", "columns"],
            ["remove_unique_constraint"] = ["table_name", "columns"],
            ["set_query"] =
            [
                "table_name",
                "query_name",
                "returns",
                "parameters[].name",
                "order_by[].column",
                "fields[].name",
            ],
            ["remove_query"] = ["table_name", "query_name"],
        };
}
