using QuickER.AI;

namespace QuickER.Tests.AI.ChatEngines;

/// <summary>網の行が渡すツールホストの種類</summary>
internal enum ChatToolHostKind
{
    /// <summary>ツールホストを渡さない</summary>
    None,

    /// <summary>呼び出しを記録して成功を返す（<see cref="RecordingToolHost"/>）</summary>
    Recording,

    /// <summary>常に例外を投げる（<see cref="ThrowingToolHost"/>・既定の例外 <c>boom</c>）</summary>
    Throwing,
}

/// <summary>バックエンドがツールの結果を受け取った記録（エンジンに依らない形）</summary>
/// <param name="Id">呼び出しの Id（バックエンドが Id を運ばなければ null）</param>
/// <param name="Result">結果のテキスト</param>
/// <param name="Success">成否（バックエンドが成否を運ばなければ null。<see cref="ChatEngineAdapter.ToolReturnCarriesSuccess"/>）</param>
internal sealed record ChatToolReturn(string? Id, string Result, bool? Success);

/// <summary>接続の状態（エンジンに依らない形）</summary>
/// <param name="IsReady">送信できるか</param>
/// <param name="CliMissing">CLI が見つからない印（エンジンが公開しなければ null。<see cref="ChatEngineCapability.ExposesCliMissing"/>）</param>
/// <param name="StatusLevel">状態の色（エンジンが持たなければ null）</param>
/// <param name="Summary">状態の概要の文</param>
/// <param name="Guidance">案内の文</param>
/// <param name="RuntimeStarted">常駐プロセスに接続しているか（常駐プロセスを持たなければ偽）</param>
internal sealed record ChatReadiness(
    bool IsReady,
    bool? CliMissing,
    ConnectionHealth? StatusLevel,
    string Summary,
    string Guidance,
    bool RuntimeStarted
);

/// <summary>エンジンのイベントの記録（作った時点から購読する）</summary>
internal sealed class ChatEngineEvents
{
    private readonly List<string> _log = [];
    private readonly List<string> _statuses = [];
    private readonly List<ErChatToolActivity> _activities = [];
    private readonly List<ErChatTurnResult> _completions = [];
    private readonly object _gate = new();

    /// <summary>購読を始める</summary>
    /// <param name="engine">エンジン</param>
    public ChatEngineEvents(IErChatEngine engine)
    {
        engine.AssistantDeltaReceived += (_, delta) => Add(_log, $"delta:{delta}");
        engine.ToolActivityReceived += (_, activity) =>
        {
            lock (_gate)
            {
                _activities.Add(activity);
                _log.Add($"tool:{activity.ToolName}:{(activity.Success ? "ok" : "fail")}");
            }
        };
        engine.TurnCompleted += (_, result) =>
        {
            lock (_gate)
            {
                _completions.Add(result);
                _log.Add(result.Success ? "completed:ok" : "completed:fail");
            }
        };
        engine.StatusChanged += (_, status) => Add(_statuses, status);
    }

    /// <summary>差分・ツールの活動・完了の順の記録（<c>delta:…</c>・<c>tool:名前:ok|fail</c>・<c>completed:ok|fail</c>）</summary>
    public IReadOnlyList<string> Log => Snapshot(_log);

    /// <summary>受け取った差分</summary>
    public IReadOnlyList<string> Deltas =>
        Log.Where(entry => entry.StartsWith("delta:", StringComparison.Ordinal))
            .Select(entry => entry["delta:".Length..])
            .ToList();

    /// <summary>ツールの活動</summary>
    public IReadOnlyList<ErChatToolActivity> Activities => Snapshot(_activities);

    /// <summary>ターンの完了</summary>
    public IReadOnlyList<ErChatTurnResult> Completions => Snapshot(_completions);

    /// <summary>状態の文言（すべて）</summary>
    public IReadOnlyList<string> Statuses => Snapshot(_statuses);

    /// <summary>いまの状態の文言の数（<see cref="StatusesSince"/> の印）</summary>
    public int StatusMark => Statuses.Count;

