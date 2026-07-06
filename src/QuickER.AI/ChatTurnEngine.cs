namespace QuickER.AI;

/// <summary>会話履歴 1 項目の役割</summary>
public enum ChatHistoryRole
{
    /// <summary>システム指示</summary>
    System,

    /// <summary>ユーザー発言</summary>
    User,

    /// <summary>アシスタント応答（ツール呼び出しを含み得る）</summary>
    Assistant,

    /// <summary>ツール実行結果</summary>
    Tool,
}

/// <summary>AI が要求した 1 件のツール呼び出し</summary>
/// <param name="Id">ツール呼び出し ID（結果の対応付けに使う）</param>
/// <param name="Name">ツール名</param>
/// <param name="ArgumentsJson">引数の JSON 文字列</param>
public sealed record ChatToolCallRequest(string Id, string Name, string ArgumentsJson);

/// <summary>会話履歴の 1 項目（エンジン・プロバイダ非依存・SDK 型を含まない中立表現）</summary>
/// <param name="Role">役割</param>
/// <param name="Text">本文（無い場合は空文字）</param>
/// <param name="ToolCalls">アシスタントが要求したツール呼び出し一覧（任意）</param>
/// <param name="ToolCallId">Tool 役割時の対応するツール呼び出し ID（任意）</param>
/// <param name="Attachments">
/// User 役割に同梱された添付（画像・PDF）。API キー接続では履歴に残り毎ターン再送されるため、
/// ステートレス API でも添付付きメッセージが正しく再構築される。既定は空。
/// </param>
public sealed record ChatHistoryItem(
    ChatHistoryRole Role,
    string Text,
    IReadOnlyList<ChatToolCallRequest>? ToolCalls = null,
    string? ToolCallId = null,
    IReadOnlyList<ChatAttachment>? Attachments = null
);

/// <summary>アシスタント 1 ターンの応答（テキストと要求されたツール呼び出し）</summary>
/// <param name="Text">応答テキスト</param>
/// <param name="ToolCalls">要求されたツール呼び出し（空なら応答完了）</param>
public sealed record ChatAssistantTurn(string Text, IReadOnlyList<ChatToolCallRequest> ToolCalls);

/// <summary>会話履歴を入力に LLM を 1 回呼び出し、アシスタント応答を返す抽象（LLM 呼び出しの seam）</summary>
/// <remarks>
/// 本番は各プロバイダ向けドライバ（OpenAI/Ollama・Anthropic など）が呼ぶ。
/// テストではスクリプト化した応答を返すフェイクに差し替える。
/// </remarks>
public interface IChatTurnDriver
{
    /// <summary>会話履歴を入力にアシスタント 1 ターンを実行する。テキスト断片は <paramref name="onTextDelta"/> で逐次通知する</summary>
    Task<ChatAssistantTurn> RunAsync(
        IReadOnlyList<ChatHistoryItem> history,
        Action<string> onTextDelta,
        CancellationToken cancellationToken
    );
}

/// <summary>AI のツール呼び出しを ER 図操作へ橋渡しするホスト（本番は MainViewModel を操作する）</summary>
public interface IErDiagramToolHost
{
    /// <summary>ツールを実行し結果テキストと成否を返す</summary>
    (string Result, bool Success) Execute(string toolName, string argumentsJson);
}

/// <summary>
/// <see cref="IChatTurnDriver"/> を介して LLM を呼び出す、プロバイダ非依存の自前チャット制御エンジン。
/// 設計ルールの system プロンプトのもと、ツール呼び出しループ（応答→ツール実行→再送信）を回し、
/// ER 図を逐次操作する。ツール実行は UI スレッドへマーシャリングする。
/// </summary>
public sealed class ChatTurnEngine : IErChatEngine
{
    private readonly IChatTurnDriver _driver;
    private readonly IErDiagramToolHost _toolHost;
    private readonly IUiDispatcher _dispatcher;
    private readonly Func<bool> _isReady;
    private readonly ErChatProfile _profile;
    private readonly Func<AttachmentSupport> _attachmentSupport;
    private readonly List<ChatHistoryItem> _history = new();
    private CancellationTokenSource? _turnCts;

    /// <inheritdoc />
    public event EventHandler<string>? AssistantDeltaReceived;

    /// <inheritdoc />
    public event EventHandler<ErChatToolActivity>? ToolActivityReceived;

    /// <inheritdoc />
    public event EventHandler<ErChatTurnResult>? TurnCompleted;

    /// <inheritdoc />
    public event EventHandler<string>? StatusChanged;

