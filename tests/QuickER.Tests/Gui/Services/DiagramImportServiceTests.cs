using System;
using System.IO;
using AwesomeAssertions;
using QuickER.Gui.Abstractions;
using QuickER.Resources;
using QuickER.Services;
using QuickER.Tests.TestDoubles;

namespace QuickER.Tests.Gui.Services;

/// <summary>
/// <see cref="DiagramImportService"/> の単体検証（スタブホスト＋スタブダイアログ＝MainViewModel を組み立てない）。
/// </summary>
/// <remarks>
/// VM 経由の統合検証（MainViewModelMergeImportTests 等＝Excel マージ経路を含む）は委譲の配線を固定し、
/// ここではサービス単体の判断ロジック（形式解決・置換確認・キャンセルで置換しない）を軽量に固定する。
/// </remarks>
public class DiagramImportServiceTests
{
    /// <summary>1 テーブルだけの最小 DBML を一時ファイルへ書き出す（呼び出し側がフォルダごと削除する）</summary>
    private static (string Dir, string Path) WriteMinimalDbml()
    {
        var dir = Directory
            .CreateDirectory(
                System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    "quicker-import-" + Guid.NewGuid().ToString("N")
                )
            )
            .FullName;
        var path = System.IO.Path.Combine(dir, "diagram.dbml");
        File.WriteAllLines(path, ["Table Customer {", "  CustomerId int [pk, not null]", "}"]);
        return (dir, path);
    }

    /// <summary>サービスと観測用スタブ一式を組み立てる</summary>
    private static (
        DiagramImportService Service,
        StubDiagramTransferHost Host,
        StubDialogService Dialogs
    ) Create(FileDialogResult? openResult)
    {
        var host = new StubDiagramTransferHost();
        var dialogs = new StubDialogService();
        var files = new StubFileDialogService { OpenResult = openResult };
        return (new DiagramImportService(host, dialogs, files), host, dialogs);
    }

    [Theory(DisplayName = "形式解決: 拡張子が最優先・無ければフィルター順・どちらも無ければ例外")]
    [InlineData("a.mmd", 3, (int)DiagramImportFormat.Mermaid)]
    [InlineData("a.dbml", 1, (int)DiagramImportFormat.Dbml)]
    [InlineData("a.xlsx", 1, (int)DiagramImportFormat.Excel)]
    [InlineData("a", 3, (int)DiagramImportFormat.Excel)]
    public void ResolveFormat_PrefersExtensionThenFilterIndex(
        string path,
        int filterIndex,
        // internal enum は public テストメソッドの引数型にできない（CS0051）ため int で受けてキャストする
        int expected
    )
    {
        DiagramImportService
            .ResolveFormat(path, filterIndex)
            .Should()
            .Be((DiagramImportFormat)expected);
    }

    [Fact(DisplayName = "形式解決: 未知の拡張子かつ範囲外フィルターは例外")]
    public void ResolveFormat_ThrowsWhenUndetermined()
    {
        var act = () => DiagramImportService.ResolveFormat("a.xyz", 99);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact(DisplayName = "失うものが無ければ DBML を無確認で丸ごと置換し、完了をモーダルで通知する")]
    public void Import_ReplacesWholesaleWithoutConfirm_WhenNothingToLose()
    {
        var (dir, path) = WriteMinimalDbml();

        try
        {
            var (service, host, dialogs) = Create(new FileDialogResult(path, 2));
            host.HasNothingToLose = true;

            service.Import();

            var replaced = host.WholesaleReplacements.Should().ContainSingle().Subject;
            replaced.Entities.Should().ContainSingle().Which.TableName.Should().Be("Customer");
            host.MergedReplacements.Should().BeEmpty("DBML はマージでなく丸ごと置換");
            dialogs.ConfirmMessages.Should().BeEmpty();
            dialogs.WarningConfirmMessages.Should().BeEmpty();
            dialogs
                .InformationMessages.Should()
                .ContainSingle()
                .Which.Should()
                .Be(string.Format(Strings.Import_Completed, Strings.Format_Dbml));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact(
        DisplayName = "ダーティ＋クエリありの置換は警告確認（クエリ削除件数付き）で、拒否なら置換しない"
    )]
    public void Import_DoesNotReplace_WhenWarningConfirmRejected()
    {
        var (dir, path) = WriteMinimalDbml();

        try
        {
            var (service, host, dialogs) = Create(new FileDialogResult(path, 2));
            host.HasNothingToLose = false;
            host.IsDirty = true;
            host.QueryCount = 2;
            dialogs.ConfirmResult = false;

            service.Import();

            // ダーティ＝警告水準の確認（ConfirmDiscard）で、クエリ全削除の告知を含む
            dialogs
                .WarningConfirmMessages.Should()
                .ContainSingle()
                .Which.Should()
                .Contain(string.Format(Strings.Import_QueriesRemovedWarning, 2));
            host.WholesaleReplacements.Should().BeEmpty("拒否したら置換しない");
            dialogs.InformationMessages.Should().BeEmpty("置換していないのに完了を出さない");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact(DisplayName = "警告確認を承諾すれば置換される（確認水準はダーティ時 Warning）")]
    public void Import_Replaces_WhenWarningConfirmAccepted()
    {
        var (dir, path) = WriteMinimalDbml();

        try
        {
            var (service, host, dialogs) = Create(new FileDialogResult(path, 2));
            host.HasNothingToLose = false;
            host.IsDirty = true;
            host.QueryCount = 1;
            dialogs.ConfirmResult = true;

            service.Import();

            dialogs.WarningConfirmMessages.Should().HaveCount(1);
            host.WholesaleReplacements.Should().HaveCount(1);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact(DisplayName = "ファイル選択のキャンセルは何も取り込まない")]
    public void Import_DoesNothingWhenDialogCancelled()
    {
        var (service, host, dialogs) = Create(openResult: null);

        service.Import();

        host.WholesaleReplacements.Should().BeEmpty();
        host.MergedReplacements.Should().BeEmpty();
        dialogs.InformationMessages.Should().BeEmpty();
        dialogs.ErrorMessages.Should().BeEmpty();
    }

    [Fact(DisplayName = "取込の失敗はエラーダイアログで報告する（存在しないファイル）")]
    public void Import_ReportsFailureViaErrorDialog()
    {
        var missing = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "quicker-import-missing-" + Guid.NewGuid().ToString("N") + ".dbml"
        );
        var (service, host, dialogs) = Create(new FileDialogResult(missing, 2));

        service.Import();

        dialogs
            .ErrorMessages.Should()
            .ContainSingle()
            .Which.Should()
            .StartWith(Strings.Import_Failed);
        host.WholesaleReplacements.Should().BeEmpty();
    }
}
