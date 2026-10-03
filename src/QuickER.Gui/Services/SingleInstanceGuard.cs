using System.Threading;

namespace QuickER.Services;

/// <summary>単一インスタンスの座を取りに行った結果</summary>
public enum SingleInstanceOutcome
{
    /// <summary>座を取得した（このプロセスが唯一のインスタンス）</summary>
    Acquired,

    /// <summary>既に起動しているインスタンスがあり、そのウィンドウのアクティブ化を頼んだ</summary>
    AlreadyRunning,

    /// <summary>
    /// 既に起動しているインスタンスがあるが、権限の違い（管理者として実行など）で名前付きオブジェクトを開けず、
    /// アクティブ化も頼めない
    /// </summary>
    Inaccessible,
}

/// <summary>
/// 名前付き Mutex による単一インスタンス制御。2 回目以降の起動を検出し、
/// 既存インスタンスへのアクティブ化要求（名前付きイベント）を仲介する。
/// </summary>
/// <remarks>
/// カーネルオブジェクト名は <c>Local\</c> プレフィックス＝ログオンセッション単位のスコープ。
/// 同一ユーザーのセッション内で 1 インスタンスに制限し、別ユーザー・別セッションには干渉しない。
/// 保持側プロセスが異常終了した場合はハンドルが OS により閉じられるため、次回起動は正常に取得できる。
/// </remarks>
public sealed class SingleInstanceGuard : IDisposable
{
    /// <summary>単一インスタンス判定用 Mutex の名前</summary>
    private const string MutexName = @"Local\QuickER.SingleInstance";

    /// <summary>既存インスタンスへウィンドウのアクティブ化を要求するイベントの名前</summary>
    private const string ActivationEventName = @"Local\QuickER.SingleInstance.Activate";

    /// <summary>プロセス生存中ずっと保持する単一インスタンス Mutex</summary>
    private readonly Mutex _mutex;

    /// <summary>アクティブ化要求の受信に使う名前付きイベント（AutoReset＝1 要求 1 起床）</summary>
    private readonly EventWaitHandle _activationEvent;

    /// <summary>アクティブ化要求の待機登録（Dispose で解除する）</summary>
    private RegisteredWaitHandle? _activationWait;

    /// <summary>破棄済みか（再起動の直前と <c>using</c> の終わりの 2 回呼ばれるため）</summary>
    private bool _disposed;

    private SingleInstanceGuard(Mutex mutex, EventWaitHandle activationEvent)
    {
        _mutex = mutex;
        _activationEvent = activationEvent;
    }

    /// <summary>
    /// 単一インスタンスの座を取得する。既に別インスタンスが起動している場合は、
    /// そのインスタンスへアクティブ化を要求したうえで <c>null</c> を返す（呼び出し側は即終了する）。
    /// </summary>
    /// <param name="outcome">結果。<c>null</c> を返したときに、アクティブ化を頼めたか（権限違いで開けなかったか）を区別する</param>
    public static SingleInstanceGuard? TryAcquire(out SingleInstanceOutcome outcome) =>
        TryAcquire(MutexName, ActivationEventName, AllowExistingInstanceToComeForward, out outcome);

