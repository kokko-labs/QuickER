using QuickER.ViewModels;

namespace QuickER.UndoRedo;

/// <summary>リレーションを追加するコマンド</summary>
public class AddRelationshipCommand : IUndoableCommand
{
    /// <summary>追加先のメイン ViewModel</summary>
    private readonly MainViewModel _main;

    /// <summary>追加対象のリレーション</summary>
    private readonly RelationshipViewModel _rel;

    /// <summary><see cref="AddRelationshipCommand"/> を生成する</summary>
    public AddRelationshipCommand(MainViewModel main, RelationshipViewModel rel)
    {
        _main = main;
        _rel = rel;
    }

    /// <inheritdoc />
    public string Description => "リレーション追加";

    /// <inheritdoc />
    public void Execute()
    {
        _main.Relationships.Add(_rel);
        // リレーション追加に伴う外部キー列の付与・更新を再評価する
        _main.ApplyRelationshipColumnRules();
    }

    /// <inheritdoc />
    public void Undo()
    {
        _main.Relationships.Remove(_rel);
        // 削除後の状態に合わせて外部キー列のルールを再適用する
        _main.ApplyRelationshipColumnRules();
    }
}
