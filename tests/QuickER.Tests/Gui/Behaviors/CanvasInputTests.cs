using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using AwesomeAssertions;
using QuickER.Behaviors;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.TestSupport.WpfApplicationTestSupport;

namespace QuickER.Tests.Gui.Behaviors;

/// <summary>キャンバスの入力の列の状態機械のエンジン（本物の MainWindow へキー・マウスの手の列を流す）</summary>
/// <remarks>
/// <para>
/// 手の送り方・流さないものはハーネス（<see cref="CanvasInputHarness"/>）、仕様のモデルは <see cref="CanvasInputModel"/>。
/// 窓を 1 つ作り、その中で行・種を順に流す（種ごとに図を差し替え、前の種の残りを表明する）。
/// </para>
/// <para>
/// 判断がアクティブの行き来とフォーカスに左右され、ビヘイビアの進行中の状態（スレッドごとの静的な状態）を触るので、
/// 並列に走らせない（<see cref="WindowActivationTestCollection"/>）。
/// </para>
/// </remarks>
[Collection(WindowActivationTestCollection.Name)]
public sealed class CanvasInputTests(ITestOutputHelper output)
{
    /// <summary>決まった形の行を流す</summary>
    [Fact(DisplayName = "入力の列: 決まった形の行は不変条件を破らない")]
    public void Rows_Hold()
    {
        var failures = new List<EngineFailure>();
        var ids = CanvasInputRows.Table.Select(row => row.Id).ToHashSet(StringComparer.Ordinal);
        var exercised = new HashSet<string>(StringComparer.Ordinal);

        RunInIsolatedWindow(
            (vm, window) =>
            {
                using var harness = new CanvasInputHarness(vm, window);
                exercised = harness.Exercised;

                foreach (var row in CanvasInputRows.Table)
                {
                    if (harness.Run(row.Input) is { } violation)
                    {
                        failures.Add(
                            new EngineFailure(
                                row.Id,
                                violation.Signature,
                                $"{violation} | 手の列: {string.Join(" ", row.Input)}"
                            )
                        );
                    }
                }
            }
        );

        CanvasInputDimensions
            .KnownFailures.Classify(failures, ids.Contains)
            .AssertClean("入力の列の決まった形の行");

        // 行が狙った表明の枝を空振りしていない（落とし込み・取り消し・ホバーなどの表明が実際に走った）
        CoverageGuard.ContainsAll(
            "決まった形の行で走った表明の枝",
            exercised,
            CanvasInputHarness.RequiredExercises
        );
    }

    /// <summary>網羅の行と、乱数の手の列を種ごとに流す</summary>
    [Fact(DisplayName = "入力の列: 網羅の行と乱数の手の列は不変条件を破らない")]
    [Trait(NightlyEngineTrait.Name, NightlyEngineTrait.Selected)]
    public void Seeds_Hold()
    {
        var plan = SeedSource.Resolve(CanvasInputGenerator.Seeds);
        plan.WriteWarnings(output);
        var failures = new List<EngineFailure>();
        var watch = Stopwatch.StartNew();
        var steps = 0;

        RunInIsolatedWindow(
            (vm, window) =>
            {
                using var harness = new CanvasInputHarness(vm, window);

                // 網羅の行は種の前に流す（単独の種の指定では流さない）
                if (!plan.IsSingle)
                {
                    foreach (
                        var (id, rowSteps) in CanvasInputRows
                            .Coverage.Concat(CanvasInputRows.PressCoverage)
                            .Concat(CanvasInputRows.DecisionCoverage)
                    )
                    {
                        steps += rowSteps.Count;

                        if (harness.Run(rowSteps) is { } violation)
                        {
                            failures.Add(
                                new EngineFailure(
                                    CanvasInputDimensions.CoverageScope,
                                    violation.Signature,
                                    $"{id}: {violation} | 手の列: {string.Join(" ", rowSteps)}"
                                )
                            );
                        }
                    }
                }

                foreach (var seed in plan.Seeds)
                {
                    var seedSteps = CanvasInputGenerator.Generate(seed);
                    steps += seedSteps.Count;

                    if (harness.Run(seedSteps) is { } violation)
                    {
                        failures.Add(
                            new EngineFailure(
                                CanvasInputDimensions.RandomScope,
                                violation.Signature,
                                $"{violation} | 手の列: {string.Join(" ", seedSteps)}",
                                seed
                            )
                        );
                    }
                }
            }
        );

        output.WriteLine(
            $"手 {steps} 個・種 {plan.Seeds.Count} 個・{watch.Elapsed.TotalSeconds:F1} 秒"
        );

        foreach (var failure in failures)
        {
            output.WriteLine(
                $"{failure.Scope} 種 {failure.Seed}: {failure.Signature} {failure.Detail}"
            );
        }

        CanvasInputDimensions
            .KnownFailures.Classify(
                failures,
                plan,
                scope =>
                    scope == CanvasInputDimensions.RandomScope
                    || (scope == CanvasInputDimensions.CoverageScope && !plan.IsSingle)
            )
            .AssertClean("入力の列の網羅の行と乱数の手の列");
    }

