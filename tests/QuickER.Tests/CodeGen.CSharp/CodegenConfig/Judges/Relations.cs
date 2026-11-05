using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using QuickER.CodeGen.CSharp;
using QuickER.Dialects;
using QuickER.Model;

namespace QuickER.Tests.CodeGen.CSharp.CodegenConfig.Judges;

/// <summary>関係（同じ行の変種を生成して全体を比べる）の種類</summary>
internal enum CodegenRelation
{
    /// <summary>方言の指定順を入れ替えても <c>.g.cs</c> と診断が同じ（分割・インラインのマルチターゲット）</summary>
    DialectOrder,

    /// <summary>
    /// 層別を外して分割にしても、スキーマ依存のファイルは namespace・using と <c>{Repository}UniquenessConstraints</c> の宣言の可視性以外が同じで、
    /// Runtime 系はさらに可視性だけが違う
    /// </summary>
    LayeredVsSplit,

    /// <summary><c>CodeSubdirectory</c> を足しても生成テキストはバイト不変で、<c>.g.cs</c> の置き場だけが 1 段下がる（<c>.g.md</c> は追随しない）</summary>
    CodeSubdirectory,

    /// <summary>パッケージ参照を外しても、パッケージ参照で出るファイルは本体（namespace 宣言以降）が同じで、外したときだけ Runtime 系が出る</summary>
    RuntimePackages,

    /// <summary>
    /// <c>Runtime*.g.cs</c> のトップレベルの型の集合が、対応するパッケージ用ソースの型の集合に含まれ、行が名指しした Runtime 系のファイルは
    /// 同じ集合になる
    /// </summary>
    RuntimeTypeSet,

    /// <summary>同じ入力（図を組み直したもの）を 2 回生成して、全ファイル・診断が同じ（決まった行だけ）</summary>
    Deterministic,

    /// <summary>比較用のオプションの生成物と違い、同じオプションどうしは同じ（空振りの検査。決まった行だけ）</summary>
    OptionEffect,

    /// <summary><c>RepositoryDialects</c> の綴りの大小を変えても同じ出力（決まった行だけ）</summary>
    DialectCasing,

    /// <summary>
    /// 比較用のオプションの生成物と全ファイル・診断が同じ（<see cref="OptionEffect"/> の逆：使われない・不正でも影響しないオプションが出力を変えない。
    /// 決まった行だけ）
    /// </summary>
    OptionNoEffect,
}

/// <summary>関係が読む入力</summary>
/// <param name="Diagram">図（行の生成に使ったもの）</param>
/// <param name="FreshDiagram">図を組み直す（決定的の関係が使う）</param>
/// <param name="TypeDialect">図の方言</param>
/// <param name="Options">実効の生成オプション</param>
/// <param name="Result">行の生成結果</param>
/// <param name="ComparisonOptions">比較用のオプション（オプションが効く関係だけ）</param>
/// <param name="ExactRuntimeTypeSets">型の集合がパッケージと同じであるべき Runtime 系のファイル名（Runtime 系の型集合の関係。null なら無し）</param>
internal sealed record RelationInput(
    ErDiagram Diagram,
    Func<ErDiagram> FreshDiagram,
    string TypeDialect,
    CodeGenerationOptions Options,
    CodeGenerationResult Result,
    CodeGenerationOptions? ComparisonOptions,
    IReadOnlyList<string>? ExactRuntimeTypeSets
);

