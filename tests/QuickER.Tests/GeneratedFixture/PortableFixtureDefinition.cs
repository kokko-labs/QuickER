using QuickER.CodeGen.CSharp;
using QuickER.Model;

namespace QuickER.Tests.GeneratedPortableFixture;

/// <summary>方言ランタイムテストで DDL の型表記を切り替えるための対象方言</summary>
public enum PortableDialect
{
    /// <summary>SQL Server（コミット済み <c>PortableFixture.g.cs</c> の基準方言）</summary>
    SqlServer,

    /// <summary>PostgreSQL</summary>
    PostgreSql,

    /// <summary>MySQL</summary>
    MySql,

    /// <summary>Oracle</summary>
    Oracle,
}

/// <summary>
/// 方言可搬な実行時テスト用の「第2の固定フィクスチャ」を生成する単一ソース。
/// </summary>
/// <remarks>
/// <para>
/// 既存の <c>GeneratedFixtureDefinition</c> は SQL Server 方言（FOR JSON・SQL Server 固有型）を前提とするため、
/// PostgreSQL / MySQL / Oracle では動かない。本フィクスチャは
/// <b>4 方言の型マッパがすべて同じ C# 型へ解決する型のみ</b>で構成した
/// 小さな図（2 エンティティ・1対多カスケード・VO 有効）で、方言非依存の生成物が実 DB で動くことを証明する。
/// </para>
/// <para>
/// 可搬型セット（各方言の DDL 往復統合テストで実証済みの型から選択）:
/// <list type="bullet">
///   <item>整数（PK/FK）: SqlServer <c>int</c> / PG <c>integer</c> / MySQL <c>int</c> / Oracle <c>NUMBER(10)</c> → すべて <c>int</c>（正規型 <c>int32</c>）</item>
///   <item>文字列: SqlServer <c>nvarchar(50)</c> / PG・MySQL <c>varchar(50)</c> / Oracle <c>NVARCHAR2(50)</c> → すべて <c>string</c>（MaxLength 50・正規型 <c>string(50)</c>）</item>
///   <item>固定小数: <c>decimal(10,2)</c>（PG <c>numeric(10,2)</c> / Oracle <c>NUMBER(10,2)</c>）→ すべて <c>decimal(10,2)</c>（正規型 <c>decimal(10,2)</c>）</item>
/// </list>
/// bool は Oracle が <c>NUMBER(1)</c> 事情を持ち方言差が大きいため<b>意図的に除外</b>する。
/// </para>
/// <para>
/// オプションは「Entity/EditModel/Mapper・VO 有効・<b>EF Core 単独出力（QuickER の SQL Server 実装なし）</b>・単一ファイル・専用 namespace」。
/// 本フィクスチャの方言ランタイムテストは EF Core しか使わないため、GenerateRepositories=false（EF Core 単独）に切り替え、
/// 新モード（EF Core 単独出力）の実 DB 実証を兼ねる。namespace は既存フィクスチャ（<c>QuickER.Tests.GeneratedFixture</c>）と衝突させない。
/// </para>
/// </remarks>
public static class PortableFixtureDefinition
{
    /// <summary>生成フィクスチャの名前空間（既存フィクスチャと衝突しない専用 namespace）</summary>
    public const string NamespaceName = "QuickER.Tests.GeneratedPortableFixture";

    /// <summary>コミット済みフィクスチャファイル名</summary>
    public const string OutputFileName = "PortableFixture.g.cs";

    /// <summary>フィクスチャ生成に用いる決定的なオプション（Entity/EditModel/Mapper・VO 有効・EF Core 単独出力・単一ファイル）</summary>
    public static CodeGenerationOptions Options { get; } =
        new()
        {
            NamespaceName = NamespaceName,
            OutputFileName = OutputFileName,
            GenerateEntityClasses = true,
            GenerateEditModels = true,
            GenerateMappers = true,
            GenerateRepositories = false,
            GenerateValueObjects = true,
            GenerateEfCore = true,
            SplitFilesByCategory = false,
        };

