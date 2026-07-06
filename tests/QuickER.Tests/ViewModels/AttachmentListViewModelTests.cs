using System.IO;
using FluentAssertions;
using QuickER.AI;
using QuickER.AI.UI;

namespace QuickER.Tests.ViewModels;

/// <summary>
/// <see cref="AttachmentListViewModel"/> の追加/削除/クリア・上限超過／非対応種別の拒否通知・
/// <see cref="AttachmentSupport"/> ゲーティング・バックエンド切替時のクリアを検証するテストクラス。
/// </summary>
public class AttachmentListViewModelTests
{
    /// <summary>API キー(Claude) 相当の種別集合（画像＋PDF＋テキスト）</summary>
    private const AttachmentSupport ClaudeApiSupport =
        AttachmentSupport.Images | AttachmentSupport.Pdf | AttachmentSupport.Text;

    /// <summary>API キー(OpenAI) 相当の種別集合（画像＋テキスト・PDF/バイナリ非対応）</summary>
    private const AttachmentSupport OpenAiSupport =
        AttachmentSupport.Images | AttachmentSupport.Text;

    /// <summary>Claude Code 相当の全種別集合</summary>
    private const AttachmentSupport ClaudeCodeSupport =
        AttachmentSupport.Images
        | AttachmentSupport.Pdf
        | AttachmentSupport.Text
        | AttachmentSupport.Binary;

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
        AttachmentSupport support = ClaudeCodeSupport
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

    /// <summary>Support=OpenAI（画像＋テキスト）では（ファイル経由で読み込んだ）PDF を拒否し通知することを検証する</summary>
    [Fact(DisplayName = "PDF 非対応では PDF を拒否する")]
    public void SupportWithoutPdf_RejectsPdf()
    {
        var (vm, statuses) = CreateVm(OpenAiSupport);
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

    /// <summary>
    /// テキストファイルはテキスト対応接続では受理され、テキスト非対応接続（None）では
    /// 拒否されることを検証する（内容ベース分類でテキストと判定される）。
    /// </summary>
    [Fact(DisplayName = "テキストはテキスト対応で受理・非対応で拒否")]
    public void TextFile_GatedByTextSupport()
    {
        var textPath = Path.Combine(Path.GetTempPath(), $"QuickERTests_{Guid.NewGuid():N}.txt");
        File.WriteAllText(textPath, "これはテキスト添付のテストです。\nline2");

        try
        {
            var (openAi, _) = CreateVm(OpenAiSupport);
            openAi.AddFiles(new[] { textPath });
            openAi.Items.Should().ContainSingle();
            openAi.Items[0].Attachment.Kind.Should().Be(ChatAttachmentKind.Text);

            // 画像のみ（テキストなし）の接続ではテキストを拒否する
            var (imagesOnly, statuses) = CreateVm(AttachmentSupport.Images);
            imagesOnly.AddFiles(new[] { textPath });
            imagesOnly.Items.Should().BeEmpty();
            statuses.Should().Contain(s => s.Contains("テキスト"));
        }
        finally
        {
            File.Delete(textPath);
        }
    }

    /// <summary>
    /// バイナリファイルは Claude Code（バイナリ対応）でのみ受理され、
    /// API キー接続（バイナリ非対応）では明確なメッセージで拒否されることを検証する。
    /// </summary>
    [Fact(DisplayName = "バイナリは Claude Code のみ受理・API キーは拒否")]
    public void BinaryFile_GatedByBinarySupport()
    {
        var binPath = Path.Combine(Path.GetTempPath(), $"QuickERTests_{Guid.NewGuid():N}.bin");
        // NUL バイトを含むためバイナリと分類される
        File.WriteAllBytes(binPath, new byte[] { 0x00, 0x01, 0x02, 0xFF, 0x00, 0x10 });

        try
        {
            var (claudeCode, _) = CreateVm(ClaudeCodeSupport);
            claudeCode.AddFiles(new[] { binPath });
            claudeCode.Items.Should().ContainSingle();
            claudeCode.Items[0].Attachment.Kind.Should().Be(ChatAttachmentKind.Binary);

            var (apiKey, statuses) = CreateVm(ClaudeApiSupport);
            apiKey.AddFiles(new[] { binPath });
            apiKey.Items.Should().BeEmpty();
            statuses.Should().Contain(s => s.Contains("Claude 接続"));
        }
        finally
        {
            File.Delete(binPath);
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

    /// <summary>対応範囲を縮小（全種別→画像＋テキスト）すると、非対応になった PDF だけが除去されることを検証する</summary>
    [Fact(DisplayName = "対応縮小で非対応種別（PDF）を除外する")]
    public void ShrinkSupport_RemovesUnsupportedKeepsSupported()
    {
        var (vm, statuses) = CreateVm();
        AddImage(vm);
        var pdf = ChatAttachmentFactory.CreateFromBytes("spec.pdf", PdfBytes());
        vm.Items.Add(new PendingAttachmentItem(pdf.Attachment!));

        // PDF 非対応（画像＋テキスト）へ縮小する
        vm.Support = OpenAiSupport;

        vm.Items.Should().ContainSingle();
        vm.Items[0].Attachment.Kind.Should().Be(ChatAttachmentKind.Image);
        statuses.Should().Contain(s => s.Contains("除外しました"));
    }

    /// <summary>ファイルフィルタが全形式開放（すべてのファイル）＋補助フィルタを含むことを検証する</summary>
    [Fact(DisplayName = "ファイルフィルタは全形式開放＋補助フィルタ")]
    public void FileDialogFilter_OpensAllFormats()
    {
        var (vm, _) = CreateVm(OpenAiSupport);

        vm.FileDialogFilter.Should().Contain("すべてのファイル");
        vm.FileDialogFilter.Should().Contain("*.*");
        // 補助フィルタ（画像・PDF）も含む
        vm.FileDialogFilter.Should().Contain("*.png");
        vm.FileDialogFilter.Should().Contain("*.pdf");
    }
}
