using System.Reflection;

namespace ERDesigner.UndoRedo;

/// <summary>
/// 任意オブジェクトのプロパティをコマンドとして変更する汎用クラスです。
/// </summary>
/// <remarks>
/// 例）テキストボックスの LostFocus 時に、編集前値と現在値を渡して生成・登録する使い方を想定しています。
/// </remarks>
public class PropertyChangeCommand : IUndoableCommand
{
    private readonly object _target;
    private readonly PropertyInfo _property;
    private readonly object? _oldValue;
    private readonly object? _newValue;
    private readonly Action? _afterApply;

    /// <summary>新しい <see cref="PropertyChangeCommand"/> を生成します。</summary>
    /// <param name="target">プロパティを保持するオブジェクト。</param>
    /// <param name="propertyName">変更するプロパティ名。</param>
    /// <param name="oldValue">変更前の値。</param>
    /// <param name="newValue">変更後の値。</param>
    public PropertyChangeCommand(object target, string propertyName, object? oldValue, object? newValue, Action? afterApply = null)
    {
        _target = target;
        _property = target.GetType().GetProperty(propertyName) ?? throw new ArgumentException($"Property '{propertyName}' not found.");
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
