using AwesomeAssertions;
using QuickER.Documents;
using QuickER.Model;
using QuickER.UndoRedo;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.UndoRedo;

/// <summary>
/// エンティティの削除コマンド（<see cref="RemoveEntityCommand"/> / <see cref="GroupRemoveEntitiesCommand"/>）が、
/// そのエンティティの名前付きクエリも取り除き、Undo で元の位置へ戻すことを検証する
/// </summary>
/// <remarks>往復の性質そのものは <see cref="UndoCommandRoundTripTests"/> が網として見る。ここは意図の名指し</remarks>
public class EntityQueryRemovalCommandTests
{
    /// <summary>テーブル名だけを持つテスト用エンティティを生成する</summary>
    private static EntityViewModel NewEntity(string tableName) =>
        new(new Entity { TableName = tableName }, new EntityLayout());

    /// <summary>Order・Customer・Order の順にクエリを並べた ViewModel を用意する</summary>
    private static (MainViewModel Main, EntityViewModel Order, EntityViewModel Customer) Build()
    {
        var main = new MainViewModel();
        var order = NewEntity("Order");
        var customer = NewEntity("Customer");
        main.Entities.Add(order);
        main.Entities.Add(customer);
        main.Queries.Add(new QueryDefinition { EntityId = order.Id, Name = "FindByCustomer" });
        main.Queries.Add(new QueryDefinition { EntityId = customer.Id, Name = "FindByName" });
        main.Queries.Add(new QueryDefinition { EntityId = order.Id, Name = "CountAll" });

        return (main, order, customer);
    }

    /// <summary>ViewModel のクエリ名を並び順のまま返す</summary>
    private static List<string> QueryNames(MainViewModel main) =>
        main.Queries.Select(query => query.Name).ToList();

    [Fact(
        DisplayName = "RemoveEntityCommand: そのエンティティのクエリを取り除き、Undo で元の位置へ戻す"
    )]
    public void RemoveEntityCommand_RemovesAndRestoresQueries()
    {
        var (main, order, _) = Build();
        var original = main.Queries.ToList();
        var command = new RemoveEntityCommand(main, order);

        command.Execute();
        QueryNames(main).Should().Equal("FindByName");

        command.Undo();
        main.Queries.Should()
            .Equal(original, (actual, expected) => ReferenceEquals(actual, expected));

        command.Execute();
        QueryNames(main).Should().Equal("FindByName");
    }

    [Fact(
        DisplayName = "GroupRemoveEntitiesCommand: 対象すべてのクエリを取り除き、Undo で元の位置へ戻す"
    )]
    public void GroupRemoveEntitiesCommand_RemovesAndRestoresQueries()
    {
        var (main, order, customer) = Build();
        var original = main.Queries.ToList();
        var command = new GroupRemoveEntitiesCommand(main, [order, customer]);

        command.Execute();
        main.Queries.Should().BeEmpty();

        command.Undo();
        main.Queries.Should()
            .Equal(original, (actual, expected) => ReferenceEquals(actual, expected));
    }

    [Fact(
        DisplayName = "RemoveEntityCommand: 削除後にクエリ一覧が差し替わっても、Undo は一覧の長さまでで挿し戻す"
    )]
    public void RemoveEntityCommand_Undo_AfterQueriesReplaced_ClampsPosition()
    {
        var (main, order, _) = Build();
        var command = new RemoveEntityCommand(main, order);
        command.Execute();

        // Undo の対象外の操作（クエリ定義ダイアログの確定など）で一覧が空へ差し替わった
        main.Queries.Clear();

        command.Undo();
        QueryNames(main).Should().Equal("FindByCustomer", "CountAll");
    }
}
