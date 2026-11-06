using System.Text.Json;
using QuickER.AI;
using QuickER.AI.Chat;
using QuickER.AI.Resources;

namespace QuickER.Tests.AI.ChatEngines;

/// <summary>Codex 接続のアダプタ（<see cref="CodexChatEngine"/>＋<see cref="FakeCodexAppServerClient"/>）</summary>
internal sealed class CodexChatEngineAdapter : ChatEngineAdapter
{
    /// <inheritdoc/>
    public override ErChatBackendKind Kind => ErChatBackendKind.Codex;

    /// <inheritdoc/>
    public override IReadOnlyDictionary<
        ChatEngineCapability,
        ChatEngineDeclaration
    > Declarations { get; } =
        new Dictionary<ChatEngineCapability, ChatEngineDeclaration>
        {
            [ChatEngineCapability.AsyncCompletion] = new(
                true,
                "src/QuickER.AI/CodexChatEngine.cs TryCompleteTurn（完了通知の発火点は受信ループの turn/completed・error 通知・接続断）"
            ),
            [ChatEngineCapability.TurnRunsInsideSend] = new(
                false,
                "src/QuickER.AI/CodexChatEngine.cs SendAsync（StartTurnAsync で始め、完了は turn/completed の通知で届く）"
            ),
            [ChatEngineCapability.ToolResponseChannel] = new(
                true,
                "src/QuickER.AI/CodexChatEngine.cs ExecuteAndRespondAsync（RespondToDynamicToolCallAsync で結果を返し、送信の失敗は Codex_ToolResponseSendFailed で知らせる）"
            ),
            [ChatEngineCapability.OptionalToolHost] = new(
                true,
                "src/QuickER.AI/CodexChatEngine.cs のコンストラクタ（toolHost は IErDiagramToolHost?）"
            ),
            [ChatEngineCapability.ReportedFailure] = new(
                true,
                "src/QuickER.AI/CodexChatEngine.cs BuildTurnFailedMessage（App Server が報告した文を詳細に添える）"
            ),
            [ChatEngineCapability.ToolReturnCarriesSuccess] = new(
                true,
                "src/QuickER.AI/CodexChatEngine.cs ExecuteAndRespondAsync（RespondToDynamicToolCallAsync へ resultText と success を渡す）"
            ),
            [ChatEngineCapability.CliLifecycle] = new(
                true,
                "src/QuickER.AI/CodexChatEngine.cs ConnectAsync（codex が見つからなければ IsCliMissing と Codex_Guidance_Install）・RefreshAccountStateAsync（再確認）"
            ),
            [ChatEngineCapability.ExposesCliMissing] = new(
                true,
                "src/QuickER.AI/CodexChatEngine.cs IsCliMissing"
            ),
            [ChatEngineCapability.LaunchesRuntime] = new(
                true,
                "src/QuickER.AI/CodexChatEngine.cs ConnectAsync（codex app-server を起動して接続する。重ねた接続は相乗りする）"
            ),
            [ChatEngineCapability.ConnectionCanDrop] = new(
                true,
                "src/QuickER.AI/CodexChatEngine.cs OnDisconnected（スレッドを捨て、次の送信は新しい会話になる）"
            ),
            [ChatEngineCapability.OwnsBackend] = new(
                true,
                "src/QuickER.AI/CodexChatEngine.cs DisposeAsync（クライアントを破棄してから作業フォルダを消す）"
            ),
            [ChatEngineCapability.CreatesWorkingDirectory] = new(
                true,
                "src/QuickER.AI/CodexChatEngine.cs BuildThreadStartOptions（BackendWorkingDirectory.Create(\"codex\") を Cwd に据える）"
            ),
            [ChatEngineCapability.AcceptsAttachments] = new(
                false,
                "src/QuickER.AI/ChatAttachment.cs AttachmentSupportResolver.Codex（添付を受け付けない）"
            ),
            [ChatEngineCapability.RefusesSomeAttachmentKind] = new(
                true,
                "src/QuickER.AI/ChatAttachment.cs AttachmentSupportResolver.Codex（すべての種類を受け付けない）"
            ),
            [ChatEngineCapability.RefusesAttachmentAsTurnFailure] = new(
                false,
                "src/QuickER.AI/CodexChatEngine.cs SendAsync（添付付きなら分かる例外で弾く＝NotSupportedException）"
            ),
        };

    /// <inheritdoc/>
    public override AttachmentSupport ExpectedAttachmentSupport => AttachmentSupport.None;

    /// <inheritdoc/>
    public override string ExpectedFailureFromException(Exception exception) =>
        SummaryWithDetail(Strings.Codex_TurnFailed, exception.Message);

    /// <inheritdoc/>
    public override string ExpectedReportedFailure(string? detail) =>
        SummaryWithDetail(Strings.Codex_TurnFailed, detail);

    /// <inheritdoc/>
    public override string ExpectedToolResponseSendFailed(string reason) =>
        string.Format(Strings.Codex_ToolResponseSendFailed, reason);

    /// <inheritdoc/>
    public override string ExpectedCliMissingSummary => Strings.Codex_Status_NotFound;

    /// <inheritdoc/>
    public override string ExpectedInstallGuidance => Strings.Codex_Guidance_Install;

    /// <inheritdoc/>
    public override ChatEngineRig CreateRig(ChatToolHostKind hostKind) => new Rig(hostKind);

