using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider.MySql;
using QuickER.Provider.Sqlite;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// 簡易 DSL の条件の総当たり（区切り 23a）: 列ごとに書ける全ての形の原子（値は乱数）と、乱数で組んだ合成の条件を
/// 生成→実コンパイル（エラー 0・警告 0）し、NULL を含むデータで QuickER 版 Repository（SQLite）・EF Core（SQLite）・
/// インメモリへ流して、同じ実装先から読み戻した全行へ docs の意味のオラクル（<see cref="DslFuzz.Evaluate"/>）を当てた結果と
/// 照合するテストクラス
/// </summary>
/// <remarks>
/// <para>
/// 本体は <see cref="DslFuzzRunner"/>。結果は既知の失敗の一覧（<see cref="DslFuzzKnownFailures"/>）と厳密に照合する
/// （一覧に無い失敗も、載っているのに起きない失敗も赤）。このクラスが受け持つ入口は <c>dsl-generate:</c>・<c>dsl-compile:</c>・
/// <c>dsl-exec:</c>（SQL Server の派生は Integration の <c>dsl-sqlserver-</c>）。
/// </para>
/// <para>
/// MySQL の単位（符号なし整数・sbyte）は MySQL の QuickER 版 Repository が無いため、インメモリでだけ実行する。
/// シードの数は <c>QUICKER_FUZZ_SEEDS</c>（既定 2）。
/// </para>
/// </remarks>
public class DslConditionFuzzTests
{
    /// <summary>このクラスが受け持つ失敗のキーの接頭辞</summary>
    private const string Suite = "dsl";

    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>SQLite の 3 実装先</summary>
    private static readonly DslFuzzTarget SqliteTarget = new(
        Suite,
        "sqlite",
        new SqliteProvider(),
        unit => Options(unit, ado: true),
        "AddGeneratedSqliteRepositories",
        EfCore: true,
        InMemory: true,
        [
            SqliteFile(
                "ado",
                DslCaseRule.AsciiFold,
                (probe, cs) => DslFuzzProbe.Ado(probe, cs),
                excludes: null
            ),
            SqliteFile(
                "efcore",
                DslCaseRule.Indeterminate,
                (probe, cs) =>
                    DslFuzzProbe.EfCore(
                        probe,
                        options => options.UseSqlite(cs).IgnoreServiceProviderCacheLimit()
                    ),
                ExcludedOnEfCoreSqlite
            ),
            DslFuzzBackend.InMemory,
        ]
    );

    /// <summary>MySQL の図（インメモリだけ）</summary>
    private static readonly DslFuzzTarget MySqlTarget = new(
        Suite,
        "mysql",
        new MySqlProvider(),
        unit => Options(unit, ado: false),
        AdoRegistration: null,
        EfCore: false,
        InMemory: true,
        [DslFuzzBackend.InMemory]
    );

    [Fact(
        DisplayName = "簡易 DSL の条件の総当たり: 生成→実コンパイル（警告 0）→3 実装先の結果が docs の意味と一致する（既知の失敗の一覧と厳密に一致）"
    )]
    public async Task DslConditions_MatchDocumentedSemantics()
    {
        var ledger = new FuzzFailureLedger(DslFuzzKnownFailures.All);

        foreach (var seed in NameFuzz.Seeds)
        {
            foreach (var unit in DslFuzzUnits.Sqlite(seed))
            {
                await DslFuzzRunner.RunAsync(ledger, unit, SqliteTarget, Ct);
            }

            foreach (var unit in DslFuzzUnits.MySql(seed))
            {
                await DslFuzzRunner.RunAsync(ledger, unit, MySqlTarget, Ct);
            }
        }

        foreach (var unit in DslFuzzUnits.Collisions())
        {
            await DslFuzzRunner.RunAsync(ledger, unit, SqliteTarget, Ct);
        }

        ledger.AssertMatchesKnown($"{Suite}-generate:", $"{Suite}-compile:", $"{Suite}-exec:");
    }

    /// <summary>
    /// EF Core（SQLite）で流さないクエリか: decimal の列の順序比較を含むもの
    /// </summary>
    /// <remarks>
    /// EF Core の SQLite プロバイダは decimal のサーバー側の順序比較・並び替えを翻訳できず例外にする（プロバイダの既知の制限＝
    /// CLAUDE.md の名前付きクエリの節「EF Core Sqlite は decimal のサーバーサイド比較・並び替え非対応」）。
    /// QuickER の不具合ではないので、この 1 か所で EF Core（SQLite）の実行から外す（ほかの実装先では流す）。
    /// </remarks>
    private static bool ExcludedOnEfCoreSqlite(DslQuery query) =>
        query.Atoms.Any(atom =>
            atom is DslCompare { Op: not (DslOp.Eq or DslOp.Ne) }
            && atom.Column.Clr == DslClr.Decimal
        );

    private static CodeGenerationOptions Options(DslFuzzUnit unit, bool ado) =>
        new()
        {
            RootNamespace = "Sample.Domain",
            GenerateRepositories = ado,
            RepositoryDialects = ado ? ["sqlite"] : [],
            GenerateEfCoreRepositories = ado,
            GenerateInMemoryRepositories = true,
            GenerateValueObjects = unit.ValueObjects,
            UseGuidKeyForStringPrimaryKey = unit.Key.Clr == DslClr.GuidKey,
            GenerateEditModels = false,
            GenerateMappers = false,
        };

    /// <summary>単位ごとに新しい SQLite のファイルへ DDL を当てて開く実装先</summary>
    private static DslFuzzBackend SqliteFile(
        string name,
        DslCaseRule rule,
        Func<Type, string, Microsoft.Extensions.DependencyInjection.ServiceProvider> build,
        Func<DslQuery, bool>? excludes
    ) =>
        new(
            name,
            rule,
            async (probe, diagram, ct) =>
            {
                var path = Path.Combine(
                    Path.GetTempPath(),
                    $"quicker-dsl-{name}-{Guid.NewGuid():N}.db"
                );
                var cs = new SqliteConnectionStringBuilder
                {
                    DataSource = path,
                    Pooling = false,
                }.ToString();

                await using (var connection = new SqliteConnection(cs))
                {
                    await connection.OpenAsync(ct);
                    await using var command = connection.CreateCommand();
                    command.CommandText = new SqliteDdlGenerator().Build(diagram);
                    await command.ExecuteNonQueryAsync(ct);
                }

                return new DslFuzzSession(
                    build(probe, cs),
                    () =>
                    {
                        File.Delete(path);
                        return ValueTask.CompletedTask;
                    }
                );
            },
            excludes
        );
}
