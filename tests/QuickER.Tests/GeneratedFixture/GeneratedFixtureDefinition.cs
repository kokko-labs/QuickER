using QuickER.CodeGen.CSharp;
using QuickER.Model;

namespace QuickER.Tests.GeneratedFixture;

/// <summary>
/// 実行時テスト用の固定フィクスチャ（<c>GeneratedFixture.g.cs</c>）を生成する「単一ソース」。
/// </summary>
/// <remarks>
/// <para>
/// コミット済みフィクスチャファイルと、それを再生成するドリフト検知テスト
/// (<see cref="GeneratedFixtureDriftTests"/>) は、この 1 箇所が返す図・オプションを共有する。
/// これにより「テストで使う型」と「ドリフト検知の期待値」が定義上ずれないことを保証する。
/// </para>
/// <para>
/// 図は実行時テストに適した最小構成:
/// <list type="bullet">
///   <item>親子（1対多・ON DELETE CASCADE）: <c>customers</c> → <c>orders</c></item>
///   <item>1対1: <c>customers</c> ↔ <c>customer_profiles</c></item>
///   <item>VO 対象カラム: <c>varchar(50)</c>（名前）・<c>decimal(10,2)</c>（金額）を含む</item>
///   <item>DB 照合順序の揺れを避けるため日本語識別子は使わない</item>
/// </list>
/// オプションは「全カテゴリ有効・VO 有効・Split 無効（1 ファイル）・専用 namespace」。
/// </para>
/// </remarks>
public static class GeneratedFixtureDefinition
{
    /// <summary>生成フィクスチャの名前空間（実行時テストはこの型を直接使用する）</summary>
    public const string NamespaceName = "QuickER.Tests.GeneratedFixture";

    /// <summary>フィクスチャ生成に用いる決定的なオプション（全カテゴリ・VO 有効・EF Core 有効・単一ファイル）</summary>
    public static CodeGenerationOptions Options { get; } =
        new()
        {
            NamespaceName = NamespaceName,
            OutputFileName = "GeneratedFixture.g.cs",
            GenerateEditModels = true,
            GenerateMappers = true,
            GenerateRepositories = true,
            GenerateValueObjects = true,
            GenerateEfCore = true,
            SplitFilesByCategory = false,
        };

    // 図の要素 ID は決定的でなければ再生成時に差分が出るため、固定 GUID を用いる。
    private static readonly Guid CustomerId = new("11111111-0000-0000-0000-000000000001");
    private static readonly Guid CustomerPkColId = new("11111111-0000-0000-0000-000000000002");
    private static readonly Guid CustomerNameColId = new("11111111-0000-0000-0000-000000000003");
    private static readonly Guid CustomerBalanceColId = new("11111111-0000-0000-0000-000000000004");
    private static readonly Guid CustomerActiveColId = new("11111111-0000-0000-0000-000000000005");

    private static readonly Guid OrderId = new("22222222-0000-0000-0000-000000000001");
    private static readonly Guid OrderPkColId = new("22222222-0000-0000-0000-000000000002");
    private static readonly Guid OrderCustomerFkColId = new("22222222-0000-0000-0000-000000000003");
    private static readonly Guid OrderMemoColId = new("22222222-0000-0000-0000-000000000004");
    private static readonly Guid OrderAmountColId = new("22222222-0000-0000-0000-000000000005");

    private static readonly Guid ProfileId = new("33333333-0000-0000-0000-000000000001");
    private static readonly Guid ProfilePkColId = new("33333333-0000-0000-0000-000000000002");
    private static readonly Guid ProfileCustomerFkColId = new(
        "33333333-0000-0000-0000-000000000003"
    );
    private static readonly Guid ProfileBioColId = new("33333333-0000-0000-0000-000000000004");

    private static readonly Guid RelCustomerOrders = new("44444444-0000-0000-0000-000000000001");
    private static readonly Guid RelCustomerProfile = new("44444444-0000-0000-0000-000000000002");

    /// <summary>
    /// 実行時テスト用の ER 図を決定的に構築する（要素 ID は固定 GUID・日本語識別子なし）。
    /// </summary>
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
                    DataType = "varchar(50)",
                    IsNullable = false,
                },
                new Column
                {
                    Id = CustomerBalanceColId,
                    Name = "balance",
                    DataType = "decimal(10,2)",
                    IsNullable = true,
                },
                new Column
                {
                    Id = CustomerActiveColId,
                    Name = "is_active",
                    DataType = "bit",
                    IsNullable = false,
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
                    DataType = "varchar(50)",
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
                    DataType = "varchar(50)",
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