/// <summary>
/// 関係（同じ行の変種を生成して全体を比べる）。入口どうしの一致は入口のアダプタ（<see cref="FileEntranceSupport.CompareWithDirect"/>）が受け持つ
/// </summary>
/// <remarks>
/// <para>
/// 層別と分割：src/QuickER.CodeGen.CSharp/CLAUDE.md「固定 infra の可視性」（層分けの ON / OFF で、スキーマ依存ファイルは namespace 宣言・
/// using 行と、固定 infra と同じ規則で可視性を切り替える型 <c>{Repository}UniquenessConstraints</c> の可視性以外が一致し、Runtime 系はさらに可視性だけが違う）。サブフォルダ：同「出力先のサブフォルダ」（サブフォルダの有無で生成テキストは
/// バイト不変・<c>.g.md</c> は追随しない）。パッケージ参照・Runtime 系の型集合：同「HTTP クライアントのバケット分離」（分割の固定ランタイムは
/// パッケージと 1:1 対称・<c>UseRuntimePackages</c> は Runtime 系を出さず using を変えるだけ）。
/// </para>
/// <para>
/// 決定的・オプションが効く・方言の綴りの大小は、網羅の配列の行に掛けると行へ包み直すだけで検出力が増えないので、決まった行だけに掛ける
/// （<see cref="CasesOnly"/>）。
/// </para>
/// </remarks>
internal static class Relations
{
    /// <summary>方言の指定順の関係の段</summary>
    public const string DialectOrderStage = "relation:dialect-order";

    /// <summary>決まった行だけに掛ける関係（網羅の配列の行には掛けない）</summary>
    public static IReadOnlyList<CodegenRelation> CasesOnly { get; } =
    [
        CodegenRelation.Deterministic,
        CodegenRelation.OptionEffect,
        CodegenRelation.DialectCasing,
        CodegenRelation.OptionNoEffect,
    ];

    /// <summary>関係の段の名前</summary>
    /// <param name="relation">関係</param>
    public static string Stage(CodegenRelation relation) =>
        relation switch
        {
            CodegenRelation.DialectOrder => DialectOrderStage,
            CodegenRelation.LayeredVsSplit => "relation:layered-vs-split",
            CodegenRelation.CodeSubdirectory => "relation:code-subdirectory",
            CodegenRelation.RuntimePackages => "relation:runtime-packages",
            CodegenRelation.RuntimeTypeSet => "relation:runtime-type-set",
            CodegenRelation.Deterministic => "relation:deterministic",
            CodegenRelation.OptionEffect => "relation:option-effect",
            CodegenRelation.DialectCasing => "relation:dialect-casing",
            CodegenRelation.OptionNoEffect => "relation:option-no-effect",
            _ => $"relation:{relation}",
        };

    /// <summary>関係を確かめる（問題を返す）</summary>
    /// <param name="relation">関係</param>
    /// <param name="input">関係が読む入力</param>
    public static IReadOnlyList<string> Run(CodegenRelation relation, RelationInput input) =>
        relation switch
        {
            CodegenRelation.DialectOrder => DialectOrder(
                input.Diagram,
                input.TypeDialect,
                input.Options,
                input.Result
            ),
            CodegenRelation.LayeredVsSplit => LayeredVsSplit(input),
            CodegenRelation.CodeSubdirectory => CodeSubdirectory(input),
            CodegenRelation.RuntimePackages => RuntimePackages(input),
            CodegenRelation.RuntimeTypeSet => RuntimeTypeSet(
                input.Result,
                input.ExactRuntimeTypeSets ?? []
            ),
            CodegenRelation.Deterministic => Deterministic(input),
            CodegenRelation.OptionEffect => OptionEffect(input),
            CodegenRelation.DialectCasing => DialectCasing(input),
            CodegenRelation.OptionNoEffect => OptionNoEffect(input),
            _ => [$"関係: 知らない関係 {relation}"],
        };

