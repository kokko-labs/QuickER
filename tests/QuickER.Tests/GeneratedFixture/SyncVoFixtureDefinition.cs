using System;
using System.Collections.Generic;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Sqlite;
using QuickER.SqlServer;

namespace QuickER.Tests.GeneratedSyncVoFixture;

/// <summary>
/// 双方向同期支援（<see cref="CodeGenerationOptions.GenerateSyncSupport"/>）× 値オブジェクト生成の
/// 交差を固定するフィクスチャの単一ソース。
/// </summary>
/// <remarks>
/// <para>
/// 同期の生成式（ミラー版の読み書き・キー整形・キー解析）は、値オブジェクトが有効なときだけ VO 型を
/// 経由する形になる。<see cref="SyncFixtureDefinition"/> は VO 無効のためその形を一度も通らず、交差は
/// コンパイル検証だけで守られていた。ここは<b>実行時</b>に通す最小の図を用意する。
/// </para>
/// <para>
/// 行バージョン列は <b>NOT NULL</b> で置く（実 SQL Server の <c>rowversion</c> と同じ宣言）。これが
/// 「宣言は NOT NULL・実体は null」が構造的に起きる側で、ミラー版の読み出しを列宣言で出し分けると
/// NullReferenceException になる:
/// </para>
/// <list type="bullet">
///   <item>マルチターゲットの方言変換はローカル（SQLite）側の行バージョン列を意図的に NULL 許容へ落とす（未同期行の版は空）</item>
///   <item>グラフ削除の記録は DB を経由せずインメモリ実体から読むため、キーだけのスタブでは列宣言に関わらず未設定</item>
/// </list>
/// <para>
/// 図は親子 2 テーブルの最小構成（カスケード削除の記録＝スタブ経路を通すため子が要る）:
/// </para>
/// <list type="bullet">
///   <item><c>syncvo_orders</c>: <c>order_id</c>（int・PK）／<c>title</c>（nvarchar(50)）／<c>row_ver</c>（rowversion・NOT NULL）</item>
///   <item><c>syncvo_lines</c>: <c>line_id</c>（int・PK）／<c>order_id</c>（int・FK）／<c>qty</c>（int）／<c>row_ver</c>（rowversion・NOT NULL）</item>
/// </list>
/// <para>
/// EditModel・Mapper・EF Core・リモートサービス・無制限バイナリ列の除外は交差の焦点でないため生成しない
/// （EF Core はマルチターゲットと排他でもある）。
/// </para>
/// </remarks>
public static class SyncVoFixtureDefinition
{
    /// <summary>生成フィクスチャの契約 namespace（既存フィクスチャと衝突しない専用 namespace）</summary>
    public const string NamespaceName = "QuickER.Tests.GeneratedSyncVoFixture";

    /// <summary>コミット済みフィクスチャファイル名</summary>
    public const string OutputFileName = "SyncVoFixture.g.cs";

    /// <summary>
    /// フィクスチャ生成に用いる決定的なオプション。
    /// SQL Server（サーバー）／SQLite（ローカル）のQuickER 版 Repository ＋ 同期支援 ＋ 値オブジェクト。
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
            GenerateEfCoreRepositories = false,
            GenerateSyncSupport = true,
            RepositoryDialects = ["sqlserver", "sqlite"],
            SplitFilesByCategory = false,
        };

    // 図の要素 ID は決定的でなければ再生成時に差分が出るため、固定 GUID を用いる。
    private static readonly Guid OrderEntityId = new("c2000000-0000-0000-0000-000000000001");
    private static readonly Guid OrderPkColId = new("c2000000-0000-0000-0000-000000000002");
    private static readonly Guid OrderTitleColId = new("c2000000-0000-0000-0000-000000000003");
    private static readonly Guid OrderRowVerColId = new("c2000000-0000-0000-0000-000000000004");
    private static readonly Guid LineEntityId = new("c2000000-0000-0000-0000-000000000011");
    private static readonly Guid LinePkColId = new("c2000000-0000-0000-0000-000000000012");
    private static readonly Guid LineOrderColId = new("c2000000-0000-0000-0000-000000000013");
    private static readonly Guid LineQtyColId = new("c2000000-0000-0000-0000-000000000014");
    private static readonly Guid LineRowVerColId = new("c2000000-0000-0000-0000-000000000015");
    private static readonly Guid OrderLineRelationshipId = new(
        "c2000000-0000-0000-0000-000000000021"
    );

    /// <summary>同期支援 × 値オブジェクトの検証用 ER 図を決定的に構築する（型は SQL Server 表記）</summary>
    public static ErDiagram Build()
    {
        var order = new Entity
        {
            Id = OrderEntityId,
            TableName = "syncvo_orders",
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
                    Id = OrderTitleColId,
                    Name = "title",
                    DataType = "nvarchar(50)",
                    IsNullable = false,
                },
                // 実 SQL Server の rowversion に合わせ NOT NULL（DB が必ず採番する）。
                // SQLite へ方言変換すると BLOB かつ NULL 許容になる（未同期の行は空になるため）
                new Column
                {
                    Id = OrderRowVerColId,
                    Name = "row_ver",
                    DataType = "rowversion",
                    IsNullable = false,
                },
            },
        };

        var line = new Entity
        {
            Id = LineEntityId,
            TableName = "syncvo_lines",
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
                    Id = LineOrderColId,
                    Name = "order_id",
                    DataType = "int",
                    IsForeignKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Id = LineQtyColId,
                    Name = "qty",
                    DataType = "int",
                    IsNullable = false,
                },
                new Column
                {
                    Id = LineRowVerColId,
                    Name = "row_ver",
                    DataType = "rowversion",
                    IsNullable = false,
                },
            },
        };

        var relationship = new Relationship
        {
            Id = OrderLineRelationshipId,
            SourceEntityId = OrderEntityId,
            TargetEntityId = LineEntityId,
            Type = RelationshipType.OneToMany,
            ConstraintName = "FK_syncvo_lines_syncvo_orders",
            ColumnPairs = { new RelationshipColumnPair(OrderPkColId, LineOrderColId) },
        };

        return new ErDiagram
        {
            TargetDbms = "sqlserver",
            Entities = { order, line },
            Relationships = { relationship },
        };
    }

    /// <summary>図を SQLite 方言へ変換した複製を返す（ローカル DB 側のスキーマ＝<c>rowversion</c> は BLOB・NULL 許容）</summary>
    /// <remarks>
    /// 実運用の「SQL Server の図をローカル用に方言切替する」手順と同じ <see cref="DiagramTypeConverter"/> を通す。
    /// SQLite 実 DB テストのスキーマ作成にそのまま使う。
    /// </remarks>
    public static ErDiagram BuildSqliteMirror()
    {
        var diagram = Build();
        var plan = DiagramTypeConverter.CreatePlan(
            diagram,
            new SqlServerTypeCatalog(),
            new SqliteTypeCatalog()
        );
        DiagramTypeConverter.Apply(diagram, plan);
        diagram.TargetDbms = "sqlite";

        return diagram;
    }

    /// <summary>
    /// 主辞書（図の方言＝SQL Server）と、実効方言（sqlserver / sqlite）ごとに解決した方言辞書を返す。
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
