using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FluentAssertions;
using QuickER.CodeGen.UI;
using QuickER.Model;

namespace QuickER.Tests.Views;

/// <summary>
/// <see cref="QueryDefinitionDialog" /> の BAML 読み込み（InitializeComponent）とグループ化配線が
/// 例外を投げないことを検証する（XAML の StaticResource / RelativeSource 参照漏れの回帰防止）。
/// </summary>
public class QueryDefinitionDialogViewTests
{
    /// <summary>STA スレッド上でダイアログを構築し、InitializeComponent が例外を投げないことを検証する</summary>
    [Fact(DisplayName = "QueryDefinitionDialog の InitializeComponent が例外を投げない")]
    public void InitializeComponent_DoesNotThrow()
    {
        WpfApplicationTestSupport.RunSta(() =>
        {
            var entity = new Entity { TableName = "Order" };
            entity.Columns.Add(new Column { Name = "CustomerId", DataType = "int" });
            var diagram = new ErDiagram
            {
                Entities = { entity },
                Queries =
                {
                    new QueryDefinition { EntityId = entity.Id, Name = "GetByCustomer" },
                },
            };

            var viewModel = new QueryDefinitionDialogViewModel(diagram);

            var dialog = new QueryDefinitionDialog(viewModel);

            dialog.ViewModel.Should().BeSameAs(viewModel);
        });
    }

    /// <summary>
    /// クエリ未選択時は右ペインのフォーム（ScrollViewer）が非表示になり、選択すると表示されることを検証する。
    /// </summary>
    /// <remarks>
    /// XAML の DataTrigger（SelectedQuery = null → Collapsed）による配線のため、VM テストでは守れない。
    /// ビジュアルツリーを実体化して Visibility の解決値を確認する（空フォームへの入力を防ぐ回帰テスト）。
    /// </remarks>
    [Fact(DisplayName = "クエリ未選択時は右ペインのフォームが非表示になる")]
    public void FormPane_IsCollapsed_WhenNoQuerySelected()
    {
        WpfApplicationTestSupport.RunSta(() =>
        {
            var entity = new Entity { TableName = "Order" };
            entity.Columns.Add(new Column { Name = "CustomerId", DataType = "int" });
            // クエリ 0 件で開く＝初期状態は未選択
            var diagram = new ErDiagram { Entities = { entity } };

            var viewModel = new QueryDefinitionDialogViewModel(diagram);
            var dialog = new QueryDefinitionDialog(viewModel);

            // Window を Show せずに検証するため論理ツリーから探す（DataTrigger の Style は
            // DataContext 設定済みなら描画なしでも評価される）
            var formPane = FindFormScrollViewer(dialog);
            formPane.Should().NotBeNull("右ペインのフォーム ScrollViewer が存在する前提");

            viewModel.SelectedQuery.Should().BeNull("クエリ 0 件で開いたため未選択");
            formPane!.Visibility.Should().Be(Visibility.Collapsed, "未選択時はフォームを隠す");

            // クエリを追加すると自動選択され、フォームが表示される（DataBind 優先度の反映を待つ）
            viewModel.AddQueryCommand.Execute(null);
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                () => { },
                System.Windows.Threading.DispatcherPriority.DataBind
            );

            viewModel.SelectedQuery.Should().NotBeNull();
            formPane.Visibility.Should().Be(Visibility.Visible, "選択中はフォームを表示する");
        });
    }

    /// <summary>右ペインのフォーム ScrollViewer（ContentControl を子に持つもの）を論理ツリーから探す</summary>
    private static ScrollViewer? FindFormScrollViewer(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is ScrollViewer viewer && viewer.Content is ContentControl)
            {
                return viewer;
            }

            if (
                child is DependencyObject dependency
                && FindFormScrollViewer(dependency) is { } found
            )
            {
                return found;
            }
        }

        return null;
    }
}
