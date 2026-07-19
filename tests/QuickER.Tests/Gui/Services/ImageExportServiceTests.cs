using FluentAssertions;
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
}
