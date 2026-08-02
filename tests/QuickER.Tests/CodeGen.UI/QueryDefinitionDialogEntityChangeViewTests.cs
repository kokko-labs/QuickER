using System.Linq;
using System.Windows.Threading;
using AwesomeAssertions;
using QuickER.CodeGen.UI;
using QuickER.Model;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.CodeGen.UI;

/// <summary>
/// 実体化した Window 上でクエリのエンティティを変更しても、例外にならず選択が維持されることを検証する。
/// </summary>
/// <remarks>
/// エンティティ変更は EntityName の変更としてライブグループへ伝わり、項目が別グループへ再配置される。
/// このとき ListBox の選択が解除されて SelectedQuery が null になり「編集中のフォームが消える」不具合が
/// あった（コードビハインドの選択復元で修正）。ライブグループと選択の相互作用は VM テスト・論理ツリーでは
/// 実体化されないため、Window を Show（画面外・非アクティブ）して検証する。
/// </remarks>
public class QueryDefinitionDialogEntityChangeViewTests
{
    [Fact(DisplayName = "エンティティ変更（ライブグループ再配置）でも例外にならず選択が維持される")]
    public void ChangingEntity_WithRealizedWindow_KeepsSelection()
    {
        WpfApplicationTestSupport.RunSta(() =>
        {
            var order = new Entity { TableName = "Order" };
            var customerCol = new Column
            {
                Name = "CustomerId",
                DataType = "int",
                IsPrimaryKey = true,
                IsNullable = false,
            };
            var amountCol = new Column { Name = "Amount", DataType = "decimal(12,2)" };
            order.Columns.Add(customerCol);
            order.Columns.Add(amountCol);

            var product = new Entity { TableName = "Product" };
            product.Columns.Add(
                new Column
                {
                    Name = "ProductId",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                }
            );

            var diagram = new ErDiagram
            {
                Entities = { order, product },
                Queries =
                {
                    new QueryDefinition
                    {
                        EntityId = order.Id,
                        Name = "GetByCustomer",
                        Condition = "CustomerId = @customerId",
                        Parameters =
                        {
                            new QueryParameter { Name = "customerId", Type = "int32" },
                        },
                        OrderBy =
                        {
                            new QueryOrdering { ColumnId = amountCol.Id, Descending = true },
                        },
                        HasPaging = true,
                    },
                },
            };

            var viewModel = new QueryDefinitionDialogViewModel(diagram);
            // 画面外・非アクティブで Show する（ライブグループと選択の実配線を実体化するため）。
            // BAML ロードは並列テストと競合しないよう直列化する
            var dialog = WpfApplicationTestSupport.LoadXamlComponent(() =>
                new QueryDefinitionDialog(viewModel)
                {
                    WindowStartupLocation = System.Windows.WindowStartupLocation.Manual,
                    Left = -4000,
                    Top = -4000,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                }
            );

            try
            {
                dialog.Show();
                DoEvents();

                var query = viewModel.SelectedQuery!;
                query.Should().NotBeNull();

                // 実 UI（ライブグループ・バインディング実体化済み）の上でエンティティを変更する
                query.EntityId = product.Id;
                DoEvents();

                // 再配置後も選択が維持され、編集フォームの対象が失われない
                viewModel
                    .SelectedQuery.Should()
                    .BeSameAs(query, "再配置による選択解除は復元される");

                // 列参照の掃除と選択肢の入れ替えも実 UI 上で機能する
                query.AvailableColumns.Select(c => c.Name).Should().Equal("ProductId");
                query.OrderBy.Should().BeEmpty();
                query.IsConditionValid.Should().BeFalse("旧エンティティの列参照は診断で表面化する");
            }
            finally
            {
                dialog.Close();
                DoEvents();
            }
        });
    }

    /// <summary>ディスパッチャキューを Background 優先度まで排出する（ライブグループ再配置・選択復元の反映）</summary>
    private static void DoEvents()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.Background,
            () => frame.Continue = false
        );
        Dispatcher.PushFrame(frame);
    }
}
