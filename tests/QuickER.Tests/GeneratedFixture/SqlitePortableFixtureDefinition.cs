using QuickER.CodeGen.CSharp;
using QuickER.Model;

namespace QuickER.Tests.GeneratedSqliteFixture;

/// <summary>
/// SQLite 方言のQuickER 版 Repository を含む「第3の固定フィクスチャ」を生成する単一ソース。
/// </summary>
/// <remarks>
/// <para>
/// 入力の ER 図は既存の方言可搬フィクスチャ（<see cref="Tests.GeneratedPortableFixture.PortableFixtureDefinition"/>）と
/// <b>同一</b>（rowversion なし・2 エンティティ・1対多カスケード・int/string/decimal のみ）。
/// 相違はオプションのみで、<see cref="CodeGenerationOptions.RepositoryDialects"/> = <c>["sqlite"]</c>・
/// <c>GenerateRepositories=true</c>・<c>GenerateEfCoreRepositories=true</c>（パリティ用に両方 ON。CLI/オプション直指定でのみ
/// 許される組合せ）で生成する。これにより SQLite 方言ランタイム（<c>SqliteRepository</c>・プレーン SELECT＋
/// DataReader 実体化・<c>IncludeLoader</c> マルチクエリ・LIMIT/OFFSET・strftime）と EF Core Sqlite の
/// 両方を 1 つのアセンブリに載せ、方言ランタイムテスト・パリティテストの入力にできる。
/// </para>
/// <para>
/// namespace は既存フィクスチャ 2 つ（<c>QuickER.Tests.GeneratedFixture</c> /
/// <c>QuickER.Tests.GeneratedPortableFixture</c>）と衝突しない専用のもの。
/// </para>
/// </remarks>
public static class SqlitePortableFixtureDefinition
{
    /// <summary>生成フィクスチャの名前空間（既存フィクスチャと衝突しない専用 namespace）</summary>
    public const string NamespaceName = "QuickER.Tests.GeneratedSqliteFixture";

    /// <summary>コミット済みフィクスチャファイル名</summary>
    public const string OutputFileName = "SqlitePortableFixture.g.cs";

    /// <summary>
    /// フィクスチャ生成に用いる決定的なオプション。
    /// SQLite 方言のQuickER 版 Repository と EF Core を両方生成する（パリティ検証用の構成）。
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
            RepositoryDialects = ["sqlite"],
            SplitFilesByCategory = false,
        };

    // 名前付きクエリの ID も決定的でなければならないため固定 GUID を用いる
    // （SQL Server 全カバレッジフィクスチャ側の 55555555-... と衝突しない専用プレフィックス）
    private static readonly Guid QuerySearchMemoContains = new(
        "55555556-0000-0000-0000-000000000001"
    );
    private static readonly Guid QueryGetMissingMemo = new("55555556-0000-0000-0000-000000000002");
    private static readonly Guid QueryGetExpensive = new("55555556-0000-0000-0000-000000000003");

    /// <summary>
    /// フィクスチャの ER 図を返す。図の中身は方言可搬フィクスチャと同一（SQL Server 型表記基準）で、
    /// SQLite の型カタログはこの表記を verbatim に受け付ける。これに SQL Server 全カバレッジ
    /// フィクスチャ（GeneratedFixture）と同内容の名前付きクエリ 3 種を追加し、DSL→SQL 翻訳
    /// （CONTAINS→LIKE エスケープ・IS NULL・decimal 比較）を SQLite 方言でも対称に検証できるようにする。
    /// </summary>
    /// <remarks>
    /// クエリは本ラッパー内で追加する（共有定義 <see cref="Tests.GeneratedPortableFixture.PortableFixtureDefinition"/>
    /// に足すと、4 方言バイト一致を検証する PortableFixture・マルチターゲットフィクスチャへ波及するため）。
    /// 対象エンティティ・列の Guid は共有定義の private のため、テーブル名・列名で解決する。
    /// </remarks>
    public static ErDiagram Build()
    {
        var diagram = Tests.GeneratedPortableFixture.PortableFixtureDefinition.Build(
            Tests.GeneratedPortableFixture.PortableDialect.SqlServer
        );
        AddNamedQueries(diagram);
        return diagram;
    }

    /// <summary>GeneratedFixture の名前付きクエリ 3 種（SearchMemoContains / GetMissingMemo / GetExpensive）と同内容を追加する</summary>
    private static void AddNamedQueries(ErDiagram diagram)
    {
        var order = diagram.Entities.Single(e => e.TableName == "orders");
        var orderPkColId = order.Columns.Single(c => c.Name == "order_id").Id;

        diagram.Queries.Add(
            new QueryDefinition
            {
                Id = QuerySearchMemoContains,
                EntityId = order.Id,
                Name = "SearchMemoContains",
                Description =
                    "メモの部分一致（CONTAINS→LIKE。%・_ 等はリテラル扱い）で注文を検索する",
                Returns = QueryReturnShape.List,
                Parameters =
                {
                    new QueryParameter { Name = "keyword", Type = "string(50)" },
                },
                Condition = "memo CONTAINS @keyword",
                OrderBy = { new QueryOrdering { ColumnId = orderPkColId } },
            }
        );

        diagram.Queries.Add(
            new QueryDefinition
            {
                Id = QueryGetMissingMemo,
                EntityId = order.Id,
                Name = "GetMissingMemo",
                Description = "メモ未設定（IS NULL）の注文を検索する",
                Returns = QueryReturnShape.List,
                Condition = "memo IS NULL",
                OrderBy = { new QueryOrdering { ColumnId = orderPkColId } },
            }
        );

        diagram.Queries.Add(
            new QueryDefinition
            {
                Id = QueryGetExpensive,
                EntityId = order.Id,
                Name = "GetExpensive",
                Description = "金額（decimal・VO 列）が下限以上の注文を検索する",
                Returns = QueryReturnShape.List,
                Parameters =
                {
                    new QueryParameter { Name = "minAmount", Type = "decimal(10,2)" },
                },
                Condition = "amount >= @minAmount",
                OrderBy = { new QueryOrdering { ColumnId = orderPkColId } },
            }
        );
    }
}
