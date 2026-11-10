using QuickER.CodeGen.CSharp;
using QuickER.Model;

namespace QuickER.Tests.GeneratedConcurrencyFixture;

/// <summary>
/// rowversion 列 × 値オブジェクト（<see cref="CodeGenerationOptions.GenerateValueObjects"/>）の組み合わせを
/// 固定するフィクスチャの単一ソース。
/// </summary>
/// <remarks>
/// <para>
/// VO 有効時の rowversion プロパティは素の <c>byte[]</c> ではなく VO 型（<c>RowVerValue</c>）になる。
/// 版番号の書き戻し・読み出しはすべてリフレクション（<c>PropertyInfo.SetValue</c> / <c>GetValue</c>）で行われるため、
/// 生の <c>byte[]</c> をそのまま渡すと <c>ArgumentException</c> になる。この組み合わせのフィクスチャが無かったため
/// 全テストが素通りしていた（DB は更新済みなのに保存が例外・手元の版は古いまま＝次回保存が偽の競合）。
/// 本フィクスチャは QuickER 版 Repository（SQL Server 方言）・EF Core・インメモリ・リモートサービスの
/// 4 経路すべてを 1 アセンブリへ生成し、各経路の実行時テストで回帰を恒久固定する。
/// </para>
/// <para>
/// 図は親子 2 テーブルの最小構成で、<b>親子ともに rowversion 列を持つ</b>（グラフ保存の版チェック・
/// 版対応表の往復がカスケード経路まで効くことを検証できる）:
/// </para>
/// <list type="bullet">
///   <item><c>gadgets</c>: <c>gadget_id</c>（int・PK）／<c>name</c>（nvarchar(50)・NOT NULL）／<c>row_ver</c>（rowversion・NOT NULL）</item>
///   <item><c>gadget_notes</c>: <c>note_id</c>（int・PK）／<c>gadget_id</c>（int・FK）／<c>note</c>（nvarchar(100)・NOT NULL）／<c>row_ver</c>（rowversion・NOT NULL）</item>
/// </list>
/// <para>
/// <c>row_ver</c> は両テーブルで同一定義のため VO は 1 クラス（<c>RowVerValue</c>＝<c>ValueObjectBinaryBase</c> 派生）に
/// 集約される。<c>gadget_id</c> も PK 側と FK 側で共有される（<c>QueryFixture</c> の <c>customer_id</c> と同流儀）。
/// NOT NULL にするのは実 SQL Server の <c>rowversion</c> の実態に合わせるため（DB が必ず採番する）。
/// 文字列列は Unicode（<c>nvarchar</c>）で統一する（可搬フィクスチャの不変条件）。
/// </para>
/// <para>
/// あわせて、行バージョンを持つ表の代替キー（主キーでない一意制約を外部キーが参照する列）の書き込みを SQL Server で確かめる
/// 2 表を置く（<c>BackendParitySqlServerRuntimeTests</c> のケース。上の 2 表とは独立）:
/// </para>
/// <list type="bullet">
///   <item><c>kits</c>: <c>kit_id</c>（int・PK）／<c>code</c>（nvarchar(20)・NOT NULL・一意制約）／<c>name</c>（nvarchar(50)・NOT NULL）／<c>row_ver</c>（rowversion・NOT NULL）</item>
///   <item><c>kit_parts</c>: <c>part_id</c>（int・PK）／<c>kit_code</c>（nvarchar(20)・NULL 許容・<c>kits.code</c> への FK・NO ACTION）</item>
/// </list>
/// <para>
/// あわせて、EF Core の SaveChanges が書く列を持たない表（キーの列と DB が値を作る列だけ）の更新を SQL Server で確かめる
/// 4 表を置く（<c>BackendParitySqlServerRuntimeTests</c> のケース。上の表とは独立）:
/// </para>
/// <list type="bullet">
///   <item><c>tags</c>: <c>tag_id</c>（int・PK）／<c>code</c>（nvarchar(20)・NOT NULL・一意制約＝<c>tag_links.tag_code</c> が参照する代替キー）／<c>row_ver</c>（rowversion・NOT NULL）</item>
///   <item><c>tag_links</c>: <c>link_id</c>（int・PK）／<c>tag_code</c>（nvarchar(20)・NULL 許容・<c>tags.code</c> への FK・NO ACTION）</item>
///   <item><c>stamps</c>: <c>stamp_id</c>（int・PK）／<c>row_ver</c>（rowversion・NOT NULL）</item>
///   <item><c>markers</c>: <c>marker_id</c>（int・PK）だけ</item>
/// </list>
/// <para>
/// あわせて「DB 採番列は EditModel の入力必須にしない」も本図で固定する。VO 有無に依らず rowversion 列は
/// <c>IsRequired=false</c>／Mapper は「入力があるときだけ代入」になり、新規行の EditModel 保存が成立する。
/// </para>
/// </remarks>
public static class ConcurrencyFixtureDefinition
{
    /// <summary>生成フィクスチャの名前空間（既存フィクスチャと衝突しない専用 namespace）</summary>
    public const string NamespaceName = "QuickER.Tests.GeneratedConcurrencyFixture";

