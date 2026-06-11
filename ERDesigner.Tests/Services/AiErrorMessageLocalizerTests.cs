using System.Net.Http;
using System.Text.Json;
using ERDesigner.Services;
using FluentAssertions;

namespace ERDesigner.Tests.Services;

/// <summary><see cref="AiErrorMessageLocalizer"/> の例外種別ごとの日本語化を検証するテストクラス</summary>
/// <remarks>ClientResultException 系は実例外を構築しづらいため、一般例外パスのみ確認する</remarks>
public class AiErrorMessageLocalizerTests
{
    /// <summary>JsonException が JSON 解釈失敗メッセージへ変換されることを検証する</summary>
    [Fact(DisplayName = "JsonException は JSON 解釈失敗メッセージになる")]
    public void Json_ReturnsParseError()
    {
        var msg = AiErrorMessageLocalizer.ToJapanese(new JsonException("bad"));
        msg.Should().Contain("JSON として解釈できませんでした");
    }

    /// <summary>TaskCanceledException がタイムアウト・キャンセルメッセージへ変換されることを検証する</summary>
    [Fact(DisplayName = "TaskCanceledException はタイムアウトメッセージになる")]
    public void Cancel_ReturnsTimeoutMessage()
    {
        var msg = AiErrorMessageLocalizer.ToJapanese(new TaskCanceledException());
        msg.Should().Contain("タイムアウト").And.Contain("キャンセル");
    }

    /// <summary>HttpRequestException が接続エラーメッセージへ変換されることを検証する</summary>
    [Fact(DisplayName = "HttpRequestException は接続エラーメッセージになる")]
    public void Http_ReturnsConnectionError()
    {
        var msg = AiErrorMessageLocalizer.ToJapanese(new HttpRequestException("conn"));
        msg.Should().Contain("接続できませんでした");
    }

    /// <summary>未知の例外が元メッセージを含む汎用エラー文へ変換されることを検証する</summary>
    [Fact(DisplayName = "未知の例外は予期しないエラーメッセージになる")]
    public void Unknown_ReturnsGeneric()
    {
        var msg = AiErrorMessageLocalizer.ToJapanese(new InvalidOperationException("foo"));
        msg.Should().Contain("予期しないエラー").And.Contain("foo");
    }
}
