using System.Text.Json;
using AwesomeAssertions;
using QuickER.AI;
using AiStrings = QuickER.AI.Resources.Strings;

namespace QuickER.Tests.AI;

/// <summary>
/// <see cref="ClaudeCodeProcessClient"/> のターン結果判定（result イベントの受信有無・終了コード・
/// stderr 添付）と、失敗 result からの原因抽出（result 文字列 / errors 配列）を検証するテストクラス。
/// 実プロセスを起動しない純関数部分のみを対象にする。
/// </summary>
public class ClaudeCodeProcessClientOutcomeTests
{
    /// <summary>result 受信・成功・正常終了は成功（Error なし・セッション ID 保持）になることを検証する</summary>
    [Fact(DisplayName = "result 成功かつ終了コード 0 は成功を返す")]
    public void EvaluateTurnOutcome_ResultSuccessAndZeroExit_ReturnsSuccess()
    {
        var stream = new ClaudeCodeStreamResult("s1", true, true, null, false);

        var outcome = ClaudeCodeProcessClient.EvaluateTurnOutcome(stream, 0, []);

        outcome.Success.Should().BeTrue();
        outcome.Error.Should().BeNull();
        outcome.SessionId.Should().Be("s1");
        outcome.NotLoggedIn.Should().BeFalse();
    }

    /// <summary>result がエラーを報告した場合は、その文言と未ログインフラグをそのまま伝えることを検証する</summary>
    [Fact(DisplayName = "result のエラーは終了コードより優先して伝える")]
    public void EvaluateTurnOutcome_ResultError_KeepsReportedMessage()
    {
        var stream = new ClaudeCodeStreamResult("s1", true, false, "Not logged in", true);

        // 終了コードが非ゼロでも、CLI 自身が報告したメッセージのほうが情報量が多いため優先する
        var outcome = ClaudeCodeProcessClient.EvaluateTurnOutcome(stream, 1, ["noise"]);

        outcome.Success.Should().BeFalse();
        // stderr 補足は他分岐と対称に付く（CLI の報告文だけでは足りない詳細を捨てない）
        outcome.Error.Should().Be("Not logged in stderr: noise");
        outcome.NotLoggedIn.Should().BeTrue();
        outcome.SessionId.Should().Be("s1");
    }

    /// <summary>CLI が報告した失敗にも stderr の補足が付くこと（stderr が空なら報告文のままであること）を検証する</summary>
    [Fact(DisplayName = "result のエラーは stderr が空なら報告文のまま返す")]
    public void EvaluateTurnOutcome_ResultErrorWithoutStandardError_KeepsMessageOnly()
    {
        var stream = new ClaudeCodeStreamResult("s1", true, false, "boom", false);

        var outcome = ClaudeCodeProcessClient.EvaluateTurnOutcome(stream, 1, []);

        outcome.Success.Should().BeFalse();
        outcome.Error.Should().Be("boom");
    }

