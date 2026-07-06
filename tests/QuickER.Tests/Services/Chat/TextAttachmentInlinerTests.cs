using System.Text;
using FluentAssertions;
using QuickER.AI;

namespace QuickER.Tests.Services.Chat;

/// <summary>
/// <see cref="TextAttachmentInliner"/> のインライン展開（見出し＋fenced block・複数連結・BOM 除去・
/// テキスト以外の無視・履歴再送での再現性）を検証するテストクラス。
/// </summary>
public class TextAttachmentInlinerTests
{
    /// <summary>テキスト添付を作る</summary>
    private static ChatAttachment Text(string name, string content) =>
        new(name, ChatAttachmentKind.Text, "text/plain", Encoding.UTF8.GetBytes(content));

    /// <summary>添付が無ければ本文をそのまま返すことを検証する</summary>
    [Fact(DisplayName = "添付なしは本文をそのまま返す")]
    public void BuildEffectiveText_NoAttachments_ReturnsOriginal()
    {
        TextAttachmentInliner.BuildEffectiveText("本文", null).Should().Be("本文");
        TextAttachmentInliner.BuildEffectiveText("本文", []).Should().Be("本文");
    }

    /// <summary>テキスト添付が「見出し＋fenced block」で本文末尾へ連結されることを検証する</summary>
    [Fact(DisplayName = "テキストは見出し＋fenced block で連結される")]
    public void BuildEffectiveText_Text_AppendsFencedBlock()
    {
        var result = TextAttachmentInliner.BuildEffectiveText(
            "これを見て",
            new[] { Text("a.txt", "hello") }
        );

        result.Should().Be("これを見て\n\n【添付ファイル: a.txt】\n```\nhello\n```");
    }

    /// <summary>複数テキストは順に連結されることを検証する</summary>
    [Fact(DisplayName = "複数テキストは順に連結される")]
    public void BuildEffectiveText_MultipleTexts_AppendsInOrder()
    {
        var result = TextAttachmentInliner.BuildEffectiveText(
            "本文",
            new[] { Text("1.txt", "one"), Text("2.txt", "two") }
        );

        result.Should().Contain("【添付ファイル: 1.txt】");
        result.Should().Contain("one");
        result.Should().Contain("【添付ファイル: 2.txt】");
        result.Should().Contain("two");
        result.IndexOf("1.txt").Should().BeLessThan(result.IndexOf("2.txt"));
    }

    /// <summary>画像・PDF・バイナリ添付はインライン対象外（無視される）ことを検証する</summary>
    [Fact(DisplayName = "テキスト以外の種別は無視される")]
    public void BuildEffectiveText_NonText_Ignored()
    {
        var attachments = new[]
        {
            new ChatAttachment("i.png", ChatAttachmentKind.Image, "image/png", [0x89]),
            new ChatAttachment("d.pdf", ChatAttachmentKind.Pdf, "application/pdf", [0x25]),
            new ChatAttachment(
                "b.bin",
                ChatAttachmentKind.Binary,
                "application/octet-stream",
                [0x00]
            ),
        };

        TextAttachmentInliner.BuildEffectiveText("本文", attachments).Should().Be("本文");
    }

    /// <summary>UTF-8 BOM 付きテキストは BOM を除去して展開されることを検証する</summary>
    [Fact(DisplayName = "BOM 付きは BOM を除去して展開する")]
    public void BuildEffectiveText_BomText_StripsBom()
    {
        byte[] bom = [0xEF, 0xBB, 0xBF];
        var data = bom.Concat(Encoding.UTF8.GetBytes("body")).ToArray();
        var attachment = new ChatAttachment("b.txt", ChatAttachmentKind.Text, "text/plain", data);

        var result = TextAttachmentInliner.BuildEffectiveText("x", new[] { attachment });

        // BOM 文字（U+FEFF）が本文に混入しないこと
        result.Should().Contain("```\nbody\n```");
        result.Should().NotContain("﻿");
    }

    /// <summary>同じ本文・添付なら常に同じ結果になり、履歴再送でも再現されることを検証する</summary>
    [Fact(DisplayName = "同一入力は同一結果（履歴再送で再現）")]
    public void BuildEffectiveText_IsDeterministic()
    {
        var attachments = new[] { Text("a.txt", "content") };

        var first = TextAttachmentInliner.BuildEffectiveText("本文", attachments);
        var second = TextAttachmentInliner.BuildEffectiveText("本文", attachments);

        second.Should().Be(first);
    }
}
