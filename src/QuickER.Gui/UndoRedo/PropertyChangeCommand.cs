namespace QuickER.UndoRedo;

/// <summary>任意オブジェクトのプロパティ 1 件の変更を Undo / Redo するコマンド</summary>
/// <remarks>変更前後の値と対象プロパティのアクセサを渡して生成・登録する</remarks>
public class PropertyChangeCommand : IUndoableCommand
{
    /// <summary>プロパティを保持する対象オブジェクト</summary>
    private readonly object _target;

    /// <summary>変更対象プロパティの型安全アクセサ</summary>
    private readonly ITrackedProperty _property;

    /// <summary>変更前の値（Undo 時に復元する）</summary>
    private readonly object? _oldValue;

    /// <summary>変更後の値（Execute / Redo 時に適用する）</summary>
    private readonly object? _newValue;

    /// <summary>Execute / Undo 後に呼ぶ後処理</summary>
    private readonly Action? _afterApply;

    /// <summary><see cref="PropertyChangeCommand"/> を生成する</summary>
    /// <param name="target">プロパティを保持する対象オブジェクト</param>
    /// <param name="property">変更対象プロパティのアクセサ</param>
    /// <param name="oldValue">変更前の値</param>
    /// <param name="newValue">変更後の値</param>
    /// <param name="afterApply">Execute / Undo 後に呼ぶ後処理</param>
    public PropertyChangeCommand(
        object target,
        ITrackedProperty property,
        object? oldValue,
        object? newValue,
        Action? afterApply = null
    )
    {
        _target = target;
        _property = property;
        _oldValue = oldValue;
        _newValue = newValue;
        _afterApply = afterApply;
    }

    /// <summary>連動する複数変更を 1 履歴へ集約するためのグループ ID</summary>
    public object? GroupId { get; init; }

    /// <summary>変更対象オブジェクト</summary>
    public object Target => _target;

    /// <summary>変更対象プロパティ名</summary>
    public string PropertyName => _property.Name;

    /// <inheritdoc />
    public string Description => $"変更: {_property.Name}";

    /// <inheritdoc />
    public void Execute()
    {
        _property.SetValue(_target, _newValue);
        _afterApply?.Invoke();
    }

    /// <inheritdoc />
    public void Undo()
    {
        _property.SetValue(_target, _oldValue);
        _afterApply?.Invoke();
    }
}
