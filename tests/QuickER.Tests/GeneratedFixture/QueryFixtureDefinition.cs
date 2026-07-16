using QuickER.CodeGen.CSharp;
using QuickER.Model;

namespace QuickER.Tests.GeneratedQueryFixture;

/// <summary>
/// 名前付きクエリ入りの固定フィクスチャを生成する単一ソース。
/// </summary>
/// <remarks>
/// <para>
/// 入力の ER 図は方言可搬フィクスチャ（<see cref="Tests.GeneratedPortableFixture.PortableFixtureDefinition"/>）と
/// 同一の 2 エンティティ（customers / orders・VO 有効）に、<b>名前付きクエリ定義を追加</b>したもの。
/// オプションは SQLite 方言のQuickER 版 Repository＋EF Core 併存（<c>SqlitePortableFixture</c> と同プロファイル）で、
/// 生成されたクエリメソッドを実ファイル DB（Docker 不要＝CI 常時実行）のQuickER・EF Core 両実装で意味検証できる。
/// </para>
/// <para>
/// クエリはミニ DSL の全戻り形（一覧・単一・件数・射影）・文字列一致・IN（VO 列×リストパラメータ）・
/// ページング・自由 SQL（リストパラメータの IN 展開込み）・manual を網羅する。
/// </para>
/// </remarks>
public static class QueryFixtureDefinition
{
    /// <summary>生成フィクスチャの名前空間（既存フィクスチャと衝突しない専用 namespace）</summary>
    public const string NamespaceName = "QuickER.Tests.GeneratedQueryFixture";

    /// <summary>コミット済みフィクスチャファイル名</summary>
    public const string OutputFileName = "QueryFixture.g.cs";

    /// <summary>フィクスチャ生成に用いる決定的なオプション（SQLite 方言・QuickER＋EF Core 併存・VO 有効）</summary>
    public static CodeGenerationOptions Options { get; } =
        new()
        {
            RootNamespace = NamespaceName,
            OutputFileName = OutputFileName,
            GenerateEditModels = true,
            GenerateMappers = true,
            GenerateRepositories = true,
            GenerateValueObjects = true,
            GenerateEfCore = true,
            RepositoryDialects = ["sqlite"],
            SplitFilesByCategory = false,
        };

    // クエリ定義の ID は決定的でなければならないため固定 GUID を用いる（出力には影響しないが定義の同一性を保つ）
    private static readonly Guid QueryGetByCustomer = new("dddddddd-0000-0000-0000-000000000001");
    private static readonly Guid QueryFindTop = new("dddddddd-0000-0000-0000-000000000002");
    private static readonly Guid QueryCountByCustomer = new("dddddddd-0000-0000-0000-000000000003");
    private static readonly Guid QuerySearchMemo = new("dddddddd-0000-0000-0000-000000000004");
    private static readonly Guid QueryGetByIds = new("dddddddd-0000-0000-0000-000000000005");
    private static readonly Guid QueryGetSummaries = new("dddddddd-0000-0000-0000-000000000006");
    private static readonly Guid QuerySumAmounts = new("dddddddd-0000-0000-0000-000000000007");
    private static readonly Guid QueryGetRecent = new("dddddddd-0000-0000-0000-000000000008");
    private static readonly Guid QuerySpecialLookup = new("dddddddd-0000-0000-0000-000000000009");
    private static readonly Guid QueryGetByCustomerTyped = new(
        "dddddddd-0000-0000-0000-000000000010"
    );