    /// <summary>コミット済みフィクスチャファイル名（本体）</summary>
    public const string OutputFileName = "ConcurrencyFixture.g.cs";

    /// <summary>コミット済みフィクスチャファイル名（サーバー実装）</summary>
    public const string ServerOutputFileName = "ConcurrencyFixture.RemoteServer.g.cs";

    /// <summary>
    /// フィクスチャ生成に用いる決定的なオプション。
    /// SQL Server 方言のQuickER 版 Repository・EF Core・インメモリ・リモートサービスを VO 有効で併存させる。
    /// </summary>
    public static CodeGenerationOptions Options { get; } =
        new()
        {
            RootNamespace = NamespaceName,
            OutputFileName = OutputFileName,
            GenerateEditModels = true,
            GenerateMappers = true,
            GenerateRepositories = true,
            GenerateValueObjects = true,
            GenerateEfCoreRepositories = true,
            GenerateInMemoryRepositories = true,
            GenerateRemoteServices = true,
            RepositoryDialects = ["sqlserver"],
            SplitFilesByCategory = false,
            ExcludeUnboundedBinaryColumns = true,
        };

    // 図の要素 ID は決定的でなければ再生成時に差分が出るため、固定 GUID を用いる。
    private static readonly Guid GadgetId = new("a1000000-0000-0000-0000-000000000001");
    private static readonly Guid GadgetPkColId = new("a1000000-0000-0000-0000-000000000002");
    private static readonly Guid GadgetNameColId = new("a1000000-0000-0000-0000-000000000003");
    private static readonly Guid GadgetRowVerColId = new("a1000000-0000-0000-0000-000000000004");
    private static readonly Guid GadgetAttachmentColId = new(
        "a1000000-0000-0000-0000-000000000005"
    );

    private static readonly Guid NoteId = new("a2000000-0000-0000-0000-000000000001");
    private static readonly Guid NotePkColId = new("a2000000-0000-0000-0000-000000000002");
    private static readonly Guid NoteGadgetFkColId = new("a2000000-0000-0000-0000-000000000003");
    private static readonly Guid NoteTextColId = new("a2000000-0000-0000-0000-000000000004");
    private static readonly Guid NoteRowVerColId = new("a2000000-0000-0000-0000-000000000005");

    private static readonly Guid RelGadgetNotes = new("a3000000-0000-0000-0000-000000000001");

    private static readonly Guid KitId = new("a4000000-0000-0000-0000-000000000001");
    private static readonly Guid KitPkColId = new("a4000000-0000-0000-0000-000000000002");
    private static readonly Guid KitCodeColId = new("a4000000-0000-0000-0000-000000000003");
    private static readonly Guid KitNameColId = new("a4000000-0000-0000-0000-000000000004");
    private static readonly Guid KitRowVerColId = new("a4000000-0000-0000-0000-000000000005");
    private static readonly Guid KitCodeUniqueId = new("a4000000-0000-0000-0000-000000000006");

    private static readonly Guid PartId = new("a5000000-0000-0000-0000-000000000001");
    private static readonly Guid PartPkColId = new("a5000000-0000-0000-0000-000000000002");
    private static readonly Guid PartKitCodeColId = new("a5000000-0000-0000-0000-000000000003");

    private static readonly Guid RelKitParts = new("a6000000-0000-0000-0000-000000000001");

