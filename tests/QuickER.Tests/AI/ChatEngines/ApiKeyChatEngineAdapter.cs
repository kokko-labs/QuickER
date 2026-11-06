using QuickER.AI;
using QuickER.AI.Chat;

namespace QuickER.Tests.AI.ChatEngines;

/// <summary>API キー接続のアダプタ（<see cref="ChatTurnEngine"/>＋台本どおりに応答するドライバ）</summary>
/// <remarks>
/// ドライバは中立（Anthropic / OpenAI の違いはドライバの表が持つ）。添付の範囲は合成ルートが注入するので、
/// 元のテスト（AttachmentSupportMatrixTests）と同じく OpenAI の規則 <see cref="AttachmentSupportResolver.ForApiKeyProvider"/> を渡す。
/// </remarks>
internal sealed class ApiKeyChatEngineAdapter : ChatEngineAdapter
{
    /// <inheritdoc/>
    public override ErChatBackendKind Kind => ErChatBackendKind.ApiKey;

    /// <inheritdoc/>
    public override IReadOnlyDictionary<
        ChatEngineCapability,
        ChatEngineDeclaration
    > Declarations { get; } =
        new Dictionary<ChatEngineCapability, ChatEngineDeclaration>
        {
            [ChatEngineCapability.AsyncCompletion] = new(
                false,
                "src/QuickER.AI/ChatTurnEngine.cs SendAsync（RunAgenticLoopAsync の後に TurnCompleted を出す。完了を運ぶ別の通知は無い）"
            ),
            [ChatEngineCapability.TurnRunsInsideSend] = new(
                true,
                "src/QuickER.AI/ChatTurnEngine.cs SendAsync（ツール呼び出しループ全体を送信の中で回す）"
            ),
            [ChatEngineCapability.ToolResponseChannel] = new(
                false,
                "src/QuickER.AI/ChatTurnEngine.cs（ツールの結果は履歴へ積み、次のドライバ呼び出しで再送する。別の送信は無い）"
            ),
            [ChatEngineCapability.OptionalToolHost] = new(
                false,
                "src/QuickER.AI/ChatTurnEngine.cs のコンストラクタ（toolHost は null 許容でない IErDiagramToolHost）"
            ),
            [ChatEngineCapability.ReportedFailure] = new(
                false,
                "src/QuickER.AI/ChatTurnEngine.cs IChatTurnDriver（ドライバは応答を返すか例外を投げる。失敗を報告する別の経路は無い）"
            ),
            [ChatEngineCapability.ToolReturnCarriesSuccess] = new(
                false,
                "src/QuickER.AI/ChatTurnEngine.cs ChatHistoryItem（ツールの結果の履歴項目はテキストと ToolCallId だけを持つ）"
            ),
            [ChatEngineCapability.CliLifecycle] = new(
                false,
                "src/QuickER.AI/ChatTurnEngine.cs InitializeAsync（何もしない。送信の可否はコンストラクタで注入した isReady で決まる）"
            ),
            [ChatEngineCapability.ExposesCliMissing] = new(
                false,
                "src/QuickER.AI/ChatTurnEngine.cs（CLI を使わないので、見つからない印を持たない）"
            ),
            [ChatEngineCapability.LaunchesRuntime] = new(
                false,
                "src/QuickER.AI/ChatTurnEngine.cs InitializeAsync（常駐プロセスを起動しない）"
            ),
            [ChatEngineCapability.ConnectionCanDrop] = new(
                false,
                "src/QuickER.AI/ChatTurnEngine.cs（ターンごとにドライバを呼び、保つ接続を持たない）"
            ),
            [ChatEngineCapability.OwnsBackend] = new(
                false,
                "src/QuickER.AI/ChatTurnEngine.cs DisposeAsync（実行中のターンを取り消すだけで、破棄するクライアントを持たない）"
            ),
            [ChatEngineCapability.CreatesWorkingDirectory] = new(
                false,
                "src/QuickER.AI/BackendWorkingDirectory.cs（作業フォルダは CLI バックエンド〔Codex・Claude Code・Copilot〕のもの）"
            ),
            [ChatEngineCapability.AcceptsAttachments] = new(
                true,
                "src/QuickER.AI/ChatAttachment.cs AttachmentSupportResolver.ForApiKeyProvider（OpenAI は画像とテキスト）"
            ),
            [ChatEngineCapability.RefusesSomeAttachmentKind] = new(
                true,
                "src/QuickER.AI/ChatAttachment.cs AttachmentSupportResolver.ForApiKeyProvider（OpenAI は PDF とバイナリを受け付けない）"
            ),
            [ChatEngineCapability.RefusesAttachmentAsTurnFailure] = new(
                true,
                "src/QuickER.AI/ChatTurnEngine.cs SendAsync（サポート外種別の添付を分かる失敗として弾く＝Chat_UnsupportedAttachment の TurnCompleted）"
            ),
        };

