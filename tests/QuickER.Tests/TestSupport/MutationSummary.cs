using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace QuickER.Tests.TestSupport;

/// <summary>影響の地図の 1 単位の変異の数</summary>
/// <param name="Unit">単位（<see cref="Engines.ImpactMap.UnitOf"/> が返す名前）</param>
/// <param name="Created">作られた変異の数（どの状態も含む）</param>
/// <param name="Killed">テストが殺した数</param>
/// <param name="Survived">テストが通ってしまった数</param>
/// <param name="NoCoverage">通るテストが無かった数</param>
/// <param name="Timeout">時間切れ（Stryker は殺したと数える）の数</param>
/// <param name="CompileError">コンパイルできなかった数（Safe Mode で外れた分を含む・母数の外）</param>
/// <param name="Ignored">設定・重複の除外で流さなかった数（母数の外）</param>
/// <param name="Dubious">Dubious のテスト（カバレッジの受け渡しが打ち切られたテスト）が通る、流した変異の数</param>
internal sealed record MutationUnitCounts(
    string Unit,
    int Created,
    int Killed,
    int Survived,
    int NoCoverage,
    int Timeout,
    int CompileError,
    int Ignored,
    int Dubious
)
{
    /// <summary>Stryker のスコアの母数（Killed＋Timeout＋Survived＋NoCoverage）</summary>
    public int Detectable => Killed + Timeout + Survived + NoCoverage;

    /// <summary>スコア（％・母数が 0 なら null）</summary>
    public double? Score => Detectable == 0 ? null : 100.0 * (Killed + Timeout) / Detectable;
}

/// <summary>Safe Mode で変異を全部外されたメソッド</summary>
/// <param name="File">ファイル（リポジトリの根からの相対パス・区切りは /）</param>
/// <param name="Method">メソッドの名前</param>
/// <param name="Mutants">そのメソッドの中で CompileError になった変異の数</param>
internal sealed record SafeModeRemoval(string File, string Method, int Mutants);

/// <summary>1 つの範囲（stryker の設定 1 本）の変異テストの要約</summary>
/// <param name="Target">範囲の名前（stryker の設定のファイル名から拡張子を除いたもの）</param>
/// <param name="Units">単位ごとの数（単位の序数の順）</param>
/// <param name="SafeMode">Safe Mode で外れたメソッド（ファイル・メソッドの序数の順）</param>
/// <param name="DubiousTests">Dubious のテストの数</param>
/// <param name="UnmappedFiles">どの単位にも入らなかったファイル（序数の順）</param>
internal sealed record MutationSummary(
    string Target,
    IReadOnlyList<MutationUnitCounts> Units,
    IReadOnlyList<SafeModeRemoval> SafeMode,
    int DubiousTests,
    IReadOnlyList<string> UnmappedFiles
)
{
    /// <summary>全単位の合計（単位の名前は「合計」）</summary>
    public MutationUnitCounts Total =>
        new(
            "合計",
            Units.Sum(unit => unit.Created),
            Units.Sum(unit => unit.Killed),
            Units.Sum(unit => unit.Survived),
            Units.Sum(unit => unit.NoCoverage),
            Units.Sum(unit => unit.Timeout),
            Units.Sum(unit => unit.CompileError),
            Units.Sum(unit => unit.Ignored),
            Units.Sum(unit => unit.Dubious)
        );
}

/// <summary>
/// Stryker.NET の結果（mutation-report.json とログ）を、影響の地図の単位ごとの要約にする
/// </summary>
/// <remarks>
/// <para>
/// 単位の判定は地図と同じ <see cref="Engines.ImpactMap.UnitOf"/> を呼び出し側から受ける（写しを作らない）。ファイルのパスは
/// Stryker が書いた絶対パスで、リポジトリの根の下か、パスの中の最後の <c>/src/</c> から後ろを相対パスにする（CI のランナーの
/// チェックアウトの場所に依らない）。
/// </para>
/// <para>
/// Safe Mode と Dubious は JSON に書かれないので、ログ（Stryker の <c>-L</c> のファイル）から読む。Safe Mode は
/// 「An unidentified mutation in {ファイル} resulted in a compile error」の直後の「Safe Mode! Stryker will remove all mutations in
/// {メソッド}」の組で、外れた数はそのファイルの CompileError のうちそのメソッドの中にあるものを数える。Dubious はテストの ID を
/// 「marking as Dubious」の行から集め、変異の coveredBy と突き合わせる。
/// </para>
/// </remarks>
internal static class MutationSummaries
{
    /// <summary>Safe Mode の前置きの行（コンパイルできなかったファイル）</summary>
    private static readonly Regex UnidentifiedMutation = new(
        @"An unidentified mutation in (?<file>.+?) resulted in a compile error",
        RegexOptions.CultureInvariant
    );

