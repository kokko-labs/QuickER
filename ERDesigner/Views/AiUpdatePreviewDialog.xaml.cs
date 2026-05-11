using System.Windows;
using ERDesigner.Services;
using ERDesigner.ViewModels;

namespace ERDesigner.Views;

/// <summary>
/// AI 更新差分プレビューのダイアログです。
/// </summary>
public partial class AiUpdatePreviewDialog : Window
{
    /// <summary>このダイアログの ViewModel です。</summary>
    public AiUpdatePreviewDialogViewModel ViewModel { get; }

    /// <summary>新しいダイアログを生成します。</summary>
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

    /// <summary>TreeView の選択変更を ViewModel に反映します。</summary>
    private void DiffTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is AiUpdateDiffItem item)
        {
            ViewModel.SelectedItem = item;
        }
    }
}
