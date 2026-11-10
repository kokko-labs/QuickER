using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using QuickER.Tests.Resources;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;

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
/// <c>…ForTests</c> で終わる名前（<c>ResetForTests</c> など）は製品のテスト用メンバーで、テストのクラスではないので拾わない。
/// (2) <c>XxxTests.cs</c> はファイルの実在、<c>XxxTests.Method</c> はそのクラスを宣言するファイルにメソッド名が
/// 現れることで照合する（メソッド名が <c>_</c> で終わるか直後が <c>*</c> なら前方一致）。
/// (3) 期待の行・シナリオの Id は、ハイフンを含みハイフンで終わらないバッククォートの小文字の語のうち、
/// 最初のハイフンまでの語が行 Id の接頭辞の集合（<see cref="RowIdPrefixesOf"/>）にあるものを拾い、tests/ の
/// C# ソースに現れることで照合する（このファイルと、作らない形の宣言のファイル <c>*NotGenerated.cs</c> は照合の本文に入れない＝
/// 書いた Id がそのファイル自身に当たって実在に見えるため）。接頭辞の集合は手で持たず、tests/ の C# ソースの文字列リテラル
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
/// (5) 影響の地図の名前か (1) のテストのクラスの名前の直後の（…）の中と「の行 `a` / `b`」（「の」の後に「規則」「形の行」のような
/// 短い修飾を置いてよい）に書いた (3) の行 Id は、(3) の全体の照合に加えて、その持ち主のテストのソースだけで照合する
/// （<see cref="FindForeignRowIds"/>）。地図の名前はその守りのソース、クラスの名前はそのクラスを宣言するファイルを含む守りのどれか
/// （守りに属さなければ宣言するファイル）。
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

    /// <summary>テストの名前（直後の <c>.cs</c> / <c>.Method</c> / <c>*</c> も読む。<c>…ForTests</c> は製品のテスト用メンバーなので読まない）</summary>
    private static readonly Regex TestName = new(
        @"(?<![A-Za-z0-9_*.])([A-Z][A-Za-z0-9_]*(?<!For)Tests(?:Base)?)(\.cs\b|\.([^\W\d]\w*)(\*)?)?(?![A-Za-z0-9_])",
        RegexOptions.CultureInvariant
    );

    /// <summary>バッククォートで囲んだ語（<see cref="ReferencesIn"/> が読めない語を探す）</summary>
    private static readonly Regex BacktickWord = new("`([^`]+)`", RegexOptions.CultureInvariant);

    /// <summary>CLAUDE.md への参照（見出しは任意）</summary>
    private static readonly Regex ClaudeMdLink = new(
        @"`([^`\s]+/CLAUDE\.md)`(?:「([^」]+)」)?",
        RegexOptions.CultureInvariant
    );

    /// <summary>
    /// 影響の地図の名前の候補：バッククォートの中身の全体が、エンジンの部分（ID は <see cref="EngineIds.All"/>・部分は大文字で始まる英数字）か、
    /// 前置き（<see cref="ProtectionNames"/>）＋表の名前（英字で始まる英数字）か規則・行の Id（小文字・数字・ハイフン）の形のもの
    /// </summary>
    private static readonly Regex MapNameCandidate = new(
        "`((?:"
            + string.Join("|", EngineIds.All.Select(Regex.Escape))
            + ")/[A-Z][A-Za-z0-9]*|"
            + Regex.Escape(ProtectionNames.Prefix(ProtectionKind.Table))
            + "[A-Za-z][A-Za-z0-9]*|(?:"
            + string.Join(
                "|",
                ProtectionNames
                    .PrefixedKinds.Where(kind => kind != ProtectionKind.Table)
                    .Select(kind => Regex.Escape(ProtectionNames.Prefix(kind)))
            )
            + ")[a-z0-9][a-z0-9-]*)`",
        RegexOptions.CultureInvariant
    );

    /// <summary>
    /// 名前に続く「の行 `a` / `b`」（区切りは <c>/</c> か <c>・</c>）。「の」と最初のバッククォートの間には、句読点・空白・括弧・
    /// 区切りを含まない 10 字以内の修飾（「行」「規則」「形の行」など。無くてもよい）を置ける
    /// </summary>
    private static readonly Regex FollowingRowList = new(
        @"\G\s*の[^`。、（）「」\s・／/]{0,10}?\s*`[^`]+`(?:\s*(?:/|・)\s*`[^`]+`)*",
        RegexOptions.CultureInvariant
    );

    /// <summary>
    /// 行 Id の持ち主を読むときの中括弧の省略の展開の区切り（同じ省略から出た名前の並びを 1 組として見分ける。
    /// CLAUDE.md の本文には現れない制御文字）
    /// </summary>
    private const char AbbreviationSeparator = '\u0001';

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

        /// <summary>宣言された型の名前 → 宣言したファイル（リポジトリのルートからの相対パス・<c>/</c> 区切り）</summary>
        public IReadOnlyDictionary<string, List<string>> DeclarationFiles { get; init; } =
            new Dictionary<string, List<string>>(StringComparer.Ordinal);
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

    /// <summary>tests/ のソース（1 回だけ読む。src・ワークフローの名指しの規則〔SourceRules.Comments.cs〕も使う）</summary>
    internal static readonly Lazy<TestSources> Sources = new(ReadTestSources);

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
    /// <param name="separator">展開した名前の間に置く文字（既定は空白）</param>
    internal static string ExpandBraces(string text, char separator = ' ')
    {
        string previous;

        do
        {
            previous = text;
            text = BraceAbbreviation.Replace(
                text,
                match =>
                    string.Join(
                        separator,
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
        var declarationFiles = new Dictionary<string, List<string>>(StringComparer.Ordinal);
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
            // 一覧の語や架空の Id が tests/ に実在するように見えてしまう）。作らない形の宣言のファイルも同じ理由で入れない
            // （受け持つ守りに書いた行 Id が宣言そのものに当たり、架空の Id が実在に見える）。宣言とファイル名の照合には入れる
            if (
                Path.GetFileName(path) != nameof(ClaudeMdReferenceTests) + ".cs"
                && !NotGeneratedGuard.IsDeclarationFileName(Path.GetFileName(path))
            )
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

                if (!declarationFiles.TryGetValue(match.Groups[1].Value, out var paths))
                {
                    paths = [];
                    declarationFiles[match.Groups[1].Value] = paths;
                }

                if (!paths.Contains(relative))
                {
                    paths.Add(relative);
                }
            }
        }

        return new TestSources(fileNames, declarations, string.Join('\n', all))
        {
            DeclarationFiles = declarationFiles,
        };
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
    internal static bool RowIdExists(string id, string source)
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

    /// <summary>
    /// 本文が書いた名前（地図の名前・テストの名前）と行 Id（行 Id の接頭辞が当たるもの）と、そのどれとしても読めないバッククォートの語を返す
    /// （作らない形の宣言の受け持つ守り〔<see cref="NotGeneratedShape.Guard"/>〕が、照合できる書き方だけで書かれていることを確かめるのに使う）
    /// </summary>
    /// <remarks>
    /// 拾い方はこのクラスの照合と同じ正規表現（<see cref="MapNameCandidate"/>・<see cref="TestName"/>〔中括弧の省略は展開する〕・
    /// <see cref="RowIdCandidate"/>）。バッククォートの語は、中身の全体がそのどれかに当たるときだけ読めるとみなす。
    /// </remarks>
    /// <param name="text">本文</param>
    /// <param name="rowIdPrefixes">行 Id の接頭辞の集合（<see cref="RowIdPrefixesOf"/>）</param>
    internal static (
        IReadOnlyList<string> Names,
        IReadOnlyList<string> RowIds,
        IReadOnlyList<string> Unreadable
    ) ReferencesIn(string text, IReadOnlySet<string> rowIdPrefixes)
    {
        bool IsRowId(string id) => rowIdPrefixes.Contains(LeadingSegment.Match(id).Value);

        bool IsWholeTestName(string word) =>
            ExpandBraces(word)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .All(name =>
                    TestName.Match(name) is { Success: true } match
                    && match.Index == 0
                    && match.Length == name.Length
                );

        bool IsWhole(Regex pattern, string quoted) =>
            pattern.Match(quoted) is { Success: true } match
            && match.Index == 0
            && match.Length == quoted.Length;

        var names = MapNameCandidate
            .Matches(text)
            .Select(match => match.Groups[1].Value)
            .Concat(
                TestName
                    .Matches(ExpandBraces(text))
                    .Select(match => match.Groups[1].Value + match.Groups[2].Value)
            )
            .ToList();
        var rowIds = RowIdCandidate
            .Matches(text)
            .Select(match => match.Groups[1].Value)
            .Where(IsRowId)
            .ToList();
        var unreadable = BacktickWord
            .Matches(text)
            .Select(match => match.Groups[1].Value)
            .Where(word =>
                !IsWhole(MapNameCandidate, "`" + word + "`")
                && !IsWholeTestName(word)
                && !(IsWhole(RowIdCandidate, "`" + word + "`") && IsRowId(word))
            )
            .ToList();

        return (names, rowIds, unreadable);
    }

    /// <summary>本文が書いた影響の地図の名前のうち、地図の守りの名前に無いものを返す</summary>
    /// <param name="text">CLAUDE.md の本文</param>
    /// <param name="names">地図の守りの名前（<see cref="ImpactMapSources.Protections"/> の名前）</param>
    internal static IReadOnlyList<string> FindMissingMapNames(
        string text,
        IReadOnlySet<string> names
    ) =>
        [
            .. MapNameCandidate
                .Matches(text)
                .Select(match => match.Groups[1].Value)
                .Where(name => !names.Contains(name))
                .ToHashSet(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];

    /// <summary>
    /// 本文が地図の名前かテストのクラスの名前に続けて書いた行 Id（直後の（…）の中と「の行 `a` / `b`」）のうち、その持ち主のテストのソースに
    /// 現れないもの（<c>名前: Id</c> の形）を返す
    /// </summary>
    /// <remarks>
    /// <para>
    /// 行 Id の拾い方は <see cref="FindMissingRowIds"/> と同じ（バッククォートの語で、最初のハイフンまでが行 Id の接頭辞の集合にあるもの）。
    /// 続きの読み方は地図の名前もクラスの名前も同じ <see cref="FollowingRows"/> で、地図の名前はバッククォートを含む一致の終わりから、
    /// クラスの名前（<see cref="FindMissingTestNames"/> と同じ拾い方・中括弧の省略は展開する）は直後の閉じのバッククォートを飛ばしてから読む。
    /// 中括弧の省略から出たクラスの名前の組（<c>Sync{Sqlite,Http}RuntimeTests</c>）に続く行 Id は、組のどれかが持てば合格にする。
    /// </para>
    /// <para>
    /// 持ち主かどうかは渡した関数が決める（本番は <see cref="ImpactMapSources.OwnsRowId"/>＝地図の名前はその守りのソース、クラスの名前は
    /// そのクラスを宣言するファイルを含む守りのソース〔どれかの守りが持てば合格〕・守りに属さなければ宣言するファイルの本文だけに
    /// <see cref="RowIdExists"/> を当てる。組み立てる Id はその本文の中で組み立てていればよい）。関数が null を返す名前
    /// （テストのソースを持たない規則・照合・文言の行・知らない名前）に続く行 Id は照合しない。
    /// </para>
    /// </remarks>
    /// <param name="text">CLAUDE.md の本文</param>
    /// <param name="ownsRowId">（地図の名前かテストのクラスの名前, 行 Id）→ その名前の行か（照合しない名前は null）</param>
    /// <param name="rowIdPrefixes">行 Id の接頭辞の集合（<see cref="RowIdPrefixesOf"/>）</param>
    internal static IReadOnlyList<string> FindForeignRowIds(
        string text,
        Func<string, string, bool?> ownsRowId,
        IReadOnlySet<string> rowIdPrefixes
    )
    {
        var foreign = new SortedSet<string>(StringComparer.Ordinal);

        void Check(IReadOnlyList<string> names, string body, int start)
        {
            foreach (Match id in RowIdCandidate.Matches(FollowingRows(body, start)))
            {
                var rowId = id.Groups[1].Value;

                if (!rowIdPrefixes.Contains(LeadingSegment.Match(rowId).Value))
                {
                    continue;
                }

                var owned = names.Select(name => ownsRowId(name, rowId)).ToList();

                if (owned.Contains(false) && !owned.Contains(true))
                {
                    foreign.Add($"{string.Join("・", names)}: {rowId}");
                }
            }
        }

        foreach (Match match in MapNameCandidate.Matches(text))
        {
            Check([match.Groups[1].Value], text, match.Index + match.Length);
        }

        var expanded = ExpandBraces(text, AbbreviationSeparator);
        var testNames = TestName
            .Matches(expanded)
            .Where(match => match.Groups[2].Value != ".cs")
            .ToList();
        var group = new List<string>();

        for (var index = 0; index < testNames.Count; index++)
        {
            var match = testNames[index];
            var end = match.Index + match.Length;
            group.Add(match.Groups[1].Value);

            // 同じ省略から出た名前（区切りの文字 1 つで次の名前に続く）は、最後の名前の続きをまとめて読む
            if (
                index + 1 < testNames.Count
                && testNames[index + 1].Index == end + 1
                && expanded[end] == AbbreviationSeparator
            )
            {
                continue;
            }

            Check(group, expanded, end < expanded.Length && expanded[end] == '`' ? end + 1 : end);
            group = [];
        }

        return [.. foreign];
    }

    /// <summary>
    /// 地図の名前・テストのクラスの名前 → 続けて書いた行 Id の持ち主か（<see cref="FindForeignRowIds"/> に渡す本番の関数）
    /// </summary>
    /// <remarks>
    /// 地図の名前はその守りのソース（<see cref="ImpactMapSources.OwnsRowId"/>）。テストのクラスの名前は、宣言するファイル（部分ファイルを
    /// 足したもの）をテストのソースに含む守りのどれかが <see cref="ImpactMapSources.OwnsRowId"/> で持てば true（守りのソースの和集合には
    /// 当てない＝守りをまたいだ偶然の組み立てを通さない）。どの守りにも属さないクラスは、宣言するファイル（部分ファイルを足したもの）の
    /// 本文に <see cref="RowIdExists"/> を当てる。テストのソースを持たない守り・宣言の無い名前は null。
    /// </remarks>
    /// <param name="protections">影響の地図の守り（<see cref="ImpactMapSources.Protections"/>）</param>
    /// <param name="sources">tests/ のソース</param>
    internal static Func<string, string, bool?> FollowingRowOwner(
        IReadOnlyList<Protection> protections,
        TestSources sources
    )
    {
        var withSources = protections
            .Where(protection => protection.SourceFiles.Count > 0)
            .ToDictionary(protection => protection.Name, StringComparer.Ordinal);
        var protectionFiles = withSources.Values.ToDictionary(
            protection => protection.Name,
            protection => ImpactMapSources.ExpandPartials(Root, protection.SourceFiles).ToHashSet(),
            StringComparer.Ordinal
        );
        var sourceTexts = new Dictionary<string, string>(StringComparer.Ordinal);
        var classOwners = new Dictionary<string, (List<Protection> Owners, string? Text)>(
            StringComparer.Ordinal
        );

        return (name, rowId) =>
        {
            if (withSources.TryGetValue(name, out var protection))
            {
                return ImpactMapSources.OwnsRowId(protection, Root, rowId, sourceTexts);
            }

            if (!sources.DeclarationFiles.TryGetValue(name, out var declared))
            {
                return null;
            }

            if (!classOwners.TryGetValue(name, out var entry))
            {
                var files = ImpactMapSources.ExpandPartials(Root, declared);
                var owners = withSources
                    .Values.Where(candidate => files.Any(protectionFiles[candidate.Name].Contains))
                    .ToList();
                entry = (
                    owners,
                    owners.Count > 0
                        ? null
                        : string.Join(
                            '\n',
                            files.Select(path => File.ReadAllText(Path.Combine(Root, path)))
                        )
                );
                classOwners[name] = entry;
            }

            return entry.Text is not null
                ? RowIdExists(rowId, entry.Text)
                : entry.Owners.Any(owner =>
                    ImpactMapSources.OwnsRowId(owner, Root, rowId, sourceTexts)
                );
        };
    }

    /// <summary>名前の直後の（…）の中身（入れ子の全角の括弧を数える）か「の行 `a` / `b`」。どちらも無ければ空</summary>
    private static string FollowingRows(string text, int start)
    {
        if (start < text.Length && text[start] == '（')
        {
            var depth = 0;

            for (var index = start; index < text.Length; index++)
            {
                depth += text[index] switch
                {
                    '（' => 1,
                    '）' => -1,
                    _ => 0,
                };

                if (depth == 0)
                {
                    return text[(start + 1)..index];
                }
            }

            return text[(start + 1)..];
        }

        var list = FollowingRowList.Match(text, start);

        return list.Success ? list.Value : "";
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

    /// <summary>
    /// 全 CLAUDE.md が書いた影響の地図の名前が地図に実在し、地図の名前とテストのクラスの名前に続けて書いた行 Id がその守り
    /// （クラスはそれが属する守り・守りに属さなければ宣言するファイル）のテストのソースにあること
    /// </summary>
    [Fact(
        DisplayName = "CLAUDE.md 群が書いた影響の地図の名前は地図に実在し、地図の名前とテストのクラスの名前に続けて書いた行 Id はその守りのテストのソースにある"
    )]
    public void MapNames_ExistInImpactMap()
    {
        var files = ClaudeMdFiles();
        var protections = ImpactMapSources.Protections(Root);
        var names = protections
            .Select(protection => protection.Name)
            .ToHashSet(StringComparer.Ordinal);
        var ownsRowId = FollowingRowOwner(protections, Sources.Value);

        files
            .Values.SelectMany(text => MapNameCandidate.Matches(text))
            .Should()
            .NotBeEmpty("CLAUDE.md 群が地図の名前を書いていて、拾う形が空振りしていないこと");

        var problems = files
            .SelectMany(file =>
                FindMissingMapNames(file.Value, names)
                    .Select(name => $"{file.Key}: 地図に無い名前 {name}")
                    .Concat(
                        FindForeignRowIds(file.Value, ownsRowId, Sources.Value.RowIdPrefixes)
                            .Select(entry => $"{file.Key}: 守りのソースに無い行 {entry}")
                    )
            )
            .ToList();

        problems
            .Should()
            .BeEmpty(
                "CLAUDE.md に書いた地図の名前が地図に無いか、地図の名前・テストのクラスの名前に続けて書いた行 Id がその守りのテストのソースに無い"
                    + "（地図の名前は tests/QuickER.Tests/TestSupport/impact-map.md で引く。改名に追従するか、行の持ち主の守り・クラスへ書き直すこと。"
                    + "組み立てる Id は、守りのソースに接頭辞まで読める文字列を置く）:"
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
                    + "`RealGuardTests.Existing_*`・`RealGuardTests.cs`・`NoSuchFileTests.cs`・*AnyPatternTests・"
                    + "`ResetForTests`",
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

        var mapNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "Dialect/DdlText",
            "Dialect/Types",
            "rule:real-rule",
            "table:RealTable",
        };

        FindMissingMapNames(
                "`Dialect/DdlText`・`Dialect/NoSuchPart`・`rule:real-rule`・`rule:no-such-rule`・"
                    + "`table:RealTable`・`table:NoSuchTable`・`english:no-such-row`・Dialect/NotQuoted・"
                    + "`CodegenConfig/Judges/`・`CodegenConfig/Judges/CompileJudge.cs`・`GeneratedRuntime/`・"
                    + "`rule:{Id}`・`table:{名前}`・`Gui/Services`・`Dialect/DdlText の文`",
                mapNames
            )
            .Should()
            .BeEquivalentTo(
                [
                    "Dialect/NoSuchPart",
                    "english:no-such-row",
                    "rule:no-such-rule",
                    "table:NoSuchTable",
                ],
                "地図の名前の形（バッククォートの全体・エンジンの ID・前置き）だけを拾い、地図に無い名前を名指しする"
            );

        // 地図の名前・テストのクラスの名前 → 行 Id の持ち主を決める本文（本番は守りのソース・クラスが属する守りのソース）
        var ownerSources = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Dialect/DdlText"] = "\"ddl-real-row\" $\"ddl-built-row-{x}\"",
            ["Dialect/Types"] = "\"type-own-row\" \"type-second-row\" \"type-third-row\"",
            ["RealGuardTests"] = "\"guard-own-row\" $\"guard-built-row-{x}\"",
            ["RealAlphaTests"] = "\"guard-alpha-row\"",
            ["RealBetaTests"] = "\"guard-beta-row\"",
        };

        FindForeignRowIds(
                "`Dialect/DdlText`（行 `ddl-real-row`・`ddl-built-row-with-default`・`type-own-row`）・"
                    + "`Dialect/Types` の行 `type-own-row` / `ddl-real-row`・"
                    + "`Dialect/DdlText`（全行に掛かる照合（行の説明））・`Dialect/Types`・`ddl-real-row`・"
                    + "`rule:real-rule`（行 `ddl-real-row`）・`Dialect/DdlText` の形の行 `type-second-row`。"
                    + "`RealGuardTests`（行 `guard-own-row`・`guard-built-row-with-default`・`ddl-real-row`）・"
                    + "`RealGuardTests.Existing_Method` の行 `type-own-row`・"
                    + "`RealGuardTests` の規則 `guard-own-row` / `type-second-row`・"
                    + "`Real{Alpha,Beta}Tests`（行 `guard-alpha-row`・`guard-beta-row`・`ddl-built-row-x`）・"
                    + "`RealGuardTests`・`type-third-row`・`RealGuardTests` の閉包・同期計画の行 `type-third-row`・"
                    + "`NoSuchGuardTests`（行 `ddl-real-row`）・`ResetForTests`（行 `ddl-real-row`）",
                (name, rowId) =>
                    ownerSources.TryGetValue(name, out var source)
                        ? RowIdExists(rowId, source)
                        : null,
                new HashSet<string>(StringComparer.Ordinal) { "ddl", "type", "guard" }
            )
            .Should()
            .BeEquivalentTo(
                [
                    "Dialect/DdlText: type-own-row",
                    "Dialect/Types: ddl-real-row",
                    "Dialect/DdlText: type-second-row",
                    "RealGuardTests: ddl-real-row",
                    "RealGuardTests: type-own-row",
                    "RealGuardTests: type-second-row",
                    "RealAlphaTests・RealBetaTests: ddl-built-row-x",
                ],
                "地図の名前・テストのクラスの名前に続けて書いた行が、その持ち主の本文に無く別の本文にだけあれば名指しし"
                    + "（「の形の行」「の規則」のような修飾つきの続きも読む・中括弧の省略の組はどれかが持てば合格）、"
                    + "組み立てる Id・続けて書いていない行・修飾に区切りのある続き・ソースを持たない守り・知らないクラス・"
                    + "…ForTests のメンバーは名指ししない"
            );
    }
}
