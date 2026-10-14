using QuickER.Resources;
using QuickER.ViewModels;

namespace QuickER.UndoRedo;

/// <summary>エンティティと接続中の全リレーション・そのエンティティの名前付きクエリを削除するコマンド（Undo で復元する）</summary>
/// <remarks>退避・復元・主選択の戻し方は <see cref="EntityRemovalCommandBase"/> が複数の削除と共有する</remarks>
public sealed class RemoveEntityCommand : EntityRemovalCommandBase
{
    /// <summary>削除対象のエンティティ</summary>
    private readonly EntityViewModel _entity;

    /// <summary><see cref="RemoveEntityCommand"/> を生成する</summary>
    /// <param name="main">削除を行うメイン ViewModel</param>
    /// <param name="entity">削除対象のエンティティ</param>
    public RemoveEntityCommand(MainViewModel main, EntityViewModel entity)
        : base(main, [entity])
    {
        _entity = entity;
    }

    /// <inheritdoc />
    public override string Description =>
        string.Format(Strings.Undo_RemoveEntity, _entity.TableName);
}
