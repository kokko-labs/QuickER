using QuickER.CodeGen.CSharp;
using QuickER.Model;

namespace QuickER.Tests.GeneratedBinaryVoFixture;

/// <summary>
/// 「値オブジェクト × 無制限バイナリ列の除外 × EditModel / Mapper」の組合せを固定するフィクスチャを生成する単一ソース。
/// </summary>
/// <remarks>
/// <para>
/// 既存のバイナリ除外フィクスチャ（<c>BinaryFixture</c> / <c>SqlServerBinaryFixture</c>）は
/// <see cref="CodeGenerationOptions.GenerateValueObjects"/> が OFF のため、
/// 「VO 有効 × 除外 × NOT NULL の除外列 × EditModel / Mapper」という組合せが行列に存在しなかった。
/// この組合せは実体の初期化子が <c>= null!</c>（非 NULL の VO）になるため未取得状態＝null で届き、
/// 修正前は EditModel の必須検証と Mapper の <c>?? throw</c> の 2 段で
/// 「未取得のまま素通しするだけの保存」が落ちていた。ここはその往復を固定するための最小構成。
/// </para>
/// <para>
/// 図は 1 テーブル <c>vault_items</c> のみ:
/// <list type="bullet">
///   <item><c>item_id</c>: 単一主キー（int・非 NULL）</item>
///   <item><c>label</c>: 通常列（<c>nvarchar(50)</c>・非 NULL）＝往復で編集する列</item>
///   <item><c>seal</c>: 無制限バイナリ（<c>varbinary(max)</c>・<b>非 NULL</b>）＝除外対象。VO 非 NULL のため初期化子は <c>= null!</c></item>
///   <item><c>note_blob</c>: 無制限バイナリ（<c>varbinary(max)</c>・NULL 許容）＝除外対象。NULL 許容側の同一往復を押さえる</item>
/// </list>
/// 文字列列は Unicode（<c>nvarchar</c>）で統一する（可搬フィクスチャの不変条件）。
/// </para>
/// <para>
/// オプションは最小（SQLite 方言のQuickER 版 Repository＋EditModel＋Mapper＋VO＋除外）で、
/// EF Core・インメモリ・リモートは持たない。SQLite ＝ Docker 不要のため実 DB 往復テストが CI で常時実行できる。
/// </para>
/// </remarks>
public static class BinaryVoFixtureDefinition
{
    /// <summary>生成フィクスチャの名前空間（既存フィクスチャと衝突しない専用 namespace）</summary>
    public const string NamespaceName = "QuickER.Tests.GeneratedBinaryVoFixture";

    /// <summary>コミット済みフィクスチャファイル名</summary>
    public const string OutputFileName = "BinaryVoFixture.g.cs";

    /// <summary>
    /// フィクスチャ生成に用いる決定的なオプション。
    /// SQLite 方言のQuickER 版 Repository に値オブジェクトと無制限バイナリ除外を併用した最小構成。
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
            RepositoryDialects = ["sqlite"],
            ExcludeUnboundedBinaryColumns = true,
            SplitFilesByCategory = false,
        };

    // 図の要素 ID は決定的でなければ再生成時に差分が出るため、固定 GUID を用いる。
    private static readonly Guid VaultItemId = new("b0000000-0000-0000-0000-000000000001");
    private static readonly Guid VaultItemPkColId = new("b0000000-0000-0000-0000-000000000002");
    private static readonly Guid VaultItemLabelColId = new("b0000000-0000-0000-0000-000000000003");
    private static readonly Guid VaultItemSealColId = new("b0000000-0000-0000-0000-000000000004");
    private static readonly Guid VaultItemNoteBlobColId = new(
        "b0000000-0000-0000-0000-000000000005"
    );

    /// <summary>
    /// VO × バイナリ除外の検証用 ER 図を決定的に構築する（要素 ID は固定 GUID・型は SQL Server 表記）。
    /// </summary>
    public static ErDiagram Build()
    {
        var vaultItem = new Entity
        {
            Id = VaultItemId,
            TableName = "vault_items",
            Columns =
            {
                new Column
                {
                    Id = VaultItemPkColId,
                    Name = "item_id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Id = VaultItemLabelColId,
                    Name = "label",
                    DataType = "nvarchar(50)",
                    IsNullable = false,
                },
                // 非 NULL の無制限バイナリ＝除外対象。VO 化されるため実体の初期化子は = null!（未取得状態＝null）
                new Column
                {
                    Id = VaultItemSealColId,
                    Name = "seal",
                    DataType = "varbinary(max)",
                    IsNullable = false,
                },
                // NULL 許容の無制限バイナリ＝除外対象。非 NULL 側と同じ往復が成立することを押さえる
                new Column
                {
                    Id = VaultItemNoteBlobColId,
                    Name = "note_blob",
                    DataType = "varbinary(max)",
                    IsNullable = true,
                },
            },
        };

        return new ErDiagram { Entities = { vaultItem } };
    }
}
