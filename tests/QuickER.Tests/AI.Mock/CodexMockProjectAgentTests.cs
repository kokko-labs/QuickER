using System.IO;
using AwesomeAssertions;
using QuickER.AI;
using QuickER.AI.Mock;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.AI.Mock;

/// <summary>
/// <see cref="CodexMockProjectAgent"/> に固有で、実行器の網（<c>MockAgentContractTests</c>）の行にしない部分を検証するテストクラス
/// （openai 以外のプロバイダーの認証の省略・PATH を走査する静的なロケーターとの一致）。
/// </summary>
public class CodexMockProjectAgentTests
{
    /// <summary>
    /// 非 openai プロバイダーは認証不要で先へ進む（未ログイン扱いにしない）ことを検証する。
    /// </summary>
    /// <remarks>
    /// 残す理由（P3-11b）：プロバイダーを選べるのは Codex だけで、網の行にすると Codex の組だけの行になる。
    /// 作業フォルダはテストごとの <see cref="TestTempFolder"/>（UI のファイルを置き、続行の促しを起こさない）。
    /// </remarks>
    [Fact(DisplayName = "非 openai プロバイダーは認証不要で進む")]
    public async Task RunAsync_NonOpenAiProvider_SkipsAuth()
    {
        using var folder = new TestTempFolder();
        var project = Path.Combine(folder.Path, "AcmeMock");
        Directory.CreateDirectory(project);
        File.WriteAllText(Path.Combine(project, "App.xaml"), "<Application/>");

        var client = new FakeCodexAppServerClient
        {
            // openai なら未ログイン扱いになる設定だが、ollama-launch では無視される
            NextAccountInfo = new CodexAccountInfo
            {
                RequiresOpenAiAuth = true,
                AuthMode = CodexAuthMode.None,
            },
        };
        var agent = new CodexMockProjectAgent(client);

        var task = agent.RunAsync(
            new MockProjectAgentRequest(
                WorkingDirectory: folder.Path,
                ProjectName: "AcmeMock",
                AdditionalInstructions: null,
                Model: "gpt-5-codex",
                Profile: MockProjectTargetProfile.Wpf,
                ModelProvider: "ollama-launch"
            ),
            _ => { },
            TestContext.Current.CancellationToken
        );
        client.RaiseTurnCompleted("completed");
        var outcome = await task;

        outcome.Success.Should().BeTrue();
        outcome.NotLoggedIn.Should().BeFalse();
        // 認証をスキップしてスレッドを開始している
        client.LastThreadStartOptions.Should().NotBeNull();
        client.LastThreadStartOptions!.ModelProvider.Should().Be("ollama-launch");
    }

    /// <summary>
    /// 可用性判定が共有ロケーター（<see cref="CodexCliLocator"/>）と同じ結果になることを検証する。
    /// モック側だけ独自走査を持つと、チャット側の未検出表示と食い違うため委譲を固定する。
    /// </summary>
    /// <remarks>残す理由（P3-11b）：静的なロケーター（PATH の走査）は差し替えられず、網の組から値を決められない</remarks>
    [Fact(DisplayName = "可用性判定は共有ロケーターと同じ結果になる")]
    public void IsAvailable_MatchesSharedLocator()
    {
        var agent = new CodexMockProjectAgent(new FakeCodexAppServerClient());

        agent.IsAvailable().Should().Be(CodexCliLocator.IsAvailable());
    }
}
