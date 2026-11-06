using System.Collections.Concurrent;
using System.IO;
using Microsoft.CodeAnalysis;

namespace QuickER.Tests.TestSupport.Guards;

/// <summary>規則が読むファイル 1 つ（本文と、条件コンパイルのどの分岐も読む構文木。構文木は初めて要るときに 1 回だけ作る）</summary>
internal sealed class GuardFile
{
    private readonly Lazy<IReadOnlyList<SyntaxTree>> _variants;
    private readonly Lazy<IReadOnlyList<SyntaxNode>> _nodes;
    private readonly Lazy<IReadOnlyList<SyntaxToken>> _tokens;

    /// <summary>ファイルを作る</summary>
    /// <param name="relativePath">リポジトリのルートからの相対パス（区切りは <c>/</c>）</param>
    /// <param name="text">本文</param>
    public GuardFile(string relativePath, string text)
    {
        RelativePath = relativePath;
        Text = text;
        _variants = new(() => ConditionalSourceParse.Parse(text, path: relativePath));
        _nodes = new(() => [.. ConditionalSourceParse.DescendantNodes(_variants.Value)]);
        _tokens = new(() => [.. ConditionalSourceParse.DescendantTokens(_variants.Value)]);
    }

    /// <summary>リポジトリのルートからの相対パス（区切りは <c>/</c>）</summary>
    public string RelativePath { get; }

    /// <summary>本文</summary>
    public string Text { get; }

    /// <summary>条件コンパイルの記号のすべての組み合わせの構文木（<see cref="ConditionalSourceParse.Parse"/>）</summary>
    public IReadOnlyList<SyntaxTree> Variants => _variants.Value;

    /// <summary>全組み合わせの構文木の節（組み合わせをまたいで同じ節は 1 回だけ）</summary>
    public IReadOnlyList<SyntaxNode> Nodes => _nodes.Value;

    /// <summary>全組み合わせの構文木のトークン（組み合わせをまたいで同じトークンは 1 回だけ）</summary>
    public IReadOnlyList<SyntaxToken> Tokens => _tokens.Value;

    /// <summary>節の行（1 始まり）</summary>
    public static int LineOf(SyntaxNode node) =>
        node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

    /// <summary>トークンの行（1 始まり）</summary>
    public static int LineOf(SyntaxToken token) =>
        token.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