    /// <summary>Codex 接続の組</summary>
    private sealed class Rig : ChatEngineRig
    {
        private readonly FakeCodexAppServerClient _client = new();
        private readonly CodexChatEngine _engine;
        private readonly List<ChatToolReturn> _toolReturns = [];
        private int _nextRequestId = 1;

        public Rig(ChatToolHostKind hostKind)
            : base(hostKind)
        {
            _engine = new CodexChatEngine(
                _client,
                ToolHost,
                new ChatSyncDispatcher(),
                ErDesignProfile.ErDesign
            );
            Attach();
        }

        public override IErChatEngine Engine => _engine;

        public override IReadOnlyList<ChatToolReturn> ToolReturns => [.. _toolReturns];

        public override int InterruptsReceived => _client.InterruptTurnCount;

        // 元のテストと同じく、接続も会話の開始もせずにツールの要求を流す
        public override Task PrepareForToolsAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;

        protected override Task RaiseToolRequestAsync(
            ChatStep.ToolCall call,
            CancellationToken cancellationToken
        )
        {
            RaiseToolCall(call);
            return Task.CompletedTask;
        }

        public override void RaiseStrayCompletion() => _client.RaiseTurnCompleted("completed");

        public override void FailToolResponses(Exception exception) =>
            _client.RespondToolException = exception;

        public override IReadOnlyList<string> PromptsReceived => [.. _client.TurnPrompts];

        public override int ConversationsOpened => _client.StartThreadCount;

        // 添付を受け付けないので、届いた添付は常に無い（受け付ける前に例外で断る）
        public override int AttachmentsReceived => 0;

        public override void LoseConnection() => _client.RaiseDisconnected();

        public override bool BackendDisposed => _client.DisposeCount > 0;

        public override string? WorkingDirectory => _client.LastThreadStartOptions?.Cwd;

        public override ChatReadiness Readiness =>
            new(
                _engine.IsReady,
                _engine.IsCliMissing,
                null,
                _engine.AccountSummary,
                _engine.Guidance,
                _engine.IsStarted
            );

        public override void SetCliAvailable(bool available) => _client.IsCliAvailable = available;

        public override void FailRuntimeStart(Exception? exception) =>
            _client.StartException = exception;

        public override void GateRuntimeStart(Task? gate) => _client.StartGate = gate;

        // フェイクは失敗した起動も数える
        public override int RuntimeStarts => _client.StartCount;

        public override Task RecheckAsync(CancellationToken cancellationToken) =>
            _engine.RefreshAccountStateAsync(cancellationToken);

        protected override Task OpenConversationAsync(CancellationToken cancellationToken) =>
            _engine.StartConversationAsync(cancellationToken);

        protected override async Task PlayTurnCoreAsync(
            string prompt,
            ChatTurnScript script,
            CancellationTokenSource caller,
            CancellationToken cancellationToken
        )
        {
            if (script.Ending.FailsSend)
            {
                // 元のテストと同じく、ターンの開始（StartTurnAsync）で例外を投げる。呼び出し元の取り消しは
                // トークンを取り消してから、そのトークンの取り消しの例外で終える
                _client.StartTurnException = script.Ending switch
                {
                    ChatTurnEnding.Throws throws => throws.Exception,
                    ChatTurnEnding.ForeignCancellation foreign => foreign.Exception,
                    _ => CancelCaller(caller),
                };
                await _engine.SendAsync(prompt, script.Attachments, caller.Token);
                _client.StartTurnException = null;
                return;
            }

            await _engine.SendAsync(prompt, script.Attachments, caller.Token);

            foreach (var step in script.Steps)
            {
                switch (step)
                {
                    case ChatStep.Delta delta:
                        _client.RaiseAgentMessageDelta(delta.Text);
                        break;

                    case ChatStep.ToolCall call:
                        RaiseToolCall(call);
                        break;
                }
            }

            switch (script.Ending)
            {
                case ChatTurnEnding.Success:
                    _client.RaiseTurnCompleted("completed");
                    break;

                case ChatTurnEnding.ReportedFailure failure:
                    _client.RaiseTurnCompleted("failed", failure.Detail);
                    break;

                case ChatTurnEnding.UserInterrupts:
                    _client.RaiseTurnCompleted("interrupted");
                    break;
            }
        }

        private static OperationCanceledException CancelCaller(CancellationTokenSource caller)
        {
            caller.Cancel();
            return new OperationCanceledException(caller.Token);
        }

        /// <summary>ツールの要求を流し、応答が送られたら結果を記録する（フェイクは最後の応答だけを持つ）</summary>
        private void RaiseToolCall(ChatStep.ToolCall call)
        {
            var before = _client.RespondToolCount;
            using var arguments = JsonDocument.Parse(call.ArgumentsJson);

            _client.RaiseDynamicToolCall(
                new CodexDynamicToolCallRequest
                {
                    RequestId = _nextRequestId++,
                    ThreadId = "thr_test",
                    TurnId = "turn_test",
                    CallId = call.Id,
                    Tool = call.Name,
                    Arguments = arguments.RootElement.Clone(),
                }
            );

            if (_client.RespondToolCount > before)
            {
                _toolReturns.Add(
                    new ChatToolReturn(null, _client.LastToolResult!, _client.LastToolSuccess)
                );
            }
        }
    }
}
