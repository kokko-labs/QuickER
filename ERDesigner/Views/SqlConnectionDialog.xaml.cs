using System.Windows;
using System.Windows.Controls;
using ERDesigner.ViewModels;

namespace ERDesigner.Views;

/// <summary>SQL Server 接続情報入力ダイアログのコードビハインド</summary>
/// <remarks>PasswordBox は WPF の制約上バインドできないため、双方向の変更をコードビハインドで同期する</remarks>
public partial class SqlConnectionDialog : Window
{
    /// <summary>このダイアログの ViewModel</summary>
    public SqlConnectionDialogViewModel ViewModel { get; }

    /// <summary>ダイアログを生成し、ViewModel を関連付ける</summary>
    public SqlConnectionDialog()
    {
        InitializeComponent();
        ViewModel = new SqlConnectionDialogViewModel
        {
            CloseAction = result =>
            {
                DialogResult = result;
                Close();
            },
        };

        DataContext = ViewModel;
        // VM 側 (プロファイル選択時など) で Password が更新されたら PasswordBox にも反映する
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SqlConnectionDialogViewModel.Password) && PasswordBoxControl.Password != ViewModel.Password)
            {
                PasswordBoxControl.Password = ViewModel.Password;
            }
        };
    }

    /// <summary>PasswordBox の変更内容を ViewModel へ反映する</summary>
    private void PasswordBoxControl_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox pb)
        {
            ViewModel.Password = pb.Password;
        }
    }
}