    /// <summary>印より後の状態の文言</summary>
    /// <param name="mark"><see cref="StatusMark"/> で取った印</param>
    public IReadOnlyList<string> StatusesSince(int mark) => Statuses.Skip(mark).ToList();

    private void Add(List<string> list, string value)
    {
        lock (_gate)
        {
            list.Add(value);
        }
    }

    private List<T> Snapshot<T>(List<T> list)
    {
        lock (_gate)
        {
            return [.. list];
        }
    }
}

/// <summary>
/// 網の 1 ケースのエンジンとテストダブルの組（実エンジン＋既存のテストダブル）。行はこの中立の操作だけを使う
/// </summary>
/// <remarks>
/// バックエンドごとの入口（イベントの発火・ドライバの応答・クライアントの戻り値）への訳し方は派生が持つ。
/// 派生の中で軸の値を名指しして分岐しない。破棄でエンジンを破棄し、実行中のまま残した送信の終わりを待つ
/// （エンジンが作る作業フォルダ <c>%TEMP%\QuickER\{backend}\&lt;guid&gt;</c> を残さない）。
/// </remarks>
internal abstract class ChatEngineRig : IAsyncDisposable
{
    /// <summary>実行中のまま残した送信の終わりを待つ上限</summary>
    private static readonly TimeSpan PendingSendTimeout = TimeSpan.FromSeconds(60);

    private readonly List<Task> _pendingSends = [];
    private readonly List<CancellationTokenSource> _callers = [];
    private bool _conversationOpen;
    private bool _disposed;

    /// <summary>組を作る</summary>
    /// <param name="hostKind">ツールホストの種類</param>
    protected ChatEngineRig(ChatToolHostKind hostKind)
    {
        HostKind = hostKind;
        Recording = hostKind == ChatToolHostKind.Recording ? new RecordingToolHost() : null;
        ToolHost = hostKind switch
        {
            ChatToolHostKind.Recording => Recording,
            ChatToolHostKind.Throwing => new ThrowingToolHost(),
            _ => null,
        };
    }

    /// <summary>ツールホストの種類</summary>
    public ChatToolHostKind HostKind { get; }

    /// <summary>エンジンへ渡したツールホスト（渡さなければ null）</summary>
    protected IErDiagramToolHost? ToolHost { get; }

    /// <summary>記録するツールホスト（<see cref="ChatToolHostKind.Recording"/> のときだけ）</summary>
    public RecordingToolHost? Recording { get; }

    /// <summary>エンジン</summary>
    public abstract IErChatEngine Engine { get; }

    /// <summary>イベントの記録</summary>
    public ChatEngineEvents Events { get; private set; } = null!;

    /// <summary>バックエンドが受け取ったツールの結果</summary>
    public abstract IReadOnlyList<ChatToolReturn> ToolReturns { get; }

    /// <summary>バックエンドへ届いた中断の数</summary>
    public abstract int InterruptsReceived { get; }

    /// <summary>派生がエンジンを作った直後に呼ぶ（イベントの購読を始める）</summary>
    protected void Attach() => Events = new ChatEngineEvents(Engine);

    /// <summary>バックエンドへ届いた送信（ターンの開始）のプロンプト（届いた順）</summary>
    public abstract IReadOnlyList<string> PromptsReceived { get; }

    /// <summary>バックエンドへ届いた送信（ターンの開始）の数</summary>
    public int SendsReceived => PromptsReceived.Count;

    /// <summary>バックエンドが開いた会話の数（続きのターンは数えない）</summary>
    public abstract int ConversationsOpened { get; }

    /// <summary>バックエンドへ届いた添付の数（送信の合計）</summary>
    public abstract int AttachmentsReceived { get; }

