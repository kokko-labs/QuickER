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

    /// <summary>追加の直前の、子列になる列の外部キーの印（Undo で戻す）</summary>
    private ForeignKeyMarkSnapshot? _marks;

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
        // 子列になる列の印を控える（手で付けた印は、子列になるとリレーションの管理に移る）
        _marks = ForeignKeyMarkSnapshot.Capture(_rel.Target, _rel.ColumnPairs);
        _main.Relationships.Add(_rel);
    }

    /// <inheritdoc />
    public void Undo()
    {
        _main.Relationships.Remove(_rel);
        _marks?.Restore();
    }
}
