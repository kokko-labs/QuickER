using System;
using System.Collections.Generic;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;

namespace QuickER.Tests.GeneratedSyncModelFixture;

/// <summary>
/// 同期エンジンのモデルベーステスト（<c>SyncModelRuntimeTests</c>）が使う生成物を固定するフィクスチャの単一ソース。
/// </summary>
/// <remarks>
/// <para>
/// 端末 2 台＋サーバーへ乱数の操作の列を流し、収束と消失なしを表明するための図。既存の <c>SyncFixture</c> が
/// 持たない形（同じ表の中の親子・版なしの表を参照する後ろの表・一意制約・NOT NULL の除外 blob・日時の主キー・
/// 文字列の主キー）を 1 つの図へ集める。表は FK の順（親→子）に次の 4 つ:
/// </para>
/// <list type="bullet">
///   <item><c>model_folders</c>: <c>folder_id</c>（int・PK）／<c>parent_id</c>（int・NULL 許容・<b>自己参照</b>の FK）／<c>name</c>（nvarchar(40)・<b>UNIQUE</b>）／<c>row_ver</c>（rowversion・NULL 許容）</item>
///   <item><c>model_labels</c>: <c>label_code</c>（<b>varchar(20)</b>・PK）／<c>folder_id</c>（int・FK）／<c>caption</c>（nvarchar(40)）＝<b>rowversion なし</b>（後勝ち専用・キー順ページング）</item>
///   <item><c>model_items</c>: <c>item_id</c>（int・PK）／<c>folder_id</c>（int・FK）／<c>label_code</c>（varchar(20)・NULL 許容・版なしの表への FK）／<c>code</c>（nvarchar(20)・<b>UNIQUE</b>）／<c>title</c>（nvarchar(40)）／<c>payload</c>（varbinary(max)・<b>NOT NULL の除外列</b>）／<c>row_ver</c>（rowversion・NULL 許容）</item>
///   <item><c>model_events</c>: <c>event_at</c>（<b>datetime2</b>・PK）／<c>item_id</c>（int・FK）／<c>memo</c>（nvarchar(40)）／<c>row_ver</c>（rowversion・NULL 許容）</item>
/// </list>
/// <para>
/// FK の参照アクションはすべて NO ACTION（既定）。削除の順序の誤りが例外として表に出る形にする
/// （CASCADE にすると順序の誤りがデータの黙った喪失へ化け、表明は消失なしの側でしか拾えない）。
/// </para>
/// <para>
/// リモートサービスを同じ図へ載せる（<c>SyncFixture</c> と同じ 2 ファイル構成）。直結と HTTP の差分ソースが同じ型の
/// まま同じモデルを共有できる。VO・EditModel・Mapper・EF Core は生成しない。
/// </para>
/// </remarks>
public static class SyncModelFixtureDefinition
{
    /// <summary>生成フィクスチャの契約 namespace（既存フィクスチャと衝突しない専用 namespace）</summary>
    public const string NamespaceName = "QuickER.Tests.GeneratedSyncModelFixture";

    /// <summary>コミット済みフィクスチャファイル名（本体）</summary>
    public const string OutputFileName = "SyncModelFixture.g.cs";

    /// <summary>コミット済みフィクスチャファイル名（リモートエンドポイント＝同期専用エンドポイントを含む）</summary>
    public const string RemoteServerOutputFileName = "SyncModelFixture.RemoteServer.g.cs";

    /// <summary>期待する出力ファイル構成（ファイル構成そのものもドリフト検知の対象）</summary>
    public static IReadOnlyList<string> OutputFileNames { get; } =
    [OutputFileName, RemoteServerOutputFileName];

    /// <summary>フィクスチャ生成に用いる決定的なオプション（SyncFixture と同じ組み合わせ）</summary>
    public static CodeGenerationOptions Options { get; } =
        new()
        {
            RootNamespace = NamespaceName,
            OutputFileName = OutputFileName,
            GenerateEditModels = false,
            GenerateMappers = false,
            GenerateRepositories = true,
            GenerateValueObjects = false,
            GenerateEfCoreRepositories = false,
            GenerateSyncSupport = true,
            GenerateRemoteServices = true,
            RepositoryDialects = ["sqlserver", "sqlite"],
            ExcludeUnboundedBinaryColumns = true,
            SplitFilesByCategory = false,
        };

