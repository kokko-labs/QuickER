using QuickER.AI;
using QuickER.AI.Mock;
using MockStrings = QuickER.AI.Mock.Resources.Strings;

namespace QuickER.Tests.AI.Mock.Agents;

/// <summary>Copilot の実行器のアダプタ（<see cref="CopilotMockProjectAgent"/>＋共有の偽のランタイムクライアント）</summary>
/// <remarks>
/// 偽のクライアントの送信の途中（<c>DuringSend</c>）で、そのターンの台本の手と終わり方を通知として流す
/// （実行器は送信の前に完了の受け口を差し替えるので、送信の中の通知を取りこぼさない）。
/// </remarks>
internal sealed class CopilotMockAgentAdapter : MockAgentAdapter
{
    private const string Source = "src/QuickER.AI.Mock/CopilotMockProjectAgent.cs";

    /// <inheritdoc/>
    public override Type AgentType => typeof(CopilotMockProjectAgent);

    /// <inheritdoc/>
    public override IReadOnlyDictionary<
        MockAgentCapability,
        MockAgentDeclaration
    > Declarations { get; } =
        new Dictionary<MockAgentCapability, MockAgentDeclaration>
        {
            [MockAgentCapability.DetectsNotLoggedIn] = new(
                true,
                $"{Source}:132-142（GetAuthStatusAsync が未認証なら NotLoggedIn の失敗を返す）"
            ),
            [MockAgentCapability.ProbesLoginBeforeSession] = new(
                true,
                $"{Source}:132-147（認証を確かめてから StartSessionAsync でセッションを張る）"
            ),
            [MockAgentCapability.ForwardsToolActivityToProgress] = new(
                true,
                $"{Source}:69（組込みツールの実行の開始を進捗へ 1 行で流す）"
            ),
            [MockAgentCapability.ReportsDeclinedPermission] = new(
                true,
                $"{Source}:72-73（許可の範囲の外の要求を断ったことを (declined) つきで進捗へ流す）"
            ),
            [MockAgentCapability.TokenCancelCallsBackendInterrupt] = new(
                true,
                $"{Source}:105-109（トークンの取り消しで AbortAsync を送る）"
            ),
            [MockAgentCapability.InterruptCompletesViaEvent] = new(
                true,
                $"{Source}:76-80（aborted のアイドル復帰を取り消しとして伝える）"
            ),
            [MockAgentCapability.InterruptReachesBackendWithoutRun] = new(
                true,
                $"{Source}:191-201（実行の有無に依らず AbortAsync を送る）"
            ),
            [MockAgentCapability.SendsContinuationNudge] = new(
                true,
                $"{Source}:161-173（成功で UI のファイルが無ければ同じセッションへ 1 回だけ続行を促す）"
            ),
            [MockAgentCapability.ReportsDisconnect] = new(
                false,
                $"{Source}:98-102（購読するのは差分・ツールの開始・断り・アイドル・セッションのエラーだけ。接続断の通知を持たない）"
            ),
            [MockAgentCapability.DisposesBackendPerRun] = new(
                true,
                $"{Source}:186（1 実行の終わりにクライアントを破棄する）"
            ),
            [MockAgentCapability.SharesAgentPromptBuilder] = new(
                true,
                $"{Source}:151-155,216-219（初回のプロンプトと Instructions を共有の組み立てで作る）"
            ),
            [MockAgentCapability.SubmitsFilesThroughTool] = new(
                false,
                $"{Source}:210-220（ツールを公開せず、組込みのファイル編集を AllowWorkspaceTools で許す）"
            ),
        };

    /// <inheritdoc/>
    public override MockAgentValue<string> BackendName { get; } =
        new(
            "Copilot",
            $"{Source}:44（生成画面のタブ src/QuickER.AI.Mock/MockGenerationDialog.xaml の Header と同じ）"
        );

    /// <inheritdoc/>
    public override MockAgentValue<bool> FinalBuildEscapesSandbox { get; } =
        new(
            true,
            $"{Source}:47-52（出力フォルダのビルド設定を書けるが、外のコマンドの実行は承認されない）"
        );

