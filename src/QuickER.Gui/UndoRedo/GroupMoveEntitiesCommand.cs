using System.Collections.Generic;
using QuickER.ViewModels;

namespace QuickER.UndoRedo;

/// <summary>複数エンティティのグループ移動を単一の Undo 単位として扱う複合コマンド</summary>
/// <remarks>
/// グループ移動では各メンバーの位置が既にドラッグで変更済みのため <see cref="UndoRedoManager.Push"/> で
/// 履歴登録する（再 Execute はしない）。1 回の Undo で全メンバーが元の座標へ同時に戻る。
/// </remarks>
public sealed class GroupMoveEntitiesCommand : IUndoableCommand
{
    /// <summary>移動対象メンバーと移動前後の座標（適用・取消しに用いる）</summary>
    private readonly IReadOnlyList<(
        EntityViewModel Entity,
        double OldX,
        double OldY,
        double NewX,
        double NewY
    )> _moves;

    /// <summary><see cref="GroupMoveEntitiesCommand"/> を生成する</summary>
    /// <param name="moves">各メンバーの移動前後の座標</param>
    public GroupMoveEntitiesCommand(
        IReadOnlyList<(
            EntityViewModel Entity,
            double OldX,
            double OldY,
            double NewX,
            double NewY
        )> moves
    )
    {
        _moves = moves;
    }

    /// <inheritdoc />
    public string Description => $"グループ移動: {_moves.Count} 個";

    /// <inheritdoc />
    public void Execute()
    {
        foreach (var move in _moves)
        {
            move.Entity.X = move.NewX;
            move.Entity.Y = move.NewY;
        }
    }

    /// <inheritdoc />
    public void Undo()
    {
        foreach (var move in _moves)
        {
            move.Entity.X = move.OldX;
            move.Entity.Y = move.OldY;
        }
    }
}
