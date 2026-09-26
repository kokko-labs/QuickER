using QuickER.AI.Resources;

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
/// 本番は各プロバイダ向けドライバ（OpenAI/ローカル LLM・Anthropic など）が呼ぶ。
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

    /// <summary>
    /// 1 ターン（<see cref="SendAsync(string, CancellationToken)"/> 1 回）内で許容するツール往復回数の上限。
    /// 1 往復（LLM 呼び出し 1 回）には複数のツール呼び出しが載り得るため、大きめの図の一括作成のような
    /// 正当な利用でも実際の往復数は数十回以内に収まる。上限の目的は進展のない繰り返し（暴走）の打ち切りで、
    /// 正当な利用を妨げないだけの大きさとして 50 とする。
    /// </summary>
    internal const int MaxToolRoundTripsPerTurn = 50;

    /// <summary>
    /// 中断・例外で未実行に終わったツール呼び出しへ補う合成結果の文言。
    /// 中断とツール実行の例外のどちらでも使うため「実行されなかった」事実だけを述べる中立な表現にする
    /// （AI がそのツールを再実行してよいかを判断する材料になるので、失敗と読める語を避ける）。
    /// ツール結果として AI（LLM API）へそのまま返る機械向け文言のため、UI 言語に関わらず英語固定とする
    /// （CodexChatEngine 等の同種の機械向けリテラルと同じ流儀）。
    /// </summary>
    private const string UnexecutedToolResultText =
        "The tool call was not executed because the turn was aborted before it ran.";

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
    /// <param name="profile">用途プロファイル（システムプロンプト等。合成ルートが明示的に指定する）</param>
    /// <param name="attachmentSupport">
    /// 添付対応範囲を返す関数（省略時は添付非対応）。API キー接続はプロバイダー依存
    /// （Anthropic=画像＋PDF・OpenAI／ローカル LLM=画像）のため、合成ルートから注入する
    /// </param>
    public ChatTurnEngine(
        IChatTurnDriver driver,
        IErDiagramToolHost toolHost,
        IUiDispatcher dispatcher,
        Func<bool> isReady,
        ErChatProfile profile,
        Func<AttachmentSupport>? attachmentSupport = null
    )
    {
        _driver = driver;
        _toolHost = toolHost;
        _dispatcher = dispatcher;
        _isReady = isReady;
        _profile = profile;
        _attachmentSupport = attachmentSupport ?? (() => AttachmentSupport.None);
    }

    /// <inheritdoc />
    public bool IsReady => _isReady();

    /// <inheritdoc />
    public AttachmentSupport AttachmentSupport => _attachmentSupport();

    /// <summary>
    /// これまでの会話履歴（User 項目）に、指定した対応範囲では扱えない添付が 1 件でも含まれるか。
    /// API キー接続はステートレスで毎ターン全履歴を再送するため、プロバイダー切替後に再送すると
    /// 対応範囲外の添付（例：Claude で送った PDF を OpenAI へ切替後）が黙って落ちる。
    /// 呼び出し側（<see cref="AiChatDialogViewModel"/>）はこれを使って切替時に告知するかを判定する。
    /// </summary>
    /// <param name="support">切替先の添付対応範囲</param>
    public bool HistoryHasAttachmentsBeyond(AttachmentSupport support) =>
        _history.Any(item =>
            item.Role == ChatHistoryRole.User
            && item.Attachments is { Count: > 0 } attachments
            && attachments.Any(a => !support.Allows(a.Kind))
        );

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
                    string.Format(
                        Strings.Chat_UnsupportedAttachment,
                        unsupported.FileName,
                        unsupported.Kind
                    )
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
        StatusChanged?.Invoke(this, Strings.Chat_Generating);

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

    /// <summary>
    /// 応答→ツール実行→再送信のループを、ツール要求が無くなるまで回す。
    /// 中断（<see cref="InterruptAsync"/>）や個々のツール実行の例外で途中終了した場合は、
    /// 直前に積んだ Assistant 項目の ToolCalls のうち未実行の呼び出しへ合成 Tool 項目を補い、
    /// 履歴の tool_use↔tool 結果対応を保ってから例外を再送出する
    /// （API キー接続はステートレスで毎ターン全履歴を再送するため、対応が崩れると
    /// 以後の送信が LLM API の構造検証で失敗し続け「新しい会話」まで回復しない）。
    /// 往復数が <see cref="MaxToolRoundTripsPerTurn"/> を超えると、暴走（進展のない繰り返し）とみなして
    /// 打ち切る。
    /// </summary>
    /// <summary>
    /// ツールを 1 件実行する（ツール側の例外は「失敗のツール結果」へ畳んでターンを続ける）
    /// </summary>
    /// <remarks>
    /// <para>
    /// 例外をそのまま伝播させると、1 件の失敗でターン全体が失敗し、後続のツール呼び出しが実行されない。
    /// Claude Code / Codex / Copilot の 3 エンジンは同じ形で失敗結果を返して続行するため、ここも揃える
    /// （ツール結果は AI へ返る機械向け文言のため英語で固定する）。
    /// </para>
    /// <para>
    /// 中断（<see cref="OperationCanceledException"/>）だけは通す。中断はターンの中止であって
    /// ツールの失敗ではなく、未実行分を合成結果で埋める呼び出し側の経路（C1）へ渡す必要がある。
    /// </para>
    /// </remarks>
    private (string Result, bool Success) ExecuteTool(ChatToolCallRequest call)
    {
        try
        {
            return _dispatcher.Invoke(() => _toolHost.Execute(call.Name, call.ArgumentsJson));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ($"The tool '{call.Name}' threw an exception: {ex.Message}", false);
        }
    }

    private async Task RunAgenticLoopAsync(CancellationToken token)
    {
        var roundTrips = 0;

        while (true)
        {
            token.ThrowIfCancellationRequested();

            // 上限判定はドライバ呼び出しの「前」（＝次の往復を駆動する前）に行う。
            // こうすると打ち切り時点の履歴は常に「直前の tool_use すべてに tool 結果が揃った」整合状態になり、
            // 未実行呼び出しへの合成補完（中断・例外用の経路）を通らずに済む。
            roundTrips++;

            if (roundTrips > MaxToolRoundTripsPerTurn)
            {
                throw new InvalidOperationException(
                    string.Format(Strings.Chat_ToolLoopLimitReached, MaxToolRoundTripsPerTurn)
                );
            }

            var turn = await _driver
                .RunAsync(_history, delta => AssistantDeltaReceived?.Invoke(this, delta), token)
                .ConfigureAwait(false);
            _history.Add(new ChatHistoryItem(ChatHistoryRole.Assistant, turn.Text, turn.ToolCalls));

            if (turn.ToolCalls.Count == 0)
            {
                return;
            }

            var executedCallIds = new HashSet<string>();

            try
            {
                foreach (var call in turn.ToolCalls)
                {
                    token.ThrowIfCancellationRequested();

                    // ER 図操作（ObservableCollection 変更）は UI スレッドで実行する
                    var (result, success) = ExecuteTool(call);
                    ToolActivityReceived?.Invoke(
                        this,
                        new ErChatToolActivity(call.Name, result, success)
                    );
                    _history.Add(
                        new ChatHistoryItem(ChatHistoryRole.Tool, result, ToolCallId: call.Id)
                    );

                    // 「実行済み」の記録は実結果を履歴へ積んだ後に行う（間の通知購読側が例外を投げても、
                    // 実行済み扱いのまま tool 結果だけが欠ける形にしない）
                    executedCallIds.Add(call.Id);
                }
            }
            catch
            {
                // 未実行分は「実行していない」ことを AI が正しく認識できるよう合成結果で埋める。
                // 実行済みのツール活動として ToolActivityReceived は発火しない（UI 上のツール活動表示は実行結果専用のため）
                foreach (var call in turn.ToolCalls)
                {
                    if (executedCallIds.Contains(call.Id))
                    {
                        continue;
                    }

                    _history.Add(
                        new ChatHistoryItem(
                            ChatHistoryRole.Tool,
                            UnexecutedToolResultText,
                            ToolCallId: call.Id
                        )
                    );
                }

                throw;
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
