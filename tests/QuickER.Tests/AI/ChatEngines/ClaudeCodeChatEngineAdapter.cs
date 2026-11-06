using System.IO;
using QuickER.AI;
using QuickER.AI.Chat;
using QuickER.AI.Resources;

namespace QuickER.Tests.AI.ChatEngines;

/// <summary>Claude Code 接続のアダプタ（<see cref="ClaudeCodeChatEngine"/>＋台本どおりに動く偽のクライアント）</summary>
/// <remarks>
/// ツールの呼び出しは、製品では claude CLI が MCP サーバー経由で呼ぶ。偽のクライアントはプロセスを持たないので、
/// MCP サーバーが呼ぶ入口 <c>ClaudeCodeChatEngine.ExecuteTool</c>（internal）を直接呼ぶ（元のテストと同じ）。
/// </remarks>
internal sealed class ClaudeCodeChatEngineAdapter : ChatEngineAdapter
{
    /// <inheritdoc/>
    public override ErChatBackendKind Kind => ErChatBackendKind.ClaudeCode;

    /// <inheritdoc/>
    public override IReadOnlyDictionary<
        ChatEngineCapability,
        ChatEngineDeclaration
    > Declarations { get; } =
        new Dictionary<ChatEngineCapability, ChatEngineDeclaration>
        {
            [ChatEngineCapability.AsyncCompletion] = new(
                false,
                "src/QuickER.AI/ClaudeCodeChatEngine.cs SendAsync（RunTurnAsync の戻り値でターンを終える。完了を運ぶ別の通知は無い）"
            ),
            [ChatEngineCapability.TurnRunsInsideSend] = new(
                true,
                "src/QuickER.AI/ClaudeCodeChatEngine.cs SendAsync（RunTurnAsync を待ってから TurnCompleted を出す）"
            ),
            [ChatEngineCapability.ToolResponseChannel] = new(
                false,
                "src/QuickER.AI/ClaudeCodeChatEngine.cs ExecuteTool（結果は MCP 応答＝戻り値として返す。別の送信は無い）"
            ),
            [ChatEngineCapability.OptionalToolHost] = new(
                true,
                "src/QuickER.AI/ClaudeCodeChatEngine.cs ExecuteTool（toolHost が null なら Tools are not available. の失敗を返す）"
            ),
            [ChatEngineCapability.ReportedFailure] = new(
                true,
                "src/QuickER.AI/ClaudeCodeChatEngine.cs BuildTurnFailedMessage（CLI・クライアントが報告した文を詳細に添える）"
            ),
            [ChatEngineCapability.ToolReturnCarriesSuccess] = new(
                true,
                "src/QuickER.AI/ClaudeCodeChatEngine.cs ExecuteTool（(Result, Success) を返し、DelegatingToolFunction が CallToolResult.IsError へ写す）"
            ),
            [ChatEngineCapability.CliLifecycle] = new(
                true,
                "src/QuickER.AI/ClaudeCodeChatEngine.cs InitializeCoreAsync（claude が見つからなければ ClaudeCode_Status_NotFound と InstallGuidance）・RefreshAsync（再確認）"
            ),
            [ChatEngineCapability.ExposesCliMissing] = new(
                false,
                "src/QuickER.AI/ClaudeCodeChatEngine.cs（見つからない印のプロパティを持たず、状態は StatusSummary・StatusLevel・Guidance で表す）"
            ),
            [ChatEngineCapability.LaunchesRuntime] = new(
                false,
                "src/QuickER.AI/ClaudeCodeChatEngine.cs SendAsync（ターンごとに RunTurnAsync で claude を動かす。常駐プロセスを起動して接続しない）"
            ),
            [ChatEngineCapability.ConnectionCanDrop] = new(
                false,
                "src/QuickER.AI/ClaudeCodeChatEngine.cs SendAsync（常駐の接続を持たず、会話はセッション Id の resume で続ける）"
            ),
            [ChatEngineCapability.OwnsBackend] = new(
                true,
                "src/QuickER.AI/ClaudeCodeChatEngine.cs DisposeAsync（クライアントを破棄する）"
            ),
            [ChatEngineCapability.CreatesWorkingDirectory] = new(
                true,
                "src/QuickER.AI/ClaudeCodeChatEngine.cs InitializeCoreAsync（BackendWorkingDirectory.Create で claude-code の作業フォルダを作る）"
            ),
            [ChatEngineCapability.AcceptsAttachments] = new(
                true,
                "src/QuickER.AI/ChatAttachment.cs AttachmentSupportResolver.ClaudeCode（全種別）"
            ),
            [ChatEngineCapability.RefusesSomeAttachmentKind] = new(
                false,
                "src/QuickER.AI/ChatAttachment.cs AttachmentSupportResolver.ClaudeCode（全種別を受け付け、断る種類が無い）"
            ),
            [ChatEngineCapability.RefusesAttachmentAsTurnFailure] = new(
                false,
                "src/QuickER.AI/ChatAttachment.cs AttachmentSupportResolver.ClaudeCode（断る種類が無いので、断り方も無い）"
            ),
        };