    /// <summary>
    /// result が文字列の result プロパティを持たず errors 配列だけを返す失敗（<c>--resume</c> の不在 ID など）で、
    /// 汎用文言ではなく具体的な原因がメッセージに載ることを検証する。
    /// </summary>
    [Fact(DisplayName = "errors 配列だけの失敗 result は原因をメッセージに載せる")]
    public void ParseResult_ErrorsArrayOnly_ReportsCause()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "type": "result",
              "subtype": "error_during_execution",
              "is_error": true,
              "errors": ["No conversation found with session ID: missing"]
            }
            """
        );

        var (success, error, notLoggedIn) = ClaudeCodeProcessClient.ParseResult(
            document.RootElement
        );

        success.Should().BeFalse();
        error.Should().Be("No conversation found with session ID: missing");
        error.Should().NotBe(AiStrings.ClaudeCode_GenericError);
        notLoggedIn.Should().BeFalse();
    }

    /// <summary>errors が複数ある場合は上限件数まで連結して伝えることを検証する</summary>
    [Fact(DisplayName = "複数の errors は上限まで連結して伝える")]
    public void ParseResult_MultipleErrors_JoinsUpToLimit()
    {
        using var document = JsonDocument.Parse(
            """
            {"is_error": true, "errors": ["first", "second", "third", "fourth"]}
            """
        );

        var (_, error, _) = ClaudeCodeProcessClient.ParseResult(document.RootElement);

        error.Should().Be("first | second | third");
    }

    /// <summary>文字列の result があるときはそれを優先すること（errors は使わないこと）を検証する</summary>
    [Fact(DisplayName = "文字列 result があれば errors より優先する")]
    public void ParseResult_ResultString_TakesPrecedenceOverErrors()
    {
        using var document = JsonDocument.Parse(
            """
            {"is_error": true, "result": "Not logged in", "errors": ["ignored"]}
            """
        );

        var (success, error, notLoggedIn) = ClaudeCodeProcessClient.ParseResult(
            document.RootElement
        );

        success.Should().BeFalse();
        error.Should().Be("Not logged in");
        notLoggedIn.Should().BeTrue();
    }

    /// <summary>原因を示す情報が何も無い失敗では汎用文言へ落ちることを検証する</summary>
    [Fact(DisplayName = "原因情報が無い失敗は汎用文言になる")]
    public void ParseResult_NoCause_FallsBackToGenericError()
    {
        using var document = JsonDocument.Parse("""{"is_error": true, "errors": []}""");

        var (success, error, _) = ClaudeCodeProcessClient.ParseResult(document.RootElement);

        success.Should().BeFalse();
        error.Should().Be(AiStrings.ClaudeCode_GenericError);
    }

    /// <summary>result 未受信（引数エラー等で stdout に何も出ないまま終了）は失敗として報告することを検証する</summary>
    [Fact(DisplayName = "result 未受信は失敗として stderr 付きで報告する")]
    public void EvaluateTurnOutcome_NoResultEvent_ReturnsFailureWithStandardError()
    {
        var stream = new ClaudeCodeStreamResult(null, false, false, null, false);

        var outcome = ClaudeCodeProcessClient.EvaluateTurnOutcome(
            stream,
            2,
            ["error: unknown option", "usage: claude"]
        );

        outcome.Success.Should().BeFalse();
        outcome.NotLoggedIn.Should().BeFalse();
        outcome
            .Error.Should()
            .Be(
                string.Format(AiStrings.ClaudeCode_NoResult, 2)
                    + " stderr: error: unknown option | usage: claude"
            );
    }

    /// <summary>result 未受信なら終了コード 0 でも失敗になること（偽の成功を返さないこと）を検証する</summary>
    [Fact(DisplayName = "result 未受信は終了コード 0 でも失敗にする")]
    public void EvaluateTurnOutcome_NoResultEventWithZeroExit_ReturnsFailure()
    {
        var stream = new ClaudeCodeStreamResult("s1", false, false, null, false);

        var outcome = ClaudeCodeProcessClient.EvaluateTurnOutcome(stream, 0, []);

        outcome.Success.Should().BeFalse();
        outcome.Error.Should().Be(string.Format(AiStrings.ClaudeCode_NoResult, 0));
        outcome.SessionId.Should().Be("s1");
    }

    /// <summary>result は成功でもプロセスが異常終了した場合は失敗として報告することを検証する</summary>
    [Fact(DisplayName = "result 成功でも終了コード非ゼロは失敗にする")]
    public void EvaluateTurnOutcome_ResultSuccessButNonZeroExit_ReturnsFailure()
    {
        var stream = new ClaudeCodeStreamResult("s1", true, true, null, false);

        var outcome = ClaudeCodeProcessClient.EvaluateTurnOutcome(stream, 1, ["boom"]);

        outcome.Success.Should().BeFalse();
        outcome
            .Error.Should()
            .Be(string.Format(AiStrings.ClaudeCode_ExitedWithError, 1) + " stderr: boom");
        outcome.SessionId.Should().Be("s1");
    }

    /// <summary>stderr が 1 行も無い場合は補足文を付けないことを検証する</summary>
    [Fact(DisplayName = "stderr が空なら補足文を付けない")]
    public void BuildStandardErrorSuffix_NoLines_ReturnsEmpty()
    {
        ClaudeCodeProcessClient.BuildStandardErrorSuffix([]).Should().BeEmpty();
    }

    /// <summary>stderr が空白行のみの場合は補足文を付けないことを検証する</summary>
    [Fact(DisplayName = "stderr が空白行のみなら補足文を付けない")]
    public void BuildStandardErrorSuffix_WhitespaceLines_ReturnsEmpty()
    {
        ClaudeCodeProcessClient.BuildStandardErrorSuffix(["", "   "]).Should().BeEmpty();
    }

    /// <summary>stderr の直近行を区切って 1 行の補足文にまとめることを検証する</summary>
    [Fact(DisplayName = "stderr の直近行を区切って連結する")]
    public void BuildStandardErrorSuffix_Lines_JoinsWithSeparator()
    {
        string[] lines = ["  first  ", "", "second"];

        ClaudeCodeProcessClient
            .BuildStandardErrorSuffix(lines)
            .Should()
            .Be(" stderr: first | second");
    }
}
