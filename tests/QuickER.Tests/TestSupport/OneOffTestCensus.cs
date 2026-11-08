using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.TestSupport;

/// <summary>単発のテストの数（1 つのコミットの分）</summary>
/// <param name="Commit">数えたコミット（無ければ空）</param>
/// <param name="Facts">単発のテストの <c>[Fact]</c> のメソッドの数</param>
/// <param name="Theories">単発のテストの <c>[Theory]</c> のメソッドの数</param>
/// <param name="ProtectedMethods">地図の守りのテストのソースにある <c>[Fact]</c> / <c>[Theory]</c> のメソッドの数（参考）</param>
/// <param name="Files">単発のテストを持つファイル（リポジトリの根からの相対パス）→ そのファイルの単発のテストのメソッドの数</param>
internal sealed record OneOffCensus(
    string Commit,
    int Facts,
    int Theories,
    int ProtectedMethods,
    IReadOnlyDictionary<string, int> Files
)
{
    /// <summary>単発のテストのメソッドの合計</summary>
    public int Total => Facts + Theories;
}

/// <summary>
/// 単発のテスト（影響の地図のどの守りのテストのソースにも入らないファイルの <c>[Fact]</c> / <c>[Theory]</c> のメソッド）を数え、
/// 基点と比べた増減を Markdown にする
/// </summary>
/// <remarks>
/// <para>
/// 数える範囲は <c>tests/QuickER.Tests</c> と <c>tests/QuickER.Tests.Integration</c> の C# のソース（<c>bin</c> / <c>obj</c> は読まない）。
/// 守りのテストのソースは地図と同じ関数（<see cref="ImpactMapSources.Protections"/> の各守りを <see cref="ImpactMapSources.ExpandPartials"/>
/// で広げたもの。Integration の守りは写し <c>protection-registry.json</c> から引く）で決め、写しを作らない。テストの土台の自己検査
/// （<see cref="ImpactMapSources.ScenarioTableExemptions"/>）は数えない。
/// </para>
/// <para>
/// 数はビルドだけで決まる数にするため、Theory のケースは数えずメソッドで数える。メソッドは条件コンパイルのどの分岐も読む
/// （<see cref="ConditionalSourceParse"/>）。報告は CI の Step Summary に出すだけで、数では赤にしない（ci.yml の build-and-test）。
/// </para>
/// </remarks>
internal static class OneOffTestCensus
{
    /// <summary>数えるテストのプロジェクトのフォルダ（リポジトリの根からの相対パス）</summary>
    public static IReadOnlyList<string> TestRoots { get; } =
    ["tests/QuickER.Tests", "tests/QuickER.Tests.Integration"];

    /// <summary>報告で名指しする増えた・減ったファイルの数の上限（残りは件数だけ）</summary>
    public const int ListedFiles = 20;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>リポジトリの今の宣言とソースで数える</summary>
    /// <param name="repositoryRoot">リポジトリの根</param>
    /// <param name="commit">数えたコミット</param>
    public static OneOffCensus FromRepository(string repositoryRoot, string commit) =>
        Count(
            repositoryRoot,
            ImpactMapSources.Protections(repositoryRoot),
            ImpactMapSources
                .ScenarioTableExemptions.Select(exemption => exemption.Path)
                .ToHashSet(StringComparer.Ordinal),
            commit
        );

    /// <summary>守りと数えないファイルを指定して数える</summary>
    /// <param name="repositoryRoot">リポジトリの根</param>
    /// <param name="protections">地図の守り</param>
    /// <param name="exempt">数えないファイル（リポジトリの根からの相対パス）</param>
    /// <param name="commit">数えたコミット</param>
    public static OneOffCensus Count(
        string repositoryRoot,
        IReadOnlyList<Protection> protections,
        IReadOnlySet<string> exempt,
        string commit
    )
    {
        var protectedFiles = protections
            .SelectMany(protection =>
                ImpactMapSources.ExpandPartials(repositoryRoot, protection.SourceFiles)
            )
            .ToHashSet(StringComparer.Ordinal);
        var facts = 0;
        var theories = 0;
        var protectedMethods = 0;
        var files = new SortedDictionary<string, int>(StringComparer.Ordinal);

        foreach (var path in TestSourceFiles(repositoryRoot))
        {
            if (exempt.Contains(path))
            {
                continue;
            }

            var (fileFacts, fileTheories) = TestMethods(
                File.ReadAllText(Path.Combine(repositoryRoot, path))
            );

            if (fileFacts + fileTheories == 0)
            {
                continue;
            }

            if (protectedFiles.Contains(path))
            {
                protectedMethods += fileFacts + fileTheories;
                continue;
            }

            facts += fileFacts;
            theories += fileTheories;
            files[path] = fileFacts + fileTheories;
        }

        return new OneOffCensus(commit, facts, theories, protectedMethods, files);
    }

