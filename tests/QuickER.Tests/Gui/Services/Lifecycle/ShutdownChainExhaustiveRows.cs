using AwesomeAssertions;
using QuickER.Gui.Abstractions;
using QuickER.Tests.TestDoubles;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Gui.Services.Lifecycle;

/// <summary>終了の連鎖の段・記録先の振る舞い</summary>
internal enum ChainBehavior
{
    /// <summary>成功する</summary>
    Ok,

    /// <summary>投げる</summary>
    Throws,
}

/// <summary>終了の連鎖の総当たりの 1 組</summary>
/// <param name="Steps">4 段それぞれの振る舞い</param>
/// <param name="Reporter">記録先の振る舞い</param>
internal sealed record ShutdownChainExhaustiveCase(
    IReadOnlyList<ChainBehavior> Steps,
    ChainBehavior Reporter
);

/// <summary>
/// 終了の連鎖の総当たり（QuickER.Gui.Abstractions の <c>ShutdownSteps.Run</c>）：4 段 × 記録先の全 32 組を決定的に列挙する。
/// 4 段すべてが順に実行される・例外を外へ出さない・記録先が成功なら記録は投げた段の段名の列（実行の順）で、
/// 記録された例外はその段が投げた例外そのもの、をテスト側のオラクルと照合する
/// </summary>
internal static class ShutdownChainExhaustiveRows
{
    private static readonly string[] StepNames =
    [
        "Interrupt",
        "SaveSettings",
        "ShutdownEngines",
        "Close",
    ];

    /// <summary>表（全組）</summary>
    public static ScenarioTable<ShutdownChainExhaustiveCase> Table =>
        new(
            EngineIds.OsBoundary,
            from mask in Enumerable.Range(0, 16)
            from reporter in Enum.GetValues<ChainBehavior>()
            let steps = Enumerable
                .Range(0, 4)
                .Select(i => (mask >> i & 1) == 1 ? ChainBehavior.Throws : ChainBehavior.Ok)
                .ToList()
            select new ScenarioRow<ShutdownChainExhaustiveCase>(
                (
                    "shutdown-chain-"
                    + string.Join("-", steps.Select((s, i) => $"s{i + 1}{s}"))
                    + $"-reporter{reporter}"
                ).ToLowerInvariant(),
                null,
                $"終了の連鎖: 段={string.Join("・", steps)}・記録先={reporter}",
                new ShutdownChainExhaustiveCase(steps, reporter)
            )
        );

    /// <summary>1 組を流してオラクルと照合する。常に true</summary>
    public static bool Run(ShutdownChainExhaustiveCase c)
    {
        var recording = new RecordingShutdownFailureReporter();
        IShutdownFailureReporter reporter =
            c.Reporter == ChainBehavior.Ok ? recording : new ThrowingReporter();
        var executed = new List<string>();
        var thrown = new List<(string Step, Exception Exception)>();

        var act = () =>
        {
            for (var i = 0; i < StepNames.Length; i++)
            {
                var name = StepNames[i];
                var throws = c.Steps[i] == ChainBehavior.Throws;

                ShutdownSteps.Run(
                    reporter,
                    name,
                    () =>
                    {
                        executed.Add(name);

                        if (throws)
                        {
                            var failure = new InvalidOperationException(name);
                            thrown.Add((name, failure));
                            throw failure;
                        }
                    }
                );
            }
        };

        act.Should().NotThrow("終了の連鎖は例外を外へ出さない");
        executed.Should().Equal(StepNames, "4 段すべてが順に実行される");

        if (c.Reporter == ChainBehavior.Ok)
        {
            var expectedSteps = StepNames.Where((_, i) => c.Steps[i] == ChainBehavior.Throws);

            recording.Steps.Should().Equal(expectedSteps);

            for (var i = 0; i < thrown.Count; i++)
            {
                recording.Reports[i].Exception.Should().BeSameAs(thrown[i].Exception);
            }
        }

        return true;
    }

    /// <summary>契約違反（記録中に投げる）を再現する記録先</summary>
    private sealed class ThrowingReporter : IShutdownFailureReporter
    {
        public void Report(string step, Exception exception) =>
            throw new InvalidOperationException("記録にも失敗した");
    }
}
