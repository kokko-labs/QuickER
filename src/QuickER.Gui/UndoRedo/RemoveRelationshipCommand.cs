using System.Collections.Generic;
using QuickER.Resources;
using QuickER.ViewModels;

namespace QuickER.UndoRedo;

/// <summary>リレーションを削除するコマンド（Undo で元の位置へ戻す）</summary>
/// <remarks>
/// 末尾へ戻すと、名前を持たない外部キーの既定名（並びの先頭から割り当てる）が入れ替わり、
/// 次の DDL や同期が実在する DB に対して偽の drop と add を出す。位置は <see cref="IndexedRemoval"/> で記録・復元する
/// </remarks>
public class RemoveRelationshipCommand : IUndoableCommand
{
    /// <summary>削除を行うメイン ViewModel</summary>
    private readonly MainViewModel _main;

    /// <summary>削除対象のリレーション</summary>
    private readonly RelationshipViewModel _rel;

    /// <summary>取り除いたリレーションと元の位置（Undo の復元元）</summary>
    private List<(int Index, RelationshipViewModel Item)> _removed = new();

    /// <summary><see cref="RemoveRelationshipCommand"/> を生成する</summary>
    public RemoveRelationshipCommand(MainViewModel main, RelationshipViewModel rel)
    {
        _main = main;
        _rel = rel;
    }

    /// <inheritdoc />
    public string Description => Strings.Undo_RemoveRelationship;

    /// <inheritdoc />
    public void Execute()
    {
        _removed = IndexedRemoval.RemoveWhere(_main.Relationships, r => ReferenceEquals(r, _rel));
        // 削除後の状態に合わせて外部キー列のルールを再適用する
        _main.ApplyRelationshipColumnRules();
    }

    /// <inheritdoc />
    public void Undo()
    {
        IndexedRemoval.Restore(_main.Relationships, _removed);
        // 復元したリレーションに対し外部キー列の付与・更新を再評価する
        _main.ApplyRelationshipColumnRules();
    }
}