    /// <summary>Safe Mode の行（変異を全部外したメソッド）</summary>
    private static readonly Regex SafeModeLine = new(
        @"Safe Mode! Stryker will remove all mutations in (?<method>[A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.CultureInvariant
    );

    /// <summary>Dubious の行（テストの ID）</summary>
    private static readonly Regex DubiousLine = new(
        @"(?<id>[0-9a-f]{16,})""?;? marking as Dubious",
        RegexOptions.CultureInvariant
    );

    /// <summary>要約を作る</summary>
    /// <param name="target">範囲の名前</param>
    /// <param name="reportJson">mutation-report.json の本文</param>
    /// <param name="log">Stryker のログの本文（無ければ空）</param>
    /// <param name="repositoryRoot">リポジトリの根（パスを相対にする）</param>
    /// <param name="unitOf">相対パスの単位（無ければ null）。地図の <see cref="Engines.ImpactMap.UnitOf"/></param>
    /// <param name="readSource">相対パスのソースの本文（読めなければ null）。Safe Mode の数を数える</param>
    /// <param name="inScope">
    /// 相対パスが設定の mutate の範囲の中か（<see cref="StrykerMutateScope.FromConfig"/>）。Stryker 5 は全部の変異をコンパイルしてから
    /// 範囲で印を付けるので、範囲の外のファイルにも CompileError・除外の Ignored が残る。範囲の外のファイルは数えない
    /// </param>
    public static MutationSummary Summarize(
        string target,
        string reportJson,
        string log,
        string repositoryRoot,
        Func<string, string?> unitOf,
        Func<string, string?> readSource,
        Func<string, bool> inScope
    )
    {
        using var report = JsonDocument.Parse(reportJson);
        var dubiousTests = DubiousLine
            .Matches(log)
            .Select(match => match.Groups["id"].Value)
            .ToHashSet(StringComparer.Ordinal);
        var units = new SortedDictionary<string, int[]>(StringComparer.Ordinal);
        var unmapped = new SortedSet<string>(StringComparer.Ordinal);
        var inScopeFiles = new HashSet<string>(StringComparer.Ordinal);
        var compileErrors = new Dictionary<string, List<(int Line, int Column)>>(
            StringComparer.Ordinal
        );

        foreach (var file in report.RootElement.GetProperty("files").EnumerateObject())
        {
            var relative = RelativePath(file.Name, repositoryRoot);
            var unit = relative is null ? null : unitOf(relative);

            if (relative is not null && !inScope(relative))
            {
                continue;
            }

            if (relative is not null)
            {
                inScopeFiles.Add(relative);
            }

            if (unit is null)
            {
                unmapped.Add(relative ?? file.Name.Replace('\\', '/'));
                continue;
            }

            if (!units.TryGetValue(unit, out var counts))
            {
                counts = new int[8];
                units[unit] = counts;
            }

            foreach (var mutant in file.Value.GetProperty("mutants").EnumerateArray())
            {
                var status = mutant.GetProperty("status").GetString();

                counts[0]++;

                switch (status)
                {
                    case "Killed":
                        counts[1]++;
                        break;
                    case "Survived":
                        counts[2]++;
                        break;
                    case "NoCoverage":
                        counts[3]++;
                        break;
                    case "Timeout":
                        counts[4]++;
                        break;
                    case "CompileError":
                        counts[5]++;
                        break;
                    case "Ignored":
                        counts[6]++;
                        break;
                }

                if (status == "CompileError" && relative is not null)
                {
                    var start = mutant.GetProperty("location").GetProperty("start");

                    if (!compileErrors.TryGetValue(relative, out var positions))
                    {
                        positions = [];
                        compileErrors[relative] = positions;
                    }

                    positions.Add(
                        (
                            start.GetProperty("line").GetInt32(),
                            start.GetProperty("column").GetInt32()
                        )
                    );
                }

                if (
                    status is "Killed" or "Survived" or "Timeout"
                    && mutant.TryGetProperty("coveredBy", out var coveredBy)
                    && coveredBy.ValueKind == JsonValueKind.Array
                    && coveredBy
                        .EnumerateArray()
                        .Any(test => dubiousTests.Contains(test.GetString() ?? ""))
                )
                {
                    counts[7]++;
                }
            }
        }

        return new MutationSummary(
            target,
            [
                .. units.Select(pair => new MutationUnitCounts(
                    pair.Key,
                    pair.Value[0],
                    pair.Value[1],
                    pair.Value[2],
                    pair.Value[3],
                    pair.Value[4],
                    pair.Value[5],
                    pair.Value[6],
                    pair.Value[7]
                )),
            ],
            [
                .. SafeModeRemovals(log, repositoryRoot, compileErrors, readSource)
                    .Where(removal => inScopeFiles.Contains(removal.File)),
            ],
            dubiousTests.Count,
            [.. unmapped]
        );
    }

