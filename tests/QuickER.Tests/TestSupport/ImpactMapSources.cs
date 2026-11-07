using System.Globalization;
using System.IO;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using QuickER.Tests.Docs;
using QuickER.Tests.TestSupport.Engines;
using QuickER.Tests.TestSupport.Guards;

namespace QuickER.Tests.TestSupport;

/// <summary>影響の地図の出どころ（宣言）を集めて地図を作る</summary>
/// <remarks>
/// <para>
/// 守りは次の宣言から作る（手で書いた対応表を正本にしない）：QuickER.Tests のエンジンの台帳（<see cref="EngineRegistry"/>）と
/// 単体の表の台帳（<see cref="TableRegistry"/>）、Integration の台帳の写し（<see cref="ProtectionSnapshot.IntegrationPath"/>）、
/// ソースの規則（<see cref="SourceRules"/> の範囲）、照合の表・英語の文言の表・必須の文言の表の行。
/// </para>
/// <para>
/// 規則は範囲が src の中に絞られているもの（範囲の根が <c>src/</c> の下か、明示のファイルがすべて <c>src/</c> の下）だけを単位の守りに
/// 数える。src 全体・src の外を読む規則と、照合・文言の表の行は横断の守り（範囲が空）にする。
/// </para>
/// </remarks>
internal static class ImpactMapSources
{
    /// <summary>地図の生成物（リポジトリ直下からの相対パス）</summary>
    public const string MapPath = "tests/QuickER.Tests/TestSupport/impact-map.md";

    /// <summary>QuickER.Tests のテストのフォルダ（守りの無い領域のミラーのフォルダの根）</summary>
    private const string TestsRoot = "tests/QuickER.Tests";

    /// <summary>地図の書き直しの手順（Integration の写しを先に書き直す）</summary>
    public static string RegenerateSteps { get; } =
        RegenerationRequest.Command(
            (
                "tests/QuickER.Tests.Integration/QuickER.Tests.Integration.csproj",
                "ProtectionSnapshotDrift"
            ),
            ("tests/QuickER.Tests/QuickER.Tests.csproj", "ImpactMapDrift")
        );

    /// <summary>リポジトリの宣言から地図を作る</summary>
    /// <param name="repositoryRoot">リポジトリの根</param>
    public static ImpactMap Build(string repositoryRoot) =>
        new(
            ImpactUnits.Enumerate(repositoryRoot),
            Protections(repositoryRoot),
            ImpactMapDeclarations.Unprotected
        );

    /// <summary>すべての守り（エンジン → 単体の表 → 規則 → 照合 → 英語の文言 → 必須の文言の順）</summary>
    /// <param name="repositoryRoot">リポジトリの根</param>
    public static IReadOnlyList<Protection> Protections(string repositoryRoot)
    {
        var snapshotPath = Path.Combine(repositoryRoot, ProtectionSnapshot.IntegrationPath);

        if (!File.Exists(snapshotPath))
        {
            throw new InvalidOperationException(
                $"Integration の台帳の写し {ProtectionSnapshot.IntegrationPath} が無い。書き直しの手順：{RegenerateSteps}"
            );
        }

        var integration = ProtectionSnapshot.Parse(File.ReadAllText(snapshotPath));
        var protections = new List<Protection>();

        protections.AddRange(
            EngineRegistry
                .All.Select(engine =>
                    (engine.Id, engine.Part, engine.Title, engine.SourceFiles, engine.Covers)
                )
                .Concat(
                    integration.Engines.Select(engine =>
                        (engine.Id, engine.Part, engine.Title, engine.SourceFiles, engine.Covers)
                    )
                )
                .Select(engine => new Protection(
                    $"{engine.Id}/{engine.Part}",
                    ProtectionKind.Engine,
                    engine.Title,
                    engine.SourceFiles,
                    UnitCovers(engine.Covers),
                    OtherCovers(engine.Covers)
                ))
        );
        protections.AddRange(
            TableRegistry
                .All.Concat(integration.Tables)
                .Select(table => new Protection(
                    $"table:{table.Name}",
                    ProtectionKind.Table,
                    table.Title,
                    table.SourceFiles,
                    table.Covers,
                    ""
                ))
        );
        protections.AddRange(SourceRules.All.Select(RuleProtection));
        protections.AddRange(
            ParityRows.All.Select(row =>
                CrossCutting($"parity:{row.Id}", ProtectionKind.Parity, "正本と写しの照合")
            )
        );
        protections.AddRange(
            EnglishTextRows.All.Select(row =>
                CrossCutting($"english:{row.Id}", ProtectionKind.English, "英語の文言の照合")
            )
        );
        protections.AddRange(
            PhraseRows.All.Select(row =>
                CrossCutting($"phrase:{row.Id}", ProtectionKind.Phrase, "必須の文言の照合")
            )
        );

        return protections;
    }

