using ERDesigner.ViewModels;

namespace ERDesigner.UndoRedo;

/// <summary>整列前後のエンティティ座標を保持し Undo/Redo 時に一括適用するコマンド</summary>
public sealed class ArrangeEntitiesCommand : IUndoableCommand
{
    /// <summary>座標を適用する対象エンティティ群</summary>
    private readonly IReadOnlyList<EntityViewModel> _entities;

    /// <summary>整列前の座標スナップショット（エンティティ ID をキーとする）</summary>
    private readonly IReadOnlyDictionary<Guid, (double X, double Y)> _before;

    /// <summary>整列後の座標スナップショット（エンティティ ID をキーとする）</summary>
    private readonly IReadOnlyDictionary<Guid, (double X, double Y)> _after;

    /// <summary>座標適用後に実行する後処理（キャンバスサイズ再計算など）</summary>
    private readonly Action? _afterApply;

    /// <summary><see cref="ArrangeEntitiesCommand" /> を生成する</summary>
    public ArrangeEntitiesCommand(
        IReadOnlyList<EntityViewModel> entities,
        IReadOnlyDictionary<Guid, (double X, double Y)> before,
        IReadOnlyDictionary<Guid, (double X, double Y)> after,
        Action? afterApply,
        string description
    )
    {
        _entities = entities;
        _before = before;
        _after = after;
        _afterApply = afterApply;
        Description = description;
    }

    /// <inheritdoc />
    public string Description { get; }

    /// <inheritdoc />
    public void Execute()
    {
        Apply(_after);
    }

    /// <inheritdoc />
    public void Undo()
    {
        Apply(_before);
    }

    /// <summary>保存済み座標スナップショットを全エンティティへ適用する</summary>
    private void Apply(IReadOnlyDictionary<Guid, (double X, double Y)> snapshot)
    {
        foreach (var entity in _entities)
        {
            // スナップショットに無いエンティティ（整列対象外）は座標を変更しない
            if (!snapshot.TryGetValue(entity.Id, out var position))
            {
                continue;
            }

            entity.X = position.X;
            entity.Y = position.Y;
        }

        _afterApply?.Invoke();
    }
}
