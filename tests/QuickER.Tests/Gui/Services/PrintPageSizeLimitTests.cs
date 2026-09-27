using System.IO;
using System.Windows;
using AwesomeAssertions;
using QuickER.Resources;
using QuickER.Services;
using QuickER.Tests.Resources;
using QuickER.Tests.TestDoubles;

namespace QuickER.Tests.Gui.Services;

/// <summary>
/// 原寸大印刷の用紙サイズの上限（<see cref="DiagramPrintService.MaxPageSideDip"/>）を検証するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// 原寸大の用紙は図の実寸そのものなので、図が大きいほど用紙も際限なく大きくなる。
/// <c>PageMediaSize</c> はどんな値でも受け取ってしまう一方、PDF / XPS の用紙は 1 辺 200 インチが上限で、
/// 超えた用紙は出力側で必ず失敗するか化ける（実測: エンティティを 100,000 DIP 離した 2 個だけの図で
/// 1045 x 1044 インチ。自動整列なら 1000 テーブルでも 100 x 61 インチなので、掛かるのは手配置の図）。
/// </para>
/// <para>
/// 取りやめたときに印刷ジョブもプレビューも残らないよう、判断は<b>印刷ダイアログを出す前</b>に行う。
/// ここで検証するのはその判断（純粋な計算＋確認の呼び出し）で、印刷そのものは実プリンタが要るため対象外。
/// </para>
/// </remarks>
public class PrintPageSizeLimitTests
{
    /// <summary>上限ちょうど（200 インチ）の用紙サイズ</summary>
    private static readonly Size AtLimit = new(
        DiagramPrintService.MaxPageSideDip,
        DiagramPrintService.MaxPageSideDip
    );

    [Fact(DisplayName = "上限ちょうどの用紙は超過としない")]
    public void ExceedsPrintablePageSize_AtLimit_IsFalse()
    {
        DiagramPrintService.ExceedsPrintablePageSize(AtLimit).Should().BeFalse();
    }

    [Theory(DisplayName = "どちらかの辺が上限を超えたら超過とする")]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    public void ExceedsPrintablePageSize_OverLimit_IsTrue(double extraWidth, double extraHeight)
    {
        var size = new Size(AtLimit.Width + extraWidth, AtLimit.Height + extraHeight);

        DiagramPrintService.ExceedsPrintablePageSize(size).Should().BeTrue();
    }

    [Fact(DisplayName = "上限に収まる原寸大はそのまま原寸大で印刷する（確認も出さない）")]
    public void ResolveEffectiveSizeMode_WithinLimit_KeepsActualSize()
    {
        var dialogs = new StubDialogService();

        var mode = DiagramPrintService.ResolveEffectiveSizeMode(
            PrintSizeMode.ActualSize,
            new Size(2000, 1000),
            headerHeight: 20,
            dialogs
        );

        mode.Should().Be(PrintSizeMode.ActualSize);
        dialogs.WarningConfirmMessages.Should().BeEmpty();
    }

    [Fact(DisplayName = "縮小フィットは大きさに関係なく確認を出さない")]
    public void ResolveEffectiveSizeMode_FitToPage_IsUntouched()
    {
        var dialogs = new StubDialogService();

        var mode = DiagramPrintService.ResolveEffectiveSizeMode(
            PrintSizeMode.FitToPage,
            new Size(1_000_000, 1_000_000),
            headerHeight: 20,
            dialogs
        );

        mode.Should().Be(PrintSizeMode.FitToPage);
        dialogs.WarningConfirmMessages.Should().BeEmpty();
    }

    [Fact(DisplayName = "上限を超える原寸大は実寸を示して確認し、了承なら縮小フィットへ倒す")]
    public void ResolveEffectiveSizeMode_OverLimit_FallsBackToFitToPage()
    {
        var dialogs = new StubDialogService { ConfirmResult = true };

        var mode = DiagramPrintService.ResolveEffectiveSizeMode(
            PrintSizeMode.ActualSize,
            new Size(100_000, 100_000),
            headerHeight: 20,
            dialogs
        );

        mode.Should().Be(PrintSizeMode.FitToPage);
        dialogs
            .WarningConfirmMessages.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("1042", "実寸を示さないと、なぜ断られたのか分からない");
    }

    /// <summary>取りやめたときは印刷そのものへ進まないことを検証する</summary>
    /// <remarks>
    /// <c>null</c> は「印刷しない」を意味し、呼び出し側は印刷ダイアログを出す前に戻る
    /// （出したあとで取りやめると、選んだプリンタに印刷ジョブが残り得る）。
    /// </remarks>
    [Fact(DisplayName = "確認を取りやめたら印刷しない")]
    public void ResolveEffectiveSizeMode_Cancelled_ReturnsNull()
    {
        var dialogs = new StubDialogService { ConfirmResult = false };

        var mode = DiagramPrintService.ResolveEffectiveSizeMode(
            PrintSizeMode.ActualSize,
            new Size(100_000, 100_000),
            headerHeight: 20,
            dialogs
        );

        mode.Should().BeNull();
    }

    /// <summary>確認の文言が資源から引かれていることを検証する（英日の対で持つ）</summary>
    [Fact(DisplayName = "確認の文言は資源から引く")]
    public void Confirmation_UsesResourceString()
    {
        Strings.Print_ActualSizeTooLarge.Should().NotBeNullOrWhiteSpace();
        Strings.Print_ActualSizeTooLarge.Should().Contain("{0}").And.Contain("{2}");
    }

    /// <summary>判断が印刷ダイアログより前に置かれていることをソース上で固定する</summary>
    /// <remarks>
    /// 順序が逆だと、取りやめたときに選んだプリンタへ印刷ジョブが渡ったあとになる。
    /// <c>Print</c> は実プリンタを開くためテストから呼べないので、順序はソースで見張る。
    /// </remarks>
    [Fact(DisplayName = "大きさの判断は印刷ダイアログより前に行う")]
    public void Print_ChecksPageSizeBeforeShowingThePrintDialog()
    {
        var source = File.ReadAllText(
            Path.Combine(
                NeutralResxFiles.FindRepositoryRoot(),
                "src",
                "QuickER.Gui",
                "Services",
                "DiagramPrintService.cs"
            )
        );

        var check = source.IndexOf("ResolveEffectiveSizeMode(sizeMode", StringComparison.Ordinal);
        var dialog = source.IndexOf("new PrintDialog()", StringComparison.Ordinal);

        check.Should().BeGreaterThan(0);
        dialog.Should().BeGreaterThan(0);
        check.Should().BeLessThan(dialog, "取りやめたときに印刷ジョブを残さない");
    }
}
