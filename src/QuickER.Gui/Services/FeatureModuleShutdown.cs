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

/// <summary>機能モジュールの後始末を、プロセスで 1 回だけ行う</summary>
/// <remarks>
/// 後始末の入口は 2 つある＝メインウィンドウの <c>Closing</c>（正常終了）とクラッシュ経路
/// （<c>Closing</c> を通らずに <see cref="Environment.Exit(int)"/> で終わる）。終了処理の途中でクラッシュすると
/// 両方から呼ばれるため、2 回目は何もしない（モジュールの終了処理を二重に走らせない）。
/// </remarks>
internal sealed class FeatureModuleShutdownOnce
{
    /// <summary>同梱されているフィーチャーモジュール</summary>
    private readonly IReadOnlyList<IFeatureModule> _modules;

    /// <summary>モジュールが自分のサービスを解決する DI コンテナ</summary>
    private readonly IServiceProvider _services;

    /// <summary>後始末の失敗を記録する口</summary>
    private readonly IShutdownFailureReporter _reporter;

    /// <summary>実行済みか（0 = 未実行・1 = 実行中もしくは実行済み）</summary>
    private int _closed;

    /// <summary>後始末の対象と記録先を受け取る</summary>
    public FeatureModuleShutdownOnce(
        IReadOnlyList<IFeatureModule> modules,
        IServiceProvider services,
        IShutdownFailureReporter reporter
    )
    {
        _modules = modules ?? throw new ArgumentNullException(nameof(modules));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _reporter = reporter ?? throw new ArgumentNullException(nameof(reporter));
    }

    /// <summary>全モジュールへ後始末を通知する。2 回目以降の呼び出しは何もしない</summary>
    public void CloseAll()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0)
        {
            return;
        }

        FeatureModuleShutdown.CloseAll(_modules, _services, _reporter);
    }
}
