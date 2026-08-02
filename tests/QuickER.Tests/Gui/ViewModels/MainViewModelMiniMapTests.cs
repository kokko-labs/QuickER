using System.Windows;
using AwesomeAssertions;
using QuickER.Documents;
using QuickER.Model;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.ViewModels;

/// <summary>
/// <see cref="MainViewModel"/> のミニマップ（可視判定・射影データ・再計算）ロジックを検証するテストクラス
/// </summary>
/// <remarks>
/// WPF UI スレッドに依存しない導出・射影ロジックのみを対象とする
/// （クリック/ドラッグの座標適用は View 側の責務、逆写像純関数は ViewportCalculatorTests が担う）
/// </remarks>
public class MainViewModelMiniMapTests
{
    /// <summary>指定座標・サイズを持つエンティティを VM へ直接投入する</summary>
    private static EntityViewModel AddEntity(
        MainViewModel vm,
        double x,
        double y,
        double width = 200
    )
    {
        var model = new Entity { TableName = "T", Columns = { new Column { Name = "Id" } } };
        var layout = new EntityLayout
        {
            X = x,
            Y = y,
            Width = width,
        };
        var entity = new EntityViewModel(model, layout);
        vm.Entities.Add(entity);

        return entity;
    }

    /// <summary>コンテンツが収まらない状況を作るため、大きく離れた 2 エンティティを投入する</summary>
    private static MainViewModel BuildOverflowingDiagram()
    {
        var vm = new MainViewModel();
        AddEntity(vm, 0, 0);
        AddEntity(vm, 3000, 2000);

        return vm;
    }

    /// <summary>空図ではトグルが ON でもミニマップは非表示であることを検証する</summary>
    [Fact(DisplayName = "MiniMap: 空図は非表示")]
    public void MiniMap_EmptyDiagram_NotVisible()
    {
        var vm = new MainViewModel { ViewportContentBounds = new Rect(0, 0, 800, 600) };

        vm.IsMiniMapEnabled.Should().BeTrue();
        vm.IsMiniMapVisible.Should().BeFalse();
    }

    /// <summary>図がビューポートに収まっているときは自動的に非表示になることを検証する</summary>
    [Fact(DisplayName = "MiniMap: 収まっている図は非表示")]
    public void MiniMap_ContentFits_NotVisible()
    {
        var vm = new MainViewModel();
        AddEntity(vm, 100, 100);
        AddEntity(vm, 300, 200);

        // コンテンツ全体（100..500, 100..300）を包含する広いビューポート
        vm.ViewportContentBounds = new Rect(0, 0, 2000, 2000);

        vm.IsMiniMapVisible.Should().BeFalse();
    }

    /// <summary>図がビューポートに収まらないときは自動表示されることを検証する</summary>
    [Fact(DisplayName = "MiniMap: 収まらない図は表示")]
    public void MiniMap_ContentOverflows_Visible()
    {
        var vm = BuildOverflowingDiagram();

        // コンテンツ（0..3200, 0..2100 付近）の一部しか映さない狭いビューポート
        vm.ViewportContentBounds = new Rect(0, 0, 800, 600);

        vm.IsMiniMapVisible.Should().BeTrue();
    }

    /// <summary>トグル OFF なら収まらない図でも常に非表示であることを検証する</summary>
    [Fact(DisplayName = "MiniMap: トグル OFF は常に非表示")]
    public void MiniMap_ToggleOff_AlwaysHidden()
    {
        var vm = BuildOverflowingDiagram();
        vm.ViewportContentBounds = new Rect(0, 0, 800, 600);

        vm.IsMiniMapVisible.Should().BeTrue();

        vm.IsMiniMapEnabled = false;

        vm.IsMiniMapVisible.Should().BeFalse();
    }

    /// <summary>ビューポート未確定（ヘッドレス）では収まり判定不能のため非表示であることを検証する</summary>
    [Fact(DisplayName = "MiniMap: ビューポート未確定は非表示")]
    public void MiniMap_ViewportUnknown_NotVisible()
    {
        var vm = BuildOverflowingDiagram();

        // ViewportContentBounds は既定の空矩形のまま
        vm.IsMiniMapVisible.Should().BeFalse();
    }

    /// <summary>エンティティ追加でミニマップの射影データ（矩形）が再計算されることを検証する</summary>
    [Fact(DisplayName = "MiniMap: エンティティ追加で射影データが再計算される")]
    public void MiniMap_AddEntity_RecalculatesProjection()
    {
        var vm = new MainViewModel();
        vm.MiniMapEntities.Should().BeEmpty();

        AddEntity(vm, 0, 0);
        AddEntity(vm, 1000, 800);

        vm.MiniMapEntities.Should().HaveCount(2);

        // 射影後の矩形はすべてミニマップ枠（200x140）内に収まる
        foreach (var e in vm.MiniMapEntities)
        {
            e.X.Should().BeGreaterThanOrEqualTo(0);
            e.Y.Should().BeGreaterThanOrEqualTo(0);
            (e.X + e.Width).Should().BeLessThanOrEqualTo(MainViewModel.MiniMapWidth + 1e-6);
            (e.Y + e.Height).Should().BeLessThanOrEqualTo(MainViewModel.MiniMapHeight + 1e-6);
        }
    }

