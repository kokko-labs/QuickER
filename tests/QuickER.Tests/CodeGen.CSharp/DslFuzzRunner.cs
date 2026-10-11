using System.IO;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>総当たりを流す方言の構成（生成オプション・検証コード・実装先）</summary>
/// <param name="Suite">失敗のキーの入口の接頭辞（テストクラスごとに分け、各テストは自分の接頭辞だけを照合する）</param>
/// <param name="Dbms">図の対象 DBMS</param>
/// <param name="Provider">型を解決する方言プロバイダ</param>
/// <param name="Options">単位ごとの生成オプション</param>
/// <param name="AdoRegistration">QuickER 版 Repository の DI 登録メソッド名（生成しないなら null）</param>
/// <param name="EfCore">EF Core 版 Repository を生成するか</param>
/// <param name="InMemory">インメモリ Repository を生成するか</param>
/// <param name="Backends">実行する実装先（行の入れ方・大文字小文字の扱い）</param>
internal sealed record DslFuzzTarget(
    string Suite,
    string Dbms,
    IDatabaseProvider Provider,
    Func<DslFuzzUnit, CodeGenerationOptions> Options,
    string? AdoRegistration,
    bool EfCore,
    bool InMemory,
    IReadOnlyList<DslFuzzBackend> Backends
)
{
    /// <summary>生成物と一緒にコンパイルする、実装先の DI を組み立てるだけの検証コード</summary>
    public string ProbeSource =>
        string.Join(
            "\n",
            "using System;",
            "using Microsoft.EntityFrameworkCore;",
            "using Microsoft.Extensions.DependencyInjection;",
            "namespace Sample.Domain;",
            $"public static class {DslFuzzProbe.TypeName}",
            "{",
            AdoRegistration is null
                ? ""
                : $"    public static ServiceProvider Ado(string cs) => new ServiceCollection().{AdoRegistration}(cs).BuildServiceProvider();",
            EfCore
                ? "    public static ServiceProvider EfCore(Action<DbContextOptionsBuilder> configure) => new ServiceCollection().AddGeneratedEfCoreRepositories(configure).BuildServiceProvider();"
                : "",
            InMemory
                ? "    public static ServiceProvider InMemory() => new ServiceCollection().AddGeneratedInMemoryRepositories(seedSampleData: false).BuildServiceProvider();"
                : "",
            "}"
        );
}

/// <summary>1 つの実装先</summary>
/// <param name="Name">名前（失敗のキーに使う）</param>
/// <param name="Rule">文字列の大文字小文字・末尾の空白の扱い（docs の表から写す）</param>
/// <param name="OpenAsync">検証コードの型と図から、空のテーブルを用意した DI のコンテナを開く</param>
/// <param name="Excludes">この実装先で流さないクエリ（理由は判定を定義した側の XmlDoc に書く）</param>
internal sealed record DslFuzzBackend(
    string Name,
    DslCaseRule Rule,
    Func<Type, ErDiagram, CancellationToken, Task<DslFuzzSession>> OpenAsync,
    Func<DslQuery, bool>? Excludes = null
)
{
    /// <summary>インメモリ Repository（序数で比べる）</summary>
    public static DslFuzzBackend InMemory { get; } =
        new(
            "inmemory",
            DslCaseRule.Ordinal,
            (probe, _, _) => Task.FromResult(new DslFuzzSession(DslFuzzProbe.InMemory(probe)))
        );
}

/// <summary>開いた実装先（DI のコンテナと、データベースの後始末）</summary>
internal sealed class DslFuzzSession(ServiceProvider services, Func<ValueTask>? cleanup = null)
    : IAsyncDisposable
{
    public ServiceProvider Services => services;

    public async ValueTask DisposeAsync()
    {
        await services.DisposeAsync();

        if (cleanup is not null)
        {
            await cleanup();
        }
    }
}

/// <summary>検証コード（<see cref="DslFuzzTarget.ProbeSource"/>）の呼び出し</summary>
internal static class DslFuzzProbe
{
    /// <summary>検証コードの型名</summary>
    public const string TypeName = "FuzzProbe";

    public static ServiceProvider Ado(Type probe, string connectionString) =>
        (ServiceProvider)probe.GetMethod("Ado")!.Invoke(null, [connectionString])!;

