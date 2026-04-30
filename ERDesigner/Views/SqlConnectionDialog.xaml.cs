using System.Windows;
using ERDesigner.ViewModels;

namespace ERDesigner.Views;

/// <summary>
/// SQL Server 接続情報入力ダイアログのコードビハインド。
/// PasswordBox は WPF の制約上バインドできないため、変更を VM へ転送します。
/// </summary>
public partial class SqlConnectionDialog : Window
{
    /// <summary>このダイアログの ViewModel を取得します。</summary>
    public SqlConnectionDialogViewModel ViewModel { get; }

    /// <summary>新しいダイアログを生成し、ViewModel を関連付けます。</summary>
    public SqlConnectionDialog()
    {
        InitializeComponent();
        ViewModel = new SqlConnectionDialogViewModel
        {
            CloseAction = result =>
            {
                DialogResult = result;
                Close();
            }
        };
        DataContext = ViewModel;
        // VM 側 (プロファイル選択時など) で Password が更新されたら PasswordBox にも反映する
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SqlConnectionDialogViewModel.Password)
                && PasswordBoxControl.Password != ViewModel.Password)
            {
                PasswordBoxControl.Password = ViewModel.Password;
            }
        };
    }

    /// <summary>PasswordBox の変更を ViewModel へ反映します。</summary>
    private void PasswordBoxControl_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.PasswordBox pb)
            ViewModel.Password = pb.Password;
    }
}
