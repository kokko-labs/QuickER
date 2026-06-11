namespace ERDesigner.UndoRedo;

/// <summary>
/// 任意オブジェクトのプロパティをコマンドとして変更する汎用クラスです。
/// </summary>
/// <remarks>
/// 編集前値と現在値、対象プロパティのアクセサを渡して生成・登録する使い方を想定しています。
/// </remarks>
public class PropertyChangeCommand : IUndoableCommand
{
    private readonly object _target;
    private readonly ITrackedProperty _property;
    private readonly object? _oldValue;
    private readonly object? _newValue;
    private readonly Action? _afterApply;

    /// <summary>新しい <see cref="PropertyChangeCommand"/> を生成します。</summary>
    /// <param name="target">プロパティを保持するオブジェクト。</param>
    /// <param name="property">変更するプロパティのアクセサ。</param>
    /// <param name="oldValue">変更前の値。</param>
    /// <param name="newValue">変更後の値。</param>
    /// <param name="afterApply">Execute / Undo 後に呼び出すアクション。</param>
    public PropertyChangeCommand(object target, ITrackedProperty property, object? oldValue, object? newValue, Action? afterApply = null)
    {
        _target = target;
        _property = property;
        _oldValue = oldValue;
        _newValue = newValue;
        _afterApply = afterApply;
    }

    /// <summary>関連変更を 1 セットで扱うためのグループ ID です。</summary>
    public object? GroupId { get; init; }

    /// <summary>変更対象オブジェクトです。</summary>
    public object Target => _target;

    /// <summary>変更対象プロパティ名です。</summary>
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
