using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using QuickER.Tests.GeneratedSyncModelFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>モデルが注入する失敗の種類（どれも 1 回だけ起きて消える）</summary>
internal enum ModelFaultKind
{
    /// <summary>端末の同期中の、サーバーへの行の書き込み（挿入・更新・保存・削除）</summary>
    ServerWrite,

    /// <summary>端末の同期中の、サーバーからの差分・ページ・キーの取得</summary>
    ServerFetch,

    /// <summary>端末の同期中の、サーバーの blob の読み出し（ダウンロードのコピー）</summary>
    BlobDownload,

    /// <summary>端末の同期中の、サーバーへの blob の書き込み（アップロードのコピー）</summary>
    BlobUpload,

    /// <summary>端末のグラフ保存の After フックで例外（業務の書き込みが失敗し、記録だけが残る）</summary>
    LocalAfterSave,

    /// <summary>端末のグラフ保存の Before フックが false を返す（その行の保存を飛ばす）</summary>
    HookSkip,

    /// <summary>端末のグラフ保存の Before フックが挿入する行の主キーを変える（キーを決めるフック）</summary>
    HookRekey,

    /// <summary>
    /// 端末のグラフ保存の Before フックが、キーの無い（0 の）新しい行にキーを決める（整数のキーの表だけ。挿入する実体はキーを
    /// 入れずに作り、フックが操作のキーを入れる＝記録はキーの無い形で付き、保存の後に新しいキーへ書き直される）
    /// </summary>
    HookAssignKey,

    /// <summary>端末の同期中の、エンジンがローカルへ行を適用するときの Before フックが false を返す（その行を飛ばす）</summary>
    /// <remarks>乱数の操作の列には混ぜない（既存の種の操作の列を変えないため）。決定的なシナリオだけが使う。</remarks>
    ApplySkip,
}

/// <summary>
/// 割り込みの点：端末の同期の実行の名前つきの時点。仕掛けた割り込み（<see cref="ArmInterruptOp"/>）は、その端末の同期が
/// この時点に来たときに 1 回だけ、実行と別の流れからローカルの書き込みを差し込む。
/// </summary>
/// <remarks>
/// どの点も、エンジンがローカルの書き込みロックを持っていない時点に置く。割り込みの書き込みはジャーナル記録のデコレータの
/// 書き込みロックを待つので、ロックの中に差し込むと互いに待ち合って止まる（エンジンが行の適用・未送信の読み直しをする区間には
/// 差し込めない）。洗い替えはロックを持ったまま全体を流すので、割り込みは起きない。
/// </remarks>
internal enum ModelInterruptPoint
{
    /// <summary>サーバーへの行の書き込み（挿入・更新・保存・削除）の直前（アップロードの再生の判定の後・その書き込みの前）</summary>
    BeforeServerWrite,

    /// <summary>サーバーからの差分・ページ・キーの取得の直前</summary>
    BeforeServerFetch,

    /// <summary>
    /// サーバーからの差分・ページの取得が 1 行以上を返した後、エンジンがロックを取ってそのバッチを適用する前（適用の最中ではない）
    /// </summary>
    AfterServerFetchBeforeApply,
}

/// <summary>注入した失敗（モデルは、これを原因とする例外を違反として数えない）</summary>
internal sealed class InjectedFaultException(ModelFaultKind kind)
    : Exception($"Injected fault: {kind}.")
{
    public ModelFaultKind Kind { get; } = kind;

    /// <summary>例外の連鎖のどこかに注入した失敗があるか</summary>
    public static bool IsInjected(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is InjectedFaultException)
            {
                return true;
            }

            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions)
                {
                    if (IsInjected(inner))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }
}

/// <summary>
/// 1 つの世界の失敗の注入（仕掛けた失敗は、対象の端末の該当する動作で 1 回だけ起きて消える）。
/// </summary>
/// <remarks>
/// サーバー側の失敗は「その端末の同期の最中」にだけ、ローカルのフックの失敗は「その端末の書き込みの操作の最中」にだけ
/// 起きる。サーバーへ直接書く他者の操作や、エンジン自身がローカルへ書く適用（フックが走る）では起きない。
/// </remarks>
internal sealed class SyncModelFaults
{
    private readonly HashSet<(ModelFaultKind Kind, ModelActor Device)> _armed = [];