    // 図の要素 ID は決定的でなければ再生成時に差分が出るため、固定 GUID を用いる。
    private static readonly Guid FolderEntityId = new("c2000000-0000-0000-0000-000000000001");
    private static readonly Guid FolderPkColId = new("c2000000-0000-0000-0000-000000000002");
    private static readonly Guid FolderParentColId = new("c2000000-0000-0000-0000-000000000003");
    private static readonly Guid FolderNameColId = new("c2000000-0000-0000-0000-000000000004");
    private static readonly Guid FolderRowVerColId = new("c2000000-0000-0000-0000-000000000005");
    private static readonly Guid FolderNameUniqueId = new("c2000000-0000-0000-0000-000000000006");
    private static readonly Guid LabelEntityId = new("c2000000-0000-0000-0000-000000000011");
    private static readonly Guid LabelPkColId = new("c2000000-0000-0000-0000-000000000012");
    private static readonly Guid LabelFolderColId = new("c2000000-0000-0000-0000-000000000013");
    private static readonly Guid LabelCaptionColId = new("c2000000-0000-0000-0000-000000000014");
    private static readonly Guid ItemEntityId = new("c2000000-0000-0000-0000-000000000021");
    private static readonly Guid ItemPkColId = new("c2000000-0000-0000-0000-000000000022");
    private static readonly Guid ItemFolderColId = new("c2000000-0000-0000-0000-000000000023");
    private static readonly Guid ItemLabelColId = new("c2000000-0000-0000-0000-000000000024");
    private static readonly Guid ItemCodeColId = new("c2000000-0000-0000-0000-000000000025");
    private static readonly Guid ItemTitleColId = new("c2000000-0000-0000-0000-000000000026");
    private static readonly Guid ItemPayloadColId = new("c2000000-0000-0000-0000-000000000027");
    private static readonly Guid ItemRowVerColId = new("c2000000-0000-0000-0000-000000000028");
    private static readonly Guid ItemCodeUniqueId = new("c2000000-0000-0000-0000-000000000029");
    private static readonly Guid EventEntityId = new("c2000000-0000-0000-0000-000000000031");
    private static readonly Guid EventPkColId = new("c2000000-0000-0000-0000-000000000032");
    private static readonly Guid EventItemColId = new("c2000000-0000-0000-0000-000000000033");
    private static readonly Guid EventMemoColId = new("c2000000-0000-0000-0000-000000000034");
    private static readonly Guid EventRowVerColId = new("c2000000-0000-0000-0000-000000000035");
    private static readonly Guid FolderParentRelationshipId = new(
        "c2000000-0000-0000-0000-000000000041"
    );
    private static readonly Guid FolderLabelRelationshipId = new(
        "c2000000-0000-0000-0000-000000000042"
    );
    private static readonly Guid FolderItemRelationshipId = new(
        "c2000000-0000-0000-0000-000000000043"
    );
    private static readonly Guid LabelItemRelationshipId = new(
        "c2000000-0000-0000-0000-000000000044"
    );
    private static readonly Guid ItemEventRelationshipId = new(
        "c2000000-0000-0000-0000-000000000045"
    );

