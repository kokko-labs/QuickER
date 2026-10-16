using System.IO;
using System.Text;
using AwesomeAssertions;
using QuickER.AI;

namespace QuickER.Tests.AI;

/// <summary>
/// <see cref="ClaudeCodeProcessClient.RunTurnAsync"/>（ターンの実行）が、プロンプトを読み切る前に終わった claude を
/// 失敗の結果として返すことを、偽の claude（<c>claude.cmd</c>）を実プロセスとして起動して検証するテストクラス。
/// </summary>
[Collection(RealProcessTestCollection.Name)]
public class ClaudeCodeProcessClientTurnTests
{
    /// <summary>並列実行でも揺れない待ちの上限</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 標準入力を読まずに終わる claude へパイプのバッファに収まらないプロンプトを送ると書き込みが失敗するが、
    /// 生の例外で抜けずに、標準エラーの診断を添えた失敗の結果として返ることを検証する。
    /// </summary>
    [Fact(DisplayName = "プロンプトを読まずに終わった claude は標準エラーつきの失敗として返る")]
    public async Task RunTurn_WhenClaudeExitsBeforeReadingPrompt_ReturnsFailureWithStandardError()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "QuickER.Tests",
            "fake-claude-turn",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        var scriptPath = Path.Combine(directory, "claude.cmd");
        File.WriteAllLines(
            scriptPath,
            ["@echo off", "echo FAKE-CLAUDE-FAILED 1>&2", "exit /b 3"],
            Encoding.ASCII
        );
        var client = new ClaudeCodeProcessClient(
            () => scriptPath,
            ClaudeCodeProcessClient.DefaultProbeTimeout
        );

        try
        {
            var outcome = await client
                .RunTurnAsync(
                    new string('x', 4 * 1024 * 1024),
                    resumeSessionId: null,
                    new ClaudeCodeLaunchOptions(
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        directory
                    ),
                    _ => { },
                    TestContext.Current.CancellationToken
                )
                .WaitAsync(Patience, TestContext.Current.CancellationToken);

            outcome.Success.Should().BeFalse();
            outcome.Error.Should().Contain("FAKE-CLAUDE-FAILED");
        }
        finally
        {
            await client.DisposeAsync();

            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // 消せなくてもテストの結果には関係しない（一時フォルダ）
            }
        }
    }

    /// <summary>
    /// 取り消し以外の例外（ここでは本文の通知の失敗）でターンを抜けても、claude の子プロセスは止まり、残らないことを検証する。
    /// 偽の claude は本文の差分を 1 行出したあと、ファイルへ 1 秒ごとに追記し続ける（止まっていれば追記が止まる）
    /// </summary>
    [Fact(DisplayName = "取り消し以外の例外でターンを抜けても、claude の子プロセスを止める")]
    public async Task RunTurn_WhenCallbackThrows_KillsClaude()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "QuickER.Tests",
            "fake-claude-orphan",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        var alive = Path.Combine(directory, "alive.txt");
        var scriptPath = Path.Combine(directory, "claude.cmd");
        File.WriteAllLines(
            scriptPath,
            [
                "@echo off",
                """echo {"type":"stream_event","event":{"type":"content_block_delta","delta":{"type":"text_delta","text":"hi"}}}""",
                ":loop",
                """echo x>>"%~dp0alive.txt" """,
                "ping -n 2 127.0.0.1 >nul",
                "goto loop",
            ],
            Encoding.ASCII
        );
        var client = new ClaudeCodeProcessClient(
            () => scriptPath,
            ClaudeCodeProcessClient.DefaultProbeTimeout
        );

        try
        {
            var act = () =>
                client
                    .RunTurnAsync(
                        "prompt",
                        resumeSessionId: null,
                        new ClaudeCodeLaunchOptions(
                            string.Empty,
                            string.Empty,
                            string.Empty,
                            string.Empty,
                            directory
                        ),
                        _ => throw new InvalidOperationException("injected"),
                        TestContext.Current.CancellationToken
                    )
                    .WaitAsync(Patience, TestContext.Current.CancellationToken);

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("injected");

            // 止まっていれば、追記はこれ以上増えない（Kill はプロセスの木ごと＝cmd と ping の両方）
            await Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
            var before = File.Exists(alive) ? new FileInfo(alive).Length : 0;
            await Task.Delay(TimeSpan.FromSeconds(4), TestContext.Current.CancellationToken);
            var after = File.Exists(alive) ? new FileInfo(alive).Length : 0;

            after.Should().Be(before, "子プロセスは止まっていて、もう書き込まない");
        }
        finally
        {
            await client.DisposeAsync();

            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // 消せなくてもテストの結果には関係しない（一時フォルダ）
            }
        }
    }
}