    /// <summary>EF Core 版 Repository の DI のコンテナを開く</summary>
    /// <remarks>
    /// 総当たりは単位ごとに別のアセンブリ（別のモデル）を作るので、EF Core の内部のサービスプロバイダーがプロセスの中で
    /// 20 を超え、EF Core が <see cref="CoreEventId.ManyServiceProvidersCreatedWarning"/> を例外にする
    /// （同じプロセスで先に流れた EF Core のテストと合わせて数えるため、全件の実行でだけ落ちる）。テストの構成だけの事情なので、
    /// この警告は全実装先・全方言でここ 1 か所で無視する。
    /// </remarks>
    public static ServiceProvider EfCore(Type probe, Action<DbContextOptionsBuilder> configure) =>
        (ServiceProvider)
            probe
                .GetMethod("EfCore")!
                .Invoke(
                    null,
                    [
                        (Action<DbContextOptionsBuilder>)(
                            options =>
                            {
                                configure(options);
                                options.ConfigureWarnings(warnings =>
                                    warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)
                                );
                            }
                        ),
                    ]
                )!;

    public static ServiceProvider InMemory(Type probe) =>
        (ServiceProvider)probe.GetMethod("InMemory")!.Invoke(null, [])!;
}

/// <summary>
/// 簡易 DSL の条件の総当たりの本体: 1 単位を生成→実コンパイル→診断のクエリへの引き当て→実行→照合する
/// </summary>
/// <remarks>
/// <para>
/// 失敗のキーの入口は <c>{Suite}-generate:{単位}</c>（生成時に断られた）・<c>{Suite}-compile:{単位}</c>（コンパイラの診断）・
/// <c>{Suite}-exec:{実装先}:{単位}</c>（オラクルとの食い違い・例外）。
/// </para>
/// <para>
/// コンパイラの診断は出た位置からクエリ（メソッド・射影の型）へ引き当て、そのクエリの原子が持つ「その診断を起こし得る既知の原因」
/// （<see cref="DslAtom.KnownDiagnostics"/>）で原因の名前を付ける。当たらなければ <c>unexplained</c>（新しい失敗）。
/// エラーの出たクエリは外して生成し直し、残りを実行する。
/// 実装先・単位ごとに照合したクエリが <see cref="MinimumCheckedQueries"/> に届かなければそれも失敗にする（黙って空振りしない）。
/// <c>QUICKER_DSL_FUZZ_DUMP</c> にフォルダを指定すると生成物を書き出す。
/// </para>
/// </remarks>
internal static class DslFuzzRunner
{
    /// <summary>実装先・単位ごとに、結果を照合したクエリの最小数（GuidKey の単位で約 120）</summary>
    public const int MinimumCheckedQueries = 100;

    /// <summary>1 単位を生成→コンパイル→実行する</summary>
    public static async Task RunAsync(
        FuzzFailureLedger ledger,
        DslFuzzUnit unit,
        DslFuzzTarget target,
        CancellationToken ct
    )
    {
        var queries = unit.Queries.ToList();

        // エラーの出たクエリを外して生成し直す（外すのは最大 3 回）
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var current = unit with { Queries = queries };
            var generated = Generate(current, target);
            var rejected = RecordGenerationDiagnostics(ledger, target.Suite, current, generated);

            if (generated.HasErrors)
            {
                ledger.Record(
                    new($"{target.Suite}-generate:{unit.Name}", "generation-failed", null),
                    string.Join(" / ", generated.Diagnostics.Select(d => d.Message))
                );

                return;
            }

            Dump(current, generated);
            var compiled = GeneratedCodeCompiler.Compile(
                generated,
                $"Dsl.{unit.Name}.{Guid.NewGuid():N}"
            );
            var broken = RecordCompileDiagnostics(ledger, target.Suite, current, compiled);
            queries = queries
                .Where(q => !broken.Contains(q.Name) && !rejected.Contains(q.Name))
                .ToList();

            if (compiled.Errors.Count == 0)
            {
                break;
            }

            // クエリへ引き当てられないエラーが残るなら実行できない（診断は記録済み）
            if (broken.Count == 0 || attempt == 2)
            {
                return;
            }
        }

        if (unit.Rows.Count == 0)
        {
            return;
        }

