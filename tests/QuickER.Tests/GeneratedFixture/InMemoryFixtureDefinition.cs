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
            NamespaceName = NamespaceName,
            OutputFileName = OutputFileName,
            GenerateEntityClasses = true,
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

        return new ErDiagram
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
    }
}
