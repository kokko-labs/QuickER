using System.Windows;
using FluentAssertions;
using QuickER.Model;
using QuickER.Services;
using QuickER.ViewModels;
using static QuickER.Tests.TestSupport.WpfApplicationTestSupport;

namespace QuickER.Tests.Gui.Services;

/// <summary><see cref="DiagramPrintService"/> の縮小フィット・ヘッダ生成・ページ合成を検証するテストクラス</summary>
public class DiagramPrintServiceTests
{
    /// <summary>領域より大きい図は縮小倍率が求まることを検証する</summary>
    [Fact(DisplayName = "CalculateFitScale は大きい図を用紙内へ縮小する")]
    public void CalculateFitScale_ScalesDownLargeContent()
    {
        var scale = DiagramPrintService.CalculateFitScale(
            new Size(2000, 1000),
            new Size(1000, 500)
        );

        scale.Should().Be(0.5);
    }

    /// <summary>領域より小さい図は等倍のまま（拡大しない）ことを検証する</summary>
    [Fact(DisplayName = "CalculateFitScale は小さい図を拡大せず等倍にする")]
    public void CalculateFitScale_DoesNotEnlargeSmallContent()
    {
        var scale = DiagramPrintService.CalculateFitScale(new Size(400, 300), new Size(1000, 800));

        scale.Should().Be(1.0);
    }

    /// <summary>縦横で厳しい方の倍率が採用されることを検証する</summary>
    [Fact(DisplayName = "CalculateFitScale は縦横で厳しい方の倍率を採用する")]
    public void CalculateFitScale_UsesTighterAxis()
    {
        // 幅は 0.5、高さは 1.25。厳しい方の 0.5 が採用される
        var scale = DiagramPrintService.CalculateFitScale(new Size(2000, 400), new Size(1000, 500));

        scale.Should().Be(0.5);
    }

    /// <summary>寸法が 0 以下のとき等倍（1.0）で扱うことを検証する</summary>
    [Theory(DisplayName = "CalculateFitScale は寸法が 0 以下なら 1.0 を返す")]
    [InlineData(0, 100, 100, 100)]
    [InlineData(100, 0, 100, 100)]
    [InlineData(100, 100, 0, 100)]
    [InlineData(100, 100, 100, 0)]
    public void CalculateFitScale_ReturnsOne_WhenAnyDimensionNonPositive(
        double contentW,
        double contentH,
        double availableW,
        double availableH
    )
    {
        var scale = DiagramPrintService.CalculateFitScale(
            new Size(contentW, contentH),
            new Size(availableW, availableH)
        );

        scale.Should().Be(1.0);
    }

    /// <summary>原寸大印刷の用紙サイズが実寸＋余白＋ヘッダ領域になることを検証する</summary>
    [Fact(DisplayName = "CalculateActualSizePageSize は実寸に余白とヘッダ領域を加える")]
    public void CalculateActualSizePageSize_AddsMarginsAndHeader()
    {
        var pageSize = DiagramPrintService.CalculateActualSizePageSize(
            new Size(2000, 1400),
            headerHeight: 16
        );

        // 幅 = 2000 + 左右余白 40×2、高さ = 1400 + 上下余白 40×2 + ヘッダ 16 + ヘッダ下余白 8
        pageSize.Width.Should().Be(2080);
        pageSize.Height.Should().Be(1504);
    }

    /// <summary>実寸が不正（0 以下）のとき既定サイズ 800x600 を実寸とみなすことを検証する</summary>
    [Fact(DisplayName = "CalculateActualSizePageSize は実寸が 0 以下なら 800x600 で計算する")]
    public void CalculateActualSizePageSize_FallsBackToDefaultSize_WhenContentNonPositive()
    {
        var pageSize = DiagramPrintService.CalculateActualSizePageSize(
            new Size(0, 0),
            headerHeight: 16
        );

        // 幅 = 800 + 左右余白 40×2、高さ = 600 + 上下余白 40×2 + ヘッダ 16 + ヘッダ下余白 8
        pageSize.Width.Should().Be(880);
        pageSize.Height.Should().Be(704);
    }

    /// <summary>タイトルが空欄・日時印字 ON のとき、ヘッダが「無題」を出さず日時のみになることを検証する</summary>
    [Fact(DisplayName = "BuildHeaderText はタイトル空欄・日時 ON のとき日時のみを返す")]
    public void BuildHeaderText_ReturnsTimestampOnly_WhenTitleBlankAndTimestampEnabled()
    {
        var text = DiagramPrintService.BuildHeaderText(
            null,
            new DateTime(2026, 7, 4, 9, 30, 0),
            includeTimestamp: true
        );

        text.Should().Be("2026/07/04 09:30");
        text.Should().NotContain("無題");
    }

