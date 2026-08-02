using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using AwesomeAssertions;
using QuickER.Documents;
using QuickER.Model;
using QuickER.Resources;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.ViewModels;

/// <summary>
/// <see cref="MainViewModel"/> の一括操作（S2: プロパティパネル切替・一括削除・一括色変更）を検証するテストクラス
/// </summary>
/// <remarks>
/// 選択数に追従する派生プロパティ（<c>IsMultiSelectionActive</c> / <c>SelectedEntityCountText</c>）の通知と、
/// 一括削除・一括色変更が「複合 1 エントリ」で Undo できること（履歴の分裂・二重登録が無いこと）を確認する。
/// </remarks>
public class MainViewModelBulkOperationTests
{
    /// <summary>指定名・色のエンティティ ViewModel を生成してキャンバスへ追加する</summary>
    private static EntityViewModel AddEntity(
        MainViewModel vm,
        string tableName,
        string? titleColor = null
    )
    {
        var model = new Entity
        {
            TableName = tableName,
            Columns =
            {
                new Column
                {
                    Name = "ID",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
            },
        };

        var layout = new EntityLayout
        {
            X = 0,
            Y = 0,
            Width = 200,
        };

        if (titleColor is not null)
        {
            layout.TitleBackgroundColor = titleColor;
        }

        var entity = new EntityViewModel(model, layout);
        vm.Entities.Add(entity);
        return entity;
    }

    /// <summary>2 つのエンティティを結ぶリレーション ViewModel を生成してキャンバスへ追加する</summary>
    private static RelationshipViewModel AddRelationship(
        MainViewModel vm,
        EntityViewModel source,
        EntityViewModel target
    )
    {
        var relationship = new RelationshipViewModel(
            new Relationship
            {
                SourceEntityId = source.Id,
                TargetEntityId = target.Id,
                Type = RelationshipType.OneToMany,
            },
            source,
            target
        );

        vm.Relationships.Add(relationship);
        return relationship;
    }

    /// <summary>指定 ViewModel のプロパティ変更通知を記録するトラッカーを取り付ける</summary>
    private static List<string> TrackPropertyChanges(MainViewModel vm)
    {
        var raised = new List<string>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not null)
            {
                raised.Add(e.PropertyName);
            }
        };

