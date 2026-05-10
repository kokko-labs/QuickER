using ERDesigner.ViewModels;

namespace ERDesigner.UndoRedo;

/// <summary>
/// 整列前後のエンティティ座標を保持し、Undo/Redo 時に一括適用するコマンドです。
/// </summary>
public sealed class ArrangeEntitiesCommand : IUndoableCommand
{
    private readonly IReadOnlyList<EntityViewModel> _entities;
    private readonly IReadOnlyDictionary<Guid, (double X, double Y)> _before;
    private readonly IReadOnlyDictionary<Guid, (double X, double Y)> _after;
    private readonly Action? _afterApply;

    /// <summary>新しい <see cref="ArrangeEntitiesCommand" /> を生成します。</summary>
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

    /// <summary>保存済み座標を全エンティティへ適用します。</summary>
    private void Apply(IReadOnlyDictionary<Guid, (double X, double Y)> snapshot)
    {
        foreach (var entity in _entities)
        {
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
