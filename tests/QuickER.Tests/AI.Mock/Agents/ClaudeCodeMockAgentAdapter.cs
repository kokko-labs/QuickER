using QuickER.AI;
using QuickER.AI.Mock;

namespace QuickER.Tests.AI.Mock.Agents;

/// <summary>Claude Code の実行器のアダプタ（<see cref="ClaudeCodeMockProjectAgent"/>＋台本どおりに動く偽のクライアント）</summary>
/// <remarks>
/// 偽のクライアントは網の中に置く。チャットの網の偽のクライアント（<c>ClaudeCodeChatEngineAdapter</c> の中）は
/// ツールの実行と中断をチャットの組（エンジン）へ戻す作りで、実行器（エンジンを持たない）からは使えない。
/// 実のクライアントと同じく、取り消しの例外は「理由の無い失敗」の結果へ畳んで返す（<c>ClaudeCodeMockProjectAgent.cs:75-80</c>）。
/// </remarks>
internal sealed class ClaudeCodeMockAgentAdapter : MockAgentAdapter
{
    private const string Source = "src/QuickER.AI.Mock/ClaudeCodeMockProjectAgent.cs";

    /// <summary>未ログインのときに偽のクライアントが返す文（実のクライアントの文をそのまま通すことを見る）</summary>
    private const string NotLoggedInText = "Not logged in";

    /// <inheritdoc/>
    public override Type AgentType => typeof(ClaudeCodeMockProjectAgent);

    /// <inheritdoc/>
    public override IReadOnlyDictionary<
        MockAgentCapability,
        MockAgentDeclaration
    > Declarations { get; } =
        new Dictionary<MockAgentCapability, MockAgentDeclaration>
        {
            [MockAgentCapability.DetectsNotLoggedIn] = new(
                true,
                $"{Source}:82（クライアントが報告した NotLoggedIn をそのまま結果へ写す）"
            ),
            [MockAgentCapability.ProbesLoginBeforeSession] = new(
                false,
                $"{Source}:71-73（認証を先に確かめず RunTurnAsync を 1 回呼ぶ。未ログインはそのターンの結果で届く）"
            ),
            [MockAgentCapability.ForwardsToolActivityToProgress] = new(
                false,
                $"{Source}:71-72（進捗へ流すのはクライアントのアシスタントの文章だけ）"
            ),
            [MockAgentCapability.ReportsDeclinedPermission] = new(
                false,
                $"{Source}:97-113（acceptEdits と許可ツールで動き、断りの通知を持たない）"
            ),
            [MockAgentCapability.TokenCancelCallsBackendInterrupt] = new(
                false,
                $"{Source}:75-80（ターンの後にトークンを見て投げ直すだけで、バックエンドの中断は呼ばない）"
            ),
            [MockAgentCapability.InterruptCompletesViaEvent] = new(
                false,
                $"{Source}:86-90（Interrupt を呼ぶだけで、中断の完了の通知を持たない）"
            ),
            [MockAgentCapability.InterruptReachesBackendWithoutRun] = new(
                true,
                $"{Source}:86-90（実行の有無に依らずクライアントの Interrupt を呼ぶ）"
            ),
            [MockAgentCapability.SendsContinuationNudge] = new(
                false,
                $"{Source}:71-82（RunTurnAsync を 1 回だけ呼んで結果を返す）"
            ),
            [MockAgentCapability.ReportsDisconnect] = new(
                false,
                $"{Source}:71-73（常駐の接続を持たず、ターンごとに RunTurnAsync を呼ぶ）"
            ),
            [MockAgentCapability.DisposesBackendPerRun] = new(
                false,
                $"{Source}:27-34（クライアントは注入された外の持ち物で、実行器は破棄しない）"
            ),
            [MockAgentCapability.SharesAgentPromptBuilder] = new(
                true,
                $"{Source}:65-69,105（初回のプロンプトとシステムプロンプトを共有の組み立てで作る）"
            ),
            [MockAgentCapability.SubmitsFilesThroughTool] = new(
                false,
                $"{Source}:24-25（Edit・Write・MultiEdit・Bash のネイティブのツールで書く）"
            ),
        };

    /// <inheritdoc/>
    public override MockAgentValue<string> BackendName { get; } =
        new(
            "Claude Code",
            $"{Source}:40（生成画面のタブ src/QuickER.AI.Mock/MockGenerationDialog.xaml の Header と同じ）"
        );

    /// <inheritdoc/>
    public override MockAgentValue<bool> FinalBuildEscapesSandbox { get; } =
        new(false, $"{Source}:43-47（無制限の Bash を持ち、最終ビルドで新しく越える境界が無い）");

    /// <inheritdoc/>
    public override MockAgentValue<MockAgentPromptSlot> AdditionalInstructionsPrompt { get; } =
        new(
            MockAgentPromptSlot.FirstPrompt,
            $"{Source}:65-69（追加の指示を共有の組み立て BuildPrompt へ渡し、作業全体を依頼する 1 回のターンの初回のプロンプトの末尾へ載せる）"
        );

