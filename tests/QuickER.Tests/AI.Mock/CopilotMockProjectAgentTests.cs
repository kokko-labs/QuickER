using System.IO;
using AwesomeAssertions;
using QuickER.AI;
using QuickER.AI.Mock;
using MockStrings = QuickER.AI.Mock.Resources.Strings;

namespace QuickER.Tests.AI.Mock;

/// <summary>
/// <see cref="CopilotMockProjectAgent"/> の Copilot 固有部（セッション生成オプション・組込みツール許可・
/// プロンプト本文の共有・進捗転送・ターン完了／エラーの写像・未ログイン判定・中断）を、フェイクの
/// Copilot ランタイムクライアントで検証するテストクラス。プロンプト本文が Claude Code / Codex 版と同一で
/// あることは、共有ヘルパ <see cref="MockProjectPromptBuilder"/> の出力と一致することで担保する。
/// </summary>
public class CopilotMockProjectAgentTests
{
    /// <summary>
    /// 既定の作業フォルダ（*.xaml を 1 つ含む）。既定の Request はここを指す＝UI 層が既に在る想定なので
    /// 自動続行ナッジは発火しない（happy path のテストは単一ターンで完結する）。
    /// </summary>
    private static readonly string DefaultWorkingDirectory = CreateFolderWithXaml();

    /// <summary>*.xaml を 1 つ含む一時フォルダを作って返す</summary>
    private static string CreateFolderWithXaml()
    {
        var folder = NewTempFolder();
        File.WriteAllText(Path.Combine(folder, "App.xaml"), "<Application/>");
        return folder;
    }

    /// <summary>一時作業フォルダを作る（*.xaml 有無で自動続行ナッジの判定を切り替えるため）</summary>
    private static string NewTempFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "QuickERTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static void Cleanup(string folder)
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static MockProjectAgentRequest Request(
        string? additionalInstructions = null,
        string model = "claude-sonnet-4.5",
        string? workingDirectory = null,
        MockProjectTargetProfile? profile = null
    ) =>
        new(
            WorkingDirectory: workingDirectory ?? DefaultWorkingDirectory,
            ProjectName: "AcmeMock",
            AdditionalInstructions: additionalInstructions,
            Model: model,
            Profile: profile ?? MockProjectTargetProfile.Wpf
        );

    /// <summary>
    /// セッション生成オプション（作業フォルダ＝出力先・組込みツール許可・ER ツール非公開・システムプロンプト共有）と、
    /// 初回プロンプトの共有・進捗転送を検証する。
    /// </summary>
    [Fact(DisplayName = "セッションは出力先を作業フォルダにし組込みツールを許可する")]
    public async Task RunAsync_SessionOptionsAndSharedPrompt()
    {
        var client = new FakeCopilotRuntimeClient();
        var agent = new CopilotMockProjectAgent(client);
        var progress = new List<string>();

        var task = agent.RunAsync(Request(), progress.Add, TestContext.Current.CancellationToken);
        // ターン完了（アイドル復帰）を待って完了させる（その前に差分通知＝進捗を流す）
        client.RaiseDelta("Starting work.\n");
        client.RaiseIdle();
        var outcome = await task;

        outcome.Success.Should().BeTrue();
        // アシスタント差分が進捗として転送される
        string.Concat(progress).Should().Contain("Starting work.");

        var options = client.LastSessionOptions!;
        options.WorkingDirectory.Should().Be(DefaultWorkingDirectory);
        options.AllowWorkspaceTools.Should().BeTrue();
        options.Model.Should().Be("claude-sonnet-4.5");
        // ER 設計ツールは公開せず、Copilot 組込みのファイル編集・シェル実行に任せる
        options.Tools.Should().BeEmpty();

        // システムプロンプト相当はセッションの追記指示として渡す（他バックエンドと同一本文＝共有ヘルパ由来）
        options
            .Instructions.Should()
            .Be(
                MockProjectPromptBuilder.BuildSystemPrompt(MockProjectTargetProfile.Wpf, "AcmeMock")
            );
        // 初回プロンプトも共有ヘルパ由来で他バックエンドと同一本文
        client
            .Sends[0]
            .Prompt.Should()
            .Be(
                MockProjectPromptBuilder.BuildPrompt(MockProjectTargetProfile.Wpf, "AcmeMock", null)
            );
        // 添付は付けない（ヘッドレス実行）
        client.Sends[0].AttachmentCount.Should().Be(0);

        // 1 実行ごとにクライアント（＝子プロセス）を破棄する
        client.Disposed.Should().BeTrue();
    }

