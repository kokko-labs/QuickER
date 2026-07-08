using QuickER.Resources;
using QuickER.ViewModels;

namespace QuickER.UndoRedo;

/// <summary>リレーションを削除するコマンド</summary>
public class RemoveRelationshipCommand : IUndoableCommand
{
    /// <summary>削除を行うメイン ViewModel</summary>
    private readonly MainViewModel _main;

    /// <summary>削除対象のリレーション</summary>
    private readonly RelationshipViewModel _rel;

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
        _main.Relationships.Remove(_rel);
        // 削除後の状態に合わせて外部キー列のルールを再適用する
        _main.ApplyRelationshipColumnRules();
    }

    /// <inheritdoc />
    public void Undo()
    {
        _main.Relationships.Add(_rel);
        // 復元したリレーションに対し外部キー列の付与・更新を再評価する
        _main.ApplyRelationshipColumnRules();
    }
}
