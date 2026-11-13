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

        // 利用者の本物の操作・キャプチャの取り消しが混ざったかを、緑の回でも数で見る（テストの出力へ書く）
        var inputReports = "";

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
                                $"{violation} | 手の列: {string.Join(" ", row.Input)}",
                                Source: violation
                            )
                        );
                    }
                }

                inputReports =
                    $"{GuiFailureDiagnostics.InputReportSummary()}・実際のボタンが押されていた手 {harness.PhysicalButtonSteps}/{harness.StepCount}";
            }
        );

        output.WriteLine($"Win32 から届いた入力: {inputReports}");
        var known = CanvasInputDimensions.KnownFailures.Classify(failures, ids.Contains);
        CanvasInputHarness.PublishStates(known);
        known.AssertClean("入力の列の決まった形の行");

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
        var inputReports = "";
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
                                    $"{id}: {violation} | 手の列: {string.Join(" ", rowSteps)}",
                                    Source: violation
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
                                seed,
                                violation
                            )
                        );
                    }
                }

                inputReports =
                    $"{GuiFailureDiagnostics.InputReportSummary()}・実際のボタンが押されていた手 {harness.PhysicalButtonSteps}/{harness.StepCount}";
            }
        );

        output.WriteLine(
            $"手 {steps} 個・種 {plan.Seeds.Count} 個・{watch.Elapsed.TotalSeconds:F1} 秒"
        );
        output.WriteLine($"Win32 から届いた入力: {inputReports}");

        foreach (var failure in failures)
        {
            output.WriteLine(
                $"{failure.Scope} 種 {failure.Seed}: {failure.Signature} {failure.Detail}"
            );
        }

        var known = CanvasInputDimensions.KnownFailures.Classify(
            failures,
            plan,
            scope =>
                scope == CanvasInputDimensions.RandomScope
                || (scope == CanvasInputDimensions.CoverageScope && !plan.IsSingle)
        );
        CanvasInputHarness.PublishStates(known);
        known.AssertClean("入力の列の網羅の行と乱数の手の列");
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

    /// <summary>
    /// 違反は値で返り、テストが表明するのは窓を閉じた後なので、例外フィルターの記録（<see cref="GuiFailureDiagnostics"/>）を
    /// 通らない。ハーネスが違反を見つけた瞬間に、後始末の前の状態（窓が開いている・押下が残っている）と直前の手を記録して違反に
    /// 持たせ、既知の失敗の一覧と照合して赤にする失敗の記録だけを出力先へ書くことを確かめる（記録の自己検査）。
    /// 記録は手の列の違反（押したままの終わり）と種の始めの違反（前の残り）の両方で見る
    /// </summary>
    [Fact(
        DisplayName = "入力の列: 違反は見つけた瞬間に後始末の前の状態と直前の手が記録され、新しい失敗の記録だけが出力先へ書かれる"
    )]
    public void Violation_IsRecordedBeforeCleanup()
    {
        using var _ = GuiFailureDiagnostics.SuppressSinksForTests();
        CanvasViolation? stepViolation = null;
        CanvasViolation? startViolation = null;

        RunInIsolatedWindow(
            (vm, window) =>
            {
                using var harness = new CanvasInputHarness(vm, window);

                // 手の列の違反：Space を押したまま終わる（後始末が Space を離す前に記録されていれば、パン待機が残って見える）
                stepViolation = harness.Run([CanvasStep.SpaceDown]);

                // 種の始めの違反：前の残り（キャプチャ）を作ってから流す
                ((UIElement)window.FindName("ColumnsDataGrid")!).CaptureMouse();
                startViolation = harness.Run([]);
                Mouse.Capture(null);
            }
        );

        stepViolation.Should().NotBeNull();
        var stepReport = stepViolation!.StateReport;
        stepReport
            .Should()
            .NotBeNull("手の列の違反を記録する")
            .And.Contain("[表明の失敗]")
            .And.Contain(stepViolation.ToString())
            .And.Contain($"署名 {stepViolation.Signature}")
            .And.Contain("_isPanActive=True", "後始末（Space を離す）の前に記録する")
            .And.Contain("Closed=False", "窓を閉じる前に記録する")
            .And.Contain("[直前の手]")
            .And.Contain("0 SpaceDown: pan=True")
            .And.Contain("[Win32 から届いた入力]")
            .And.Contain("手: 0 SpaceDown")
            .And.NotContain("記録に失敗");

        startViolation.Should().NotBeNull();
        startViolation!.Invariant.Should().Be("I4");
        startViolation
            .StateReport.Should()
            .NotBeNull("種の始めの違反を記録する")
            .And.Contain(startViolation.ToString())
            .And.Contain("Mouse.Captured=DataGrid(ColumnsDataGrid)");

        // 出力先へ書くのは赤にする失敗の記録だけ（既知として数えた失敗＝毎回起きる G5 の行は書かない）。
        // 既知の失敗は新しい失敗の後に並ぶので、書いてしまえば最後に書いた記録がそれに変わる
        const string Self = "self-check";
        const string KnownRow = "g5-reload-during-space-restores-cursor";
        var known = new CanvasViolation(
            "I4",
            3,
            CanvasStep.SpaceUp,
            null,
            "キャンバスのカーソル Hand"
        )
        {
            StateReport = "known-state",
        };
        var report = CanvasInputDimensions.KnownFailures.Classify(
            [
                new EngineFailure(
                    Self,
                    stepViolation.Signature,
                    stepViolation.ToString(),
                    Source: stepViolation
                ),
                new EngineFailure(KnownRow, known.Signature, known.ToString(), Source: known),
            ],
            scope => scope is Self or KnownRow
        );
        report
            .Matched.Should()
            .ContainSingle(match => ReferenceEquals(match.Failure.Source, known));
        CanvasInputHarness.PublishStates(report);
        GuiFailureDiagnostics.LastPublished.Should().Be(stepReport);
    }

    /// <summary>
    /// WPF のコントロール（DataGrid のドラッグ選択・ButtonBase）は読み口の差し替えを通らず、実際のボタンをスレッドのキーの
    /// 状態の表から読む。全件の実行中に利用者がほかのアプリで押したボタンが混ざると、列一覧の押下の後にキャプチャが残る
    /// （網羅の行 coverage-press-rowother-move-out-none-none の I4）。ハーネスが入力を流すたびに表のボタンを「離れている」に
    /// 固定し、破棄で元へ戻すことを、表を押下にしてから同じ行を流して確かめる（固定の自己検査）。固定を外すと赤になることで、
    /// 押下が WPF に届く形を作れていることも確かめる。戻すことは成功・違反・例外の 3 つの道で見て、ほかのキーには触らない
    /// </summary>
    [Fact(
        DisplayName = "入力の列: 入力を流す間はキーの状態の表のマウスのボタンを離れているに固定し、破棄で元へ戻す"
    )]
    public void MouseButtons_ArePinnedReleasedAndRestored()
    {
        const int Left = 0x01;
        const int Marker = 0x87; // VK_F24：ボタンでないキー（触らないことを見る印）
        IReadOnlyList<CanvasStep> row =
        [
            CanvasStep.ClickFocus(CanvasTarget.Canvas),
            CanvasStep.LeftDown(CanvasSpot.RowOther),
            CanvasStep.MoveTo(CanvasSpot.Blank),
            CanvasStep.LeftUp,
        ];
        CanvasViolation? pinned = null;
        CanvasViolation? unpinned = null;
        var restoredAfterSuccess = false;
        var restoredAfterViolation = false;
        var restoredAfterException = false;
        var markerKept = false;
        var waits = 0;
        Exception? thrown = null;

        using var _ = GuiFailureDiagnostics.SuppressSinksForTests();

        RunInIsolatedWindow(
            (vm, window) =>
            {
                var original = MouseButtonKeyState.Capture();
                var markerOriginal = MouseButtonKeyState.IsDown(Marker);

                try
                {
                    MouseButtonKeyState.SetForTests(Marker, true);

                    // 成功の道：押下を表に置いてから流しても、固定があれば違反にならない
                    MouseButtonKeyState.SetForTests(Left, true);

                    using (var harness = new CanvasInputHarness(vm, window))
                    {
                        // 種の始めと手ごとに、実際のボタンが離れるまで待つ（ここでは回数だけ数える）
                        harness.WaitForPhysicalRelease = () => waits++;
                        pinned = harness.Run(row);
                    }

                    restoredAfterSuccess = MouseButtonKeyState.IsDown(Left);
                    markerKept = MouseButtonKeyState.IsDown(Marker);

                    // 違反の道：固定を外すと、表の押下が DataGrid に届いてキャプチャが残る。実際のボタンが押されていた
                    // 形にして（読み取りを差し替える）、違反の文に外からの乱れとして名指しされることも見る
                    using (
                        var harness = new CanvasInputHarness(vm, window)
                        {
                            PinsMouseButtons = false,
                            PhysicalButtonDown = () => true,
                        }
                    )
                    {
                        unpinned = harness.Run(row);
                        Mouse.Capture(null);
                    }

                    restoredAfterViolation = MouseButtonKeyState.IsDown(Left);

                    // 例外の道：手が組めず投げても、破棄で戻す
                    try
                    {
                        using var harness = new CanvasInputHarness(vm, window);
                        harness.Run([new CanvasStep(CanvasOp.TabTo)]);
                    }
                    catch (InvalidOperationException ex)
                    {
                        thrown = ex;
                    }

                    restoredAfterException = MouseButtonKeyState.IsDown(Left);
                }
                finally
                {
                    original.Restore();
                    MouseButtonKeyState.SetForTests(Marker, markerOriginal);
                }
            }
        );

        pinned.Should().BeNull("表のボタンを離れているに固定すれば、外からの押下は混ざらない");
        waits
            .Should()
            .Be(
                row.Count + 1,
                "種の始めと手ごとに、実際のボタンが離れるまで待つ（表は押されている間に書いても漏れる）"
            );
        unpinned
            .Should()
            .NotBeNull("固定を外すと押下が WPF のコントロールに届く（自己検査が効いている）");
        unpinned!.ToString().Should().Contain("キャプチャ DataGrid#ColumnsDataGrid");
        unpinned
            .ToString()
            .Should()
            .Contain(
                "（外からの乱れ: 実際のマウスのボタンが押されていた手 4 個（表は離れているに固定していない））",
                "手の列の間に実際のボタンが押されていたら、違反の文に名指しする（赤のまま）"
            );
        unpinned.StateReport.Should().Contain("外からの乱れ: 実際のマウスのボタンが押されていた手");
        restoredAfterSuccess.Should().BeTrue("成功の後も、ハーネスを作る前の値（押下）へ戻す");
        restoredAfterViolation.Should().BeTrue("違反の後も戻す");
        thrown.Should().NotBeNull("例外の道を通った");
        restoredAfterException.Should().BeTrue("例外の後も戻す");
        markerKept.Should().BeTrue("ボタンでないキーには触らない");
    }

    /// <summary>
    /// 手の列の間に見た外からの乱れ（前面が別のプロセスへ移った・実際のボタンが押されていた）は、違反を赤のまま残して
    /// 失敗の文に名指しする（流し直さない）。乱れが無ければ何も足さない（名指しの自己検査）
    /// </summary>
    [Fact(DisplayName = "入力の列: 外からの乱れは違反の文に名指しし、無ければ何も足さない")]
    public void Disturbance_IsNamedOnTheViolation()
    {
        CanvasInputHarness.DescribeDisturbance(0, 0, pinned: true).Should().BeNull();
        CanvasInputHarness
            .DescribeDisturbance(2, 0, pinned: true)
            .Should()
            .Be("手の列の間に前面が別のプロセスのウィンドウへ 2 回移った");
        CanvasInputHarness
            .DescribeDisturbance(0, 3, pinned: true)
            .Should()
            .Be("実際のマウスのボタンが押されていた手 3 個（表は離れているに固定）");
        CanvasInputHarness
            .DescribeDisturbance(1, 1, pinned: false)
            .Should()
            .Be(
                "手の列の間に前面が別のプロセスのウィンドウへ 1 回移った・実際のマウスのボタンが押されていた手 1 個（表は離れているに固定していない）"
            );

        var violation = new CanvasViolation("I4", 3, CanvasStep.LeftUp, null, "キャプチャ X");
        violation.ToString().Should().NotContain("外からの乱れ");
        (violation with { Disturbance = "前面が移った" })
            .ToString()
            .Should()
            .EndWith("キャプチャ X（外からの乱れ: 前面が移った）");
        (violation with { Disturbance = "前面が移った" })
            .Signature.Should()
            .Be(violation.Signature, "署名（既知の失敗の照合）には入れない");
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
