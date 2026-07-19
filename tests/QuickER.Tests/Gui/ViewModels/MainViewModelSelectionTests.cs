using System.Windows;
using FluentAssertions;
using QuickER.Model;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.ViewModels;

/// <summary>
/// <see cref="MainViewModel"/> の複数選択（Ctrl+クリック・ラバーバンド・全選択・解除）と
/// グループ移動のクランプ純関数を検証するテストクラス（S1 分）
/// </summary>
/// <remarks>
/// 選択の正は <see cref="EntityViewModel.IsSelected"/> フラグであり、選択操作は Undo 履歴を汚さない
/// （<see cref="QuickER.UndoRedo.UndoRedoManager.CanUndo"/> が偽のまま）ことも併せて検証する。
/// </remarks>
public class MainViewModelSelectionTests
{
    /// <summary>指定座標・サイズのエンティティ ViewModel を生成してキャンバスへ追加する</summary>
    private static EntityViewModel AddEntity(
        MainViewModel vm,
        string tableName,
        double x = 0,
        double y = 0,
        double width = 200
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

        var entity = new EntityViewModel(
            model,
            new QuickER.Documents.EntityLayout
            {
                X = x,
                Y = y,
                Width = width,
            }
        );
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

    // ---------------- Ctrl+クリック（トグル） ----------------

    [Fact(DisplayName = "トグル: 追加・除外と主選択の更新")]
    public void Toggle_AddsRemovesAndUpdatesPrimarySelection()
    {
        var vm = new MainViewModel();
        var a = AddEntity(vm, "A");
        var b = AddEntity(vm, "B");

        // A を追加 → A が主選択
        vm.ToggleEntitySelection(a);
        a.IsSelected.Should().BeTrue();
        vm.SelectedEntity.Should().Be(a);
        vm.SelectedEntities.Should().ContainSingle().Which.Should().Be(a);

        // B を追加 → B が主選択、両方選択
        vm.ToggleEntitySelection(b);
        b.IsSelected.Should().BeTrue();
        vm.SelectedEntity.Should().Be(b);
        vm.SelectedEntities.Should().BeEquivalentTo(new[] { a, b });

        // B を除外 → 主選択は残る A へ付け替わる
        vm.ToggleEntitySelection(b);
        b.IsSelected.Should().BeFalse();
        vm.SelectedEntity.Should().Be(a);
        vm.SelectedEntities.Should().ContainSingle().Which.Should().Be(a);
    }

    [Fact(DisplayName = "トグル: リレーション選択と排他")]
    public void Toggle_ClearsRelationshipSelection()
    {
        var vm = new MainViewModel();
        var a = AddEntity(vm, "A");
        var b = AddEntity(vm, "B");
        var rel = AddRelationship(vm, a, b);

        vm.OnRelationshipClicked(rel);
        vm.SelectedRelationship.Should().Be(rel);
        rel.IsSelected.Should().BeTrue();

        // エンティティのトグルはリレーション選択を解除する
        vm.ToggleEntitySelection(a);
        vm.SelectedRelationship.Should().BeNull();
        rel.IsSelected.Should().BeFalse();
        a.IsSelected.Should().BeTrue();
    }

    [Fact(DisplayName = "トグル: 全除外で主選択が null")]
    public void Toggle_AllRemoved_PrimaryBecomesNull()
    {
        var vm = new MainViewModel();
        var a = AddEntity(vm, "A");

        vm.ToggleEntitySelection(a);
        vm.ToggleEntitySelection(a);

        a.IsSelected.Should().BeFalse();
        vm.SelectedEntity.Should().BeNull();
        vm.SelectedEntities.Should().BeEmpty();
    }

    // ---------------- 全選択 / 解除 ----------------

    [Fact(DisplayName = "全選択: すべてのエンティティを選択し末尾を主選択にする")]
    public void SelectAll_SelectsEveryEntity()
    {
        var vm = new MainViewModel();
        var a = AddEntity(vm, "A");
        var b = AddEntity(vm, "B");
        var c = AddEntity(vm, "C");

        vm.SelectAllEntitiesCommand.Execute(null);

        vm.SelectedEntities.Should().BeEquivalentTo(new[] { a, b, c });
        vm.SelectedEntity.Should().Be(c);
    }

    [Fact(DisplayName = "選択解除: エンティティ選択のみ解除しリレーションモードには触れない")]
    public void ClearSelection_ClearsEntitiesOnly()
    {
        var vm = new MainViewModel();
        var a = AddEntity(vm, "A");
        var b = AddEntity(vm, "B");

        vm.SelectAllEntitiesCommand.Execute(null);
        vm.SelectedEntities.Should().HaveCount(2);

        // 保留リレーションモードは解除の影響を受けない
        vm.IsRelationshipMode = true;
        vm.ClearSelectionCommand.Execute(null);

        vm.SelectedEntities.Should().BeEmpty();
        vm.SelectedEntity.Should().BeNull();
        vm.IsRelationshipMode.Should().BeTrue();
    }

    // ---------------- ラバーバンド ----------------

    [Fact(DisplayName = "ラバーバンド: 触れたら選択（交差・完全包含不要）")]
    public void RubberBand_SelectsIntersecting()
    {
        var vm = new MainViewModel();

        // A: (0,0,200x?)・B: (400,0)・C: (1000,0)
        var a = AddEntity(vm, "A", x: 0, y: 0, width: 200);
        var b = AddEntity(vm, "B", x: 400, y: 0, width: 200);
        var c = AddEntity(vm, "C", x: 1000, y: 0, width: 200);

        // 矩形 (100,-10)-(450,50): A の右部と B の左部に交差、C とは交差しない
        var area = new Rect(100, -10, 350, 60);
        vm.ApplyRubberBandSelection(area, additive: false);

        a.IsSelected.Should().BeTrue();
        b.IsSelected.Should().BeTrue();
        c.IsSelected.Should().BeFalse();
        vm.SelectedEntities.Should().BeEquivalentTo(new[] { a, b });
    }

    [Fact(DisplayName = "ラバーバンド: 非追加モードは矩形外の選択を落とす")]
    public void RubberBand_NonAdditive_ReplacesSelection()
    {
        var vm = new MainViewModel();
        var a = AddEntity(vm, "A", x: 0, y: 0, width: 200);
        var b = AddEntity(vm, "B", x: 1000, y: 0, width: 200);

        // 先に B を選択しておく
        vm.ToggleEntitySelection(b);
        b.IsSelected.Should().BeTrue();

        // A のみに交差する矩形で置換選択 → B は落ちる
        vm.ApplyRubberBandSelection(new Rect(0, 0, 50, 50), additive: false);

        a.IsSelected.Should().BeTrue();
        b.IsSelected.Should().BeFalse();
    }

    [Fact(DisplayName = "ラバーバンド: 追加モードは既存選択を維持する")]
    public void RubberBand_Additive_KeepsExistingSelection()
    {
        var vm = new MainViewModel();
        var a = AddEntity(vm, "A", x: 0, y: 0, width: 200);
        var b = AddEntity(vm, "B", x: 1000, y: 0, width: 200);

        vm.ToggleEntitySelection(b);

        // A のみに交差する矩形で追加選択 → B も残る
        vm.ApplyRubberBandSelection(new Rect(0, 0, 50, 50), additive: true);

        a.IsSelected.Should().BeTrue();
        b.IsSelected.Should().BeTrue();
        vm.SelectedEntities.Should().BeEquivalentTo(new[] { a, b });
    }

    // ---------------- グループ剛体クランプ（純関数） ----------------

    [Fact(DisplayName = "クランプ: 負に振れない範囲ではデルタをそのまま返す")]
    public void ClampGroupDelta_WithinBounds_PassesThrough()
    {
        var (dx, dy) = MainViewModel.ClampGroupDelta(
            minX: 100,
            minY: 100,
            deltaX: -50,
            deltaY: -30
        );

        dx.Should().Be(-50);
        dy.Should().Be(-30);
    }

    [Fact(DisplayName = "クランプ: 左端・上端が 0 を割るデルタは 0 到達で止める")]
    public void ClampGroupDelta_BeyondZero_ClampsToMinCoordinate()
    {
        // 最小座標 50 に対し -200 を要求 → -50 でクランプ（min が 0 に到達）
        var (dx, dy) = MainViewModel.ClampGroupDelta(
            minX: 50,
            minY: 20,
            deltaX: -200,
            deltaY: -100
        );

        dx.Should().Be(-50);
        dy.Should().Be(-20);
    }

    [Fact(DisplayName = "クランプ: 正方向のデルタは制限しない")]
    public void ClampGroupDelta_PositiveDelta_Unrestricted()
    {
        var (dx, dy) = MainViewModel.ClampGroupDelta(minX: 0, minY: 0, deltaX: 300, deltaY: 400);

        dx.Should().Be(300);
        dy.Should().Be(400);
    }

    // ---------------- ハイライト連動 ----------------

    [Fact(DisplayName = "ハイライト: 1 個選択で減光あり")]
    public void SingleSelection_AppliesDimming()
    {
        var vm = new MainViewModel();
        var a = AddEntity(vm, "A");
        var b = AddEntity(vm, "B");
        AddRelationship(vm, a, b);
        var c = AddEntity(vm, "C");

        vm.ToggleEntitySelection(a);

        // A に無関係な C は減光される
        c.IsDimmed.Should().BeTrue();
    }

    [Fact(DisplayName = "ハイライト: 複数選択は選択集合の和で関連を判定する")]
    public void MultiSelection_DimsByUnionOfSelection()
    {
        var vm = new MainViewModel();
        var a = AddEntity(vm, "A");
        var b = AddEntity(vm, "B");
        var relAb = AddRelationship(vm, a, b);
        var c = AddEntity(vm, "C");
        var d = AddEntity(vm, "D");
        var relCd = AddRelationship(vm, c, d);
        var e = AddEntity(vm, "E");
        var f = AddEntity(vm, "F");
        var relEf = AddRelationship(vm, e, f);

        // A と C を選択 → 接続相手の B・D は非減光、線 A-B / C-D は強調。
        // どの選択メンバーとも無関係な島（E-F）はエンティティ・線とも減光される
        vm.ToggleEntitySelection(a);
        vm.ToggleEntitySelection(c);

        a.IsDimmed.Should().BeFalse();
        b.IsDimmed.Should().BeFalse("選択メンバー A の接続相手のため");
        c.IsDimmed.Should().BeFalse();
        d.IsDimmed.Should().BeFalse("選択メンバー C の接続相手のため");
        relAb.IsEmphasized.Should().BeTrue();
        relCd.IsEmphasized.Should().BeTrue();

        e.IsDimmed.Should().BeTrue();
        f.IsDimmed.Should().BeTrue();
        relEf.IsDimmed.Should().BeTrue();
        relEf.IsEmphasized.Should().BeFalse();
    }

    // ---------------- 履歴汚染なし ----------------

    [Fact(DisplayName = "履歴汚染なし: 選択操作で CanUndo が偽のまま")]
    public void SelectionOperations_DoNotPolluteUndoHistory()
    {
        var vm = new MainViewModel();
        var a = AddEntity(vm, "A", x: 0, y: 0, width: 200);
        var b = AddEntity(vm, "B", x: 400, y: 0, width: 200);

        vm.ToggleEntitySelection(a);
        vm.ToggleEntitySelection(b);
        vm.ApplyRubberBandSelection(new Rect(0, 0, 50, 50), additive: false);
        vm.SelectAllEntitiesCommand.Execute(null);
        vm.ClearSelectionCommand.Execute(null);

        vm.UndoRedo.CanUndo.Should().BeFalse();
    }
}
