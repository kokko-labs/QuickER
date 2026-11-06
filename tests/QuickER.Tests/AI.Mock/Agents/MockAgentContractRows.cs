using AwesomeAssertions;
using QuickER.AI.Mock;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.AI.Mock.Agents.MockAgentCapability;
using MockStrings = QuickER.AI.Mock.Resources.Strings;

namespace QuickER.Tests.AI.Mock.Agents;

/// <summary>モックプロジェクト生成の実行器の契約の行（行 × 軸の全実行器で流す）</summary>
/// <remarks>
/// 行は元のテストの入力と期待を持つ。実行器ごとの期待の違いはアダプタの宣言から組み、行の中で実行器を名指ししない。
/// 前提の性質を持たない実行器では流さない（網羅しない組の Accepted に載る）。ターゲットごとに見る行は Id の末尾にターゲットを持つ。
/// </remarks>
internal static class MockAgentContractRows
{
    /// <summary>成功のターンの途中で流すアシスタントの文章</summary>
    private const string StartText = "作業を開始します。\n";

    /// <summary>追加の指示</summary>
    private const string Instructions = "ダークテーマで実装して";

    /// <summary>行の表</summary>
    public static ScenarioTable<MockAgentScenario> Table =>
        new(
            EngineIds.Ai,
            [
                .. Outcome(),
                .. Login(),
                .. Progress(),
                .. Cancel(),
                .. Interrupt(),
                .. Nudge(),
                .. Lifetime(),
                .. Declaration(),
                .. Launch(),
                .. Prompt(),
            ]
        );

    /// <summary>実行の結果（成功・理由つきの失敗・接続断）</summary>
    private static IEnumerable<ScenarioRow<MockAgentScenario>> Outcome() =>
        [
            Row(
                "outcome-success-forwards-assistant-text",
                "バックエンドが成功で終えたら (true, null, false) を返し、アシスタントの文章を進捗へ流す",
                MockAgentTarget.Wpf,
                async c =>
                {
                    var outcome = await c.RunAsync(
                        MockAgentScript.Of(
                            MockAgentTurn.Of(
                                new MockAgentTurnEnding.Success(),
                                new MockAgentStep.Delta(StartText)
                            )
                        )
                    );

                    MockAgentContext.ShouldBe(outcome, true, null, false);
                    c.Rig.ProgressText.Should().Contain("作業を開始します");
                }
            ),
            Row(
                "outcome-reported-failure-carries-reason",
                "バックエンドが理由つきの失敗を報告したら、その理由を（実行器の組み方で）Error に載せた失敗を返す",
                MockAgentTarget.Wpf,
                async c =>
                {
                    var outcome = await c.RunAsync(
                        MockAgentScript.Of(
                            MockAgentTurn.Of(new MockAgentTurnEnding.ReportedFailure("boom"))
                        )
                    );

                    MockAgentContext.ShouldBe(
                        outcome,
                        false,
                        c.Adapter.ExpectedReportedFailure("boom"),
                        false
                    );
                }
            ),
            Row(
                "outcome-disconnect-while-waiting-fails-immediately",
                "ターンの完了を待つ間に接続が切れたら、全体のタイムアウトを待たずに失敗で終える",
                MockAgentTarget.Wpf,
                async c =>
                {
                    var outcome = await c.RunAsync(
                        MockAgentScript.Of(MockAgentTurn.Of(new MockAgentTurnEnding.Disconnected()))
                    );

                    MockAgentContext.ShouldBe(
                        outcome,
                        false,
                        c.Adapter.ExpectedDisconnectError,
                        false
                    );
                },
                ReportsDisconnect
            ),
        ];

