using System.Net.Http;
using System.Text.Json;
using FluentAssertions;
using QuickER.AI;
using QuickER.AI.Resources;
using QuickER.Services;

namespace QuickER.Tests.Services;

/// <summary><see cref="AiErrorMessageLocalizer"/> の例外種別ごとの表示用メッセージ変換を検証するテストクラス</summary>
/// <remarks>
/// ClientResultException 系は実例外を構築しづらいため、一般例外パスのみ確認する。
/// アサートは表示言語に依存しないよう resx（<see cref="Strings"/>）参照で行う。
/// </remarks>
public class AiErrorMessageLocalizerTests
{
    /// <summary>JsonException が JSON 解釈失敗メッセージへ変換されることを検証する</summary>
    [Fact(DisplayName = "JsonException は JSON 解釈失敗メッセージになる")]
    public void Json_ReturnsParseError()
    {
        var msg = AiErrorMessageLocalizer.ToUserMessage(new JsonException("bad"));
        msg.Should().Be(Strings.Ai_JsonParseError);
    }

    /// <summary>TaskCanceledException がタイムアウト・キャンセルメッセージへ変換されることを検証する</summary>
    [Fact(DisplayName = "TaskCanceledException はタイムアウトメッセージになる")]
    public void Cancel_ReturnsTimeoutMessage()
    {
        var msg = AiErrorMessageLocalizer.ToUserMessage(new TaskCanceledException());
        msg.Should().Be(Strings.Ai_Timeout);
    }

    /// <summary>HttpRequestException が接続エラーメッセージへ変換されることを検証する</summary>
    [Fact(DisplayName = "HttpRequestException は接続エラーメッセージになる")]
    public void Http_ReturnsConnectionError()
    {
        var msg = AiErrorMessageLocalizer.ToUserMessage(new HttpRequestException("conn"));
        msg.Should().Contain(Strings.Ai_ConnectionFailed).And.Contain("conn");
    }

    /// <summary>未知の例外が元メッセージを含む汎用エラー文へ変換されることを検証する</summary>
    [Fact(DisplayName = "未知の例外は予期しないエラーメッセージになる")]
    public void Unknown_ReturnsGeneric()
    {
        var msg = AiErrorMessageLocalizer.ToUserMessage(new InvalidOperationException("foo"));
        msg.Should().Be(Strings.Ai_Unexpected + "foo");
    }
}
