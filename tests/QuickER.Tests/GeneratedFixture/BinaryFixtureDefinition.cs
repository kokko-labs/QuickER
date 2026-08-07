using QuickER.CodeGen.CSharp;
using QuickER.Model;

namespace QuickER.Tests.GeneratedBinaryFixture;

/// <summary>
/// 無制限バイナリ列の除外（<see cref="CodeGenerationOptions.ExcludeUnboundedBinaryColumns"/>）の固定フィクスチャを
/// 生成する単一ソース。
/// </summary>
/// <remarks>
/// <para>
/// 独立した図（他フィクスチャと共有しない）で、無制限バイナリ列の全経路（QuickER の SQLite Repository・EF Core・
/// インメモリ・リモートサービス）を 1 つのアセンブリで検証できるよう、オプションを併存させる。
/// エンティティ <c>documents</c> は無制限バイナリ列（<c>payload</c>＝nullable・<c>thumb</c>＝非 nullable）と、
/// 除外対象外のバイナリ（<c>checksum</c>＝有界 <c>varbinary(16)</c>・<c>row_ver</c>＝<c>rowversion</c>）を併せ持ち、
/// 「除外される列」と「されない列」の両方を 1 図に含める。子 <c>document_notes</c> は Include・グラフ保存カスケードの検証用。
/// </para>
/// <para>
/// 値オブジェクトは OFF（シンプル優先）。バイナリ列を素の <c>byte[]</c> のまま扱うことで、除外の意味論
/// （null / 空配列の未取得状態・更新ガード・生 SQL の opportunistic マップ）を素直に検証できる。
/// <c>row_ver</c>（<c>rowversion</c>）は store-generated 列として <c>[StoreGeneratedColumn]</c> が付与され、
/// QuickER の INSERT / BulkInsert / UPDATE の対象から外れる（DB が採番するため。SELECT では取得する）。EF Core も
/// Fluent の <c>IsRowVersion()</c> で store-generated として扱う。nullable にする＝SQLite/EF Core は <c>rowversion</c> を
/// 自動採番しないため、INSERT で列が省略されても NULL のまま成立させる。
/// 文字列列は Unicode（<c>nvarchar</c>）で統一する（可搬フィクスチャの不変条件）。
/// </para>
/// <para>
/// 名前付きクエリ 3 本で除外列まわりの DSL 経路を網羅する:
/// <list type="bullet">
///   <item><c>GetPayloads</c>（射影）: 除外列 <c>payload</c> を射影が参照＝サーバー側刈り込み SELECT に含まれ値が取れる</item>
///   <item><c>GetByTitle</c>（一覧）: 全エンティティ取得で除外が効き <c>payload</c> は null</item>
///   <item><c>CountWithPayload</c>（件数）: 除外列 <c>payload</c> への WHERE 参照（<c>IS NOT NULL</c>）が動く</item>
/// </list>
/// </para>
/// </remarks>
public static class BinaryFixtureDefinition
{
    /// <summary>生成フィクスチャの名前空間（既存フィクスチャと衝突しない専用 namespace）</summary>
    public const string NamespaceName = "QuickER.Tests.GeneratedBinaryFixture";

    /// <summary>コミット済みフィクスチャファイル名（本体）</summary>
    public const string OutputFileName = "BinaryFixture.g.cs";

    /// <summary>コミット済みフィクスチャファイル名（サーバー実装）</summary>
    public const string ServerOutputFileName = "BinaryFixture.RemoteServer.g.cs";