    /// <summary>網羅の行と乱数の手の列の網羅を数え、網羅しない組の一覧と照合する（窓で流さない）</summary>
    [Fact(DisplayName = "入力の列: 網羅の行と乱数の手の列の網羅は一覧と一致する")]
    [Trait(NightlyEngineTrait.Name, NightlyEngineTrait.Selected)]
    public void Coverage_MatchesGaps()
    {
        var plan = SeedSource.Resolve(CanvasInputGenerator.Seeds);
        var counter = new CoverageCounter(CanvasInputDimensions.Table);

        foreach (var (_, steps) in CanvasInputRows.Coverage)
        {
            CanvasInputDimensions.Observe(counter, steps);
        }

        foreach (var seed in plan.Seeds)
        {
            CanvasInputDimensions.Observe(counter, CanvasInputGenerator.Generate(seed));
        }

        var report = counter.Report();
        report.WriteSummary();
        report.WriteGapUsage(CanvasInputDimensions.Gaps, CanvasInputDimensions.RandomScope);
        report.AssertGaps(CanvasInputDimensions.Gaps, CanvasInputDimensions.RandomScope, plan);
    }

    /// <summary>押下の網羅の行と乱数の手の列の左ボタンの押下の網羅を数え、網羅しない組の一覧と照合する（窓で流さない）</summary>
    [Fact(DisplayName = "入力の列: 左ボタンの押下の網羅は一覧と一致する")]
    [Trait(NightlyEngineTrait.Name, NightlyEngineTrait.Selected)]
    public void PressCoverage_MatchesGaps()
    {
        var plan = SeedSource.Resolve(CanvasInputGenerator.Seeds);
        var counter = new CoverageCounter(CanvasInputDimensions.PressTable);

        foreach (var (_, steps) in CanvasInputRows.Coverage.Concat(CanvasInputRows.PressCoverage))
        {
            CanvasInputDimensions.ObservePresses(counter, steps);
        }

        foreach (var seed in plan.Seeds)
        {
            CanvasInputDimensions.ObservePresses(counter, CanvasInputGenerator.Generate(seed));
        }

        var report = counter.Report();
        report.WriteSummary();
        report.WriteGapUsage(CanvasInputDimensions.PressGaps, CanvasInputDimensions.RandomScope);
        report.AssertGaps(CanvasInputDimensions.PressGaps, CanvasInputDimensions.RandomScope, plan);
    }

    /// <summary>Space の持ち主の判定の網羅の行と乱数の手の列の判定を数え、網羅しない組の一覧と照合する（窓で流さない）</summary>
    [Fact(DisplayName = "入力の列: Space の持ち主の判定の網羅は一覧と一致する")]
    [Trait(NightlyEngineTrait.Name, NightlyEngineTrait.Selected)]
    public void DecisionCoverage_MatchesGaps()
    {
        var plan = SeedSource.Resolve(CanvasInputGenerator.Seeds);
        var counter = new CoverageCounter(CanvasInputDimensions.DecisionTable);

        foreach (
            var (_, steps) in CanvasInputRows.Coverage.Concat(CanvasInputRows.DecisionCoverage)
        )
        {
            CanvasInputDimensions.ObserveDecisions(counter, steps);
        }

        foreach (var seed in plan.Seeds)
        {
            CanvasInputDimensions.ObserveDecisions(counter, CanvasInputGenerator.Generate(seed));
        }

        var report = counter.Report();
        report.WriteSummary();
        report.WriteGapUsage(CanvasInputDimensions.DecisionGaps, CanvasInputDimensions.RandomScope);
        report.AssertGaps(
            CanvasInputDimensions.DecisionGaps,
            CanvasInputDimensions.RandomScope,
            plan
        );
    }