    private static readonly Guid TagId = new("a7000000-0000-0000-0000-000000000001");
    private static readonly Guid TagPkColId = new("a7000000-0000-0000-0000-000000000002");
    private static readonly Guid TagCodeColId = new("a7000000-0000-0000-0000-000000000003");
    private static readonly Guid TagRowVerColId = new("a7000000-0000-0000-0000-000000000004");
    private static readonly Guid TagCodeUniqueId = new("a7000000-0000-0000-0000-000000000005");

    private static readonly Guid TagLinkId = new("a8000000-0000-0000-0000-000000000001");
    private static readonly Guid TagLinkPkColId = new("a8000000-0000-0000-0000-000000000002");
    private static readonly Guid TagLinkCodeColId = new("a8000000-0000-0000-0000-000000000003");

    private static readonly Guid RelTagLinks = new("a8000000-0000-0000-0000-000000000004");

    private static readonly Guid StampId = new("a9000000-0000-0000-0000-000000000001");
    private static readonly Guid StampPkColId = new("a9000000-0000-0000-0000-000000000002");
    private static readonly Guid StampRowVerColId = new("a9000000-0000-0000-0000-000000000003");

    private static readonly Guid MarkerId = new("aa000000-0000-0000-0000-000000000001");
    private static readonly Guid MarkerPkColId = new("aa000000-0000-0000-0000-000000000002");

    /// <summary>
    /// rowversion × VO の検証用 ER 図を決定的に構築する（要素 ID は固定 GUID・型は SQL Server 表記）。
    /// </summary>
    public static ErDiagram Build()
    {
        var gadget = new Entity
        {
            Id = GadgetId,
            TableName = "gadgets",
            Columns =
            {
                new Column
                {
                    Id = GadgetPkColId,
                    Name = "gadget_id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Id = GadgetNameColId,
                    Name = "name",
                    DataType = "nvarchar(50)",
                    IsNullable = false,
                },
                // rowversion＝store-generated 列。VO 有効なので生成プロパティは RowVerValue（byte[] ではない）
                new Column
                {
                    Id = GadgetRowVerColId,
                    Name = "row_ver",
                    DataType = "rowversion",
                    IsNullable = false,
                },
                // 除外する無制限バイナリ列＝EF Core のサーバーのリモートで、版の古い ForceOverwrite のやり直しの後も
                // blob を書かないことを確かめる（RemoteEfCoreUnboundedBinarySqlServerRuntimeTests）
                new Column
                {
                    Id = GadgetAttachmentColId,
                    Name = "attachment",
                    DataType = "varbinary(max)",
                    IsNullable = true,
                },
            },
        };

        var note = new Entity
        {
            Id = NoteId,
            TableName = "gadget_notes",
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
                    Id = NoteGadgetFkColId,
                    Name = "gadget_id",
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
                // 子側にも rowversion を置き、グラフ保存のカスケード経路まで版チェック・版の書き戻しを検証できるようにする
                new Column
                {
                    Id = NoteRowVerColId,
                    Name = "row_ver",
                    DataType = "rowversion",
                    IsNullable = false,
                },
            },
        };

