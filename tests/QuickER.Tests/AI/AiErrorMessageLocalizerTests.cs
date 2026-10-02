using System.ClientModel;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Anthropic.Exceptions;
using AwesomeAssertions;
using QuickER.AI;
using QuickER.AI.Resources;
using QuickER.Services;

namespace QuickER.Tests.AI;

/// <summary><see cref="AiErrorMessageLocalizer"/> の例外種別ごとの表示用メッセージ変換を検証するテストクラス</summary>
/// <remarks>
/// ClientResultException 系は応答付きの実例外を構築しづらいため、応答なしのパスのみ確認する
/// （ステータス別の分岐は Anthropic SDK の例外で確かめる＝両 SDK が同じ分岐を共有する）。
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

    /// <summary>Anthropic の本文（<c>{"type":"error","error":{…}}</c>）を作る</summary>
    private static string AnthropicErrorBody(string type, string message) =>
        $$$"""{"type":"error","error":{"type":"{{{type}}}","message":"{{{message}}}"}}""";

    /// <summary>Anthropic SDK の 401 が「予期しないエラー」でなく、OpenAI と同じステータス別の文言になることを検証する</summary>
    [Fact(DisplayName = "Anthropic の 401 はステータス別の文言になる")]
    public void Anthropic401_ReturnsStatusMessage()
    {
        var ex = new AnthropicUnauthorizedException(new HttpRequestException("inner"))
        {
            StatusCode = HttpStatusCode.Unauthorized,
            ResponseBody = AnthropicErrorBody("authentication_error", "invalid x-api-key"),
        };

        var msg = AiErrorMessageLocalizer.ToUserMessage(ex);

        msg.Should().Be("HTTP 401" + Strings.Ai_Http_401);
    }

    /// <summary>Anthropic SDK の 5xx は、本文の error.message だけを詳細として添えることを検証する（生の JSON を出さない）</summary>
    [Fact(DisplayName = "Anthropic の 5xx は本文のメッセージを詳細に添える")]
    public void Anthropic5xx_AppendsBodyMessage()
    {
        var ex = new Anthropic5xxException(new HttpRequestException("inner"))
        {
            StatusCode = HttpStatusCode.ServiceUnavailable,
            ResponseBody = AnthropicErrorBody("overloaded_error", "Overloaded"),
        };

        var msg = AiErrorMessageLocalizer.ToUserMessage(ex);

        msg.Should()
            .Be(
                "HTTP 503"
                    + Strings.Ai_Http_ServerError
                    + Environment.NewLine
                    + Strings.Ai_DetailLabel
                    + "Overloaded"
            );
    }

    /// <summary>ステータス別の文言が無い Anthropic の 4xx は、本文の error.message をそのまま添えることを検証する</summary>
    [Fact(DisplayName = "Anthropic の 400 は本文のメッセージを添える")]
    public void Anthropic400_AppendsBodyMessage()
    {
        var ex = new AnthropicBadRequestException(new HttpRequestException("inner"))
        {
            StatusCode = HttpStatusCode.BadRequest,
            ResponseBody = AnthropicErrorBody("invalid_request_error", "bad image"),
        };

        AiErrorMessageLocalizer.ToUserMessage(ex).Should().Be("HTTP 400: bad image");
    }

    /// <summary>Anthropic SDK の接続失敗は接続エラーの文言になることを検証する</summary>
    [Fact(DisplayName = "Anthropic の接続失敗は接続エラーメッセージになる")]
    public void AnthropicIo_ReturnsConnectionError()
    {
        var ex = new AnthropicIOException("no route", new HttpRequestException("inner"));

        AiErrorMessageLocalizer
            .ToUserMessage(ex)
            .Should()
            .Contain(Strings.Ai_ConnectionFailed)
            .And.Contain("no route");
    }

    /// <summary>応答本文の無い OpenAI SDK の例外は、ステータス別分岐の汎用枝で例外メッセージを添えることを検証する</summary>
    [Fact(DisplayName = "応答の無い OpenAI の例外は例外メッセージを添える")]
    public void OpenAiWithoutResponse_AppendsExceptionMessage()
    {
        var msg = AiErrorMessageLocalizer.ToUserMessage(new ClientResultException("boom"));

        msg.Should().Be("HTTP 0: boom");
    }
}