    /// <summary>数えるテストのソース（リポジトリの根からの相対パス・序数の順・<c>bin</c> / <c>obj</c> を除く・無いフォルダは飛ばす）</summary>
    private static IEnumerable<string> TestSourceFiles(string repositoryRoot) =>
        TestRoots
            .Select(root => Path.Combine(repositoryRoot, root))
            .Where(Directory.Exists)
            .SelectMany(folder =>
                Directory
                    .EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)
                    .Where(file => !SourceFiles.IsBuildOutput(Path.GetRelativePath(folder, file)))
            )
            .Select(file => Path.GetRelativePath(repositoryRoot, file).Replace('\\', '/'))
            .Order(StringComparer.Ordinal);

    /// <summary>
    /// ソースの <c>[Fact]</c> / <c>[Theory]</c> のメソッドの数（属性の名前の最後の部分が Fact・FactAttribute・Theory・TheoryAttribute。
    /// 両方を持つメソッドは Theory に数える。条件コンパイルのどの分岐も読み、コメント・文字列の中の綴りは数えない）
    /// </summary>
    /// <param name="source">C# のソースの本文</param>
    public static (int Facts, int Theories) TestMethods(string source)
    {
        if (
            !source.Contains("Fact", StringComparison.Ordinal)
            && !source.Contains("Theory", StringComparison.Ordinal)
        )
        {
            return (0, 0);
        }

        var facts = 0;
        var theories = 0;

        foreach (
            var method in ConditionalSourceParse
                .DescendantNodes(ConditionalSourceParse.Parse(source))
                .OfType<MethodDeclarationSyntax>()
        )
        {
            var names = method
                .AttributeLists.SelectMany(list => list.Attributes)
                .Select(attribute => LastName(attribute.Name))
                .ToList();

            if (names.Any(name => name is "Theory" or "TheoryAttribute"))
            {
                theories++;
            }
            else if (names.Any(name => name is "Fact" or "FactAttribute"))
            {
                facts++;
            }
        }

        return (facts, theories);
    }

    private static string LastName(NameSyntax name) =>
        name switch
        {
            QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
            AliasQualifiedNameSyntax alias => alias.Name.Identifier.ValueText,
            SimpleNameSyntax simple => simple.Identifier.ValueText,
            _ => name.ToString(),
        };

    /// <summary>数を JSON にする（次の実行の基点として読む）</summary>
    /// <param name="census">数</param>
    public static string ToJson(OneOffCensus census) =>
        JsonSerializer.Serialize(census, JsonOptions);

    /// <summary>基点の数の JSON を読む（無いか空なら null。壊れていれば例外＝道具が動かないときは赤にする）</summary>
    /// <param name="json">JSON</param>
    public static OneOffCensus? FromJson(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<OneOffCensus>(json, JsonOptions)
                ?? throw new InvalidOperationException("単発のテストの数の JSON が null だった");

    /// <summary>
    /// 報告の Markdown：比べた基点 → 増えたファイルとフォルダ → 減ったファイルとフォルダ → 合計の順（見るのは増減なので合計を後ろに置く）
    /// </summary>
    /// <param name="current">今回の数</param>
    /// <param name="baseline">基点の数（無ければ null）</param>
    /// <param name="expectedBaseCommit">比べたい基点のコミット（PR は向き先の先端・push は直前のコミット。分からなければ空）</param>
    public static string Render(
        OneOffCensus current,
        OneOffCensus? baseline,
        string expectedBaseCommit
    )
    {
        var text = new StringBuilder();

        void Line(string line) => text.Append(line).Append('\n');

        Line("## 単発のテスト（影響の地図の守りの外の [Fact] / [Theory]）");
        Line("");

        if (baseline is null)
        {
            Line(
                $"比べた基点：なし（基点 {Short(expectedBaseCommit)} の数がキャッシュに無く、ほかのコミットの数も無い）。合計だけを書く。"
            );
        }
        else if (
            expectedBaseCommit.Length > 0
            && string.Equals(baseline.Commit, expectedBaseCommit, StringComparison.Ordinal)
        )
        {
            Line($"比べた基点：{Short(baseline.Commit)}");
        }
        else
        {
            Line(
                $"比べた基点：近似（基点 {Short(expectedBaseCommit)} の数がキャッシュに無く、いちばん新しい {Short(baseline.Commit)} の数と比べた）"
            );
        }

        if (baseline is not null)
        {
            var changes = current
                .Files.Keys.Union(baseline.Files.Keys, StringComparer.Ordinal)
                .Select(path =>
                    (
                        Path: path,
                        Before: baseline.Files.GetValueOrDefault(path),
                        After: current.Files.GetValueOrDefault(path)
                    )
                )
                .Where(change => change.Before != change.After)
                .ToList();
            var increased = changes.Where(change => change.After > change.Before).ToList();
            var decreased = changes.Where(change => change.After < change.Before).ToList();

            Line("");
            ChangeSection(
                Line,
                "増えた",
                increased
                    .OrderByDescending(change => change.After - change.Before)
                    .ThenBy(change => change.Path, StringComparer.Ordinal)
                    .ToList()
            );
            ChangeSection(
                Line,
                "減った",
                decreased
                    .OrderBy(change => change.After - change.Before)
                    .ThenBy(change => change.Path, StringComparer.Ordinal)
                    .ToList()
            );
        }

        if (baseline is null)
        {
            Line("");
        }

        Line("### 合計");
        Line("");
        Line(
            $"- 単発のテスト：{Number(current.Total)}（Fact {Number(current.Facts)}・Theory {Number(current.Theories)}）"
                + (
                    baseline is null
                        ? ""
                        : $"・基点 {Number(baseline.Total)}・増減 {Signed(current.Total - baseline.Total)}"
                )
        );
        Line(
            $"- 守りのテストのソースのメソッド（参考）：{Number(current.ProtectedMethods)}"
                + (
                    baseline is null
                        ? ""
                        : $"・増減 {Signed(current.ProtectedMethods - baseline.ProtectedMethods)}"
                )
        );
        Line(
            "- 数える範囲は tests/QuickER.Tests と tests/QuickER.Tests.Integration。守りは影響の地図（tests/QuickER.Tests/TestSupport/impact-map.md）と同じ宣言で決める。数では赤にしない。"
        );

        return text.ToString();
    }

    private static void ChangeSection(
        Action<string> line,
        string label,
        IReadOnlyList<(string Path, int Before, int After)> changes
    )
    {
        var sum = changes.Sum(change => change.After - change.Before);

        line($"### {label}ファイル（{Number(changes.Count)} ファイル・メソッド {Signed(sum)}）");
        line("");

        if (changes.Count == 0)
        {
            line("（なし）");
            line("");
            return;
        }

        line("| ファイル | 基点 | 今回 | 増減 |");
        line("|---|---|---|---|");

        foreach (var change in changes.Take(ListedFiles))
        {
            line(
                $"| `{change.Path}` | {Number(change.Before)} | {Number(change.After)} | {Signed(change.After - change.Before)} |"
            );
        }

        if (changes.Count > ListedFiles)
        {
            line($"| ほか {Number(changes.Count - ListedFiles)} ファイル | | | |");
        }

        line("");
        line($"{label}フォルダ：");
        line("");

        foreach (
            var folder in changes
                .GroupBy(change => FolderOf(change.Path), StringComparer.Ordinal)
                .Select(group => (Folder: group.Key, Delta: group.Sum(c => c.After - c.Before)))
                .OrderByDescending(group => Math.Abs(group.Delta))
                .ThenBy(group => group.Folder, StringComparer.Ordinal)
        )
        {
            line($"- `{folder.Folder}`：{Signed(folder.Delta)}");
        }

        line("");
    }

    /// <summary>報告のフォルダ（<c>tests/{プロジェクト}/{その直下のフォルダ}</c>。プロジェクトの直下のファイルはプロジェクト）</summary>
    /// <param name="path">リポジトリの根からの相対パス</param>
    public static string FolderOf(string path)
    {
        var segments = path.Split('/');

        return segments.Length > 3
            ? string.Join('/', segments.Take(3))
            : string.Join('/', segments.Take(2));
    }

    private static string Short(string commit) =>
        commit.Length == 0 ? "（不明）" : $"`{commit[..Math.Min(7, commit.Length)]}`";

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Signed(int value) => value > 0 ? "+" + Number(value) : Number(value);
}