    /// <summary>追加指示が非空ならプロンプト末尾へ見出し付きで連結されることを検証する（共有ヘルパと一致）</summary>
    [Fact(DisplayName = "追加指示はプロンプト末尾へ連結される")]
    public async Task RunAsync_AppendsAdditionalInstructions()
    {
        var client = new FakeCopilotRuntimeClient();
        var agent = new CopilotMockProjectAgent(client);

        var task = agent.RunAsync(
            Request("ダークテーマで実装して"),
            _ => { },
            TestContext.Current.CancellationToken
        );
        client.RaiseIdle();
        await task;

        client
            .Sends[0]
            .Prompt.Should()
            .Be(
                MockProjectPromptBuilder.BuildPrompt(
                    MockProjectTargetProfile.Wpf,
                    "AcmeMock",
                    "ダークテーマで実装して"
                )
            );
        client
            .Sends[0]
            .Prompt.Should()
            .Contain(MockProjectPromptBuilder.AdditionalInstructionsHeading);
    }

    /// <summary>組込みツールの実行開始・許可拒否が進捗テキストへ転送されることを検証する</summary>
    [Fact(DisplayName = "ツール実行と許可拒否が進捗へ流れる")]
    public async Task RunAsync_ForwardsToolActivityToProgress()
    {
        var client = new FakeCopilotRuntimeClient();
        var agent = new CopilotMockProjectAgent(client);
        var progress = new List<string>();

        var task = agent.RunAsync(Request(), progress.Add, TestContext.Current.CancellationToken);
        client.RaiseToolExecutionStarted("bash");
        client.RaisePermissionDeclined("url: https://example.com");
        client.RaiseIdle();
        await task;

        var log = string.Concat(progress);
        log.Should().Contain("bash");
        log.Should().Contain("url: https://example.com");
        log.Should().Contain("declined");
    }

    /// <summary>セッションエラーがターン失敗（エラーメッセージ付き）として写ることを検証する</summary>
    [Fact(DisplayName = "セッションエラーはターン失敗になる")]
    public async Task RunAsync_SessionError_FailsTurn()
    {
        var client = new FakeCopilotRuntimeClient();
        var agent = new CopilotMockProjectAgent(client);

        var task = agent.RunAsync(Request(), _ => { }, TestContext.Current.CancellationToken);
        client.RaiseError("boom");
        var outcome = await task;

        outcome.Success.Should().BeFalse();
        outcome.Error.Should().Be("boom");
        outcome.NotLoggedIn.Should().BeFalse();
    }

    /// <summary>未ログインのとき、セッションを張らず NotLoggedIn 失敗を返すことを検証する</summary>
    [Fact(DisplayName = "未ログインは NotLoggedIn 失敗を返す（セッション未生成）")]
    public async Task RunAsync_NotLoggedIn_ReturnsFailureWithoutSession()
    {
        var client = new FakeCopilotRuntimeClient
        {
            AuthInfo = new CopilotAuthInfo(false, string.Empty, string.Empty, string.Empty),
        };
        var agent = new CopilotMockProjectAgent(client);

        var outcome = await agent.RunAsync(
            Request(),
            _ => { },
            TestContext.Current.CancellationToken
        );

        outcome.Success.Should().BeFalse();
        outcome.NotLoggedIn.Should().BeTrue();
        outcome.Error.Should().Be(MockStrings.Mock_CopilotNotLoggedIn);
        // 認証で弾かれるためセッションは生成しない
        client.StartSessionCallCount.Should().Be(0);
    }

    /// <summary>中断（アイドル復帰の aborted）がキャンセルとして伝播することを検証する</summary>
    [Fact(DisplayName = "中断で AbortAsync を呼びキャンセルする")]
    public async Task InterruptAsync_CallsAbort()
    {
        var client = new FakeCopilotRuntimeClient();
        var agent = new CopilotMockProjectAgent(client);

        var task = agent.RunAsync(Request(), _ => { }, TestContext.Current.CancellationToken);

        await agent.InterruptAsync();
        client.AbortCallCount.Should().Be(1);

        // 中断されたアイドル復帰が届くとキャンセルとして伝播する
        client.RaiseIdle(aborted: true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task);
    }

