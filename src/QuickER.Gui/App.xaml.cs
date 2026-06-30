using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Services;
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

            var services = new ServiceCollection();

            services.AddSingleton<IDialogService, MessageBoxDialogService>();
            services.AddSingleton<IAppDialogService, WpfAppDialogService>();
            services.AddSingleton<IFileDialogService, WpfFileDialogService>();
            services.AddSingleton<IAiChatLauncher, AiChatLauncher>();
            services.AddSingleton<MainViewModel>(serviceProvider => new MainViewModel(
                serviceProvider.GetRequiredService<IDialogService>(),
                serviceProvider.GetRequiredService<IAppDialogService>(),
                serviceProvider.GetRequiredService<IFileDialogService>(),
                serviceProvider.GetRequiredService<IAiChatLauncher>()
            ));
            services.AddTransient<MainWindow>();

            _provider = services.BuildServiceProvider();

            _provider.GetRequiredService<MainWindow>().Show();
        }

        /// <summary>終了時に DI コンテナを破棄する</summary>
        protected override void OnExit(ExitEventArgs e)
        {
            _provider?.Dispose();
            base.OnExit(e);
        }
    }
}
