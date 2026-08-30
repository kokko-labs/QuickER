using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Extensibility;
using QuickER.Gui.Abstractions;
using QuickER.Gui.Common;
using QuickER.MySql;
using QuickER.Oracle;
using QuickER.PostgreSql;
using QuickER.Provider;
using QuickER.Resources;
using QuickER.Services;
using QuickER.Sqlite;
using QuickER.SqlServer;
using QuickER.ViewModels;

namespace QuickER
{
    /// <summary>アプリケーションのエントリポイント（App.xaml のコードビハインド）</summary>
    /// <remarks>
    /// 起動時に DI コンテナを構築し、<see cref="MainWindow"/> を解決して表示する。
    /// View → ViewModel の結線は DI が担い、XAML の <c>StartupUri</c> は使用しない。
    /// </remarks>
    public partial class App : Application
    {
        /// <summary>
        /// クラッシュログを書けなかった場合に、ダイアログのログパス欄へ差し込む代替表記。
        /// 診断向けの機械可読な短句のため UI 言語に追従させない（英語固定）。
        /// </summary>
        private const string CrashLogUnavailable = "(not available)";

        /// <summary>アプリ全体の DI コンテナ（終了時に破棄する）</summary>
        private ServiceProvider? _provider;

        /// <summary>解決済みの主 ViewModel（クラッシュ時の緊急保存対象。未構築なら null）</summary>
        /// <remarks>
        /// クラッシュハンドラから <c>GetService</c> で取り直すと、まだ生成されていない場合に
        /// その場でシングルトンを構築してしまう（＝空の図を復旧ファイルへ書き戻しかねない）。
        /// 起動時に解決した実体だけを退避対象とするため、ここへ保持する。
        /// </remarks>
        private MainViewModel? _mainViewModel;

        /// <summary>DI コンテナを構築し、メインウィンドウを解決して表示する</summary>
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 未捕捉例外の受け皿を最初期に張る（以降の初期化中に落ちても緊急保存・ログ・報告が働く）。
            // 非 UI スレッドの即死経路（AppDomain）は Program.Main 側で購読済み。
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            // 起動最初期に表示言語のカルチャを適用する。
            // 切替は再起動反映方式のため、ウィンドウ生成前のここで一度だけ設定する。
            ApplyUiCulture();

            var services = new ServiceCollection();

            services.AddSingleton<IDialogService, MessageBoxDialogService>();
            services.AddSingleton<IAppDialogService, WpfAppDialogService>();
            services.AddSingleton<IFileDialogService, WpfFileDialogService>();

            // フィーチャーモジュールへ ER 図操作能力を提供する契約実装（MainViewModel を包む）
            services.AddSingleton<IErDiagramHost>(sp => new MainViewModelErDiagramHost(
                sp.GetRequiredService<MainViewModel>()
            ));

            // DB プロバイダを登録し、識別名で解決するレジストリをシングルトンで供給する
            // 新 DBMS 対応時は IDatabaseProvider 実装を追加登録するだけで済む
            services.AddSingleton<IDatabaseProvider, SqlServerProvider>();
            services.AddSingleton<IDatabaseProvider, PostgreSqlProvider>();
            services.AddSingleton<IDatabaseProvider, MySqlProvider>();
            services.AddSingleton<IDatabaseProvider, OracleProvider>();
            services.AddSingleton<IDatabaseProvider, SqliteProvider>();
            services.AddSingleton(serviceProvider => new DatabaseProviderRegistry(
                serviceProvider.GetServices<IDatabaseProvider>()
            ));

            services.AddSingleton<MainViewModel>(serviceProvider => new MainViewModel(
                serviceProvider.GetRequiredService<IDialogService>(),
                serviceProvider.GetRequiredService<IAppDialogService>(),
                serviceProvider.GetRequiredService<IFileDialogService>(),
                serviceProvider.GetRequiredService<DatabaseProviderRegistry>()
            ));
            services.AddTransient<MainWindow>();

            // 同梱フィーチャーモジュールを列挙し、各モジュールが必要とするサービスを登録する
            var modules = FeatureModuleCatalog.CreateModules();

            foreach (var module in modules)
            {
                module.ConfigureServices(services);
            }

            // 起動時更新チェックサービス。本番用ファクトリ（feed => Velopack 実装）と
            // 実環境変数によるフィード解決（UpdateFeed.Resolve）を注入する。
            services.AddSingleton(serviceProvider => new UpdateService(
                serviceProvider.GetRequiredService<IDialogService>(),
                feed => new VelopackAppUpdater(feed),
                () => UpdateFeed.Resolve(Environment.GetEnvironmentVariable)
            ));

            _provider = services.BuildServiceProvider();

            // 各モジュールを初期化する（ホストイベント購読などの準備。ツールバー寄与の生成より前に行う）
            foreach (var module in modules)
            {
                module.Initialize(_provider);
            }

            // 各モジュールのツールバー寄与を集約し、主 ViewModel へ流し込む
            var mainViewModel = _provider.GetRequiredService<MainViewModel>();
            _mainViewModel = mainViewModel;
            var toolbarItems = modules
                .SelectMany(module => module.CreateToolbarItems(_provider))
                .ToList();

            // 集約後の全体先頭ボタンは BeginsGroup を必ず false にする。
            // ItemsControl の直前は対象 DB 選択グループ（＋その手前の静的セパレータ）なので、
            // 先頭ボタンが自前の区切りを持つと二重の区切りになる。そのため
            // 各モジュールが自前で持つ先頭 BeginsGroup を全体先頭でだけ矯正する
            // （DB ツールを外した構成で AI やコード生成モジュールが先頭へ来る場合にも効く）。
            if (toolbarItems.Count > 0)
            {
                toolbarItems[0].BeginsGroup = false;
            }

