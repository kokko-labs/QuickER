using System.Globalization;
using AwesomeAssertions;
using QuickER.Documents;
using QuickER.Model;
using QuickER.Services;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.Services;

/// <summary><see cref="ImageExportService"/> の SVG 生成を検証するテストクラス</summary>
public class ImageExportServiceTests
{
    /// <summary>生成 SVG にエンティティ見出しの背景色とテーブル名が含まれることを検証する</summary>
    [Fact(DisplayName = "BuildSvg はエンティティ見出しの背景色を出力する")]
    public void BuildSvg_UsesEntityTitleBackgroundColor()
    {
        var vm = new MainViewModel();
        vm.Entities.Add(
            new EntityViewModel(
                new Entity { TableName = "Customer" },
                new EntityLayout { TitleBackgroundColor = "#E4F1C9" }
            )
        );

        var svg = ImageExportService.BuildSvg(vm);

        svg.Should().Contain("fill=\"#E4F1C9\"");
        svg.Should().Contain(">Customer</text>");
    }

    /// <summary>透明背景がダークモードのビューアで黒く見えないよう、白背景が敷かれることを検証する</summary>
    [Fact(DisplayName = "BuildSvg はキャンバス全体に白背景を出力する")]
    public void BuildSvg_OutputsWhiteBackground()
    {
        var vm = new MainViewModel();
        vm.Entities.Add(
            new EntityViewModel(new Entity { TableName = "Customer" }, new EntityLayout())
        );

        var svg = ImageExportService.BuildSvg(vm);

        svg.Should().Contain("<rect width=\"100%\" height=\"100%\" fill=\"#fff\" />");
    }

