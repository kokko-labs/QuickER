using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using QuickER.Tests.Resources;

namespace QuickER.Tests.Docs;

/// <summary>
/// リポジトリの全 CLAUDE.md（ルートと領域のフォルダ）が名指すテストの名前・期待の行の Id と、
/// ほかの CLAUDE.md への参照が実在することを固定する。
/// </summary>
/// <remarks>
/// <para>
/// CLAUDE.md は不変条件ごとにガードテストの名前と期待の行の Id を書き、ルートの索引から詳細の CLAUDE.md の
/// 見出しを指す。テストの改名・削除や見出しの書き換えでは、CLAUDE.md を思い出さない限り名前が古いまま残るので、
/// 書かれた名前の側を実体と突き合わせる（<see cref="ClaudeMdParityTests"/> と同じ流儀）。
/// </para>
/// <para>
/// 書式の契約（書き方の正本は <c>tests/QuickER.Tests/Docs/CLAUDE.md</c> の「CLAUDE.md の照合」）：
/// (1) テストの名前は <c>XxxTests</c> / <c>XxxTestsBase</c> の形で拾い、<c>Prefix{A,B}Suffix</c> の中括弧の
/// 省略は展開する。直前が <c>*</c> の名前（<c>*DdlGeneratorTests</c>）はパターンなので照合しない。
/// (2) <c>XxxTests.cs</c> はファイルの実在、<c>XxxTests.Method</c> はそのクラスを宣言するファイルにメソッド名が
/// 現れることで照合する（メソッド名が <c>_</c> で終わるか直後が <c>*</c> なら前方一致）。
/// (3) 期待の行・シナリオの Id は、ハイフンを含みハイフンで終わらないバッククォートの小文字の語のうち、
/// 最初のハイフンまでの語が行 Id の接頭辞の集合（<see cref="RowIdPrefixesOf"/>）にあるものを拾い、tests/ の
/// C# ソースに現れることで照合する。接頭辞の集合は手で持たず、tests/ の C# ソースの文字列リテラル
/// （補間・連結の先頭を含む）の先頭の、最初のハイフンまでの語から作る（表に新しい接頭辞の行を足せば、
/// そのまま照合の対象に入る）。接頭辞が当たる行 Id でない語は、理由つきの既知の一覧
/// （<see cref="NotRowIds"/>）に置く。Id の多くは
/// <c>$"realdb-column-drop-{…}"</c> や <c>"realdb-pk-on-new-column-" + shape</c> のように組み立てるので、
/// ハイフンで切った接頭辞（ハイフン 2 つ以上）が補間か連結の直前（<c>P{</c> / <c>P"</c>）に現れるか、
/// 接頭辞と残りの文字列リテラル <c>"S"</c> の両方が現れれば実在とみなす（組み立てる Id は接頭辞までしか
/// 照合できない）。<c>*</c> を含む Id は断片ごとに照合し、ハイフンで始まる断片は、そのまま現れるか、
/// 先頭のハイフンを外した文字列リテラルの先頭（<c>"S</c>）として現れればよい（<c>$"module-{a}-{b}"</c> の
/// 後ろを別のリテラルで組み立てる形）。
/// (4) 別の CLAUDE.md は <c>`path/CLAUDE.md`</c> の形（リポジトリのルートからの相対パス）で書き、直後の
/// 「」に見出しを書いたときは、その CLAUDE.md にその文字列の見出し（<c>#</c> の行）があることを照合する。
/// 書式を変えるときはこの契約も更新すること。
/// </para>
/// </remarks>
public class ClaudeMdReferenceTests
{
    /// <summary>リポジトリルート</summary>
    private static readonly string Root = NeutralResxFiles.FindRepositoryRoot();

