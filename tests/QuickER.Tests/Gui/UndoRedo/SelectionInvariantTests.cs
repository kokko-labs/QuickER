using AwesomeAssertions;
using QuickER.Model;
using QuickER.Tests.TestSupport.Engines;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.UndoRedo;

/// <summary>
/// 選択・検索結果・作成中のリレーションの始点が、図に居る要素（<see cref="MainViewModel.Entities"/> など）だけを指すことを、
/// 図を変える操作と Undo / Redo の全経路で表明するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// 図から外れた ViewModel を選択が指したままだと、プロパティパネルが追跡の外れた要素を編集し（履歴にも未保存にもならない）、
/// Redo で同じインスタンスが戻ると記録のない編集ごと現れる。削除を実行すると、Id が同じ生きている表の
/// 名前付きクエリまで消える（再読込後に古い検索結果から選んだ場合）。ビルドも型検査も通るので、不変条件として固定する。
/// </para>
/// <para>
/// 不変条件: <c>SelectedEntity</c> は <c>Entities</c> の要素か null・<c>SelectedRelationship</c> は <c>Relationships</c> の要素か null・
/// <c>SelectedColumn</c> は <c>SelectedEntity</c> の列か null・<c>SearchResults</c> の各エンティティは <c>Entities</c> の要素・
/// 作成中の始点は <c>Entities</c> の要素か null。
/// </para>
/// </remarks>
public class SelectionInvariantTests
{
    /// <summary>顧客・注文の 2 テーブルとリレーション 1 本、注文のクエリ 1 本を持つ図を読み込む</summary>
    internal static MainViewModel Build()
    {
        var customerId = new Column
        {
            Name = "Id",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var customer = new Entity { TableName = "Customers" };
        customer.Columns.Add(customerId);

        var orderId = new Column
        {
            Name = "Id",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var orderCustomerId = new Column
        {
            Name = "CustomerId",
            DataType = "int",
            IsNullable = true,
        };
        var order = new Entity { TableName = "Orders" };
        order.Columns.Add(orderId);
        order.Columns.Add(orderCustomerId);

        var relationship = new Relationship
        {
            SourceEntityId = customer.Id,
            TargetEntityId = order.Id,
            Type = RelationshipType.OneToMany,
            ConstraintName = "FK_Orders_Customers",
            ColumnPairs =
            {
                new RelationshipColumnPair
                {
                    SourceColumnId = customerId.Id,
                    TargetColumnId = orderCustomerId.Id,
                },
            },
        };

        var vm = new MainViewModel();
        vm.ReplaceDiagramFromModule(
            new ErDiagram
            {
                Entities = { customer, order },
                Relationships = { relationship },
                Queries =
                {
                    new QueryDefinition
                    {
                        EntityId = order.Id,
                        Name = "FindByCustomer",
                        Returns = QueryReturnShape.List,
                        Condition = "CustomerId = @customerId",
                        Parameters =
                        {
                            new QueryParameter { Name = "customerId", Type = "int" },
                        },
                    },
                },
            }
        );

        return vm;
    }

    /// <summary>行の Id（<see cref="SelectionInvariantRows"/>）</summary>
    public static TheoryData<string> Rows =>
        new(SelectionInvariantRows.Table.Select(row => row.Id));

    [Theory(DisplayName = "図から外れる・戻る操作の各手順のあとも、選択は図に居る要素だけを指す")]
    [MemberData(nameof(Rows))]
    public void Row_KeepsInvariants(string id) =>
        UndoKnownFailures.Run(
            id,
            () => SelectionScenarioRunner.Run(SelectionInvariantRows.Table[id])
        );

    [Fact(DisplayName = "選択の不変条件の行は、外れ得るもの × 外れ方の組を宣言どおりに網羅する")]
    public void Rows_CoverSelectionDimensions()
    {
        var counter = new CoverageCounter(SelectionInvariantDimensions.Table);
        SelectionInvariantDimensions.ObserveDeclarations(counter);

        var report = counter.Report();
        report.WriteSummary();
        report.WriteGapUsage(SelectionInvariantDimensions.Gaps, SelectionInvariantDimensions.Scope);
        report.AssertGaps(SelectionInvariantDimensions.Gaps, SelectionInvariantDimensions.Scope);
    }
}
