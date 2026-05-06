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

    /// <summary>新しい <see cref="PropertyChangeCommand"/> を生成します。</summary>
    /// <param name="target">プロパティを保持するオブジェクト。</param>
    /// <param name="propertyName">変更するプロパティ名。</param>
    /// <param name="oldValue">変更前の値。</param>
    /// <param name="newValue">変更後の値。</param>
    public PropertyChangeCommand(object target, string propertyName, object? oldValue, object? newValue)
    {
        _target = target;
        _property = target.GetType().GetProperty(propertyName) ?? throw new ArgumentException($"Property '{propertyName}' not found.");
        _oldValue = oldValue;
        _newValue = newValue;
    }

    /// <inheritdoc />
    public string Description => $"変更: {_property.Name}";

    /// <inheritdoc />
    public void Execute() => _property.SetValue(_target, _newValue);

    /// <inheritdoc />
    public void Undo() => _property.SetValue(_target, _oldValue);
}
