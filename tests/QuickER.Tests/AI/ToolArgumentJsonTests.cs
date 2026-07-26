using System.Linq;
using System.Text.Json;
using FluentAssertions;
using QuickER.AI;

namespace QuickER.Tests.AI;

/// <summary>
/// <see cref="ToolArgumentJson"/>（ツール引数 JSON の緩和修復・履歴サニタイズ）と、
/// <see cref="OpenAiTurnDriver.ToAssistantMessage"/> の履歴サニタイズ適用を検証するテストクラス。
/// </summary>
/// <remarks>
/// ローカル LLM が壊れた引数 JSON を出したとき、(1) 直せる壊れ方はツール実行前に修復され、
/// (2) 修復不能でも履歴再送では空オブジェクトへ置換されて会話が死なない（履歴を検証する
/// Ollama 互換層の HTTP 400 恒久化を防ぐ）ことが主眼。
/// </remarks>
public class ToolArgumentJsonTests
{
    // ── NormalizeForExecution / SanitizeForHistory の基本 ──

    /// <summary>有効な JSON はどちらの経路でも原文のまま返ることを検証する</summary>
    [Fact(DisplayName = "有効な JSON は原文のまま")]
    public void ValidJson_PassesThroughUnchanged()
    {
        var json = "{\"name\":\"Product\",\"type\":\"int\"}";

        ToolArgumentJson.NormalizeForExecution(json).Should().BeSameAs(json);
        ToolArgumentJson.SanitizeForHistory(json).Should().BeSameAs(json);
    }

    /// <summary>空・空白の扱い（実行＝素通し・履歴＝空オブジェクト）を検証する</summary>
    [Fact(DisplayName = "空・空白は実行=素通し・履歴={}")]
    public void EmptyOrWhitespace_Handled()
    {
        ToolArgumentJson.NormalizeForExecution("").Should().Be("");
        ToolArgumentJson.NormalizeForExecution(null).Should().Be("");
        ToolArgumentJson.SanitizeForHistory("").Should().Be("{}");
        ToolArgumentJson.SanitizeForHistory("   ").Should().Be("{}");
        ToolArgumentJson.SanitizeForHistory(null).Should().Be("{}");
    }

    /// <summary>修復不能なゴミ（実行＝原文のままツールホストへ・履歴＝{}）を検証する</summary>
    [Fact(DisplayName = "修復不能は実行=原文・履歴={}")]
    public void Unrepairable_ExecutionKeepsRaw_HistoryBecomesEmptyObject()
    {
        var garbage = "not json at all";

        ToolArgumentJson.TryRepair(garbage).Should().BeNull();
        // 実行側は原文のまま＝ツールホストが解析エラーを返し、モデルにリトライさせる既存経路を維持
        ToolArgumentJson.NormalizeForExecution(garbage).Should().Be(garbage);
        // 履歴側は {} へ置換＝壊れた引数の再送で以後の要求が拒否される事故を防ぐ
        ToolArgumentJson.SanitizeForHistory(garbage).Should().Be("{}");
    }

    // ── TryRepair の修復パターン ──

    /// <summary>末尾カンマが緩和パースで修復され、厳密 JSON へ再直列化されることを検証する</summary>
    [Fact(DisplayName = "末尾カンマを修復する")]
    public void TryRepair_TrailingComma()
    {
        var repaired = ToolArgumentJson.TryRepair("{\"name\":\"Product\",\"pk\":true,}");

        repaired.Should().NotBeNull();

        using var doc = JsonDocument.Parse(repaired!);
        doc.RootElement.GetProperty("name").GetString().Should().Be("Product");
        doc.RootElement.GetProperty("pk").GetBoolean().Should().BeTrue();
    }

    /// <summary>マークダウンのコードフェンスを剝がして修復することを検証する</summary>
    [Fact(DisplayName = "コードフェンスを剝がす")]
    public void TryRepair_CodeFence()
    {
        var repaired = ToolArgumentJson.TryRepair("```json\n{\"table\":\"Stock\"}\n```");

        repaired.Should().NotBeNull();

        using var doc = JsonDocument.Parse(repaired!);
        doc.RootElement.GetProperty("table").GetString().Should().Be("Stock");
    }

    /// <summary>文字列リテラル中の生改行・タブがエスケープされ、値が往復することを検証する</summary>
    [Fact(DisplayName = "文字列中の生改行・タブをエスケープする")]
    public void TryRepair_RawControlCharsInString()
    {
        // モデルが複数行テキストを引数へ入れるときにエスケープし忘れる、最も多い壊れ方
        var broken = "{\"description\":\"1 行目\n2 行目\tタブ\"}";

        var repaired = ToolArgumentJson.TryRepair(broken);

        repaired.Should().NotBeNull();

        using var doc = JsonDocument.Parse(repaired!);
        doc.RootElement.GetProperty("description").GetString().Should().Be("1 行目\n2 行目\tタブ");
    }

    /// <summary>エスケープ済みのシーケンスは二重エスケープされないことを検証する</summary>
    [Fact(DisplayName = "エスケープ済み文字列は変更しない")]
    public void TryRepair_DoesNotDoubleEscape()
    {
        // 有効な JSON は修復対象にならず原文のまま（NormalizeForExecution 経由で確認）
        var valid = "{\"html\":\"line1\\nline2 \\\"quoted\\\"\"}";

        ToolArgumentJson.NormalizeForExecution(valid).Should().BeSameAs(valid);
    }

    /// <summary>前後に散文が付いた JSON を {..} 抽出で修復することを検証する</summary>
    [Fact(DisplayName = "前後の散文を落として抽出する")]
    public void TryRepair_SurroundingProse()
    {
        var repaired = ToolArgumentJson.TryRepair(
            "以下の引数で呼び出します: {\"name\":\"StockMovement\"} よろしくお願いします"
        );

        repaired.Should().NotBeNull();

        using var doc = JsonDocument.Parse(repaired!);
        doc.RootElement.GetProperty("name").GetString().Should().Be("StockMovement");
    }

    // ── OpenAiTurnDriver の履歴サニタイズ適用 ──

    /// <summary>壊れた引数のツール呼び出しが、履歴メッセージでは空オブジェクトに置換されることを検証する</summary>
    [Fact(DisplayName = "履歴のアシスタントメッセージで壊れた引数が {} になる")]
    public void ToAssistantMessage_SanitizesBrokenArguments()
    {
        var item = new ChatHistoryItem(
            ChatHistoryRole.Assistant,
            "カラムを追加します。",
            ToolCalls:
            [
                new ChatToolCallRequest("call_1", "add_column", "{\"broken\": "),
                new ChatToolCallRequest("call_2", "add_column", "{\"name\":\"Quantity\"}"),
            ]
        );

        var message = OpenAiTurnDriver.ToAssistantMessage(item);

        var arguments = message.ToolCalls.Select(tc => tc.FunctionArguments.ToString()).ToList();

        // 壊れた引数は {} へ・有効な引数は原文のまま（履歴を検証するプロバイダーの 400 恒久化を防ぐ）
        arguments.Should().Equal("{}", "{\"name\":\"Quantity\"}");
    }
}
