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

    /// <summary>
    /// 資格情報の失効（ヘッドレス実行で出る <c>Failed to authenticate: ...</c>）を再認証要と判定することを検証する。
    /// </summary>
    /// <remarks>
    /// 判定を <c>Not logged in</c> だけに絞るとこのテストが赤くなる。この文言を取りこぼすと状態表示は
    /// 「判定不能＝しばらく待って再確認」へ落ちるが、資格情報の失効は待っても直らず <c>/login</c> が要る。
    /// </remarks>
    [Fact(DisplayName = "資格情報の失効は再認証要と判定する")]
    public void ParseResult_ExpiredCredentials_FlagsNotLoggedIn()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "is_error": true,
              "result": "Failed to authenticate: OAuth session expired and could not be refreshed"
            }
            """
        );

        var (success, error, notLoggedIn) = ClaudeCodeProcessClient.ParseResult(
            document.RootElement
        );

        success.Should().BeFalse();
        // 原因の文言は捨てない（対処は /login でも、状況の照合には報告文が要る）
        error
            .Should()
            .Be("Failed to authenticate: OAuth session expired and could not be refreshed");
        notLoggedIn.Should().BeTrue();
    }

    /// <summary>claude CLI が実際に出す認証エラー文言をいずれも再認証要と判定することを検証する</summary>
    [Theory(DisplayName = "CLI の認証エラー文言は再認証要と判定する")]
    [InlineData("Not logged in · Please run /login")]
    [InlineData("Failed to authenticate: OAuth session expired and could not be refreshed")]
    [InlineData("Failed to authenticate. Anthropic API: 401")]
    [InlineData("API Error: 401 Invalid API key · Please run /login")]
    [InlineData("Please run /login · Anthropic API: 401")]
    public void IndicatesLoginRequired_AuthenticationFailures_ReturnsTrue(string message)
    {
        ClaudeCodeProcessClient.IndicatesLoginRequired(message).Should().BeTrue();
    }

    /// <summary>
    /// 認証以外の失敗を再認証要と誤判定しないことを検証する（誤判定は「/login し直せ」の誤誘導になる）。
    /// </summary>
    /// <remarks>
    /// MCP サーバー側の OAuth 失敗を含めるのは、<c>OAuth</c> / <c>expired</c> 単体をマーカーに加えると
    /// claude 本体のログインとは無関係な失敗まで拾ってしまうため（マーカー選定の根拠を固定する）。
    /// </remarks>
    [Theory(DisplayName = "認証以外の失敗は再認証要と判定しない")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("No conversation found with session ID: missing")]
    [InlineData("Credit balance is too low")]
    [InlineData("MCP server erdesigner: OAuth authorization expired")]
    public void IndicatesLoginRequired_OtherFailures_ReturnsFalse(string? message)
    {
        ClaudeCodeProcessClient.IndicatesLoginRequired(message).Should().BeFalse();
    }

    /// <summary>ログインプローブが成功応答をログイン済みと解釈することを検証する</summary>
    [Fact(DisplayName = "プローブの成功応答はログイン済み")]
    public void InterpretProbe_Success_ReturnsLoggedIn()
    {
        ClaudeCodeProcessClient
            .InterpretProbe("""{"is_error": false, "result": "pong"}""")
            .Should()
            .Be(ClaudeLoginProbeResult.LoggedIn);
    }

    /// <summary>
    /// ログインプローブが資格情報の失効を「未ログイン」（＝/login の案内）として扱うことを検証する。
    /// </summary>
    [Fact(DisplayName = "プローブの資格情報失効は未ログイン")]
    public void InterpretProbe_ExpiredCredentials_ReturnsNotLoggedIn()
    {
        ClaudeCodeProcessClient
            .InterpretProbe(
                """
                {"is_error": true, "result": "Failed to authenticate: OAuth session expired and could not be refreshed"}
                """
            )
            .Should()
            .Be(ClaudeLoginProbeResult.NotLoggedIn);
    }

    /// <summary>
    /// プローブが errors 配列だけの失敗でも実ターンと同じ規則で判定すること
    /// （<see cref="ClaudeCodeProcessClient.ParseResult"/> への委譲）を検証する。
    /// </summary>
    [Fact(DisplayName = "プローブは errors 配列だけの失敗も実ターンと同じ規則で判定する")]
    public void InterpretProbe_ErrorsArrayOnly_UsesSameRuleAsTurn()
    {
        ClaudeCodeProcessClient
            .InterpretProbe("""{"is_error": true, "errors": ["Not logged in"]}""")
            .Should()
            .Be(ClaudeLoginProbeResult.NotLoggedIn);
    }

    /// <summary>認証と無関係な失敗・解析不能な応答は判定不能になることを検証する</summary>
    [Theory(DisplayName = "認証以外の失敗と解析不能な応答は判定不能")]
    [InlineData("""{"is_error": true, "result": "Credit balance is too low"}""")]
    [InlineData("not json")]
    [InlineData("")]
    public void InterpretProbe_NonAuthFailures_ReturnUnavailable(string output)
    {
        ClaudeCodeProcessClient
            .InterpretProbe(output)
            .Should()
            .Be(ClaudeLoginProbeResult.Unavailable);
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
