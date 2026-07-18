using System.Windows;

namespace QuickER.Db.UI;

/// <summary>DB スキーマ同期ダイアログのコードビハインド</summary>
public partial class SchemaSyncDialog : Window
{
    /// <summary>このダイアログの ViewModel</summary>
    public SchemaSyncDialogViewModel ViewModel { get; }

    /// <summary>ダイアログを生成し、表示時に差分を自動取得する</summary>
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
        // 表示完了時点で DB との差分を取得し、初期表示に反映する
        Loaded += async (_, _) => await ViewModel.RefreshCommand.ExecuteAsync(null);
    }
}