    /// <inheritdoc/>
    public override AttachmentSupport ExpectedAttachmentSupport =>
        AttachmentSupport.Images
        | AttachmentSupport.Pdf
        | AttachmentSupport.Text
        | AttachmentSupport.Binary;

    /// <inheritdoc/>
    public override string ExpectedFailureFromException(Exception exception) =>
        SummaryWithDetail(Strings.ClaudeCode_TurnFailed, exception.Message);

    /// <inheritdoc/>
    public override string ExpectedReportedFailure(string? detail) =>
        SummaryWithDetail(Strings.ClaudeCode_TurnFailed, detail);

    /// <inheritdoc/>
    public override string ExpectedCliMissingSummary => Strings.ClaudeCode_Status_NotFound;

    /// <inheritdoc/>
    public override string ExpectedInstallGuidance => Strings.ClaudeCode_Guidance_Install;

    /// <inheritdoc/>
    public override ChatEngineRig CreateRig(ChatToolHostKind hostKind) => new Rig(hostKind);

    /// <summary>Claude Code 接続の組</summary>
    private sealed class Rig : ChatEngineRig
    {
        private readonly ScriptedClaudeCodeClient _client;
        private readonly ClaudeCodeChatEngine _engine;
        private readonly List<ChatToolReturn> _toolReturns = [];

        public Rig(ChatToolHostKind hostKind)
            : base(hostKind)
        {
            _client = new ScriptedClaudeCodeClient(this);
            _engine = new ClaudeCodeChatEngine(
                _client,
                ToolHost,
                new ChatSyncDispatcher(),
                ErDesignProfile.ErDesign
            );
            Attach();
        }

        public override IErChatEngine Engine => _engine;

        public override IReadOnlyList<ChatToolReturn> ToolReturns => [.. _toolReturns];

        public override int InterruptsReceived => _client.InterruptCount;

        // 元のテストと同じく、初期化せずに MCP の入口を直接呼ぶ
        public override Task PrepareForToolsAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;

        protected override Task RaiseToolRequestAsync(
            ChatStep.ToolCall call,
            CancellationToken cancellationToken
        )
        {
            ExecuteTool(call);
            return Task.CompletedTask;
        }

        public override IReadOnlyList<string> PromptsReceived => [.. _client.Prompts];

        public override int ConversationsOpened => _client.ConversationsOpened;

        public override int AttachmentsReceived => _client.AttachmentsReceived;

        public override bool BackendDisposed => _client.Disposed;

        public override string? WorkingDirectory => _client.LastWorkingDirectory;

        public override ChatReadiness Readiness =>
            new(
                _engine.IsReady,
                null,
                _engine.StatusLevel,
                _engine.StatusSummary,
                _engine.Guidance,
                false
            );

        public override void SetCliAvailable(bool available) => _client.Available = available;

        public override Task RecheckAsync(CancellationToken cancellationToken) =>
            _engine.RefreshAsync(cancellationToken);

        protected override async Task OpenConversationAsync(CancellationToken cancellationToken)
        {
            await _engine.InitializeAsync(cancellationToken);
            await _engine.StartConversationAsync(cancellationToken);
        }

        protected override async Task PlayTurnCoreAsync(
            string prompt,
            ChatTurnScript script,
            CancellationTokenSource caller,
            CancellationToken cancellationToken
        )
        {
            _client.Next = (script, caller);
            await _engine.SendAsync(prompt, script.Attachments, caller.Token);
        }

