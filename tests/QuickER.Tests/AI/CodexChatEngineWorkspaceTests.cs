using System.IO;
using AwesomeAssertions;
using QuickER.AI;
using QuickER.AI.Chat;

namespace QuickER.Tests.AI;

/// <summary>
/// <see cref="CodexChatEngine"/> がスレッドへ与える作業領域（cwd・サンドボックス）を検証するテストクラス。
/// </summary>
/// <remarks>
/// 承認ポリシーが <c>never</c> のため、cwd をアプリの実行フォルダに向けたまま既定サンドボックスで
/// 走らせると、チャットが承認なしにその配下を読める。隔離した一時フォルダ＋読み取り専用であることを固定する。
/// </remarks>
public class CodexChatEngineWorkspaceTests
{
    private sealed class SyncUiDispatcher : IUiDispatcher
    {
        public T Invoke<T>(Func<T> func) => func();
    }

    private static CodexChatEngine CreateEngine() =>
        new(
            new FakeCodexAppServerClient(),
            new RecordingToolHost(),
            new SyncUiDispatcher(),
            ErDesignProfile.ErDesign
        );

    /// <summary>cwd がアプリのカレントではなく %TEMP%\QuickER\codex 配下の専用フォルダになることを検証する</summary>
    [Fact(DisplayName = "スレッドの cwd は一時フォルダへ隔離される")]
    public void BuildThreadStartOptions_UsesIsolatedTempWorkingDirectory()
    {
        var engine = CreateEngine();

        var options = engine.BuildThreadStartOptions();

        try
        {
            options.Cwd.Should().NotBeNullOrEmpty();
            options.Cwd.Should().NotBe(Environment.CurrentDirectory);

            var expectedRoot = Path.Combine(Path.GetTempPath(), "QuickER", "codex");
            options.Cwd!.Should().StartWith(expectedRoot);
            Directory.Exists(options.Cwd).Should().BeTrue();
        }
        finally
        {
            if (options.Cwd is { Length: > 0 } && Directory.Exists(options.Cwd))
            {
                Directory.Delete(options.Cwd, recursive: true);
            }
        }
    }

    /// <summary>サンドボックスを読み取り専用で明示すること（既定任せにしないこと）を検証する</summary>
    [Fact(DisplayName = "スレッドのサンドボックスは read-only を明示する")]
    public void BuildThreadStartOptions_DeclaresReadOnlySandbox()
    {
        var engine = CreateEngine();

        var options = engine.BuildThreadStartOptions();

        try
        {
            options.Sandbox.Should().Be("read-only");
            options.ApprovalPolicy.Should().Be("never");
        }
        finally
        {
            if (options.Cwd is { Length: > 0 } && Directory.Exists(options.Cwd))
            {
                Directory.Delete(options.Cwd, recursive: true);
            }
        }
    }

    /// <summary>同じエンジンでスレッドを開き直しても作業フォルダは 1 つだけであることを検証する</summary>
    [Fact(DisplayName = "作業フォルダはエンジンごとに 1 つだけ作る")]
    public void BuildThreadStartOptions_ReusesWorkingDirectory()
    {
        var engine = CreateEngine();

        var first = engine.BuildThreadStartOptions();
        var second = engine.BuildThreadStartOptions();

        try
        {
            second.Cwd.Should().Be(first.Cwd);
        }
        finally
        {
            if (first.Cwd is { Length: > 0 } && Directory.Exists(first.Cwd))
            {
                Directory.Delete(first.Cwd, recursive: true);
            }
        }
    }

    /// <summary>破棄で一時作業フォルダが片付くことを検証する</summary>
    [Fact(DisplayName = "破棄で一時作業フォルダを削除する")]
    public async Task DisposeAsync_DeletesWorkingDirectory()
    {
        var engine = CreateEngine();
        var workingDirectory = engine.BuildThreadStartOptions().Cwd!;

        Directory.Exists(workingDirectory).Should().BeTrue();

        await engine.DisposeAsync();

        Directory.Exists(workingDirectory).Should().BeFalse();
    }
}
