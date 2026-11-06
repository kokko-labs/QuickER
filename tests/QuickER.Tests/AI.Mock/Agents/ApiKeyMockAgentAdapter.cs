using QuickER.AI;
using QuickER.AI.Mock;
using MockStrings = QuickER.AI.Mock.Resources.Strings;

namespace QuickER.Tests.AI.Mock.Agents;

/// <summary>
/// API キーの実行器のアダプタ（<see cref="ApiKeyMockProjectAgent"/>＋共有の <see cref="FakeChatEngine"/>＋<see cref="QueueBuildRunner"/>）
/// </summary>
/// <remarks>
/// <para>
/// 実行器は固定のパイプライン（共通部 → 画面ごと → 中間ビルド）を回すので、組は素材（画面 1 つ）を作業フォルダへ置き、
/// パイプラインの各ターンに提出を 1 つずつ積む（台本の成功＝全部のターンが提出し、中間ビルドが通る）。
/// </para>
/// <para>
/// 中立の台本の初回のターン（手と終わり方）は、パイプラインの最初のターン（共通部）へ訳す。<see cref="FakeChatEngine"/> は
/// 送信の中で成功の完了だけを流すので、失敗・中断で終えるターンは、共有のダブルの完了の保留（<c>HoldTurnCompletion</c>）と
/// 保留した完了の失敗（<c>FailHeldTurn</c>）を送信の中から呼ぶ薄い包み（<see cref="ScriptedEngine"/>）で作る。
/// 中断の数は共有のダブルの <c>InterruptCount</c> を読む。
/// </para>
/// </remarks>
internal sealed class ApiKeyMockAgentAdapter : MockAgentAdapter
{
    private const string Source = "src/QuickER.AI.Mock/ApiKeyMockProjectAgent.cs";

    /// <inheritdoc/>
    public override Type AgentType => typeof(ApiKeyMockProjectAgent);

    /// <inheritdoc/>
    public override IReadOnlyDictionary<
        MockAgentCapability,
        MockAgentDeclaration
    > Declarations { get; } =
        new Dictionary<MockAgentCapability, MockAgentDeclaration>
        {
            [MockAgentCapability.DetectsNotLoggedIn] = new(
                false,
                $"{Source}:90-92,152-155（ログインの概念が無い。可否は VM の IsBackendReady が決め、実行器は常に可用で認証を確かめない）"
            ),
            [MockAgentCapability.ProbesLoginBeforeSession] = new(
                false,
                $"{Source}:152-155（エンジンの初期化と会話の開始だけで、認証を確かめない）"
            ),
            [MockAgentCapability.ForwardsToolActivityToProgress] = new(
                false,
                $"{Source}:149-150（エンジンの差分とターンの完了だけを購読し、ツールの活動は購読しない）"
            ),
            [MockAgentCapability.ReportsDeclinedPermission] = new(
                false,
                $"{Source}:149-150（許可を求めるバックエンドを持たず、断りの通知が無い）"
            ),
            [MockAgentCapability.TokenCancelCallsBackendInterrupt] = new(
                false,
                $"{Source}:329-336（送信の後にトークンを見て投げ直すだけで、エンジンの中断は呼ばない）"
            ),
            [MockAgentCapability.InterruptCompletesViaEvent] = new(
                false,
                $"{Source}:303,339（中断はエンジンへ渡すだけで、理由の無い失敗のターンは打ち切りにもならない）"
            ),
            [MockAgentCapability.InterruptReachesBackendWithoutRun] = new(
                false,
                $"{Source}:303（実行の外ではエンジンが無く、何もしない）"
            ),
            [MockAgentCapability.SendsContinuationNudge] = new(
                false,
                $"{Source}:157-223（固定のパイプラインで、続行の促しのターンを持たない）"
            ),
            [MockAgentCapability.ReportsDisconnect] = new(
                false,
                $"{Source}:149-150（接続断の通知を持たない）"
            ),
            [MockAgentCapability.DisposesBackendPerRun] = new(
                true,
                $"{Source}:279（1 実行の終わりにエンジンを破棄する）"
            ),
            [MockAgentCapability.SharesAgentPromptBuilder] = new(
                false,
                $"{Source}:133-144,165-172（BuildApiKeySystemPrompt・BuildApiKeyCommonPrompt の別系統で組む）"
            ),
            [MockAgentCapability.SubmitsFilesThroughTool] = new(
                true,
                $"{Source}:142,406-417（emit_file のツールを渡し、実行器自身がツールホストとして提出を書く）"
            ),
        };