    /// <summary>未ログイン</summary>
    private static IEnumerable<ScenarioRow<MockAgentScenario>> Login() =>
        [
            Row(
                "login-not-logged-in-is-reported",
                "未ログインなら NotLoggedIn の印つきの失敗を返す",
                MockAgentTarget.Wpf,
                async c =>
                {
                    var outcome = await c.RunAsync(
                        MockAgentScript.SingleSuccess with
                        {
                            LoggedIn = false,
                        }
                    );

                    MockAgentContext.ShouldBe(
                        outcome,
                        false,
                        c.Adapter.ExpectedNotLoggedInError,
                        true
                    );
                },
                DetectsNotLoggedIn
            ),
            Row(
                "login-probe-not-logged-in-starts-no-session",
                "セッションの前に認証を確かめる実行器は、未ログインならセッション（スレッド）を張らない",
                MockAgentTarget.Wpf,
                async c =>
                {
                    await c.RunAsync(MockAgentScript.SingleSuccess with { LoggedIn = false });

                    c.Rig.SessionStarted.Should().BeFalse();
                    c.Rig.Prompts.Should().BeEmpty();
                },
                ProbesLoginBeforeSession
            ),
        ];

    /// <summary>進捗へ流すもの</summary>
    private static IEnumerable<ScenarioRow<MockAgentScenario>> Progress() =>
        [
            Row(
                "progress-tool-activity-is-forwarded",
                "バックエンドのツールの活動を進捗へ流す",
                MockAgentTarget.Wpf,
                async c =>
                {
                    await c.RunAsync(
                        MockAgentScript.Of(
                            MockAgentTurn.Of(
                                new MockAgentTurnEnding.Success(),
                                new MockAgentStep.ToolStarted()
                            )
                        )
                    );

                    c.Rig.ProgressText.Should().Contain(c.Adapter.ExpectedToolActivityText);
                },
                ForwardsToolActivityToProgress
            ),
            Row(
                "progress-declined-permission-is-forwarded",
                "許可の範囲の外の要求を断ったことを、説明と declined の印つきで進捗へ流す",
                MockAgentTarget.Wpf,
                async c =>
                {
                    await c.RunAsync(
                        MockAgentScript.Of(
                            MockAgentTurn.Of(
                                new MockAgentTurnEnding.Success(),
                                new MockAgentStep.PermissionDeclined("url: https://example.com")
                            )
                        )
                    );

                    c.Rig.ProgressText.Should()
                        .Contain("url: https://example.com")
                        .And.Contain("declined");
                },
                ReportsDeclinedPermission
            ),
        ];

    /// <summary>トークンの取り消し</summary>
    private static IEnumerable<ScenarioRow<MockAgentScenario>> Cancel() =>
        [
            Row(
                "cancel-token-before-run-throws",
                "取り消し済みのトークンなら、バックエンドが中断の結果を返しても OperationCanceledException を投げる",
                MockAgentTarget.Wpf,
                async c =>
                {
                    using var cts = new CancellationTokenSource();
                    await cts.CancelAsync();

                    var act = () =>
                        c.Rig.RunAsync(
                            MockAgentScript.Of(
                                MockAgentTurn.Of(new MockAgentTurnEnding.Interrupted())
                            ),
                            cts.Token
                        );

                    await act.Should().ThrowAsync<OperationCanceledException>();
                }
            ),
            Row(
                "cancel-token-mid-run-throws",
                "実行中のトークンの取り消しで OperationCanceledException を投げ、バックエンドへ中断を送る実行器だけが 1 回送る",
                MockAgentTarget.Wpf,
                async c =>
                {
                    using var cts = new CancellationTokenSource();
                    var run = c.Rig.Start(
                        MockAgentScript.Of(MockAgentTurn.Of(new MockAgentTurnEnding.Open())),
                        cts.Token
                    );
                    await c.Rig.WhenTurnOpenAsync();

                    await cts.CancelAsync();

                    var act = () => MockAgentRig.Bounded(run);
                    await act.Should().ThrowAsync<OperationCanceledException>();
                    c.Rig.InterruptsReceived.Should()
                        .Be(c.Adapter.Has(TokenCancelCallsBackendInterrupt) ? 1 : 0);
                }
            ),
        ];

