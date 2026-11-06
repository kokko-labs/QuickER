using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Gui.Services.Lifecycle;

/// <summary>終了の連鎖の総当たりの次元表・網羅しない組の一覧・台帳の行</summary>
/// <remarks>全組を列挙するので原理的に起こり得ない組は無く、網羅しない組も無い</remarks>
internal static class ShutdownChainExhaustiveDimensions
{
    /// <summary>網羅しない組の照合の範囲</summary>
    public const string Scope = "os-boundary-shutdown-chain-exhaustive";

    private static readonly string[] Names = ["Step1", "Step2", "Step3", "Step4", "Reporter"];

    /// <summary>次元表</summary>
    public static DimensionTable Table { get; } =
        new(
            EngineIds.OsBoundary,
            Names
                .Select(name => new Factor(
                    name,
                    Enum.GetValues<ChainBehavior>().Select(v => new Level(v.ToString())).ToList(),
                    name == "Reporter" ? "記録先の振る舞い" : "連鎖の段の振る舞い"
                ))
                .ToList()
        );

    /// <summary>網羅しない組の一覧（無い）</summary>
    public static CoverageGapSet Gaps => CoverageGap.Set(EngineIds.OsBoundary, []);

    /// <summary>台帳の行</summary>
    public static EngineDescriptor Descriptor =>
        new(
            EngineIds.OsBoundary,
            "ShutdownChainExhaustive",
            "終了の連鎖の総当たり（4 段 × 記録先・全 32 組）",
            Table,
            ShutdownChainExhaustiveRows.Table.Count,
            [
                "tests/QuickER.Tests/Gui/Services/Lifecycle/ShutdownChainExhaustiveDimensions.cs",
                "tests/QuickER.Tests/Gui/Services/Lifecycle/ShutdownChainExhaustiveRows.cs",
                "tests/QuickER.Tests/Gui/Services/Lifecycle/ShutdownChainExhaustiveTests.cs",
            ],
            ["src/QuickER.Gui.Abstractions/ShutdownSteps.cs"]
        );

    /// <summary>1 組を数える</summary>
    public static void Observe(CoverageCounter counter, ShutdownChainExhaustiveCase c)
    {
        var assignment = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var i = 0; i < 4; i++)
        {
            assignment[Names[i]] = c.Steps[i].ToString();
        }

        assignment["Reporter"] = c.Reporter.ToString();
        counter.Observe(assignment);
    }
}
