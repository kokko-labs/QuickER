using System.Windows;
using QuickER.AI;

namespace QuickER.AI.UI;

/// <summary>WPF の Application.Current.Dispatcher を用いる <see cref="IUiDispatcher"/> の本番実装</summary>
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