    /// <summary>Stryker が書いたパスを、リポジトリの根からの相対パスにする（できなければ null）</summary>
    public static string? RelativePath(string path, string repositoryRoot)
    {
        var normalized = path.Replace('\\', '/');
        var root = repositoryRoot.Replace('\\', '/').TrimEnd('/') + "/";

        if (normalized.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            return normalized[root.Length..];
        }

        var index = normalized.LastIndexOf("/src/", StringComparison.Ordinal);

        return index < 0 ? null : normalized[(index + 1)..];
    }

    /// <summary>ログの Safe Mode の組から、外れたメソッドとその中の CompileError の数を数える（重複の組は 1 つにする）</summary>
    private static IReadOnlyList<SafeModeRemoval> SafeModeRemovals(
        string log,
        string repositoryRoot,
        IReadOnlyDictionary<string, List<(int Line, int Column)>> compileErrors,
        Func<string, string?> readSource
    )
    {
        var pairs = new SortedSet<(string File, string Method)>();
        string? pendingFile = null;

        foreach (var line in log.Split('\n'))
        {
            var unidentified = UnidentifiedMutation.Match(line);

            if (unidentified.Success)
            {
                pendingFile = RelativePath(
                    unidentified.Groups["file"].Value.Trim(),
                    repositoryRoot
                );
                continue;
            }

            var safeMode = SafeModeLine.Match(line);

            if (safeMode.Success && pendingFile is not null)
            {
                pairs.Add((pendingFile, safeMode.Groups["method"].Value));
                pendingFile = null;
            }
        }

        return
        [
            .. pairs.Select(pair => new SafeModeRemoval(
                pair.File,
                pair.Method,
                CountInMethod(pair.File, pair.Method, compileErrors, readSource)
            )),
        ];
    }

    /// <summary>ファイルの CompileError のうち、その名前のメソッドの中にあるものの数（ソースを読めなければ 0）</summary>
    private static int CountInMethod(
        string file,
        string method,
        IReadOnlyDictionary<string, List<(int Line, int Column)>> compileErrors,
        Func<string, string?> readSource
    )
    {
        var source = readSource(file);

        if (source is null || !compileErrors.TryGetValue(file, out var positions))
        {
            return 0;
        }

        var tree = ConditionalSourceParse.Parse(source, path: file)[0];
        var lines = tree.GetText().Lines;
        var spans = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Where(declaration => declaration.Identifier.ValueText == method)
            .Select(declaration => declaration.Span)
            .ToList();

        // 位置は Stryker の JSON の 1 始まりの行・列
        return positions.Count(position =>
            position.Line >= 1
            && position.Line <= lines.Count
            && spans.Any(span =>
                span.Contains(lines[position.Line - 1].Start + Math.Max(0, position.Column - 1))
            )
        );
    }

