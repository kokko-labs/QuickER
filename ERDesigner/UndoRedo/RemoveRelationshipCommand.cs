using ERDesigner.ViewModels;

namespace ERDesigner.UndoRedo;

/// <summary>リレーションを削除するコマンドです。</summary>
public class RemoveRelationshipCommand : IUndoableCommand
{
    private readonly MainViewModel _main;
    private readonly RelationshipViewModel _rel;

    /// <summary>新しい <see cref="RemoveRelationshipCommand"/> を生成します。</summary>
    public RemoveRelationshipCommand(MainViewModel main, RelationshipViewModel rel)
    {
        _main = main;
        _rel = rel;
    }

    /// <inheritdoc />
    public string Description => "リレーション削除";

    /// <inheritdoc />
    public void Execute()
    {
        _main.Relationships.Remove(_rel);
        _main.ApplyRelationshipColumnRules();
    }

    /// <inheritdoc />
    public void Undo()
    {
        _main.Relationships.Add(_rel);
        _main.ApplyRelationshipColumnRules();
    }
}
