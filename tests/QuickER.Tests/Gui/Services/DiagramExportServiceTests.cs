using System;
using System.IO;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Resources;
using QuickER.Services;
using QuickER.Tests.TestDoubles;

namespace QuickER.Tests.Gui.Services;

/// <summary>
/// <see cref="DiagramExportService"/> の単体検証（スタブホスト＋スタブダイアログ＝MainViewModel を組み立てない）。
/// </summary>
/// <remarks>
/// VM 経由の統合検証（MainViewModelExportOmissionTests 等）は委譲の配線を固定し、ここではサービス単体の
/// 判断ロジック（形式解決・欠落告知のセッション 1 回・キャンセル・失敗報告）を軽量に固定する。
/// </remarks>
public class DiagramExportServiceTests
{
    /// <summary>NOT NULL 列を持つ最小の図（Mermaid 出力で欠落告知＝ColumnNullability が必ず立つ）</summary>
    private static ErDiagram BuildModelWithOmission() =>
        new()
        {
            Entities =
            [
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "Customer",
                    Columns =
                    [
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "CustomerId",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                    ],
                },
            ],
        };

    /// <summary>サービスと観測用スタブ一式を組み立てる</summary>
    private static (
        DiagramExportService Service,
        StubDiagramTransferHost Host,
        StubDialogService Dialogs
    ) Create(QuickER.Gui.Abstractions.FileDialogResult? saveResult = null)
    {
        var host = new StubDiagramTransferHost();
        var dialogs = new StubDialogService();
        var files = new StubFileDialogService { SaveResult = saveResult };
        return (new DiagramExportService(host, dialogs, files), host, dialogs);
    }

    [Theory(DisplayName = "形式解決: 拡張子が最優先・無ければフィルター順・どちらも無ければ例外")]
    [InlineData("a.png", 9, (int)DiagramExportFormat.Png)]
    [InlineData("a.mermaid", 1, (int)DiagramExportFormat.Mermaid)]
    [InlineData("a.htm", 1, (int)DiagramExportFormat.Html)]
    [InlineData("a.json", 1, (int)DiagramExportFormat.SchemaJson)]
    [InlineData("a", 4, (int)DiagramExportFormat.SchemaJson)]
    [InlineData("a.xyz", 3, (int)DiagramExportFormat.Sql)]
    public void ResolveFormat_PrefersExtensionThenFilterIndex(
        string path,
        int filterIndex,
        // internal enum は public テストメソッドの引数型にできない（CS0051）ため int で受けてキャストする
        int expected
    )
    {
        DiagramExportService
            .ResolveFormat(path, filterIndex)
            .Should()
            .Be((DiagramExportFormat)expected);
    }

    [Fact(DisplayName = "形式解決: 未知の拡張子かつ範囲外フィルターは例外")]
    public void ResolveFormat_ThrowsWhenUndetermined()
    {
        var act = () => DiagramExportService.ResolveFormat("a.xyz", 99);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact(DisplayName = "欠落告知は同一形式でセッション 1 回だけ（2 回目は完了文のみ）")]
    public void OmissionDetails_AreShownOncePerFormat()
    {
        var (service, host, dialogs) = Create();
        host.Model = BuildModelWithOmission();
        var dir = Directory
            .CreateDirectory(
                Path.Combine(Path.GetTempPath(), "quicker-export-" + Guid.NewGuid().ToString("N"))
            )
            .FullName;

        try
        {
            service.SaveDiagram(DiagramExportFormat.Mermaid, Path.Combine(dir, "first.mmd"), null);
            service.SaveDiagram(DiagramExportFormat.Mermaid, Path.Combine(dir, "second.mmd"), null);

            // 初回は要約＋詳細（内訳付き）・2 回目は完了文のみ（形式ごとの告知済み記録が効く）
            dialogs.InformationDetailsMessages.Should().HaveCount(1);
            dialogs
                .InformationDetailsMessages[0]
                .Details.Should()
                .Contain(Strings.ExportOmission_ColumnNullability);
            dialogs.InformationMessages.Should().HaveCount(1);
            File.Exists(Path.Combine(dir, "second.mmd")).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact(DisplayName = "SVG は描画をホストへ委ね、完了をモーダルで通知する")]
    public void Svg_DelegatesRenderingToHost()
    {
        var (service, host, dialogs) = Create();

        service.SaveDiagram(DiagramExportFormat.Svg, @"X:\out\diagram.svg", null);

        host.SvgRenderPaths.Should().ContainSingle().Which.Should().Be(@"X:\out\diagram.svg");
        dialogs
            .InformationMessages.Should()
            .ContainSingle()
            .Which.Should()
            .Be(string.Format(Strings.Export_Completed, Strings.Format_Svg));
    }

    [Fact(DisplayName = "保存ダイアログのキャンセルは何も出力・通知しない")]
    public void Export_DoesNothingWhenDialogCancelled()
    {
        var (service, host, dialogs) = Create(saveResult: null);

        service.Export(visual: null);

        host.SvgRenderPaths.Should().BeEmpty();
        dialogs.InformationMessages.Should().BeEmpty();
        dialogs.ErrorMessages.Should().BeEmpty();
    }

    [Fact(DisplayName = "書き出しの失敗はエラーダイアログで報告する（完了通知は出さない）")]
    public void Export_ReportsFailureViaErrorDialog()
    {
        // 実在しないディレクトリへの SchemaJson 出力＝原子的書き込みが失敗する
        var missing = Path.Combine(
            Path.GetTempPath(),
            "quicker-export-missing-" + Guid.NewGuid().ToString("N"),
            "schema.json"
        );
        var (service, _, dialogs) = Create(
            saveResult: new QuickER.Gui.Abstractions.FileDialogResult(missing, 4)
        );

        service.Export(visual: null);

        dialogs
            .ErrorMessages.Should()
            .ContainSingle()
            .Which.Should()
            .StartWith(Strings.Export_Failed);
        dialogs.InformationMessages.Should().BeEmpty();
    }
}
