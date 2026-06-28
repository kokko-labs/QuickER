using QuickER.Model;
using QuickER.Services;
using QuickER.ViewModels;
using FluentAssertions;

namespace QuickER.Tests.Services;

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
                new Entity { TableName = "Customer", TitleBackgroundColor = "#E4F1C9" }
            )
        );

        var svg = ImageExportService.BuildSvg(vm);

        svg.Should()
            .Contain("<rect width=\"200\" height=\"28\" rx=\"6\" ry=\"6\" fill=\"#E4F1C9\" />");
        svg.Should().Contain(">Customer</text>");
    }
}
