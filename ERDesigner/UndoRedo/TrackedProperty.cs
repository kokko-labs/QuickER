namespace ERDesigner.UndoRedo;

/// <summary>追跡対象プロパティ 1 件分の型安全な読み書きアクセサ</summary>
/// <remarks>
/// プロパティ名文字列によるリフレクションを避け、コンパイル時に検証される
/// getter / setter デリゲート経由で値を読み書きする
/// </remarks>
public interface ITrackedProperty
{
    /// <summary>PropertyChanged イベント名と照合するためのプロパティ名</summary>
    string Name { get; }

    /// <summary>対象オブジェクトから現在値を取得する</summary>
    object? GetValue(object target);

    /// <summary>対象オブジェクトへ値を設定する</summary>
    void SetValue(object target, object? value);
}

/// <summary>getter / setter デリゲートによる <see cref="ITrackedProperty"/> 実装</summary>
/// <typeparam name="T">対象オブジェクトの型</typeparam>
public sealed class TrackedProperty<T>(
    string name,
    Func<T, object?> getter,
    Action<T, object?> setter
) : ITrackedProperty
{
    /// <inheritdoc />
    public string Name { get; } = name;

    /// <inheritdoc />
    public object? GetValue(object target) => getter((T)target);

    /// <inheritdoc />
    public void SetValue(object target, object? value) => setter((T)target, value);
}
