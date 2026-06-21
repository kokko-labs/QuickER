namespace ERDesigner.Services.Chat;

/// <summary>会話履歴 1 項目の役割</summary>
public enum OpenAiChatRole
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
public sealed record OpenAiToolCallRequest(string Id, string Name, string ArgumentsJson);

/// <summary>会話履歴の 1 項目（エンジン非依存・SDK 型を含まない中立表現）</summary>
/// <param name="Role">役割</param>
/// <param name="Text">本文（無い場合は空文字）</param>
/// <param name="ToolCalls">アシスタントが要求したツール呼び出し一覧（任意）</param>
/// <param name="ToolCallId">Tool 役割時の対応するツール呼び出し ID（任意）</param>
public sealed record OpenAiChatHistoryItem(
    OpenAiChatRole Role,
    string Text,
    IReadOnlyList<OpenAiToolCallRequest>? ToolCalls = null,
    string? ToolCallId = null
);

/// <summary>アシスタント 1 ターンの応答（テキストと要求されたツール呼び出し）</summary>
/// <param name="Text">応答テキスト</param>
/// <param name="ToolCalls">要求されたツール呼び出し（空なら応答完了）</param>
public sealed record OpenAiAssistantTurn(
    string Text,
    IReadOnlyList<OpenAiToolCallRequest> ToolCalls
);

/// <summary>会話履歴を入力に LLM を 1 回呼び出し、アシスタント応答を返す抽象（LLM 呼び出しの seam）</summary>
/// <remarks>本番は OpenAI SDK のストリーミングを呼ぶ。テストではスクリプト化した応答を返すフェイクに差し替える</remarks>
public interface IOpenAiTurnDriver
{
    /// <summary>会話履歴を入力にアシスタント 1 ターンを実行する。テキスト断片は <paramref name="onTextDelta"/> で逐次通知する</summary>
    Task<OpenAiAssistantTurn> RunAsync(
        IReadOnlyList<OpenAiChatHistoryItem> history,
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
/// OpenAI SDK の Function Calling を用いた自前チャット制御エンジン。
/// 設計ルールの system プロンプトのもと、ツール呼び出しループ（応答→ツール実行→再送信）を回し、
/// ER 図を逐次操作する。ツール実行は UI スレッドへマーシャリングする。
/// </summary>
public sealed class OpenAiChatEngine : IErChatEngine
{
    private readonly IOpenAiTurnDriver _driver;
    private readonly IErDiagramToolHost _toolHost;
    private readonly IUiDispatcher _dispatcher;
    private readonly Func<bool> _isReady;
    private readonly List<OpenAiChatHistoryItem> _history = new();
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
    public OpenAiChatEngine(
        IOpenAiTurnDriver driver,
        IErDiagramToolHost toolHost,
        IUiDispatcher dispatcher,
        Func<bool> isReady
    )
    {
        _driver = driver;
        _toolHost = toolHost;
        _dispatcher = dispatcher;
        _isReady = isReady;
    }

    /// <inheritdoc />
    public bool IsReady => _isReady();

    /// <inheritdoc />
    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    /// <inheritdoc />
    public Task StartConversationAsync(CancellationToken cancellationToken = default)
    {
        _history.Clear();
        _history.Add(
            new OpenAiChatHistoryItem(
                OpenAiChatRole.System,
                ErDesignRules.BuildOpenAiChatSystemPrompt()
            )
        );
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task SendAsync(string prompt, CancellationToken cancellationToken = default)
    {
        if (_history.Count == 0)
        {
            await StartConversationAsync(cancellationToken).ConfigureAwait(false);
        }

        _history.Add(new OpenAiChatHistoryItem(OpenAiChatRole.User, prompt));

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

    /// <summary>応答→ツール実行→再送信のループを、ツール要求が無くなるまで回す</summary>
    private async Task RunAgenticLoopAsync(CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();

            var turn = await _driver
                .RunAsync(_history, delta => AssistantDeltaReceived?.Invoke(this, delta), token)
                .ConfigureAwait(false);
            _history.Add(
                new OpenAiChatHistoryItem(OpenAiChatRole.Assistant, turn.Text, turn.ToolCalls)
            );

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
                    new OpenAiChatHistoryItem(OpenAiChatRole.Tool, result, ToolCallId: call.Id)
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
