using System.Windows;
using System.Windows.Media;
using AwesomeAssertions;
using QuickER.Documents;
using QuickER.Model;
using QuickER.Services;
using QuickER.ViewModels;
using static QuickER.Tests.TestSupport.WpfApplicationTestSupport;

namespace QuickER.Tests.Gui.Services;

/// <summary><see cref="DiagramVectorRenderer"/> のベクタ描画と範囲計算を検証するテストクラス</summary>
public class DiagramVectorRendererTests
{
    /// <summary>
    /// 描画結果に GlyphRunDrawing が含まれることを検証する
    /// （文字がビットマップ化されずベクタ（GlyphRun）で描かれている証明。
    /// XPS/PDF 出力時に Glyphs として保持される前提条件）
    /// </summary>
    [Fact(DisplayName = "RenderDiagram は文字を GlyphRunDrawing としてベクタ描画する")]
    public void RenderDiagram_ProducesGlyphRunDrawings()
    {
        RunSta(() =>
        {
            var vm = CreateViewModelWithTwoEntitiesAndRelationship();

            var drawing = DiagramVectorRenderer.RenderDiagram(vm).Drawing;

            CountGlyphRunDrawings(drawing).Should().BeGreaterThan(0);
        });
    }

    /// <summary>NULL 許容表示 ON でカラム行の文字数が増える（NULL / NOT NULL が描かれる）ことを検証する</summary>
    [Fact(DisplayName = "RenderDiagram は NULL 許容表示 ON で GlyphRunDrawing が増える")]
    public void RenderDiagram_DrawsMoreGlyphRuns_WhenNullabilityShown()
    {
        RunSta(() =>
        {
            var vmWithout = CreateViewModelWithTwoEntitiesAndRelationship();
            vmWithout.ShowNullabilityInDiagram = false;

            var vmWith = CreateViewModelWithTwoEntitiesAndRelationship();
            vmWith.ShowNullabilityInDiagram = true;

            var countWithout = CountGlyphRunDrawings(
                DiagramVectorRenderer.RenderDiagram(vmWithout).Drawing
            );
            var countWith = CountGlyphRunDrawings(
                DiagramVectorRenderer.RenderDiagram(vmWith).Drawing
            );

            countWith.Should().BeGreaterThan(countWithout);
        });
    }

    /// <summary>図の範囲が全エンティティ（位置＋サイズ）を包含することを検証する</summary>
    [Fact(DisplayName = "CalculateDiagramBounds は全エンティティを包含する")]
    public void CalculateDiagramBounds_ContainsAllEntities()
    {
        RunSta(() =>
        {
            var vm = CreateViewModelWithTwoEntitiesAndRelationship();

            var bounds = DiagramVectorRenderer.CalculateDiagramBounds(vm);

            foreach (var entity in vm.Entities)
            {
                bounds
                    .Contains(new Rect(entity.X, entity.Y, entity.Width, entity.DisplayHeight))
                    .Should()
                    .BeTrue($"エンティティ {entity.TableName} が範囲に含まれること");
            }
        });
    }

    /// <summary>エンティティ 0 件のとき既定の 800x600 が返ることを検証する</summary>
    [Fact(DisplayName = "CalculateDiagramBounds はエンティティ 0 件なら 800x600 を返す")]
    public void CalculateDiagramBounds_ReturnsDefault_WhenEmpty()
    {
        RunSta(() =>
        {
            var bounds = DiagramVectorRenderer.CalculateDiagramBounds(new MainViewModel());

            bounds.Should().Be(new Rect(0, 0, 800, 600));
        });
    }

    /// <summary>エンティティ 2 件＋リレーション 1 件の VM を組み立てる</summary>
    private static MainViewModel CreateViewModelWithTwoEntitiesAndRelationship()
    {
        var vm = new MainViewModel();
        var customer = new EntityViewModel(
            new Entity
            {
                TableName = "Customer",
                Columns =
                {
                    new Column
                    {
                        Name = "Id",
                        DataType = "int",
                        IsPrimaryKey = true,
                    },
                    new Column
                    {
                        Name = "Name",
                        DataType = "nvarchar(50)",
                        IsNullable = true,
                    },
                },
            },
            new EntityLayout { X = 0, Y = 0 }
        );
        var order = new EntityViewModel(
            new Entity
            {
                TableName = "Order",
                Columns =
                {
                    new Column
                    {
                        Name = "Id",
                        DataType = "int",
                        IsPrimaryKey = true,
                    },
                    new Column
                    {
                        Name = "CustomerId",
                        DataType = "int",
                        IsForeignKey = true,
                    },
                },
            },
            new EntityLayout { X = 400, Y = 250 }
        );

        vm.Entities.Add(customer);
        vm.Entities.Add(order);
        vm.Relationships.Add(
            new RelationshipViewModel(
                new Relationship { SourceEntityId = customer.Id, TargetEntityId = order.Id },
                customer,
                order
            )
        );

        return vm;
    }

    /// <summary>Drawing ツリーを再帰走査し、含まれる <see cref="GlyphRunDrawing"/> の数を数える</summary>
    private static int CountGlyphRunDrawings(Drawing? drawing) =>
        drawing switch
        {
            GlyphRunDrawing => 1,
            DrawingGroup group => group.Children.Sum(CountGlyphRunDrawings),
            _ => 0,
        };
}