    /// <summary>InterruptAsync</summary>
    private static IEnumerable<ScenarioRow<MockAgentScenario>> Interrupt() =>
        [
            Row(
                "interrupt-during-turn-completes-as-canceled",
                "実行中の InterruptAsync はバックエンドへ 1 回届き、中断の完了の通知で OperationCanceledException になる",
                MockAgentTarget.Wpf,
                async c =>
                {
                    var run = c.Rig.Start(
                        MockAgentScript.Of(MockAgentTurn.Of(new MockAgentTurnEnding.Open())),
                        c.CancellationToken
                    );
                    await c.Rig.WhenTurnOpenAsync();

                    await c.Agent.InterruptAsync();

                    c.Rig.InterruptsReceived.Should().Be(1);
                    c.Rig.LastInterruptTarget.Should().Be(c.Adapter.ExpectedInterruptTarget);

                    c.Rig.EndOpenTurnAsInterrupted();

                    var act = () => MockAgentRig.Bounded(run);
                    await act.Should().ThrowAsync<OperationCanceledException>();
                },
                InterruptCompletesViaEvent
            ),
            Row(
                "interrupt-without-run-is-harmless",
                "実行の前の InterruptAsync は例外にならず、届く実行器だけがバックエンドへ 1 回届ける",
                MockAgentTarget.Wpf,
                async c =>
                {
                    var act = () => c.Agent.InterruptAsync();

                    await act.Should().NotThrowAsync();
                    c.Rig.InterruptsReceived.Should()
                        .Be(c.Adapter.Has(InterruptReachesBackendWithoutRun) ? 1 : 0);
                }
            ),
        ];

    /// <summary>続行の促し（ターゲットごと）</summary>
    private static IEnumerable<ScenarioRow<MockAgentScenario>> Nudge() =>
        MockAgentTarget.All.SelectMany<MockAgentTarget, ScenarioRow<MockAgentScenario>>(target =>
            [
                Row(
                    $"nudge-once-without-ui-file-{target.Name}",
                    "成功で終わったのに UI のファイルが無ければ、同じセッションへ 1 回だけ続行を促し、その完了を採用して進捗へ一行流す",
                    target,
                    async c =>
                    {
                        var outcome = await c.RunAsync(
                            MockAgentScript.Of(
                                MockAgentTurn.Of(new MockAgentTurnEnding.Success()),
                                MockAgentTurn.Of(new MockAgentTurnEnding.Success())
                            )
                        );

                        MockAgentContext.ShouldBe(outcome, true, null, false);
                        c.Rig.Prompts.Should().HaveCount(2);
                        c.Rig.Prompts[1].Should().Be(MockProjectPromptBuilder.ContinuationNudge);
                        c.Rig.ProgressText.Should()
                            .Contain(MockStrings.Mock_AutoContinueNotice.Trim());
                    },
                    SendsContinuationNudge,
                    uiFilePresent: false
                ),
                Row(
                    $"nudge-none-with-ui-file-{target.Name}",
                    "UI のファイルがあれば続行を促さず、1 ターンで終える",
                    target,
                    async c =>
                    {
                        var outcome = await c.RunAsync(MockAgentScript.SingleSuccess);

                        MockAgentContext.ShouldBe(outcome, true, null, false);
                        c.Rig.Prompts.Should().HaveCount(1);
                        c.Rig.ProgressText.Should()
                            .NotContain(MockStrings.Mock_AutoContinueNotice.Trim());
                    },
                    SendsContinuationNudge
                ),
            ]
        );

    /// <summary>可用性と破棄</summary>
    private static IEnumerable<ScenarioRow<MockAgentScenario>> Lifetime() =>
        [
            Row(
                "lifetime-availability-follows-declaration",
                "IsAvailable は例外にならず、バックエンドの可用性を（決められる実行器では）宣言どおりに写す",
                MockAgentTarget.Wpf,
                c =>
                {
                    foreach (var backendAvailable in new[] { false, true })
                    {
                        c.Rig.SetBackendAvailable(backendAvailable);
                        var available = c.Agent.IsAvailable();

                        if (c.Adapter.ExpectedAvailability(backendAvailable) is { } expected)
                        {
                            available
                                .Should()
                                .Be(expected, $"バックエンドの可用性 {backendAvailable}");
                        }
                    }

                    return Task.CompletedTask;
                }
            ),
            Row(
                "lifetime-backend-disposed-after-run",
                "1 回の実行の終わりにバックエンドを破棄する",
                MockAgentTarget.Wpf,
                async c =>
                {
                    await c.RunAsync(MockAgentScript.SingleSuccess);

                    c.Rig.BackendDisposed.Should().BeTrue();
                },
                DisposesBackendPerRun
            ),
        ];