    /// <inheritdoc/>
    public override AttachmentSupport ExpectedAttachmentSupport =>
        AttachmentSupport.Images | AttachmentSupport.Text;

    /// <inheritdoc/>
    public override string ExpectedFailureFromException(Exception exception) =>
        AiErrorMessageLocalizer.ToUserMessage(exception);

    /// <inheritdoc/>
    public override ChatEngineRig CreateRig(ChatToolHostKind hostKind) => new Rig(hostKind);

    /// <summary>API キー接続の組</summary>
    private sealed class Rig : ChatEngineRig
    {
        private readonly ScriptedTurnDriver _driver;
        private readonly ChatTurnEngine _engine;

        public Rig(ChatToolHostKind hostKind)
            : base(hostKind)
        {
            _driver = new ScriptedTurnDriver(this);

            // ChatTurnEngine はツールホストを必須とする（ホスト無しの行はこのエンジンでは流さない）。
            // ホストを渡さない行でも呼ばれない記録用のホストを渡す
            _engine = new ChatTurnEngine(
                _driver,
                ToolHost ?? new RecordingToolHost(),
                new ChatSyncDispatcher(),
                () => true,
                ErDesignProfile.ErDesign,
                attachmentSupport: () =>
                    AttachmentSupportResolver.ForApiKeyProvider(AiProvider.OpenAI)
            );
            Attach();
        }

        public override IErChatEngine Engine => _engine;

        public override IReadOnlyList<ChatToolReturn> ToolReturns =>
            _driver
                .LastHistory.Where(item => item.Role == ChatHistoryRole.Tool)
                .Select(item => new ChatToolReturn(item.ToolCallId, item.Text, null))
                .ToList();

        public override int InterruptsReceived => _driver.CancellationsObserved;

        public override IReadOnlyList<string> PromptsReceived =>
            _driver
                .TurnHistories.Select(history =>
                    history.Last(item => item.Role == ChatHistoryRole.User).Text
                )
                .ToList();

        // 会話の開始は履歴を system だけへ戻す。新しい会話の最初のターンは、利用者の発話を 1 つだけ持つ履歴で届く
        public override int ConversationsOpened =>
            _driver.TurnHistories.Count(history =>
                history.Count(item => item.Role == ChatHistoryRole.User) == 1
            );

        public override int AttachmentsReceived =>
            _driver.TurnHistories.Sum(history =>
                history.LastOrDefault(item => item.Role == ChatHistoryRole.User)?.Attachments?.Count
                ?? 0
            );

        public override Task PrepareForToolsAsync(CancellationToken cancellationToken) =>
            StartConversationAsync(cancellationToken);

        // ツールの要求はドライバの応答として届く（元のテストと同じく、ツールを要求する応答→完了の応答の 2 回）
        protected override Task RaiseToolRequestAsync(
            ChatStep.ToolCall call,
            CancellationToken cancellationToken
        ) =>
            PlayTurnAsync(
                "tool",
                ChatTurnScript.Of(new ChatTurnEnding.Success(), call),
                cancellationToken
            );

        protected override Task OpenConversationAsync(CancellationToken cancellationToken) =>
            _engine.StartConversationAsync(cancellationToken);

        protected override async Task PlayTurnCoreAsync(
            string prompt,
            ChatTurnScript script,
            CancellationTokenSource caller,
            CancellationToken cancellationToken
        )
        {
            _driver.Load(script, caller);
            await _engine.SendAsync(prompt, script.Attachments, caller.Token);
        }