        var executable = unit with { Queries = queries };
        var assembly = GeneratedCodeCompiler.CompileAndLoad(
            Generate(executable, target),
            $"Dsl.Exec.{unit.Name}.{Guid.NewGuid():N}",
            target.ProbeSource
        );
        var executed = await DslFuzzExecution.RunAsync(
            ledger,
            executable,
            target,
            assembly,
            executable.BuildDiagram(target.Dbms),
            ct
        );

        // 実装先ごとに照合したクエリが少なすぎれば失敗にする（引き当て・実行の不具合で黙って空振りしない）
        foreach (var backend in target.Backends)
        {
            var count = executed.GetValueOrDefault(backend.Name);

            if (count < MinimumCheckedQueries)
            {
                ledger.Record(
                    new(
                        $"{target.Suite}-exec:{backend.Name}:{unit.Name}",
                        "too-few-queries-checked",
                        null
                    ),
                    $"seed={unit.Seed} checked={count}"
                );
            }
        }
    }

    private static CodeGenerationResult Generate(DslFuzzUnit unit, DslFuzzTarget target) =>
        DiagramCodeGenerator.Generate(
            target.Provider.TypeMapper,
            target.Provider.TypeCatalog,
            unit.BuildDiagram(target.Dbms),
            target.Options(unit)
        );

    /// <summary>
    /// 調べるための書き出し: 環境変数 <c>QUICKER_DSL_FUZZ_DUMP</c> にフォルダを指定すると、単位ごとに生成物を書き出す
    /// </summary>
    private static void Dump(DslFuzzUnit unit, CodeGenerationResult generated)
    {
        if (Environment.GetEnvironmentVariable("QUICKER_DSL_FUZZ_DUMP") is not { Length: > 0 } root)
        {
            return;
        }

        var folder = Path.Combine(root, $"{unit.Name}-seed{unit.Seed}");
        Directory.CreateDirectory(folder);

        foreach (var file in generated.Files)
        {
            File.WriteAllText(Path.Combine(folder, file.FileName), file.Content);
        }
    }

    /// <summary>生成時に断られた（警告で飛ばされた）クエリを記録し、その名前を返す</summary>
    private static HashSet<string> RecordGenerationDiagnostics(
        FuzzFailureLedger ledger,
        string suite,
        DslFuzzUnit unit,
        CodeGenerationResult generated
    )
    {
        var rejected = new HashSet<string>(StringComparer.Ordinal);

        foreach (
            var diagnostic in generated.Diagnostics.Where(d =>
                d.Severity != GenerationDiagnosticSeverity.Info
            )
        )
        {
            var query = unit.Queries.FirstOrDefault(q =>
                Regex.IsMatch(
                    diagnostic.Message,
                    $@"(?<![A-Za-z0-9_]){Regex.Escape(q.Name)}(?![A-Za-z0-9_])"
                )
            );

            if (query is not null)
            {
                rejected.Add(query.Name);
            }

            // 型の検査が断ると決まっているリテラルの警告は期待どおり（記録しない）
            if (
                query is not null
                && diagnostic.Severity == GenerationDiagnosticSeverity.Warning
                && ExpectsRejection(query)
            )
            {
                continue;
            }

            ledger.Record(
                new(
                    $"{suite}-generate:{unit.Name}",
                    $"query-{diagnostic.Severity.ToString().ToLowerInvariant()}",
                    query is null ? "unattributed" : FirstKind(query)
                ),
                $"seed={unit.Seed} {query?.Name}: {(query is null ? "" : DslFuzz.Render(query.Condition))} → {diagnostic.Message}"
            );
        }

        // 断られるはずのリテラルが通った（警告なしで生成された）
        foreach (
            var query in unit.Queries.Where(q => ExpectsRejection(q) && !rejected.Contains(q.Name))
        )
        {
            ledger.Record(
                new(
                    $"{suite}-generate:{unit.Name}",
                    "expected-rejection-missing",
                    FirstKind(query)
                ),
                $"seed={unit.Seed} {query.Name}: {DslFuzz.Render(query.Condition)}"
            );
            rejected.Add(query.Name);
        }

        return rejected;
    }

    /// <summary>コンパイラの診断を記録し、エラーの出たクエリの名前を返す</summary>
    private static HashSet<string> RecordCompileDiagnostics(
        FuzzFailureLedger ledger,
        string suite,
        DslFuzzUnit unit,
        GeneratedCodeCompilationResult compiled
    )
    {
        var broken = new HashSet<string>(StringComparer.Ordinal);

        // 射影の型名を衝突させた単位は、診断が衝突の相手の型（エンティティ・リポジトリ・値オブジェクト・Task）の側にも
        // 散らばり位置でクエリへ引き当てられないので、XmlDoc の cref 以外はまとめて衝突の原因とし、単位ごと外す
        var collision = unit
            .Queries.FirstOrDefault(q => q.TypeNameCollision is not null)
            ?.TypeNameCollision;

        foreach (var diagnostic in compiled.Errors.Concat(compiled.Warnings))
        {
            var isError = diagnostic.Severity == DiagnosticSeverity.Error;
            var query = collision is null ? AttributeToQuery(diagnostic, unit) : null;
            var difference =
                diagnostic.Id == "CS1574" ? "CS1574:xmldoc-cref"
                : collision is not null ? $"projection-type-name-collision:{collision}"
                : $"{diagnostic.Id}:{Cause(diagnostic, query, unit)}";

            if (isError && query is not null)
            {
                broken.Add(query.Name);
            }

            ledger.Record(
                new(
                    $"{suite}-compile:{unit.Name}",
                    isError ? "compile-error" : "compile-warning",
                    difference
                ),
                $"seed={unit.Seed} {query?.Name}: {(query is null ? "" : DslFuzz.Render(query.Condition))} → [{diagnostic.Id}] {diagnostic.GetMessage()}"
            );
        }

        if (collision is not null && compiled.Errors.Count > 0)
        {
            foreach (var query in unit.Queries)
            {
                broken.Add(query.Name);
            }
        }

        return broken;
    }

    /// <summary>診断の原因の名前（クエリの原子が持つ既知の原因・当たらなければ unexplained）</summary>
    private static string Cause(Diagnostic diagnostic, DslQuery? query, DslFuzzUnit unit) =>
        query is null
            ? "unattributed"
            : query
                .KnownDiagnostics(unit.ValueObjects)
                .Where(known => known.Code == diagnostic.Id)
                .Select(known => known.Cause)
                .FirstOrDefault()
                ?? "unexplained";

    /// <summary>診断の位置を囲むメソッド・型の名前から、クエリを引き当てる</summary>
    private static DslQuery? AttributeToQuery(Diagnostic diagnostic, DslFuzzUnit unit)
    {
        if (diagnostic.Location.SourceTree is not { } tree)
        {
            return null;
        }

        var node = tree.GetRoot().FindNode(diagnostic.Location.SourceSpan);

        foreach (var ancestor in node.AncestorsAndSelf())
        {
            var name = ancestor switch
            {
                MethodDeclarationSyntax method => method.Identifier.Text,
                TypeDeclarationSyntax type => type.Identifier.Text,
                // 射影の選択式は「_{メソッド名}Selector」のフィールドへ出る
                VariableDeclaratorSyntax field => field.Identifier.Text.TrimStart('_') is var text
                && text.EndsWith("Selector", StringComparison.Ordinal)
                    ? text[..^"Selector".Length]
                    : null,
                _ => null,
            };

            if (name is null)
            {
                continue;
            }

            // メソッド名はクエリ名をパスカルケースにしたもの（A1n → A1NAsync）
            var query = unit.Queries.FirstOrDefault(q =>
                string.Equals(name, q.Name + "Async", StringComparison.OrdinalIgnoreCase)
                || name == q.ResultTypeName
            );

            if (query is not null)
            {
                return query;
            }
        }

        return null;
    }

    /// <summary>型の検査が断ると決まっているリテラルを含むクエリか</summary>
    private static bool ExpectsRejection(DslQuery query) =>
        query.Atoms.Any(atom => atom is DslCompare { RejectedByTypeCheck: true });

    /// <summary>クエリの最初の原子の種類（生成時の拒否のキー）</summary>
    private static string FirstKind(DslQuery query) =>
        query.IsAtomQuery ? query.Atoms.First().Kind : $"composition:{query.Returns}";
}