    /// <summary>CLAUDE.md とテストのソースを探さないフォルダ（ビルド成果物・git・worktree のコピー・非公開の開発メモ）</summary>
    private static readonly HashSet<string> SkippedDirectories = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        "bin",
        "obj",
        ".git",
        ".vs",
        ".claude",
        "node_modules",
        "TestResults",
        "tasks",
    };

    /// <summary>
    /// 行 Id の接頭辞が当たるのに行 Id でない語（CLAUDE.md のパス・語・理由）。照合は件数まで 2 方向
    /// （<see cref="TestSupport.GuardKnownList"/>）で、CLAUDE.md から消えた語や tests/ に現れるようになった語は赤になる
    /// </summary>
    internal static readonly (string Path, string Word, string Reason)[] NotRowIds =
    [
        (
            "src/QuickER.Provider/CLAUDE.md",
            "a--b",
            "SQLite が返す宣言型の任意のテキストの例（行 Id でない）"
        ),
        (
            "tests/QuickER.Tests/TestSupport/CLAUDE.md",
            "integration-nightly-trx",
            "nightly.yml の成果物の名前（ワークフローの値で、テストのソースには無い）"
        ),
        (
            "tests/QuickER.Tests/TestSupport/CLAUDE.md",
            "unit-nightly-trx",
            "nightly.yml の成果物の名前（ワークフローの値で、テストのソースには無い）"
        ),
    ];

    /// <summary>行 Id の候補（バッククォートで囲まれ、ハイフンを含み、ハイフンで終わらない小文字の語）</summary>
    private static readonly Regex RowIdCandidate = new(
        "`([a-z][0-9a-z]*-[-0-9a-z*]*[0-9a-z*])`",
        RegexOptions.CultureInvariant
    );

    /// <summary>文字列リテラル（補間・連結の先頭を含む）の先頭の、最初のハイフンまでの語</summary>
    private static readonly Regex LiteralRowIdPrefix = new(
        "\"([a-z][0-9a-z]*)-[0-9a-z{\"]",
        RegexOptions.CultureInvariant
    );

    /// <summary>行 Id の候補の最初のハイフン（か <c>*</c>）までの語</summary>
    private static readonly Regex LeadingSegment = new("^[0-9a-z]+", RegexOptions.CultureInvariant);

    /// <summary>中括弧の省略 <c>Prefix{A,B}Suffix</c></summary>
    private static readonly Regex BraceAbbreviation = new(
        @"([A-Za-z0-9_]*)\{([A-Za-z0-9_,]+)\}(\w*)",
        RegexOptions.CultureInvariant
    );

    /// <summary>テストの名前（直後の <c>.cs</c> / <c>.Method</c> / <c>*</c> も読む）</summary>
    private static readonly Regex TestName = new(
        @"(?<![A-Za-z0-9_*.])([A-Z][A-Za-z0-9_]*Tests(?:Base)?)(\.cs\b|\.([^\W\d]\w*)(\*)?)?(?![A-Za-z0-9_])",
        RegexOptions.CultureInvariant
    );

    /// <summary>CLAUDE.md への参照（見出しは任意）</summary>
    private static readonly Regex ClaudeMdLink = new(
        @"`([^`\s]+/CLAUDE\.md)`(?:「([^」]+)」)?",
        RegexOptions.CultureInvariant
    );

    /// <summary>tests/ の C# ソース</summary>
    /// <param name="FileNames">ファイル名の集合</param>
    /// <param name="Declarations">宣言された型の名前 → 宣言したファイルの本文</param>
    /// <param name="AllText">全ソースを連結した本文（Id の照合に使う）</param>
    internal sealed record TestSources(
        IReadOnlySet<string> FileNames,
        IReadOnlyDictionary<string, List<string>> Declarations,
        string AllText
    )
    {
        /// <summary>行 Id の接頭辞の集合（<see cref="RowIdPrefixesOf"/>）</summary>
        public IReadOnlySet<string> RowIdPrefixes { get; } = RowIdPrefixesOf(AllText);
    }

    /// <summary>
    /// 行 Id の接頭辞の集合：ソースの文字列リテラル（補間・連結の先頭を含む）の先頭の、最初のハイフンまでの語
    /// </summary>
    /// <remarks>
    /// 行 Id は表の行の文字列リテラル（<c>"realdb-…"</c>・<c>$"module-{…}"</c>・<c>"coverage-" + x</c>）なので、
    /// その先頭の語を集めれば、手で接頭辞を足さなくても新しい表の行が照合の対象に入る。
    /// </remarks>
    /// <param name="source">tests/ の全ソース</param>
    internal static IReadOnlySet<string> RowIdPrefixesOf(string source) =>
        LiteralRowIdPrefix
            .Matches(source)
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>tests/ のソース（1 回だけ読む）</summary>
    private static readonly Lazy<TestSources> Sources = new(ReadTestSources);

    /// <summary>リポジトリの全 CLAUDE.md（ルートからの相対パス → 本文）</summary>
    private static IReadOnlyDictionary<string, string> ClaudeMdFiles()
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var pending = new Stack<string>();
        pending.Push(Root);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            var path = Path.Combine(directory, "CLAUDE.md");

            if (File.Exists(path))
            {
                result[Path.GetRelativePath(Root, path).Replace('\\', '/')] = File.ReadAllText(
                    path
                );
            }

            foreach (var child in Directory.GetDirectories(directory))
            {
                if (!SkippedDirectories.Contains(Path.GetFileName(child)))
                {
                    pending.Push(child);
                }
            }
        }

        return result;
    }

    /// <summary>中括弧の省略を展開する（入れ子は無い前提で、変わらなくなるまで繰り返す）</summary>
    /// <param name="text">展開する本文</param>
    internal static string ExpandBraces(string text)
    {
        string previous;

        do
        {
            previous = text;
            text = BraceAbbreviation.Replace(
                text,
                match =>
                    string.Join(
                        ' ',
                        match
                            .Groups[2]
                            .Value.Split(',')
                            .Select(part => match.Groups[1].Value + part + match.Groups[3].Value)
                    )
            );
        } while (text != previous);

        return text;
    }

    /// <summary>tests/ のソースを読む</summary>
    private static TestSources ReadTestSources()
    {
        var fileNames = new HashSet<string>(StringComparer.Ordinal);
        var declarations = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var all = new List<string>();
        var declaration = new Regex(
            @"\b(?:class|record|struct|interface)\s+([A-Za-z_][A-Za-z0-9_]*)",
            RegexOptions.CultureInvariant
        );

        foreach (
            var path in Directory.EnumerateFiles(
                Path.Combine(Root, "tests"),
                "*.cs",
                SearchOption.AllDirectories
            )
        )
        {
            var relative = Path.GetRelativePath(Root, path).Replace('\\', '/');

            if (relative.Split('/').Any(SkippedDirectories.Contains))
            {
                continue;
            }

            fileNames.Add(Path.GetFileName(path));
            var source = File.ReadAllText(path);

            // このファイル自身は Id の照合の本文に入れない（行 Id でない語の一覧と自己検査の架空の Id が写り込み、
            // 一覧の語や架空の Id が tests/ に実在するように見えてしまう）。宣言とファイル名の照合には入れる
            if (Path.GetFileName(path) != nameof(ClaudeMdReferenceTests) + ".cs")
            {
                all.Add(source);
            }

            foreach (Match match in declaration.Matches(source))
            {
                if (!declarations.TryGetValue(match.Groups[1].Value, out var files))
                {
                    files = [];
                    declarations[match.Groups[1].Value] = files;
                }

                files.Add(source);
            }
        }

        return new TestSources(fileNames, declarations, string.Join('\n', all));
    }

    /// <summary>本文が名指すテストの名前のうち、tests/ に実在しないものを返す</summary>
    /// <param name="text">CLAUDE.md の本文</param>
    /// <param name="sources">tests/ のソース</param>
    internal static IReadOnlyList<string> FindMissingTestNames(string text, TestSources sources)
    {
        var missing = new SortedSet<string>(StringComparer.Ordinal);

        foreach (Match match in TestName.Matches(ExpandBraces(text)))
        {
            var className = match.Groups[1].Value;

            if (match.Groups[2].Value == ".cs")
            {
                if (!sources.FileNames.Contains(className + ".cs"))
                {
                    missing.Add(className + ".cs");
                }

                continue;
            }

            if (!sources.Declarations.TryGetValue(className, out var files))
            {
                missing.Add(className);
                continue;
            }

            if (!match.Groups[3].Success)
            {
                continue;
            }

            var method = match.Groups[3].Value;
            var prefix = method.EndsWith('_') || match.Groups[4].Success;
            var pattern = new Regex(
                @"(?<!\w)" + Regex.Escape(method) + (prefix ? "" : @"(?!\w)"),
                RegexOptions.CultureInvariant
            );

            if (!files.Any(source => pattern.IsMatch(source)))
            {
                missing.Add(className + "." + method);
            }
        }

        return [.. missing];
    }

    /// <summary>本文が名指す期待の行・シナリオの Id のうち、tests/ のソースに現れないものを返す</summary>
    /// <param name="text">CLAUDE.md の本文</param>
    /// <param name="sources">tests/ のソース</param>
    internal static IReadOnlyList<string> FindMissingRowIds(string text, TestSources sources)
    {
        var missing = new SortedSet<string>(StringComparer.Ordinal);

        foreach (Match match in RowIdCandidate.Matches(text))
        {
            var id = match.Groups[1].Value;

            if (!sources.RowIdPrefixes.Contains(LeadingSegment.Match(id).Value))
            {
                continue;
            }

            if (!RowIdExists(id, sources.AllText))
            {
                missing.Add(id);
            }
        }

        return [.. missing];
    }

    /// <summary>Id が tests/ のソースに現れるか（組み立てる Id は接頭辞まで照合する）</summary>
    /// <param name="id">Id（<c>*</c> を含んでよい）</param>
    /// <param name="source">tests/ の全ソース</param>
    private static bool RowIdExists(string id, string source)
    {
        var fragments = id.Split('*');
        var first = fragments[0];

        if (
            !fragments
                .Skip(1)
                .Where(f => f.Length > 0)
                .All(f =>
                    source.Contains(f, StringComparison.Ordinal)
                    || (
                        f.Length > 1
                        && f[0] == '-'
                        && source.Contains("\"" + f[1..], StringComparison.Ordinal)
                    )
                )
        )
        {
            return false;
        }

        if (source.Contains(first, StringComparison.Ordinal))
        {
            return true;
        }

        for (var index = 0; index < first.Length; index++)
        {
            if (first[index] != '-')
            {
                continue;
            }

            var head = first[..(index + 1)];
            var rest = first[(index + 1)..];
            var hyphens = head.Count(c => c == '-');

            if (
                hyphens >= 2
                && (
                    source.Contains(head + "{", StringComparison.Ordinal)
                    || source.Contains(head + "\"", StringComparison.Ordinal)
                )
            )
            {
                return true;
            }

            if (
                rest.Length > 0
                && source.Contains(head, StringComparison.Ordinal)
                && source.Contains("\"" + rest + "\"", StringComparison.Ordinal)
            )
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>本文が名指す CLAUDE.md への参照のうち、ファイルか見出しが実在しないものを返す</summary>
    /// <param name="text">CLAUDE.md の本文</param>
    /// <param name="files">全 CLAUDE.md（ルートからの相対パス → 本文）</param>
    internal static IReadOnlyList<string> FindBrokenLinks(
        string text,
        IReadOnlyDictionary<string, string> files
    )
    {
        var broken = new SortedSet<string>(StringComparer.Ordinal);

        foreach (Match match in ClaudeMdLink.Matches(text))
        {
            var path = match.Groups[1].Value;

            if (!files.TryGetValue(path, out var target))
            {
                broken.Add(path);
                continue;
            }

            if (!match.Groups[2].Success)
            {
                continue;
            }

            var heading = match.Groups[2].Value;
            var hasHeading = target
                .Split('\n')
                .Select(line => line.TrimEnd('\r'))
                .Any(line =>
                    Regex.IsMatch(
                        line,
                        @"^#{1,6}\s+" + Regex.Escape(heading) + @"\s*$",
                        RegexOptions.CultureInvariant
                    )
                );

            if (!hasHeading)
            {
                broken.Add($"{path}「{heading}」");
            }
        }

        return [.. broken];
    }

    /// <summary>全 CLAUDE.md が名指すテストの名前が tests/ に実在すること</summary>
    [Fact(DisplayName = "CLAUDE.md 群が名指すテストの名前は、tests/ に実在する")]
    public void TestNames_ExistInTests()
    {
        var files = ClaudeMdFiles();
        files.Should().ContainKey("CLAUDE.md");

        var problems = files
            .SelectMany(file =>
                FindMissingTestNames(file.Value, Sources.Value)
                    .Select(name => $"{file.Key}: {name}")
            )
            .ToList();

        problems
            .Should()
            .BeEmpty(
                "CLAUDE.md に書いたテストの名前が tests/ に無い（改名・削除に追従するか、名前を直すこと）:"
                    + Environment.NewLine
                    + string.Join(Environment.NewLine, problems)
            );
    }

    /// <summary>全 CLAUDE.md が名指す期待の行・シナリオの Id が tests/ のソースに現れること</summary>
    [Fact(DisplayName = "CLAUDE.md 群が名指す期待の行・シナリオの Id は、tests/ のソースに現れる")]
    public void RowIds_ExistInTests()
    {
        var files = ClaudeMdFiles();

        var found = files
            .SelectMany(file =>
                FindMissingRowIds(file.Value, Sources.Value).Select(id => (file.Key, id))
            )
            .GroupBy(entry => entry)
            .ToDictionary(group => group.Key, group => group.Count());

        TestSupport.GuardKnownList.AssertMatches(
            found,
            NotRowIds.Select(entry => ((entry.Path, entry.Word), 1)),
            (key, _) => $"{key.Item1}: {key.Item2}",
            "CLAUDE.md に書いた期待の行・シナリオの Id が tests/ に無い（改名・削除に追従するか、Id を直すこと。"
                + "行 Id でない語なら理由を添えて NotRowIds に置く）",
            "NotRowIds の語が CLAUDE.md から消えたか tests/ に現れるようになった（一覧から消す）"
        );
    }

    /// <summary>全 CLAUDE.md が名指す CLAUDE.md とその見出しが実在すること</summary>
    [Fact(DisplayName = "CLAUDE.md 群が指す CLAUDE.md のパスと見出しは実在する")]
    public void ClaudeMdLinks_PointToExistingFilesAndHeadings()
    {
        var files = ClaudeMdFiles();
        files.Count.Should().BeGreaterThan(1, "領域のフォルダの CLAUDE.md が見つかること");

        var problems = files
            .SelectMany(file =>
                FindBrokenLinks(file.Value, files).Select(link => $"{file.Key}: {link}")
            )
            .ToList();

        problems
            .Should()
            .BeEmpty(
                "CLAUDE.md が指すファイルか見出しが無い（パスか見出しを直すこと）:"
                    + Environment.NewLine
                    + string.Join(Environment.NewLine, problems)
            );
    }

    /// <summary>照合の読み方そのものの自己検査（架空の名前・Id・パス・見出しを拾うこと）</summary>
    [Fact(DisplayName = "CLAUDE.md の照合は、架空のテストの名前・Id・パス・見出しを名指しする")]
    public void Checker_ReportsFictitiousNamesIdsAndLinks()
    {
        var sources = new TestSources(
            new HashSet<string>(StringComparer.Ordinal) { "RealGuardTests.cs" },
            new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                ["RealGuardTests"] = ["public void Existing_Method() { }"],
            },
            "\"realdb-real-row\" $\"realdb-built-row-{x}\" \"coverage-\" \"server-stub\" \"plan-other\" "
                + "$\"module-{a}-{b}\" \"launcher-reporter\""
        );

        sources
            .RowIdPrefixes.Should()
            .BeEquivalentTo(
                ["realdb", "coverage", "server", "plan", "module", "launcher"],
                "接頭辞の集合は文字列リテラル・補間・連結の先頭の語から作る"
            );

        FindMissingTestNames(
                "ガード：`RealGuardTests`・`NoSuchGuardTests`・Real{Guard,Fake}Tests・"
                    + "`RealGuardTests.Existing_Method`・`RealGuardTests.NoSuchMethod`・"
                    + "`RealGuardTests.Existing_*`・`RealGuardTests.cs`・`NoSuchFileTests.cs`・*AnyPatternTests",
                sources
            )
            .Should()
            .BeEquivalentTo(
                "NoSuchGuardTests",
                "RealFakeTests",
                "RealGuardTests.NoSuchMethod",
                "NoSuchFileTests.cs"
            );

        FindMissingRowIds(
                "`realdb-real-row`・`realdb-built-row-with-default`・`coverage-server-stub`・"
                    + "`realdb-real-*`・`realdb-no-such-row`・`coverage-no-such-stub`・`plan-*-no-such`・"
                    + "`module-*-launcher-reporter`・`module-*-launcher-missing`・`unknown-prefix-word`",
                sources
            )
            .Should()
            .BeEquivalentTo(
                "realdb-no-such-row",
                "coverage-no-such-stub",
                "plan-*-no-such",
                "module-*-launcher-missing"
            );

        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["a/CLAUDE.md"] = "# A\n\n## 見出し\n",
        };

        FindBrokenLinks(
                "`a/CLAUDE.md`「見出し」・`a/CLAUDE.md`「無い見出し」・`b/CLAUDE.md`・`a/CLAUDE.md`",
                files
            )
            .Should()
            .BeEquivalentTo("a/CLAUDE.md「無い見出し」", "b/CLAUDE.md");
    }
}
