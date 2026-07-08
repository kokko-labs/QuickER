using System.Globalization;
using System.Threading;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using QuickER.AI.UI;
using QuickER.Gui.Abstractions;
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
            services.AddSingleton<IAiChatLauncher, AiChatLauncher>();
            services.AddSingleton<IMockGenerationLauncher, MockGenerationLauncher>();

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
                serviceProvider.GetRequiredService<IAiChatLauncher>(),
                serviceProvider.GetRequiredService<IMockGenerationLauncher>(),
                serviceProvider.GetRequiredService<DatabaseProviderRegistry>()
            ));
            services.AddTransient<MainWindow>();

            _provider = services.BuildServiceProvider();

            _provider.GetRequiredService<MainWindow>().Show();
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
