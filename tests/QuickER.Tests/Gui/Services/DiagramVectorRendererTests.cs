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

    /// <summary>
    /// 自己参照リレーションが（両端点が同一点で消えるゼロ長の線ではなく）
    /// 画面と同じループ楕円として描かれることを検証する
    /// </summary>
    [Fact(DisplayName = "RenderDiagram は自己参照リレーションをループ楕円として描く")]
    public void RenderDiagram_DrawsSelfLoopEllipse_ForSelfRelationship()
    {
        RunSta(() =>
        {
            var vm = CreateViewModelWithSelfRelationship();
            var relationship = vm.Relationships[0];

            var drawing = DiagramVectorRenderer.RenderDiagram(vm).Drawing;

            var ellipses = EnumerateGeometries(drawing).OfType<EllipseGeometry>().ToList();
            ellipses.Should().HaveCount(1, "自己参照 1 件ぶんのループ楕円が描かれること");

            // 画面（MainWindow.xaml）の Ellipse は配置枠（SelfLoopLeft/Top・SelfLoopWidth/Height）へ
            // 線幅ぶん食い込んで描かれるため、中心＝枠の中心・半径＝(枠サイズ − 線幅) / 2 と一致すること
            var ellipse = ellipses[0];
            ellipse
                .Center.X.Should()
                .BeApproximately(relationship.SelfLoopLeft + relationship.SelfLoopWidth / 2, 0.001);
            ellipse
                .Center.Y.Should()
                .BeApproximately(relationship.SelfLoopTop + relationship.SelfLoopHeight / 2, 0.001);
            ellipse
                .RadiusX.Should()
                .BeApproximately((relationship.SelfLoopWidth - RelationStrokeThickness) / 2, 0.001);
            ellipse
                .RadiusY.Should()
                .BeApproximately(
                    (relationship.SelfLoopHeight - RelationStrokeThickness) / 2,
                    0.001
                );

            // ゼロ長の線（X1,Y1 == X2,Y2）が描かれないこと
            EnumerateGeometries(drawing)
                .OfType<LineGeometry>()
                .Should()
                .NotContain(line => line.StartPoint == line.EndPoint);
        });
    }

    /// <summary>通常リレーションが端点間の線として描かれることを検証する</summary>
    [Fact(DisplayName = "RenderDiagram は通常リレーションを線として描く")]
    public void RenderDiagram_DrawsLine_ForNormalRelationship()
    {
        RunSta(() =>
        {
            var vm = CreateViewModelWithTwoEntitiesAndRelationship();
            var relationship = vm.Relationships[0];

            var drawing = DiagramVectorRenderer.RenderDiagram(vm).Drawing;

            var lines = EnumerateGeometries(drawing).OfType<LineGeometry>().ToList();
            lines.Should().HaveCount(1);
            lines[0].StartPoint.Should().Be(new Point(relationship.X1, relationship.Y1));
            lines[0].EndPoint.Should().Be(new Point(relationship.X2, relationship.Y2));
            EnumerateGeometries(drawing).OfType<EllipseGeometry>().Should().BeEmpty();
        });
    }

    /// <summary>自己参照ループがページ端で欠けないよう、図の範囲がループ全体を含むことを検証する</summary>
    [Fact(DisplayName = "CalculateDiagramBounds は自己参照ループ全体を含む")]
    public void CalculateDiagramBounds_ContainsSelfLoop()
    {
        RunSta(() =>
        {
            var vm = CreateViewModelWithSelfRelationship();
            var relationship = vm.Relationships[0];

            var bounds = DiagramVectorRenderer.CalculateDiagramBounds(vm);

            bounds
                .Contains(
                    new Rect(
                        relationship.SelfLoopLeft,
                        relationship.SelfLoopTop,
                        relationship.SelfLoopWidth,
                        relationship.SelfLoopHeight
                    )
                )
                .Should()
                .BeTrue("自己参照ループが範囲に含まれること");
        });
    }

    /// <summary>リレーション線の太さ（DiagramVectorRenderer の RelationPen と同一）</summary>
    private const double RelationStrokeThickness = 1.6;

    /// <summary>エンティティ 1 件と、それ自身を指す自己参照リレーション 1 件の VM を組み立てる</summary>
    private static MainViewModel CreateViewModelWithSelfRelationship()
    {
        var vm = new MainViewModel();
        var employee = new EntityViewModel(
            new Entity
            {
                TableName = "Employee",
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
                        Name = "ManagerId",
                        DataType = "int",
                        IsForeignKey = true,
                        IsNullable = true,
                    },
                },
            },
            new EntityLayout { X = 200, Y = 150 }
        );

        vm.Entities.Add(employee);
        vm.Relationships.Add(
            new RelationshipViewModel(
                new Relationship
                {
                    SourceEntityId = employee.Id,
                    TargetEntityId = employee.Id,
                    Type = RelationshipType.OneToMany,
                },
                employee,
                employee
            )
        );

        return vm;
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

    /// <summary>Drawing ツリーを再帰走査し、含まれる図形ジオメトリを列挙する</summary>
    private static IEnumerable<Geometry> EnumerateGeometries(Drawing? drawing)
    {
        switch (drawing)
        {
            case GeometryDrawing geometryDrawing when geometryDrawing.Geometry is not null:
                yield return geometryDrawing.Geometry;
                break;

            case DrawingGroup group:
                foreach (var child in group.Children.SelectMany(EnumerateGeometries))
                {
                    yield return child;
                }

                break;
        }
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