        var kit = new Entity
        {
            Id = KitId,
            TableName = "kits",
            Columns =
            {
                new Column
                {
                    Id = KitPkColId,
                    Name = "kit_id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                // 一意なコード（kit_parts.kit_code が参照する＝EF Core は HasPrincipalKey で代替キーにする。代替キーは必須なので NOT NULL）
                new Column
                {
                    Id = KitCodeColId,
                    Name = "code",
                    DataType = "nvarchar(20)",
                    IsNullable = false,
                },
                new Column
                {
                    Id = KitNameColId,
                    Name = "name",
                    DataType = "nvarchar(50)",
                    IsNullable = false,
                },
                new Column
                {
                    Id = KitRowVerColId,
                    Name = "row_ver",
                    DataType = "rowversion",
                    IsNullable = false,
                },
            },
            UniqueConstraints =
            {
                new UniqueConstraint
                {
                    Id = KitCodeUniqueId,
                    Name = "UQ_kits_code",
                    ColumnIds = [KitCodeColId],
                },
            },
        };

        var part = new Entity
        {
            Id = PartId,
            TableName = "kit_parts",
            Columns =
            {
                new Column
                {
                    Id = PartPkColId,
                    Name = "part_id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Id = PartKitCodeColId,
                    Name = "kit_code",
                    DataType = "nvarchar(20)",
                    IsForeignKey = true,
                    IsNullable = true,
                },
            },
        };

        // 書ける列が代替キーと版だけ（EF Core は代替キーを書かず、版は DB が作るので、SaveChanges が UPDATE を送らない）
        var tag = new Entity
        {
            Id = TagId,
            TableName = "tags",
            Columns =
            {
                new Column
                {
                    Id = TagPkColId,
                    Name = "tag_id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Id = TagCodeColId,
                    Name = "code",
                    DataType = "nvarchar(20)",
                    IsNullable = false,
                },
                new Column
                {
                    Id = TagRowVerColId,
                    Name = "row_ver",
                    DataType = "rowversion",
                    IsNullable = false,
                },
            },
            UniqueConstraints =
            {
                new UniqueConstraint
                {
                    Id = TagCodeUniqueId,
                    Name = "UQ_tags_code",
                    ColumnIds = [TagCodeColId],
                },
            },
        };

        var tagLink = new Entity
        {
            Id = TagLinkId,
            TableName = "tag_links",
            Columns =
            {
                new Column
                {
                    Id = TagLinkPkColId,
                    Name = "link_id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Id = TagLinkCodeColId,
                    Name = "tag_code",
                    DataType = "nvarchar(20)",
                    IsForeignKey = true,
                    IsNullable = true,
                },
            },
        };

        // キーと版だけ（SaveChanges が UPDATE を送らない）
        var stamp = new Entity
        {
            Id = StampId,
            TableName = "stamps",
            Columns =
            {
                new Column
                {
                    Id = StampPkColId,
                    Name = "stamp_id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Id = StampRowVerColId,
                    Name = "row_ver",
                    DataType = "rowversion",
                    IsNullable = false,
                },
            },
        };

        // キーだけ（SaveChanges が UPDATE を送らない・版も無い）
        var marker = new Entity
        {
            Id = MarkerId,
            TableName = "markers",
            Columns =
            {
                new Column
                {
                    Id = MarkerPkColId,
                    Name = "marker_id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
            },
        };

        return new ErDiagram
        {
            Entities = { gadget, note, kit, part, tag, tagLink, stamp, marker },
            Relationships =
            {
                // 1対多: gadgets -> gadget_notes（ON DELETE CASCADE）
                new Relationship
                {
                    Id = RelGadgetNotes,
                    Type = RelationshipType.OneToMany,
                    SourceEntityId = GadgetId,
                    TargetEntityId = NoteId,
                    ColumnPairs = [new(GadgetPkColId, NoteGadgetFkColId)],
                    ConstraintName = "FK_gadget_notes_gadgets",
                    OnDelete = ForeignKeyReferentialAction.Cascade,
                    OnUpdate = ForeignKeyReferentialAction.NoAction,
                },
                // 1対多: kits.code -> kit_parts.kit_code（主キーでない一意制約を参照する・NO ACTION＝参照されているコードの
                // 変更は DB が断る）
                new Relationship
                {
                    Id = RelKitParts,
                    Type = RelationshipType.OneToMany,
                    SourceEntityId = KitId,
                    TargetEntityId = PartId,
                    ColumnPairs = [new(KitCodeColId, PartKitCodeColId)],
                    ConstraintName = "FK_kit_parts_kits_code",
                    OnDelete = ForeignKeyReferentialAction.NoAction,
                    OnUpdate = ForeignKeyReferentialAction.NoAction,
                },
                // 1対多: tags.code -> tag_links.tag_code（tags.code を EF Core の代替キーにする）
                new Relationship
                {
                    Id = RelTagLinks,
                    Type = RelationshipType.OneToMany,
                    SourceEntityId = TagId,
                    TargetEntityId = TagLinkId,
                    ColumnPairs = [new(TagCodeColId, TagLinkCodeColId)],
                    ConstraintName = "FK_tag_links_tags_code",
                    OnDelete = ForeignKeyReferentialAction.NoAction,
                    OnUpdate = ForeignKeyReferentialAction.NoAction,
                },
            },
        };
    }
}
