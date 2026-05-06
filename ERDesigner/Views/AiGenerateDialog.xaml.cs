using System.Windows;
using System.Windows.Controls;
using ERDesigner.ViewModels;

namespace ERDesigner.Views;

/// <summary>
/// AI スキーマ生成ダイアログ。PasswordBox は WPF の制約上バインドできないためコードビハインドで VM へ反映します。
/// </summary>
public partial class AiGenerateDialog : Window
{
    /// <summary>このダイアログの ViewModel を取得します。</summary>
    public AiGenerateDialogViewModel ViewModel { get; }

    /// <summary>新しいダイアログを生成します。</summary>
    public AiGenerateDialog()
    {
        InitializeComponent();
        ViewModel = new AiGenerateDialogViewModel
        {
            CloseAction = result =>
            {
                DialogResult = result;
                Close();
            },
        };

        DataContext = ViewModel;

        // 保存済み API キーがあれば PasswordBox に反映
        if (!string.IsNullOrEmpty(ViewModel.ApiKey))
        {
            ApiKeyBox.Password = ViewModel.ApiKey;
        }
    }

    /// <summary>PasswordBox の変更を ViewModel に転送します。</summary>
    private void ApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox pb)
        {
            ViewModel.ApiKey = pb.Password;
        }
    }
}