        return raised;
    }

    // ---------------- 派生プロパティの追従・通知 ----------------

    [Fact(DisplayName = "一括切替: トグルで 2 個目到達時に IsMultiSelectionActive が真＋通知")]
    public void MultiSelectionActive_TogglesWithSelectionCount()
    {
        var vm = new MainViewModel();
        var a = AddEntity(vm, "A");
        var b = AddEntity(vm, "B");

        var raised = TrackPropertyChanges(vm);

        vm.ToggleEntitySelection(a);
        vm.IsMultiSelectionActive.Should().BeFalse();
        vm.SelectedEntityCountText.Should().Be(string.Format(Strings.Selection_CountText, 1));

        raised.Clear();
        vm.ToggleEntitySelection(b);

        vm.IsMultiSelectionActive.Should().BeTrue();
        vm.SelectedEntityCountText.Should().Be(string.Format(Strings.Selection_CountText, 2));
        raised.Should().Contain(nameof(MainViewModel.IsMultiSelectionActive));
        raised.Should().Contain(nameof(MainViewModel.SelectedEntityCountText));
    }

    [Fact(DisplayName = "一括切替: 全選択・全解除・ラバーバンドでも件数と通知が追従")]
    public void MultiSelectionActive_FollowsAllSelectionGestures()
    {
        var vm = new MainViewModel();
        var a = AddEntity(vm, "A");
        var b = AddEntity(vm, "B");
        var c = AddEntity(vm, "C");

        var raised = TrackPropertyChanges(vm);

        // 全選択 → 3 個
        vm.SelectAllEntitiesCommand.Execute(null);
        vm.IsMultiSelectionActive.Should().BeTrue();
        vm.SelectedEntityCountText.Should().Be(string.Format(Strings.Selection_CountText, 3));
        raised.Should().Contain(nameof(MainViewModel.IsMultiSelectionActive));

        // 全解除 → 0 個
        raised.Clear();
        vm.ClearSelectionCommand.Execute(null);
        vm.IsMultiSelectionActive.Should().BeFalse();
        vm.SelectedEntityCountText.Should().Be(string.Format(Strings.Selection_CountText, 0));
        raised.Should().Contain(nameof(MainViewModel.IsMultiSelectionActive));

        // ラバーバンドで全域選択 → 3 個
        raised.Clear();
        vm.ApplyRubberBandSelection(new Rect(-100, -100, 10000, 10000), additive: false);
        vm.IsMultiSelectionActive.Should().BeTrue();
        vm.SelectedEntityCountText.Should().Be(string.Format(Strings.Selection_CountText, 3));
        raised.Should().Contain(nameof(MainViewModel.SelectedEntityCountText));
    }

    // ---------------- 一括削除（複合 1 エントリ Undo） ----------------

    [Fact(
        DisplayName = "一括削除: 3 個選択→削除でエンティティ・接続線が消え、1 回の Undo で全復元"
    )]
    public void BulkDelete_RemovesAllAndSingleUndoRestores()
    {
        var vm = new MainViewModel();
        var a = AddEntity(vm, "A");
        var b = AddEntity(vm, "B");
        var c = AddEntity(vm, "C");
        var relAb = AddRelationship(vm, a, b);
        var relBc = AddRelationship(vm, b, c);

        vm.SelectAllEntitiesCommand.Execute(null);
        vm.SelectedEntities.Should().HaveCount(3);

        vm.RemoveSelectedEntityCommand.Execute(null);

        // 全エンティティ・接続リレーションが消滅
        vm.Entities.Should().BeEmpty();
        vm.Relationships.Should().BeEmpty();

        // Undo は複合 1 エントリ: 1 回で全復元、その後 CanUndo が偽
        vm.UndoRedo.CanUndo.Should().BeTrue();
        vm.UndoRedo.Undo();

        vm.Entities.Should().BeEquivalentTo(new[] { a, b, c });
        vm.Relationships.Should().BeEquivalentTo(new[] { relAb, relBc });
        vm.UndoRedo.CanUndo.Should().BeFalse();
    }

    [Fact(DisplayName = "一括削除: Delete 経路（DeleteSelectedCommand）も選択中全員に作用")]
    public void BulkDelete_ViaDeleteSelectedCommand()
    {
        var vm = new MainViewModel();
        AddEntity(vm, "A");
        AddEntity(vm, "B");
        AddEntity(vm, "C");

        vm.SelectAllEntitiesCommand.Execute(null);
        vm.DeleteSelectedCommand.Execute(null);

        vm.Entities.Should().BeEmpty();
        vm.UndoRedo.CanUndo.Should().BeTrue();

        vm.UndoRedo.Undo();
        vm.Entities.Should().HaveCount(3);
        vm.UndoRedo.CanUndo.Should().BeFalse();
    }

    // ---------------- 一括色変更（複合 1 エントリ Undo・二重登録なし） ----------------

    [Fact(
        DisplayName = "一括色変更: 3 個選択→色変更で全員反映、1 回の Undo で全員元の色（履歴 1 エントリ）"
    )]
    public void BulkColorChange_AppliesToAllAndSingleUndo()
    {
        var vm = new MainViewModel();
        var a = AddEntity(vm, "A", titleColor: "#111111");
        var b = AddEntity(vm, "B", titleColor: "#222222");
        var c = AddEntity(vm, "C", titleColor: "#333333");

        vm.SelectAllEntitiesCommand.Execute(null);

        const string target = "#ABCDEF";
        vm.BulkChangeTitleColorCommand.Execute(target);

        a.TitleBackgroundColor.Should().Be(target);
        b.TitleBackgroundColor.Should().Be(target);
        c.TitleBackgroundColor.Should().Be(target);

        // 履歴は複合 1 エントリ: 1 回の Undo で全員元の色へ戻り、CanUndo は偽になる
        // （DiagramChangeTracker の自動記録による N 件分裂・二重登録が無いことの検証）
        vm.UndoRedo.CanUndo.Should().BeTrue();
        vm.UndoRedo.Undo();

        a.TitleBackgroundColor.Should().Be("#111111");
        b.TitleBackgroundColor.Should().Be("#222222");
        c.TitleBackgroundColor.Should().Be("#333333");
        vm.UndoRedo.CanUndo.Should().BeFalse();
    }

    [Fact(DisplayName = "一括色変更: 全員同色なら履歴を積まない（実変更なし）")]
    public void BulkColorChange_NoOpWhenAlreadySameColor()
    {
        var vm = new MainViewModel();
        AddEntity(vm, "A", titleColor: "#ABCDEF");
        AddEntity(vm, "B", titleColor: "#ABCDEF");

        vm.SelectAllEntitiesCommand.Execute(null);
        vm.BulkChangeTitleColorCommand.Execute("#ABCDEF");

        vm.UndoRedo.CanUndo.Should().BeFalse();
    }

    // ---------------- 単一選択時の従来挙動（非回帰） ----------------

    [Fact(DisplayName = "単一選択: 削除は従来どおり単一エンティティに作用（複合化しない）")]
    public void SingleSelection_DeleteRemovesOnlyPrimary()
    {
        var vm = new MainViewModel();
        var a = AddEntity(vm, "A");
        var b = AddEntity(vm, "B");

        vm.SelectedEntity = a;
        a.IsSelected = true;

        vm.IsMultiSelectionActive.Should().BeFalse();
        vm.RemoveSelectedEntityCommand.Execute(null);

        vm.Entities.Should().ContainSingle().Which.Should().Be(b);

        vm.UndoRedo.Undo();
        vm.Entities.Should().BeEquivalentTo(new[] { a, b });
    }
}
