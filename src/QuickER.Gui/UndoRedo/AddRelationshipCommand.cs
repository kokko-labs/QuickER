using QuickER.Resources;
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
    public string Description => Strings.Undo_AddRelationship;

    /// <inheritdoc />
    public void Execute()
    {
        _main.Relationships.Add(_rel);
    }

    /// <inheritdoc />
    public void Undo()
    {
        _main.Relationships.Remove(_rel);
    }
}
