using QuickER.AI;
using QuickER.AI.Chat;
using QuickER.AI.Resources;

namespace QuickER.Tests.AI.ChatEngines;

/// <summary>Copilot 接続のアダプタ（<see cref="CopilotChatEngine"/>＋<see cref="FakeCopilotRuntimeClient"/>）</summary>
internal sealed class CopilotChatEngineAdapter : ChatEngineAdapter
{
    /// <inheritdoc/>
    public override ErChatBackendKind Kind => ErChatBackendKind.Copilot;

    /// <inheritdoc/>
    public override IReadOnlyDictionary<
        ChatEngineCapability,
        ChatEngineDeclaration
    > Declarations { get; } =
        new Dictionary<ChatEngineCapability, ChatEngineDeclaration>
        {
            [ChatEngineCapability.AsyncCompletion] = new(
                true,
                "src/QuickER.AI/CopilotChatEngine.cs OnSessionIdle（アイドル復帰をターン完了へ変換する。ターン外のアイドルは無視する）"
            ),
            [ChatEngineCapability.TurnRunsInsideSend] = new(
                false,
                "src/QuickER.AI/CopilotChatEngine.cs OnSessionIdle（完了はランタイムのアイドル通知で届く）"
            ),
            [ChatEngineCapability.ToolResponseChannel] = new(
                true,
                "src/QuickER.AI/CopilotChatEngine.cs ExecuteAndRespondAsync（RespondToToolCallAsync で結果を返し、送信の失敗は Copilot_ToolResponseSendFailed で知らせる）"
            ),
            [ChatEngineCapability.OptionalToolHost] = new(
                true,
                "src/QuickER.AI/CopilotChatEngine.cs のコンストラクタ（toolHost は IErDiagramToolHost?）"
            ),
            [ChatEngineCapability.ReportedFailure] = new(
                true,
                "src/QuickER.AI/CopilotChatEngine.cs（SessionErrorReceived のセッションエラーを要約＋詳細の失敗で終える）"
            ),
            [ChatEngineCapability.ToolReturnCarriesSuccess] = new(
                true,
                "src/QuickER.AI/ICopilotRuntimeClient.cs RespondToToolCallAsync（requestId・result・success を返す）"
            ),
            [ChatEngineCapability.CliLifecycle] = new(
                true,
                "src/QuickER.AI/CopilotChatEngine.cs InstallGuidance（copilot が見つからなければインストールを案内する）・RefreshAsync（再確認）"
            ),
            [ChatEngineCapability.ExposesCliMissing] = new(
                true,
                "src/QuickER.AI/CopilotChatEngine.cs IsCliMissing"
            ),
            [ChatEngineCapability.LaunchesRuntime] = new(
                true,
                "src/QuickER.AI/CopilotChatEngine.cs ConnectAsync（copilot ランタイムを起動して接続する。重ねた接続は SingleFlight で 1 回）"
            ),
            [ChatEngineCapability.ConnectionCanDrop] = new(
                true,
                "src/QuickER.AI/CopilotChatEngine.cs（接続が切れたら次の送信で接続し直して会話を開き直す）"
            ),
            [ChatEngineCapability.OwnsBackend] = new(
                true,
                "src/QuickER.AI/CopilotChatEngine.cs DisposeAsync（クライアントを破棄する）"
            ),
            [ChatEngineCapability.CreatesWorkingDirectory] = new(
                false,
                "src/QuickER.AI/CopilotRuntimeClient.cs StartAsync（作業フォルダはエンジンでなく実クライアントが作る）"
            ),
            [ChatEngineCapability.AcceptsAttachments] = new(
                true,
                "src/QuickER.AI/ChatAttachment.cs AttachmentSupportResolver.Copilot（画像だけ）"
            ),
            [ChatEngineCapability.RefusesSomeAttachmentKind] = new(
                true,
                "src/QuickER.AI/ChatAttachment.cs AttachmentSupportResolver.Copilot（画像以外は受け付けない）"
            ),
            [ChatEngineCapability.RefusesAttachmentAsTurnFailure] = new(
                false,
                "src/QuickER.AI/CopilotChatEngine.cs（画像以外の添付を分かる例外で弾く＝NotSupportedException）"
            ),
        };

    /// <inheritdoc/>
    public override AttachmentSupport ExpectedAttachmentSupport => AttachmentSupport.Images;

    /// <inheritdoc/>
    public override string ExpectedFailureFromException(Exception exception) =>
        SummaryWithDetail(Strings.Copilot_TurnFailed, exception.Message);

    /// <inheritdoc/>
    public override string ExpectedReportedFailure(string? detail) =>
        SummaryWithDetail(Strings.Copilot_TurnFailed, detail);

    /// <inheritdoc/>
    public override string ExpectedToolResponseSendFailed(string reason) =>
        string.Format(Strings.Copilot_ToolResponseSendFailed, reason);

    /// <inheritdoc/>
    public override string ExpectedCliMissingSummary => Strings.Copilot_Status_NotFound;

    /// <inheritdoc/>
    public override string ExpectedInstallGuidance => Strings.Copilot_Guidance_Install;

