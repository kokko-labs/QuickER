using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.MySql;
using QuickER.Provider.Oracle;
using QuickER.Provider.PostgreSql;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;
using QuickER.Tests.CodeGen.CSharp.CodegenConfig.Judges;
using QuickER.Tests.TestSupport.Engines;
using Xunit.Sdk;
using static QuickER.Tests.CodeGen.CSharp.CodegenConfig.CodegenConfigDimensions;

namespace QuickER.Tests.CodeGen.CSharp.CodegenConfig;

/// <summary>エンジンへ渡す 1 つの入力</summary>
/// <param name="Label">失敗の文に添える名前（行の Id と水準・決まった行の Id）</param>
/// <param name="Diagram">図</param>
/// <param name="TypeDialect">図の方言（型解決のプロバイダ）</param>
/// <param name="Options">行のオプション（入口へ渡す前）</param>
/// <param name="Entrance">入口</param>
/// <param name="ManualSources">手動実装のクエリのソース（実効のオプションから作る。無ければ空）</param>
/// <param name="FreshDiagram">図を組み直す（決定的の関係が使う。null なら <paramref name="Diagram"/> をそのまま使う）</param>
internal sealed record CodegenInput(
    string Label,
    ErDiagram Diagram,
    string TypeDialect,
    CodeGenerationOptions Options,
    CodegenEntrance Entrance,
    Func<CodeGenerationOptions, IReadOnlyList<GeneratedFile>> ManualSources,
    Func<ErDiagram>? FreshDiagram = null
);

/// <summary>1 つの入力に掛ける期待（実効のオプションが決まってから作る）</summary>
/// <param name="Diagnostics">診断の期待</param>
/// <param name="Files">ファイル構成の判定（null なら見ない）</param>
/// <param name="Namespaces">名前空間の規則を掛けるか</param>
/// <param name="Compile">コンパイルするか</param>
/// <param name="Semantics">意味の規則を掛けるか（図の形の行だけ）</param>
/// <param name="Probes">探針</param>
/// <param name="Relations">
/// 関係（方言の指定順は生成がエラーでも診断を比べるので常に、ほかは生成が通ったときだけ掛ける）
/// </param>
/// <param name="ProbeFacts">決まった行が探針へ渡す追加の事実</param>
/// <param name="ComparisonOptions">オプションが効く関係の比較用のオプション</param>
/// <param name="ExactRuntimeTypeSets">Runtime 系の型集合の関係で、型の集合がパッケージと同じであるべきファイル名</param>
/// <param name="CompileAsOneAssembly">層別出力も 1 アセンブリへまとめてコンパイルする（決まった行だけ。<see cref="CompileJudge.Check"/>）</param>
internal sealed record CodegenExpectation(
    DiagnosticExpectation Diagnostics,
    Func<CodeGenerationOptions, CodeGenerationResult, IReadOnlyList<string>>? Files,
    bool Namespaces,
    bool Compile,
    bool Semantics,
    IReadOnlyList<CodegenProbe> Probes,
    IReadOnlyList<CodegenRelation> Relations,
    CodegenProbeFacts? ProbeFacts = null,
    CodeGenerationOptions? ComparisonOptions = null,
    IReadOnlyList<string>? ExactRuntimeTypeSets = null,
    bool CompileAsOneAssembly = false
)
{
    /// <summary>生成が成功する（エラーを期待しない）か</summary>
    public bool ExpectsSuccess => !Diagnostics.ExpectsError;
}

/// <summary>
/// 1 行の流れ：図 → 入口（オプションを作る・生成する）→ 判定（診断 → ファイル構成・名前空間 → コンパイル → 意味 → 探針 → 関係）
/// </summary>
/// <remarks>
/// 判定は問題を集めて最後にまとめて赤にする（前の段の赤が後の段を隠さない）。最後に、期待した段が実際に走ったことを表明する
/// （黙った空振りを止める）。
/// </remarks>
internal static class CodegenRunner
{
    /// <summary>行のルート名前空間（固定）</summary>
    public const string RootNamespace = "Sample.Domain";

    /// <summary>診断の段</summary>
    public const string DiagnosticsStage = "diagnostics";

    /// <summary>ファイル構成の段</summary>
    public const string FilesStage = "files";

    /// <summary>名前空間の段</summary>
    public const string NamespacesStage = "namespaces";

    /// <summary>コンパイルの段</summary>
    public const string CompileStage = "compile";

    /// <summary>意味の段</summary>
    public const string SemanticsStage = "semantics";