    /// <inheritdoc/>
    public override MockAgentLaunch ExpectedLaunch(MockProjectAgentRequest request) =>
        new(
            request.WorkingDirectory,
            request.Model,
            MockProjectPromptBuilder.BuildSystemPrompt(request.Profile, request.ProjectName),
            "permissionMode=acceptEdits;allowedTools=Edit,Write,MultiEdit,Bash;mcpConfig=;allowedTool="
        );

    /// <inheritdoc/>
    public override string ExpectedReportedFailure(string detail) => detail;

    /// <inheritdoc/>
    public override bool? ExpectedAvailability(bool backendAvailable) => backendAvailable;

    /// <inheritdoc/>
    public override string ExpectedNotLoggedInError => NotLoggedInText;

    /// <inheritdoc/>
    public override MockAgentRig CreateRig(MockAgentRigOptions options) => new Rig(options);

    /// <summary>Claude Code の実行器の組</summary>
    private sealed class Rig : MockAgentRig
    {
        private readonly ScriptedClient _client;
        private readonly ClaudeCodeMockProjectAgent _agent;

        public Rig(MockAgentRigOptions options)
            : base(options)
        {
            _client = new ScriptedClient(SignalTurnOpen);
            _agent = new ClaudeCodeMockProjectAgent(_client);
        }

        public override IMockProjectAgent Agent => _agent;

        public override IReadOnlyList<string> Prompts => [.. _client.Prompts];

        public override string? SystemPrompt => _client.LastOptions?.SystemPrompt;

        public override MockAgentLaunch? Launch =>
            _client.LastOptions is { } options
                ? new(
                    options.WorkingDirectory,
                    options.Model,
                    options.SystemPrompt,
                    $"permissionMode={options.PermissionMode};allowedTools={string.Join(",", options.AdditionalAllowedTools)};mcpConfig={options.McpConfigPath};allowedTool={options.AllowedTool}"
                )
                : null;

        public override bool SessionStarted => _client.Prompts.Count > 0;

        public override int InterruptsReceived => _client.InterruptCount;

        public override string? LastInterruptTarget => null;

        public override bool BackendDisposed => _client.Disposed;

        public override void SetBackendAvailable(bool available) => _client.Available = available;

        protected override Task<MockProjectAgentOutcome> PlayAsync(
            MockAgentScript script,
            CancellationToken runToken
        )
        {
            _client.Script = script;
            return _agent.RunAsync(Request, OnProgress, runToken);
        }
    }

    /// <summary>台本どおりにターンを返す偽の Claude Code クライアント（プロセスを持たない）</summary>
    /// <param name="signalTurnOpen">終わらないターンが待ちに入った合図</param>
    private sealed class ScriptedClient(Action signalTurnOpen) : IClaudeCodeClient
    {
        /// <summary>流す台本</summary>
        public MockAgentScript? Script { get; set; }

        /// <summary>流れたターンのプロンプト</summary>
        public List<string> Prompts { get; } = [];

        /// <summary>最後のターンの起動オプション</summary>
        public ClaudeCodeLaunchOptions? LastOptions { get; private set; }

        /// <summary>中断の要求の数</summary>
        public int InterruptCount { get; private set; }

        /// <summary>claude が見つかるか</summary>
        public bool Available { get; set; } = true;

        /// <summary>破棄されたか</summary>
        public bool Disposed { get; private set; }

        public bool IsAvailable() => Available;

        public Task<ClaudeLoginProbeResult> ProbeLoginAsync(CancellationToken cancellationToken) =>
            Task.FromResult(ClaudeLoginProbeResult.LoggedIn);

        public async Task<ClaudeCodeTurnOutcome> RunTurnAsync(
            string prompt,
            string? resumeSessionId,
            ClaudeCodeLaunchOptions options,
            Action<string> onAssistantText,
            CancellationToken cancellationToken
        )
        {
            var script = Script ?? throw new InvalidOperationException("台本の無いターンが流れた");
            var turn =
                Prompts.Count < script.Turns.Count
                    ? script.Turns[Prompts.Count]
                    : throw new InvalidOperationException("台本に無いターンが流れた");
            Prompts.Add(prompt);
            LastOptions = options;

            if (!script.LoggedIn)
            {
                return new ClaudeCodeTurnOutcome(false, NotLoggedInText, null, true);
            }

            foreach (var step in turn.Steps)
            {
                if (step is not MockAgentStep.Delta delta)
                {
                    throw new NotSupportedException($"Claude Code の組は {step} を流せない");
                }

                onAssistantText(delta.Text);
            }

            switch (turn.Ending)
            {
                case MockAgentTurnEnding.Success:
                    return new ClaudeCodeTurnOutcome(true, null, "s1", false);

                case MockAgentTurnEnding.ReportedFailure failure:
                    return new ClaudeCodeTurnOutcome(false, failure.Detail, null, false);

                case MockAgentTurnEnding.Interrupted:
                    return new ClaudeCodeTurnOutcome(false, null, null, false);

                case MockAgentTurnEnding.Open:
                    signalTurnOpen();

                    try
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        // 実のクライアントと同じく、取り消しは理由の無い失敗の結果へ畳む
                    }

                    return new ClaudeCodeTurnOutcome(false, null, null, false);

                default:
                    throw new NotSupportedException($"Claude Code の組は {turn.Ending} を流せない");
            }
        }

        public void Interrupt() => InterruptCount++;

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
