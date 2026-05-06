using System.Collections.ObjectModel;
using ERDesigner.ViewModels;

namespace ERDesigner.UndoRedo;

/// <summary>
/// カラムを 1 つ削除するコマンドです。Undo で元の位置へ復元します。
/// </summary>
/// <remarks>
/// カラムが FK として使用されていたリレーションの SourceColumnId/TargetColumnId を
/// 削除前にスナップショット保存し、Undo 時に一緒に復元します。
/// </remarks>
public class RemoveColumnCommand : IUndoableCommand
{
    private readonly ObservableCollection<ColumnViewModel> _columns;
    private readonly ColumnViewModel _column;
    private readonly int _index;

    /// <summary>削除前のリレーション FK スナップショット（リレーションVM → (SourceColumnId, TargetColumnId)）。</summary>
    private readonly IReadOnlyList<(RelationshipViewModel Relationship, Guid? SourceColumnId, Guid? TargetColumnId)> _relationshipSnapshots;

    /// <summary>Undo/Redo 後に FK ルールを再適用するコールバック。</summary>
    private readonly Action _afterApply;

    /// <summary>新しい <see cref="RemoveColumnCommand"/> を生成します。</summary>
    /// <param name="columns">カラムコレクション。</param>
    /// <param name="column">削除するカラム。</param>
    /// <param name="affectedRelationships">削除対象カラムを参照しているリレーション一覧。</param>
    /// <param name="afterApply">Undo/Redo 後に呼ぶ後処理（FK ルール再適用など）。</param>
    public RemoveColumnCommand(ObservableCollection<ColumnViewModel> columns, ColumnViewModel column, IEnumerable<RelationshipViewModel> affectedRelationships, Action afterApply)
    {
        _columns = columns;
        _column = column;
        _index = columns.IndexOf(column);
        _afterApply = afterApply;

        // 削除前の SourceColumnId/TargetColumnId をスナップショット保存する
        _relationshipSnapshots = affectedRelationships.Select(r => (r, r.SourceColumnId, r.TargetColumnId)).ToList();
    }

    /// <inheritdoc />
    public string Description => $"カラム削除: {_column.Name}";

    /// <inheritdoc />
    public void Execute()
    {
        _columns.Remove(_column);
        _afterApply();
    }

    /// <inheritdoc />
    public void Undo()
    {
        if (_columns.Contains(_column))
        {
            return;
        }

        var insertIndex = Math.Clamp(_index, 0, _columns.Count);
        _columns.Insert(insertIndex, _column);

        // カラム復元後、リレーションの FK 設定もスナップショットから復元する
        foreach (var (rel, sourceColumnId, targetColumnId) in _relationshipSnapshots)
        {
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