    /// <summary>
    /// 5 プロバイダの登録簿（<c>GenerationExecutor</c> と同じ組み）
    /// </summary>
    public static DatabaseProviderRegistry Providers { get; } =
        new([
            new SqlServerProvider(),
            new PostgreSqlProvider(),
            new MySqlProvider(),
            new OracleProvider(),
            new SqliteProvider(),
        ]);

    /// <summary>
    /// 本番と同じ経路で生成する（図の方言のプロバイダ＋実効方言ごとの型マッパ＝<c>GenerationExecutor.ResolveDialectTypeMappers</c> と同じ規則）
    /// </summary>
    /// <param name="diagram">図</param>
    /// <param name="typeDialect">図の方言</param>
    /// <param name="options">生成オプション</param>
    public static CodeGenerationResult Generate(
        ErDiagram diagram,
        string typeDialect,
        CodeGenerationOptions options
    )
    {
        var provider = Providers.Get(typeDialect);
        var mappers = new Dictionary<string, IColumnTypeMapper>(StringComparer.OrdinalIgnoreCase);

        foreach (var dialect in options.ResolveRepositoryDialects())
        {
            if (Providers.TryGet(dialect, out var dialectProvider))
            {
                mappers[dialect] = dialectProvider.TypeMapper;
            }
        }

        return DiagramCodeGenerator.Generate(
            provider.TypeMapper,
            provider.TypeCatalog,
            mappers,
            diagram,
            options
        );
    }

    /// <summary>網羅の配列の行を流す（期待は規則から作る）</summary>
    /// <param name="row">行</param>
    /// <param name="requireSuccess">
    /// Success の表の行か（領域の述語の中の行なので、規則がエラーを予測しても、生成がエラーになっても赤＝述語と製品の食い違い）
    /// </param>
    public static Task RunRowAsync(CodegenRow row, bool requireSuccess = false)
    {
        var shape = new ShapeDiagram(row.Shape);
        var input = new CodegenInput(
            row.Describe(),
            shape.Diagram,
            row.Shape.TypeDialect,
            row.Options(),
            row.Entrance,
            shape.ManualSources,
            () => new ShapeDiagram(row.Shape).Diagram
        );

        return RunAsync(
            input,
            options => MatrixExpectation(shape, options),
            shape,
            requireSuccess,
            row.Id
        );
    }

    /// <summary>
    /// 行の入口が生成へ渡すはずのオプション（GUI は画面に出ていないオプションを落とす＝<see cref="GuiOptionRules"/>。
    /// ほかの入口は行のオプションのまま）。領域の述語が使う（GUI の入口の段は実際の <c>ToOptions()</c> をこれと照合する）
    /// </summary>
    /// <param name="row">行</param>
    public static CodeGenerationOptions EffectiveOptions(CodegenRow row) =>
        row.Entrance == CodegenEntrance.Gui
            ? GuiOptionRules.Expected(row.Options())
            : row.Options();

    /// <summary>図の形の行の期待（規則の表から作る）</summary>
    /// <remarks>
    /// 探針・関係を掛ける条件はここだけに書く（網羅の配列の行の並び・次元表・領域の述語は変えない）。決まった行だけに掛ける関係
    /// （<see cref="Judges.Relations.CasesOnly"/>）は掛けない。
    /// </remarks>
    /// <param name="shape">図の形</param>
    /// <param name="options">実効の生成オプション</param>
    public static CodegenExpectation MatrixExpectation(
        ShapeDiagram shape,
        CodeGenerationOptions options
    )
    {
        var probes = new List<CodegenProbe>();

        if (
            options.GenerateValueObjects
            && options.UseGuidKeyForStringPrimaryKey
            && shape.Levels.Key == KeyLevels.String
        )
        {
            probes.Add(CodegenProbe.GuidKeyCreate);
        }

        if (options.GenerateRepositories && shape.Levels.DbGenerated != DbGeneratedLevels.None)
        {
            probes.Add(CodegenProbe.SaveMetadataWriteExclusion);
        }

        if (options.GenerateInMemoryRepositories)
        {
            probes.Add(CodegenProbe.InMemorySeed);
        }

        if (options.GenerateEfCoreRepositories)
        {
            probes.Add(CodegenProbe.EfCoreModel);
        }

        if (options.GenerateSyncSupport)
        {
            probes.Add(CodegenProbe.SyncKeyText);
        }

        var relations = new List<CodegenRelation>();
        var split = CodegenOptionFacts.Split(options);

        // 方言の指定順の関係は、元の DialectOrderInvarianceTests と同じ「分割出力・インライン（パッケージ参照モードでない）」に限る。
        // パッケージ参照モードの生成ヘッダーの PackageReference の案内の行は指定順に並び（内容ハッシュの行も変わる）、
        // CLAUDE.md・docs はその並びを約束していない（1 ファイル出力・.g.md と同じく製品の判断の候補として記録だけ残す）
        if (CodegenOptionFacts.Dialects(options).Count >= 2 && split && !options.UseRuntimePackages)
        {
            relations.Add(CodegenRelation.DialectOrder);
        }

        if (options.LayeredOutput)
        {
            relations.Add(CodegenRelation.LayeredVsSplit);
        }

        // サブフォルダは全出力モードで有効（src/QuickER.CodeGen.CSharp/CLAUDE.md「出力先のサブフォルダ」）
        relations.Add(CodegenRelation.CodeSubdirectory);

        // パッケージ参照のオン／オフ・Runtime 系の型集合は、元の SplitRuntimeSymmetryTests と同じ分割出力に限る
        // （1 ファイル出力は固定 infra とスキーマ依存物が同じファイルに入るので、ファイル単位の対応が無い）
        if (split && options.UseRuntimePackages)
        {
            relations.Add(CodegenRelation.RuntimePackages);
        }

        if (split && !options.UseRuntimePackages)
        {
            relations.Add(CodegenRelation.RuntimeTypeSet);
        }

        return new CodegenExpectation(
            DiagnosticRules.For(shape, options),
            FileLayoutOracle.Check,
            Namespaces: true,
            Compile: true,
            Semantics: true,
            probes,
            relations
        );
    }