    /// <summary>要約を JSON にする（次の実行の「前回」として読む）</summary>
    public static string ToJson(MutationSummary summary) =>
        JsonSerializer.Serialize(summary, JsonOptions);

    /// <summary>前回の要約の JSON を読む（読めなければ null）</summary>
    public static MutationSummary? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<MutationSummary>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>要約を Markdown にする（夜間のジョブの Step Summary）</summary>
    /// <param name="current">今回の要約</param>
    /// <param name="previous">前回の要約（無ければ null）</param>
    public static string Render(MutationSummary current, MutationSummary? previous)
    {
        var builder = new StringBuilder();
        var total = current.Total;
        var previousUnits =
            previous?.Units.ToDictionary(unit => unit.Unit, StringComparer.Ordinal)
            ?? new Dictionary<string, MutationUnitCounts>(StringComparer.Ordinal);

        builder.Append(CultureInfo.InvariantCulture, $"## 変異テスト：{current.Target}\n\n");
        var delta = previous is null
            ? "・前回なし"
            : "・前回から " + FormatScoreDelta(total.Score, previous.Total.Score);
        var dubious =
            current.DubiousTests > 0
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"（それが通る流した変異 {total.Dubious} 件。多ければ取り直しを検討する）"
                )
                : "";

        builder.Append(
            CultureInfo.InvariantCulture,
            $"- スコア {FormatScore(total.Score)}（Killed {total.Killed}・Survived {total.Survived}・NoCoverage {total.NoCoverage}・Timeout {total.Timeout}・CompileError {total.CompileError}・Ignored {total.Ignored}）{delta}\n"
        );
        builder.Append(
            CultureInfo.InvariantCulture,
            $"- Dubious のテスト {current.DubiousTests} 件{dubious}\n"
        );
        builder.Append(
            CultureInfo.InvariantCulture,
            $"- Safe Mode で外れたメソッド {current.SafeMode.Count} 個（変異 {current.SafeMode.Sum(removal => removal.Mutants)} 件）\n"
        );

        if (current.UnmappedFiles.Count > 0)
        {
            builder.Append(
                CultureInfo.InvariantCulture,
                $"- どの単位にも入らないファイル：{string.Join("・", current.UnmappedFiles)}\n"
            );
        }

        builder.Append(
            "\n| 単位 | 変異 | Killed | Survived | NoCoverage | Timeout | Dubious | スコア |\n"
        );
        builder.Append("|---|---|---|---|---|---|---|---|\n");

        foreach (var unit in current.Units)
        {
            previousUnits.TryGetValue(unit.Unit, out var before);
            builder.Append(
                CultureInfo.InvariantCulture,
                $"| `{unit.Unit}` | {Cell(unit.Created, before?.Created, previous)} | {Cell(unit.Killed, before?.Killed, previous)}"
                    + $" | {Cell(unit.Survived, before?.Survived, previous)} | {Cell(unit.NoCoverage, before?.NoCoverage, previous)}"
                    + $" | {Cell(unit.Timeout, before?.Timeout, previous)} | {Cell(unit.Dubious, before?.Dubious, previous)}"
                    + $" | {FormatScore(unit.Score)} |\n"
            );
        }

        if (current.SafeMode.Count > 0)
        {
            builder.Append("\n| Safe Mode で外れたメソッド | 変異 |\n|---|---|\n");

            foreach (var removal in current.SafeMode)
            {
                builder.Append(
                    CultureInfo.InvariantCulture,
                    $"| `{removal.File}` の `{removal.Method}` | {removal.Mutants} |\n"
                );
            }
        }

        return builder.ToString();
    }

    /// <summary>数と前回からの増減（前回の要約が無ければ数だけ・前回に無い単位は「新」）</summary>
    private static string Cell(int value, int? before, MutationSummary? previous) =>
        previous is null ? value.ToString(CultureInfo.InvariantCulture)
        : before is null ? string.Create(CultureInfo.InvariantCulture, $"{value}（新）")
        : value == before ? value.ToString(CultureInfo.InvariantCulture)
        : string.Create(CultureInfo.InvariantCulture, $"{value}（{value - before.Value:+0;-0}）");

    /// <summary>スコアの表記（母数が 0 なら —）</summary>
    private static string FormatScore(double? score) =>
        score is null ? "—" : string.Create(CultureInfo.InvariantCulture, $"{score:0.0}%");

    /// <summary>スコアの前回からの増減（ポイント）</summary>
    private static string FormatScoreDelta(double? current, double? previous) =>
        current is null || previous is null
            ? "—"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{current - previous:+0.0;-0.0;±0.0} pt"
            );

    /// <summary>要約の JSON の書き方（前回の読み戻しと同じ）</summary>
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
}

