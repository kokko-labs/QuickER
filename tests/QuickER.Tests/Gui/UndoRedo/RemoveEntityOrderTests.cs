using AwesomeAssertions;
using QuickER.Documents;
using QuickER.Model;
using QuickER.UndoRedo;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.UndoRedo;

/// <summary>
/// エンティティの削除コマンド（<see cref="RemoveEntityCommand"/> / <see cref="GroupRemoveEntitiesCommand"/>）の Undo が、
/// エンティティと巻き添えのリレーションを元の位置へ戻すことを検証する
/// </summary>
/// <remarks>
/// 末尾へ戻すと、削除して Undo しただけで図の順序が変わる（保存内容・DDL の順序・重なり順に現れる）。
/// 名前付きクエリの位置は <see cref="EntityQueryRemovalCommandTests"/> が見る。
/// </remarks>
public class RemoveEntityOrderTests
{
    /// <summary>A・B・C・D の 4 テーブルと、A→B・C→D・A→C の順のリレーションを持つ ViewModel を用意する</summary>
    private static MainViewModel Build()
    {
        var main = new MainViewModel();

        foreach (var name in new[] { "A", "B", "C", "D" })
        {
            main.Entities.Add(
                new EntityViewModel(new Entity { TableName = name }, new EntityLayout())
            );
        }

        AddRelationship(main, "A", "B");
        AddRelationship(main, "C", "D");
        AddRelationship(main, "A", "C");
        return main;
    }

    /// <summary>親子のテーブル名を指定してリレーションを足す</summary>
    private static void AddRelationship(MainViewModel main, string parent, string child)
    {
        var source = Entity(main, parent);
        var target = Entity(main, child);
        main.Relationships.Add(
            new RelationshipViewModel(
                new Relationship { SourceEntityId = source.Id, TargetEntityId = target.Id },
                source,
                target
            )
        );
    }

    /// <summary>テーブル名でエンティティを引く</summary>
    private static EntityViewModel Entity(MainViewModel main, string name) =>
        main.Entities.Single(entity => entity.TableName == name);

    /// <summary>エンティティの並びをテーブル名で返す</summary>
    private static List<string> EntityOrder(MainViewModel main) =>
        main.Entities.Select(entity => entity.TableName).ToList();

    /// <summary>リレーションの並びを「親→子」で返す</summary>
    private static List<string> RelationshipOrder(MainViewModel main) =>
        main
            .Relationships.Select(relationship =>
                $"{relationship.Source.TableName}->{relationship.Target.TableName}"
            )
            .ToList();

    [Fact(
        DisplayName = "RemoveEntityCommand: 先頭のテーブルを削除して Undo すると、テーブルもリレーションも元の位置へ戻る"
    )]
    public void RemoveEntityCommand_Undo_RestoresOrder()
    {
        var main = Build();
        var command = new RemoveEntityCommand(main, Entity(main, "A"));

        command.Execute();
        EntityOrder(main).Should().Equal("B", "C", "D");
        RelationshipOrder(main).Should().Equal("C->D");

        command.Undo();
        EntityOrder(main).Should().Equal("A", "B", "C", "D");
        RelationshipOrder(main).Should().Equal("A->B", "C->D", "A->C");

        // Redo（再実行）からもう一度戻しても同じ
        command.Execute();
        command.Undo();
        EntityOrder(main).Should().Equal("A", "B", "C", "D");
        RelationshipOrder(main).Should().Equal("A->B", "C->D", "A->C");
    }

    [Fact(
        DisplayName = "GroupRemoveEntitiesCommand: 離れた位置のテーブルをまとめて削除して Undo すると、元の位置へ戻る"
    )]
    public void GroupRemoveEntitiesCommand_Undo_RestoresOrder()
    {
        var main = Build();

        // 選択の順（D、B）は図の順と逆にしておく。A->C は残るので、末尾へ戻すと順序が変わる
        var command = new GroupRemoveEntitiesCommand(main, [Entity(main, "D"), Entity(main, "B")]);

        command.Execute();
        EntityOrder(main).Should().Equal("A", "C");
        RelationshipOrder(main).Should().Equal("A->C");

        command.Undo();
        EntityOrder(main).Should().Equal("A", "B", "C", "D");
        RelationshipOrder(main).Should().Equal("A->B", "C->D", "A->C");
    }
}
