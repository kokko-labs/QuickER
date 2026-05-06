using System.Windows;
using ERDesigner.ViewModels;

namespace ERDesigner.Views;

/// <summary>
/// DB スキーマ同期ダイアログのコードビハインド。
/// </summary>
public partial class SchemaSyncDialog : Window
{
    /// <summary>このダイアログの ViewModel。</summary>
    public SchemaSyncDialogViewModel ViewModel { get; }

    /// <summary>新しいダイアログを生成します。</summary>
    public SchemaSyncDialog(SchemaSyncDialogViewModel vm)
    {
        InitializeComponent();
        ViewModel = vm;
        ViewModel.CloseAction = result =>
        {
            DialogResult = result;
            Close();
        };

        DataContext = ViewModel;
        Loaded += async (_, _) => await ViewModel.RefreshCommand.ExecuteAsync(null);
    }
}
