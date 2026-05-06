using ERDesigner.ViewModels;

namespace ERDesigner.UndoRedo;

/// <summary>リレーションを追加するコマンドです。</summary>
public class AddRelationshipCommand : IUndoableCommand
{
    private readonly MainViewModel _main;
    private readonly RelationshipViewModel _rel;

    /// <summary>新しい <see cref="AddRelationshipCommand"/> を生成します。</summary>
    public AddRelationshipCommand(MainViewModel main, RelationshipViewModel rel)
    {
        _main = main;
        _rel = rel;
    }

    /// <inheritdoc />
    public string Description => "リレーション追加";

    /// <inheritdoc />
    public void Execute() => _main.Relationships.Add(_rel);

    /// <inheritdoc />
    public void Undo() => _main.Relationships.Remove(_rel);
}