    /// <summary>
    /// 1 ターンを台本どおりに流す（ターンを始めるのに要る準備＝初期化・会話の開始も、そのバックエンドの流儀で行う）
    /// </summary>
    /// <param name="prompt">送るプロンプト</param>
    /// <param name="script">台本</param>
    /// <param name="cancellationToken">テストの取り消し</param>
    /// <param name="openConversation">
    /// 先に会話を開くか（偽なら会話を開かずに送る＝送信が会話を開くかを確かめる行で使う）
    /// </param>
    public async Task PlayTurnAsync(
        string prompt,
        ChatTurnScript script,
        CancellationToken cancellationToken,
        bool openConversation = true
    )
    {
        if (openConversation)
        {
            await StartConversationAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await PrepareSendAsync(cancellationToken).ConfigureAwait(false);
        }

        // 呼び出し元のトークンは、実行中のまま残す送信が使い続けるので、組の破棄まで持つ
        var caller = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _callers.Add(caller);
        var pending = PlayTurnCoreAsync(prompt, script, caller, cancellationToken);

        if (script.Ending is ChatTurnEnding.Open)
        {
            // 終わらないターンは、送信を実行中のまま残して戻る（破棄で終わりを待つ）
            _pendingSends.Add(pending);
            await WhenTurnOpenAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await pending.ConfigureAwait(false);
    }

    /// <summary>ツールの呼び出しを 1 回受ける（受けるのに要る準備もそのバックエンドの流儀で行う）</summary>
    /// <param name="call">ツールの呼び出し</param>
    /// <param name="cancellationToken">テストの取り消し</param>
    public async Task RequestToolAsync(ChatStep.ToolCall call, CancellationToken cancellationToken)
    {
        await PrepareForToolsAsync(cancellationToken).ConfigureAwait(false);
        await RaiseToolRequestAsync(call, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>ツールの呼び出しを受けられる状態にする（何度呼んでもよい。元のテストと同じ準備をそのバックエンドの流儀で行う）</summary>
    /// <param name="cancellationToken">テストの取り消し</param>
    public abstract Task PrepareForToolsAsync(CancellationToken cancellationToken);

    /// <summary>準備の済んだ状態でツールの呼び出しを 1 回流す</summary>
    /// <param name="call">ツールの呼び出し</param>
    /// <param name="cancellationToken">テストの取り消し</param>
    protected abstract Task RaiseToolRequestAsync(
        ChatStep.ToolCall call,
        CancellationToken cancellationToken
    );

    /// <summary>実行中のターンが無いときに、バックエンドからターンの完了の通知が届く</summary>
    /// <remarks><see cref="ChatEngineCapability.AsyncCompletion"/> を持つアダプタだけが呼ばれる</remarks>
    public virtual void RaiseStrayCompletion() =>
        throw new NotSupportedException("このバックエンドはターンの完了を別の通知で運ばない");

    /// <summary>以後のツールの結果の送信を、この例外で失敗させる</summary>
    /// <remarks><see cref="ChatEngineCapability.ToolResponseChannel"/> を持つアダプタだけが呼ばれる</remarks>
    /// <param name="exception">送信が投げる例外</param>
    public virtual void FailToolResponses(Exception exception) =>
        throw new NotSupportedException("このバックエンドはツールの結果を別の送信で返さない");

    /// <summary>会話を開いた状態にする（元のテストと同じ準備をそのバックエンドの流儀で行う。2 回目からは何もしない）</summary>
    /// <param name="cancellationToken">テストの取り消し</param>
    public async Task StartConversationAsync(CancellationToken cancellationToken)
    {
        if (_conversationOpen)
        {
            return;
        }

        _conversationOpen = true;
        await OpenConversationAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>会話を開き直す（エンジンの StartConversationAsync をもう一度呼ぶ）</summary>
    /// <param name="cancellationToken">テストの取り消し</param>
    public Task RestartConversationAsync(CancellationToken cancellationToken) =>
        Engine.StartConversationAsync(cancellationToken);

    /// <summary>会話を開く（初期化が要るバックエンドは初期化してから）</summary>
    /// <param name="cancellationToken">テストの取り消し</param>
    protected abstract Task OpenConversationAsync(CancellationToken cancellationToken);

    /// <summary>会話を開かずに送るときの準備（元のテストと同じ準備。既定は何もしない）</summary>
    /// <param name="cancellationToken">テストの取り消し</param>
    protected virtual Task PrepareSendAsync(CancellationToken cancellationToken) =>
        Task.CompletedTask;

    /// <summary>バックエンドとの接続が切れる（ターンの外で）</summary>
    /// <remarks><see cref="ChatEngineCapability.ConnectionCanDrop"/> を持つアダプタだけが呼ばれる</remarks>
    public virtual void LoseConnection() =>
        throw new NotSupportedException("このバックエンドは接続を持たない");

    /// <summary>バックエンド（クライアント）を破棄したか</summary>
    /// <remarks><see cref="ChatEngineCapability.OwnsBackend"/> を持つアダプタだけが読まれる</remarks>
    public virtual bool BackendDisposed =>
        throw new NotSupportedException("このバックエンドは破棄するクライアントを持たない");

    /// <summary>エンジンが作った作業フォルダ（まだ作っていなければ null）</summary>
    /// <remarks><see cref="ChatEngineCapability.CreatesWorkingDirectory"/> を持つアダプタだけが読まれる</remarks>
    public virtual string? WorkingDirectory =>
        throw new NotSupportedException("このエンジンは作業フォルダを作らない");

    /// <summary>接続の状態（エンジンに依らない形）</summary>
    /// <remarks><see cref="ChatEngineCapability.CliLifecycle"/> を持つアダプタだけが読まれる</remarks>
    public virtual ChatReadiness Readiness =>
        throw new NotSupportedException("このエンジンは CLI の接続を持たない");

    /// <summary>CLI が見つかるかを切り替える</summary>
    /// <param name="available">見つかるか</param>
    public virtual void SetCliAvailable(bool available) =>
        throw new NotSupportedException("このエンジンは CLI の接続を持たない");

    /// <summary>以後の常駐プロセスの起動を、この例外で失敗させる（null で戻す）</summary>
    /// <remarks><see cref="ChatEngineCapability.LaunchesRuntime"/> を持つアダプタだけが呼ばれる</remarks>
    /// <param name="exception">起動が投げる例外</param>
    public virtual void FailRuntimeStart(Exception? exception) =>
        throw new NotSupportedException("このエンジンは常駐プロセスを起動しない");

    /// <summary>以後の常駐プロセスの起動を、このタスクの完了まで待たせる（null で戻す）</summary>
    /// <param name="gate">待たせるタスク</param>
    public virtual void GateRuntimeStart(Task? gate) =>
        throw new NotSupportedException("このエンジンは常駐プロセスを起動しない");

    /// <summary>常駐プロセスを起動した数（フェイクが数える）</summary>
    public virtual int RuntimeStarts =>
        throw new NotSupportedException("このエンジンは常駐プロセスを起動しない");

    /// <summary>接続する（エンジンの初期化）</summary>
    /// <param name="cancellationToken">テストの取り消し</param>
    public Task ConnectAsync(CancellationToken cancellationToken) =>
        Engine.InitializeAsync(cancellationToken);

    /// <summary>接続を確かめ直す（各エンジンの「再確認」）</summary>
    /// <param name="cancellationToken">テストの取り消し</param>
    public virtual Task RecheckAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException("このエンジンは CLI の接続を持たない");

    /// <summary>台本を訳して流す本体（会話の準備は済んでいる）</summary>
    /// <param name="prompt">送るプロンプト</param>
    /// <param name="script">台本</param>
    /// <param name="caller">送信に渡す呼び出し元のトークン（<see cref="ChatTurnEnding.CallerCancels"/> で取り消す）</param>
    /// <param name="cancellationToken">テストの取り消し</param>
    protected abstract Task PlayTurnCoreAsync(
        string prompt,
        ChatTurnScript script,
        CancellationTokenSource caller,
        CancellationToken cancellationToken
    );

    /// <summary>終わらないターンが実行中になるのを待つ（送信の中で待つバックエンドは、待ちに入ったところで戻る）</summary>
    /// <param name="cancellationToken">テストの取り消し</param>
    protected virtual Task WhenTurnOpenAsync(CancellationToken cancellationToken) =>
        Task.CompletedTask;

    /// <summary>エンジンを破棄し、実行中のまま残した送信の終わりを待つ</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await Engine.DisposeAsync().ConfigureAwait(false);

        foreach (var pending in _pendingSends)
        {
            await pending.WaitAsync(PendingSendTimeout).ConfigureAwait(false);
        }

        foreach (var caller in _callers)
        {
            caller.Dispose();
        }
    }
}