    /// <summary>値の宣言</summary>
    private static IEnumerable<ScenarioRow<MockAgentScenario>> Declaration() =>
        [
            Row(
                "declaration-backend-name-is-tab-header",
                "BackendName は生成画面のタブの表記と同じ",
                MockAgentTarget.Wpf,
                c =>
                {
                    c.Agent.BackendName.Should().Be(c.Adapter.BackendName.Value);
                    return Task.CompletedTask;
                }
            ),
            Row(
                "declaration-final-build-escapes-sandbox",
                "FinalBuildEscapesSandbox は宣言の値を返す（最終ビルドの前の変更の検知を受けるか）",
                MockAgentTarget.Wpf,
                c =>
                {
                    c.Agent.FinalBuildEscapesSandbox.Should()
                        .Be(c.Adapter.FinalBuildEscapesSandbox.Value);
                    return Task.CompletedTask;
                }
            ),
        ];

    /// <summary>起動の設定（ターゲットごと）</summary>
    private static IEnumerable<ScenarioRow<MockAgentScenario>> Launch() =>
        MockAgentTarget.All.Select(target =>
            Row(
                $"launch-options-match-declaration-{target.Name}",
                "作業フォルダ・モデル・システムプロンプト・固有の設定（許可・サンドボックス・ツール）がアダプタの期待どおり",
                target,
                async c =>
                {
                    await c.RunAsync(MockAgentScript.SingleSuccess);

                    c.Rig.Launch.Should().Be(c.Adapter.ExpectedLaunch(c.Rig.Request));
                }
            )
        );

