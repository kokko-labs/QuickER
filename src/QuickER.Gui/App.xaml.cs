using System.Globalization;
using System.Threading;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Extensibility;
using QuickER.Gui.Abstractions;
using QuickER.Gui.Common;
using QuickER.MySql;
using QuickER.Oracle;
using QuickER.PostgreSql;
using QuickER.Provider;
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
        /// <summary>アプリ全体の DI コンテナ（終了時に破棄する）</summary>
        private ServiceProvider? _provider;

        /// <summary>DI コンテナを構築し、メインウィンドウを解決して表示する</summary>
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

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
            // 環境変数取得（Environment.GetEnvironmentVariable）を注入する。
            services.AddSingleton(serviceProvider => new UpdateService(
                serviceProvider.GetRequiredService<IDialogService>(),
                feed => new VelopackAppUpdater(feed),
                Environment.GetEnvironmentVariable
            ));

            _provider = services.BuildServiceProvider();

            // 各モジュールを初期化する（ホストイベント購読などの準備。ツールバー寄与の生成より前に行う）
            foreach (var module in modules)
            {
                module.Initialize(_provider);
            }

            // 各モジュールのツールバー寄与を集約し、主 ViewModel へ流し込む
            var mainViewModel = _provider.GetRequiredService<MainViewModel>();
            var toolbarItems = modules
                .SelectMany(module => module.CreateToolbarItems(_provider))
                .ToList();

            // 集約後の全体先頭ボタンは BeginsGroup を必ず false にする。
            // ItemsControl の直前は対象 DB 選択グループ（＋その手前の静的セパレータ）で、
            // 従来は先頭の DB 取込ボタンがそこへ区切りなしで続いていた。先頭ボタンが区切りを持つと
            // 二重の区切りになるため、各モジュールが自前で持つ先頭 BeginsGroup を全体先頭でだけ矯正する
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

            // 起動を阻害しない fire-and-forget での更新チェック。
            // 例外・フィード未設定・非インストール実行はすべて UpdateService 内で処理済み。
            _ = _provider.GetRequiredService<UpdateService>().CheckOnStartupAsync();
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
