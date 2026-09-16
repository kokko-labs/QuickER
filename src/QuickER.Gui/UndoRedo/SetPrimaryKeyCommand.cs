using System;
using System.Collections.Generic;
using QuickER.Resources;
using QuickER.ViewModels;

namespace QuickER.UndoRedo;

/// <summary>テーブルの主キー（膜と順序）の置き換えを単一の Undo 単位として扱う複合コマンド</summary>
/// <remarks>
/// <para>
/// 膜（どの列が主キーか）の正本は <see cref="ColumnViewModel.IsPrimaryKey"/>、順序の正本は
/// <see cref="EntityViewModel.PrimaryKeyColumnIds"/> で、両方をまとめて 1 回で往復させる。
/// </para>
/// <para>
/// <see cref="ColumnViewModel.IsNullable"/> も新旧で保持する。
/// <see cref="DiagramChangeTracker.RunWithoutTracking"/> が止めるのは変更追跡ハンドラだけで、
/// <c>ColumnViewModel</c> 側の「主キー化したら NULL 許容を落とす」連動はそのまま走る。しかも主キーを
/// 外しても元の NULL 許容は戻らないため、膜だけ戻しても NULL 許容は戻らない。
/// </para>
/// <para>
/// 適用順は <see cref="ColumnViewModel.IsPrimaryKey"/> → <see cref="ColumnViewModel.IsNullable"/> の順。
/// 主キーが <c>true</c> の間は <c>IsNullable</c> の setter が値を握り潰すため、逆順では NULL 許容が入らない
/// （変更追跡の <c>TrackedColumnProperties</c> の並び順と同じ前例）。
/// </para>
/// <para>
/// 適用は追跡抑止下で行う（そうしないと膜が動いた列の数だけ履歴が分裂して二重登録になる）。
/// リレーションの外部キー列ルール（<c>ApplyRelationshipColumnRules</c>）は呼ばない
/// ＝ロック判定はリレーションと列ペアだけを読み <c>IsPrimaryKey</c> を参照しないため、
/// 膜が動いても結果が変わらず呼ぶ必要が無い。
/// </para>
/// </remarks>
public sealed class SetPrimaryKeyCommand : IUndoableCommand
{
    /// <summary>主キーを差し替える対象のエンティティ</summary>
    private readonly EntityViewModel _entity;

    /// <summary>変更前の列状態（膜が動く列のみ）</summary>
    private readonly IReadOnlyList<(
        ColumnViewModel Column,
        bool IsPrimaryKey,
        bool IsNullable
    )> _before;

    /// <summary>変更後の列状態（膜が動く列のみ）</summary>
    private readonly IReadOnlyList<(
        ColumnViewModel Column,
        bool IsPrimaryKey,
        bool IsNullable
    )> _after;

    /// <summary>変更前の主キー順</summary>
    private readonly IReadOnlyList<Guid> _beforeOrder;

    /// <summary>変更後の主キー順</summary>
    private readonly IReadOnlyList<Guid> _afterOrder;

    /// <summary>列状態の変更を追跡なしで適用するためのラッパー（MainViewModel の RunWithoutTracking フック）</summary>
    private readonly Action<Action> _runWithoutTracking;

    /// <summary><see cref="SetPrimaryKeyCommand"/> を生成する</summary>
    /// <param name="entity">主キーを差し替える対象のエンティティ</param>
    /// <param name="before">膜が動く列の変更前の状態</param>
    /// <param name="after">膜が動く列の変更後の状態</param>
    /// <param name="beforeOrder">変更前の主キー順</param>
    /// <param name="afterOrder">変更後の主キー順</param>
    /// <param name="runWithoutTracking">追跡抑止下で処理を実行するラッパー</param>
    public SetPrimaryKeyCommand(
        EntityViewModel entity,
        IReadOnlyList<(ColumnViewModel Column, bool IsPrimaryKey, bool IsNullable)> before,
        IReadOnlyList<(ColumnViewModel Column, bool IsPrimaryKey, bool IsNullable)> after,
        IReadOnlyList<Guid> beforeOrder,
        IReadOnlyList<Guid> afterOrder,
        Action<Action> runWithoutTracking
    )
    {
        _entity = entity;
        _before = before;
        _after = after;
        _beforeOrder = beforeOrder;
        _afterOrder = afterOrder;
        _runWithoutTracking = runWithoutTracking;
    }

    /// <inheritdoc />
    /// <remarks>膜が動かない（＝並べ替えだけの）変更は履歴の表示も並べ替えとして見せる</remarks>
    public string Description =>
        _before.Count == 0 ? Strings.Undo_ReorderPrimaryKeyColumns : Strings.Undo_SetPrimaryKey;

    /// <inheritdoc />
    public void Execute() => Apply(_after, _afterOrder);

    /// <inheritdoc />
    public void Undo() => Apply(_before, _beforeOrder);

    /// <summary>列状態と主キー順を追跡抑止下でまとめて適用する</summary>
    private void Apply(
        IReadOnlyList<(ColumnViewModel Column, bool IsPrimaryKey, bool IsNullable)> states,
        IReadOnlyList<Guid> order
    )
    {
        _runWithoutTracking(() =>
        {
            foreach (var state in states)
            {
                state.Column.IsPrimaryKey = state.IsPrimaryKey;
                state.Column.IsNullable = state.IsNullable;
            }

            _entity.SetPrimaryKeyColumnIds(order);
        });
    }
}
