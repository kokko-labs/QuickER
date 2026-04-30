using ERDesigner.ViewModels;

namespace ERDesigner.UndoRedo;

/// <summary>
/// エンティティを 1 つ追加するコマンドです。Undo で同じエンティティが削除されます。
/// </summary>
public class AddEntityCommand : IUndoableCommand
{
    private readonly MainViewModel _main;
    private readonly EntityViewModel _entity;

    /// <summary>新しい <see cref="AddEntityCommand"/> を生成します。</summary>
    /// <param name="main">追加先の <see cref="MainViewModel"/>。</param>
    /// <param name="entity">追加するエンティティ。</param>
    public AddEntityCommand(MainViewModel main, EntityViewModel entity)
    {
        _main = main;
        _entity = entity;
    }

    /// <inheritdoc />
    public string Description => $"エンティティ追加: {_entity.DisplayName}";

    /// <inheritdoc />
    public void Execute() => _main.Entities.Add(_entity);

    /// <inheritdoc />
    public void Undo() => _main.Entities.Remove(_entity);
}
