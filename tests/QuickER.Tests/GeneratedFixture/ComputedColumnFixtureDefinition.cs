using System;
using System.Collections.Generic;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;

namespace QuickER.Tests.GeneratedComputedColumnFixture;

/// <summary>
/// 計算列・生成列（<see cref="Column.IsComputed"/>）を持つ図を「SQL Server ＋ SQLite のマルチターゲット」で
/// 生成するフィクスチャの単一ソース。
/// </summary>
/// <remarks>
/// <para>
/// 固定する対象は「計算列がマーカー属性 <c>[ComputedColumn]</c> を持ち、<c>EntitySaveMetadata</c> が
/// INSERT / BulkInsert / UPDATE の対象から外す（SELECT では読む）」という、<b>方言に依らない</b>規則。
/// 行バージョン列の除外が SQL Server だけの能力（<c>dialect_assigns_store_generated</c>）で切り替わるのに対し、
/// 計算列は列自身の事実なので両方言の実装で同じ扱いになる——その非対称を 1 アセンブリへ焼き付けるために
/// マルチターゲットで生成する。
/// </para>
/// <para>
/// 図は 2 テーブルで、計算列の NULL 許容性の両アームを持つ:
/// </para>
/// <list type="bullet">
///   <item>
///     <c>items</c>: <c>item_id</c>（int・PK）／<c>qty</c>（int・NOT NULL）／
///     <c>price</c>（decimal(18,2)・NOT NULL）／<c>total</c>（decimal(21,2)・<b>NULL 許容</b>の計算列）
///   </item>
///   <item>
///     <c>temporal_items</c>: <c>id</c>（int・PK）／<c>name</c>（nvarchar(50)・NOT NULL）／
///     <c>period_start</c>・<c>period_end</c>（datetime2・<b>NOT NULL</b> の計算列）＝
///     SQL Server のシステムバージョン管理テーブル（テンポラルテーブル）の期間列に対応する形。
///     取込は期間列（<c>generated_always_type</c> が 1/2）に <see cref="Column.IsComputed"/> を立てるが、
///     期間列は常に NOT NULL なので「非 NULL の値型の計算列」がここで初めて生成物に現れる
///     （Mapper の <c>ApplyToEntity</c> が <c>Nullable&lt;DateTime&gt;</c> を素で代入すると CS0266 になる）。
///   </item>
/// </list>
/// <para>
/// EditModel / Mapper も生成する（計算列は入力必須にならず、未入力なら実体の現在値を保つ＝
/// 行バージョン列と同じ規則が EditModel 側でも効くことを固定するため）。
/// VO・EF Core は交差の焦点でないため生成しない（EF Core はマルチターゲットと排他でもある）。
/// </para>
/// </remarks>
public static class ComputedColumnFixtureDefinition
{
    /// <summary>生成フィクスチャの契約 namespace（既存フィクスチャと衝突しない専用 namespace）</summary>
    public const string NamespaceName = "QuickER.Tests.GeneratedComputedColumnFixture";

    /// <summary>コミット済みフィクスチャファイル名</summary>
    public const string OutputFileName = "ComputedColumnFixture.g.cs";

    /// <summary>
    /// フィクスチャ生成に用いる決定的なオプション。
    /// SQL Server / SQLite のQuickER 版 Repository を同時生成する（EF Core は併用不可）。
    /// </summary>
    public static CodeGenerationOptions Options { get; } =
        new()
        {
            RootNamespace = NamespaceName,
            OutputFileName = OutputFileName,
            GenerateEditModels = true,
            GenerateMappers = true,
            GenerateRepositories = true,
            GenerateValueObjects = false,
            GenerateEfCoreRepositories = false,
            RepositoryDialects = ["sqlserver", "sqlite"],
            SplitFilesByCategory = false,
        };