    /// <summary>入力を流し、期待と照合する（問題は段ごとの失敗にして既知の失敗の一覧と突き合わせ、問題があれば全部を並べて赤にする）</summary>
    /// <param name="input">入力</param>
    /// <param name="expect">実効のオプションから期待を作る</param>
    /// <param name="shape">図の形（意味の規則と書き込み除外の探針が読む。決まった行は null）</param>
    /// <param name="requireSuccess">生成が通ることを要求するか（Success の表の行）</param>
    /// <param name="scope">既知の失敗の範囲（行の Id。<see cref="CodegenKnownFailures"/> の項目と序数で照合する）</param>
    public static async Task RunAsync(
        CodegenInput input,
        Func<CodeGenerationOptions, CodegenExpectation> expect,
        ShapeDiagram? shape,
        bool requireSuccess = false,
        string? scope = null
    )
    {
        var entrance = CodegenEntrances.Axis.Get(CodegenEntrances.NameOf(input.Entrance));
        var outcome = await entrance.RunAsync(input);
        var options = outcome.EffectiveOptions;
        var result = outcome.Result;
        var expectation = expect(options);
        var problems = outcome
            .Problems.Select(problem => (Stage: EntranceStage, Text: problem))
            .ToList();
        var stages = new List<string>(outcome.Stages);
        var manual = input.ManualSources(options);

        void Stage(string name, Func<IReadOnlyList<string>> judge)
        {
            stages.Add(name);

            try
            {
                problems.AddRange(judge().Select(problem => (name, problem)));
            }
            catch (Exception ex) when (ex is not XunitException)
            {
                problems.Add((name, $"{name}: 例外 {ex.GetType().Name}: {ex.Message}"));
            }
        }

        // エラーのときはファイルを 1 つも出さない（CSharpCodeGenerationService.Generate：エラー検出時は診断だけを返す）
        Stage(
            DiagnosticsStage,
            () =>
                expectation
                    .Diagnostics.Check(result.Diagnostics)
                    .Concat(
                        result.HasErrors && result.Files.Count > 0
                            ? [$"診断: エラーなのにファイルを {result.Files.Count} 個出した"]
                            : []
                    )
                    .ToList()
        );

        var relationInput = new RelationInput(
            input.Diagram,
            input.FreshDiagram ?? (() => input.Diagram),
            input.TypeDialect,
            options,
            result,
            expectation.ComparisonOptions,
            expectation.ExactRuntimeTypeSets
        );

        if (!result.HasErrors && expectation.ExpectsSuccess)
        {
            if (expectation.Files is { } files)
            {
                Stage(FilesStage, () => files(options, result));
            }

            if (expectation.Namespaces)
            {
                Stage(NamespacesStage, () => NamespaceJudge.Check(options, result));
            }

            if (expectation.Compile)
            {
                Stage(
                    CompileStage,
                    () =>
                        CompileJudge.Check(
                            options,
                            result,
                            manual,
                            expectation.CompileAsOneAssembly
                        )
                );
            }

            if (expectation.Semantics && shape is not null)
            {
                Stage(SemanticsStage, () => SemanticJudge.Check(shape, options, result, manual));
            }

            // 探針は行の生成物を 1 回だけコンパイルして読み込み、共有する
            var assembly = new Lazy<System.Reflection.Assembly>(() =>
                Judges.Probes.Load(result, manual)
            );

            foreach (var probe in expectation.Probes)
            {
                Stage(
                    ProbeStage(probe),
                    () =>
                        Judges.Probes.Run(
                            probe,
                            shape,
                            options,
                            assembly.Value,
                            expectation.ProbeFacts
                        )
                );
            }

            foreach (var relation in SuccessRelations(expectation))
            {
                Stage(Relations.Stage(relation), () => Relations.Run(relation, relationInput));
            }
        }

        if (expectation.Relations.Contains(CodegenRelation.DialectOrder))
        {
            Stage(
                Relations.DialectOrderStage,
                () => Relations.Run(CodegenRelation.DialectOrder, relationInput)
            );
        }

        if (requireSuccess && expectation.Diagnostics.ExpectsError)
        {
            problems.Add(
                (
                    RunnerStage,
                    "領域: Success の表の行なのに、診断の規則が生成時エラーを予測した（領域の述語と規則の食い違い）"
                )
            );
        }

        if (requireSuccess && result.HasErrors)
        {
            problems.Add(
                (
                    RunnerStage,
                    "領域: Success の表の行なのに、生成時エラーになった（領域の述語と製品の食い違い）"
                )
            );
        }

        var expectedStages = ExpectedStages(entrance, expectation, shape is not null);

        if (!stages.ToHashSet(StringComparer.Ordinal).SetEquals(expectedStages))
        {
            problems.Add(
                (
                    RunnerStage,
                    $"段: 走った段 [{string.Join(", ", stages)}] が期待した段 [{string.Join(", ", expectedStages)}] と違う（黙った空振り）"
                )
            );
        }

        // 問題を段ごとの失敗にして、既知の失敗の一覧（範囲＝行の Id）と突き合わせる。この行の項目だけを受け持つので、
        // 流していない行の項目は「起きない」と数えない
        var key = scope ?? input.Label;
        var report = CodegenKnownFailures.Set.Classify(
            problems.Select(problem => new EngineFailure(key, problem.Stage, problem.Text)),
            owned => string.Equals(owned, key, StringComparison.Ordinal)
        );

        if (report.Problem($"[{key}]") is { } problem)
        {
            throw new XunitException(
                $"{input.Label}\n実効のオプション: {options}\n{problem}\n  "
                    + string.Join("\n  ", problems.Select(p => p.Text))
            );
        }
    }

