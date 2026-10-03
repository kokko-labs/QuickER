using System.Collections.Generic;
using System.IO;
using AwesomeAssertions;
using QuickER.AI;
using QuickER.AI.UI;

namespace QuickER.Tests.AI.UI;

/// <summary>
/// <see cref="BackendConnectionChecks"/> が、接続方式の変化に応じてその方式の確認だけを起動し、
/// 失敗を外へ漏らさず知らせ先へ渡すことを検証するテストクラス。
/// </summary>
public class BackendConnectionChecksTests
{
    /// <summary>設定ファイルへ触れない接続方式の状態部品を作る</summary>
    private static ChatConnectionSettingsViewModel CreateConnection() =>
        new(
            AiDialogKind.AiChat,
            settingsStore: new AiSettingsStore(
                Path.Combine(Path.GetTempPath(), "QuickERTests", Guid.NewGuid().ToString("N"))
            ),
            codexConfigReader: () => new CodexConfigToml(),
            apiKeyLoader: _ => string.Empty,
            apiKeySaver: (_, _) => { }
        );

    /// <summary>起動された確認の名前と、知らせ先へ渡された失敗を記録する組を作る</summary>
    private static (
        BackendConnectionChecks Checks,
        List<string> Started,
        List<Exception> Failures
    ) Create(ChatConnectionSettingsViewModel connection, Exception? failure = null)
    {
        var started = new List<string>();
        var failures = new List<Exception>();

        Func<Task> Check(string name) =>
            () =>
            {
                started.Add(name);
                return failure is null ? Task.CompletedTask : Task.FromException(failure);
            };

        var checks = new BackendConnectionChecks(
            connection,
            Check("codex"),
            Check("claude"),
            Check("copilot"),
            failures.Add
        );
        return (checks, started, failures);
    }

    /// <summary>接続方式を切り替えるたびに、その方式の確認だけを起動することを検証する</summary>
    [Fact(DisplayName = "切り替えた接続方式の確認だけを起動し、API キーでは何もしない")]
    public void StartsOnlyTheSelectedBackendsCheck()
    {
        var connection = CreateConnection();
        var (_, started, failures) = Create(connection);

        connection.SelectedBackend = ErChatBackendKind.Codex;
        connection.SelectedBackend = ErChatBackendKind.ClaudeCode;
        connection.SelectedBackend = ErChatBackendKind.Copilot;
        connection.SelectedBackend = ErChatBackendKind.ApiKey;

        started.Should().Equal("codex", "claude", "copilot");
        failures.Should().BeEmpty();
    }

    /// <summary>接続方式以外の変更では確認を起動しないことを検証する</summary>
    [Fact(DisplayName = "接続方式以外の変更では確認を起動しない")]
    public void OtherPropertyChanges_DoNotStartChecks()
    {
        var connection = CreateConnection();
        var (_, started, _) = Create(connection);

        connection.CodexModel = "gpt-test";
        connection.ClaudeCodeModel = "sonnet";

        started.Should().BeEmpty();
    }

    /// <summary>切替で起動した確認の失敗は、例外にせず知らせ先へ渡すことを検証する</summary>
    [Fact(DisplayName = "切替で起動した確認の失敗は知らせ先へ渡り外へ漏れない")]
    public void FailureOnSwitch_IsReported_NotThrown()
    {
        var connection = CreateConnection();
        var failure = new InvalidOperationException("probe failure");
        var (_, started, failures) = Create(connection, failure);

        var act = () => connection.SelectedBackend = ErChatBackendKind.Copilot;

        act.Should().NotThrow();
        started.Should().Equal("copilot");
        failures.Should().Equal(failure);
    }

    /// <summary>「再確認」で実行した確認の失敗も、例外にせず知らせ先へ渡すことを検証する</summary>
    [Fact(DisplayName = "再確認の失敗も知らせ先へ渡り外へ漏れない")]
    public async Task RunAsync_Failure_IsReported_NotThrown()
    {
        var connection = CreateConnection();
        var (checks, _, failures) = Create(connection);
        var failure = new InvalidOperationException("refresh failure");

        await checks.RunAsync(() => Task.FromException(failure));

        failures.Should().Equal(failure);
    }
}
