using QuickER.CodeGen.CSharp;
using QuickER.Model;

namespace QuickER.Tests.GeneratedSqliteFixture;

/// <summary>
/// SQLite 方言の自作 Repository を含む「第3の固定フィクスチャ」を生成する単一ソース。
/// </summary>
/// <remarks>
/// <para>
/// 入力の ER 図は既存の方言可搬フィクスチャ（<see cref="Tests.GeneratedPortableFixture.PortableFixtureDefinition"/>）と
/// <b>同一</b>（rowversion なし・2 エンティティ・1対多カスケード・int/string/decimal のみ）。
/// 相違はオプションのみで、<see cref="CodeGenerationOptions.RepositoryDialect"/> = <c>"sqlite"</c>・
/// <c>GenerateRepositories=true</c>・<c>GenerateEfCore=true</c>（パリティ用に両方 ON。CLI/オプション直指定でのみ
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
    /// SQLite 方言の自作 Repository と EF Core を両方生成する（パリティ検証用の構成）。
    /// </summary>
    public static CodeGenerationOptions Options { get; } =
        new()
        {
            NamespaceName = NamespaceName,
            OutputFileName = OutputFileName,
            GenerateEntityClasses = true,
            GenerateEditModels = true,
            GenerateMappers = true,
            GenerateRepositories = true,
            GenerateValueObjects = true,
            GenerateEfCore = true,
            RepositoryDialect = "sqlite",
            SplitFilesByCategory = false,
        };

    /// <summary>
    /// フィクスチャの ER 図を返す。図の中身は方言可搬フィクスチャと同一（SQL Server 型表記基準）で、
    /// SQLite の型カタログはこの表記を verbatim に受け付ける。
    /// </summary>
    public static ErDiagram Build() =>
        Tests.GeneratedPortableFixture.PortableFixtureDefinition.Build(
            Tests.GeneratedPortableFixture.PortableDialect.SqlServer
        );
}
