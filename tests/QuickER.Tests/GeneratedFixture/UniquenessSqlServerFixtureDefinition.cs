using System.Linq;
using QuickER.CodeGen.CSharp;
using QuickER.Model;

namespace QuickER.Tests.GeneratedUniquenessSqlServerFixture;

/// <summary>
/// UNIQUE 制約ベースの重複事前チェック（<c>CheckUniquenessAsync</c>）を <b>SQL Server 方言</b>のQuickER 版
/// Repository で生成する固定フィクスチャの単一ソース。
/// </summary>
/// <remarks>
/// <para>
/// 入力の ER 図はクエリフィクスチャ（<see cref="Tests.GeneratedQueryFixture.QueryFixtureDefinition"/>）と
/// <b>同一の 2 エンティティ・同一の UNIQUE 制約</b>（orders に単一列 <c>UQ_orders_memo</c>〔NULL 許容列〕と
/// 複合〔<c>customer_id</c>＋<c>amount</c>・名前なし＝合成名〕）で、オプションだけを SQL Server 方言の
/// QuickER 版 Repository 単独へ差し替えたもの（<c>SqlServerBinaryFixtureDefinition</c> の先例と同型）。
/// </para>
/// <para>
/// <b>名前付きクエリは外す</b>。図のクエリ定義は自由 SQL を <c>sqlite</c> キーでしか持たないため SQL Server 方言では
/// すべて manual（契約宣言のみ）になり、重複事前チェックの検証には無関係な partial 実装を要求するだけだから。
/// 制約の形をクエリフィクスチャと揃えることで、重複事前チェックのランタイムスイート
/// （<see cref="Tests.Integration.GeneratedRuntime.UniquenessCheckRuntimeTestsBase{TOrder}"/>）を SQL Server でも
/// <b>同一のアサーション</b>のまま流せる＝実装先が SQLite・EF Core・インメモリ・リモートと揃うことを実証できる。
/// </para>
/// </remarks>
public static class UniquenessSqlServerFixtureDefinition
{
    /// <summary>生成フィクスチャの名前空間（既存フィクスチャと衝突しない専用 namespace）</summary>
    public const string NamespaceName = "QuickER.Tests.GeneratedUniquenessSqlServerFixture";

    /// <summary>コミット済みフィクスチャファイル名</summary>
    public const string OutputFileName = "UniquenessSqlServerFixture.g.cs";

    /// <summary>フィクスチャ生成に用いる決定的なオプション（SQL Server 方言のQuickER 版 Repository 単独・VO 有効）</summary>
    public static CodeGenerationOptions Options { get; } =
        new()
        {
            RootNamespace = NamespaceName,
            OutputFileName = OutputFileName,
            GenerateEditModels = true,
            GenerateMappers = true,
            GenerateRepositories = true,
            GenerateValueObjects = true,
            RepositoryDialects = ["sqlserver"],
            SplitFilesByCategory = false,
        };

    /// <summary>クエリフィクスチャと同一の図から、名前付きクエリ定義だけを外して返す</summary>
    public static ErDiagram Build()
    {
        var diagram = Tests.GeneratedQueryFixture.QueryFixtureDefinition.Build();
        diagram.Queries.Clear();

        return diagram;
    }
}
