using System.Threading;

namespace QuickER.Services;

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

    private SingleInstanceGuard(Mutex mutex, EventWaitHandle activationEvent)
    {
        _mutex = mutex;
        _activationEvent = activationEvent;
    }

    /// <summary>
    /// 単一インスタンスの座を取得する。既に別インスタンスが起動している場合は、
    /// そのインスタンスへアクティブ化を要求したうえで <c>null</c> を返す（呼び出し側は即終了する）。
    /// </summary>
    public static SingleInstanceGuard? TryAcquire()
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        var activationEvent = new EventWaitHandle(
            initialState: false,
            EventResetMode.AutoReset,
            ActivationEventName
        );

        if (createdNew)
        {
            return new SingleInstanceGuard(mutex, activationEvent);
        }

        // 2 回目の起動: 既存インスタンスにウィンドウのアクティブ化を頼み、自分は座を持たずに帰る
        activationEvent.Set();
        activationEvent.Dispose();
        mutex.Dispose();

        return null;
    }

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

    /// <summary>待機登録を解除し、Mutex・イベントを解放する</summary>
    public void Dispose()
    {
        _activationWait?.Unregister(null);
        _mutex.Dispose();
        _activationEvent.Dispose();
    }
}
