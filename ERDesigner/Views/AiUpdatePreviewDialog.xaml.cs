using System.Windows;
using ERDesigner.Services;
using ERDesigner.ViewModels;

namespace ERDesigner.Views;

/// <summary>AI 更新差分プレビューダイアログのコードビハインド</summary>
public partial class AiUpdatePreviewDialog : Window
{
    /// <summary>このダイアログの ViewModel</summary>
    public AiUpdatePreviewDialogViewModel ViewModel { get; }

    /// <summary>ダイアログを生成し、ViewModel を関連付ける</summary>
    public AiUpdatePreviewDialog(AiUpdatePreviewDialogViewModel viewModel)
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

    /// <summary>差分 TreeView の選択変更を ViewModel へ反映する</summary>
    /// <remarks>TreeView.SelectedItem は読み取り専用でバインドできないためイベントで橋渡しする</remarks>
    private void DiffTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is AiUpdateDiffItem item)
        {
            ViewModel.SelectedItem = item;
        }
    }
}