    /// <inheritdoc/>
    public override MockAgentLaunch ExpectedLaunch(MockProjectAgentRequest request) =>
        new(
            request.WorkingDirectory,
            request.Model,
            MockProjectPromptBuilder.BuildSystemPrompt(request.Profile, request.ProjectName),
            "allowWorkspaceTools=True;tools=0;attachments=0"
        );

    /// <inheritdoc/>
    public override string ExpectedReportedFailure(string detail) => detail;

    /// <inheritdoc/>
    public override bool? ExpectedAvailability(bool backendAvailable) => backendAvailable;

    /// <inheritdoc/>
    public override string ExpectedNotLoggedInError => MockStrings.Mock_CopilotNotLoggedIn;

    /// <inheritdoc/>
    public override string ExpectedToolActivityText => Rig.ToolName;

    /// <inheritdoc/>
    public override MockAgentRig CreateRig(MockAgentRigOptions options) => new Rig(options);

    /// <summary>Copilot の実行器の組</summary>
    private sealed class Rig : MockAgentRig
    {
        /// <summary>ツールの活動の 1 手で流すツール名</summary>
        public const string ToolName = "bash";

        private readonly FakeCopilotRuntimeClient _client = new();
        private readonly CopilotMockProjectAgent _agent;

        public Rig(MockAgentRigOptions options)
            : base(options) => _agent = new CopilotMockProjectAgent(_client);

        public override IMockProjectAgent Agent => _agent;

        public override IReadOnlyList<string> Prompts =>
            [.. _client.Sends.Select(send => send.Prompt)];

        public override string? SystemPrompt => _client.LastSessionOptions?.Instructions;

        public override MockAgentLaunch? Launch =>
            _client.LastSessionOptions is { } options
                ? new(
                    options.WorkingDirectory,
                    options.Model,
                    options.Instructions,
                    string.Create(
                        System.Globalization.CultureInfo.InvariantCulture,
                        $"allowWorkspaceTools={options.AllowWorkspaceTools};tools={options.Tools.Count};attachments={_client.Sends.Sum(send => send.AttachmentCount)}"
                    )
                )
                : null;

        public override bool SessionStarted => _client.StartSessionCallCount > 0;

        public override int InterruptsReceived => _client.AbortCallCount;

        public override string? LastInterruptTarget => null;

        public override bool BackendDisposed => _client.Disposed;

        public override void SetBackendAvailable(bool available) => _client.Available = available;

        public override void EndOpenTurnAsInterrupted() => _client.RaiseIdle(aborted: true);

        protected override Task<MockProjectAgentOutcome> PlayAsync(
            MockAgentScript script,
            CancellationToken runToken
        )
        {
            if (!script.LoggedIn)
            {
                _client.AuthInfo = new CopilotAuthInfo(
                    false,
                    string.Empty,
                    string.Empty,
                    string.Empty
                );
            }

            // 送信の途中で、そのターンの台本を流す（台本に無いターンは何も返さない＝完了の待ちは上限で落ちる）
            _client.DuringSend = () =>
            {
                var index = _client.Sends.Count - 1;

                if (index < script.Turns.Count)
                {
                    PlayTurn(script.Turns[index]);
                }
            };

            return _agent.RunAsync(Request, OnProgress, runToken);
        }

        private void PlayTurn(MockAgentTurn turn)
        {
            foreach (var step in turn.Steps)
            {
                switch (step)
                {
                    case MockAgentStep.Delta delta:
                        _client.RaiseDelta(delta.Text);
                        break;

                    case MockAgentStep.ToolStarted:
                        _client.RaiseToolExecutionStarted(ToolName);
                        break;

                    case MockAgentStep.PermissionDeclined declined:
                        _client.RaisePermissionDeclined(declined.Description);
                        break;
                }
            }

            switch (turn.Ending)
            {
                case MockAgentTurnEnding.Success:
                    _client.RaiseIdle();
                    break;

                case MockAgentTurnEnding.ReportedFailure failure:
                    _client.RaiseError(failure.Detail);
                    break;

                case MockAgentTurnEnding.Interrupted:
                    _client.RaiseIdle(aborted: true);
                    break;

                case MockAgentTurnEnding.Open:
                    SignalTurnOpen();
                    break;

                default:
                    throw new NotSupportedException($"Copilot の組は {turn.Ending} を流せない");
            }
        }
    }
}