    /// <summary>
    /// 分割出力で方言の指定順を入れ替えて生成しても、<c>.g.cs</c> のファイル（相対パスと内容）と診断の文の集合が同じ
    /// （CLAUDE.md「マルチターゲット × rowversion」：統一先は指定順の先頭でなく行バージョンと解決した方言で決まる。
    /// 出力の順だけは違ってよい＝元の <c>DialectOrderInvarianceTests</c> と同じ比べ方）
    /// </summary>
    /// <remarks>
    /// 範囲は元の <c>DialectOrderInvarianceTests</c> と同じ「分割出力の <c>.g.cs</c>」に限る（呼び出し側は分割出力の行だけに掛ける）。
    /// 1 ファイル出力（方言の名前空間のブロックを 1 ファイルへ連結する並び）と API リファレンス（<c>.g.md</c> の DI 登録の例の並び）は
    /// 指定順に従って並ぶが、CLAUDE.md・docs はその並びを指定順に依らないとは約束しておらず、元のテストも比べていないので比べない。
    /// </remarks>
    /// <param name="diagram">図</param>
    /// <param name="typeDialect">図の方言</param>
    /// <param name="options">実効の生成オプション</param>
    /// <param name="result">指定順のままの生成結果</param>
    public static IReadOnlyList<string> DialectOrder(
        ErDiagram diagram,
        string typeDialect,
        CodeGenerationOptions options,
        CodeGenerationResult result
    )
    {
        var reversed = options with
        {
            RepositoryDialects = CodegenOptionFacts.Dialects(options).Reverse().ToList(),
        };
        var other = CodegenRunner.Generate(diagram, typeDialect, reversed);
        var problems = new List<string>();
        var left = CodeFiles(result);
        var right = CodeFiles(other);

        if (!left.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(right.Keys))
        {
            problems.Add(
                $"関係（方言の指定順）: ファイルの集合が違う [{string.Join(", ", left.Keys.Order(StringComparer.Ordinal))}] / [{string.Join(", ", right.Keys.Order(StringComparer.Ordinal))}]"
            );
        }

        foreach (var key in left.Keys.Intersect(right.Keys).Order(StringComparer.Ordinal))
        {
            if (!string.Equals(left[key], right[key], StringComparison.Ordinal))
            {
                problems.Add($"関係（方言の指定順）: {key} の内容が違う");
            }
        }

        if (!Messages(result).SequenceEqual(Messages(other), StringComparer.Ordinal))
        {
            problems.Add("関係（方言の指定順）: 診断の文の集合が違う");
        }

        return problems;
    }

    /// <summary>
    /// 層別を外して分割にした変種と比べる：ファイル名の並びが同じ。<c>.g.cs</c> は namespace 宣言・using 行・内容ハッシュの行と、
    /// 名指しの型 <c>{Repository}UniquenessConstraints</c> の宣言の可視性を除けば同じで、Runtime 系はさらに可視性（<c>public</c> / <c>internal</c>）
    /// だけが違ってよい（元の <c>LayeredOutputTests.Generate_Layered_KeepsContentIdenticalExceptNamespaces</c> の比較を Runtime 系・一意制約のある図まで広げたもの）
    /// </summary>
    /// <remarks>
    /// スキーマ依存のファイルで public / internal の違いを許すのは、一意制約の事前チェックの型 <c>{Repository}UniquenessConstraints</c> の
    /// 宣言の行だけ（固定 infra と同じ <c>infra_visibility</c> で切り替わる。ほかの型・メンバーの可視性の違いは許さない）。
    /// </remarks>
    private static IReadOnlyList<string> LayeredVsSplit(RelationInput input)
    {
        var split = Variant(
            input,
            input.Options with
            {
                LayeredOutput = false,
                SplitFilesByCategory = true,
            },
            "層別と分割",
            out var problems
        );

        if (split is null)
        {
            return problems;
        }

        var layeredNames = input.Result.Files.Select(file => file.FileName).ToList();
        var splitNames = split.Files.Select(file => file.FileName).ToList();

        if (!layeredNames.SequenceEqual(splitNames, StringComparer.Ordinal))
        {
            problems.Add(
                $"関係（層別と分割）: ファイル名の並びが違う [{string.Join(", ", layeredNames)}] / [{string.Join(", ", splitNames)}]"
            );
            return problems;
        }

        foreach (var (layered, splitFile) in input.Result.Files.Zip(split.Files))
        {
            if (!layered.FileName.EndsWith(".g.cs", StringComparison.Ordinal))
            {
                continue;
            }

            var runtime = layered.FileName.StartsWith("Runtime", StringComparison.Ordinal);
            var left = StripNamespaceLines(layered.Content, runtime);
            var right = StripNamespaceLines(splitFile.Content, runtime);

            if (!string.Equals(left, right, StringComparison.Ordinal))
            {
                problems.Add(
                    $"関係（層別と分割）: {layered.FileName} が namespace・using{(runtime ? "・可視性" : string.Empty)}以外でも違う（{FirstDifference(left, right)}）"
                );
            }
        }

        return problems;
    }

