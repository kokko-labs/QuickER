using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// 名前の総当たり（<see cref="NameFuzz"/>）の名前をテーブル名・列名・一意制約名・名前付きクエリ名・パラメータ名に置いた図から
/// C# を生成し、生成時エラーで名指しして断るか、生成物がエラー 0・警告 0 でコンパイルできることを確かめるテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// 図は MCP のツールで作る（<see cref="NameFuzzDiagrams.BuildAll"/>＝ツールが受け付けた名前だけが図に入る）。
/// 黙ったコンパイル失敗（生成時の診断が無いのにコンパイルできない）と、生成コードの警告を失敗として数える。
/// 名前ごとの生成・コンパイルは遅いので、名前は 1 つの図にまとめ、構成 2 つでだけ流す。
/// </para>
/// <para>結果は既知の失敗の一覧（<see cref="NameFuzzKnownFailures"/>）と厳密に照合する。</para>
/// </remarks>
public sealed class NameFuzzCompilationTests
{
    /// <summary>生成の構成</summary>
    public enum Config
    {
        /// <summary>SQL Server の QuickER 版 Repository・EF Core・インメモリ・値オブジェクト・EditModel・Mapper・リモートサービス</summary>
        SqlServerFull,

        /// <summary>SQLite の QuickER 版 Repository・インメモリ・EditModel（値オブジェクトなし）</summary>
        SqlitePlain,
    }

    [Theory(
        DisplayName = "名前の総当たり: 生成物は生成時エラーで断るか、エラー 0・警告 0 でコンパイルできる"
    )]
    [InlineData(Config.SqlServerFull)]
    [InlineData(Config.SqlitePlain)]
    public void GeneratedCode_CompilesWithoutWarnings(Config config)
    {
        var ledger = new FuzzFailureLedger(NameFuzzKnownFailures.All);
        var entrance = $"compile:{config}";
        var (dialect, provider, options) = Configure(config);
        var (diagram, names) = NameFuzzDiagrams.BuildAll(
            dialect,
            NameFuzzDiagrams.NamesForRun().Concat(CaseOnlyNames)
        );
        AddQueries(diagram, names);

        // 図を作れずに名前が入らないまま緑にならないよう、代表の名前の半分以上は入っていること
        Assert.True(
            names.Count >= NameFuzzDiagrams.Representative.Count / 2,
            $"図に入った名前が少なすぎる: {names.Count}"
        );

        var (result, removed) = GenerateDroppingNamedCollisions(provider, diagram, options);
        var example =
            "names: "
            + string.Join(", ", names.Select(NameFuzz.Describe))
            + " / dropped by named errors: "
            + string.Join(", ", removed.Select(NameFuzz.Describe));

        if (result.HasErrors)
        {
            ledger.Record(
                new(entrance, "generation-error", null),
                example
                    + " / "
                    + string.Join(
                        " / ",
                        result
                            .Diagnostics.Where(d =>
                                d.Severity == GenerationDiagnosticSeverity.Error
                            )
                            .Select(d => d.Message)
                    )
            );
        }
        else
        {
            var compiled = GeneratedCodeCompiler.Compile(
                result,
                $"QuickER.NameFuzz.{config}.{Guid.NewGuid():N}"
            );

            foreach (var group in compiled.Errors.GroupBy(error => error.Id))
            {
                ledger.Record(
                    new(entrance, "compile-error", group.Key),
                    $"{example} / {group.First()}"
                );
            }

            foreach (var group in compiled.Warnings.GroupBy(warning => warning.Id))
            {
                ledger.Record(
                    new(entrance, "compile-warning", group.Key),
                    $"{example} / {group.First()} / {SourceLine(result, group.First())}"
                );
            }

            // コンパイルでは分からない実行時の失敗（大文字小文字を区別しない JSON とルーティング）も見る
            if (config == Config.SqlServerFull && compiled.Success)
            {
                RecordRuntimeProblems(ledger, entrance, result, config, example);
            }
        }

        ledger.AssertMatchesKnown(entrance);
    }

    /// <summary>
    /// どの種でも必ず図へ置く、大文字小文字だけ違う生成名（エンティティのメンバー名・リモートのルート名）を作る名前
    /// </summary>
    /// <remarks>
    /// 図の表は列 <c>Id</c> と、表と同じ名前の列を持つ。<c>i_d</c> は <c>Id</c> と大小だけ違う <c>ID</c> を、<c>rowstate</c> は基底の
    /// <c>RowState</c> と大小だけ違う <c>Rowstate</c> を作り、<c>case_route</c> と <c>caseroute</c> はルート名 <c>CaseRoute</c> と
    /// <c>Caseroute</c> を作る。生成時のエラーが表を名指しして外すのが正しく、止めないと実行時の段
    /// （<see cref="RecordRuntimeProblems"/>）が JSON の設定の組み立ての失敗・エンドポイントの重なりとして記録する。
    /// </remarks>
    private static readonly string[] CaseOnlyNames = ["i_d", "rowstate", "case_route", "caseroute"];

