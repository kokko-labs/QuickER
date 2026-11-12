using System.Reflection;
using System.Text;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>担当のセル 1 つ（クラスの属性かエンジンの登録から）</summary>
/// <param name="Feature">機能の名前</param>
/// <param name="Target">実装先</param>
/// <param name="Server">リモートのサーバーの裏の実装先（リモート以外・リポジトリなしは null）</param>
/// <param name="ServerNone">リモートのサーバーがリポジトリを持たない</param>
/// <param name="ClassName">担当のテストのクラスの名前</param>
/// <param name="Detail">エンジンの担当の出どころ（シナリオの群・実装先の軸。属性は null）</param>
public sealed record RuntimeCoverage(
    string Feature,
    RuntimeTarget Target,
    RuntimeTarget? Server,
    bool ServerNone,
    string ClassName,
    string? Detail
);

/// <summary>
/// 機能 × 実装先の行列：テストのクラスの走査・担当の集め方・照合・生成物の描画（<c>RuntimeTestMatrixTests</c> が使う）
/// </summary>
/// <remarks>
/// <para>
/// <b>走査の規約</b>（行列の照合の対象のクラス。変えるならここも変える）：名前空間が
/// <c>QuickER.Tests.Integration.GeneratedRuntime</c> のものすべて（クラス名の綴りに依らない）と、名前空間が
/// <c>QuickER.Tests.Generated…</c>（フィクスチャの名前空間群）のうちクラス名が <c>RuntimeTests</c> で終わるもの。
/// いずれも具象で [Fact] / [Theory] を宣言または継承するものだけ（xUnit が実行する単位）。行列自身のガードは外す。
/// </para>
/// <para>
/// 担当は、テストのクラスの <see cref="RuntimeCellAttribute"/> と、実装先を比べるエンジンの登録（<see cref="RuntimeMatrixEngines"/>）から導く。
/// 担当でないセルは行の表（<see cref="RuntimeMatrixRows"/>）が宣言する。
/// </para>
/// </remarks>
public static class RuntimeMatrix
{
    /// <summary>生成物（行列）のリポジトリの相対パス</summary>
    public const string DocumentPath =
        "tests/QuickER.Tests.Integration/GeneratedRuntime/runtime-matrix.md";

    /// <summary>書き直しのコマンド</summary>
    public static string RegenerateSteps { get; } =
        RegenerationRequest.Command(
            (
                "tests/QuickER.Tests.Integration/QuickER.Tests.Integration.csproj",
                "RuntimeMatrixDrift"
            )
        );

    /// <summary>走査の規約に合う実行時テストのクラス（名前の序数順）</summary>
    public static IReadOnlyList<Type> RuntimeTestClasses() =>
        typeof(RuntimeMatrix)
            .Assembly.GetTypes()
            .Where(IsRuntimeTestClass)
            .OrderBy(type => type.Name, StringComparer.Ordinal)
            .ToList();

    /// <summary>担当のセルを集める（クラスの属性とエンジンの登録。エンジンの登録の問題も返す）</summary>
    public static (IReadOnlyList<RuntimeCoverage> Cells, IReadOnlyList<string> Problems) Collect()
    {
        var (engineCells, problems) = RuntimeMatrixEngines.Derive();
        var cells = new List<RuntimeCoverage>(engineCells);

        foreach (var type in RuntimeTestClasses())
        {
            cells.AddRange(
                type.GetCustomAttributes<RuntimeCellAttribute>(inherit: false)
                    .Select(attribute => new RuntimeCoverage(
                        attribute.Feature,
                        attribute.Target,
                        attribute.DeclaredServer,
                        attribute.ServerNone,
                        type.Name,
                        Detail: null
                    ))
            );
        }

        return (cells, problems);
    }

    /// <summary>走査の規約</summary>
    private static bool IsRuntimeTestClass(Type type)
    {
        if (type is not { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false })
        {
            return false;
        }

        if (type == typeof(RuntimeTestMatrixTests) || !HasTestMethods(type))
        {
            return false;
        }

        var ns = type.Namespace ?? string.Empty;

        if (string.Equals(ns, typeof(RuntimeMatrix).Namespace, StringComparison.Ordinal))
        {
            return true;
        }

        return ns.StartsWith("QuickER.Tests.Generated", StringComparison.Ordinal)
            && type.Name.EndsWith("RuntimeTests", StringComparison.Ordinal);
    }

