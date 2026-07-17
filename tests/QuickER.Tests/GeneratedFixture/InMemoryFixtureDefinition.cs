using QuickER.CodeGen.CSharp;
using QuickER.Model;

namespace QuickER.Tests.GeneratedInMemoryFixture;

/// <summary>
/// インメモリ Repository の実行時テスト用「固定フィクスチャ」を生成する単一ソース。
/// </summary>
/// <remarks>
/// <para>
/// 契約（<c>I{Entity}Repository</c> / <c>IRepository</c> / <c>SqlQuery</c> 等）＋インメモリ実装のみを出力する
/// （<c>GenerateRepositories=false</c> / <c>GenerateEfCore=false</c> / <c>GenerateInMemoryRepositories=true</c>・VO off・Split off）。
/// これによりインメモリ単独出力（QuickER の ADO 実装・EF Core なし）がコンパイル可能かつ実行時に正しく動くことを実証する。
/// </para>
/// <para>
/// 図は実行時テストに適した最小構成:
/// <list type="bullet">
///   <item>親子（1対多・ON DELETE CASCADE）: <c>customers</c> → <c>orders</c></item>
///   <item>1対1: <c>customers</c> ↔ <c>customer_profiles</c></item>
///   <item>NULL 許容混在（<c>balance</c> / <c>memo</c> / <c>bio</c>）を含む</item>
///   <item>DB 照合順序の揺れを避けるため日本語識別子は使わない</item>
///   <item>名前付きクエリ（ミニ DSL の一覧＋ページング・単一・件数・射影）＝DSL 共有本体が
///     インメモリ実装にも出力されることの実行検証用</item>
/// </list>
/// namespace は既存フィクスチャと衝突させない専用 namespace を使う。
/// </para>
/// </remarks>
public static class InMemoryFixtureDefinition
{
    /// <summary>生成フィクスチャの名前空間（既存フィクスチャと衝突しない専用 namespace）</summary>
    public const string NamespaceName = "QuickER.Tests.GeneratedInMemoryFixture";

    /// <summary>コミット済みフィクスチャファイル名</summary>
    public const string OutputFileName = "InMemoryFixture.g.cs";

    /// <summary>フィクスチャ生成に用いる決定的なオプション（契約＋インメモリのみ・VO off・Split off）</summary>
    public static CodeGenerationOptions Options { get; } =
        new()
        {
            RootNamespace = NamespaceName,
            OutputFileName = OutputFileName,
            GenerateEditModels = true,
            GenerateMappers = true,
            GenerateRepositories = false,
            GenerateEfCore = false,
            GenerateInMemoryRepositories = true,
            GenerateValueObjects = false,
            SplitFilesByCategory = false,
        };

    // 図の要素 ID は決定的でなければ再生成時に差分が出るため、固定 GUID を用いる。
    private static readonly Guid CustomerId = new("dddddddd-0000-0000-0000-000000000001");
    private static readonly Guid CustomerPkColId = new("dddddddd-0000-0000-0000-000000000002");
    private static readonly Guid CustomerNameColId = new("dddddddd-0000-0000-0000-000000000003");
    private static readonly Guid CustomerBalanceColId = new("dddddddd-0000-0000-0000-000000000004");

    private static readonly Guid OrderId = new("eeeeeeee-0000-0000-0000-000000000001");
    private static readonly Guid OrderPkColId = new("eeeeeeee-0000-0000-0000-000000000002");
    private static readonly Guid OrderCustomerFkColId = new("eeeeeeee-0000-0000-0000-000000000003");
    private static readonly Guid OrderMemoColId = new("eeeeeeee-0000-0000-0000-000000000004");
    private static readonly Guid OrderAmountColId = new("eeeeeeee-0000-0000-0000-000000000005");

    private static readonly Guid ProfileId = new("ffffffff-0000-0000-0000-000000000001");
    private static readonly Guid ProfilePkColId = new("ffffffff-0000-0000-0000-000000000002");
    private static readonly Guid ProfileCustomerFkColId = new(
        "ffffffff-0000-0000-0000-000000000003"
    );
    private static readonly Guid ProfileBioColId = new("ffffffff-0000-0000-0000-000000000004");

    private static readonly Guid RelCustomerOrders = new("dddddddd-1111-0000-0000-000000000001");
    private static readonly Guid RelCustomerProfile = new("dddddddd-1111-0000-0000-000000000002");

    // 名前付きクエリの ID も決定的でなければならないため固定 GUID を用いる
    private static readonly Guid QueryGetByCustomer = new("99999999-0000-0000-0000-000000000001");
    private static readonly Guid QueryFindTop = new("99999999-0000-0000-0000-000000000002");
    private static readonly Guid QueryCountByCustomer = new("99999999-0000-0000-0000-000000000003");
    private static readonly Guid QueryGetSummaries = new("99999999-0000-0000-0000-000000000004");