    /// <summary>可搬フィクスチャの図（SQL Server 型表記）に名前付きクエリ定義を追加して返す</summary>
    public static ErDiagram Build()
    {
        var diagram = Tests.GeneratedPortableFixture.PortableFixtureDefinition.Build();
        var orders = diagram.Entities.First(entity => entity.TableName == "orders");
        var orderPk = orders.Columns.First(column => column.Name == "order_id");
        var orderCustomerFk = orders.Columns.First(column => column.Name == "customer_id");
        var orderAmount = orders.Columns.First(column => column.Name == "amount");

        diagram.Queries.Add(
            new QueryDefinition
            {
                Id = QueryGetByCustomer,
                EntityId = orders.Id,
                Name = "GetByCustomer",
                Description = "顧客IDで注文を新しい順（注文ID降順）に検索する（ページング付き）",
                Returns = QueryReturnShape.List,
                Parameters =
                {
                    new QueryParameter { Name = "customerId", Type = "int32" },
                },
                Condition = "customer_id = @customerId",
                // EF Core Sqlite は decimal のサーバーサイド並び替えを非対応のため、並び替えは整数キーで行う
                OrderBy =
                {
                    new QueryOrdering { ColumnId = orderPk.Id, Descending = true },
                },
                HasPaging = true,
            }
        );

        diagram.Queries.Add(
            new QueryDefinition
            {
                Id = QueryFindTop,
                EntityId = orders.Id,
                Name = "FindTop",
                Description = "最新（注文IDが最大）の注文を 1 件取得する",
                Returns = QueryReturnShape.Single,
                OrderBy =
                {
                    new QueryOrdering { ColumnId = orderPk.Id, Descending = true },
                },
            }
        );

        diagram.Queries.Add(
            new QueryDefinition
            {
                Id = QueryCountByCustomer,
                EntityId = orders.Id,
                Name = "CountByCustomer",
                Description = "顧客IDに紐づく注文件数を取得する",
                Returns = QueryReturnShape.Count,
                Parameters =
                {
                    new QueryParameter { Name = "customerId", Type = "int32" },
                },
                Condition = "customer_id = @customerId",
            }
        );

        diagram.Queries.Add(
            new QueryDefinition
            {
                Id = QuerySearchMemo,
                EntityId = orders.Id,
                Name = "SearchMemo",
                Description = "メモの部分一致で注文を検索する",
                Returns = QueryReturnShape.List,
                Parameters =
                {
                    new QueryParameter { Name = "keyword", Type = "string(50)" },
                },
                Condition = "memo LIKE @keyword",
            }
        );

        diagram.Queries.Add(
            new QueryDefinition
            {
                Id = QueryGetByIds,
                EntityId = orders.Id,
                Name = "GetByIds",
                Description = "注文IDの一覧で注文を取得する",
                Returns = QueryReturnShape.List,
                Parameters =
                {
                    new QueryParameter
                    {
                        Name = "ids",
                        Type = "int32",
                        IsList = true,
                    },
                },
                Condition = "order_id IN @ids",
            }
        );

        diagram.Queries.Add(
            new QueryDefinition
            {
                Id = QueryGetSummaries,
                EntityId = orders.Id,
                Name = "GetSummaries",
                Description = "顧客IDに紐づく注文を射影（顧客ID・金額）で新しい順に取得する",
                Returns = QueryReturnShape.Projection,
                ResultTypeName = "OrderSummaryRow",
                Parameters =
                {
                    new QueryParameter { Name = "customerId", Type = "int32" },
                },
                // EF Core Sqlite の decimal 制約（サーバーサイド比較・並び替え非対応）に合わせ、条件・並び替えは整数キーで行う。
                // decimal（amount）は射影フィールドとしての実体化のみに使う（それは EF Core でも可能）
                Condition = "customer_id = @customerId",
                OrderBy =
                {
                    new QueryOrdering { ColumnId = orderPk.Id, Descending = true },
                },
                HasPaging = true,
                Fields =
                {
                    new ProjectionField
                    {
                        Name = "CustomerId",
                        Type = "int32",
                        SourceColumnId = orderCustomerFk.Id,
                    },
                    new ProjectionField
                    {
                        Name = "Amount",
                        Type = "decimal(10,2)",
                        SourceColumnId = orderAmount.Id,
                    },
                },
            }
        );

        diagram.Queries.Add(
            new QueryDefinition
            {
                Id = QuerySumAmounts,
                EntityId = orders.Id,
                Name = "SumAmounts",
                Description = "顧客IDに紐づく注文金額の合計を取得する（自由 SQL・SQLite）",
                Returns = QueryReturnShape.Scalar,
                ScalarType = "decimal(10,2)",
                Implementation = QueryImplementationKind.Sql,
                Sql =
                {
                    ["sqlite"] =
                        "SELECT SUM(\"amount\") FROM \"orders\" WHERE \"customer_id\" = @customerId",
                },
                Parameters =
                {
                    new QueryParameter { Name = "customerId", Type = "int32" },
                },
            }
        );

        diagram.Queries.Add(
            new QueryDefinition
            {
                Id = QueryGetRecent,
                EntityId = orders.Id,
                Name = "GetByIdsRaw",
                Description = "注文IDの一覧で注文を取得する（自由 SQL・IN のリスト展開）",
                Returns = QueryReturnShape.List,
                Implementation = QueryImplementationKind.Sql,
                Sql =
                {
                    ["sqlite"] =
                        "SELECT * FROM \"orders\" WHERE \"order_id\" IN (@ids) ORDER BY \"order_id\"",
                },
                Parameters =
                {
                    new QueryParameter
                    {
                        Name = "ids",
                        Type = "int32",
                        IsList = true,
                    },
                },
            }
        );

        diagram.Queries.Add(
            new QueryDefinition
            {
                Id = QueryGetByCustomerTyped,
                EntityId = orders.Id,
                Name = "GetByCustomerTyped",
                Description = "顧客IDで注文を古い順に検索する（列参照型付け＝VO 有効時は VO 引数）",
                Returns = QueryReturnShape.List,
                Parameters =
                {
                    new QueryParameter { Name = "customerId", SourceColumnId = orderCustomerFk.Id },
                },
                Condition = "customer_id = @customerId",
                OrderBy = { new QueryOrdering { ColumnId = orderPk.Id } },
            }
        );

        diagram.Queries.Add(
            new QueryDefinition
            {
                Id = QuerySpecialLookup,
                EntityId = orders.Id,
                Name = "SpecialLookup",
                Description = "利用者が partial クラスで実装する特別な検索（manual）",
                Returns = QueryReturnShape.Single,
                Implementation = QueryImplementationKind.Manual,
                Parameters =
                {
                    new QueryParameter { Name = "customerId", Type = "int32" },
                },
            }
        );

        return diagram;
    }
}