    /// <summary>決まった形の行の表の型の名前</summary>
    private const string ScenarioTableTypeName = "ScenarioTable";

    /// <summary>
    /// ソースが決まった形の行の表の型を使うか：型の名前が総称の名前の節（<c>ScenarioTable&lt;T&gt;</c>）として現れるか。条件コンパイルのどの分岐も読み
    /// （<see cref="ConditionalSourceParse"/>）、コメント・XmlDoc・文字列の中の綴りは数えない
    /// </summary>
    /// <param name="source">C# のソースの本文</param>
    public static bool UsesScenarioTable(string source) =>
        source.Contains(ScenarioTableTypeName, StringComparison.Ordinal)
        && ConditionalSourceParse
            .DescendantNodes(ConditionalSourceParse.Parse(source))
            .OfType<GenericNameSyntax>()
            .Any(name => name.Identifier.ValueText == ScenarioTableTypeName);

    /// <summary>
    /// 決まった形の行の表を使うのに、エンジンの部分にも単体の表にも登録しなくてよいテストのソース（パスと理由）
    /// </summary>
    public static IReadOnlyList<(string Path, string Reason)> ScenarioTableExemptions { get; } =
    [
        (
            "tests/QuickER.Tests/TestSupport/ScenarioTableTests.cs",
            "表の型そのものの自己検査（架空の行で表の構築の検査を確かめる。src を守る表ではない）"
        ),
    ];

    /// <summary>
    /// 決まった形の行の表を使う（<see cref="UsesScenarioTable"/>）のに、どの守りのテストのソース（部分ファイルを含む）にも、
    /// 登録しなくてよいソースの一覧にも無いファイル（序数の順）
    /// </summary>
    /// <remarks>
    /// 表の型の本体（<c>TestSupport/Engines/</c>）と <c>bin</c> / <c>obj</c> は読まない。表を足したら、エンジンの部分のソースか単体の表の台帳
    /// （<see cref="TableRegistry"/>）に入れる。入れ忘れると表の守りの範囲が地図から漏れる。
    /// </remarks>
    /// <param name="protections">守り</param>
    /// <param name="repositoryRoot">リポジトリの根</param>
    public static IReadOnlyList<string> UnregisteredScenarioTableSources(
        IReadOnlyList<Protection> protections,
        string repositoryRoot
    )
    {
        var registered = protections
            .SelectMany(protection => ExpandPartials(repositoryRoot, protection.SourceFiles))
            .Concat(ScenarioTableExemptions.Select(exemption => exemption.Path))
            .ToHashSet(StringComparer.Ordinal);

        return ScenarioTableSources(repositoryRoot)
            .Where(path => !registered.Contains(path))
            .ToList();
    }