        protected override Task WhenTurnOpenAsync(CancellationToken cancellationToken) =>
            _client.Opened.Task.WaitAsync(cancellationToken);

        /// <summary>MCP の入口でツールを呼び、戻り値を記録する</summary>
        internal void ExecuteTool(ChatStep.ToolCall call)
        {
            var (result, success) = _engine.ExecuteTool(call.Name, call.ArgumentsJson);
            _toolReturns.Add(new ChatToolReturn(null, result, success));
        }

        /// <summary>利用者の中断（台本の <see cref="ChatTurnEnding.UserInterrupts"/>）</summary>
        internal void Interrupt() => _engine.InterruptAsync().GetAwaiter().GetResult();
    }

    /// <summary>台本どおりにターンを流す偽の Claude Code クライアント（プロセスを持たない）</summary>
    private sealed class ScriptedClaudeCodeClient(Rig rig) : IClaudeCodeClient
    {
        /// <summary>次のターンの台本と呼び出し元のトークン</summary>
        public (ChatTurnScript Script, CancellationTokenSource Caller)? Next { get; set; }

        /// <summary>終わらないターンが待ちに入った合図</summary>
        public TaskCompletionSource Opened { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>中断の要求の数</summary>
        public int InterruptCount { get; private set; }

        /// <summary>claude が見つかるか</summary>
        public bool Available { get; set; } = true;

        /// <summary>流れたターンのプロンプト（添付のパスを付記した後のもの）</summary>
        public List<string> Prompts { get; } = [];

        /// <summary>新しい会話で流れたターンの数（直前に返したセッション Id を resume に渡さなかったターン）</summary>
        public int ConversationsOpened { get; private set; }

        /// <summary>ターンが始まる前に作業フォルダの attachments へ書き出された添付の数（ターンの合計）</summary>
        public int AttachmentsReceived { get; private set; }

        /// <summary>最後のターンで渡された作業フォルダ</summary>
        public string? LastWorkingDirectory { get; private set; }

        /// <summary>破棄されたか</summary>
        public bool Disposed { get; private set; }

        private string? _lastSessionId;
        private int _sessionNumber;
        private int _attachmentFiles;

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
            var (script, caller) =
                Next ?? throw new InvalidOperationException("台本の無いターンが流れた");
            Next = null;
            Prompts.Add(prompt);
            LastWorkingDirectory = options.WorkingDirectory;

            if (resumeSessionId is null || resumeSessionId != _lastSessionId)
            {
                ConversationsOpened++;
            }

            var attachments = Path.Combine(options.WorkingDirectory, "attachments");
            var files = Directory.Exists(attachments) ? Directory.GetFiles(attachments).Length : 0;
            AttachmentsReceived += files - _attachmentFiles;
            _attachmentFiles = files;

            foreach (var step in script.Steps)
            {
                switch (step)
                {
                    case ChatStep.Delta delta:
                        onAssistantText(delta.Text);
                        break;

                    case ChatStep.ToolCall call:
                        rig.ExecuteTool(call);
                        break;
                }
            }

            switch (script.Ending)
            {
                case ChatTurnEnding.ReportedFailure failure:
                    return new ClaudeCodeTurnOutcome(false, failure.Detail, null, false);

                case ChatTurnEnding.Throws throws:
                    throw throws.Exception;

                case ChatTurnEnding.CallerCancels:
                    caller.Cancel();
                    throw new OperationCanceledException(cancellationToken);

                case ChatTurnEnding.ForeignCancellation foreign:
                    throw foreign.Exception;

                case ChatTurnEnding.UserInterrupts interrupts:
                    // 元のテストと同じく、中断を受けたクライアントは理由の無い失敗を返すか、取り消しの例外を投げる
                    rig.Interrupt();

                    if (interrupts.BackendThrows)
                    {
                        throw new OperationCanceledException(cancellationToken);
                    }

                    return new ClaudeCodeTurnOutcome(false, null, null, false);

                case ChatTurnEnding.Open:
                    Opened.TrySetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    break;
            }

            _lastSessionId = $"s{++_sessionNumber}";
            return new ClaudeCodeTurnOutcome(true, null, _lastSessionId, false);
        }

        public void Interrupt() => InterruptCount++;

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
