using System;
using System.Collections.Generic;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Sqlite;
using QuickER.SqlServer;

namespace QuickER.Tests.GeneratedSyncFixture;

/// <summary>
/// 双方向同期支援（<see cref="CodeGenerationOptions.GenerateSyncSupport"/>）の生成物を固定するフィクスチャの単一ソース。
/// </summary>
/// <remarks>
/// <para>
/// サーバー＝SQL Server・ローカル＝SQLite のハイブリッド構成で、同期エンジン・同期記述子・ジャーナル記録
/// デコレータ・直結差分ソース・DI 登録が生成されることを固定する。
/// </para>
/// <para>
/// 図は親子 2 テーブル（版あり）＋版なし子テーブル 1 つの最小構成（FK 順の検証を含めるため 1 テーブルでは
/// 足りず、版あり／版なしの混在＝後勝ちモードの検証には rowversion を持たないテーブルが要る）:
/// </para>
/// <list type="bullet">
///   <item><c>sync_orders</c>: <c>order_id</c>（int・PK）／<c>customer_name</c>（nvarchar(50)）／<c>attachment</c>（varbinary(max)・NULL 許容）／<c>row_ver</c>（rowversion）</item>
///   <item><c>sync_order_lines</c>: <c>line_id</c>（int・PK）／<c>order_id</c>（int・FK）／<c>product</c>（nvarchar(50)）／<c>row_ver</c>（rowversion）</item>
///   <item><c>sync_notes</c>: <c>note_id</c>（int・PK）／<c>order_id</c>（int・FK）／<c>body</c>（nvarchar(100)）＝<b>rowversion なし</b>（後勝ち専用・キー順全量ダウンロード・版ありの親を参照する混在 FK トポロジ）</item>
/// </list>
/// <para>
/// 無制限バイナリ列の除外（<see cref="CodeGenerationOptions.ExcludeUnboundedBinaryColumns"/>）を併用し、
/// <c>attachment</c> を「行の転送に載らない列」にする。除外列を持つテーブル（<c>sync_orders</c>）と持たない
/// テーブル（<c>sync_order_lines</c>）が同じエンジンに同居するため、コピーも洗い替えの損失ガードも
/// 「テーブル単位で効く」ことが同じ 1 つの図で確かめられる。除外列を使わない既存シナリオは、除外列が
/// 増えても行の同期に影響しないためそのまま通る。
/// </para>
/// <para>
/// 行バージョン列は <b>NULL 許容</b>で置く。ローカルで作られてまだ一度もアップロードされていない行には
/// ミラーすべきサーバー版が存在せず、そこが空であること自体が「未同期」という状態の表現になっている
/// （アンカー導出の <c>MAX</c> からも自然に外れる）。
/// </para>
/// <para>
/// VO・EditModel・Mapper・EF Core は交差の焦点でないため生成しない（EF Core はマルチターゲットと排他でもある）。
/// </para>
/// </remarks>
public static class SyncFixtureDefinition
{
    /// <summary>生成フィクスチャの契約 namespace（既存フィクスチャと衝突しない専用 namespace）</summary>
    public const string NamespaceName = "QuickER.Tests.GeneratedSyncFixture";

    /// <summary>コミット済みフィクスチャファイル名（本体）</summary>
    public const string OutputFileName = "SyncFixture.g.cs";

    /// <summary>コミット済みフィクスチャファイル名（リモートエンドポイント＝同期専用エンドポイントを含む）</summary>
    public const string RemoteServerOutputFileName = "SyncFixture.RemoteServer.g.cs";

    /// <summary>期待する出力ファイル構成（ファイル構成そのものもドリフト検知の対象）</summary>
    public static IReadOnlyList<string> OutputFileNames { get; } =
    [OutputFileName, RemoteServerOutputFileName];

    /// <summary>
    /// フィクスチャ生成に用いる決定的なオプション。
    /// SQL Server（サーバー）／SQLite（ローカル）のQuickER 版 Repository ＋ 同期支援を生成する。
    /// </summary>
    public static CodeGenerationOptions Options { get; } =
        new()
        {
            RootNamespace = NamespaceName,
            OutputFileName = OutputFileName,
            GenerateEditModels = false,
            GenerateMappers = false,
            GenerateRepositories = true,
            GenerateValueObjects = false,
            GenerateEfCore = false,
            GenerateSyncSupport = true,
            // 同期の 2 経路（直結・HTTP）を 1 つの生成物で覆う。リモートサービスを足すと同期専用エンドポイント
            // （サーバー側）と HTTP 差分ソース（クライアント側）が加わり、実行時テストは同じ型・同じシナリオを
            // 転送経路だけ差し替えて流せる（別フィクスチャに分けると型が別 namespace へ出て共有基底が書けない）。
            GenerateRemoteServices = true,
            RepositoryDialects = ["sqlserver", "sqlite"],
            // 同期 × 無制限バイナリ列の除外の交差。除外列は SELECT / UPDATE から外れ、blob は
            // ストリーミングアクセサ（と同期の IncludeUnboundedBinary）だけが運ぶ。
            ExcludeUnboundedBinaryColumns = true,
            SplitFilesByCategory = false,
        };

