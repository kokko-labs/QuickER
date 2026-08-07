using System.Collections.Generic;
using System.Linq;
using QuickER.Model;
using QuickER.Resources;
using QuickER.ViewModels;

namespace QuickER.UndoRedo;

/// <summary>カラムを 1 件削除するコマンド（Undo で元の位置へ復元する）</summary>
/// <remarks>
/// 削除カラムを含むリレーションは「列ペアを全クリア」し（線・種別・制約名は残す）、削除前の列ペアを
/// スナップショット保存して Undo 時にカラムと併せて復元する。複合外部キーから 1 組だけ抜くと、
/// 黙って別の意味の外部キー（縮んだキー）へ変質するため。
/// 削除カラムを構成列に含む一意制約も同じ理由で「制約ごと削除」し、同じ Undo 単位で位置ごと復元する
/// </remarks>
public class RemoveColumnCommand : IUndoableCommand
{
    /// <summary>削除対象カラムを保持するエンティティ</summary>
    private readonly EntityViewModel _entity;

    /// <summary>削除対象のカラム</summary>
    private readonly ColumnViewModel _column;

    /// <summary>削除前のインデックス（Undo 時の復元位置）</summary>
    private readonly int _index;

    /// <summary>削除前のリレーション列ペアのスナップショット（リレーション VM と全列ペアの対）</summary>
    private readonly IReadOnlyList<(
        RelationshipViewModel Relationship,
        IReadOnlyList<RelationshipColumnPair> ColumnPairs
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

        // 削除前の列ペアをまるごとスナップショット保存する
        _relationshipSnapshots = affectedRelationships
            .Select(relationship => (relationship, relationship.SnapshotColumnPairs()))
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

        // 削除カラムを含むリレーションは列ペアを全クリアする（縮んだ外部キーへ変質させない）
        foreach (var (relationship, _) in _relationshipSnapshots)
        {
            relationship.SetColumnPairs([]);
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

        // カラム復元後、リレーションの列ペアもスナップショットから復元する
        foreach (var (relationship, columnPairs) in _relationshipSnapshots)
        {
            // 復元代入が整合性ロジックを誘発しないよう一時的に抑止する
            relationship.SuppressColumnSelectionConsistency = true;

            try
            {
                relationship.SetColumnPairs(columnPairs);
            }
            finally
            {
                relationship.SuppressColumnSelectionConsistency = false;
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