        protected override Task WhenTurnOpenAsync(CancellationToken cancellationToken) =>
            _driver.Opened.Task.WaitAsync(cancellationToken);

        /// <summary>利用者の中断（台本の <see cref="ChatTurnEnding.UserInterrupts"/>）</summary>
        internal void Interrupt() => _engine.InterruptAsync().GetAwaiter().GetResult();
    }

    /// <summary>台本を応答の列へ訳して返すドライバ</summary>
    /// <remarks>
    /// 差分はその応答の中で 1 手ずつ <c>onTextDelta</c> へ流す。ツールの呼び出しは続く限り 1 つの応答にまとめ、
    /// その後の手は次の応答に置く。最後の応答で終わり方を演じる。
    /// </remarks>
    private sealed class ScriptedTurnDriver(Rig rig) : IChatTurnDriver
    {
        private readonly Queue<IReadOnlyList<ChatStep>> _responses = new();
        private ChatTurnEnding _ending = new ChatTurnEnding.Success();
        private CancellationTokenSource? _caller;
        private bool _firstCallOfTurn;

        /// <summary>最後の呼び出しで受け取った履歴</summary>
        public IReadOnlyList<ChatHistoryItem> LastHistory { get; private set; } = [];

        /// <summary>ターンごとの最初の呼び出しで受け取った履歴（届いた送信）</summary>
        public List<IReadOnlyList<ChatHistoryItem>> TurnHistories { get; } = [];

        /// <summary>終わらないターンが待ちに入った合図</summary>
        public TaskCompletionSource Opened { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>待ちの間にトークンの取り消しを受けた数（バックエンドへ届いた中断）</summary>
        public int CancellationsObserved { get; private set; }

        /// <summary>1 ターンの台本を読み込む</summary>
        public void Load(ChatTurnScript script, CancellationTokenSource caller)
        {
            _responses.Clear();
            var current = new List<ChatStep>();

            foreach (var step in script.Steps)
            {
                if (step is not ChatStep.ToolCall && current.OfType<ChatStep.ToolCall>().Any())
                {
                    _responses.Enqueue(current);
                    current = [];
                }

                current.Add(step);
            }

            if (current.OfType<ChatStep.ToolCall>().Any())
            {
                _responses.Enqueue(current);
                current = [];
            }

            _responses.Enqueue(current);
            _ending = script.Ending;
            _caller = caller;
            _firstCallOfTurn = true;
        }

        public async Task<ChatAssistantTurn> RunAsync(
            IReadOnlyList<ChatHistoryItem> history,
            Action<string> onTextDelta,
            CancellationToken cancellationToken
        )
        {
            LastHistory = [.. history];

            if (_firstCallOfTurn)
            {
                _firstCallOfTurn = false;
                TurnHistories.Add(LastHistory);
            }

            var steps = _responses.Dequeue();
            var text = new System.Text.StringBuilder();

            foreach (var delta in steps.OfType<ChatStep.Delta>())
            {
                onTextDelta(delta.Text);
                text.Append(delta.Text);
            }

            var calls = steps
                .OfType<ChatStep.ToolCall>()
                .Select(call => new ChatToolCallRequest(call.Id, call.Name, call.ArgumentsJson))
                .ToList();

            if (calls.Count > 0 || _responses.Count > 0)
            {
                return new ChatAssistantTurn(text.ToString(), calls);
            }

            switch (_ending)
            {
                case ChatTurnEnding.Throws throws:
                    throw throws.Exception;

                case ChatTurnEnding.CallerCancels:
                    _caller!.Cancel();
                    throw new OperationCanceledException(cancellationToken);

                case ChatTurnEnding.ForeignCancellation foreign:
                    throw foreign.Exception;

                case ChatTurnEnding.UserInterrupts:
                    // 元のテストと同じく、ドライバの中で中断を受け、そのトークンの取り消しの例外を投げる
                    // （ドライバは応答を返すか例外を投げるだけなので、理由の無い失敗を返す終え方は無い）
                    rig.Interrupt();
                    throw new OperationCanceledException(cancellationToken);

                case ChatTurnEnding.Open:
                    Opened.TrySetResult();

                    try
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        CancellationsObserved++;
                        throw;
                    }

                    break;
            }

            return new ChatAssistantTurn(text.ToString(), []);
        }
    }
}
