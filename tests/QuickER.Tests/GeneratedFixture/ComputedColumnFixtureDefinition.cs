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
/// 図は 1 テーブルの最小構成:
/// </para>
/// <list type="bullet">
///   <item>
///     <c>items</c>: <c>item_id</c>（int・PK）／<c>qty</c>（int・NOT NULL）／
///     <c>price</c>（decimal(18,2)・NOT NULL）／<c>total</c>（decimal(21,2)・計算列）
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

        return new ErDiagram { TargetDbms = "sqlserver", Entities = { item } };
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