    /// <summary>[Fact] / [Theory] を宣言または継承しているか</summary>
    private static bool HasTestMethods(Type type) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy)
            .Any(method =>
                method.GetCustomAttributes(typeof(FactAttribute), inherit: true).Length > 0
            );

    /// <summary>
    /// 担当と行の表の食い違いをすべて並べる：どの機能 × 実装先も担当か宣言のちょうど一方・宣言の形（理由・区切り・根拠）・
    /// 担当の機能が行の表にある・リモートの足りないサーバーが担当に現れていない・行の機能の重複
    /// </summary>
    /// <param name="rows">行の表</param>
    /// <param name="cells">担当のセル</param>
    public static IReadOnlyList<string> CellProblems(
        IReadOnlyList<RuntimeFeatureRow> rows,
        IReadOnlyList<RuntimeCoverage> cells
    )
    {
        var problems = new List<string>();
        var features = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            if (!features.Add(row.Feature))
            {
                problems.Add($"機能 {row.Feature} の行が 2 つある");
            }

            if (FactorInventoryGuard.IsVagueReason(row.Description))
            {
                problems.Add($"機能 {row.Feature} の説明が具体的でない");
            }

            foreach (var target in Enum.GetValues<RuntimeTarget>())
            {
                var owners = cells
                    .Where(cell => cell.Feature == row.Feature && cell.Target == target)
                    .ToList();
                var declared = row.Declared.TryGetValue(target, out var declaration);
                var cell = $"[{row.Feature} × {target}]";

                if (owners.Count == 0 && !declared)
                {
                    problems.Add(
                        $"{cell} は担当も宣言も無い（担当のクラスに RuntimeCell 属性を付けるか、RuntimeMatrixRows に空き・割り切り・非該当を宣言する）"
                    );
                }
                else if (owners.Count > 0 && declared)
                {
                    problems.Add(
                        $"{cell} は {declaration!.Kind}（{declaration.Reason}）と宣言されているが、担当がある"
                            + $"（{string.Join("・", owners.Select(owner => owner.ClassName).Distinct(StringComparer.Ordinal))}）。埋まったので宣言を消す"
                    );
                }

                if (declared)
                {
                    problems.AddRange(
                        DeclarationProblems(declaration!).Select(problem => $"{cell} {problem}")
                    );
                }
            }

            var remoteServers = cells
                .Where(cell => cell.Feature == row.Feature && cell.Target == RuntimeTarget.Remote)
                .Select(cell => cell.Server)
                .ToHashSet();

            foreach (var missing in row.MissingServers)
            {
                var cell = $"[{row.Feature} × Remote（サーバー {missing.Server}）]";

                if (row.Declared.ContainsKey(RuntimeTarget.Remote))
                {
                    problems.Add($"{cell} の足りないサーバーは、リモートが担当のセルにだけ書く");
                }

                if (missing.Server == RuntimeTarget.Remote)
                {
                    problems.Add($"{cell} のサーバーにリモートは書けない");
                }

                if (remoteServers.Contains(missing.Server))
                {
                    problems.Add(
                        $"{cell} は足りないと宣言されているが、担当がある。埋まったので宣言を消す"
                    );
                }

                if (string.IsNullOrWhiteSpace(missing.FixedIn))
                {
                    problems.Add($"{cell} の直す区切りが空");
                }

                if (FactorInventoryGuard.IsVagueReason(missing.Reason))
                {
                    problems.Add($"{cell} の理由が具体的でない");
                }
            }
        }

        foreach (
            var feature in cells
                .Select(cell => cell.Feature)
                .Distinct(StringComparer.Ordinal)
                .Where(feature => !features.Contains(feature))
                .OrderBy(feature => feature, StringComparer.Ordinal)
        )
        {
            problems.Add($"担当が名乗る機能 {feature} が行の表に無い");
        }

        return problems;
    }

    /// <summary>担当でないセルの宣言の形の問題（無ければ空）</summary>
    private static IEnumerable<string> DeclarationProblems(RuntimeCellDeclaration declaration)
    {
        if (FactorInventoryGuard.IsVagueReason(declaration.Reason))
        {
            yield return "の理由が具体的でない";
        }

        if (
            (declaration.Kind == RuntimeCellKind.Gap)
            != !string.IsNullOrWhiteSpace(declaration.FixedIn)
        )
        {
            yield return "の直す区切りは空きだけが書き、空きは必ず書く";
        }

        if (
            (declaration.Kind == RuntimeCellKind.Accepted)
            != !string.IsNullOrWhiteSpace(declaration.Basis)
        )
        {
            yield return "の根拠は割り切りだけが書き、割り切りは必ず書く";
        }
    }

    /// <summary>担当のセルの形の問題：リモートはサーバーかリポジトリなしのちょうど一方を名乗り、リモート以外はどちらも名乗らない</summary>
    /// <param name="cells">担当のセル</param>
    public static IReadOnlyList<string> CoverageProblems(IReadOnlyList<RuntimeCoverage> cells)
    {
        var problems = new List<string>();

        foreach (var cell in cells)
        {
            var label = $"{cell.ClassName} の [{cell.Feature} × {cell.Target}]";

            if (cell.Target == RuntimeTarget.Remote)
            {
                if ((cell.Server is null) == !cell.ServerNone)
                {
                    problems.Add(
                        $"{label} はサーバー（Server）かリポジトリなし（ServerNone）のちょうど一方を名乗る"
                    );
                }

                if (cell.Server == RuntimeTarget.Remote)
                {
                    problems.Add($"{label} のサーバーにリモートは書けない");
                }
            }
            else if (cell.Server is not null || cell.ServerNone)
            {
                problems.Add($"{label} はリモートでないのにサーバーを名乗っている");
            }
        }

        foreach (
            var duplicate in cells
                .Where(cell => cell.Detail is null)
                .GroupBy(cell =>
                    (cell.ClassName, cell.Feature, cell.Target, cell.Server, cell.ServerNone)
                )
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
        )
        {
            problems.Add(
                $"{duplicate.ClassName} に同じ担当 [{duplicate.Feature} × {duplicate.Target}] の属性が 2 つある"
            );
        }

        return problems;
    }

    /// <summary>
    /// 割り切りの根拠の形の問題：<c>リポジトリの相対パス「見出し」</c>で、ファイルがあり、その見出しの行（<c>#</c> で始まる）がある
    /// </summary>
    /// <param name="rows">行の表</param>
    /// <param name="root">リポジトリの根</param>
    public static IReadOnlyList<string> BasisProblems(
        IReadOnlyList<RuntimeFeatureRow> rows,
        string root
    )
    {
        var problems = new List<string>();

        foreach (
            var (feature, target, basis) in rows.SelectMany(row =>
                row.Declared.Where(entry => entry.Value.Basis is not null)
                    .Select(entry => (row.Feature, entry.Key, entry.Value.Basis!))
            )
        )
        {
            var cell = $"[{feature} × {target}]";
            var open = basis.IndexOf('「', StringComparison.Ordinal);

            if (open <= 0 || !basis.EndsWith('」'))
            {
                problems.Add($"{cell} の根拠 {basis} は「相対パス「見出し」」の形でない");
                continue;
            }

            var path = Path.Combine(root, basis[..open]);
            var heading = basis[(open + 1)..^1];

            if (!File.Exists(path))
            {
                problems.Add($"{cell} の根拠のファイル {basis[..open]} が無い");
                continue;
            }

            if (
                !File.ReadLines(path)
                    .Any(line =>
                        line.StartsWith('#')
                        && string.Equals(
                            line.TrimStart('#').Trim(),
                            heading,
                            StringComparison.Ordinal
                        )
                    )
            )
            {
                problems.Add($"{cell} の根拠の見出し「{heading}」が {basis[..open]} に無い");
            }
        }

        return problems;
    }

    /// <summary>
    /// 走査したクラスと担当の食い違い：属性もエンジンの登録も無いクラス・実在しないクラスのエンジンの登録
    /// </summary>
    /// <param name="classNames">走査したクラスの名前</param>
    /// <param name="cells">担当のセル</param>
    /// <param name="engineClassNames">エンジンの登録のクラスの名前</param>
    public static IReadOnlyList<string> ClassProblems(
        IReadOnlyList<string> classNames,
        IReadOnlyList<RuntimeCoverage> cells,
        IReadOnlyList<string> engineClassNames
    )
    {
        var owners = cells.Select(cell => cell.ClassName).ToHashSet(StringComparer.Ordinal);
        var problems = classNames
            .Where(name => !owners.Contains(name))
            .Select(name =>
                $"{name} は機能 × 実装先のセルを名乗っていない（RuntimeCell 属性を付ける。実装先を比べるエンジンなら RuntimeMatrixEngines に登録する）"
            )
            .ToList();

        problems.AddRange(
            engineClassNames
                .Where(name => !classNames.Contains(name, StringComparer.Ordinal))
                .Select(name => $"エンジンの登録のクラス {name} が走査の規約のクラスに無い")
        );

        return problems;
    }

    /// <summary>行列の生成物を描く（改行は LF）</summary>
    /// <param name="rows">行の表</param>
    /// <param name="cells">担当のセル</param>
    public static string Render(
        IReadOnlyList<RuntimeFeatureRow> rows,
        IReadOnlyList<RuntimeCoverage> cells
    )
    {
        var text = new StringBuilder();
        var targets = RuntimeTargets.All;

        text.Append("# 生成コードのランタイムの機能 × 実装先の行列\n\n");
        text.Append(
            "<!-- 生成物（RuntimeTestMatrixTests が宣言から作る）。手で直さない。書き直し（リポジトリ直下・PowerShell）："
                + RegenerateSteps
                + " -->\n\n"
        );
        text.Append(
            "生成コードのランタイムのテスト（tests/QuickER.Tests.Integration）を、機能ごとに実装先ごとの担当のテストのクラス・エンジンと、担当でないセルの理由で並べる。\n"
        );
        text.Append(
            "出どころはテストのクラスの RuntimeCell 属性・実装先を比べるエンジンの登録（RuntimeMatrixEngines）・行の表（RuntimeMatrixRows）。\n\n"
        );

        text.Append("## 実装先\n\n");
        text.Append("| 実装先 | 見出し | 生成の実装 | 方言 |\n|---|---|---|---|\n");

        foreach (var info in targets)
        {
            text.Append(
                $"| `{info.Target}` | {info.Label} | {EngineLabel(info.Engine)} | {info.Dialect ?? "—"} |\n"
            );
        }

        text.Append("\n## セルの語\n\n| 語 | 意味 |\n|---|---|\n");
        text.Append("| 担当 | 担当のテストのクラスかエンジンがある |\n");
        text.Append(
            "| 担当（サーバー：…） | リモートの担当。HTTP クライアントの裏のサーバーの実装先を並べる（リポジトリなし＝転送だけのスタブ・HTTP を使わない検査） |\n"
        );
        text.Append(
            "| 空き（区切り） | まだ検証していない。括弧は直す区切り（決まっていなければきっかけ待ち） |\n"
        );
        text.Append(
            "| 割り切り | 意図して検証しない（根拠の docs か CLAUDE.md の見出しを添える） |\n"
        );
        text.Append("| 非該当 | その組み合わせが原理的に成り立たない |\n");

        text.Append("\n## 行列\n\n| 機能 |");

        foreach (var info in targets)
        {
            text.Append($" {info.Label} |");
        }

        text.Append("\n|---|");
        text.Append(string.Concat(targets.Select(_ => "---|")));
        text.Append('\n');

        foreach (var row in rows)
        {
            text.Append($"| `{row.Feature}` |");

            foreach (var info in targets)
            {
                text.Append($" {ShortCell(row, info.Target, cells)} |");
            }

            text.Append('\n');
        }

        text.Append("\n## 機能ごとの詳細\n");

        foreach (var row in rows)
        {
            text.Append($"\n### `{row.Feature}`\n\n{row.Description}\n\n");

            foreach (var info in targets)
            {
                text.Append($"- {info.Label}：{LongCell(row, info.Target, cells)}\n");
            }

            foreach (var missing in row.MissingServers)
            {
                text.Append(
                    $"- リモートの足りないサーバー：{RuntimeTargets.Of(missing.Server).Label}（{missing.FixedIn}）＝{missing.Reason}\n"
                );
            }
        }

        return text.ToString();
    }

    /// <summary>行列のセルの短い語</summary>
    private static string ShortCell(
        RuntimeFeatureRow row,
        RuntimeTarget target,
        IReadOnlyList<RuntimeCoverage> cells
    )
    {
        if (row.Declared.TryGetValue(target, out var declaration))
        {
            return KindLabel(declaration);
        }

        var owners = OwnersOf(row, target, cells);

        if (owners.Count == 0)
        {
            return "（宣言なし）";
        }

        return target == RuntimeTarget.Remote ? $"担当（サーバー：{ServersOf(owners)}）" : "担当";
    }

    /// <summary>詳細の節のセル</summary>
    private static string LongCell(
        RuntimeFeatureRow row,
        RuntimeTarget target,
        IReadOnlyList<RuntimeCoverage> cells
    )
    {
        if (row.Declared.TryGetValue(target, out var declaration))
        {
            var basis = declaration.Basis is null ? "" : $"（根拠：{declaration.Basis}）";

            return $"{KindLabel(declaration)}＝{declaration.Reason}{basis}";
        }

        var owners = OwnersOf(row, target, cells);

        if (owners.Count == 0)
        {
            return "（宣言なし）";
        }

        var listed = owners
            .GroupBy(owner => owner.ClassName, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                var notes = new List<string>();

                if (target == RuntimeTarget.Remote)
                {
                    notes.Add($"サーバー：{ServersOf(group.ToList())}");
                }

                notes.AddRange(
                    group
                        .Select(owner => owner.Detail)
                        .OfType<string>()
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(detail => detail, StringComparer.Ordinal)
                );

                return notes.Count == 0
                    ? $"`{group.Key}`"
                    : $"`{group.Key}`（{string.Join("／", notes)}）";
            });

        return "担当＝" + string.Join("・", listed);
    }

    private static List<RuntimeCoverage> OwnersOf(
        RuntimeFeatureRow row,
        RuntimeTarget target,
        IReadOnlyList<RuntimeCoverage> cells
    ) => cells.Where(cell => cell.Feature == row.Feature && cell.Target == target).ToList();

    /// <summary>リモートのサーバーを実装先の並びで（リポジトリなしは最後）</summary>
    private static string ServersOf(IReadOnlyList<RuntimeCoverage> owners)
    {
        var servers = RuntimeTargets
            .All.Where(info => owners.Any(owner => owner.Server == info.Target))
            .Select(info => info.Label)
            .ToList();

        if (owners.Any(owner => owner.ServerNone))
        {
            servers.Add("リポジトリなし");
        }

        return string.Join("・", servers);
    }

    private static string KindLabel(RuntimeCellDeclaration declaration) =>
        declaration.Kind switch
        {
            RuntimeCellKind.Gap => $"空き（{declaration.FixedIn}）",
            RuntimeCellKind.Accepted => "割り切り",
            RuntimeCellKind.NotApplicable => "非該当",
            _ => throw new ArgumentOutOfRangeException(nameof(declaration)),
        };

    private static string EngineLabel(RuntimeEngineKind engine) =>
        engine switch
        {
            RuntimeEngineKind.Ado => "QuickER 版 Repository（ADO.NET を直接使う生成の実装）",
            RuntimeEngineKind.EfCore => "EF Core 版 Repository",
            RuntimeEngineKind.InMemory => "インメモリ Repository",
            RuntimeEngineKind.Remote => "HTTP クライアント＋生成サーバー（in-process の Kestrel）",
            _ => throw new ArgumentOutOfRangeException(nameof(engine)),
        };
}