    /// <summary>プロンプト</summary>
    private static IEnumerable<ScenarioRow<MockAgentScenario>> Prompt() =>
        [
            .. MockAgentTarget.All.Select(target =>
                Row(
                    $"prompt-states-target-conventions-{target.Name}",
                    "成功の実行で、システムプロンプトと初回のプロンプトがターゲットの規約と共有の規約を案内し、ほかのターゲットの語彙を出さない",
                    target,
                    async c =>
                    {
                        var outcome = await c.RunAsync(MockAgentScript.SingleSuccess);

                        MockAgentContext.ShouldBe(outcome, true, null, false);
                        var combined = c.Rig.SystemPrompt + "\n" + c.Rig.Prompts[0];
                        var (present, absent) = TargetConventions(target);

                        foreach (var fragment in present)
                        {
                            combined.Should().Contain(fragment);
                        }

                        foreach (var fragment in absent)
                        {
                            combined.Should().NotContain(fragment);
                        }
                    }
                )
            ),
            .. MockAgentTarget.All.Select(target =>
                Row(
                    $"prompt-shared-builder-states-headless-rules-{target.Name}",
                    "共有の組み立てを使う実行器は、その出力そのままを送り、非対話の実行・確認の禁止・完了条件・VS 標準の構成を案内する",
                    target,
                    async c =>
                    {
                        await c.RunAsync(MockAgentScript.SingleSuccess);

                        var profile = target.Profile;
                        var system = c.Rig.SystemPrompt;
                        var first = c.Rig.Prompts[0];

                        system
                            .Should()
                            .Be(
                                MockProjectPromptBuilder.BuildSystemPrompt(
                                    profile,
                                    MockAgentRig.ProjectName
                                )
                            );
                        first
                            .Should()
                            .Be(
                                MockProjectPromptBuilder.BuildPrompt(
                                    profile,
                                    MockAgentRig.ProjectName,
                                    null
                                )
                            );
                        system
                            .Should()
                            .Contain("non-interactive, headless run")
                            .And.Contain("wait for approval")
                            .And.Contain("AcmeMock.sln")
                            .And.Contain("AcmeMock/Generated/")
                            .And.Contain("AcmeMock/design/mock/mock.json");
                        first
                            .Should()
                            .Contain("Do not ask for confirmation or approval")
                            .And.Contain("Completion criteria")
                            .And.Contain("through I{Entity}Repository")
                            .And.Contain("AcmeMock/design/mock/mock.json")
                            .And.Contain("AcmeMock/README-QuickER.md")
                            .And.Contain("AcmeMock/Generated/")
                            .And.Contain("AcmeMock.sln");
                    },
                    SharesAgentPromptBuilder
                )
            ),
            Row(
                "prompt-emit-tool-paths-follow-target-blazor",
                "提出のツールで受け取る実行器は、Blazor の提出のパスの例と共通部の提出のファイルを案内する",
                MockAgentTarget.Blazor,
                async c =>
                {
                    await c.RunAsync(MockAgentScript.SingleSuccess);

                    c.Rig.SystemPrompt.Should().Contain("Components/Pages/OrderList.razor");
                    c.Rig.Prompts[0].Should().Contain("Components/App.razor");
                },
                SubmitsFilesThroughTool
            ),
            Row(
                "prompt-additional-instructions-appended-after-heading",
                "追加の指示が空でなければ、初回のプロンプトの末尾に見出しの後に連結される",
                MockAgentTarget.Wpf,
                async c =>
                {
                    await c.RunAsync(MockAgentScript.SingleSuccess);

                    c.Rig.Prompts[0]
                        .Should()
                        .EndWith(
                            "\n\n"
                                + MockProjectPromptBuilder.AdditionalInstructionsHeading
                                + "\n"
                                + Instructions
                        );
                },
                additionalInstructions: Instructions
            ),
            Row(
                "prompt-blank-additional-instructions-add-no-heading",
                "追加の指示が空白だけなら、どのプロンプトにも見出しが付かない",
                MockAgentTarget.Wpf,
                async c =>
                {
                    await c.RunAsync(MockAgentScript.SingleSuccess);

                    c.Rig.Prompts.Append(c.Rig.SystemPrompt ?? string.Empty)
                        .Should()
                        .NotContain(prompt =>
                            prompt.Contains(
                                MockProjectPromptBuilder.AdditionalInstructionsHeading,
                                StringComparison.Ordinal
                            )
                        );
                },
                additionalInstructions: "   "
            ),
        ];

    /// <summary>ターゲットの規約の語句（含む・含まない）</summary>
    /// <param name="target">ターゲット</param>
    private static (string[] Present, string[] Absent) TargetConventions(MockAgentTarget target) =>
        target == MockAgentTarget.Blazor
            ? (
                [
                    ".razor",
                    "@page",
                    "InteractiveServer",
                    "style.css",
                    "assign the primary key in the application",
                    "NuGet.Config",
                ],
                ["CommunityToolkit", ".xaml"]
            )
            : (
                [
                    "XAML view",
                    "forbidden",
                    "hard-coded list",
                    "assign the primary key in the application",
                    "GuidKey",
                    "NuGet.Config",
                ],
                []
            );

    /// <summary>行を作る</summary>
    /// <param name="id">行の Id（{観点}-{守っていること}[-{ターゲット}]）</param>
    /// <param name="description">説明</param>
    /// <param name="target">ターゲット</param>
    /// <param name="body">流して表明する本体</param>
    /// <param name="requires">前提にする性質</param>
    /// <param name="uiFilePresent">UI のファイルを置いておくか</param>
    /// <param name="additionalInstructions">要求に添える追加の指示</param>
    private static ScenarioRow<MockAgentScenario> Row(
        string id,
        string description,
        MockAgentTarget target,
        Func<MockAgentContext, Task> body,
        MockAgentCapability? requires = null,
        bool uiFilePresent = true,
        string? additionalInstructions = null
    ) =>
        new(
            id,
            null,
            description,
            new MockAgentScenario(
                id[..id.IndexOf('-')],
                requires is { } capability ? [capability] : [],
                new MockAgentRigOptions(target, uiFilePresent, additionalInstructions),
                body
            )
        );
}
