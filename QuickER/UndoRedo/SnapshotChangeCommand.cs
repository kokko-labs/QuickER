namespace QuickER.UndoRedo;

/// <summary>複数プロパティの変更前後スナップショットを Undo / Redo 時に一括適用する汎用コマンド</summary>
/// <remarks>
/// IsPrimaryKey ↔ IsNullable や RelationshipType ↔ SourceColumnId / TargetColumnId のような
/// 連動するプロパティ変更を 1 履歴として扱う用途を想定する
/// </remarks>
public sealed class SnapshotChangeCommand : IUndoableCommand
{
    /// <summary>スナップショットを適用する対象オブジェクト</summary>
    private readonly object _target;

    /// <summary>変更前のプロパティ値スナップショット（プロパティ名 → 値）</summary>
    private readonly IReadOnlyDictionary<string, object?> _before;

    /// <summary>変更後のプロパティ値スナップショット（プロパティ名 → 値）</summary>
    private readonly IReadOnlyDictionary<string, object?> _after;

    /// <summary>スナップショットを一括適用するコールバック（変更追跡を抑止した文脈で呼ぶ想定）</summary>
    private readonly Action<object, IReadOnlyDictionary<string, object?>> _applySnapshot;

    /// <summary>スナップショット適用後に呼ぶ後処理（FK ルール再適用など）</summary>
    private readonly Action? _afterApply;

    /// <inheritdoc />
    public string Description { get; }

    /// <summary><see cref="SnapshotChangeCommand"/> を生成する</summary>
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