    // 図の要素 ID は決定的でなければ再生成時に差分が出るため、固定 GUID を用いる。
    private static readonly Guid ItemEntityId = new("c1000000-0000-0000-0000-000000000001");
    private static readonly Guid ItemPkColId = new("c1000000-0000-0000-0000-000000000002");
    private static readonly Guid ItemQtyColId = new("c1000000-0000-0000-0000-000000000003");
    private static readonly Guid ItemPriceColId = new("c1000000-0000-0000-0000-000000000004");
    private static readonly Guid ItemTotalColId = new("c1000000-0000-0000-0000-000000000005");
    private static readonly Guid TemporalEntityId = new("c1000000-0000-0000-0000-000000000011");
    private static readonly Guid TemporalIdColId = new("c1000000-0000-0000-0000-000000000012");
    private static readonly Guid TemporalNameColId = new("c1000000-0000-0000-0000-000000000013");
    private static readonly Guid TemporalStartColId = new("c1000000-0000-0000-0000-000000000014");
    private static readonly Guid TemporalEndColId = new("c1000000-0000-0000-0000-000000000015");

    /// <summary>計算列の検証用 ER 図を決定的に構築する（型は SQL Server 表記）</summary>
    public static ErDiagram Build()
    {
        var item = new Entity
        {
            Id = ItemEntityId,
            TableName = "items",
            Columns =
            {
                new Column
                {
                    Id = ItemPkColId,
                    Name = "item_id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Id = ItemQtyColId,
                    Name = "qty",
                    DataType = "int",
                    IsNullable = false,
                },
                new Column
                {
                    Id = ItemPriceColId,
                    Name = "price",
                    DataType = "decimal(18,2)",
                    IsNullable = false,
                },
                // 計算列。式（qty * price）は意味モデルに載らないため、運ぶのは
                // 「書き込みを受け付けない」という事実だけ。実 DB のスキーマはテスト側の DDL が作る
                new Column
                {
                    Id = ItemTotalColId,
                    Name = "total",
                    DataType = "decimal(21,2)",
                    IsNullable = true,
                    IsComputed = true,
                },
            },
        };

        // テンポラルテーブルの本表。期間列は DB が採番する NOT NULL の計算列で、
        // 取込（SqlServerSchemaImporter）が generated_always_type から IsComputed を立てる形をそのまま写す
        var temporal = new Entity
        {
            Id = TemporalEntityId,
            TableName = "temporal_items",
            Columns =
            {
                new Column
                {
                    Id = TemporalIdColId,
                    Name = "id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Id = TemporalNameColId,
                    Name = "name",
                    DataType = "nvarchar(50)",
                    IsNullable = false,
                },
                new Column
                {
                    Id = TemporalStartColId,
                    Name = "period_start",
                    DataType = "datetime2",
                    IsNullable = false,
                    IsComputed = true,
                },
                new Column
                {
                    Id = TemporalEndColId,
                    Name = "period_end",
                    DataType = "datetime2",
                    IsNullable = false,
                    IsComputed = true,
                },
            },
        };

        return new ErDiagram { TargetDbms = "sqlserver", Entities = { item, temporal } };
    }

    /// <summary>
    /// 主辞書（図の方言＝SQL Server）と、実効方言（sqlserver / sqlite）ごとに解決した方言辞書を返す。
    /// マルチ辞書オーバーロードの入力に使う。
    /// </summary>
    public static (
        IReadOnlyDictionary<Guid, CSharpTypeInfo> Primary,
        IReadOnlyDictionary<string, IReadOnlyDictionary<Guid, CSharpTypeInfo>> ByDialect
    ) ResolveColumnTypes(ErDiagram diagram)
    {
        var primary = SqlServerCSharpTypeMapper.ResolveColumnTypes(diagram);
        var byDialect = new Dictionary<string, IReadOnlyDictionary<Guid, CSharpTypeInfo>>(
            StringComparer.OrdinalIgnoreCase
        )
        {
            ["sqlserver"] = primary,
            ["sqlite"] = SqliteCSharpTypeMapper.ResolveColumnTypes(diagram),
        };

        return (primary, byDialect);
    }
}
