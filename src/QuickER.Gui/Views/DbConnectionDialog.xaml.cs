using System.Windows;
using QuickER.ViewModels;

namespace QuickER.Views;

/// <summary>DB 接続情報入力ダイアログのコードビハインド（多 DBMS 共通）</summary>
/// <remarks>PasswordBox の双方向同期は <see cref="PasswordBoxBehavior"/> 添付ビヘイビアが担う</remarks>
public partial class DbConnectionDialog : Window
{
    /// <summary>このダイアログの ViewModel</summary>
    public DbConnectionDialogViewModel ViewModel { get; }

    /// <summary>注入された ViewModel を結び付けてダイアログを生成する</summary>
    public DbConnectionDialog(DbConnectionDialogViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        ViewModel.CloseAction = result =>
        {
            DialogResult = result;
            Close();
        };

        DataContext = ViewModel;
    }
}