    /// <inheritdoc/>
    public override ChatEngineRig CreateRig(ChatToolHostKind hostKind) => new Rig(hostKind);

    /// <summary>Copilot 接続の組</summary>
    private sealed class Rig : ChatEngineRig
    {
        private readonly FakeCopilotRuntimeClient _client = new();
        private readonly CopilotChatEngine _engine;

        public Rig(ChatToolHostKind hostKind)
            : base(hostKind)
        {
            _engine = new CopilotChatEngine(
                _client,
                ToolHost,
                new ChatSyncDispatcher(),
                ErDesignProfile.ErDesign
            );
            Attach();
        }

        public override IErChatEngine Engine => _engine;

        public override IReadOnlyList<ChatToolReturn> ToolReturns =>
            _client
                .ToolResponses.Select(response => new ChatToolReturn(
                    response.RequestId,
                    response.Result,
                    response.Success
                ))
                .ToList();

        public override int InterruptsReceived => _client.AbortCallCount;

        // 元のテストと同じく、接続して会話を開いてからツールの要求を流す
        public override Task PrepareForToolsAsync(CancellationToken cancellationToken) =>
            StartConversationAsync(cancellationToken);

        protected override Task RaiseToolRequestAsync(
            ChatStep.ToolCall call,
            CancellationToken cancellationToken
        )
        {
            _client.RaiseToolCall(call.Id, call.Name, call.ArgumentsJson);
            return Task.CompletedTask;
        }

        public override void RaiseStrayCompletion() => _client.RaiseIdle();

        public override void FailToolResponses(Exception exception) =>
            _client.RespondToolException = exception;

        public override IReadOnlyList<string> PromptsReceived =>
            _client.Sends.Select(send => send.Prompt).ToList();

        public override int ConversationsOpened => _client.StartSessionCallCount;

        public override int AttachmentsReceived => _client.Sends.Sum(send => send.AttachmentCount);

        public override void LoseConnection() => _client.SimulateConnectionLost();

        public override bool BackendDisposed => _client.Disposed;

        public override ChatReadiness Readiness =>
            new(
                _engine.IsReady,
                _engine.IsCliMissing,
                _engine.StatusLevel,
                _engine.StatusSummary,
                _engine.Guidance,
                _client.IsStarted
            );

        public override void SetCliAvailable(bool available) => _client.Available = available;

        public override void FailRuntimeStart(Exception? exception) =>
            _client.StartError = exception;

        public override void GateRuntimeStart(Task? gate) => _client.StartGate = gate;

        // フェイクは成功した起動だけを数える
        public override int RuntimeStarts => _client.StartCallCount;

        public override Task RecheckAsync(CancellationToken cancellationToken) =>
            _engine.RefreshAsync(cancellationToken);

        protected override async Task OpenConversationAsync(CancellationToken cancellationToken)
        {
            await _engine.InitializeAsync(cancellationToken);
            await _engine.StartConversationAsync(cancellationToken);
        }

        // 元のテストと同じく、会話を開かずに送るときも接続はしておく
        protected override Task PrepareSendAsync(CancellationToken cancellationToken) =>
            _engine.InitializeAsync(cancellationToken);

        protected override async Task PlayTurnCoreAsync(
            string prompt,
            ChatTurnScript script,
            CancellationTokenSource caller,
            CancellationToken cancellationToken
        )
        {
            if (script.Ending.FailsSend)
            {
                // 元のテストと同じく、送信の中（DuringSend）で例外を投げる。呼び出し元の取り消しは
                // トークンを立ててからトークンを持たない取り消しの例外を投げる
                _client.DuringSend = () =>
                {
                    RaiseSteps(script.Steps);
                    throw script.Ending switch
                    {
                        ChatTurnEnding.Throws throws => throws.Exception,
                        ChatTurnEnding.ForeignCancellation foreign => foreign.Exception,
                        _ => CancelCaller(caller),
                    };
                };
                await _engine.SendAsync(prompt, script.Attachments, caller.Token);
                _client.DuringSend = null;
                return;
            }

            await _engine.SendAsync(prompt, script.Attachments, caller.Token);
            RaiseSteps(script.Steps);

            switch (script.Ending)
            {
                case ChatTurnEnding.Success:
                    _client.RaiseIdle();
                    break;

                case ChatTurnEnding.ReportedFailure failure:
                    // ランタイムはセッションエラーの後にもアイドルを返す（完了は 1 回だけ）
                    _client.RaiseError(failure.Detail ?? string.Empty);
                    _client.RaiseIdle();
                    break;

                case ChatTurnEnding.UserInterrupts:
                    _client.RaiseIdle(aborted: true);
                    break;
            }
        }

        private static OperationCanceledException CancelCaller(CancellationTokenSource caller)
        {
            caller.Cancel();
            return new OperationCanceledException();
        }

        private void RaiseSteps(IReadOnlyList<ChatStep> steps)
        {
            foreach (var step in steps)
            {
                switch (step)
                {
                    case ChatStep.Delta delta:
                        _client.RaiseDelta(delta.Text);
                        break;

                    case ChatStep.ToolCall call:
                        _client.RaiseToolCall(call.Id, call.Name, call.ArgumentsJson);
                        break;
                }
            }
        }
    }
}
