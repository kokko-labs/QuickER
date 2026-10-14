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

    /// <summary>取り除いたカラムと元の位置（Undo の復元元）</summary>
    private List<(int Index, ColumnViewModel Item)> _removedColumns = new();

    /// <summary>削除前のリレーション列ペアのスナップショット（リレーション VM と全列ペアの対）</summary>
    private readonly IReadOnlyList<(
        RelationshipViewModel Relationship,
        IReadOnlyList<RelationshipColumnPair> ColumnPairs
    )> _relationshipSnapshots;

    /// <summary>巻き添えで取り除いた一意制約と元の位置（Undo の復元元）</summary>
    private List<(int Index, UniqueConstraintViewModel Item)> _removedConstraints = new();

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
        _afterApply = afterApply;

        // 削除前の列ペアをまるごとスナップショット保存する
        _relationshipSnapshots = affectedRelationships
            .Select(relationship => (relationship, relationship.SnapshotColumnPairs()))
            .ToList();
    }

    /// <inheritdoc />
    public string Description => string.Format(Strings.Undo_RemoveColumn, _column.Name);

    /// <inheritdoc />
    public void Execute()
    {
        // 削除カラムを構成列に含む一意制約を、元の位置を記録して先に外す（カラム削除で構成列候補が作り直される前に取り除く）
        _removedConstraints = IndexedRemoval.RemoveWhere(
            _entity.UniqueConstraints,
            constraint => constraint.ContainsColumn(_column.Id)
        );

        // 削除カラムを含むリレーションは列ペアを全クリアする（縮んだ外部キーへ変質させない）
        foreach (var (relationship, _) in _relationshipSnapshots)
        {
            relationship.SetColumnPairs([]);
        }

        _removedColumns = IndexedRemoval.RemoveWhere(
            _entity.Columns,
            column => ReferenceEquals(column, _column)
        );
        _afterApply();
    }

    /// <inheritdoc />
    public void Undo()
    {
        IndexedRemoval.Restore(_entity.Columns, _removedColumns);

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

        // 巻き添えで消した一意制約を元の位置へ戻す
        IndexedRemoval.Restore(_entity.UniqueConstraints, _removedConstraints);

        _afterApply();
    }
}
