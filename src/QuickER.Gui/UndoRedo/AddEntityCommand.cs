using QuickER.ViewModels;

namespace QuickER.UndoRedo;

/// <summary>エンティティを 1 件追加するコマンド（Undo で同じエンティティを削除する）</summary>
public class AddEntityCommand : IUndoableCommand
{
    /// <summary>追加先のメイン ViewModel</summary>
    private readonly MainViewModel _main;

    /// <summary>追加対象のエンティティ</summary>
    private readonly EntityViewModel _entity;

    /// <summary><see cref="AddEntityCommand"/> を生成する</summary>
    /// <param name="main">追加先のメイン ViewModel</param>
    /// <param name="entity">追加対象のエンティティ</param>
    public AddEntityCommand(MainViewModel main, EntityViewModel entity)
    {
        _main = main;
        _entity = entity;
    }

    /// <inheritdoc />
    public string Description => $"エンティティ追加: {_entity.TableName}";

    /// <inheritdoc />
    public void Execute() => _main.Entities.Add(_entity);

    /// <inheritdoc />
    public void Undo() => _main.Entities.Remove(_entity);
}
