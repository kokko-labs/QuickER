using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using QuickER.Tests.GeneratedBinaryFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 呼び出しを共有ログへ記録するテスト用の Save フック。<see cref="BeforePredicate"/> で Before の返り値（スキップ）を、
/// <see cref="AfterAction"/> で After の副作用（context 経由の書き込み・例外）を差し込める。
/// </summary>
/// <remarks>
/// バイナリフィクスチャ（<c>QuickER.Tests.GeneratedBinaryFixture</c>）の <c>ISaveHook&lt;TEntity&gt;</c> を実装するため、
/// 同フィクスチャを使う Save フック系スイート（直結パリティ基底とリモート）で共有する。
/// ログ行は <c>{name}:{before|after}:{操作}:{キー}</c> で、<paramref name="name"/> を省略すると接頭辞なしの
/// <c>{before|after}:{操作}:{キー}</c> になる（フックが 1 つだけのスイート向け）。
/// </remarks>
/// <typeparam name="TEntity">フックの対象エンティティ型</typeparam>
/// <param name="log">発火を記録する共有ログ</param>
/// <param name="keySelector">ログに載せるキーの取り出し</param>
/// <param name="name">ログ行の接頭辞（null＝接頭辞なし）</param>
internal sealed class RecordingHook<TEntity>(
    List<string> log,
    Func<TEntity, object> keySelector,
    string? name = null
) : ISaveHook<TEntity>
    where TEntity : EntityBase
{
    /// <summary>ログ行の接頭辞（名前なしなら空文字）</summary>
    private readonly string _prefix = name is null ? string.Empty : $"{name}:";

    /// <summary>Before の返り値を決める述語（null＝常に true＝スキップしない）</summary>
    /// <remarks>途中でスキップ条件を解除するテストがあるため <c>set</c> 可能にしてある</remarks>
    public Func<TEntity, SaveOperation, bool>? BeforePredicate { get; set; }

    /// <summary>After の副作用（null＝何もしない）</summary>
    public Func<TEntity, SaveOperation, ISaveHookContext, Task>? AfterAction { get; init; }

    public Task<bool> BeforeSaveAsync(
        TEntity entity,
        SaveOperation operation,
        CancellationToken cancellationToken = default
    )
    {
        log.Add($"{_prefix}before:{operation}:{keySelector(entity)}");
        return Task.FromResult(BeforePredicate?.Invoke(entity, operation) ?? true);
    }

    public async Task AfterSaveAsync(
        TEntity entity,
        SaveOperation operation,
        ISaveHookContext context,
        CancellationToken cancellationToken = default
    )
    {
        log.Add($"{_prefix}after:{operation}:{keySelector(entity)}");

        if (AfterAction is not null)
        {
            await AfterAction(entity, operation, context);
        }
    }
}
