namespace QuickER.AI;

/// <summary>UI スレッドへ処理をマーシャリングする抽象（テストでは同期実装に差し替える）</summary>
/// <remarks>WPF 実装 <c>WpfUiDispatcher</c> はアプリ側に置く（このライブラリは WPF 非依存）</remarks>
public interface IUiDispatcher
{
    /// <summary>UI スレッドで関数を実行し結果を返す（既に UI スレッドなら即時実行）</summary>
    T Invoke<T>(Func<T> func);
}