/// <summary>
/// stryker の設定の mutate の範囲（ファイル単位）の読み方。照合の行（mutate の指定が実在のファイルに当たるか）と、変異テストの要約
/// （範囲の外のファイルを数えない）が同じ読み方を使う
/// </summary>
/// <remarks>
/// 受ける形は「**/」に続く後ろの一致（ワイルドカードは「**/*.cs」のときだけ）と、その前に ! を付けたファイル単位の除外だけ。
/// 文字の範囲（{a..b}）は使わない（Stryker 5 は全部の変異をコンパイルしてから範囲で印を付けるので、Safe Mode を避けられず、
/// 位置はソースが動くと黙ってずれる）。含める指定が無ければ project の全部のファイルが範囲。
/// </remarks>
internal static class StrykerMutateScope
{
    private const string Prefix = "**/";
    private const string AnySource = "*.cs";

    /// <summary>受ける形の指定か（先頭の ! を除いた後の形を見る）</summary>
    public static bool IsSupported(string pattern)
    {
        var body = Body(pattern);

        return body.StartsWith(Prefix, StringComparison.Ordinal)
            && (
                body[Prefix.Length..] == AnySource
                || body[Prefix.Length..].IndexOfAny(['*', '?', '{', '!']) < 0
            );
    }

    /// <summary>指定（先頭の ! は無視する）が、project のフォルダからの相対パス（区切りは /）のファイルに当たるか</summary>
    public static bool Matches(string pattern, string pathInProject)
    {
        if (!IsSupported(pattern))
        {
            return false;
        }

        var suffix = Body(pattern)[Prefix.Length..];

        return suffix == AnySource
            ? pathInProject.EndsWith(".cs", StringComparison.Ordinal)
            : pathInProject == suffix
                || pathInProject.EndsWith("/" + suffix, StringComparison.Ordinal);
    }

    /// <summary>
    /// 設定の project のフォルダ（リポジトリの根からの相対・区切りは /）と mutate の並びから、リポジトリの根からの相対パスが範囲の中かを返す
    /// 関数を作る（含める指定のどれかに当たり〔含める指定が無ければ全部〕、! の指定のどれにも当たらない）
    /// </summary>
    public static Func<string, bool> FromConfig(
        string projectFolder,
        IReadOnlyList<string> patterns
    )
    {
        var folder = projectFolder.TrimEnd('/') + "/";
        var includes = patterns.Where(pattern => !pattern.StartsWith('!')).ToList();
        var excludes = patterns.Where(pattern => pattern.StartsWith('!')).ToList();

        return path =>
        {
            if (!path.StartsWith(folder, StringComparison.Ordinal))
            {
                return false;
            }

            var inProject = path[folder.Length..];

            return (includes.Count == 0 || includes.Any(pattern => Matches(pattern, inProject)))
                && !excludes.Any(pattern => Matches(pattern, inProject));
        };
    }

    /// <summary>stryker の設定のファイル（project と mutate）から範囲の関数を作る（project のフォルダは src の同じ名前のフォルダ）</summary>
    public static Func<string, bool> FromConfigFile(string configPath)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(configPath));
        var config = json.RootElement.GetProperty("stryker-config");
        var project = config.GetProperty("project").GetString() ?? "";
        var patterns = config.TryGetProperty("mutate", out var mutate)
            ? mutate.EnumerateArray().Select(item => item.GetString() ?? "").ToList()
            : [];

        return FromConfig("src/" + Path.GetFileNameWithoutExtension(project), patterns);
    }

    private static string Body(string pattern) => pattern.StartsWith('!') ? pattern[1..] : pattern;
}