            mainViewModel.FeatureToolbarItems = toolbarItems;

            var window = _provider.GetRequiredService<MainWindow>();

            // メインウィンドウ終了時に各モジュールへ後始末（モードレスウィンドウの強制終了など）を通知する
            window.Closing += (_, _) =>
            {
                foreach (var module in modules)
                {
                    module.OnMainWindowClosing(_provider);
                }
            };

            window.Show();

            // クラッシュハンドラの実挙動（緊急保存・ログ・ダイアログ・終了コード）を実起動で検証するための
            // 隠しトリガー。環境変数を立てたときだけ UI スレッドで意図的に例外を投げる（通常起動には影響しない）。
            if (Environment.GetEnvironmentVariable("QUICKER_CRASH_TEST") == "1")
            {
                _ = Dispatcher.BeginInvoke(() =>
                    throw new InvalidOperationException("Crash handler test")
                );
            }

            // 起動を阻害しない fire-and-forget での更新チェック。
            // 例外・フィード未設定・非インストール実行はすべて UpdateService 内で処理済み。
            _ = _provider.GetRequiredService<UpdateService>().CheckOnStartupAsync();
        }

        /// <summary>
        /// UI スレッドの未捕捉例外を受け、緊急保存 → クラッシュログ → 報告ダイアログの順に処理してから終了する。
        /// </summary>
        /// <remarks>
        /// 終了に <see cref="Application.Shutdown()"/> を使わないのは、MainWindow の Closing 経由で
        /// 通常の自動保存が「壊れた ViewModel 状態」で走り、直前に採った緊急保存スナップショットを
        /// 上書きしうるため。プロセスを即座に終える <see cref="Environment.Exit(int)"/> を用いる。
        /// </remarks>
        private void OnDispatcherUnhandledException(
            object sender,
            DispatcherUnhandledExceptionEventArgs e
        )
        {
            var version = CrashHandlingService.ResolveAppVersion();

            CrashHandlingService.HandleCrash(
                e.Exception,
                version,
                TryEmergencySave,
                logPath => ShowCrashDialog(e.Exception, version, logPath)
            );

            // WPF 既定の即死（未処理例外による強制終了）を抑止し、こちらの手順で終了する
            e.Handled = true;
            Environment.Exit(1);
        }

        /// <summary>
        /// 取りこぼした Task 例外を記録する（アプリは継続させる）。
        /// </summary>
        /// <remarks>
        /// UI の破綻を伴わないことが多く、終了させるとかえって作業を失わせるため証跡だけ残し、
        /// <see cref="UnobservedTaskExceptionEventArgs.SetObserved"/> でプロセス終了を防ぐ。
        /// </remarks>
        private static void OnUnobservedTaskException(
            object? sender,
            UnobservedTaskExceptionEventArgs e
        )
        {
            CrashHandlingService.WriteCrashLog(
                e.Exception,
                CrashHandlingService.ResolveAppVersion()
            );
            e.SetObserved();
        }

        /// <summary>現在の編集内容を復旧用の自動保存ファイルへ緊急退避する</summary>
        private void TryEmergencySave()
        {
            // DI 構築前（起動最初期）のクラッシュでは退避対象そのものが無いため何もしない
            _mainViewModel?.TryEmergencyAutoSave();
        }

        /// <summary>クラッシュの要約と詳細（コピー可能）を提示する報告ダイアログを表示する</summary>
        /// <param name="exception">発生した未捕捉例外</param>
        /// <param name="version">アプリのバージョン文字列</param>
        /// <param name="logPath">書き出したクラッシュログのパス（書けなかった場合は null）</param>
        private static void ShowCrashDialog(Exception exception, string version, string? logPath)
        {
            var message = string.Format(
                CultureInfo.CurrentCulture,
                Strings.Crash_Message,
                logPath ?? CrashLogUnavailable
            );

            var dialog = new InformationDetailsDialog(
                message,
                CrashHandlingService.FormatDetails(exception, version),
                Strings.Crash_DialogTitle,
                isError: true,
                copyButtonText: Strings.Crash_CopyDetails
            );

            var owner = Application.Current?.MainWindow;

            // 未表示のウィンドウを Owner にすると WPF が例外を投げるため、表示済みのときだけ紐付ける
            // （起動最初期のクラッシュでは Owner なしで中央に出す）
            if (owner is not null && owner.IsLoaded)
            {
                dialog.Owner = owner;
            }

            dialog.ShowDialog();
        }

        /// <summary>
        /// 保存された言語設定（未設定なら OS 言語から導出）を実効カルチャとして UI へ適用する。
        /// 以降に生成されるスレッド既定と現在スレッドの <see cref="Thread.CurrentUICulture"/> を上書きする。
        /// </summary>
        private static void ApplyUiCulture()
        {
            var settings = new GuiAppSettingsStore().Load();
            var languageCode = AppLanguage.Resolve(settings.Language, CultureInfo.CurrentUICulture);
            var culture = new CultureInfo(languageCode);

            CultureInfo.DefaultThreadCurrentUICulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
        }

        /// <summary>終了時に DI コンテナを破棄する</summary>
        protected override void OnExit(ExitEventArgs e)
        {
            _provider?.Dispose();
            base.OnExit(e);
        }
    }
}