    /// <summary>いま同期を流している端末</summary>
    public ModelActor? Syncing { get; set; }

    /// <summary>いま書き込みの操作を流している端末</summary>
    public ModelActor? Writing { get; set; }

    /// <summary>失敗を仕掛ける</summary>
    public void Arm(ModelFaultKind kind, ModelActor device) => _armed.Add((kind, device));

    /// <summary>失敗が仕掛けてあるか（起こさずに見る）</summary>
    public bool IsArmed(ModelFaultKind kind, ModelActor device) => _armed.Contains((kind, device));

    /// <summary><see cref="ModelFaultKind.HookAssignKey"/> のフックが決めるキー（書き込みの間だけ）</summary>
    public int? KeyToAssign { get; set; }

    /// <summary>仕掛けた失敗と割り込みをすべて外す（静止化の前）</summary>
    public void Clear()
    {
        _armed.Clear();

        lock (_interruptLock)
        {
            _interrupt = null;
        }
    }

    /// <summary>仕掛けた失敗が起きた回数（操作の前後で比べ、失敗が効いた操作を見分ける）</summary>
    public int Fired { get; private set; }

    /// <summary>対象の動作の最中なら、仕掛けた失敗を 1 回起こす（起きたら true）</summary>
    public bool TryFire(ModelFaultKind kind, ModelActor? who)
    {
        if (who is { } device && _armed.Remove((kind, device)))
        {
            Fired++;

            return true;
        }

        return false;
    }

    /// <summary>サーバー側の動作の入口で呼ぶ（同期中の端末に仕掛けてあれば例外）</summary>
    public void ThrowOnServer(ModelFaultKind kind)
    {
        if (TryFire(kind, Syncing))
        {
            throw new InjectedFaultException(kind);
        }
    }

    /// <summary>割り込みの書き込みを待つ上限（超えたら待つのをやめ、<see cref="InterruptTimedOut"/> を立てる）</summary>
    public static readonly TimeSpan InterruptTimeout = TimeSpan.FromSeconds(30);

    private readonly object _interruptLock = new();
    private (ModelActor Device, ModelInterruptPoint Point, Func<Task> Write)? _interrupt;

    /// <summary>いま洗い替えを流しているか（洗い替えはロックを持ったまま流すので、割り込みを起こさない）</summary>
    public bool Refreshing { get; set; }

    /// <summary>仕掛けたまま、まだ起きていない割り込みの点（無ければ null）</summary>
    public ModelInterruptPoint? PendingInterrupt
    {
        get
        {
            lock (_interruptLock)
            {
                return _interrupt?.Point;
            }
        }
    }

    /// <summary>割り込みの書き込みが上限の時間で終わらなかった点（待ち合いの疑い。無ければ null）</summary>
    public ModelInterruptPoint? InterruptTimedOut { get; private set; }

    /// <summary>
    /// 割り込みを仕掛ける（端末の次の同期がその点に来たときに 1 回だけ、<paramref name="write"/> を実行と別の流れで流す。
    /// 同時に仕掛けられるのは 1 つだけ）
    /// </summary>
    public void ArmInterrupt(ModelActor device, ModelInterruptPoint point, Func<Task> write)
    {
        lock (_interruptLock)
        {
            if (_interrupt is not null)
            {
                throw new InvalidOperationException("An interrupt is already armed.");
            }

            _interrupt = (device, point, write);
        }
    }

    /// <summary>
    /// サーバー役の割り込みの点で呼ぶ。同期中の端末にこの点の割り込みが仕掛けてあれば、書き込みを
    /// <see cref="ExecutionContext.SuppressFlow"/> の下の <see cref="Task.Run(Func{Task})"/> で流して終わりを待つ
    /// （同期の抑止の印も書き込みロックのリースも引き継がない＝アプリの別の流れの書き込みと同じ）
    /// </summary>
    public async Task InterruptAsync(ModelInterruptPoint point)
    {
        Func<Task> write;

        lock (_interruptLock)
        {
            if (
                Refreshing
                || Syncing is not { } device
                || _interrupt is not { } armed
                || armed.Device != device
                || armed.Point != point
            )
            {
                return;
            }

            write = armed.Write;
            _interrupt = null;
        }

        Task task;

        using (ExecutionContext.SuppressFlow())
        {
            task = Task.Run(write);
        }

        if (await Task.WhenAny(task, Task.Delay(InterruptTimeout)) != task)
        {
            // 待ち合い（エンジンがロックを持ったまま点に来た）の疑い。止まらないよう待つのをやめ、違反として残す
            InterruptTimedOut = point;

            return;
        }

        await task;
    }

