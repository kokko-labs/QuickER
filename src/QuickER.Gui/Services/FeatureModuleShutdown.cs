using QuickER.Extensibility;
using QuickER.Gui.Abstractions;

namespace QuickER.Services;

/// <summary>メインウィンドウの終了時に、各フィーチャーモジュールへ後始末を通知する</summary>
/// <remarks>
/// モジュールの後始末は互いに独立（AI チャット・モック生成のモードレスウィンドウを閉じる等）なので、
/// 1 つが失敗しても残りへ必ず届かせる。届かないと、そのモジュールが抱えた常駐プロセス
/// （codex app-server・copilot ランタイム・claude / dotnet の子プロセス）が孤児として残る。
/// ループを <c>App</c> のイベントハンドラから切り出しているのは、この受け止めを直接検証できるようにするため。
/// </remarks>
internal static class FeatureModuleShutdown
{
    /// <summary>全モジュールへ後始末を通知する（1 つの失敗で他を止めない）</summary>
    /// <param name="modules">同梱されているフィーチャーモジュール</param>
    /// <param name="services">モジュールが自分のサービスを解決する DI コンテナ</param>
    /// <param name="reporter">後始末の失敗を記録する口</param>
    public static void CloseAll(
        IEnumerable<IFeatureModule> modules,
        IServiceProvider services,
        IShutdownFailureReporter reporter
    )
    {
        ArgumentNullException.ThrowIfNull(modules);
        ArgumentNullException.ThrowIfNull(reporter);

        foreach (var module in modules)
        {
            ShutdownSteps.Run(
                reporter,
                $"FeatureModule.{module.Id}.OnMainWindowClosing",
                () => module.OnMainWindowClosing(services)
            );
        }
    }
}
