using System.Windows;

namespace QuickER.Services.Chat;

/// <summary>UI スレッドへ処理をマーシャリングする抽象（テストでは同期実装に差し替える）</summary>
public interface IUiDispatcher
{
    /// <summary>UI スレッドで関数を実行し結果を返す（既に UI スレッドなら即時実行）</summary>
    T Invoke<T>(Func<T> func);
}

/// <summary>WPF の Application.Current.Dispatcher を用いる本番実装</summary>
public sealed class WpfUiDispatcher : IUiDispatcher
{
    /// <inheritdoc />
    public T Invoke<T>(Func<T> func)
    {
        var dispatcher = Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess())
        {
            return func();
        }

        return dispatcher.Invoke(func);
    }
}