    /// <summary>インメモリ実行時テスト用の ER 図を決定的に構築する（要素 ID は固定 GUID・日本語識別子なし）。</summary>
    public static ErDiagram Build()
    {
        var customer = new Entity
        {
            Id = CustomerId,
            TableName = "customers",
            Columns =
            {
                new Column
                {
                    Id = CustomerPkColId,
                    Name = "customer_id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Id = CustomerNameColId,
                    Name = "name",
                    DataType = "nvarchar(50)",
                    IsNullable = false,
                },
                new Column
                {
                    Id = CustomerBalanceColId,
                    Name = "balance",
                    DataType = "decimal(10,2)",
                    IsNullable = true,
                },
            },
        };

        var order = new Entity
        {
            Id = OrderId,
            TableName = "orders",
            Columns =
            {
                new Column
                {
                    Id = OrderPkColId,
                    Name = "order_id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Id = OrderCustomerFkColId,
                    Name = "customer_id",
                    DataType = "int",
                    IsForeignKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Id = OrderMemoColId,
                    Name = "memo",
                    DataType = "nvarchar(50)",
                    IsNullable = true,
                },
                new Column
                {
                    Id = OrderAmountColId,
                    Name = "amount",
                    DataType = "decimal(10,2)",
                    IsNullable = false,
                },
            },
        };

        var profile = new Entity
        {
            Id = ProfileId,
            TableName = "customer_profiles",
            Columns =
            {
                new Column
                {
                    Id = ProfilePkColId,
                    Name = "profile_id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Id = ProfileCustomerFkColId,
                    Name = "customer_id",
                    DataType = "int",
                    IsForeignKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Id = ProfileBioColId,
                    Name = "bio",
                    DataType = "nvarchar(50)",
                    IsNullable = true,
                },
            },
        };

        var diagram = new ErDiagram
        {
            Entities = { customer, order, profile },
            Relationships =
            {
                // 1対多: customers -> orders（ON DELETE CASCADE）
                new Relationship
                {
                    Id = RelCustomerOrders,
                    Type = RelationshipType.OneToMany,
                    SourceEntityId = CustomerId,
                    TargetEntityId = OrderId,
                    SourceColumnId = CustomerPkColId,
                    TargetColumnId = OrderCustomerFkColId,
                    ConstraintName = "FK_orders_customers",
                    OnDelete = ForeignKeyReferentialAction.Cascade,
                    OnUpdate = ForeignKeyReferentialAction.NoAction,
                },
                // 1対1: customers <-> customer_profiles
                new Relationship
                {
                    Id = RelCustomerProfile,
                    Type = RelationshipType.OneToOne,
                    SourceEntityId = CustomerId,
                    TargetEntityId = ProfileId,
                    SourceColumnId = CustomerPkColId,
                    TargetColumnId = ProfileCustomerFkColId,
                    ConstraintName = "FK_customer_profiles_customers",
                    OnDelete = ForeignKeyReferentialAction.NoAction,
                    OnUpdate = ForeignKeyReferentialAction.NoAction,
                },
            },
        };

        // 名前付きクエリ（ミニ DSL）: 共有本体がインメモリ実装にも出力されることを
        // 代表的な戻り形（一覧＋ページング・単一・件数・射影）×パラメータありで実行検証する
        diagram.Queries.Add(
            new QueryDefinition
            {
                Id = QueryGetByCustomer,
                EntityId = OrderId,
                Name = "GetByCustomer",
                Description = "顧客IDで注文を新しい順（注文ID降順）に検索する（ページング付き）",
                Returns = QueryReturnShape.List,
                Parameters =
                {
                    new QueryParameter { Name = "customerId", Type = "int32" },
                },
                Condition = "customer_id = @customerId",
                OrderBy =
                {
                    new QueryOrdering { ColumnId = OrderPkColId, Descending = true },
                },
                HasPaging = true,
            }
        );

        diagram.Queries.Add(
            new QueryDefinition
            {
                Id = QueryFindTop,
                EntityId = OrderId,
                Name = "FindTop",
                Description = "最新（注文IDが最大）の注文を 1 件取得する",
                Returns = QueryReturnShape.Single,
                OrderBy =
                {
                    new QueryOrdering { ColumnId = OrderPkColId, Descending = true },
                },
            }
        );

        diagram.Queries.Add(
            new QueryDefinition
            {
                Id = QueryCountByCustomer,
                EntityId = OrderId,
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
                Id = QueryGetSummaries,
                EntityId = OrderId,
                Name = "GetSummaries",
                Description = "顧客IDに紐づく注文を射影（顧客ID・金額）で古い順に取得する",
                Returns = QueryReturnShape.Projection,
                ResultTypeName = "OrderSummaryRow",
                Parameters =
                {
                    new QueryParameter { Name = "customerId", Type = "int32" },
                },
                Condition = "customer_id = @customerId",
                OrderBy = { new QueryOrdering { ColumnId = OrderPkColId } },
                Fields =
                {
                    new ProjectionField
                    {
                        Name = "CustomerId",
                        Type = "int32",
                        SourceColumnId = OrderCustomerFkColId,
                    },
                    new ProjectionField
                    {
                        Name = "Amount",
                        Type = "decimal(10,2)",
                        SourceColumnId = OrderAmountColId,
                    },
                },
            }
        );

        return diagram;
    }
}
