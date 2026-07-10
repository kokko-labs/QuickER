using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;

namespace QuickER.CodeGen.UI;

/// <summary>名前付きクエリ定義エディタのコードビハインド</summary>
/// <remarks>
/// 画面制御（DataContext 結線・閉じる要求の受け・一覧のエンティティ別グループ化）のみを担い、
/// 操作ロジックは ViewModel に置く。
/// </remarks>
public partial class QueryDefinitionDialog : Window
{
    /// <summary>このダイアログの ViewModel</summary>
    public QueryDefinitionDialogViewModel ViewModel { get; }

    /// <summary>注入された ViewModel を結び付けてダイアログを生成する</summary>
    public QueryDefinitionDialog(QueryDefinitionDialogViewModel viewModel)
    {
        InitializeComponent();

        ViewModel = viewModel;
        ViewModel.CloseAction = result =>
        {
            DialogResult = result;
            Close();
        };

        DataContext = ViewModel;

        // クエリ一覧をエンティティ名でグループ表示する（ライブグループでリネームにも追従）。
        var view = CollectionViewSource.GetDefaultView(ViewModel.Queries);
        view.GroupDescriptions.Clear();
        view.GroupDescriptions.Add(
            new PropertyGroupDescription(nameof(QueryItemViewModel.EntityName))
        );

        if (view is ListCollectionView liveView)
        {
            liveView.IsLiveGrouping = true;
            liveView.LiveGroupingProperties.Add(nameof(QueryItemViewModel.EntityName));
        }

        // エンティティ変更（EntityName 変更）でライブグループが項目を別グループへ移動すると、
        // ListBox の選択が解除され SelectedQuery が null になる（編集中のフォームが消える）。
        // 項目自体は一覧に残っているため、再配置による選択解除を検出して選択を復元する。
        // 単一選択の ListBox はユーザー操作で「選択なし」にはならないため、復元しても操作と衝突しない。
        QueryList.SelectionChanged += (_, e) =>
        {
            if (
                QueryList.SelectedItem is null
                && e.RemovedItems.Count == 1
                && e.RemovedItems[0] is QueryItemViewModel item
                && ViewModel.Queries.Contains(item)
            )
            {
                // その場で選択を戻す（単一選択の ListBox は SelectionChanged 内の再選択を許容する）
                QueryList.SelectedItem = item;
            }
        };
    }
}