    /// <summary>
    /// フィクスチャ生成に用いる決定的なオプション。
    /// SQLite 方言のQuickER 版 Repository・EF Core・インメモリ・リモートサービスを併存させ、無制限バイナリ除外を有効にする。
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
            GenerateEfCore = true,
            GenerateInMemoryRepositories = true,
            GenerateRemoteServices = true,
            RepositoryDialects = ["sqlite"],
            ExcludeUnboundedBinaryColumns = true,
            SplitFilesByCategory = false,
        };

    // 図の要素 ID は決定的でなければ再生成時に差分が出るため、固定 GUID を用いる。
    private static readonly Guid DocumentId = new("d0000000-0000-0000-0000-000000000001");
    private static readonly Guid DocumentPkColId = new("d0000000-0000-0000-0000-000000000002");
    private static readonly Guid DocumentTitleColId = new("d0000000-0000-0000-0000-000000000003");
    private static readonly Guid DocumentPayloadColId = new("d0000000-0000-0000-0000-000000000004");
    private static readonly Guid DocumentThumbColId = new("d0000000-0000-0000-0000-000000000005");
    private static readonly Guid DocumentChecksumColId = new(
        "d0000000-0000-0000-0000-000000000006"
    );
    private static readonly Guid DocumentRowVerColId = new("d0000000-0000-0000-0000-000000000007");
    private static readonly Guid DocumentIsPublishedColId = new(
        "d0000000-0000-0000-0000-000000000008"
    );

    private static readonly Guid NoteId = new("e0000000-0000-0000-0000-000000000001");
    private static readonly Guid NotePkColId = new("e0000000-0000-0000-0000-000000000002");
    private static readonly Guid NoteDocumentFkColId = new("e0000000-0000-0000-0000-000000000003");
    private static readonly Guid NoteTextColId = new("e0000000-0000-0000-0000-000000000004");

    private static readonly Guid RelDocumentNotes = new("f0000000-0000-0000-0000-000000000001");

    private static readonly Guid QueryGetPayloads = new("c0000000-0000-0000-0000-000000000001");
    private static readonly Guid QueryGetByTitle = new("c0000000-0000-0000-0000-000000000002");
    private static readonly Guid QueryCountWithPayload = new(
        "c0000000-0000-0000-0000-000000000003"
    );

    /// <summary>
    /// 無制限バイナリ除外の検証用 ER 図を決定的に構築する（要素 ID は固定 GUID・型は SQL Server 表記）。
    /// </summary>
    public static ErDiagram Build()
    {
        var document = new Entity
        {
            Id = DocumentId,
            TableName = "documents",
            Columns =
            {
                new Column
                {
                    Id = DocumentPkColId,
                    Name = "document_id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Id = DocumentTitleColId,
                    Name = "title",
                    DataType = "nvarchar(50)",
                    IsNullable = false,
                },
                // 素の bool 列（bit・非 nullable）。翻訳器の bool 短縮分岐（[col]=1 / [col]=0）の実 DB 検証用。
                // VO 化フィクスチャ（GeneratedFixture 等）では素の bool に構造的に到達できないため、
                // raw 型で生成される本フィクスチャ（SQLite）と SqlServerBinaryFixture（SQL Server）が唯一の担い手
                new Column
                {
                    Id = DocumentIsPublishedColId,
                    Name = "is_published",
                    DataType = "bit",
                    IsNullable = false,
                },
                // 無制限バイナリ（varbinary(max)・nullable）＝除外対象。nullable プロパティは未取得状態が null
                new Column
                {
                    Id = DocumentPayloadColId,
                    Name = "payload",
                    DataType = "varbinary(max)",
                    IsNullable = true,
                },
                // 無制限バイナリ（varbinary(max)・非 nullable）＝除外対象。非 nullable プロパティは未取得状態が空配列
                new Column
                {
                    Id = DocumentThumbColId,
                    Name = "thumb",
                    DataType = "varbinary(max)",
                    IsNullable = false,
                },
                // 有界バイナリ（varbinary(16)）＝除外対象外の検証用
                new Column
                {
                    Id = DocumentChecksumColId,
                    Name = "checksum",
                    DataType = "varbinary(16)",
                    IsNullable = true,
                },
                // rowversion ＝store-generated 列（[StoreGeneratedColumn]）。QuickER も EF Core も INSERT / UPDATE から除外し DB が採番する。
                // DB 側自動採番のため nullable（SQLite/EF Core は自動採番しないので INSERT 省略で NULL のまま）
                new Column
                {
                    Id = DocumentRowVerColId,
                    Name = "row_ver",
                    DataType = "rowversion",
                    IsNullable = true,
                },
            },
        };

        var note = new Entity
        {
            Id = NoteId,
            TableName = "document_notes",
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
                    Id = NoteDocumentFkColId,
                    Name = "document_id",
                    DataType = "int",
                    IsForeignKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Id = NoteTextColId,
                    Name = "note",
                    DataType = "nvarchar(100)",
                    IsNullable = false,
                },
            },
        };

        var diagram = new ErDiagram
        {
            Entities = { document, note },
            Relationships =
            {
                // 1対多: documents -> document_notes（ON DELETE CASCADE）
                new Relationship
                {
                    Id = RelDocumentNotes,
                    Type = RelationshipType.OneToMany,
                    SourceEntityId = DocumentId,
                    TargetEntityId = NoteId,
                    ColumnPairs = [new(DocumentPkColId, NoteDocumentFkColId)],
                    ConstraintName = "FK_document_notes_documents",
                    OnDelete = ForeignKeyReferentialAction.Cascade,
                    OnUpdate = ForeignKeyReferentialAction.NoAction,
                },
            },
        };

        // 1. 射影（Dsl）: 除外列 payload を射影フィールドが参照＝サーバー側刈り込み SELECT に含まれ値が取れる
        diagram.Queries.Add(
            new QueryDefinition
            {
                Id = QueryGetPayloads,
                EntityId = DocumentId,
                Name = "GetPayloads",
                Description =
                    "文書 ID と本体バイナリ（除外列 payload）を射影で取得する（文書 ID 昇順）",
                Returns = QueryReturnShape.Projection,
                ResultTypeName = "DocumentPayloadRow",
                Fields =
                {
                    new ProjectionField { Name = "DocumentId", SourceColumnId = DocumentPkColId },
                    new ProjectionField { Name = "Payload", SourceColumnId = DocumentPayloadColId },
                },
                OrderBy = { new QueryOrdering { ColumnId = DocumentPkColId } },
            }
        );

        // 2. 一覧（Dsl・title 条件）: 全エンティティ取得で除外が効き payload は null
        diagram.Queries.Add(
            new QueryDefinition
            {
                Id = QueryGetByTitle,
                EntityId = DocumentId,
                Name = "GetByTitle",
                Description = "タイトル完全一致で文書を取得する（除外列 payload は取得されない）",
                Returns = QueryReturnShape.List,
                Parameters =
                {
                    new QueryParameter { Name = "title", Type = "string(50)" },
                },
                Condition = "title = @title",
            }
        );

        // 3. 件数（Dsl・payload IS NOT NULL）: 除外列への WHERE 参照が動く
        diagram.Queries.Add(
            new QueryDefinition
            {
                Id = QueryCountWithPayload,
                EntityId = DocumentId,
                Name = "CountWithPayload",
                Description = "本体バイナリ（payload）が存在する文書の件数を取得する",
                Returns = QueryReturnShape.Count,
                Condition = "payload IS NOT NULL",
            }
        );

        return diagram;
    }
}
