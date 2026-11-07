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

    /// <summary>網羅しない組の一覧（読み方 × 照合の形 × 置き場の、起こり得る組の全部を自己検査の行が持つ）</summary>
    /// <remarks>
    /// 構文の節・トークンで読む必須の規則は、単一インスタンスの配線（program-single-instance-uses＝節）と UI 例外の受け皿
    /// （app-ui-crash-handler-uses＝トークンの並び）。どちらかを文字列の読み方へ戻すと、その読み方の組が欠けて赤になる。
    /// </remarks>
    public static CoverageGapSet Gaps => CoverageGap.Set(EngineIds.Guard, []);

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
}
