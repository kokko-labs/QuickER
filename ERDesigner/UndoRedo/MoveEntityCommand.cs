using ERDesigner.ViewModels;

namespace ERDesigner.UndoRedo;

/// <summary>エンティティの位置 (X, Y) を移動するコマンド</summary>
/// <remarks>
/// ドラッグ操作では既に位置が変更済みのため <see cref="UndoRedoManager.Push"/> で履歴登録する
/// </remarks>
public class MoveEntityCommand : IUndoableCommand
{
    /// <summary>移動対象のエンティティ</summary>
    private readonly EntityViewModel _entity;

    /// <summary>移動前後の X / Y 座標</summary>
    private readonly double _oldX,
        _oldY,
        _newX,
        _newY;

    /// <summary><see cref="MoveEntityCommand"/> を生成する</summary>
    /// <param name="entity">移動対象のエンティティ</param>
    /// <param name="oldX">移動前の X 座標</param>
    /// <param name="oldY">移動前の Y 座標</param>
    /// <param name="newX">移動後の X 座標</param>
    /// <param name="newY">移動後の Y 座標</param>
    public MoveEntityCommand(
        EntityViewModel entity,
        double oldX,
        double oldY,
        double newX,
        double newY
    )
    {
        _entity = entity;
        _oldX = oldX;
        _oldY = oldY;
        _newX = newX;
        _newY = newY;
    }

    /// <inheritdoc />
    public string Description => $"移動: {_entity.TableName}";

    /// <inheritdoc />
    public void Execute()
    {
        _entity.X = _newX;
        _entity.Y = _newY;
    }

    /// <inheritdoc />
    public void Undo()
    {
        _entity.X = _oldX;
        _entity.Y = _oldY;
    }
}