    /// <summary>エンジンを生成する</summary>
    /// <param name="driver">LLM 呼び出しの seam</param>
    /// <param name="toolHost">ツール実行ホスト</param>
    /// <param name="dispatcher">UI スレッドへのマーシャリング</param>
    /// <param name="isReady">送信可能判定（API キー有無など）</param>
    /// <param name="profile">用途プロファイル（システムプロンプト等。省略時は ER 図設計）</param>
    /// <param name="attachmentSupport">
    /// 添付対応範囲を返す関数（省略時は添付非対応）。API キー接続はプロバイダー依存
    /// （Anthropic=画像＋PDF・OpenAI=画像・Ollama=なし）のため、合成ルートから注入する
    /// </param>
    public ChatTurnEngine(
        IChatTurnDriver driver,
        IErDiagramToolHost toolHost,
        IUiDispatcher dispatcher,
        Func<bool> isReady,
        ErChatProfile? profile = null,
        Func<AttachmentSupport>? attachmentSupport = null
    )
    {
        _driver = driver;
        _toolHost = toolHost;
        _dispatcher = dispatcher;
        _isReady = isReady;
        _profile = profile ?? ErChatProfile.ErDesign;
        _attachmentSupport = attachmentSupport ?? (() => AttachmentSupport.None);
    }

    /// <inheritdoc />
    public bool IsReady => _isReady();

    /// <inheritdoc />
    public AttachmentSupport AttachmentSupport => _attachmentSupport();

    /// <inheritdoc />
    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    /// <inheritdoc />
    public Task StartConversationAsync(CancellationToken cancellationToken = default)
    {
        _history.Clear();
        _history.Add(new ChatHistoryItem(ChatHistoryRole.System, _profile.BuildSystemPrompt()));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SendAsync(string prompt, CancellationToken cancellationToken = default) =>
        SendAsync(prompt, Array.Empty<ChatAttachment>(), cancellationToken);

    /// <inheritdoc />
    public async Task SendAsync(
        string prompt,
        IReadOnlyList<ChatAttachment> attachments,
        CancellationToken cancellationToken = default
    )
    {
        if (_history.Count == 0)
        {
            await StartConversationAsync(cancellationToken).ConfigureAwait(false);
        }

        // UI がゲートする前提だが、防御的にサポート外種別の添付を分かる失敗として弾く
        // （履歴を汚さないよう、User 項目を積む前に検査する）
        if (FindUnsupportedAttachment(attachments) is { } unsupported)
        {
            TurnCompleted?.Invoke(
                this,
                new ErChatTurnResult(
                    false,
                    $"この接続方式は添付「{unsupported.FileName}」（{unsupported.Kind}）に対応していません。"
                )
            );
            return;
        }

        // 添付は User 履歴項目に載せ、ステートレス API の毎ターン再送でも再構築されるようにする
        _history.Add(
            new ChatHistoryItem(
                ChatHistoryRole.User,
                prompt,
                Attachments: attachments is { Count: > 0 } ? attachments : null
            )
        );

        _turnCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _turnCts.Token;
        StatusChanged?.Invoke(this, "生成中...");

        try
        {
            await RunAgenticLoopAsync(token).ConfigureAwait(false);
            TurnCompleted?.Invoke(this, new ErChatTurnResult(true, null));
        }
        catch (OperationCanceledException)
        {
            TurnCompleted?.Invoke(this, new ErChatTurnResult(false, null));
        }
        catch (Exception ex)
        {
            TurnCompleted?.Invoke(this, new ErChatTurnResult(false, ex.Message));
        }
        finally
        {
            _turnCts?.Dispose();
            _turnCts = null;
        }
    }

    /// <summary>添付にサポート外の種別が含まれていれば、その 1 件目を返す（無ければ null）</summary>
    private ChatAttachment? FindUnsupportedAttachment(IReadOnlyList<ChatAttachment> attachments)
    {
        if (attachments is not { Count: > 0 })
        {
            return null;
        }

        var support = AttachmentSupport;
        return attachments.FirstOrDefault(a => !support.Allows(a.Kind));
    }

    /// <summary>応答→ツール実行→再送信のループを、ツール要求が無くなるまで回す</summary>
    private async Task RunAgenticLoopAsync(CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();

            var turn = await _driver
                .RunAsync(_history, delta => AssistantDeltaReceived?.Invoke(this, delta), token)
                .ConfigureAwait(false);
            _history.Add(new ChatHistoryItem(ChatHistoryRole.Assistant, turn.Text, turn.ToolCalls));

            if (turn.ToolCalls.Count == 0)
            {
                return;
            }

            foreach (var call in turn.ToolCalls)
            {
                token.ThrowIfCancellationRequested();

                // ER 図操作（ObservableCollection 変更）は UI スレッドで実行する
                var (result, success) = _dispatcher.Invoke(() =>
                    _toolHost.Execute(call.Name, call.ArgumentsJson)
                );
                ToolActivityReceived?.Invoke(
                    this,
                    new ErChatToolActivity(call.Name, result, success)
                );
                _history.Add(
                    new ChatHistoryItem(ChatHistoryRole.Tool, result, ToolCallId: call.Id)
                );
            }
        }
    }

    /// <inheritdoc />
    public Task InterruptAsync(CancellationToken cancellationToken = default)
    {
        _turnCts?.Cancel();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _turnCts?.Cancel();
        _turnCts?.Dispose();
        return ValueTask.CompletedTask;
    }
}