    // 図の要素 ID は決定的でなければ再生成時に差分が出るため、固定 GUID を用いる。
    private static readonly Guid CustomerId = new("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid CustomerPkColId = new("aaaaaaaa-0000-0000-0000-000000000002");
    private static readonly Guid CustomerNameColId = new("aaaaaaaa-0000-0000-0000-000000000003");
    private static readonly Guid CustomerBalanceColId = new("aaaaaaaa-0000-0000-0000-000000000004");

    private static readonly Guid OrderId = new("bbbbbbbb-0000-0000-0000-000000000001");
    private static readonly Guid OrderPkColId = new("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid OrderCustomerFkColId = new("bbbbbbbb-0000-0000-0000-000000000003");
    private static readonly Guid OrderMemoColId = new("bbbbbbbb-0000-0000-0000-000000000004");
    private static readonly Guid OrderAmountColId = new("bbbbbbbb-0000-0000-0000-000000000005");

    private static readonly Guid RelCustomerOrders = new("cccccccc-0000-0000-0000-000000000001");

    /// <summary>方言ごとの可搬型表記（整数・文字列・固定小数）を返す</summary>
    /// <remarks>
    /// 文字列は Unicode 可変長で統一する（SqlServer <c>nvarchar</c> / Oracle <c>NVARCHAR2</c>・PG/MySQL の <c>varchar</c> は
    /// 既定で Unicode）。各方言の型カタログはこれらをすべて正規型 <c>String</c> へ解析するため、DB 定義メタ属性の
    /// 中立トークンが全方言で <c>string(50)</c> に揃い、EF Core 単独出力の方言可搬性（バイト一致）が保たれる。
    /// 非 Unicode の <c>varchar</c>（SqlServer/Oracle は AnsiString）を使うと方言間でトークンが割れるため用いない。
    /// </remarks>
    private static (string Int, string Varchar50, string Decimal) TypesFor(
        PortableDialect dialect
    ) =>
        dialect switch
        {
            PortableDialect.SqlServer => ("int", "nvarchar(50)", "decimal(10,2)"),
            PortableDialect.PostgreSql => ("integer", "varchar(50)", "numeric(10,2)"),
            PortableDialect.MySql => ("int", "varchar(50)", "decimal(10,2)"),
            PortableDialect.Oracle => ("NUMBER(10)", "NVARCHAR2(50)", "NUMBER(10,2)"),
            _ => throw new ArgumentOutOfRangeException(nameof(dialect)),
        };

    /// <summary>
    /// 方言可搬な ER 図を決定的に構築する（要素 ID は固定 GUID・日本語識別子なし）。
    /// </summary>
    /// <remarks>
    /// 型表記のみ <paramref name="dialect"/> で切り替える。4 方言の型マッパはこれらを同じ C# 型へ解決するため、
    /// どの方言で生成しても C# 出力は一致する（<c>PortableFixtureDialectIndependenceTests</c> が保証）。
    /// </remarks>
    public static ErDiagram Build(PortableDialect dialect = PortableDialect.SqlServer)
    {
        var (intType, varchar50, decimalType) = TypesFor(dialect);

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
                    DataType = intType,
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Id = CustomerNameColId,
                    Name = "name",
                    DataType = varchar50,
                    IsNullable = false,
                },
                new Column
                {
                    Id = CustomerBalanceColId,
                    Name = "balance",
                    DataType = decimalType,
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
                    DataType = intType,
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Id = OrderCustomerFkColId,
                    Name = "customer_id",
                    DataType = intType,
                    IsForeignKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Id = OrderMemoColId,
                    Name = "memo",
                    DataType = varchar50,
                    IsNullable = true,
                },
                new Column
                {
                    Id = OrderAmountColId,
                    Name = "amount",
                    DataType = decimalType,
                    IsNullable = false,
                },
            },
        };

        return new ErDiagram
        {
            Entities = { customer, order },
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
            },
        };
    }
}