    /// <summary>タイトルが空欄・日時印字 OFF のとき、ヘッダが空文字（何も印字しない）になることを検証する</summary>
    [Fact(DisplayName = "BuildHeaderText はタイトル空欄・日時 OFF のとき空文字を返す")]
    public void BuildHeaderText_ReturnsEmpty_WhenTitleBlankAndTimestampDisabled()
    {
        var text = DiagramPrintService.BuildHeaderText(
            "   ",
            new DateTime(2026, 7, 4, 9, 30, 0),
            includeTimestamp: false
        );

        text.Should().BeEmpty();
    }

    /// <summary>タイトルが指定されているときヘッダがその名前で始まることを検証する</summary>
    [Fact(DisplayName = "BuildHeaderText はタイトルで始まる")]
    public void BuildHeaderText_StartsWithTitle()
    {
        var text = DiagramPrintService.BuildHeaderText(
            "顧客管理",
            new DateTime(2026, 7, 4, 9, 30, 0),
            includeTimestamp: true
        );

        text.Should().StartWith("顧客管理");
    }

    /// <summary>日時印字 ON のときヘッダに "yyyy/MM/dd HH:mm" 形式の日時が含まれることを検証する</summary>
    [Fact(DisplayName = "BuildHeaderText は日時印字 ON のとき印刷日時を含む")]
    public void BuildHeaderText_ContainsFormattedTimestamp_WhenEnabled()
    {
        var text = DiagramPrintService.BuildHeaderText(
            "顧客管理",
            new DateTime(2026, 7, 4, 9, 30, 0),
            includeTimestamp: true
        );

        text.Should().Contain("2026/07/04 09:30");
    }

    /// <summary>日時印字 OFF のときヘッダがタイトルのみで日時を含まないことを検証する</summary>
    [Fact(DisplayName = "BuildHeaderText は日時印字 OFF のとき日時を含まない")]
    public void BuildHeaderText_OmitsTimestamp_WhenDisabled()
    {
        var text = DiagramPrintService.BuildHeaderText(
            "顧客管理",
            new DateTime(2026, 7, 4, 9, 30, 0),
            includeTimestamp: false
        );

        text.Should().Be("顧客管理");
    }

    /// <summary>ページ合成が DrawingVisual を返し、描画内容を持つことを検証する</summary>
    [Fact(DisplayName = "CreatePrintVisual は描画内容を持つ DrawingVisual を返す")]
    public void CreatePrintVisual_ReturnsVisualWithDrawing()
    {
        RunSta(() =>
        {
            // エンティティ 1 件の VM を印刷対象とする（図は VM から直接ベクタ描画される）
            var vm = new MainViewModel();
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
                        },
                    }
                )
            );

            var contentBounds = DiagramVectorRenderer.CalculateDiagramBounds(vm);
            var imageableArea = new Rect(40, 40, 700, 500);

            var visual = DiagramPrintService.CreatePrintVisual(
                vm,
                contentBounds,
                imageableArea,
                "無題  2026/07/04 09:30"
            );

            visual.Should().NotBeNull();
            visual.Drawing.Should().NotBeNull();
        });
    }

    /// <summary>
    /// 原寸大印刷のレイアウト連鎖を検証する: 自前の用紙サイズ → 自前の印刷可能領域 →
    /// ヘッダを除いた残り領域が図の実寸と一致し、フィット倍率が 1.0（原寸）になる。
    /// ドライバ報告の印刷可能領域（標準用紙サイズ）を誤って使うと縮小される回帰の防止
    /// </summary>
    [Fact(DisplayName = "原寸大印刷は自前の印刷可能領域によりフィット倍率が 1.0 になる")]
    public void ActualSizeLayoutChain_YieldsScaleOne()
    {
        var content = new Size(2000, 1400);
        const double headerHeight = 16;

        var pageSize = DiagramPrintService.CalculateActualSizePageSize(content, headerHeight);
        var imageable = DiagramPrintService.CalculateActualSizeImageableArea(pageSize);

        // CreatePrintVisual と同じ計算: ヘッダ高さ + 8px の間隔を除いた残りが図の配置領域
        var diagramTop = imageable.Top + headerHeight + 8;
        var available = new Size(imageable.Width, imageable.Bottom - diagramTop);

        var scale = DiagramPrintService.CalculateFitScale(content, available);

        scale.Should().Be(1.0);
    }
}