    /// <inheritdoc/>
    public override MockAgentValue<string> BackendName { get; } =
        new(
            MockStrings.Mock_TabApiKey,
            $"{Source}:95（生成画面のタブ src/QuickER.AI.Mock/MockGenerationDialog.xaml の Header と同じ resx の Mock_TabApiKey）"
        );

    /// <inheritdoc/>
    public override MockAgentValue<bool> FinalBuildEscapesSandbox { get; } =
        new(false, $"{Source}:97-103（拡張子のホワイトリストでビルド設定を構造的に書けない）");

    /// <inheritdoc/>
    public override MockAgentValue<MockAgentPromptSlot> AdditionalInstructionsPrompt { get; } =
        new(
            MockAgentPromptSlot.SystemPrompt,
            $"{Source}:133-141（追加の指示を BuildApiKeySystemPrompt へ渡し、実行全体の規則を持つシステムプロンプトの末尾へ載せる。"
                + "共通部・画面・修正のプロンプトはそのターンに閉じた依頼。"
                + "src/QuickER.AI/ChatTurnEngine.cs:210-214,252-258,346 がシステムプロンプトを履歴の先頭に置き、毎ターン全履歴を送り直すので、1 回でどのターンにも届く）"
        );

    /// <inheritdoc/>
    public override MockAgentLaunch ExpectedLaunch(MockProjectAgentRequest request) =>
        new(
            null,
            null,
            MockProjectPromptBuilder.BuildApiKeySystemPrompt(
                request.Profile,
                request.ProjectName,
                request.AdditionalInstructions
            ),
            $"tools={MockProjectEmitTools.EmitFileToolName}"
        );

    /// <inheritdoc/>
    public override string ExpectedReportedFailure(string detail) =>
        string.Format(MockStrings.Mock_ApiRun_ErrorTurnFailedFormat, detail);

    /// <inheritdoc/>
    public override bool? ExpectedAvailability(bool backendAvailable) => true;

    /// <inheritdoc/>
    public override MockAgentRig CreateRig(MockAgentRigOptions options) => new Rig(options);

    /// <summary>API キーの実行器の組</summary>
    private sealed class Rig : MockAgentRig
    {
        private readonly QueueBuildRunner _build = new();
        private readonly ApiKeyMockProjectAgent _agent;
        private ScriptedEngine? _engine;
        private ErChatProfile? _profile;
        private MockAgentScript? _script;

        public Rig(MockAgentRigOptions options)
            : base(options)
        {
            ApiKeyMockMaterials.Setup(
                WorkingDirectory,
                ProjectName,
                ("OrderList.html", "Order List", "MARKER_S1")
            );
            _agent = new ApiKeyMockProjectAgent(CreateEngine, _build);
        }

        public override IMockProjectAgent Agent => _agent;

        public override IReadOnlyList<string> Prompts =>
            _engine is null ? [] : [.. _engine.Inner.SentPrompts];

        public override string? SystemPrompt => _profile?.BuildSystemPrompt();

        public override MockAgentLaunch? Launch =>
            _profile is null
                ? null
                : new(
                    null,
                    null,
                    _profile.BuildSystemPrompt(),
                    $"tools={string.Join(",", _profile.Tools.Select(tool => tool.Name))}"
                );

        public override bool SessionStarted => _engine is not null;

        public override int InterruptsReceived => _engine?.Inner.InterruptCount ?? 0;

        public override string? LastInterruptTarget => null;

        public override bool BackendDisposed => _engine?.Inner.DisposeCount > 0;

        // 可用性は常に true（API キーの準備は VM が見る）で、組から変えるものが無い
        public override void SetBackendAvailable(bool available) { }

        protected override Task<MockProjectAgentOutcome> PlayAsync(
            MockAgentScript script,
            CancellationToken runToken
        )
        {
            _script = script;
            return _agent.RunAsync(Request, OnProgress, runToken);
        }

        /// <summary>実行器が実行ごとに呼ぶエンジンのファクトリ（パイプラインの各ターンに提出を 1 つずつ積む）</summary>
        private IErChatEngine CreateEngine(ErChatProfile profile, IErDiagramToolHost toolHost)
        {
            _profile = profile;
            var inner = new FakeChatEngine(toolHost);
            var emit = ApiKeyMockMaterials.Emit($"{ProjectName}/{Options.Target.UiFile}");

            // 共通部・画面 1 つ・修正の分（使わない分は残るだけ）
            for (var i = 0; i < 3; i++)
            {
                inner.ScriptedTurns.Enqueue([emit]);
            }

            var first = _script?.Turns.Count > 0 ? _script.Turns[0] : null;
            _engine = new ScriptedEngine(inner, first, SignalTurnOpen);
            return _engine;
        }
    }

