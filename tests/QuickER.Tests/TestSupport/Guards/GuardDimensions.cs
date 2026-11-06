using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.TestSupport.Guards;

/// <summary>ソースの規則の表の次元表・網羅しない組の一覧・台帳の行</summary>
/// <remarks>
/// 網羅は自己検査の行（<see cref="SourceRuleProbes"/>）で数える：読み方 × 照合の形 × 拾うべき出現の置き場（普通の場所か、
/// 条件コンパイルの外れる分岐か）の 3 つの組ごとに、規則を合成のソースへ当てて拾うことを確かめた行がある。
/// 製品の仕様の軸は持たない（因子の棚卸しの対象なし）。
/// </remarks>
internal static class GuardDimensions
{
    /// <summary>網羅しない組の照合の範囲</summary>
    public const string Scope = "guard-source-rules";

    /// <summary>読み方</summary>
    public const string Reader = "Reader";

    /// <summary>照合の形</summary>
    public const string Expectation = "Expectation";

    /// <summary>拾うべき出現の置き場</summary>
    public const string Branch = "Branch";

    /// <summary>普通の場所</summary>
    public const string Plain = "Plain";

    /// <summary>条件コンパイルの外れる分岐</summary>
    public const string InactiveBranch = "InactiveBranch";

    /// <summary>次元表</summary>
    public static DimensionTable Table { get; } =
        new(
            EngineIds.Guard,
            [
                new Factor(
                    Reader,
                    [
                        .. Enum.GetValues<GuardReader>()
                            .Select(reader => new Level(reader.ToString())),
                    ],
                    "読み方"
                ),
                new Factor(
                    Expectation,
                    [
                        .. Enum.GetValues<GuardExpectation>()
                            .Select(expectation => new Level(expectation.ToString())),
                    ],
                    "照合の形"
                ),
                new Factor(
                    Branch,
                    [new Level(Plain), new Level(InactiveBranch)],
                    "拾うべき出現の置き場"
                ),
            ],
            [new FactorGroup(Reader, Expectation, Branch)],
            IsValid
        );

    /// <summary>網羅しない組の一覧</summary>
    /// <remarks>
    /// 構文の節・トークン・XML で「あるべき出現」を確かめる規則は表に無い（必須の確認は、元のガードがどれも文字列かシンボルで
    /// 書いている）。規則が無い組は自己検査の行を作れないので、直す区切り（P4）つきで残す（必須の規則を足せば埋まる＝表の今の状態で、受け入れではない）。その読み方の必須の規則を足したら、
    /// 自己検査の行（普通の場所と外れる分岐）も足して、ここから消す。
    /// </remarks>
    public static CoverageGapSet Gaps =>
        CoverageGap.Set(
            EngineIds.Guard,
            [
                .. NoRequiredRule(GuardReader.SyntaxNodes),
                .. NoRequiredRule(GuardReader.Tokens),
                .. NoRequiredRule(GuardReader.Xml),
            ]
        );