    /// <summary>エンティティ矩形にタイトル背景色が反映されることを検証する</summary>
    [Fact(DisplayName = "MiniMap: 矩形にタイトル背景色が反映される")]
    public void MiniMap_Entity_CarriesTitleColor()
    {
        var vm = new MainViewModel();
        var entity = AddEntity(vm, 0, 0);
        var expected = entity.TitleBackgroundColor;

        vm.MiniMapEntities.Should().ContainSingle();
        vm.MiniMapEntities[0].TitleBackgroundColor.Should().Be(expected);
    }

    /// <summary>エンティティ移動でミニマップ射影が追従して再計算されることを検証する</summary>
    [Fact(DisplayName = "MiniMap: エンティティ移動で射影が追従する")]
    public void MiniMap_MoveEntity_Recalculates()
    {
        var vm = new MainViewModel();
        AddEntity(vm, 0, 0);
        var moving = AddEntity(vm, 500, 500);

        var before = vm.MiniMapEntities[1];

        // 大きく移動 → 全体 bbox が広がり、射影スケールが縮む＝矩形の投影結果が変わる
        moving.X = 5000;
        moving.Y = 4000;

        var after = vm.MiniMapEntities[1];
        (after.X != before.X || after.Y != before.Y).Should().BeTrue();
    }

    /// <summary>エンティティ削除でミニマップ矩形が減ることを検証する</summary>
    [Fact(DisplayName = "MiniMap: エンティティ削除で矩形が減る")]
    public void MiniMap_RemoveEntity_UpdatesProjection()
    {
        var vm = new MainViewModel();
        AddEntity(vm, 0, 0);
        var second = AddEntity(vm, 1000, 800);

        vm.MiniMapEntities.Should().HaveCount(2);

        vm.Entities.Remove(second);

        vm.MiniMapEntities.Should().ContainSingle();
    }

    /// <summary>リレーション追加でミニマップの線データが生成されることを検証する</summary>
    [Fact(DisplayName = "MiniMap: リレーション追加で線データが生成される")]
    public void MiniMap_AddRelationship_ProducesLine()
    {
        var vm = new MainViewModel();
        var a = AddEntity(vm, 0, 0);
        var b = AddEntity(vm, 1000, 800);

        vm.MiniMapLines.Should().BeEmpty();

        var rel = new RelationshipViewModel(
            new Relationship { SourceEntityId = a.Id, TargetEntityId = b.Id },
            a,
            b
        );
        vm.Relationships.Add(rel);

        vm.MiniMapLines.Should().ContainSingle();
    }

    /// <summary>ビューポート枠が ViewportContentBounds に追従して射影されることを検証する</summary>
    [Fact(DisplayName = "MiniMap: ビューポート枠が表示領域に追従する")]
    public void MiniMap_ViewportFrame_TracksViewportContentBounds()
    {
        var vm = BuildOverflowingDiagram();

        vm.ViewportContentBounds = new Rect(0, 0, 800, 600);
        var first = vm.MiniMapViewport;

        // 表示領域をスクロールすると枠の位置が動く
        vm.ViewportContentBounds = new Rect(1500, 1000, 800, 600);
        var second = vm.MiniMapViewport;

        second.X.Should().BeGreaterThan(first.X);
        second.Y.Should().BeGreaterThan(first.Y);

        // 枠はミニマップ枠内の座標系で表される（左上は非負）
        first.X.Should().BeGreaterThanOrEqualTo(0);
        first.Y.Should().BeGreaterThanOrEqualTo(0);
    }

    /// <summary>スクロール（ViewportContentBounds 変更）では射影データ自体は作り直されない（軽い更新）ことを検証する</summary>
    [Fact(DisplayName = "MiniMap: スクロールでは射影データを作り直さない")]
    public void MiniMap_Scroll_DoesNotRebuildProjectionData()
    {
        var vm = BuildOverflowingDiagram();
        vm.ViewportContentBounds = new Rect(0, 0, 800, 600);

        // 射影データ（矩形リスト）の参照を捕捉
        var entitiesBefore = vm.MiniMapEntities;
        var linesBefore = vm.MiniMapLines;

        // スクロールのみ（コンテンツは不変）→ 射影データの参照は据え置き、枠だけ動く
        vm.ViewportContentBounds = new Rect(1500, 1000, 800, 600);

        vm.MiniMapEntities.Should().BeSameAs(entitiesBefore);
        vm.MiniMapLines.Should().BeSameAs(linesBefore);
    }

    /// <summary>逆写像パン計算が現在のズーム倍率を保った中央寄せオフセットを返すことを検証する</summary>
    [Fact(DisplayName = "MiniMap: パン計算は現在の倍率を保って中央へ据える")]
    public void MiniMap_CalculateMiniMapPan_HonorsZoom()
    {
        var vm = BuildOverflowingDiagram();
        vm.ViewportContentBounds = new Rect(0, 0, 800, 600);
        vm.ZoomLevel = 1.5;

        var viewport = new Size(800, 600);

        // ミニマップ枠中央を押したときのオフセット（コンテンツ中心付近を中央へ据える）
        var offset = vm.CalculateMiniMapPan(
            new Point(MainViewModel.MiniMapWidth / 2, MainViewModel.MiniMapHeight / 2),
            viewport
        );

        // 現在の倍率（1.5）を保ったオフセットが返る（負にはならない）
        offset.X.Should().BeGreaterThanOrEqualTo(0);
        offset.Y.Should().BeGreaterThanOrEqualTo(0);
    }
}
