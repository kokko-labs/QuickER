using FluentAssertions;
using QuickER.Documents;
using QuickER.Model;
using QuickER.Services;
using QuickER.ViewModels;

namespace QuickER.Tests.Services;

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
                Columns =
                {
                    new Column
                    {
                        Name = "VeryLongColumnNameForCustomerIdentifier",
                        DataType = "nvarchar(128)",
                    },
                },
            },
            new EntityLayout { Width = 120 }
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
                Description =
                    "テーブル説明が複数行になるように十分長い文字列です。テーブル説明が複数行になるように十分長い文字列です。",
                Columns =
                {
                    new Column
                    {
                        Name = "CustomerName",
                        DataType = "nvarchar(100)",
                        Description =
                            "カラム説明が折り返されることを確認するための十分に長い説明文です。",
                    },
                },
            },
            new EntityLayout { Width = 220 }
        );

        var withoutDescriptions = DiagramMetricsService.EstimateEntityHeight(
            entity,
            showDescriptions: false
        );
        var withDescriptions = DiagramMetricsService.EstimateEntityHeight(
            entity,
            showDescriptions: true
        );

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
                Columns =
                {
                    new Column
                    {
                        Name = "Code",
                        DataType = "nvarchar(128)",
                        IsNullable = false,
                    },
                },
            },
            new EntityLayout { Width = 120 }
        );

        entity.ShowNullabilityInDiagram = false;
        var withoutNullability = DiagramMetricsService.CalculateAutoWidth(entity);
        entity.ShowNullabilityInDiagram = true;
        var withNullability = DiagramMetricsService.CalculateAutoWidth(entity);

        withNullability.Should().BeGreaterThan(withoutNullability);
    }

    /// <summary>簡易表示時に PK/FK 以外のカラム行が高さから除外され、カードが縮むことを検証する</summary>
    [Fact(DisplayName = "EstimateEntityHeight: 簡易表示時は PK/FK 以外の行分だけ縮む")]
    public void EstimateEntityHeight_CompactView_CountsOnlyKeyColumns()
    {
        // PK1 + FK1 + 一般3 のエンティティ
        var entity = new EntityViewModel(
            new Entity
            {
                TableName = "Orders",
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
                    new Column { Name = "Note1", DataType = "nvarchar(50)" },
                    new Column { Name = "Note2", DataType = "nvarchar(50)" },
                    new Column { Name = "Note3", DataType = "nvarchar(50)" },
                },
            },
            new EntityLayout { Width = 220 }
        );

        var fullHeight = DiagramMetricsService.EstimateEntityHeight(
            entity,
            showDescriptions: false,
            isCompactView: false
        );
        var compactHeight = DiagramMetricsService.EstimateEntityHeight(
            entity,
            showDescriptions: false,
            isCompactView: true
        );

        // 一般カラム3行分縮むため簡易表示のほうが小さい
        compactHeight.Should().BeLessThan(fullHeight);

        // 簡易表示の高さは「PK/FK の 2 行のみ」を数えた高さと一致する
        var twoKeyColumns = new EntityViewModel(
            new Entity
            {
                TableName = "Orders",
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
            new EntityLayout { Width = 220 }
        );
        var twoRowHeight = DiagramMetricsService.EstimateEntityHeight(
            twoKeyColumns,
            showDescriptions: false,
            isCompactView: false
        );

        compactHeight.Should().Be(twoRowHeight);
    }

    /// <summary>PK/FK のみのエンティティは簡易表示でも高さが変わらないことを検証する</summary>
    [Fact(DisplayName = "EstimateEntityHeight: PK/FK のみのエンティティは簡易表示で高さ不変")]
    public void EstimateEntityHeight_CompactView_KeyOnlyEntity_IsUnchanged()
    {
        var entity = new EntityViewModel(
            new Entity
            {
                TableName = "OrderItems",
                Columns =
                {
                    new Column
                    {
                        Name = "OrderId",
                        DataType = "int",
                        IsPrimaryKey = true,
                    },
                    new Column
                    {
                        Name = "ProductId",
                        DataType = "int",
                        IsForeignKey = true,
                    },
                },
            },
            new EntityLayout { Width = 220 }
        );

        var fullHeight = DiagramMetricsService.EstimateEntityHeight(
            entity,
            showDescriptions: false,
            isCompactView: false
        );
        var compactHeight = DiagramMetricsService.EstimateEntityHeight(
            entity,
            showDescriptions: false,
            isCompactView: true
        );

        compactHeight.Should().Be(fullHeight);
    }
}