    /// <summary>
    /// 生成物を読み込み、全エンティティの JSON の設定（リモート・SQL Server の FOR JSON）の組み立てと、
    /// リモートのエンドポイントの大文字小文字だけ違うパターンを確かめる
    /// </summary>
    private static void RecordRuntimeProblems(
        FuzzFailureLedger ledger,
        string entrance,
        CodeGenerationResult result,
        Config config,
        string example
    )
    {
        var assembly = GeneratedCodeCompiler.CompileAndLoad(
            result,
            $"QuickER.NameFuzz.Runtime.{config}.{Guid.NewGuid():N}"
        );

        foreach (var (kind, detail) in GeneratedRuntimeProbe.FindJsonProblems(assembly))
        {
            ledger.Record(new(entrance, kind, null), $"{example} / {detail}");
        }

        foreach (var collision in GeneratedRuntimeProbe.FindRouteCaseCollisions(assembly))
        {
            ledger.Record(new(entrance, "route-case-collision", null), $"{example} / {collision}");
        }
    }

    /// <summary>診断の位置の生成コードの行（報告用）</summary>
    private static string SourceLine(
        CodeGenerationResult result,
        Microsoft.CodeAnalysis.Diagnostic diagnostic
    )
    {
        var span = diagnostic.Location.GetLineSpan();
        var file = result.Files.FirstOrDefault(f => f.FileName == span.Path);
        var lines = file?.Content.Split((char)10);

        return lines is not null && span.StartLinePosition.Line < lines.Length
            ? lines[span.StartLinePosition.Line].Trim()
            : string.Empty;
    }

    /// <summary>
    /// 生成し、生成時エラーがテーブルを名指ししていれば（生成する識別子が衝突する名前の組＝名指しして断るのは正しい）、
    /// 名指しされた最後のテーブルを図から外して生成し直す。名指しの無いエラーが残れば、その結果を返す
    /// </summary>
    /// <returns>最後の生成結果と、外したテーブル名</returns>
    private static (
        CodeGenerationResult Result,
        List<string> Removed
    ) GenerateDroppingNamedCollisions(
        IDatabaseProvider provider,
        ErDiagram diagram,
        CodeGenerationOptions options
    )
    {
        var removed = new List<string>();

        while (true)
        {
            var result = DiagramCodeGenerator.Generate(
                provider.TypeMapper,
                provider.TypeCatalog,
                diagram,
                options
            );
            var named = result
                .Diagnostics.Where(d => d.Severity == GenerationDiagnosticSeverity.Error)
                .Select(d =>
                    diagram.Entities.LastOrDefault(e =>
                        d.Message.Contains($"'{e.TableName}'", StringComparison.Ordinal)
                    )
                )
                .FirstOrDefault(entity => entity is not null);

            if (!result.HasErrors || named is null)
            {
                return (result, removed);
            }

            diagram.Entities.Remove(named);
            diagram.Queries.RemoveAll(query => query.EntityId == named.Id);
            removed.Add(named.TableName);
        }
    }

    /// <summary>名前ごとに、その名前のクエリ（その名前のパラメータを 1 つ持つ一覧の戻り形）を足す（定義の規則で断られる名前は飛ばす）</summary>
    private static void AddQueries(ErDiagram diagram, IReadOnlyList<string> names)
    {
        foreach (var name in names)
        {
            var entity = diagram.Entities.Single(e => e.TableName == name.Trim());
            var query = new QueryDefinition
            {
                EntityId = entity.Id,
                Name = name,
                Returns = QueryReturnShape.List,
                Parameters = [new QueryParameter { Name = name, Type = "int32" }],
                Condition = null,
            };
            var violations = QuickER.CodeGen.CSharp.Queries.QueryDefinitionRules.Validate(
                entity,
                query,
                diagram.Queries
            );

            if (violations.Count == 0)
            {
                diagram.Queries.Add(query);
            }
        }
    }

    private static (
        string Dialect,
        IDatabaseProvider Provider,
        CodeGenerationOptions Options
    ) Configure(Config config) =>
        config switch
        {
            Config.SqlServerFull => (
                "sqlserver",
                new SqlServerProvider(),
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    GenerateRepositories = true,
                    RepositoryDialects = ["sqlserver"],
                    GenerateEfCoreRepositories = true,
                    GenerateInMemoryRepositories = true,
                    GenerateValueObjects = true,
                    GenerateEditModels = true,
                    GenerateMappers = true,
                    GenerateRemoteServices = true,
                }
            ),
            _ => (
                "sqlite",
                new SqliteProvider(),
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    GenerateRepositories = true,
                    RepositoryDialects = ["sqlite"],
                    GenerateInMemoryRepositories = true,
                    GenerateValueObjects = false,
                    GenerateEditModels = true,
                    GenerateMappers = true,
                }
            ),
        };
}
