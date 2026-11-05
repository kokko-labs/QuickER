using System.Globalization;
using System.Windows;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.ViewModels;

/// <summary>
/// <see cref="MainViewModel"/> の複数選択（Ctrl+クリック・ラバーバンド・全選択・解除）と
/// グループ移動のクランプ純関数を検証するテストクラス（S1 分）
/// </summary>
/// <remarks>
/// 選択の正は <see cref="EntityViewModel.IsSelected"/> フラグ。選択操作が Undo 履歴を汚さないことは、
/// 操作の往復の表（<see cref="QuickER.Tests.Gui.UndoRedo.UndoOperationRows"/> の <c>selection-*</c> の行）が流す。
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

    /// <summary>ラバーバンドが、触れたテーブル（交差・完全包含は不要）を選び、非追加なら矩形外の選択を落とし、追加なら既存の選択を保つことを検証する</summary>
    /// <param name="entities">配置するテーブル（「名前:X 座標」の「;」区切り・Y は 0・幅は 200）</param>
    /// <param name="alreadySelected">先に選んでおくテーブルの名前（空は無し）</param>
    /// <param name="areaX">矩形の X</param>
    /// <param name="areaY">矩形の Y</param>
    /// <param name="areaWidth">矩形の幅</param>
    /// <param name="areaHeight">矩形の高さ</param>
    /// <param name="additive">追加モードか</param>
    /// <param name="expectedSelected">選ばれているはずのテーブルの名前（宣言順に並べたもの）</param>
    [Theory(
        DisplayName = "ラバーバンド: 触れたら選択し、非追加は矩形外の選択を落とし、追加は既存選択を維持する"
    )]
    [InlineData("A:0;B:400;C:1000", "", 100, -10, 350, 60, false, "AB")] // 触れたら選択（交差・完全包含不要）。矩形は A の右部と B の左部に交差し、C とは交差しない
    [InlineData("A:0;B:1000", "B", 0, 0, 50, 50, false, "A")] // 非追加モードは矩形外の選択を落とす
    [InlineData("A:0;B:1000", "B", 0, 0, 50, 50, true, "AB")] // 追加モードは既存選択を維持する
    public void RubberBand_AppliesSelection(
        string entities,
        string alreadySelected,
        double areaX,
        double areaY,
        double areaWidth,
        double areaHeight,
        bool additive,
        string expectedSelected
    )
    {
        var vm = new MainViewModel();
        var added = entities
            .Split(';')
            .Select(spec => spec.Split(':'))
            .Select(parts =>
                AddEntity(
                    vm,
                    parts[0],
                    x: double.Parse(parts[1], CultureInfo.InvariantCulture),
                    y: 0,
                    width: 200
                )
            )
            .ToList();

        foreach (var name in alreadySelected.Select(letter => $"{letter}"))
        {
            var entity = added.Single(e => e.TableName == name);
            vm.ToggleEntitySelection(entity);
            entity.IsSelected.Should().BeTrue();
        }

        vm.ApplyRubberBandSelection(new Rect(areaX, areaY, areaWidth, areaHeight), additive);

        foreach (var entity in added)
        {
            entity
                .IsSelected.Should()
                .Be(expectedSelected.Contains(entity.TableName, StringComparison.Ordinal));
        }

        vm.SelectedEntities.Should()
            .BeEquivalentTo(
                added.Where(e => expectedSelected.Contains(e.TableName, StringComparison.Ordinal))
            );
    }

    // ---------------- グループ剛体クランプ（純関数） ----------------

    /// <summary>グループ移動のデルタが、負に振れない範囲ではそのまま返り、左端・上端が 0 を割るなら 0 到達で止まり、正方向は制限されないことを検証する</summary>
    [Theory(
        DisplayName = "クランプ: 左端・上端が 0 を割るデルタは 0 到達で止め、それ以外はそのまま返す"
    )]
    [InlineData(100, 100, -50, -30, -50, -30)] // 負に振れない範囲ではデルタをそのまま返す
    [InlineData(50, 20, -200, -100, -50, -20)] // 最小座標 50 に対し -200 を要求 → -50 でクランプ（min が 0 に到達）
    [InlineData(0, 0, 300, 400, 300, 400)] // 正方向のデルタは制限しない
    public void ClampGroupDelta_StopsAtZero(
        double minX,
        double minY,
        double deltaX,
        double deltaY,
        double expectedDeltaX,
        double expectedDeltaY
    )
    {
        var (dx, dy) = MainViewModel.ClampGroupDelta(
            minX: minX,
            minY: minY,
            deltaX: deltaX,
            deltaY: deltaY
        );

        dx.Should().Be(expectedDeltaX);
        dy.Should().Be(expectedDeltaY);
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
}
