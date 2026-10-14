using QuickER.CodeGen.CSharp;
using QuickER.Model;

namespace QuickER.Tests.GeneratedBackendParityFixture;

/// <summary>
/// 実装先どうしの一致テスト（QuickER 版 Repository・EF Core・インメモリ・リモート）の固定フィクスチャを生成する単一ソース。
/// </summary>
/// <remarks>
/// <para>
/// 4 つの実装先を 1 つの名前空間へそろえる（同じエンティティ型のまま、操作の列を実装先だけ差し替えて流すため）。
/// SQLite 方言の QuickER 版 Repository・EF Core・インメモリ・リモートサービスを併存させ、値オブジェクトと無制限バイナリ列の
/// 除外を有効にする。
/// </para>
/// <para>
/// 図は <c>customers → orders → order_lines</c> の 3 階層（どれも ON DELETE CASCADE の 1 対多＝カスケード保存・削除の
/// ナビゲーションを持つ）。型の違う NULL 許容列（文字列・decimal・日時）、<c>orders</c> の除外 blob 2 本
/// （<c>payload</c>＝NULL 許容・<c>thumb</c>＝NOT NULL）、<c>customers.name</c> の一意制約と、<c>customers.referrer_id</c> の自己参照（循環するカスケード）を持つ。
/// 行バージョンは持たせない＝SQLite では QuickER 版も EF Core も版を採番しないのに、インメモリは擬似版を採番するため、
/// 同じ操作で結果が割れる（行バージョンの系統は <c>ConcurrencyFixture</c> が SQL Server で担う）。
/// </para>
/// </remarks>
public static class BackendParityFixtureDefinition
{
    /// <summary>生成フィクスチャの名前空間（既存フィクスチャと衝突しない専用 namespace）</summary>
    public const string NamespaceName = "QuickER.Tests.GeneratedBackendParityFixture";

    /// <summary>コミット済みフィクスチャファイル名（本体）</summary>
    public const string OutputFileName = "BackendParityFixture.g.cs";

    /// <summary>コミット済みフィクスチャファイル名（サーバー実装）</summary>
    public const string ServerOutputFileName = "BackendParityFixture.RemoteServer.g.cs";

    /// <summary>
    /// フィクスチャ生成に用いる決定的なオプション。
    /// SQLite 方言の QuickER 版 Repository・EF Core・インメモリ・リモートサービスを併存させ、値オブジェクトと無制限バイナリ除外を
    /// 有効にする（EditModel・Mapper は一致テストで使わないので出さない）。
    /// </summary>
    public static CodeGenerationOptions Options { get; } =
        new()
        {
            RootNamespace = NamespaceName,
            OutputFileName = OutputFileName,
            GenerateEditModels = false,
            GenerateMappers = false,
            GenerateRepositories = true,
            GenerateValueObjects = true,
            GenerateEfCoreRepositories = true,
            GenerateInMemoryRepositories = true,
            GenerateRemoteServices = true,
            RepositoryDialects = ["sqlite"],
            ExcludeUnboundedBinaryColumns = true,
            SplitFilesByCategory = false,
        };

    // 図の要素 ID は決定的でなければ再生成時に差分が出るため、固定 GUID を用いる。
    private static readonly Guid CustomerId = new("b9000000-0000-0000-0000-000000000001");
    private static readonly Guid CustomerPkColId = new("b9000000-0000-0000-0000-000000000002");
    private static readonly Guid CustomerNameColId = new("b9000000-0000-0000-0000-000000000003");
    private static readonly Guid CustomerNoteColId = new("b9000000-0000-0000-0000-000000000004");
    private static readonly Guid CustomerNameUniqueId = new("b9000000-0000-0000-0000-000000000005");
    private static readonly Guid CustomerReferrerColId = new(
        "b9000000-0000-0000-0000-000000000006"
    );

    private static readonly Guid OrderId = new("b9100000-0000-0000-0000-000000000001");
    private static readonly Guid OrderPkColId = new("b9100000-0000-0000-0000-000000000002");
    private static readonly Guid OrderCustomerFkColId = new("b9100000-0000-0000-0000-000000000003");
    private static readonly Guid OrderAmountColId = new("b9100000-0000-0000-0000-000000000004");
    private static readonly Guid OrderMemoColId = new("b9100000-0000-0000-0000-000000000005");
    private static readonly Guid OrderOrderedAtColId = new("b9100000-0000-0000-0000-000000000006");
    private static readonly Guid OrderIsPaidColId = new("b9100000-0000-0000-0000-000000000007");
    private static readonly Guid OrderPayloadColId = new("b9100000-0000-0000-0000-000000000008");
    private static readonly Guid OrderThumbColId = new("b9100000-0000-0000-0000-000000000009");