    /// <summary>
    /// I1 の例外（キーボードで移ったボタン自身の押下の取り消し）は、例外の形だけを通し、Click・切り替え・Space の手・
    /// 押下の持ち主の違いを伴う形を通さない（例外の自己検査）
    /// </summary>
    [Fact(
        DisplayName = "入力の列: I1 の例外は取り消しだけを通し、Click や切り替えを伴う形を通さない"
    )]
    public void CancelledControlPressException_IsNarrow()
    {
        // 例外の形：ボタン自身の押下・Space 以外のキー・モデルが取り消しを言う・IsPressed が戻った・反応 0・切り替えなし
        CanvasInputHarness
            .IsCancelledControlPress(true, false, true, true, 0, false)
            .Should()
            .BeTrue();

        var notAllowed = new (
            string Name,
            bool Owned,
            bool Space,
            bool Cancelled,
            bool Released,
            int Activations,
            bool CheckedChanged
        )[]
        {
            ("Click を伴う", true, false, true, true, 1, false),
            ("切り替えを伴う", true, false, true, true, 0, true),
            ("Space の手", true, true, true, true, 0, false),
            ("ボタンの押下でない", false, false, true, true, 0, false),
            ("モデルが取り消しを言わない", true, false, false, true, 0, false),
            ("押された（戻ったのでない）", true, false, true, false, 0, false),
        };

        foreach (var c in notAllowed)
        {
            CanvasInputHarness
                .IsCancelledControlPress(
                    c.Owned,
                    c.Space,
                    c.Cancelled,
                    c.Released,
                    c.Activations,
                    c.CheckedChanged
                )
                .Should()
                .BeFalse(c.Name);
        }
    }

    /// <summary>読み口の差し替えは、差し替えたスレッドだけに効き、スコープの破棄で前の値へ戻る（二重の破棄では何もしない）</summary>
    /// <remarks>
    /// 差し替えは <c>[ThreadStatic]</c> で、エンジンはハーネスの破棄で外す。外し忘れ・スレッドをまたいだ漏れがあると、
    /// 別のテストの実入力（差し替えなしで流す手書きのテスト）が仮想のボタン・位置を読んで黙って別の経路を通る。
    /// </remarks>
    [Fact(DisplayName = "入力の列: 読み口の差し替えはスレッドごとで、破棄で前の値へ戻る")]
    public void InputOverride_IsThreadLocalAndRestored()
    {
        var outer = new FixedInput();
        var inner = new FixedInput();
        var otherThreadSawOverride = true;

        using (BehaviorInput.OverrideForTests(outer))
        {
            var thread = new Thread(() =>
                otherThreadSawOverride = BehaviorInput.IsOverriddenForTests
            );
            thread.Start();
            thread.Join();
            var nested = BehaviorInput.OverrideForTests(inner);
            nested.Dispose();
            nested.Dispose();
            BehaviorInput
                .Modifiers.Should()
                .Be(
                    outer.Modifiers,
                    "入れ子の破棄は外側の差し替えへ戻る（二重の破棄では何もしない）"
                );
        }

        otherThreadSawOverride.Should().BeFalse("差し替えは差し替えたスレッドだけに効く");
        BehaviorInput.IsOverriddenForTests.Should().BeFalse("破棄で差し替えの無い状態へ戻る");
    }

    /// <summary>修飾キーだけを決めた差し替え先（外側と内側を見分けるために Ctrl を立てる）</summary>
    private sealed class FixedInput : IBehaviorInputSource
    {
        private static int _next;

        public MouseButtonState LeftButton => MouseButtonState.Released;

        public MouseButtonState MiddleButton => MouseButtonState.Released;

        public ModifierKeys Modifiers { get; } =
            Interlocked.Increment(ref _next) % 2 == 1 ? ModifierKeys.Control : ModifierKeys.Shift;

        public Point GetPosition(IInputElement? relativeTo) => default;

        public DragDropEffects DoDragDrop(
            DependencyObject dragSource,
            object data,
            DragDropEffects allowedEffects
        ) => DragDropEffects.None;
    }
}
