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
            ],
            ["remove_query"] =
            [
                (
                    "base",
                    new JsonObject { ["table_name"] = "Order", ["query_name"] = "ByCustomer" }
                ),
            ],
        };
}
