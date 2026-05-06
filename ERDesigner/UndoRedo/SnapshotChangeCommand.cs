namespace ERDesigner.UndoRedo;

/// <summary>
/// 複数プロパティの変更前後スナップショットを保持し、Undo/Redo 時に一括適用する汎用コマンドです。
/// IsPrimaryKey ↔ IsNullable のような連動変更や、
/// RelationshipType ↔ SourceColumnId/TargetColumnId のような連動変更に使います。
/// </summary>
public sealed class SnapshotChangeCommand : IUndoableCommand
{
    private readonly object _target;
    private readonly IReadOnlyDictionary<string, object?> _before;
    private readonly IReadOnlyDictionary<string, object?> _after;

    /// <summary>プロパティを一括適用するコールバック（RunWithoutUndoTracking 内で呼ぶことを想定）。</summary>
    private readonly Action<object, IReadOnlyDictionary<string, object?>> _applySnapshot;

    /// <summary>プロパティ適用後に呼ぶ後処理（FK ルール再適用など）。</summary>
    private readonly Action? _afterApply;

    /// <inheritdoc />
    public string Description { get; }

    /// <summary>新しい <see cref="SnapshotChangeCommand"/> を生成します。</summary>
    public SnapshotChangeCommand(
        object target,
        IReadOnlyDictionary<string, object?> before,
        IReadOnlyDictionary<string, object?> after,
        Action<object, IReadOnlyDictionary<string, object?>> applySnapshot,
        Action? afterApply = null,
        string description = "プロパティ変更"
    )
    {
        _target = target;
        _before = before;
        _after = after;
        _applySnapshot = applySnapshot;
        _afterApply = afterApply;
        Description = description;
    }

    /// <inheritdoc />
    public void Execute()
    {
        _applySnapshot(_target, _after);
        _afterApply?.Invoke();
    }

    /// <inheritdoc />
    public void Undo()
    {
        _applySnapshot(_target, _before);
        _afterApply?.Invoke();
    }
}
