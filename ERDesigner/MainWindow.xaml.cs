using System.ComponentModel;
using System.Windows;
using ERDesigner.ViewModels;

namespace ERDesigner;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Closing += MainWindow_Closing;

        if (DataContext is MainViewModel vm)
        {
            vm.Initialize();
        }
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.AutoSave();

            // メイン画面終了時に Codex チャット画面も強制終了する
            vm.CloseCodexDialog();
        }
    }
}
