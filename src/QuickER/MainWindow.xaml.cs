using System.ComponentModel;
using System.Windows;
using QuickER.ViewModels;

namespace QuickER;

/// <summary>アプリケーションのメインウィンドウ（MainWindow.xaml のコードビハインド）</summary>
public partial class MainWindow : Window
{
    /// <summary>ウィンドウ全体で参照する主 ViewModel</summary>
    private readonly MainViewModel _viewModel;

    /// <summary>DI から注入された ViewModel を DataContext に結び、起動処理を行う</summary>
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = viewModel;

        viewModel.Initialize();
        Closing += MainWindow_Closing;
    }

    /// <summary>ウィンドウ終了時に自動保存と AI チャット画面の終了を行う</summary>
    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        _viewModel.AutoSave();

        // メイン画面終了時に AI チャット画面も強制終了する
        _viewModel.CloseAiChatDialog();
    }
}