    /// <summary>本文の位置の行（1 始まり・LF で数える）</summary>
    public int LineAt(int index)
    {
        var line = 1;

        for (var i = 0; i < index && i < Text.Length; i++)
        {
            if (Text[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }
}

/// <summary>規則を当てた結果（範囲の出現・範囲のファイルの数・空振りの検知の範囲の出現）</summary>
/// <param name="Hits">範囲の出現</param>
/// <param name="FileCount">範囲に入ったファイルの数</param>
/// <param name="WitnessHits">空振りの検知の範囲の出現</param>
/// <param name="FloorCount">空振りの検知の候補の数（下限を持たない規則は 0）</param>
internal sealed record GuardOutcome(
    IReadOnlyList<GuardHit> Hits,
    int FileCount,
    IReadOnlyList<GuardHit> WitnessHits,
    int FloorCount
);

/// <summary>
/// 規則の表を 1 回の走査で当てる（同じファイルを規則の数だけ読み直さない・構文木を作り直さない）
/// </summary>
/// <remarks>
/// ファイルで読む規則（構文・トークン・文字列・XML）は、すべての規則の範囲に入るファイルを 1 回ずつ読み、そのファイルに当たる
/// 規則を全部当てる。構文木はファイルごとに 1 回だけ作り、そのファイルを当て終えたら捨てる（覚えるのは出現だけ）。
/// シンボルの規則は <see cref="SourceSymbolScan.SrcProjects"/> の各プロジェクトを 1 回ずつ <see cref="SourceProject.ScanNodes"/> で
/// たどり、当たる規則を全部当てる。結果は規則の表ごとに 1 回だけ作る。
/// </remarks>
internal static class GuardScan
{
    private static readonly Lazy<IReadOnlyDictionary<string, GuardOutcome>> FileOutcomes = new(() =>
        ScanFiles([.. SourceRules.All.Where(rule => rule.Finder is not SymbolFinder)])
    );

    private static readonly Lazy<IReadOnlyDictionary<string, GuardOutcome>> SymbolOutcomes = new(
        () =>
            ScanSymbols([.. SourceRules.All.Where(rule => rule.Finder is SymbolFinder)])
    );

    /// <summary>規則の表の規則を当てた結果（初回に表のすべての規則を 1 回の走査で当てる）</summary>
    public static GuardOutcome For(SourceRule rule) =>
        (rule.Finder is SymbolFinder ? SymbolOutcomes : FileOutcomes).Value[rule.Id];

    /// <summary>ファイルで読む規則を、範囲に入るファイルへ 1 回の走査で当てる</summary>
    public static IReadOnlyDictionary<string, GuardOutcome> ScanFiles(
        IReadOnlyList<SourceRule> rules
    )
    {
        var targets = Targets(rules);
        var files = targets
            .Select(target => (target.Scope.Root, target.Scope.Extension))
            .Distinct()
            .SelectMany(root => SourceFiles.Enumerate(root.Root, "*" + root.Extension))
            .DistinctBy(entry => entry.RelativePath, StringComparer.Ordinal)
            .ToList();
        var collector = new Collector(rules);

        Parallel.ForEach(
            files,
            entry =>
            {
                var applicable = targets
                    .Where(target => target.Scope.Contains(entry.RelativePath))
                    .ToList();

                if (applicable.Count == 0)
                {
                    return;
                }

                var file = new GuardFile(entry.RelativePath, File.ReadAllText(entry.FullPath));

                foreach (var target in applicable)
                {
                    collector.Count(target);

                    foreach (var finding in Find(target.Finder, file))
                    {
                        collector.Add(
                            target,
                            new GuardHit(file.RelativePath, finding.What, finding.Line)
                        );
                    }
                }
            }
        );

        return collector.Outcomes();
    }

    /// <summary>シンボルの規則を、src のプロジェクトへ 1 回の走査で当てる</summary>
    public static IReadOnlyDictionary<string, GuardOutcome> ScanSymbols(
        IReadOnlyList<SourceRule> rules
    )
    {
        var targets = Targets(rules);
        var collector = new Collector(rules);

        Parallel.ForEach(
            SourceSymbolScan.SrcProjects,
            project =>
            {
                var byFile = project
                    .Files.Select(file =>
                        (
                            file.RelativePath,
                            Targets: targets
                                .Where(target => target.Scope.Contains(file.RelativePath))
                                .ToList()
                        )
                    )
                    .Where(file => file.Targets.Count > 0)
                    .ToDictionary(
                        file => file.RelativePath,
                        file => file.Targets,
                        StringComparer.Ordinal
                    );

                if (byFile.Count == 0)
                {
                    return;
                }

                foreach (var target in byFile.Values.SelectMany(list => list))
                {
                    collector.Count(target);
                }

                foreach (var item in project.ScanNodes())
                {
                    if (!byFile.TryGetValue(item.File.RelativePath, out var applicable))
                    {
                        continue;
                    }

                    foreach (var target in applicable)
                    {
                        if (
                            ((SymbolFinder)target.Finder).Describe(item.Model, item.Node) is
                            { } what
                        )
                        {
                            collector.Add(
                                target,
                                new GuardHit(
                                    item.File.RelativePath,
                                    what,
                                    GuardFile.LineOf(item.Node)
                                )
                            );
                        }
                    }
                }
            }
        );

        return collector.Outcomes();
    }

    /// <summary>ファイルで読む見つける形をファイル 1 つに当てる</summary>
    public static IEnumerable<GuardFinding> Find(GuardFinder finder, GuardFile file) =>
        finder switch
        {
            NodeFinder nodes => file
                .Nodes.Select(node => (Node: node, What: nodes.Describe(node)))
                .Where(item => item.What is not null)
                .Select(item => new GuardFinding(item.What!, GuardFile.LineOf(item.Node))),
            TokenFinder tokens => file
                .Tokens.Select(token => (Token: token, What: tokens.Describe(token)))
                .Where(item => item.What is not null)
                .Select(item => new GuardFinding(item.What!, GuardFile.LineOf(item.Token))),
            FileFinder whole => whole.Find(file),
            _ => throw new ArgumentException(
                $"ファイルで読めない見つける形: {finder.GetType().Name}",
                nameof(finder)
            ),
        };

    /// <summary>規則の範囲・空振りの検知の範囲・候補の数の下限（走査で当てる単位）</summary>
    private static List<Target> Targets(IReadOnlyList<SourceRule> rules) =>
        [
            .. rules.Select(rule => new Target(rule, rule.Scope, rule.Finder, TargetKind.Main)),
            .. rules
                .Where(rule => rule.Witness is not null)
                .Select(rule => new Target(
                    rule,
                    rule.Witness!.Scope,
                    rule.Finder,
                    TargetKind.Witness
                )),
            .. rules
                .Where(rule => rule.Floor is not null)
                .Select(rule => new Target(
                    rule,
                    rule.Scope,
                    rule.Floor!.Candidates,
                    TargetKind.Floor
                )),
        ];

    /// <summary>走査で当てる単位の種類</summary>
    private enum TargetKind
    {
        /// <summary>規則の範囲に規則の見つける形を当てる</summary>
        Main,

        /// <summary>空振りの検知の範囲に規則の見つける形を当てる</summary>
        Witness,

        /// <summary>規則の範囲に候補を数える見つける形を当てる</summary>
        Floor,
    }

    /// <summary>走査で当てる単位（規則・範囲・見つける形・種類）</summary>
    private sealed record Target(
        SourceRule Rule,
        SourceScope Scope,
        GuardFinder Finder,
        TargetKind Kind
    );

    /// <summary>並列の走査の出現とファイルの数を集める</summary>
    private sealed class Collector(IReadOnlyList<SourceRule> rules)
    {
        private readonly ConcurrentBag<(string Id, bool Witness, GuardHit Hit)> _hits = [];
        private readonly ConcurrentDictionary<string, int> _counts = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, int> _floors = new(StringComparer.Ordinal);

        public void Count(Target target)
        {
            if (target.Kind == TargetKind.Main)
            {
                _counts.AddOrUpdate(target.Rule.Id, 1, (_, count) => count + 1);
            }
        }

        public void Add(Target target, GuardHit hit)
        {
            if (target.Kind == TargetKind.Floor)
            {
                _floors.AddOrUpdate(target.Rule.Id, 1, (_, count) => count + 1);
                return;
            }

            _hits.Add((target.Rule.Id, target.Kind == TargetKind.Witness, hit));
        }

        public IReadOnlyDictionary<string, GuardOutcome> Outcomes()
        {
            var byRule = _hits.ToLookup(hit => hit.Id, StringComparer.Ordinal);

            return rules.ToDictionary(
                rule => rule.Id,
                rule => new GuardOutcome(
                    Ordered(byRule[rule.Id].Where(hit => !hit.Witness)),
                    _counts.GetValueOrDefault(rule.Id),
                    Ordered(byRule[rule.Id].Where(hit => hit.Witness)),
                    _floors.GetValueOrDefault(rule.Id)
                ),
                StringComparer.Ordinal
            );
        }

        private static List<GuardHit> Ordered(
            IEnumerable<(string Id, bool Witness, GuardHit Hit)> hits
        ) =>
            [
                .. hits.Select(hit => hit.Hit)
                    .OrderBy(hit => hit.File, StringComparer.Ordinal)
                    .ThenBy(hit => hit.Line)
                    .ThenBy(hit => hit.What, StringComparer.Ordinal),
            ];
    }
}