    /// <summary>
    /// 差分・ページの取得が返した行を、エンジンへ返す前に通す（1 行以上なら
    /// <see cref="ModelInterruptPoint.AfterServerFetchBeforeApply"/> の割り込みの点）
    /// </summary>
    public async Task<SyncChangeBatch<TEntity>> AfterFetchAsync<TEntity>(
        IReadOnlyList<TEntity> rows,
        int batchSize
    )
        where TEntity : EntityBase, new()
    {
        if (rows.Count > 0)
        {
            await InterruptAsync(ModelInterruptPoint.AfterServerFetchBeforeApply)
                .ConfigureAwait(false);
        }

        return new SyncChangeBatch<TEntity>(rows, rows.Count >= batchSize);
    }

    private readonly List<(ModelActor Device, string Table, string Key)> _writeHits = [];

    /// <summary>
    /// サーバーへの行の書き込みの入口で呼ぶ（同期中の端末に ServerWrite が仕掛けてあれば、当たった（端末・表・キー）を
    /// 控えてから例外）。控えは、競合・決着の理由の根拠を確かめる表明が、根拠の無い違反の形を分けるのに使う
    /// （注入した失敗そのものは根拠にしない）
    /// </summary>
    public void ThrowOnServerWrite(Type entityType, object key)
    {
        var device = Syncing;

        if (TryFire(ModelFaultKind.ServerWrite, device))
        {
            lock (_writeHits)
            {
                _writeHits.Add(
                    (device!.Value, ModelTables.TableOf(entityType), ModelTables.KeyText(key))
                );
            }

            throw new InjectedFaultException(ModelFaultKind.ServerWrite);
        }
    }

    /// <summary>これまでに ServerWrite の失敗が当たった（端末・表・キー）の数</summary>
    public int WriteHitCount
    {
        get
        {
            lock (_writeHits)
            {
                return _writeHits.Count;
            }
        }
    }

    /// <summary><paramref name="from"/> 番目から後に ServerWrite の失敗が当たった（端末・表・キー）</summary>
    public IReadOnlyList<(ModelActor Device, string Table, string Key)> WriteHitsSince(int from)
    {
        lock (_writeHits)
        {
            return [.. _writeHits.Skip(from)];
        }
    }
}

/// <summary>端末のローカルのリポジトリへ渡す Save フック（失敗の注入の口）</summary>
/// <typeparam name="TEntity">エンティティ型</typeparam>
internal sealed class SyncModelHook<TEntity>(
    SyncModelFaults faults,
    Action<TEntity>? rekey,
    Func<TEntity, bool>? keyless = null,
    Action<TEntity, int>? assign = null
) : ISaveHook<TEntity>
    where TEntity : EntityBaseCore
{
    public Task<bool> BeforeSaveAsync(
        TEntity entity,
        SaveOperation operation,
        CancellationToken cancellationToken = default
    )
    {
        if (
            faults.TryFire(ModelFaultKind.HookSkip, faults.Writing)
            || faults.TryFire(ModelFaultKind.ApplySkip, faults.Syncing)
        )
        {
            return Task.FromResult(false);
        }

        // キーの無い新しい行にキーを決める（世界がキーを入れずに作った実体だけ）
        if (
            operation == SaveOperation.Insert
            && keyless is not null
            && assign is not null
            && keyless(entity)
            && faults.KeyToAssign is { } key
            && faults.TryFire(ModelFaultKind.HookAssignKey, faults.Writing)
        )
        {
            assign(entity, key);
        }

        if (
            operation == SaveOperation.Insert
            && rekey is not null
            && faults.TryFire(ModelFaultKind.HookRekey, faults.Writing)
        )
        {
            rekey(entity);
        }

        return Task.FromResult(true);
    }

    public Task AfterSaveAsync(
        TEntity entity,
        SaveOperation operation,
        ISaveHookContext context,
        CancellationToken cancellationToken = default
    ) =>
        faults.TryFire(ModelFaultKind.LocalAfterSave, faults.Writing)
            ? throw new InjectedFaultException(ModelFaultKind.LocalAfterSave)
            : Task.CompletedTask;
}

