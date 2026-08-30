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
///   <item>VO 対象カラム: <c>varchar(50)</c>（名前）・<c>decimal(10,2)</c>（金額）・<c>datetime2</c>（注文日時）を含む</item>
///   <item>DB 照合順序の揺れを避けるため日本語識別子は使わない</item>
///   <item>名前付きクエリ（ミニ DSL）: CONTAINS（LIKE エスケープ）・IS NULL・decimal 比較＝
///     SQL Server 方言の DSL→SQL 翻訳の実 DB 検証用</item>
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
            RootNamespace = NamespaceName,
            OutputFileName = "GeneratedFixture.g.cs",
            GenerateEditModels = true,
            GenerateMappers = true,
            GenerateRepositories = true,
            GenerateValueObjects = true,
            GenerateEfCoreRepositories = true,
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
    private static readonly Guid OrderOrderedAtColId = new("22222222-0000-0000-0000-000000000006");
    private static readonly Guid OrderDeliveryDateColId = new(
        "22222222-0000-0000-0000-000000000007"
    );

    private static readonly Guid ProfileId = new("33333333-0000-0000-0000-000000000001");
    private static readonly Guid ProfilePkColId = new("33333333-0000-0000-0000-000000000002");
    private static readonly Guid ProfileCustomerFkColId = new(
        "33333333-0000-0000-0000-000000000003"
    );
    private static readonly Guid ProfileBioColId = new("33333333-0000-0000-0000-000000000004");
    private static readonly Guid ProfileCustomerUniqueId = new(
        "33333333-0000-0000-0000-000000000005"
    );
    private static readonly Guid ProfileCustomerBioUniqueId = new(
        "33333333-0000-0000-0000-000000000006"
    );

    private static readonly Guid RelCustomerOrders = new("44444444-0000-0000-0000-000000000001");
    private static readonly Guid RelCustomerProfile = new("44444444-0000-0000-0000-000000000002");

    // 名前付きクエリの ID も決定的でなければならないため固定 GUID を用いる
    private static readonly Guid QuerySearchMemoContains = new(
        "55555555-0000-0000-0000-000000000001"
    );
    private static readonly Guid QueryGetMissingMemo = new("55555555-0000-0000-0000-000000000002");
    private static readonly Guid QueryGetExpensive = new("55555555-0000-0000-0000-000000000003");

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
                // EditModel のロードが「文字列往復で秒未満・DateTimeKind を落とさない」ことを実型で検証するための列。
                // 既存シード（INSERT 側は列を指定しない）へ影響しないよう NULL 許容にする
                new Column
                {
                    Id = OrderOrderedAtColId,
                    Name = "ordered_at",
                    DataType = "datetime2",
                    IsNullable = true,
                },
                // 日付のみ（date）の列。EditModel の表示文字列が時刻部（"0:00:00"）を含まないことを実型で検証するための列。
                // 既存シード（INSERT 側は列を指定しない）へ影響しないよう NULL 許容にする
                new Column
                {
                    Id = OrderDeliveryDateColId,
                    Name = "delivery_date",
                    DataType = "date",
                    IsNullable = true,
                },
            },
        };

        var profile = new Entity
        {
            Id = ProfileId,
            TableName = "customer_profiles",
            // UNIQUE 制約は重複事前チェック（CheckUniquenessAsync / ValidateUniqueAsync / コレクション内検証）の
            // 生成カバレッジ用。1対1 の子（customer_id が実質一意）へ置くことで、既存の実 DB テストのデータでは
            // 決して違反せず、生成物にだけ単一列制約（実名）と複合制約（名前なし＝合成名）の双方が現れる
            UniqueConstraints =
            {
                new UniqueConstraint
                {
                    Id = ProfileCustomerUniqueId,
                    Name = "UQ_customer_profiles_customer_id",
                    ColumnIds = { ProfileCustomerFkColId },
                },
                new UniqueConstraint
                {
                    Id = ProfileCustomerBioUniqueId,
                    ColumnIds = { ProfileCustomerFkColId, ProfileBioColId },
                },
            },
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
                    ColumnPairs = [new(CustomerPkColId, OrderCustomerFkColId)],
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
                    ColumnPairs = [new(CustomerPkColId, ProfileCustomerFkColId)],
                    ConstraintName = "FK_customer_profiles_customers",
                    OnDelete = ForeignKeyReferentialAction.NoAction,
                    OnUpdate = ForeignKeyReferentialAction.NoAction,
                },
            },
        };

        // 名前付きクエリ（ミニ DSL）: SQL Server 方言の DSL→SQL 翻訳
        // （CONTAINS→LIKE エスケープ・IS NULL・decimal 比較＝VO 比較）を実 DB で検証するための定義。
        // すべて DSL（共有本体）のため QuickER・EF Core 両実装へ同一テキストで出力され、manual 実装は不要
        diagram.Queries.Add(
            new QueryDefinition
            {
                Id = QuerySearchMemoContains,
                EntityId = OrderId,
                Name = "SearchMemoContains",
                Description =
                    "メモの部分一致（CONTAINS→LIKE。%・_ 等はリテラル扱い）で注文を検索する",
                Returns = QueryReturnShape.List,
                Parameters =
                {
                    new QueryParameter { Name = "keyword", Type = "string(50)" },
                },
                Condition = "memo CONTAINS @keyword",
                OrderBy = { new QueryOrdering { ColumnId = OrderPkColId } },
            }
        );

        diagram.Queries.Add(
            new QueryDefinition
            {
                Id = QueryGetMissingMemo,
                EntityId = OrderId,
                Name = "GetMissingMemo",
                Description = "メモ未設定（IS NULL）の注文を検索する",
                Returns = QueryReturnShape.List,
                Condition = "memo IS NULL",
                OrderBy = { new QueryOrdering { ColumnId = OrderPkColId } },
            }
        );

        diagram.Queries.Add(
            new QueryDefinition
            {
                Id = QueryGetExpensive,
                EntityId = OrderId,
                Name = "GetExpensive",
                Description = "金額（decimal・VO 列）が下限以上の注文を検索する",
                Returns = QueryReturnShape.List,
                Parameters =
                {
                    new QueryParameter { Name = "minAmount", Type = "decimal(10,2)" },
                },
                Condition = "amount >= @minAmount",
                OrderBy = { new QueryOrdering { ColumnId = OrderPkColId } },
            }
        );

        return diagram;
    }
}