    /// <summary>モデルベーステスト用の ER 図を決定的に構築する（型は SQL Server 表記）</summary>
    public static ErDiagram Build()
    {
        var folder = new Entity
        {
            Id = FolderEntityId,
            TableName = "model_folders",
            Columns =
            {
                IntKey(FolderPkColId, "folder_id"),
                new Column
                {
                    Id = FolderParentColId,
                    Name = "parent_id",
                    DataType = "int",
                    IsForeignKey = true,
                    IsNullable = true,
                },
                Text(FolderNameColId, "name", "nvarchar(40)"),
                RowVersion(FolderRowVerColId),
            },

            // 自己参照の表の一意制約（一意な値の「削除→同じ値で挿入」を、同じ表の中の親子と組み合わせて確かめるため）
            UniqueConstraints =
            {
                new UniqueConstraint
                {
                    Id = FolderNameUniqueId,
                    Name = "UQ_model_folders_name",
                    ColumnIds = { FolderNameColId },
                },
            },
        };

        // 版なしの表。キーは varchar（SQL Server の SQL_* 照合順序ではキー順の比較規則が nvarchar と違う）
        var label = new Entity
        {
            Id = LabelEntityId,
            TableName = "model_labels",
            Columns =
            {
                new Column
                {
                    Id = LabelPkColId,
                    Name = "label_code",
                    DataType = "varchar(20)",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                ForeignKey(LabelFolderColId, "folder_id", nullable: false),
                Text(LabelCaptionColId, "caption", "nvarchar(40)"),
            },
        };

        var item = new Entity
        {
            Id = ItemEntityId,
            TableName = "model_items",
            Columns =
            {
                IntKey(ItemPkColId, "item_id"),
                ForeignKey(ItemFolderColId, "folder_id", nullable: false),
                new Column
                {
                    Id = ItemLabelColId,
                    Name = "label_code",
                    DataType = "varchar(20)",
                    IsForeignKey = true,
                    IsNullable = true,
                },
                Text(ItemCodeColId, "code", "nvarchar(20)"),
                Text(ItemTitleColId, "title", "nvarchar(40)"),
                // NOT NULL の除外列＝行の転送に載らず、blob を運ぶ回だけが列単位でコピーする
                new Column
                {
                    Id = ItemPayloadColId,
                    Name = "payload",
                    DataType = "varbinary(max)",
                    IsNullable = false,
                },
                RowVersion(ItemRowVerColId),
            },
            UniqueConstraints =
            {
                new UniqueConstraint
                {
                    Id = ItemCodeUniqueId,
                    Name = "UQ_model_items_code",
                    ColumnIds = { ItemCodeColId },
                },
            },
        };

        var @event = new Entity
        {
            Id = EventEntityId,
            TableName = "model_events",
            Columns =
            {
                new Column
                {
                    Id = EventPkColId,
                    Name = "event_at",
                    DataType = "datetime2",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                ForeignKey(EventItemColId, "item_id", nullable: false),
                Text(EventMemoColId, "memo", "nvarchar(40)"),
                RowVersion(EventRowVerColId),
            },
        };

        return new ErDiagram
        {
            TargetDbms = "sqlserver",
            Entities = { folder, label, item, @event },
            Relationships =
            {
                Relation(
                    FolderParentRelationshipId,
                    FolderEntityId,
                    FolderEntityId,
                    FolderPkColId,
                    FolderParentColId,
                    "FK_model_folders_model_folders"
                ),
                Relation(
                    FolderLabelRelationshipId,
                    FolderEntityId,
                    LabelEntityId,
                    FolderPkColId,
                    LabelFolderColId,
                    "FK_model_labels_model_folders"
                ),
                Relation(
                    FolderItemRelationshipId,
                    FolderEntityId,
                    ItemEntityId,
                    FolderPkColId,
                    ItemFolderColId,
                    "FK_model_items_model_folders"
                ),
                Relation(
                    LabelItemRelationshipId,
                    LabelEntityId,
                    ItemEntityId,
                    LabelPkColId,
                    ItemLabelColId,
                    "FK_model_items_model_labels"
                ),
                Relation(
                    ItemEventRelationshipId,
                    ItemEntityId,
                    EventEntityId,
                    ItemPkColId,
                    EventItemColId,
                    "FK_model_events_model_items"
                ),
            },
        };
    }

    /// <summary>図を SQLite 方言へ変換した複製を返す（ローカル DB・SQLite のサーバー役のスキーマ）</summary>
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

    /// <summary>主辞書（SQL Server）と、実効方言（sqlserver / sqlite）ごとに解決した方言辞書を返す</summary>
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

    private static Column IntKey(Guid id, string name) =>
        new()
        {
            Id = id,
            Name = name,
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };

    private static Column ForeignKey(Guid id, string name, bool nullable) =>
        new()
        {
            Id = id,
            Name = name,
            DataType = "int",
            IsForeignKey = true,
            IsNullable = nullable,
        };

    private static Column Text(Guid id, string name, string dataType) =>
        new()
        {
            Id = id,
            Name = name,
            DataType = dataType,
            IsNullable = false,
        };

    // 行バージョン列は NULL 許容＝ローカルで作ってまだ上がっていない行の「未同期」を空で表す（SyncFixture と同じ）
    private static Column RowVersion(Guid id) =>
        new()
        {
            Id = id,
            Name = "row_ver",
            DataType = "rowversion",
            IsNullable = true,
        };

    private static Relationship Relation(
        Guid id,
        Guid parentEntityId,
        Guid childEntityId,
        Guid parentColumnId,
        Guid childColumnId,
        string constraintName
    ) =>
        new()
        {
            Id = id,
            SourceEntityId = parentEntityId,
            TargetEntityId = childEntityId,
            Type = RelationshipType.OneToMany,
            ConstraintName = constraintName,
            ColumnPairs = { new RelationshipColumnPair(parentColumnId, childColumnId) },
        };
}
