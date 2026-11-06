using QuickER.AI;
using QuickER.AI.Mock;
using MockStrings = QuickER.AI.Mock.Resources.Strings;

namespace QuickER.Tests.AI.Mock.Agents;

/// <summary>Codex の実行器のアダプタ（<see cref="CodexMockProjectAgent"/>＋共有の偽の App Server クライアント）</summary>
/// <remarks>
/// 偽のクライアントは呼び出しを同期で終えるので、実行器はターンを送ってから完了の通知を待つ。組はターンが送られたのを
/// 見てから、台本の手と終わり方を通知として流す（続行の促しの 2 ターン目は別のスレッドで送られるので待つ）。
/// </remarks>
internal sealed class CodexMockAgentAdapter : MockAgentAdapter
{
    private const string Source = "src/QuickER.AI.Mock/CodexMockProjectAgent.cs";

    /// <inheritdoc/>
    public override Type AgentType => typeof(CodexMockProjectAgent);

    /// <inheritdoc/>
    public override IReadOnlyDictionary<
        MockAgentCapability,
        MockAgentDeclaration
    > Declarations { get; } =
        new Dictionary<MockAgentCapability, MockAgentDeclaration>
        {
            [MockAgentCapability.DetectsNotLoggedIn] = new(
                true,
                $"{Source}:168-178（openai のプロバイダーで ReadAccountAsync が未ログインなら NotLoggedIn の失敗を返す）"
            ),
            [MockAgentCapability.ProbesLoginBeforeSession] = new(
                true,
                $"{Source}:168-184（認証を確かめてから StartThreadAsync でスレッドを張る）"
            ),
            [MockAgentCapability.ForwardsToolActivityToProgress] = new(
                true,
                $"{Source}:83-84,305-311（item/started の commandExecution・fileChange を進捗へ 1 行で流す）"
            ),
            [MockAgentCapability.ReportsDeclinedPermission] = new(
                false,
                $"{Source}:125-128（購読するのは差分・項目の開始・ターンの完了・接続断だけ。承認は never で断りの通知を持たない）"
            ),
            [MockAgentCapability.TokenCancelCallsBackendInterrupt] = new(
                true,
                $"{Source}:131-135（トークンの取り消しで InterruptTurnAsync を送る）"
            ),
            [MockAgentCapability.InterruptCompletesViaEvent] = new(
                true,
                $"{Source}:91-95（interrupted のターンの完了を取り消しとして伝える）"
            ),
            [MockAgentCapability.InterruptReachesBackendWithoutRun] = new(
                false,
                $"{Source}:232-247（スレッドとターンが在るときだけ InterruptTurnAsync を送る）"
            ),
            [MockAgentCapability.SendsContinuationNudge] = new(
                true,
                $"{Source}:198-213（成功で UI のファイルが無ければ同じスレッドへ 1 回だけ続行を促す）"
            ),
            [MockAgentCapability.ReportsDisconnect] = new(
                true,
                $"{Source}:116-123（接続断で待機中の完了を失敗で解く）"
            ),
            [MockAgentCapability.DisposesBackendPerRun] = new(
                true,
                $"{Source}:227（1 実行の終わりにクライアントを破棄する）"
            ),
            [MockAgentCapability.SharesAgentPromptBuilder] = new(
                true,
                $"{Source}:188-192,273-276（初回のプロンプトと developer instructions を共有の組み立てで作る）"
            ),
            [MockAgentCapability.SubmitsFilesThroughTool] = new(
                false,
                $"{Source}:263-277（dynamicTools を登録せず、Codex のファイル編集に任せる）"
            ),
        };

    /// <inheritdoc/>
    public override MockAgentValue<string> BackendName { get; } =
        new(
            "Codex",
            $"{Source}:59（生成画面のタブ src/QuickER.AI.Mock/MockGenerationDialog.xaml の Header と同じ）"
        );

    /// <inheritdoc/>
    public override MockAgentValue<bool> FinalBuildEscapesSandbox { get; } =
        new(
            true,
            $"{Source}:61-66（workspace-write のサンドボックスでビルド設定を書けるが、外でコマンドを実行できない）"
        );

    /// <inheritdoc/>
    public override MockAgentValue<MockAgentPromptSlot> AdditionalInstructionsPrompt { get; } =
        new(
            MockAgentPromptSlot.FirstPrompt,
            $"{Source}:188-192（追加の指示を共有の組み立て BuildPrompt へ渡し、作業全体を依頼する初回のプロンプトの末尾へ載せる。続行の促しは同じスレッドへの別のターン）"
        );

    /// <inheritdoc/>
    public override MockAgentLaunch ExpectedLaunch(MockProjectAgentRequest request) =>
        new(
            request.WorkingDirectory,
            request.Model,
            MockProjectPromptBuilder.BuildSystemPrompt(request.Profile, request.ProjectName),
            $"approval=never;sandbox=workspace-write;provider={request.ModelProvider};dynamicTools=none"
        );

