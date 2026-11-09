using System.Windows;
using System.Windows.Input;

namespace QuickER.Behaviors;

/// <summary>
/// キャンバスのビヘイビアが、マウスのボタン・位置・修飾キーを読み、ドラッグ＆ドロップを始める唯一の口
/// </summary>
/// <remarks>
/// <para>
/// 差し替えが無いとき（製品）は、WPF の値をそのまま返す（<see cref="MouseEventArgs.LeftButton"/>・
/// <see cref="MouseEventArgs.GetPosition"/>・<see cref="Keyboard.Modifiers"/>・<see cref="DragDrop.DoDragDrop"/>）。
/// </para>
/// <para>
/// WPF はマウスのボタンと修飾キーを実際の機器の状態から、位置を実際のカーソルから読み、ドラッグ＆ドロップは
/// OS の入れ子のループで実際のボタンとカーソルを見る。入力の列を流すテストのエンジンは、これらを擬似の入力では
/// 変えられないので、呼び出したスレッドの間だけ差し替える（<see cref="OverrideForTests"/>）。
/// ビヘイビアがこの口を迂回して読むと、エンジンが見る値と製品が読む値が食い違うので、ソースの規則
/// <c>behavior-input-reads</c> が迂回を断る。
/// </para>
/// </remarks>
internal static class BehaviorInput
{
    /// <summary>呼び出したスレッドの差し替え先（テストだけが入れる。null なら WPF の値）</summary>
    [ThreadStatic]
    private static IBehaviorInputSource? _override;

    /// <summary>左ボタンの状態</summary>
    internal static MouseButtonState LeftButton(MouseEventArgs e) =>
        _override is { } source ? source.LeftButton : e.LeftButton;

    /// <summary>中ボタンの状態</summary>
    internal static MouseButtonState MiddleButton(MouseEventArgs e) =>
        _override is { } source ? source.MiddleButton : e.MiddleButton;

    /// <summary>指定要素を基準にしたマウス位置（null は入力元のルート要素を基準にする）</summary>
    internal static Point GetPosition(MouseEventArgs e, IInputElement? relativeTo) =>
        _override is { } source ? source.GetPosition(relativeTo) : e.GetPosition(relativeTo);

    /// <summary>押されている修飾キー</summary>
    internal static ModifierKeys Modifiers =>
        _override is { } source ? source.Modifiers : Keyboard.Modifiers;

    /// <summary>ドラッグ＆ドロップを始める</summary>
    internal static DragDropEffects DoDragDrop(
        DependencyObject dragSource,
        object data,
        DragDropEffects allowedEffects
    ) =>
        _override is { } source
            ? source.DoDragDrop(dragSource, data, allowedEffects)
            : DragDrop.DoDragDrop(dragSource, data, allowedEffects);

    /// <summary>テスト専用: 呼び出したスレッドの読み口を差し替え、破棄で元へ戻す</summary>
    /// <param name="source">差し替え先</param>
    /// <returns>破棄すると差し替える前の値へ戻すスコープ</returns>
    internal static IDisposable OverrideForTests(IBehaviorInputSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var previous = _override;
        _override = source;
        return new Scope(previous);
    }

    /// <summary>テスト専用: 呼び出したスレッドに差し替えが残っているか</summary>
    internal static bool IsOverriddenForTests => _override is not null;

    /// <summary>差し替える前の値へ戻すスコープ（二重の破棄では何もしない）</summary>
    private sealed class Scope(IBehaviorInputSource? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _override = previous;
        }
    }
}

/// <summary><see cref="BehaviorInput"/> の差し替え先（テストのエンジンが実装する）</summary>
internal interface IBehaviorInputSource
{
    /// <summary>左ボタンの状態</summary>
    MouseButtonState LeftButton { get; }

    /// <summary>中ボタンの状態</summary>
    MouseButtonState MiddleButton { get; }

    /// <summary>押されている修飾キー</summary>
    ModifierKeys Modifiers { get; }

    /// <summary>指定要素を基準にしたマウス位置（null は入力元のルート要素を基準にする）</summary>
    Point GetPosition(IInputElement? relativeTo);

    /// <summary>ドラッグ＆ドロップを始める（入れ子のループを回さずに戻る）</summary>
    DragDropEffects DoDragDrop(
        DependencyObject dragSource,
        object data,
        DragDropEffects allowedEffects
    );
}