    /// <summary>名前と前面化の権利の渡し方を指定して座を取得する（テストで本物の名前を使わないため）</summary>
    /// <param name="mutexName">単一インスタンス判定用 Mutex の名前</param>
    /// <param name="activationEventName">アクティブ化要求のイベントの名前</param>
    /// <param name="allowForegroundActivation">
    /// 既存インスタンスへアクティブ化を頼む<b>直前</b>に呼ぶ、前面化の権利を渡す処理。失敗しても起動を止めない
    /// </param>
    /// <param name="outcome">結果</param>
    /// <remarks>
    /// <para>
    /// 権限の違うインスタンス（管理者として実行）が作った名前付きオブジェクトは、通常の起動からは
    /// 開けず <see cref="UnauthorizedAccessException"/> になる。WPF の初期化前に落ちて crash ログしか
    /// 残らないのを避け、<see cref="SingleInstanceOutcome.Inaccessible"/> として呼び出し側へ返す
    /// （単一インスタンス制御なしでは起動しない＝2 つのインスタンスが同じ自動保存・設定を書き合うため）。
    /// </para>
    /// <para>
    /// 前面化の権利を先に渡すのは、裏にいる既存インスタンスの <c>Activate()</c> が前面ロック
    /// （前面に出られるのは直近の入力を受けたプロセスとその許可を受けたプロセスだけ）で止められ、
    /// タスクバーで点滅するだけになるため。利用者が起動したこのプロセスは前面化の権利を持っているので、
    /// アクティブ化を頼む前に渡す。
    /// </para>
    /// </remarks>
    internal static SingleInstanceGuard? TryAcquire(
        string mutexName,
        string activationEventName,
        Action allowForegroundActivation,
        out SingleInstanceOutcome outcome
    )
    {
        Mutex mutex;
        bool createdNew;

        try
        {
            mutex = new Mutex(initiallyOwned: true, mutexName, out createdNew);
        }
        catch (UnauthorizedAccessException)
        {
            outcome = SingleInstanceOutcome.Inaccessible;
            return null;
        }

        EventWaitHandle activationEvent;

        try
        {
            activationEvent = new EventWaitHandle(
                initialState: false,
                EventResetMode.AutoReset,
                activationEventName
            );
        }
        catch (UnauthorizedAccessException)
        {
            // イベントだけ開けないのは、権限の違うインスタンスが作った直後に座を争った場合に限られる。
            // アクティブ化の受け口を持てないまま座を取るより、同じ扱い（案内して終わる）に倒す
            mutex.Dispose();
            outcome = SingleInstanceOutcome.Inaccessible;
            return null;
        }

        if (createdNew)
        {
            outcome = SingleInstanceOutcome.Acquired;
            return new SingleInstanceGuard(mutex, activationEvent);
        }

        // 2 回目の起動: 前面化の権利を渡してから既存インスタンスにウィンドウのアクティブ化を頼み、
        // 自分は座を持たずに帰る
        try
        {
            allowForegroundActivation();
        }
        catch
        {
            // 権利を渡せなくてもアクティブ化の依頼は出す（点滅に留まるだけで、起動の流れは止めない）
        }

        activationEvent.Set();
        activationEvent.Dispose();
        mutex.Dispose();

        outcome = SingleInstanceOutcome.AlreadyRunning;
        return null;
    }

    /// <summary>既存インスタンスがウィンドウを前面へ出せるよう、前面化の権利をどのプロセスにも許す</summary>
    /// <remarks>
    /// 失敗（このプロセス自身が前面化の権利を持たないなど）は戻り値の false で返るだけで、
    /// アクティブ化が点滅に留まる以外の影響は無いため無視する。
    /// </remarks>
    private static void AllowExistingInstanceToComeForward() =>
        NativeMethods.AllowSetForegroundWindow(NativeMethods.AllowSetForegroundAnyProcess);

    /// <summary>
    /// 後続インスタンスからのアクティブ化要求を待ち受け、要求のたびにコールバックを呼ぶ。
    /// コールバックはスレッドプール上で呼ばれるため、UI 操作は呼び出し側で Dispatcher へ委譲すること。
    /// </summary>
    public void ListenForActivation(Action onActivationRequested)
    {
        _activationWait = ThreadPool.RegisterWaitForSingleObject(
            _activationEvent,
            (_, _) => onActivationRequested(),
            state: null,
            millisecondsTimeOutInterval: Timeout.Infinite,
            executeOnlyOnce: false
        );
    }

    /// <summary>待機登録を解除し、Mutex・イベントを解放する（座を手放す。2 回目以降は何もしない）</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _activationWait?.Unregister(null);
        _mutex.Dispose();
        _activationEvent.Dispose();
    }
}
