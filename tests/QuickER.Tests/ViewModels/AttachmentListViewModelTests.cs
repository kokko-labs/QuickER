using System.IO;
using FluentAssertions;
using QuickER.AI;
using QuickER.ViewModels;

namespace QuickER.Tests.ViewModels;

/// <summary>
/// <see cref="AttachmentListViewModel"/> の追加/削除/クリア・上限超過／非対応種別の拒否通知・
/// <see cref="AttachmentSupport"/> ゲーティング・バックエンド切替時のクリアを検証するテストクラス。
/// </summary>
public class AttachmentListViewModelTests
{
    /// <summary>PNG シグネチャ＋任意末尾でバイト列を作る</summary>
    private static byte[] PngBytes(int totalLength = 16)
    {
        var data = new byte[Math.Max(totalLength, 8)];
        byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        Array.Copy(signature, data, signature.Length);
        return data;
    }

    /// <summary>PDF シグネチャ＋任意末尾でバイト列を作る</summary>
    private static byte[] PdfBytes(int totalLength = 16)
    {
        var data = new byte[Math.Max(totalLength, 5)];
        var signature = "%PDF-"u8.ToArray();
        Array.Copy(signature, data, signature.Length);
        return data;
    }

    /// <summary>ステータス通知を記録するリストと VM を生成する</summary>
    private static (AttachmentListViewModel vm, List<string> statuses) CreateVm(
        AttachmentSupport support = AttachmentSupport.ImagesAndPdf
    )
    {
        var statuses = new List<string>();
        var vm = new AttachmentListViewModel(statuses.Add);
        vm.Support = support;
        return (vm, statuses);
    }

    /// <summary>クリップボード画像相当（PNG バイト列）を追加する</summary>
    private static void AddImage(AttachmentListViewModel vm, byte[]? data = null) =>
        vm.AddClipboardImage(data ?? PngBytes(), new DateTime(2026, 1, 2, 3, 4, 5));

    /// <summary>ImagesAndPdf では PNG 画像を追加でき、送信用リスト・要約に反映されることを検証する</summary>
    [Fact(DisplayName = "対応時に画像を追加できる")]
    public void AddImage_WhenSupported_AddsItem()
    {
        var (vm, _) = CreateVm();

        AddImage(vm);

        vm.Items.Should().HaveCount(1);
        vm.HasAttachments.Should().BeTrue();
        vm.BuildAttachments().Should().HaveCount(1);
        vm.BuildSummary().Should().Contain("📎").And.Contain(".png");
    }

    /// <summary>削除・クリアで送信待ちが空になることを検証する</summary>
    [Fact(DisplayName = "削除・クリアで空になる")]
    public void RemoveAndClear_EmptiesItems()
    {
        var (vm, _) = CreateVm();
        AddImage(vm);
        var item = vm.Items[0];

        vm.Remove(item);
        vm.Items.Should().BeEmpty();

        AddImage(vm);
        vm.Clear();
        vm.Items.Should().BeEmpty();
        vm.HasAttachments.Should().BeFalse();
    }

    /// <summary>Support=None では追加不可・ボタン無効・理由が示されることを検証する</summary>
    [Fact(DisplayName = "None では添付不可・理由あり")]
    public void SupportNone_DisablesAndRejects()
    {
        var (vm, statuses) = CreateVm(AttachmentSupport.None);

        vm.IsEnabled.Should().BeFalse();
        vm.DisabledReason.Should().NotBeEmpty();

        AddImage(vm);

        vm.Items.Should().BeEmpty();
        statuses.Should().ContainSingle().Which.Should().Contain("対応していません");
    }

    /// <summary>Support=Images では（ファイル経由で読み込んだ）PDF を拒否し通知することを検証する</summary>
    [Fact(DisplayName = "Images では PDF を拒否する")]
    public void SupportImages_RejectsPdf()
    {
        var (vm, statuses) = CreateVm(AttachmentSupport.Images);
        vm.AllowsPdf.Should().BeFalse();

        var pdfPath = Path.Combine(Path.GetTempPath(), $"QuickERTests_{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(pdfPath, PdfBytes());

        try
        {
            vm.AddFiles(new[] { pdfPath });

            vm.Items.Should().BeEmpty();
            statuses.Should().Contain(s => s.Contains("PDF"));
        }
        finally
        {
            File.Delete(pdfPath);
        }
    }

    /// <summary>画像枚数の上限（5 枚）を超えると拒否し通知することを検証する</summary>
    [Fact(DisplayName = "画像は 5 枚を超えると拒否する")]
    public void ImageCount_ExceedingLimit_Rejected()
    {
        var (vm, statuses) = CreateVm();

        for (var i = 0; i < ChatAttachmentLimits.MaxImagesPerMessage; i++)
        {
            AddImage(vm);
        }

        vm.Items.Should().HaveCount(ChatAttachmentLimits.MaxImagesPerMessage);

        AddImage(vm); // 6 枚目

        vm.Items.Should().HaveCount(ChatAttachmentLimits.MaxImagesPerMessage);
        statuses.Should().Contain(s => s.Contains("最大"));
    }

    /// <summary>ターン実行中は追加・削除ができないことを検証する</summary>
    [Fact(DisplayName = "ターン中は添付を変更できない")]
    public void TurnInProgress_BlocksEditing()
    {
        var (vm, statuses) = CreateVm();
        AddImage(vm);
        var item = vm.Items[0];

        vm.IsTurnInProgress = true;
        vm.IsEnabled.Should().BeFalse();

        AddImage(vm);
        vm.Items.Should().HaveCount(1); // 追加されない
        vm.Remove(item);
        vm.Items.Should().HaveCount(1); // 削除もされない
    }

    /// <summary>Support=None へ切り替えると送信待ちがクリアされ通知されることを検証する</summary>
    [Fact(DisplayName = "None へ切替で Pending をクリアする")]
    public void SwitchToNone_ClearsPending()
    {
        var (vm, statuses) = CreateVm();
        AddImage(vm);
        vm.Items.Should().HaveCount(1);

        vm.Support = AttachmentSupport.None;

        vm.Items.Should().BeEmpty();
        statuses.Should().Contain(s => s.Contains("クリアしました"));
    }

    /// <summary>ImagesAndPdf→Images へ切り替えると PDF だけが除去されることを検証する</summary>
    [Fact(DisplayName = "Images へ切替で PDF を除外する")]
    public void SwitchToImages_RemovesPdfKeepsImages()
    {
        var (vm, statuses) = CreateVm();
        AddImage(vm);
        var pdf = ChatAttachmentFactory.CreateFromBytes("spec.pdf", PdfBytes());
        vm.Items.Add(new PendingAttachmentItem(pdf.Attachment!));

        vm.Support = AttachmentSupport.Images;

        vm.Items.Should().ContainSingle();
        vm.Items[0].Attachment.Kind.Should().Be(ChatAttachmentKind.Image);
        statuses.Should().Contain(s => s.Contains("PDF"));
    }

    /// <summary>ファイルフィルタが対応範囲に応じて画像のみ／画像＋PDF になることを検証する</summary>
    [Fact(DisplayName = "ファイルフィルタは対応範囲に追従する")]
    public void FileDialogFilter_TracksSupport()
    {
        var (vm, _) = CreateVm(AttachmentSupport.Images);
        vm.FileDialogFilter.Should().NotContain("pdf");

        vm.Support = AttachmentSupport.ImagesAndPdf;
        vm.FileDialogFilter.Should().Contain("pdf");
    }
}