    /// <summary>NULL 許容表示 ON のとき、カラム行に NULL / NOT NULL が出力されることを検証する</summary>
    [Fact(DisplayName = "BuildSvg は NULL 許容表示 ON のとき NULL / NOT NULL を出力する")]
    public void BuildSvg_OutputsNullability_WhenEnabled()
    {
        var vm = new MainViewModel { ShowNullabilityInDiagram = true };
        vm.Entities.Add(
            new EntityViewModel(
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
                            Name = "Note",
                            DataType = "nvarchar(100)",
                            IsNullable = true,
                        },
                    },
                }
            )
        );

        var svg = ImageExportService.BuildSvg(vm);

        svg.Should().Contain(">NOT NULL</text>");
        svg.Should().Contain(">NULL</text>");
    }

    /// <summary>NULL 許容表示 OFF のとき、NULL / NOT NULL が出力されないことを検証する</summary>
    [Fact(DisplayName = "BuildSvg は NULL 許容表示 OFF のとき NULL 表記を出力しない")]
    public void BuildSvg_OmitsNullability_WhenDisabled()
    {
        var vm = new MainViewModel { ShowNullabilityInDiagram = false };
        vm.Entities.Add(
            new EntityViewModel(
                new Entity
                {
                    TableName = "Customer",
                    Columns =
                    {
                        new Column { Name = "Id", DataType = "int" },
                    },
                }
            )
        );

        var svg = ImageExportService.BuildSvg(vm);

        svg.Should().NotContain(">NOT NULL</text>");
        svg.Should().NotContain(">NULL</text>");
    }

    /// <summary>説明表示 ON のとき、テーブル説明とカラム説明が出力されることを検証する</summary>
    [Fact(DisplayName = "BuildSvg は説明表示 ON のときテーブル・カラムの説明を出力する")]
    public void BuildSvg_OutputsDescriptions_WhenEnabled()
    {
        var vm = new MainViewModel { ShowColumnDescriptionsInDiagram = true };
        vm.Entities.Add(
            new EntityViewModel(
                new Entity
                {
                    TableName = "Customer",
                    Description = "顧客マスタ",
                    Columns =
                    {
                        new Column
                        {
                            Name = "Id",
                            DataType = "int",
                            Description = "主キー",
                        },
                    },
                }
            )
        );

        var svg = ImageExportService.BuildSvg(vm);

        svg.Should().Contain(">顧客マスタ</text>");
        svg.Should().Contain(">主キー</text>");
    }

    /// <summary>説明表示 OFF のとき、説明テキストが出力されないことを検証する</summary>
    [Fact(DisplayName = "BuildSvg は説明表示 OFF のとき説明を出力しない")]
    public void BuildSvg_OmitsDescriptions_WhenDisabled()
    {
        var vm = new MainViewModel { ShowColumnDescriptionsInDiagram = false };
        vm.Entities.Add(
            new EntityViewModel(
                new Entity
                {
                    TableName = "Customer",
                    Description = "顧客マスタ",
                    Columns =
                    {
                        new Column
                        {
                            Name = "Id",
                            DataType = "int",
                            Description = "主キー",
                        },
                    },
                }
            )
        );

        var svg = ImageExportService.BuildSvg(vm);

        svg.Should().NotContain("顧客マスタ");
        svg.Should().NotContain("主キー");
    }

    /// <summary>
    /// SVG のエンティティ枠の高さが <see cref="EntityViewModel.DisplayHeight"/>（リレーション線の
    /// 端点計算の基礎）と一致し、線とカード枠がずれないことを検証する
    /// </summary>
    [Fact(DisplayName = "BuildSvg のエンティティ枠は DisplayHeight と同じ高さで出力される")]
    public void BuildSvg_EntityRectHeight_MatchesDisplayHeight()
    {
        var vm = new MainViewModel { ShowColumnDescriptionsInDiagram = true };
        var entity = new EntityViewModel(
            new Entity
            {
                TableName = "Customer",
                Description = "顧客マスタ（説明表示で見出しが高くなるケース）",
                Columns =
                {
                    new Column
                    {
                        Name = "Id",
                        DataType = "int",
                        IsPrimaryKey = true,
                        Description = "主キー",
                    },
                    new Column { Name = "Name", DataType = "nvarchar(50)" },
                },
            }
        );
        vm.Entities.Add(entity);

        var svg = ImageExportService.BuildSvg(vm);

        svg.Should()
            .Contain(
                $"<rect class=\"entity\" width=\"200\" height=\"{entity.DisplayHeight:0.##}\""
            );
    }

    /// <summary>簡易表示 ON のとき、PK/FK 以外のカラム行が出力されないことを検証する</summary>
    [Fact(DisplayName = "BuildSvg は簡易表示 ON のとき PK/FK 以外のカラムを出力しない")]
    public void BuildSvg_OmitsNonKeyColumns_WhenCompactView()
    {
        var vm = new MainViewModel { IsCompactViewInDiagram = true };
        vm.Entities.Add(
            new EntityViewModel(
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
                        new Column { Name = "Address", DataType = "nvarchar(200)" },
                    },
                }
            )
        );

        var svg = ImageExportService.BuildSvg(vm);

        svg.Should().Contain(">Id</text>");
        svg.Should().NotContain(">Address</text>");
    }

    /// <summary>
    /// 自己参照リレーションが（両端点が同一点で消えるゼロ長の線ではなく）
    /// 画面と同じループ楕円として出力されることを検証する
    /// </summary>
    [Fact(DisplayName = "BuildSvg は自己参照リレーションをループ楕円として出力する")]
    public void BuildSvg_OutputsSelfLoopEllipse_ForSelfRelationship()
    {
        var vm = CreateViewModelWithSelfRelationship();
        var relationship = vm.Relationships[0];

        var svg = ImageExportService.BuildSvg(vm);

        // 画面（MainWindow.xaml）の Ellipse は配置枠（SelfLoopLeft/Top・SelfLoopWidth/Height）へ
        // 線幅ぶん食い込んで描かれるため、中心＝枠の中心・半径＝(枠サイズ − 線幅) / 2 と一致すること
        svg.Should()
            .Contain(
                $"<ellipse class=\"rel\" cx=\"{F(relationship.SelfLoopLeft + relationship.SelfLoopWidth / 2)}\""
                    + $" cy=\"{F(relationship.SelfLoopTop + relationship.SelfLoopHeight / 2)}\""
                    + $" rx=\"{F((relationship.SelfLoopWidth - RelationStrokeThickness) / 2)}\""
                    + $" ry=\"{F((relationship.SelfLoopHeight - RelationStrokeThickness) / 2)}\" />"
            );

        // ゼロ長の線（X1,Y1 == X2,Y2）が出力されないこと
        svg.Should().NotContain("<line");
    }

    /// <summary>自己参照ループがキャンバス右端で欠けないよう、SVG の幅がループ全体を含むことを検証する</summary>
    [Fact(DisplayName = "BuildSvg のキャンバス幅は自己参照ループ全体を含む")]
    public void BuildSvg_CanvasWidth_ContainsSelfLoop()
    {
        var vm = CreateViewModelWithSelfRelationship();
        var relationship = vm.Relationships[0];

        var svg = ImageExportService.BuildSvg(vm);

        // width="..." を取り出してループ右端と比較する
        var widthMarker = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"";
        var start = svg.IndexOf(widthMarker, StringComparison.Ordinal) + widthMarker.Length;
        var width = double.Parse(svg[start..svg.IndexOf('"', start)], CultureInfo.InvariantCulture);

        width
            .Should()
            .BeGreaterThanOrEqualTo(relationship.SelfLoopLeft + relationship.SelfLoopWidth);
    }

    /// <summary>通常リレーションの出力（線・スタイル定義）が従来どおり変わらないことを検証する</summary>
    [Fact(DisplayName = "BuildSvg は通常リレーションを従来どおりの線として出力する")]
    public void BuildSvg_OutputsLine_ForNormalRelationship()
    {
        var vm = CreateViewModelWithTwoEntitiesAndRelationship();
        var relationship = vm.Relationships[0];

        var svg = ImageExportService.BuildSvg(vm);

        svg.Should().Contain(".rel{stroke:#5F6B7A;stroke-width:1.6;fill:none}");
        svg.Should()
            .Contain(
                $"  <line class=\"rel\" x1=\"{F(relationship.X1)}\" y1=\"{F(relationship.Y1)}\""
                    + $" x2=\"{F(relationship.X2)}\" y2=\"{F(relationship.Y2)}\" />"
            );
        svg.Should().NotContain("<ellipse");
    }

    /// <summary>リレーション線の太さ（SVG の .rel stroke-width と同一）</summary>
    private const double RelationStrokeThickness = 1.6;

    /// <summary>SVG 出力と同じ書式（不変カルチャ・小数 2 桁まで）で数値を整形する</summary>
    private static string F(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

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
            // 既定の最小キャンバス幅（400）より右へ置き、ループのはみ出しが幅計算に効くようにする
            new EntityLayout { X = 400, Y = 90 }
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

    /// <summary>エンティティ 2 件＋通常リレーション 1 件の VM を組み立てる</summary>
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
                new Relationship
                {
                    SourceEntityId = customer.Id,
                    TargetEntityId = order.Id,
                    Type = RelationshipType.OneToMany,
                },
                customer,
                order
            )
        );

        return vm;
    }
}
