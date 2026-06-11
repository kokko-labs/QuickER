using ERDesigner.Models;
using ERDesigner.Services;
using ERDesigner.ViewModels;
using FluentAssertions;

namespace ERDesigner.Tests.Services;

/// <summary><see cref="DiagramMetricsService"/> の幅・高さ見積もりを検証するテストクラス</summary>
public class DiagramMetricsServiceTests
{
    /// <summary>長いカラム名と型が重ならない十分な幅が返ることを検証する</summary>
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

    /// <summary>説明表示ありの見積もり高さが説明なしより大きくなることを検証する</summary>
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

    /// <summary>NULL 許容表示ありの自動幅が表示なしより大きくなることを検証する</summary>
    [Fact(DisplayName = "CalculateAutoWidth: NULL 表示時は幅が増える")]
    public void CalculateAutoWidth_WithNullability_IsWider()
    {
        var entity = new EntityViewModel(
            new Entity
            {
                TableName = "Orders",
                Width = 120,
                Columns =
                {
                    new Column
                    {
                        Name = "Code",
                        DataType = "nvarchar(128)",
                        IsNullable = false,
                    },
                },
            }
        );

        entity.ShowNullabilityInDiagram = false;
        var withoutNullability = DiagramMetricsService.CalculateAutoWidth(entity);
        entity.ShowNullabilityInDiagram = true;
        var withNullability = DiagramMetricsService.CalculateAutoWidth(entity);

        withNullability.Should().BeGreaterThan(withoutNullability);
    }
}
