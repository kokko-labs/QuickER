using System.Collections.Generic;
using System.Linq;
using QuickER.Resources;
using QuickER.ViewModels;

namespace QuickER.UndoRedo;

/// <summary>カラムを 1 件削除するコマンド（Undo で元の位置へ復元する）</summary>
/// <remarks>
/// 削除カラムを外部キーとして参照するリレーションの SourceColumnId / TargetColumnId を
/// 削除前にスナップショット保存し Undo 時にカラムと併せて復元する。
/// 削除カラムを構成列に含む一意制約は「制約ごと削除」し、同じ Undo 単位で位置ごと復元する
/// （構成列を 1 つ失った制約を黙って別の意味の制約へ変質させないため）
/// </remarks>
public class RemoveColumnCommand : IUndoableCommand
{
    /// <summary>削除対象カラムを保持するエンティティ</summary>
    private readonly EntityViewModel _entity;

    /// <summary>削除対象のカラム</summary>
    private readonly ColumnViewModel _column;

    /// <summary>削除前のインデックス（Undo 時の復元位置）</summary>
    private readonly int _index;

    /// <summary>削除前のリレーション FK スナップショット（リレーション VM と参照カラム ID の対）</summary>
    private readonly IReadOnlyList<(
        RelationshipViewModel Relationship,
        Guid? SourceColumnId,
        Guid? TargetColumnId
    )> _relationshipSnapshots;

    /// <summary>巻き添えで削除する一意制約と、その削除前のインデックス（Undo の復元位置）</summary>
    private readonly IReadOnlyList<(
        UniqueConstraintViewModel Constraint,
        int Index
    )> _affectedConstraints;

    /// <summary>Undo / Redo 後に FK ルールを再適用する後処理</summary>
    private readonly Action _afterApply;

    /// <summary><see cref="RemoveColumnCommand"/> を生成する</summary>
    /// <param name="entity">削除対象カラムを保持するエンティティ</param>
    /// <param name="column">削除対象のカラム</param>
    /// <param name="affectedRelationships">削除カラムを参照しているリレーション一覧</param>
    /// <param name="afterApply">Undo / Redo 後に呼ぶ後処理（FK ルール再適用など）</param>
    public RemoveColumnCommand(
        EntityViewModel entity,
        ColumnViewModel column,
        IEnumerable<RelationshipViewModel> affectedRelationships,
        Action afterApply
    )
    {
        _entity = entity;
        _column = column;
        _index = entity.Columns.IndexOf(column);
        _afterApply = afterApply;

        // 削除前の SourceColumnId/TargetColumnId をスナップショット保存する
        _relationshipSnapshots = affectedRelationships
            .Select(r => (r, r.SourceColumnId, r.TargetColumnId))
            .ToList();

        // 削除カラムを構成列に含む一意制約を、復元位置つきで退避する
        _affectedConstraints = entity
            .UniqueConstraints.Select((constraint, index) => (Constraint: constraint, Index: index))
            .Where(item => item.Constraint.ContainsColumn(column.Id))
            .ToList();
    }

    /// <inheritdoc />
    public string Description => string.Format(Strings.Undo_RemoveColumn, _column.Name);

    /// <inheritdoc />
    public void Execute()
    {
        // 制約を先に外す（カラム削除で構成列候補が作り直される前に取り除く）
        foreach (var (constraint, _) in _affectedConstraints)
        {
            _entity.UniqueConstraints.Remove(constraint);
        }

        _entity.Columns.Remove(_column);
        _afterApply();
    }

    /// <inheritdoc />
    public void Undo()
    {
        // Undo 連打などによる二重挿入を避ける
        if (_entity.Columns.Contains(_column))
        {
            return;
        }

        var insertIndex = Math.Clamp(_index, 0, _entity.Columns.Count);
        _entity.Columns.Insert(insertIndex, _column);

        // カラム復元後、リレーションの FK 設定もスナップショットから復元する
        foreach (var (rel, sourceColumnId, targetColumnId) in _relationshipSnapshots)
        {
            // 復元代入が整合性ロジックを誘発しないよう一時的に抑止する
            rel.SuppressColumnSelectionConsistency = true;

            try
            {
                rel.SourceColumnId = sourceColumnId;
                rel.TargetColumnId = targetColumnId;
            }
            finally
            {
                rel.SuppressColumnSelectionConsistency = false;
            }
        }

        // 巻き添えで消した一意制約を元の位置へ戻す（前から順に挿せば元の並びが再現できる）
        foreach (var (constraint, index) in _affectedConstraints)
        {
            if (_entity.UniqueConstraints.Contains(constraint))
            {
                continue;
            }

            _entity.UniqueConstraints.Insert(
                Math.Clamp(index, 0, _entity.UniqueConstraints.Count),
                constraint
            );
        }

        _afterApply();
    }
}