/// <summary>読み進めると途中で注入した失敗を投げるストリーム（blob の書き込みの失敗の注入）</summary>
internal sealed class SyncModelFailingStream(byte[] content) : Stream
{
    private int _position;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => content.Length;

    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_position > 0)
        {
            throw new InjectedFaultException(ModelFaultKind.LocalAfterSave);
        }

        var length = Math.Min(Math.Min(count, content.Length), 1);
        Array.Copy(content, 0, buffer, offset, length);
        _position += length;

        return length;
    }

    public override void Flush() { }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();
}

/// <summary>
/// 外部の <see cref="ISyncTable"/> 実装の代役（公開の面だけを転送する包み）。
/// </summary>
/// <remarks>
/// エンジンの内部の面（internal のインターフェイス・ダウンロードの文脈の internal なメンバー）は持たないので、利用者が
/// 自分で書いた実装と同じく、公開の面だけで動く。ダウンロードの文脈は公開のメンバー（未送信のキー・受理記録）だけを
/// 写して渡す。
/// </remarks>
internal sealed class SyncModelExternalTable(ISyncTable inner) : ISyncTable
{
    public string TableName => inner.TableName;

    public Type EntityType => inner.EntityType;

    public bool IsVersionless => inner.IsVersionless;

    public IReadOnlyList<string> UnboundedBinaryColumnNames => inner.UnboundedBinaryColumnNames;

    public Task<byte[]?> GetChangeCeilingAsync(CancellationToken cancellationToken = default) =>
        inner.GetChangeCeilingAsync(cancellationToken);

    public Task<int> DownloadAsync(
        byte[]? ceiling,
        int batchSize,
        bool includeUnboundedBinary,
        CancellationToken cancellationToken = default
    ) => inner.DownloadAsync(ceiling, batchSize, includeUnboundedBinary, cancellationToken);

    public Task<SyncDownloadOutcome> DownloadAsync(
        byte[]? ceiling,
        int batchSize,
        bool includeUnboundedBinary,
        SyncDownloadContext context,
        CancellationToken cancellationToken = default
    ) =>
        inner.DownloadAsync(
            ceiling,
            batchSize,
            includeUnboundedBinary,
            new SyncDownloadContext
            {
                PendingKeyTexts = context.PendingKeyTexts,
                AcknowledgedRowVersions = context.AcknowledgedRowVersions,
            },
            cancellationToken
        );

    public Task<IReadOnlyList<SyncAppliedServerRow>> ApplyServerRowsAsync(
        IReadOnlyList<string> keyTexts,
        bool includeUnboundedBinary,
        CancellationToken cancellationToken = default
    ) => inner.ApplyServerRowsAsync(keyTexts, includeUnboundedBinary, cancellationToken);

    public Task<IReadOnlyList<string>> PropagateDeletesAsync(
        IReadOnlySet<string> pendingKeyTexts,
        CancellationToken cancellationToken = default
    ) => inner.PropagateDeletesAsync(pendingKeyTexts, cancellationToken);

    public Task<int> DeleteAllLocalAsync(CancellationToken cancellationToken = default) =>
        inner.DeleteAllLocalAsync(cancellationToken);

    public Task<int> RefreshAsync(
        byte[]? ceiling,
        int batchSize,
        bool includeUnboundedBinary,
        CancellationToken cancellationToken = default
    ) => inner.RefreshAsync(ceiling, batchSize, includeUnboundedBinary, cancellationToken);

    public Task<SyncUploadOutcome> UploadAsync(
        SyncJournalRow entry,
        SyncMode mode,
        SyncConflictPolicy policy,
        bool includeUnboundedBinary,
        byte[]? acknowledgedRowVersion,
        CancellationToken cancellationToken = default
    ) =>
        inner.UploadAsync(
            entry,
            mode,
            policy,
            includeUnboundedBinary,
            acknowledgedRowVersion,
            cancellationToken
        );
}