    private static readonly Guid LineId = new("b9200000-0000-0000-0000-000000000001");
    private static readonly Guid LinePkColId = new("b9200000-0000-0000-0000-000000000002");
    private static readonly Guid LineOrderFkColId = new("b9200000-0000-0000-0000-000000000003");
    private static readonly Guid LineItemColId = new("b9200000-0000-0000-0000-000000000004");
    private static readonly Guid LineQuantityColId = new("b9200000-0000-0000-0000-000000000005");
    private static readonly Guid LineDiscountColId = new("b9200000-0000-0000-0000-000000000006");

    private static readonly Guid RelCustomerOrders = new("b9300000-0000-0000-0000-000000000001");
    private static readonly Guid RelOrderLines = new("b9300000-0000-0000-0000-000000000002");
    private static readonly Guid RelCustomerReferrals = new("b9300000-0000-0000-0000-000000000003");

    /// <summary>一致テスト用の ER 図を決定的に構築する（要素 ID は固定 GUID・型は SQL Server 表記）。</summary>
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
                    Id = CustomerNoteColId,
                    Name = "note",
                    DataType = "nvarchar(100)",
                    IsNullable = true,
                },
                // 紹介元の顧客（自己参照・NULL 許容）。カスケードのナビゲーションが [Orders, 自己参照] の順になり、顧客の
                // カスケードの一括削除は循環として断られる（EF Core が循環を見つける前に文を流さないことの確かめに使う）
                new Column
                {
                    Id = CustomerReferrerColId,
                    Name = "referrer_id",
                    DataType = "int",
                    IsForeignKey = true,
                    IsNullable = true,
                },
            },
            UniqueConstraints =
            {
                new UniqueConstraint
                {
                    Id = CustomerNameUniqueId,
                    Name = "UQ_customers_name",
                    ColumnIds = [CustomerNameColId],
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
                    Id = OrderAmountColId,
                    Name = "amount",
                    DataType = "decimal(10,2)",
                    IsNullable = false,
                },
                new Column
                {
                    Id = OrderMemoColId,
                    Name = "memo",
                    DataType = "nvarchar(100)",
                    IsNullable = true,
                },
                new Column
                {
                    Id = OrderOrderedAtColId,
                    Name = "ordered_at",
                    DataType = "datetime2",
                    IsNullable = true,
                },
                new Column
                {
                    Id = OrderIsPaidColId,
                    Name = "is_paid",
                    DataType = "bit",
                    IsNullable = false,
                },
                // 無制限バイナリ（NULL 許容）＝除外対象。未取得状態は null
                new Column
                {
                    Id = OrderPayloadColId,
                    Name = "payload",
                    DataType = "varbinary(max)",
                    IsNullable = true,
                },
                // 無制限バイナリ（NOT NULL）＝除外対象。未取得状態は空の値
                new Column
                {
                    Id = OrderThumbColId,
                    Name = "thumb",
                    DataType = "varbinary(max)",
                    IsNullable = false,
                },
            },
        };

        var line = new Entity
        {
            Id = LineId,
            TableName = "order_lines",
            Columns =
            {
                new Column
                {
                    Id = LinePkColId,
                    Name = "line_id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Id = LineOrderFkColId,
                    Name = "order_id",
                    DataType = "int",
                    IsForeignKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Id = LineItemColId,
                    Name = "item",
                    DataType = "nvarchar(50)",
                    IsNullable = false,
                },
                new Column
                {
                    Id = LineQuantityColId,
                    Name = "quantity",
                    DataType = "int",
                    IsNullable = false,
                },
                new Column
                {
                    Id = LineDiscountColId,
                    Name = "discount",
                    DataType = "decimal(5,2)",
                    IsNullable = true,
                },
            },
        };

        return new ErDiagram
        {
            Entities = { customer, order, line },
            Relationships =
            {
                new Relationship
                {
                    Id = RelCustomerOrders,
                    Type = RelationshipType.OneToMany,
                    SourceEntityId = CustomerId,
                    TargetEntityId = OrderId,
                    ColumnPairs = [new(CustomerPkColId, OrderCustomerFkColId)],
                    ConstraintName = "FK_orders_customers",
                    OnDelete = ForeignKeyReferentialAction.Cascade,
                    OnUpdate = ForeignKeyReferentialAction.NoAction,
                },
                new Relationship
                {
                    Id = RelCustomerReferrals,
                    Type = RelationshipType.OneToMany,
                    SourceEntityId = CustomerId,
                    TargetEntityId = CustomerId,
                    ColumnPairs = [new(CustomerPkColId, CustomerReferrerColId)],
                    ConstraintName = "FK_customers_referrer",
                    OnDelete = ForeignKeyReferentialAction.NoAction,
                    OnUpdate = ForeignKeyReferentialAction.NoAction,
                },
                new Relationship
                {
                    Id = RelOrderLines,
                    Type = RelationshipType.OneToMany,
                    SourceEntityId = OrderId,
                    TargetEntityId = LineId,
                    ColumnPairs = [new(OrderPkColId, LineOrderFkColId)],
                    ConstraintName = "FK_order_lines_orders",
                    OnDelete = ForeignKeyReferentialAction.Cascade,
                    OnUpdate = ForeignKeyReferentialAction.NoAction,
                },
            },
        };
    }
}
