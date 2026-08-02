using System.IO;
using AwesomeAssertions;
using QuickER.Documents;
using QuickER.Gui.Abstractions;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Services;
using QuickER.Tests.TestDoubles;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.ViewModels;

/// <summary>
/// <see cref="MainViewModel"/> のズーム倍率・fit-to-window 要求ロジックを検証するテストクラス
/// </summary>
/// <remarks>WPF UI スレッドに依存しないロジックのみを対象とする（オフセット適用は View 側の責務）</remarks>
public class MainViewModelViewportTests
{
    /// <summary>スクロール済みビューポートが設定されていれば、追加エンティティが表示領域内へ置かれることを検証する</summary>
    [Fact(DisplayName = "AddEntity: 表示中のビューポート内へ配置される")]
    public void AddEntity_PlacesInsideViewportContentBounds()
    {
        var vm = new MainViewModel
        {
            ViewportContentBounds = new System.Windows.Rect(1000, 800, 900, 600),
        };

        vm.AddEntityCommand.Execute(null);

        var entity = vm.Entities[0];
        entity.X.Should().Be(1060);
        entity.Y.Should().Be(860);
    }

    /// <summary>ビューポート未設定（ヘッドレス）では従来のカスケード配置が維持されることを検証する</summary>
    [Fact(DisplayName = "AddEntity: ビューポート未設定は従来カスケード配置")]
    public void AddEntity_WithoutViewport_KeepsLegacyCascade()
    {
        var vm = new MainViewModel();

        vm.AddEntityCommand.Execute(null);
        vm.AddEntityCommand.Execute(null);

        vm.Entities[0].X.Should().Be(60);
        vm.Entities[0].Y.Should().Be(60);
        vm.Entities[1].X.Should().Be(90);
        vm.Entities[1].Y.Should().Be(90);
    }

    /// <summary>既定倍率が 100% であることを検証する</summary>
    [Fact(DisplayName = "ZoomLevel: 既定は 1.0")]
    public void ZoomLevel_DefaultsToOne()
    {
        var vm = new MainViewModel();

        vm.ZoomLevel.Should().Be(1.0);
        vm.ZoomPercentText.Should().Be("100%");
    }

    /// <summary>下限・上限を超える設定がクランプされることを検証する</summary>
    [Fact(DisplayName = "ZoomLevel: 範囲外はクランプされる")]
    public void ZoomLevel_ClampsOutOfRange()
    {
        var vm = new MainViewModel();

        vm.ZoomLevel = 0.01;
        vm.ZoomLevel.Should().Be(ViewportCalculator.MinZoom);

        vm.ZoomLevel = 99.0;
        vm.ZoomLevel.Should().Be(ViewportCalculator.MaxZoom);
    }

    /// <summary>倍率表示文字列が倍率に追従することを検証する</summary>
    [Fact(DisplayName = "ZoomPercentText: 倍率に追従する")]
    public void ZoomPercentText_TracksZoom()
    {
        var vm = new MainViewModel { ZoomLevel = 1.5 };

        vm.ZoomPercentText.Should().Be("150%");
    }

    /// <summary>ZoomIn/ZoomOut が 10% 刻みで増減することを検証する</summary>
    [Fact(DisplayName = "ZoomIn/ZoomOut: 10% 刻みで増減する")]
    public void ZoomInOut_StepsByTenPercent()
    {
        var vm = new MainViewModel();

        vm.ZoomInCommand.Execute(null);
        vm.ZoomLevel.Should().BeApproximately(1.1, 1e-9);

        vm.ZoomOutCommand.Execute(null);
        vm.ZoomLevel.Should().BeApproximately(1.0, 1e-9);
    }

    /// <summary>中途半端な倍率（fit 直後など）からの増減が 10% の倍数へスナップすることを検証する</summary>
    [Fact(DisplayName = "ZoomIn/ZoomOut: 中途半端な倍率は 10% の倍数へスナップ")]
    public void ZoomInOut_SnapsToTenPercentMultiples()
    {
        var vm = new MainViewModel { ZoomLevel = 0.873 };

        vm.ZoomInCommand.Execute(null);
        vm.ZoomLevel.Should().BeApproximately(0.9, 1e-9);

        vm.ZoomLevel = 0.873;
        vm.ZoomOutCommand.Execute(null);
        vm.ZoomLevel.Should().BeApproximately(0.8, 1e-9);
    }