    /// <summary>2 つのテキストが初めて食い違う行（失敗の文に添える）</summary>
    private static string FirstDifference(string left, string right)
    {
        var leftLines = left.Split('\n');
        var rightLines = right.Split('\n');

        for (var index = 0; index < Math.Min(leftLines.Length, rightLines.Length); index++)
        {
            if (leftLines[index] != rightLines[index])
            {
                return string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"{index + 1} 行目: [{leftLines[index].Trim()}] / [{rightLines[index].Trim()}]"
                );
            }
        }

        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"行の数が違う {leftLines.Length} / {rightLines.Length}"
        );
    }

    /// <summary>
    /// namespace 宣言・using 行と、それらを含む本文から計算される内容ハッシュの行を取り除く（Runtime 系は可視性の語を、
    /// スキーマ依存のファイルは <c>{Repository}UniquenessConstraints</c> の宣言の可視性だけをそろえる）
    /// </summary>
    private static string StripNamespaceLines(string content, bool normalizeVisibility)
    {
        content = UniquenessConstraintsDeclaration.Replace(content, "${indent}(visibility)${rest}");

        var stripped = string.Join(
            '\n',
            content
                .Split('\n')
                .Where(line =>
                    !line.TrimStart().StartsWith("using ", StringComparison.Ordinal)
                    && !line.TrimStart().StartsWith("namespace ", StringComparison.Ordinal)
                    && !line.StartsWith(
                        GeneratedContentHash.HashLinePrefix,
                        StringComparison.Ordinal
                    )
                )
        );

        return normalizeVisibility ? VisibilityWord.Replace(stripped, "(visibility)") : stripped;
    }

    /// <summary>
    /// <c>{Repository}UniquenessConstraints</c> の宣言の行の可視性（固定 infra と同じ <c>infra_visibility</c> で層別のとき public になる）
    /// </summary>
    private static readonly Regex UniquenessConstraintsDeclaration = new(
        @"^(?<indent>[ ]*)(?:public|internal)(?<rest> static class \w+UniquenessConstraints\b)",
        RegexOptions.Multiline | RegexOptions.CultureInvariant
    );

    /// <summary>可視性の語（層別の固定 infra は <c>infra_visibility</c> を public へ切り替える）</summary>
    private static readonly Regex VisibilityWord = new(
        @"\b(?:public|internal)\b",
        RegexOptions.CultureInvariant
    );

    /// <summary>
    /// <c>CodeSubdirectory</c> を足した変種と比べる：ファイルの並び・名前・内容がバイト不変で、<c>.g.cs</c> の置き場だけが
    /// 元の置き場の 1 段下（<c>{元}/Generated</c>。直下なら <c>Generated</c>）になり、<c>.g.md</c> の置き場は変わらない
    /// （元の <c>CodeSubdirectoryTests.Generate_Subdirectory_ChangesPlacementOnly</c> を全出力モードへ広げたもの）
    /// </summary>
    private static IReadOnlyList<string> CodeSubdirectory(RelationInput input)
    {
        const string subdirectory = "Generated";

        if (input.Options.CodeSubdirectory is not null)
        {
            return ["関係（サブフォルダ）: 行がすでに CodeSubdirectory を持つ（変種を作れない）"];
        }

        var other = Variant(
            input,
            input.Options with
            {
                CodeSubdirectory = subdirectory,
            },
            "サブフォルダ",
            out var problems
        );

        if (other is null)
        {
            return problems;
        }

        if (other.Files.Count != input.Result.Files.Count)
        {
            problems.Add(
                $"関係（サブフォルダ）: ファイルの数が違う（{input.Result.Files.Count} / {other.Files.Count}）"
            );
            return problems;
        }

        foreach (var (baseline, moved) in input.Result.Files.Zip(other.Files))
        {
            if (baseline.FileName != moved.FileName)
            {
                problems.Add(
                    $"関係（サブフォルダ）: ファイルの並びが違う（{baseline.FileName} / {moved.FileName}）"
                );
                continue;
            }

            if (!string.Equals(baseline.Content, moved.Content, StringComparison.Ordinal))
            {
                problems.Add($"関係（サブフォルダ）: {baseline.FileName} の内容が変わった");
            }

            var from = Directory(baseline.RelativeDirectory);
            var expected =
                baseline.FileName.EndsWith(".g.md", StringComparison.Ordinal) ? from
                : from is null ? subdirectory
                : $"{from}/{subdirectory}";

            if (Directory(moved.RelativeDirectory) != expected)
            {
                problems.Add(
                    $"関係（サブフォルダ）: {baseline.FileName} の置き場 {moved.RelativeDirectory ?? "(直下)"}（期待 {expected ?? "(直下)"}）"
                );
            }
        }

        return problems;
    }

    /// <summary>
    /// パッケージ参照を外した変種と比べる：パッケージ参照で出るファイルは全部、外したときにも同じ名前で出て、本体（最初の namespace 宣言以降）が同じ。
    /// 外したときにだけ出るファイルは Runtime 系ちょうどで、パッケージ参照では Runtime 系が 1 本も出ない
    /// （元の <c>SplitRuntimeSymmetryTests.SchemaDependentFiles_ShouldBeIdenticalAcrossRuntimePackageModes</c>）
    /// </summary>
    /// <remarks>
    /// 比べるのは元のテストと同じ <c>.g.cs</c> だけ。API リファレンス（<c>.g.md</c>）は生成されたファイルの一覧と名前空間を載せるので、
    /// Runtime 系の有無に従ってモードで違ってよい。
    /// </remarks>
    private static IReadOnlyList<string> RuntimePackages(RelationInput input)
    {
        var inline = Variant(
            input,
            input.Options with
            {
                UseRuntimePackages = false,
            },
            "パッケージ参照",
            out var problems
        );

        if (inline is null)
        {
            return problems;
        }

        var inlineFiles = CodeFileList(inline)
            .ToDictionary(file => file.FileName, StringComparer.Ordinal);

        foreach (var file in CodeFileList(input.Result))
        {
            if (IsRuntimeFile(file.FileName))
            {
                problems.Add(
                    $"関係（パッケージ参照）: パッケージ参照で Runtime 系の {file.FileName} が出た"
                );
            }

            if (!inlineFiles.TryGetValue(file.FileName, out var counterpart))
            {
                problems.Add(
                    $"関係（パッケージ参照）: {file.FileName} がパッケージ参照を外すと出ない"
                );
                continue;
            }

            if (
                !string.Equals(
                    Body(file.Content),
                    Body(counterpart.Content),
                    StringComparison.Ordinal
                )
            )
            {
                problems.Add(
                    $"関係（パッケージ参照）: {file.FileName} の本体（namespace 宣言以降）がモードで違う"
                );
            }
        }

        var onlyInline = inlineFiles
            .Keys.Except(
                CodeFileList(input.Result).Select(file => file.FileName),
                StringComparer.Ordinal
            )
            .Where(name => !IsRuntimeFile(name))
            .ToList();

        if (onlyInline.Count > 0)
        {
            problems.Add(
                $"関係（パッケージ参照）: パッケージ参照を外したときだけ出る Runtime 系でないファイル [{string.Join(", ", onlyInline)}]"
            );
        }

        return problems;
    }

    /// <summary>生成結果の <c>.g.cs</c> のファイル（出力の並び）</summary>
    private static IEnumerable<GeneratedFile> CodeFileList(CodeGenerationResult result) =>
        result.Files.Where(file => file.FileName.EndsWith(".g.cs", StringComparison.Ordinal));

    /// <summary>ヘッダ（auto-generated・using・案内コメント）を落とし、namespace 宣言以降の本体を返す</summary>
    private static string Body(string content)
    {
        var index = content.IndexOf("namespace ", StringComparison.Ordinal);
        return index < 0 ? content : content[index..];
    }

    /// <summary>固定 infra のファイル（Runtime 系）か</summary>
    private static bool IsRuntimeFile(string fileName) =>
        fileName.StartsWith("Runtime.", StringComparison.Ordinal)
        && fileName.EndsWith(".g.cs", StringComparison.Ordinal);

    /// <summary>
    /// 分割の <c>Runtime*.g.cs</c> が宣言するトップレベルの型の集合（名前と型引数の個数）が、対応するパッケージ用ソース
    /// （<see cref="RuntimePackageSourceRenderer"/>）の型の集合に含まれる（パッケージに無い型が固定 infra に紛れない）。
    /// 名指しのファイルは同じ集合になる（元の <c>SplitRuntimeSymmetryTests</c> の型集合の比較 5 本）
    /// </summary>
    /// <remarks>
    /// 分割の固定 infra は、図・オプションが使う属性型・EditModel などだけを出す（元のテストは属性型の出力条件をすべて満たす図で比べていた）ので、
    /// 同じ集合になるのは、出力条件を満たす図とオプションの組だけ。網羅の配列の行は含まれることだけを見て、同じ集合は決まった行が名指しする。
    /// </remarks>
    /// <param name="result">生成結果</param>
    /// <param name="exact">型の集合がパッケージと同じであるべきファイル名</param>
    private static IReadOnlyList<string> RuntimeTypeSet(
        CodeGenerationResult result,
        IReadOnlyList<string> exact
    )
    {
        var problems = new List<string>();
        var runtimeFiles = result.Files.Where(file => IsRuntimeFile(file.FileName)).ToList();

        foreach (
            var missing in exact.Except(
                runtimeFiles.Select(file => file.FileName),
                StringComparer.Ordinal
            )
        )
        {
            problems.Add($"関係（Runtime 系の型集合）: 名指しの {missing} が出ていない");
        }

        foreach (var file in runtimeFiles)
        {
            if (!PackageTypeSets.Value.TryGetValue(file.FileName, out var package))
            {
                problems.Add(
                    $"関係（Runtime 系の型集合）: {file.FileName} に対応するパッケージが無い"
                );
                continue;
            }

            var declared = DeclaredTypes(file.Content);
            var onlyFile = declared.Except(package).Order(StringComparer.Ordinal).ToList();

            if (onlyFile.Count > 0)
            {
                problems.Add(
                    $"関係（Runtime 系の型集合）: {file.FileName} にパッケージに無い型がある [{string.Join(", ", onlyFile)}]"
                );
            }

            if (
                exact.Contains(file.FileName, StringComparer.Ordinal)
                && !package.IsSubsetOf(declared)
            )
            {
                problems.Add(
                    $"関係（Runtime 系の型集合）: {file.FileName} にパッケージの型が無い [{string.Join(", ", package.Except(declared).Order(StringComparer.Ordinal))}]"
                );
            }
        }

        return problems;
    }

    /// <summary>Runtime 系のファイル名 → 対応するパッケージ用ソースの型の集合（1 プロセスで 1 回だけ作る）</summary>
    private static readonly Lazy<Dictionary<string, HashSet<string>>> PackageTypeSets = new(() =>
    {
        var renderer = new RuntimePackageSourceRenderer();

        return new Dictionary<string, Func<string>>(StringComparer.Ordinal)
        {
            ["Runtime.g.cs"] = renderer.RenderCore,
            ["Runtime.SqlServer.g.cs"] = renderer.RenderSqlServer,
            ["Runtime.Sqlite.g.cs"] = renderer.RenderSqlite,
            ["Runtime.EntityFrameworkCore.g.cs"] = renderer.RenderEfCore,
            ["Runtime.InMemory.g.cs"] = renderer.RenderInMemory,
            ["Runtime.AspNetCore.g.cs"] = renderer.RenderAspNetCore,
            ["Runtime.Sync.g.cs"] = renderer.RenderSync,
        }.ToDictionary(
            pair => pair.Key,
            pair => DeclaredTypes(pair.Value()),
            StringComparer.Ordinal
        );
    });

    /// <summary>
    /// ソースのトップレベル（名前空間の直下・ファイルの直下）の型宣言を「名前＋型引数の個数」（例 <c>IValueObject`1</c>）の集合にする
    /// （構文木は <see cref="GeneratedCodeCompiler.CreateCompilation"/> から取る）
    /// </summary>
    /// <remarks>
    /// 型引数の個数まで含めるのは、同名で個数だけが違う型が別々のファイルへ宣言されるため（拡張シムの <c>IValueObject</c> と、
    /// 固定 infra の <c>IValueObject&lt;TSelf&gt;</c> / <c>IValueObject&lt;TSelf, TValue&gt;</c>）。
    /// </remarks>
    private static HashSet<string> DeclaredTypes(string content) =>
        GeneratedCodeCompiler
            .CreateCompilation([("TypeSet.cs", content)], "QuickER.CodegenConfig.TypeSet")
            .SyntaxTrees[0]
            .GetRoot()
            .DescendantNodes()
            .Where(node => node.Parent is BaseNamespaceDeclarationSyntax or CompilationUnitSyntax)
            .Select(node =>
                node switch
                {
                    TypeDeclarationSyntax type => Arity(
                        type.Identifier.ValueText,
                        type.TypeParameterList?.Parameters.Count ?? 0
                    ),
                    EnumDeclarationSyntax enumeration => enumeration.Identifier.ValueText,
                    DelegateDeclarationSyntax @delegate => Arity(
                        @delegate.Identifier.ValueText,
                        @delegate.TypeParameterList?.Parameters.Count ?? 0
                    ),
                    _ => null,
                }
            )
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

    private static string Arity(string name, int count) =>
        count == 0
            ? name
            : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{name}`{count}");

    /// <summary>図を組み直して同じオプションで生成し、全ファイル（置き場・名前・内容の並び）と診断が同じ（元の <c>ApiReferenceDocTests.Deterministic_SameInputProducesIdenticalMarkdown</c> を全ファイルへ広げたもの）</summary>
    private static IReadOnlyList<string> Deterministic(RelationInput input)
    {
        var again = CodegenRunner.Generate(input.FreshDiagram(), input.TypeDialect, input.Options);
        var problems = new List<string>();

        if (!Render(input.Result).SequenceEqual(Render(again), StringComparer.Ordinal))
        {
            problems.Add("関係（決定的）: 同じ入力の 2 回の生成でファイルが違う");
        }

        if (!Messages(input.Result).SequenceEqual(Messages(again), StringComparer.Ordinal))
        {
            problems.Add("関係（決定的）: 同じ入力の 2 回の生成で診断が違う");
        }

        return problems;
    }

    /// <summary>
    /// 比較用のオプション（そのオプションを既定へ戻したもの）の生成物と違い、対照として同じオプションで生成し直した生成物とは同じ
    /// （元の <c>GeneratedCodeCompilationTests.Generate_SingleOptions_ChangeTheOutput</c>・<c>OutputDiffers_IsFalseForTheSameOptions</c>）
    /// </summary>
    private static IReadOnlyList<string> OptionEffect(RelationInput input)
    {
        if (input.ComparisonOptions is not { } comparison)
        {
            return ["関係（オプションが効く）: 行に比較用のオプションが無い"];
        }

        var problems = new List<string>();
        var baseline = CodegenRunner.Generate(input.Diagram, input.TypeDialect, comparison);
        var again = CodegenRunner.Generate(input.Diagram, input.TypeDialect, input.Options);

        if (Render(input.Result).SequenceEqual(Render(baseline), StringComparer.Ordinal))
        {
            problems.Add(
                "関係（オプションが効く）: 比較用のオプションの生成物と同じ（オプションが図に効いていない＝空振り）"
            );
        }

        if (!Render(input.Result).SequenceEqual(Render(again), StringComparer.Ordinal))
        {
            problems.Add(
                "関係（オプションが効く）: 同じオプションで生成し直した生成物と違う（比べ方が同じものを同じと判定しない）"
            );
        }

        return problems;
    }

    /// <summary>
    /// 比較用のオプションで生成しても、全ファイル（相対パスと内容）と診断が同じ（使われない・不正でも影響しないオプションが出力を変えない）
    /// </summary>
    private static IReadOnlyList<string> OptionNoEffect(RelationInput input)
    {
        if (input.ComparisonOptions is not { } comparison)
        {
            return ["関係（オプションが効かない）: 行に比較用のオプションが無い"];
        }

        var other = CodegenRunner.Generate(input.Diagram, input.TypeDialect, comparison);
        var problems = new List<string>();

        if (!Render(input.Result).SequenceEqual(Render(other), StringComparer.Ordinal))
        {
            problems.Add(
                "関係（オプションが効かない）: 比較用のオプションの生成物と違う（効かないはずのオプションが出力を変えた）"
            );
        }

        if (
            !input
                .Result.Diagnostics.Select(diagnostic => diagnostic.Message)
                .SequenceEqual(
                    other.Diagnostics.Select(diagnostic => diagnostic.Message),
                    StringComparer.Ordinal
                )
        )
        {
            problems.Add("関係（オプションが効かない）: 比較用のオプションと診断が違う");
        }

        return problems;
    }

    /// <summary>
    /// <c>RepositoryDialects</c> の綴りの大小を変えて（<c>sqlite</c> → <c>SQLite</c>・<c>sqlserver</c> → <c>SqlServer</c>）生成しても、
    /// 全ファイルが同じ（元の <c>MultiTargetRepositoryGenerationTests.Generate_CaseInsensitiveDialectSpelling_MatchesCanonicalOutput</c>）
    /// </summary>
    private static IReadOnlyList<string> DialectCasing(RelationInput input)
    {
        var dialects = CodegenOptionFacts.Dialects(input.Options);
        var spelled = dialects.Select(OtherCasing).ToList();

        if (dialects.SequenceEqual(spelled, StringComparer.Ordinal))
        {
            return ["関係（方言の綴りの大小）: 綴りを変えられない（空振り）"];
        }

        var other = Variant(
            input,
            input.Options with
            {
                RepositoryDialects = spelled,
            },
            "方言の綴りの大小",
            out var problems
        );

        if (
            other is not null
            && !Render(input.Result).SequenceEqual(Render(other), StringComparer.Ordinal)
        )
        {
            problems.Add(
                $"関係（方言の綴りの大小）: [{string.Join(", ", spelled)}] の生成物が [{string.Join(", ", dialects)}] と違う"
            );
        }

        return problems;
    }

    private static string OtherCasing(string dialect) =>
        dialect switch
        {
            DialectNames.Sqlite => "SQLite",
            DialectNames.SqlServer => "SqlServer",
            _ => dialect.ToUpperInvariant(),
        };

    /// <summary>変種を生成する（エラーなら問題に足して null）</summary>
    private static CodeGenerationResult? Variant(
        RelationInput input,
        CodeGenerationOptions options,
        string name,
        out List<string> problems
    )
    {
        problems = [];
        var result = CodegenRunner.Generate(input.Diagram, input.TypeDialect, options);

        if (result.HasErrors)
        {
            problems.Add(
                $"関係（{name}）: 変種の生成がエラー（{string.Join(" / ", result.Diagnostics.Where(d => d.Severity == GenerationDiagnosticSeverity.Error).Select(d => d.Message))}）"
            );
            return null;
        }

        return result;
    }

    /// <summary>生成物（置き場・名前・内容）を出力の並びで比べられる形にする</summary>
    private static IEnumerable<string> Render(CodeGenerationResult result) =>
        result.Files.Select(file =>
            $"{Directory(file.RelativeDirectory) ?? "."}/{file.FileName}\n{file.Content}"
        );

    /// <summary>診断の文（重要度つき）の集合を並べたもの</summary>
    private static IEnumerable<string> Messages(CodeGenerationResult result) =>
        result.Diagnostics.Select(d => $"[{d.Severity}] {d.Message}").Order(StringComparer.Ordinal);

    private static string? Directory(string? directory) =>
        string.IsNullOrEmpty(directory) ? null : directory.Replace('\\', '/').TrimEnd('/');

    /// <summary>生成結果の <c>.g.cs</c> だけを（相対パス → 内容）にする</summary>
    private static Dictionary<string, string> CodeFiles(CodeGenerationResult result) =>
        FileEntranceSupport
            .ToTree(result)
            .Where(pair => pair.Key.EndsWith(".g.cs", StringComparison.Ordinal))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
}
