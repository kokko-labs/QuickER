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
            GenerateEfCore = true,
            GenerateInMemoryRepositories = true,
            GenerateRemoteServices = true,
            RepositoryDialects = ["sqlserver"],
            SplitFilesByCategory = false,
        };

    // 図の要素 ID は決定的でなければ再生成時に差分が出るため、固定 GUID を用いる。
    private static readonly Guid GadgetId = new("a1000000-0000-0000-0000-000000000001");
    private static readonly Guid GadgetPkColId = new("a1000000-0000-0000-0000-000000000002");
    private static readonly Guid GadgetNameColId = new("a1000000-0000-0000-0000-000000000003");
    private static readonly Guid GadgetRowVerColId = new("a1000000-0000-0000-0000-000000000004");

    private static readonly Guid NoteId = new("a2000000-0000-0000-0000-000000000001");
    private static readonly Guid NotePkColId = new("a2000000-0000-0000-0000-000000000002");
    private static readonly Guid NoteGadgetFkColId = new("a2000000-0000-0000-0000-000000000003");
    private static readonly Guid NoteTextColId = new("a2000000-0000-0000-0000-000000000004");
    private static readonly Guid NoteRowVerColId = new("a2000000-0000-0000-0000-000000000005");

    private static readonly Guid RelGadgetNotes = new("a3000000-0000-0000-0000-000000000001");

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

        return new ErDiagram
        {
            Entities = { gadget, note },
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
            },
        };
    }
}
