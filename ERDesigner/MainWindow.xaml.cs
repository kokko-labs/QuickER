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
            vm.Initialize();
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.AutoSave();
    }
}
