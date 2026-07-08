using System.Collections.ObjectModel;
using QuickER.Resources;
using QuickER.ViewModels;

namespace QuickER.UndoRedo;

/// <summary>カラムを 1 件削除するコマンド（Undo で元の位置へ復元する）</summary>
/// <remarks>
/// 削除カラムを外部キーとして参照するリレーションの SourceColumnId / TargetColumnId を
/// 削除前にスナップショット保存し Undo 時にカラムと併せて復元する
/// </remarks>
public class RemoveColumnCommand : IUndoableCommand
{
    /// <summary>削除対象を含むカラムコレクション</summary>
    private readonly ObservableCollection<ColumnViewModel> _columns;

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

    /// <summary>Undo / Redo 後に FK ルールを再適用する後処理</summary>
    private readonly Action _afterApply;

    /// <summary><see cref="RemoveColumnCommand"/> を生成する</summary>
    /// <param name="columns">削除対象を含むカラムコレクション</param>
    /// <param name="column">削除対象のカラム</param>
    /// <param name="affectedRelationships">削除カラムを参照しているリレーション一覧</param>
    /// <param name="afterApply">Undo / Redo 後に呼ぶ後処理（FK ルール再適用など）</param>
    public RemoveColumnCommand(
        ObservableCollection<ColumnViewModel> columns,
        ColumnViewModel column,
        IEnumerable<RelationshipViewModel> affectedRelationships,
        Action afterApply
    )
    {
        _columns = columns;
        _column = column;
        _index = columns.IndexOf(column);
        _afterApply = afterApply;

        // 削除前の SourceColumnId/TargetColumnId をスナップショット保存する
        _relationshipSnapshots = affectedRelationships
            .Select(r => (r, r.SourceColumnId, r.TargetColumnId))
            .ToList();
    }

    /// <inheritdoc />
    public string Description => string.Format(Strings.Undo_RemoveColumn, _column.Name);

    /// <inheritdoc />
    public void Execute()
    {
        _columns.Remove(_column);
        _afterApply();
    }

    /// <inheritdoc />
    public void Undo()
    {
        // Undo 連打などによる二重挿入を避ける
        if (_columns.Contains(_column))
        {
            return;
        }

        var insertIndex = Math.Clamp(_index, 0, _columns.Count);
        _columns.Insert(insertIndex, _column);

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

        _afterApply();
    }
}