    /// <summary>決まった形の行の表を使うテストのソース（表の型の本体を除く・序数の順）</summary>
    /// <param name="repositoryRoot">リポジトリの根</param>
    public static IReadOnlyList<string> ScenarioTableSources(string repositoryRoot) =>
        new[] { TestsRoot, "tests/QuickER.Tests.Integration" }
            .SelectMany(folder =>
                Directory.EnumerateFiles(
                    Path.Combine(repositoryRoot, folder),
                    "*.cs",
                    SearchOption.AllDirectories
                )
            )
            .Select(path => Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/'))
            .Where(path =>
                !path.Split('/').Any(segment => segment is "bin" or "obj")
                && !path.StartsWith(TestsRoot + "/TestSupport/Engines/", StringComparison.Ordinal)
            )
            .Where(path => UsesScenarioTable(File.ReadAllText(Path.Combine(repositoryRoot, path))))
            .Order(StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// 守りの無い領域の単位 → エンジン・表の外の手書きのテストがあるミラーのフォルダ（<c>src/QuickER.X/Y</c> → <c>tests/QuickER.Tests/X/Y</c>）
    /// </summary>
    /// <param name="map">地図</param>
    /// <param name="repositoryRoot">リポジトリの根</param>
    public static IReadOnlyDictionary<string, string> HandwrittenTests(
        ImpactMap map,
        string repositoryRoot
    ) =>
        HandwrittenTestFiles(map, repositoryRoot)
            .Where(pair => pair.Value.Count > 0)
            .ToDictionary(pair => pair.Key, pair => MirrorFolder(pair.Key), StringComparer.Ordinal);

    /// <summary>守りの無い領域の単位 → ミラーのフォルダにある、エンジン・表のソースでないテストのファイル（報告用）</summary>
    /// <param name="map">地図</param>
    /// <param name="repositoryRoot">リポジトリの根</param>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> HandwrittenTestFiles(
        ImpactMap map,
        string repositoryRoot
    )
    {
        var registered = map
            .Protections.SelectMany(protection =>
                ExpandPartials(repositoryRoot, protection.SourceFiles)
            )
            .ToHashSet(StringComparer.Ordinal);
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        foreach (var area in map.Unprotected)
        {
            var folder = Path.Combine(repositoryRoot, MirrorFolder(area.Unit));

            result[area.Unit] = Directory.Exists(folder)
                ? Directory
                    .EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)
                    .Select(path => Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/'))
                    .Where(path => !registered.Contains(path))
                    .Order(StringComparer.Ordinal)
                    .ToList()
                : [];
        }

        return result;
    }

    /// <summary>単位のミラーのテストのフォルダ</summary>
    private static string MirrorFolder(string unit) =>
        $"{TestsRoot}/{unit["src/QuickER.".Length..]}";

    /// <summary>規則の守り（範囲が src の中に絞られていれば単位の守り、そうでなければ横断）</summary>
    private static Protection RuleProtection(SourceRule rule)
    {
        var scope = rule.Scope;
        IReadOnlyList<string> covers =
            scope.Only.Count > 0
                ? scope.Only.All(path => path.StartsWith("src/", StringComparison.Ordinal))
                    ? scope.Only
                    : []
                : scope.Root.StartsWith("src/", StringComparison.Ordinal)
                    ? [scope.Root]
                    : [];

        return new Protection(
            $"rule:{rule.Id}",
            ProtectionKind.Rule,
            "",
            [],
            covers,
            covers.Count == 0 ? ScopeLabel(scope) : ""
        );
    }

    /// <summary>単位の守りに数える範囲（src のプロジェクトの中を指すもの）</summary>
    private static IReadOnlyList<string> UnitCovers(IReadOnlyList<string> covers) =>
        covers.Where(IsUnderProject).ToList();

    /// <summary>単位の守りに数えない範囲（src 全体・src の外）の表記（無ければ空・範囲の宣言が無ければその旨）</summary>
    private static string OtherCovers(IReadOnlyList<string> covers) =>
        covers.Count == 0
            ? "（範囲の宣言が無い）"
            : string.Join("・", covers.Where(cover => !IsUnderProject(cover)));

    private static bool IsUnderProject(string path) =>
        path.StartsWith(ImpactUnits.SourceRoot + "/", StringComparison.Ordinal);

    /// <summary>横断の規則の読む範囲の短い表記（除外は件数だけ。除外の中身は規則の表にある）</summary>
    private static string ScopeLabel(SourceScope scope) =>
        scope.Only.Count > 0
            ? string.Join("・", scope.Only)
            : $"{scope.Root}/**/*{scope.Extension}"
                + (
                    scope.Exclusions.Count > 0
                        ? $"（除外 {scope.Exclusions.Count.ToString(CultureInfo.InvariantCulture)} 件）"
                        : ""
                );

    private static Protection CrossCutting(string name, ProtectionKind kind, string scope) =>
        new(name, kind, "", [], [], scope);

    /// <summary>
    /// 行 Id（<c>*</c> の省略を含む。CLAUDE.md の照合と同じ判定 <see cref="ClaudeMdReferenceTests.RowIdExists"/>）を、テストのソース
    /// （部分ファイルを含む）に持つ守りの名前（渡した順）
    /// </summary>
    /// <remarks>
    /// 表の行の名前は「守りの名前＋行 Id」（例：<c>Dialect/DdlText</c> の行 <c>ddl-*-trimmed-table</c>）。CLAUDE.md の名指しを地図の名前へ
    /// 寄せるとき（P4-5）、行 Id がその守りのものかをこれで確かめる。地図の Markdown には行 Id の接頭辞を載せない（テストのソースの
    /// 文字列リテラルから読むと値のデータまで拾い、行やデータを足すたびに地図が変わる）。
    /// </remarks>
    /// <param name="protections">守り</param>
    /// <param name="repositoryRoot">リポジトリの根</param>
    /// <param name="rowId">行 Id</param>
    public static IReadOnlyList<string> RowIdOwners(
        IReadOnlyList<Protection> protections,
        string repositoryRoot,
        string rowId
    ) =>
        protections
            .Where(protection =>
                ExpandPartials(repositoryRoot, protection.SourceFiles)
                    .Any(path =>
                        ClaudeMdReferenceTests.RowIdExists(
                            rowId,
                            File.ReadAllText(Path.Combine(repositoryRoot, path))
                        )
                    )
            )
            .Select(protection => protection.Name)
            .ToList();

    /// <summary>
    /// テストのソースに、その部分ファイル（同じフォルダの <c>{名前}.*.cs</c>）を足したもの（実在するものだけ・宣言の順のあとに部分ファイルを序数の順）
    /// </summary>
    /// <remarks>
    /// 台帳のソースは partial の本体だけを書き、部分ファイル（<c>DialectDdlRows.Names.cs</c> など）を書かないことがあるので、
    /// 行 Id の持ち主・登録の照合はこの広げたソースで読む。
    /// </remarks>
    /// <param name="repositoryRoot">リポジトリの根</param>
    /// <param name="sourceFiles">台帳のテストのソース</param>
    public static IReadOnlyList<string> ExpandPartials(
        string repositoryRoot,
        IReadOnlyList<string> sourceFiles
    )
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var path in sourceFiles)
        {
            var full = Path.Combine(repositoryRoot, path);

            if (!File.Exists(full))
            {
                continue;
            }

            if (seen.Add(path))
            {
                result.Add(path);
            }

            var folder = Path.GetDirectoryName(full)!;
            var partials = Directory
                .EnumerateFiles(folder, Path.GetFileNameWithoutExtension(full) + ".*.cs")
                .Select(partial => Path.GetRelativePath(repositoryRoot, partial).Replace('\\', '/'))
                .Order(StringComparer.Ordinal);

            foreach (var partial in partials.Where(seen.Add))
            {
                result.Add(partial);
            }
        }

        return result;
    }
}
