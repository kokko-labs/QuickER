using System;
using ERDesigner.Services;
using FluentAssertions;

namespace ERDesigner.Tests.Services;

/// <summary>
/// <see cref="AiErrorMessageLocalizer"/> の代表的なケースを検証します。
/// (ClientResultException 系は実際の例外型を構築しづらいため一般例外パスのみ確認)
/// </summary>
public class AiErrorMessageLocalizerTests
{
    [Fact(DisplayName = "JsonException は JSON 解釈失敗メッセージになる")]
    public void Json_ReturnsParseError()
    {
        var msg = AiErrorMessageLocalizer.ToJapanese(new System.Text.Json.JsonException("bad"));
        msg.Should().Contain("JSON として解釈できませんでした");
    }

    [Fact(DisplayName = "TaskCanceledException はタイムアウトメッセージになる")]
    public void Cancel_ReturnsTimeoutMessage()
    {
        var msg = AiErrorMessageLocalizer.ToJapanese(new TaskCanceledException());
        msg.Should().Contain("タイムアウト").And.Contain("キャンセル");
    }

    [Fact(DisplayName = "HttpRequestException は接続エラーメッセージになる")]
    public void Http_ReturnsConnectionError()
    {
        var msg = AiErrorMessageLocalizer.ToJapanese(new System.Net.Http.HttpRequestException("conn"));
        msg.Should().Contain("接続できませんでした");
    }

    [Fact(DisplayName = "未知の例外は予期しないエラーメッセージになる")]
    public void Unknown_ReturnsGeneric()
    {
        var msg = AiErrorMessageLocalizer.ToJapanese(new InvalidOperationException("foo"));
        msg.Should().Contain("予期しないエラー").And.Contain("foo");
    }
}