    /// <summary>キャンセルトークンの発火で実行中ターンを中断し OCE を伝播することを検証する</summary>
    [Fact(DisplayName = "トークンキャンセルでターン中断・OCE 伝播")]
    public async Task RunAsync_TokenCancellation_AbortsAndThrows()
    {
        var client = new FakeCopilotRuntimeClient();
        var agent = new CopilotMockProjectAgent(client);
        using var cts = new CancellationTokenSource();

        var task = agent.RunAsync(Request(), _ => { }, cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task);
        client.AbortCallCount.Should().Be(1);
    }

    /// <summary>
    /// 成功で完了したのに UI 成果物が 1 つも無いとき（承認待ちで止まった疑い）、同一セッションへ 1 回だけ
    /// 続行ナッジを送り、その完了を成功として採用し、進捗へ検知の一行を流すことを検証する（Codex 版と同流儀）。
    /// </summary>
    [Fact(DisplayName = "xaml なしなら自動続行ナッジを 1 回送り成功する")]
    public async Task RunAsync_NoXaml_SendsSingleContinuationNudge()
    {
        var client = new FakeCopilotRuntimeClient { AutoIdleAfterSend = true };
        var agent = new CopilotMockProjectAgent(client);
        var progress = new List<string>();

        // WorkingDirectory は存在しない（＝xaml なし）ためナッジが発火する
        var outcome = await agent.RunAsync(
            Request(workingDirectory: @"C:\work\NoXamlHere"),
            progress.Add,
            TestContext.Current.CancellationToken
        );

        outcome.Success.Should().BeTrue();

        // ちょうど 2 ターン（初回＋ナッジ 1 回だけ）送られる（2 回目以降はナッジしない）
        client.Sends.Should().HaveCount(2);
        client
            .Sends[0]
            .Prompt.Should()
            .Be(
                MockProjectPromptBuilder.BuildPrompt(MockProjectTargetProfile.Wpf, "AcmeMock", null)
            );
        client.Sends[1].Prompt.Should().Be(MockProjectPromptBuilder.ContinuationNudge);

        // 承認待ち検知の一行が進捗へ流れる
        string.Concat(progress).Should().Contain(MockStrings.Mock_AutoContinueNotice.Trim());
    }

    /// <summary>作業フォルダに UI 成果物があれば自動続行ナッジを送らないことを検証する</summary>
    [Fact(DisplayName = "xaml があれば自動続行ナッジを送らない")]
    public async Task RunAsync_XamlPresent_DoesNotNudge()
    {
        var folder = CreateFolderWithXaml();
        var client = new FakeCopilotRuntimeClient { AutoIdleAfterSend = true };
        var agent = new CopilotMockProjectAgent(client);
        var progress = new List<string>();

        try
        {
            var outcome = await agent.RunAsync(
                Request(workingDirectory: folder),
                progress.Add,
                TestContext.Current.CancellationToken
            );

            outcome.Success.Should().BeTrue();
            client.Sends.Should().HaveCount(1);
            string.Concat(progress).Should().NotContain(MockStrings.Mock_AutoContinueNotice.Trim());
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>Blazor プロファイルでは共有プロンプトが Blazor 版になることを検証する</summary>
    [Fact(DisplayName = "Blazor はプロンプトが Blazor プロファイル版になる")]
    public async Task RunAsync_BlazorProfile_UsesBlazorPrompt()
    {
        var folder = NewTempFolder();
        File.WriteAllText(Path.Combine(folder, "Home.razor"), "@page \"/\"");

        var client = new FakeCopilotRuntimeClient { AutoIdleAfterSend = true };
        var agent = new CopilotMockProjectAgent(client);

        try
        {
            await agent.RunAsync(
                Request(workingDirectory: folder, profile: MockProjectTargetProfile.Blazor),
                _ => { },
                TestContext.Current.CancellationToken
            );

            client
                .LastSessionOptions!.Instructions.Should()
                .Be(
                    MockProjectPromptBuilder.BuildSystemPrompt(
                        MockProjectTargetProfile.Blazor,
                        "AcmeMock"
                    )
                );
            var combined = client.LastSessionOptions!.Instructions + "\n" + client.Sends[0].Prompt;
            combined.Should().Contain(".razor");
            combined.Should().Contain("InteractiveServer");
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>可用性判定がクライアント（＝共有ロケーター）へ委譲されることを検証する</summary>
    [Fact(DisplayName = "可用性判定はクライアントへ委譲される")]
    public void IsAvailable_DelegatesToClient()
    {
        var client = new FakeCopilotRuntimeClient { Available = false };

        new CopilotMockProjectAgent(client).IsAvailable().Should().BeFalse();

        client.Available = true;
        new CopilotMockProjectAgent(client).IsAvailable().Should().BeTrue();
    }
}