    /// <summary>
    /// 共有の <see cref="FakeChatEngine"/> を包み、最初のターンだけ台本の手と終わり方で流すエンジン（ほかは包んだダブルへ渡す）
    /// </summary>
    private sealed class ScriptedEngine : IErChatEngine
    {
        private readonly MockAgentTurn? _first;
        private readonly Action _signalTurnOpen;
        private int _sends;

        public ScriptedEngine(FakeChatEngine inner, MockAgentTurn? first, Action signalTurnOpen)
        {
            Inner = inner;
            _first = first;
            _signalTurnOpen = signalTurnOpen;
            inner.AssistantDeltaReceived += (_, delta) =>
                AssistantDeltaReceived?.Invoke(this, delta);
            inner.ToolActivityReceived += (_, activity) =>
                ToolActivityReceived?.Invoke(this, activity);
            inner.TurnCompleted += (_, result) => TurnCompleted?.Invoke(this, result);
            inner.StatusChanged += (_, status) => StatusChanged?.Invoke(this, status);
        }

        /// <summary>包んだ共有のダブル</summary>
        public FakeChatEngine Inner { get; }

        public event EventHandler<string>? AssistantDeltaReceived;
        public event EventHandler<ErChatToolActivity>? ToolActivityReceived;
        public event EventHandler<ErChatTurnResult>? TurnCompleted;
        public event EventHandler<string>? StatusChanged;

        public bool IsReady => Inner.IsReady;

        public Task InitializeAsync(CancellationToken cancellationToken = default) =>
            Inner.InitializeAsync(cancellationToken);

        public Task StartConversationAsync(CancellationToken cancellationToken = default) =>
            Inner.StartConversationAsync(cancellationToken);

        public Task SendAsync(string prompt, CancellationToken cancellationToken = default) =>
            SendAsync(prompt, Array.Empty<ChatAttachment>(), cancellationToken);

        public async Task SendAsync(
            string prompt,
            IReadOnlyList<ChatAttachment> attachments,
            CancellationToken cancellationToken = default
        )
        {
            if (++_sends != 1 || _first is null)
            {
                await Inner.SendAsync(prompt, attachments, cancellationToken);
                return;
            }

            foreach (var step in _first.Steps)
            {
                if (step is not MockAgentStep.Delta delta)
                {
                    throw new NotSupportedException($"API キーの組は {step} を流せない");
                }

                AssistantDeltaReceived?.Invoke(this, delta.Text);
            }

            switch (_first.Ending)
            {
                case MockAgentTurnEnding.Success:
                    await Inner.SendAsync(prompt, attachments, cancellationToken);
                    break;

                case MockAgentTurnEnding.ReportedFailure failure:
                    await SendHeldAsync(prompt, attachments, cancellationToken);
                    Inner.FailHeldTurn(failure.Detail);
                    break;

                case MockAgentTurnEnding.Interrupted:
                    await SendHeldAsync(prompt, attachments, cancellationToken);
                    Inner.FailHeldTurn(null);
                    break;

                case MockAgentTurnEnding.Open:
                    await SendHeldAsync(prompt, attachments, cancellationToken);
                    _signalTurnOpen();

                    try
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        // 実のエンジンと同じく、取り消しは中断（理由の無い失敗）のターンの完了へ畳む
                    }

                    Inner.FailHeldTurn(null);
                    break;

                default:
                    throw new NotSupportedException($"API キーの組は {_first.Ending} を流せない");
            }
        }

        /// <summary>完了を保留して送る（提出は流れ、完了だけが後で届く）</summary>
        private async Task SendHeldAsync(
            string prompt,
            IReadOnlyList<ChatAttachment> attachments,
            CancellationToken cancellationToken
        )
        {
            Inner.HoldTurnCompletion = true;

            try
            {
                await Inner.SendAsync(prompt, attachments, cancellationToken);
            }
            finally
            {
                Inner.HoldTurnCompletion = false;
            }
        }

        public Task InterruptAsync(CancellationToken cancellationToken = default) =>
            Inner.InterruptAsync(cancellationToken);

        public ValueTask DisposeAsync() => Inner.DisposeAsync();
    }
}