    /// <inheritdoc/>
    public override string ExpectedReportedFailure(string detail) => detail;

    /// <inheritdoc/>
    /// <remarks>可用性は PATH を走査する静的なロケーターで決まり、組からは変えられない（値は問わない）</remarks>
    public override bool? ExpectedAvailability(bool backendAvailable) => null;

    /// <inheritdoc/>
    public override string ExpectedNotLoggedInError => MockStrings.Mock_CodexNotLoggedIn;

    /// <inheritdoc/>
    public override string ExpectedDisconnectError => MockStrings.Mock_CodexDisconnected;

    /// <inheritdoc/>
    public override string ExpectedToolActivityText => "commandExecution";

    /// <inheritdoc/>
    public override string? ExpectedInterruptTarget => $"{Rig.ThreadId}/{Rig.TurnId}";

    /// <inheritdoc/>
    public override MockAgentRig CreateRig(MockAgentRigOptions options) => new Rig(options);

    /// <summary>Codex の実行器の組</summary>
    private sealed class Rig : MockAgentRig
    {
        /// <summary>偽のクライアントが返すスレッドの Id</summary>
        public const string ThreadId = "thr_test";

        /// <summary>偽のクライアントが返すターンの Id</summary>
        public const string TurnId = "turn_test";

        private readonly FakeCodexAppServerClient _client = new() { NextTurnId = TurnId };
        private readonly CodexMockProjectAgent _agent;

        public Rig(MockAgentRigOptions options)
            : base(options) => _agent = new CodexMockProjectAgent(_client);

        public override IMockProjectAgent Agent => _agent;

        public override IReadOnlyList<string> Prompts => [.. _client.TurnPrompts];

        public override string? SystemPrompt =>
            _client.LastThreadStartOptions?.DeveloperInstructions;

        public override MockAgentLaunch? Launch =>
            _client.LastThreadStartOptions is { } options
                ? new(
                    options.Cwd,
                    options.Model,
                    options.DeveloperInstructions,
                    $"approval={options.ApprovalPolicy};sandbox={options.Sandbox};provider={options.ModelProvider};dynamicTools={(options.DynamicTools is null ? "none" : "set")}"
                )
                : null;

        public override bool SessionStarted => _client.StartThreadCount > 0;

        public override int InterruptsReceived => _client.InterruptTurnCount;

        public override string? LastInterruptTarget =>
            _client.LastInterruptThreadId is null
                ? null
                : $"{_client.LastInterruptThreadId}/{_client.LastInterruptTurnId}";

        public override bool BackendDisposed => _client.DisposeCount > 0;

        // 可用性は静的なロケーターが決めるので、偽のクライアントの値は効かない（変えても実行器は読まない）
        public override void SetBackendAvailable(bool available) =>
            _client.IsCliAvailable = available;

        public override void EndOpenTurnAsInterrupted() =>
            _client.RaiseTurnCompleted("interrupted");

        protected override async Task<MockProjectAgentOutcome> PlayAsync(
            MockAgentScript script,
            CancellationToken runToken
        )
        {
            if (!script.LoggedIn)
            {
                _client.NextAccountInfo = new CodexAccountInfo
                {
                    RequiresOpenAiAuth = true,
                    AuthMode = CodexAuthMode.None,
                };
            }

            var run = _agent.RunAsync(Request, OnProgress, runToken);

            for (var i = 0; i < script.Turns.Count; i++)
            {
                var index = i;
                await WaitUntilAsync(() => _client.StartTurnCount > index || run.IsCompleted);

                if (_client.StartTurnCount <= index)
                {
                    break;
                }

                var turn = script.Turns[i];

                foreach (var step in turn.Steps)
                {
                    switch (step)
                    {
                        case MockAgentStep.Delta delta:
                            _client.RaiseAgentMessageDelta(delta.Text);
                            break;

                        case MockAgentStep.ToolStarted:
                            _client.RaiseItemStarted(
                                new CodexItemStartedNotification { ItemType = "commandExecution" }
                            );
                            break;

                        default:
                            throw new NotSupportedException($"Codex の組は {step} を流せない");
                    }
                }

                switch (turn.Ending)
                {
                    case MockAgentTurnEnding.Success:
                        _client.RaiseTurnCompleted("completed");
                        break;

                    case MockAgentTurnEnding.ReportedFailure failure:
                        _client.RaiseTurnCompleted("failed", failure.Detail);
                        break;

                    case MockAgentTurnEnding.Interrupted:
                        _client.RaiseTurnCompleted("interrupted");
                        break;

                    case MockAgentTurnEnding.Disconnected:
                        _client.RaiseDisconnected();
                        break;

                    case MockAgentTurnEnding.Open:
                        SignalTurnOpen();
                        break;
                }
            }

            return await run;
        }
    }
}