    /// <summary>入口の段で見つけた問題の段の名前（既知の失敗の署名）</summary>
    public const string EntranceStage = "entrance";

    /// <summary>領域・段の空振りの問題の段の名前（既知の失敗の署名）</summary>
    public const string RunnerStage = "runner";

    /// <summary>探針の段の名前</summary>
    public static string ProbeStage(CodegenProbe probe) => $"probe:{probe}";

    /// <summary>生成が通ったときだけ掛ける関係（方言の指定順は生成がエラーでも診断を比べるので別に掛ける）</summary>
    private static IEnumerable<CodegenRelation> SuccessRelations(CodegenExpectation expectation) =>
        expectation.Relations.Where(relation => relation != CodegenRelation.DialectOrder);

    /// <summary>期待から、走るべき段を決める</summary>
    private static List<string> ExpectedStages(
        ICodegenEntrance entrance,
        CodegenExpectation expectation,
        bool hasShape
    )
    {
        var stages = new List<string>(entrance.Stages) { DiagnosticsStage };

        if (expectation.ExpectsSuccess)
        {
            if (expectation.Files is not null)
            {
                stages.Add(FilesStage);
            }

            if (expectation.Namespaces)
            {
                stages.Add(NamespacesStage);
            }

            if (expectation.Compile)
            {
                stages.Add(CompileStage);
            }

            if (expectation.Semantics && hasShape)
            {
                stages.Add(SemanticsStage);
            }

            stages.AddRange(expectation.Probes.Select(ProbeStage));
            stages.AddRange(SuccessRelations(expectation).Select(Relations.Stage));
        }

        if (expectation.Relations.Contains(CodegenRelation.DialectOrder))
        {
            stages.Add(Relations.DialectOrderStage);
        }

        return stages;
    }
}
