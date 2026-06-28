using System.ComponentModel;
using System.Windows;
using QuickER.ViewModels;

namespace QuickER;

/// <summary>アプリケーションのメインウィンドウ（MainWindow.xaml のコードビハインド）</summary>
public partial class MainWindow : Window
{
    /// <summary>ウィンドウを初期化し、DataContext の ViewModel を起動する</summary>
    public MainWindow()
    {
        InitializeComponent();
        Closing += MainWindow_Closing;

        if (DataContext is MainViewModel vm)
        {
            vm.Initialize();
        }
    }

    /// <summary>ウィンドウ終了時に自動保存と AI チャット画面の終了を行う</summary>
    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.AutoSave();

            // メイン画面終了時に AI チャット画面も強制終了する
            vm.CloseAiChatDialog();
        }
    }
}
