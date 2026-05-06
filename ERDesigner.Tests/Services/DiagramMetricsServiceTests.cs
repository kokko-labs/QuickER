using ERDesigner.Models;
using ERDesigner.Services;
using ERDesigner.ViewModels;
using FluentAssertions;

namespace ERDesigner.Tests.Services;

/// <summary>
/// <see cref="DiagramMetricsService"/> のテスト。
/// </summary>
public class DiagramMetricsServiceTests
{
    [Fact(DisplayName = "CalculateAutoWidth: 長いカラム名と型が重ならない幅を返す")]
    public void CalculateAutoWidth_ReturnsWideEnoughWidth()
    {
        var entity = new EntityViewModel(
            new Entity
            {
                TableName = "Orders",
                Width = 120,
                Columns =
                {
                    new Column { Name = "VeryLongColumnNameForCustomerIdentifier", DataType = "nvarchar(128)" },
                },
            }
        );

        var width = DiagramMetricsService.CalculateAutoWidth(entity);

        width.Should().BeGreaterThan(200);
    }

    [Fact(DisplayName = "EstimateEntityHeight: 説明表示時は高さが増える")]
    public void EstimateEntityHeight_WithDescriptions_IsHigher()
    {
        var entity = new EntityViewModel(
            new Entity
            {
                TableName = "Orders",
                Width = 220,
                Description = "テーブル説明が複数行になるように十分長い文字列です。テーブル説明が複数行になるように十分長い文字列です。",
                Columns =
                {
                    new Column
                    {
                        Name = "CustomerName",
                        DataType = "nvarchar(100)",
                        Description = "カラム説明が折り返されることを確認するための十分に長い説明文です。",
                    },
                },
            }
        );

        var withoutDescriptions = DiagramMetricsService.EstimateEntityHeight(entity, showDescriptions: false);
        var withDescriptions = DiagramMetricsService.EstimateEntityHeight(entity, showDescriptions: true);

        withDescriptions.Should().BeGreaterThan(withoutDescriptions);
    }
}
