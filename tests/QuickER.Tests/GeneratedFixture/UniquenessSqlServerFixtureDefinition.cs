using System;
using System.Collections.Generic;
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
/// <b>名前付きクエリも持つ</b>。クエリフィクスチャの自由 SQL は <c>sqlite</c> キーしか持たないので、<see cref="Build"/> が
/// 同じ意味の SQL Server の SQL を <c>sqlserver</c> キーに足す（クエリフィクスチャの図は変えない＝ほかのフィクスチャへ波及させない）。
/// あわせて、生 SQL のバインダの IN の展開の置換の境界（左右）を実 SQL Server で確かめる SQL Server だけのクエリ
/// <c>GetByIdsBoundaryRaw</c> を足す。manual の <c>SpecialLookup</c> は <c>UniquenessSqlServerFixtureCustomCheck.cs</c> が実装する。
/// 生 SQL の戻り形の表（<see cref="Tests.Integration.GeneratedRuntime.NamedQueryRawSqlRows"/>）を SQL Server で流すのに使う。
/// </para>
/// <para>
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

    /// <summary>
    /// 生 SQL の IN の展開の置換の境界を確かめるクエリの ID（クエリフィクスチャの ID と重ならない固定 GUID）
    /// </summary>
    private static readonly Guid QueryGetByIdsBoundaryRaw = new(
        "dddddddd-0000-0000-0000-000000000101"
    );

    /// <summary>
    /// クエリフィクスチャの自由 SQL の名前 → 同じ意味の SQL Server の SQL（クエリフィクスチャの自由 SQL はどれもここに載せる）
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> SqlServerSql = new Dictionary<
        string,
        string
    >(StringComparer.Ordinal)
    {
        ["SumAmounts"] = "SELECT SUM([amount]) FROM [orders] WHERE [customer_id] = @customerId",
        ["GetByIdsRaw"] = "SELECT * FROM [orders] WHERE [order_id] IN (@ids) ORDER BY [order_id]",
        ["FindTopRaw"] = "SELECT TOP (1) * FROM [orders] ORDER BY [order_id] DESC",
        ["CountByCustomerRaw"] = "SELECT COUNT(*) FROM [orders] WHERE [customer_id] = @customerId",
        ["GetMemoRowsRaw"] =
            "SELECT [order_id] AS OrderId, [memo] AS Memo FROM [orders] WHERE [customer_id] = @customerId ORDER BY [order_id]",
    };

    /// <summary>
    /// クエリフィクスチャと同一の図に、自由 SQL の SQL Server の SQL と、IN の展開の境界のクエリを足して返す
    /// </summary>
    /// <exception cref="InvalidOperationException">SQL Server の SQL を持たない自由 SQL がクエリフィクスチャにある</exception>
    public static ErDiagram Build()
    {
        var diagram = Tests.GeneratedQueryFixture.QueryFixtureDefinition.Build();
        var orders = diagram.Entities.First(entity => entity.TableName == "orders");

        foreach (
            var query in diagram.Queries.Where(query =>
                query.Implementation == QueryImplementationKind.Sql
            )
        )
        {
            // 載せ忘れた自由 SQL は SQL Server で manual になり、partial の実装が無いとコンパイルできないので、ここで名指しする
            if (!SqlServerSql.TryGetValue(query.Name, out var sql))
            {
                throw new InvalidOperationException(
                    $"自由 SQL のクエリ {query.Name} の SQL Server の SQL が {nameof(SqlServerSql)} に無い"
                );
            }

            query.Sql["sqlserver"] = sql;
        }

        // コレクションのパラメータ rowcount の IN の展開は、名前がその前置きで始まるパラメータ（@rowcountMin）とシステム変数
        // （@@rowcount）を置き換えない（置換の境界が右だけ・左だけだと、そのどちらかの綴りが壊れて SQL Server が断る）
        diagram.Queries.Add(
            new QueryDefinition
            {
                Id = QueryGetByIdsBoundaryRaw,
                EntityId = orders.Id,
                Name = "GetByIdsBoundaryRaw",
                Description =
                    "注文IDの一覧と下限で注文を取得する（自由 SQL・IN の展開の置換の境界＝前置きが同じパラメータとシステム変数を置き換えない）",
                Returns = QueryReturnShape.List,
                Implementation = QueryImplementationKind.Sql,
                Sql =
                {
                    ["sqlserver"] =
                        "SELECT * FROM [orders] WHERE [order_id] IN (@rowcount) AND [order_id] >= @rowcountMin AND @@rowcount >= 0 ORDER BY [order_id]",
                },
                Parameters =
                {
                    new QueryParameter
                    {
                        Name = "rowcount",
                        Type = "int32",
                        IsList = true,
                    },
                    new QueryParameter { Name = "rowcountMin", Type = "int32" },
                },
            }
        );

        return diagram;
    }
}