    // 図の要素 ID は決定的でなければ再生成時に差分が出るため、固定 GUID を用いる。
    private static readonly Guid OrderEntityId = new("c1000000-0000-0000-0000-000000000001");
    private static readonly Guid OrderPkColId = new("c1000000-0000-0000-0000-000000000002");
    private static readonly Guid OrderNameColId = new("c1000000-0000-0000-0000-000000000003");
    private static readonly Guid OrderRowVerColId = new("c1000000-0000-0000-0000-000000000004");
    private static readonly Guid OrderAttachmentColId = new("c1000000-0000-0000-0000-000000000005");
    private static readonly Guid LineEntityId = new("c1000000-0000-0000-0000-000000000011");
    private static readonly Guid LinePkColId = new("c1000000-0000-0000-0000-000000000012");
    private static readonly Guid LineOrderColId = new("c1000000-0000-0000-0000-000000000013");
    private static readonly Guid LineProductColId = new("c1000000-0000-0000-0000-000000000014");
    private static readonly Guid LineRowVerColId = new("c1000000-0000-0000-0000-000000000015");
    private static readonly Guid OrderLineRelationshipId = new(
        "c1000000-0000-0000-0000-000000000021"
    );
    private static readonly Guid NoteEntityId = new("c1000000-0000-0000-0000-000000000031");
    private static readonly Guid NotePkColId = new("c1000000-0000-0000-0000-000000000032");
    private static readonly Guid NoteOrderColId = new("c1000000-0000-0000-0000-000000000033");
    private static readonly Guid NoteBodyColId = new("c1000000-0000-0000-0000-000000000034");
    private static readonly Guid OrderNoteRelationshipId = new(
        "c1000000-0000-0000-0000-000000000041"
    );

    /// <summary>同期支援の検証用 ER 図を決定的に構築する（型は SQL Server 表記）</summary>
    public static ErDiagram Build()
    {
        var order = new Entity
        {
            Id = OrderEntityId,
            TableName = "sync_orders",
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
                    Id = OrderNameColId,
                    Name = "customer_name",
                    DataType = "nvarchar(50)",
                    IsNullable = false,
                },
                new Column
                {
                    Id = OrderAttachmentColId,
                    Name = "attachment",
                    DataType = "varbinary(max)",
                    IsNullable = true,
                },
                new Column
                {
                    Id = OrderRowVerColId,
                    Name = "row_ver",
                    DataType = "rowversion",
                    IsNullable = true,
                },
            },
        };

        var line = new Entity
        {
            Id = LineEntityId,
            TableName = "sync_order_lines",
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
                    Id = LineProductColId,
                    Name = "product",
                    DataType = "nvarchar(50)",
                    IsNullable = false,
                },
                new Column
                {
                    Id = LineRowVerColId,
                    Name = "row_ver",
                    DataType = "rowversion",
                    IsNullable = true,
                },
            },
        };

        // 版なしテーブル（rowversion 列なし）＝後勝ちモード専用。版ありの親（sync_orders）を参照する
        // 混在 FK トポロジで、キー順全量ダウンロードと FK 順の交差を 1 つの図で固定する
        var note = new Entity
        {
            Id = NoteEntityId,
            TableName = "sync_notes",
            Columns =
            {
                new Column
                {
                    Id = NotePkColId,
                    Name = "note_id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Id = NoteOrderColId,
                    Name = "order_id",
                    DataType = "int",
                    IsForeignKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Id = NoteBodyColId,
                    Name = "body",
                    DataType = "nvarchar(100)",
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
            ConstraintName = "FK_sync_order_lines_sync_orders",
            ColumnPairs = { new RelationshipColumnPair(OrderPkColId, LineOrderColId) },
        };

        var noteRelationship = new Relationship
        {
            Id = OrderNoteRelationshipId,
            SourceEntityId = OrderEntityId,
            TargetEntityId = NoteEntityId,
            Type = RelationshipType.OneToMany,
            ConstraintName = "FK_sync_notes_sync_orders",
            ColumnPairs = { new RelationshipColumnPair(OrderPkColId, NoteOrderColId) },
        };

        return new ErDiagram
        {
            TargetDbms = "sqlserver",
            Entities = { order, line, note },
            Relationships = { relationship, noteRelationship },
        };
    }

    /// <summary>図を SQLite 方言へ変換した複製を返す（ローカル DB 側のスキーマ＝<c>rowversion</c> は BLOB）</summary>
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
