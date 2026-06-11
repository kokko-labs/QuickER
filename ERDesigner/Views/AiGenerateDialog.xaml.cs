using System.Windows;
using System.Windows.Controls;
using ERDesigner.ViewModels;

namespace ERDesigner.Views;

/// <summary>AI スキーマ生成ダイアログのコードビハインド</summary>
/// <remarks>PasswordBox は WPF の制約上バインドできないため、コードビハインドで ViewModel へ反映する</remarks>
public partial class AiGenerateDialog : Window
{
    /// <summary>このダイアログの ViewModel</summary>
    public AiGenerateDialogViewModel ViewModel { get; }

    /// <summary>ダイアログを生成し、既存ダイアグラムがあれば更新モードで初期化する</summary>
    public AiGenerateDialog(Models.ErDiagram? existingDiagram = null)
    {
        InitializeComponent();
        ViewModel = new AiGenerateDialogViewModel(existingDiagram: existingDiagram)
        {
            CloseAction = result =>
            {
                DialogResult = result;
                Close();
            },
        };

        DataContext = ViewModel;

        // 保存済み API キーがあれば PasswordBox の初期値として反映する
        if (!string.IsNullOrEmpty(ViewModel.ApiKey))
        {
            ApiKeyBox.Password = ViewModel.ApiKey;
        }
    }

    /// <summary>PasswordBox の変更内容を ViewModel へ転送する</summary>
    private void ApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox pb)
        {
            ViewModel.ApiKey = pb.Password;
        }
    }
}
