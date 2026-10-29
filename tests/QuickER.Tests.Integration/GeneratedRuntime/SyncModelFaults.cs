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

    /// <summary>仕掛けた失敗をすべて外す（静止化の前）</summary>
    public void Clear() => _armed.Clear();

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
