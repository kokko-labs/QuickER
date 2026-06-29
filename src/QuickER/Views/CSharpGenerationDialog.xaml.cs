using System.Windows;
using QuickER.ViewModels;

namespace QuickER.Views;

/// <summary>C# コード生成ダイアログのコードビハインド</summary>
/// <remarks>画面制御（DataContext 結線・閉じる要求の受け）のみを担い、操作ロジックは ViewModel に置く</remarks>
public partial class CSharpGenerationDialog : Window
{
    /// <summary>このダイアログの ViewModel</summary>
    public CSharpGenerationDialogViewModel ViewModel { get; }

    /// <summary>注入された ViewModel を結び付けてダイアログを生成する</summary>
    public CSharpGenerationDialog(CSharpGenerationDialogViewModel viewModel)
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