    /// <summary>台帳の行</summary>
    public static EngineDescriptor Descriptor =>
        new(
            EngineIds.Guard,
            "SourceRules",
            "ソースの規則の表（読み方 × 照合の形 × 拾うべき出現の置き場。src と tests のソースを走査するガード）",
            Table,
            SourceRules.All.Count + SourceRuleProbes.All.Count,
            [
                "tests/QuickER.Tests/TestSupport/Guards/Finders/CultureDependentText.cs",
                "tests/QuickER.Tests/TestSupport/Guards/Finders/FileWriteCalls.cs",
                "tests/QuickER.Tests/TestSupport/Guards/Finders/PathPrefixCalls.cs",
                "tests/QuickER.Tests/TestSupport/Guards/Finders/ProcessLaunchCalls.cs",
                "tests/QuickER.Tests/TestSupport/Guards/GuardDimensions.cs",
                "tests/QuickER.Tests/TestSupport/Guards/GuardScan.cs",
                "tests/QuickER.Tests/TestSupport/Guards/SourceRule.cs",
                "tests/QuickER.Tests/TestSupport/Guards/SourceRuleProbe.cs",
                "tests/QuickER.Tests/TestSupport/Guards/SourceRuleProbes.Gui.cs",
                "tests/QuickER.Tests/TestSupport/Guards/SourceRuleProbes.Mcp.cs",
                "tests/QuickER.Tests/TestSupport/Guards/SourceRuleProbes.Names.cs",
                "tests/QuickER.Tests/TestSupport/Guards/SourceRuleProbes.Platform.cs",
                "tests/QuickER.Tests/TestSupport/Guards/SourceRuleProbes.Provider.cs",
                "tests/QuickER.Tests/TestSupport/Guards/SourceRuleProbes.Tests.cs",
                "tests/QuickER.Tests/TestSupport/Guards/SourceRuleProbes.Text.cs",
                "tests/QuickER.Tests/TestSupport/Guards/SourceRuleProbes.cs",
                "tests/QuickER.Tests/TestSupport/Guards/SourceRuleTests.cs",
                "tests/QuickER.Tests/TestSupport/Guards/SourceRules.Gui.cs",
                "tests/QuickER.Tests/TestSupport/Guards/SourceRules.Mcp.cs",
                "tests/QuickER.Tests/TestSupport/Guards/SourceRules.Names.cs",
                "tests/QuickER.Tests/TestSupport/Guards/SourceRules.Platform.cs",
                "tests/QuickER.Tests/TestSupport/Guards/SourceRules.Provider.cs",
                "tests/QuickER.Tests/TestSupport/Guards/SourceRules.Tests.cs",
                "tests/QuickER.Tests/TestSupport/Guards/SourceRules.Text.cs",
                "tests/QuickER.Tests/TestSupport/Guards/SourceRules.cs",
                "tests/QuickER.Tests/TestSupport/Guards/SourceScope.cs",
            ],
            ["src", "tests"]
        );

    /// <summary>自己検査の行 1 つを数える</summary>
    public static void Observe(CoverageCounter counter, SourceRuleProbe probe)
    {
        var rule = SourceRules.ById(probe.RuleId);
        counter.Observe(
            new Dictionary<string, string>
            {
                [Reader] = rule.Finder.Reader.ToString(),
                [Expectation] = rule.Expectation.ToString(),
                [Branch] = probe.InactiveBranch ? InactiveBranch : Plain,
            }
        );
    }

    /// <summary>起こり得ない組（原理的に作れないものだけ）</summary>
    private static bool IsValid(IReadOnlyDictionary<string, string> assignment) =>
        // XML（XAML・csproj）には条件コンパイルの分岐が無い
        !(
            assignment.TryGetValue(Reader, out var reader)
            && reader == nameof(GuardReader.Xml)
            && assignment.TryGetValue(Branch, out var branch)
            && branch == InactiveBranch
        );

    /// <summary>必須の規則が無い読み方の組（読み方と必須の組と、それを含む置き場の 3 つの組）</summary>
    private static IEnumerable<CoverageGapEntry> NoRequiredRule(GuardReader reader)
    {
        var reason =
            $"読み方 {reader} で「あるべき出現」を確かめる規則が表に無い（必須の確認はどのガードも文字列かシンボルで書いている）。"
            + "規則が無いので自己検査の行を作れない（その読み方の必須の規則を持つガードがまだ無い）";
        (string Factor, string Level) readerLevel = (Reader, reader.ToString());
        (string Factor, string Level) required = (Expectation, nameof(GuardExpectation.Required));

        yield return CoverageGap.Tuple(Scope, "P3-12", "P4", reason, readerLevel, required);

        foreach (
            var branch in reader == GuardReader.Xml ? [Plain] : new[] { Plain, InactiveBranch }
        )
        {
            yield return CoverageGap.Tuple(
                Scope,
                "P3-12",
                "P4",
                reason,
                readerLevel,
                required,
                (Branch, branch)
            );
        }
    }
}