    /// <summary>ResetZoom が 100% へ戻すことを検証する</summary>
    [Fact(DisplayName = "ResetZoom: 100% へ戻す")]
    public void ResetZoom_ReturnsToHundredPercent()
    {
        var vm = new MainViewModel { ZoomLevel = 2.5 };

        vm.ResetZoomCommand.Execute(null);

        vm.ZoomLevel.Should().Be(1.0);
    }

    /// <summary>FitToWindowCommand が FitToWindowRequested を発火することを検証する</summary>
    [Fact(DisplayName = "FitToWindowCommand: FitToWindowRequested を発火する")]
    public void FitToWindowCommand_RaisesEvent()
    {
        var vm = new MainViewModel();
        var raised = 0;
        vm.FitToWindowRequested += (_, _) => raised++;

        vm.FitToWindowCommand.Execute(null);

        raised.Should().Be(1);
    }

    /// <summary>格子整列コマンドが FitToWindowRequested を発火することを検証する</summary>
    [Fact(DisplayName = "AutoLayoutGrid: FitToWindowRequested を発火する")]
    public void AutoLayoutGrid_RaisesFitRequest()
    {
        var vm = new MainViewModel();
        vm.AddEntityCommand.Execute(null);
        var raised = 0;
        vm.FitToWindowRequested += (_, _) => raised++;

        vm.AutoLayoutGridCommand.Execute(null);

        raised.Should().Be(1);
    }

    /// <summary>木整列コマンドが FitToWindowRequested を発火することを検証する</summary>
    [Fact(DisplayName = "AutoLayoutTree: FitToWindowRequested を発火する")]
    public void AutoLayoutTree_RaisesFitRequest()
    {
        var vm = new MainViewModel();
        vm.AddEntityCommand.Execute(null);
        var raised = 0;
        vm.FitToWindowRequested += (_, _) => raised++;

        vm.AutoLayoutTreeCommand.Execute(null);

        raised.Should().Be(1);
    }

    /// <summary>自由整列コマンドが FitToWindowRequested を発火することを検証する</summary>
    [Fact(DisplayName = "AutoLayoutForce: FitToWindowRequested を発火する")]
    public void AutoLayoutForce_RaisesFitRequest()
    {
        var vm = new MainViewModel();
        vm.AddEntityCommand.Execute(null);
        var raised = 0;
        vm.FitToWindowRequested += (_, _) => raised++;

        vm.AutoLayoutForceCommand.Execute(null);

        raised.Should().Be(1);
    }

    /// <summary>AI 生成直後の整列（AutoArrangeNewDiagram）が FitToWindowRequested を発火することを検証する</summary>
    [Fact(DisplayName = "AutoArrangeNewDiagram: FitToWindowRequested を発火する")]
    public void AutoArrangeNewDiagram_RaisesFitRequest()
    {
        var vm = new MainViewModel();
        vm.AddEntityCommand.Execute(null);
        var raised = 0;
        vm.FitToWindowRequested += (_, _) => raised++;

        vm.AutoArrangeNewDiagram();

        raised.Should().Be(1);
    }

    /// <summary>ファイル読込（ReplaceDiagram 経由）が FitToWindowRequested を発火することを検証する</summary>
    [Fact(DisplayName = "Open（ReplaceDiagram 経由）: FitToWindowRequested を発火する")]
    public void Open_RaisesFitRequest()
    {
        var path = Path.Combine(Path.GetTempPath(), $"er-viewport-{Guid.NewGuid()}.json");

        // 読み込み対象のドキュメントを一旦保存しておく
        var document = new DiagramDocument
        {
            Schema = new ErDiagram
            {
                TargetDbms = "sqlserver",
                Entities = { new Entity { TableName = "T1" } },
            },
        };
        JsonStorageService.Save(path, document);

        var files = new StubFileDialogService { OpenResult = new FileDialogResult(path, 1) };
        var vm = new MainViewModel(new StubDialogService(), files: files);
        var raised = 0;
        vm.FitToWindowRequested += (_, _) => raised++;

        try
        {
            vm.OpenCommand.Execute(null);

            vm.Entities.Should().ContainSingle();
            raised.Should().BeGreaterThanOrEqualTo(1);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    // StubDialogService / StubFileDialogService は共有版（QuickER.Tests.TestDoubles）を使用する
}
