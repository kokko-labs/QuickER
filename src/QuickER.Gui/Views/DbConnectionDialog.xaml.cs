using System.Windows;
using System.Windows.Controls;
using QuickER.ViewModels;

namespace QuickER.Views;

/// <summary>DB 接続情報入力ダイアログのコードビハインド（多 DBMS 共通）</summary>
/// <remarks>PasswordBox は WPF の制約上バインドできないため、双方向の変更をコードビハインドで同期する</remarks>
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

        // 前回接続やプロファイル選択で復元されたパスワードを PasswordBox へ初期反映する
        PasswordBoxControl.Password = ViewModel.Password;

        // VM 側 (プロファイル選択時など) で Password が更新されたら PasswordBox にも反映する
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (
                e.PropertyName == nameof(DbConnectionDialogViewModel.Password)
                && PasswordBoxControl.Password != ViewModel.Password
            )
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
