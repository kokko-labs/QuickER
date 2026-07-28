using System.IO;
using FluentAssertions;
using QuickER.AI;
using QuickER.AI.Mock;
using MockStrings = QuickER.AI.Mock.Resources.Strings;

namespace QuickER.Tests.AI.Mock;

/// <summary>
/// <see cref="CodexMockProjectAgent"/> の Codex 固有部（スレッド開始オプション・dynamicTools 不使用・
/// プロンプト本文の共有・ターン完了→結果の写像・未ログイン判定・中断）を、フェイクの Codex App Server クライアントで
/// 検証するテストクラス。プロンプト本文が Claude Code 版と同一であることは、共有ヘルパ
/// <see cref="MockProjectPromptBuilder"/> の出力と一致することで担保する。
/// </summary>
public class CodexMockProjectAgentTests
{
    /// <summary>
    /// 既定の作業フォルダ（*.xaml を 1 つ含む）。既定の Request はここを指す＝UI 層が既に在る想定なので
    /// 自動続行ナッジは発火しない（happy path のテストは単一ターンで完結する）。ナッジ検証のテストは
    /// 明示的に「xaml の無いフォルダ」を渡して発火させる。
    /// </summary>
    private static readonly string DefaultWorkingDirectory = CreateFolderWithXaml();

    /// <summary>*.xaml を 1 つ含む一時フォルダを作って返す</summary>
    private static string CreateFolderWithXaml()
    {
        var folder = NewTempFolder();
        File.WriteAllText(Path.Combine(folder, "App.xaml"), "<Application/>");
        return folder;
    }

    /// <summary>*.razor を 1 つ含む一時フォルダを作って返す（Blazor の UI 成果物あり判定用）</summary>
    private static string CreateFolderWithRazor()
    {
        var folder = NewTempFolder();
        File.WriteAllText(Path.Combine(folder, "Home.razor"), "@page \"/\"");
        return folder;
    }

    private static MockProjectAgentRequest Request(
        string? additionalInstructions = null,
        string model = "gpt-5-codex",
        string modelProvider = "openai",
        string? workingDirectory = null,
        MockProjectTargetProfile? profile = null
    ) =>
        new(
            WorkingDirectory: workingDirectory ?? DefaultWorkingDirectory,
            ProjectName: "AcmeMock",
            AdditionalInstructions: additionalInstructions,
            Model: model,
            Profile: profile ?? MockProjectTargetProfile.Wpf,
            ModelProvider: modelProvider
        );

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

    /// <summary>スレッド開始オプション（cwd/approval=never/sandbox=workspace-write・dynamicTools なし）と、プロンプト本文の共有・進捗転送を検証する</summary>
    [Fact(DisplayName = "スレッド開始オプション・dynamicTools なし・プロンプト共有・進捗転送")]
    public async Task RunAsync_ThreadOptionsAndSharedPrompt()
    {
        var client = new FakeCodexAppServerClient();
        var agent = new CodexMockProjectAgent(client);
        var progress = new List<string>();

        var task = agent.RunAsync(Request(), progress.Add, TestContext.Current.CancellationToken);
        // ターン完了通知を待って完了させる（その前に差分通知＝進捗を流す）
        client.RaiseAgentMessageDelta("作業を開始します。\n");
        client.RaiseTurnCompleted("completed");
        var outcome = await task;

        outcome.Success.Should().BeTrue();
        // アシスタント差分が進捗として転送される
        string.Concat(progress).Should().Contain("作業を開始します");

        var options = client.LastThreadStartOptions!;
        options.Cwd.Should().Be(DefaultWorkingDirectory);
        options.ApprovalPolicy.Should().Be("never");
        options.Sandbox.Should().Be("workspace-write");
        options.Model.Should().Be("gpt-5-codex");
        options.ModelProvider.Should().Be("openai");
        // Codex ネイティブのファイル編集・コマンド実行に任せる（dynamicTools は登録しない）
        options.DynamicTools.Should().BeNull();

        // システムプロンプト相当は developer instructions として渡す（Claude Code 版と同一本文＝共有ヘルパ由来）
        options
            .DeveloperInstructions.Should()
            .Be(
                MockProjectPromptBuilder.BuildSystemPrompt(MockProjectTargetProfile.Wpf, "AcmeMock")
            );
        // 初回プロンプトも共有ヘルパ由来で Claude Code 版と同一本文
        client
            .LastTurnPrompt.Should()
            .Be(
                MockProjectPromptBuilder.BuildPrompt(MockProjectTargetProfile.Wpf, "AcmeMock", null)
            );

        // 非対話・確認禁止の明示（ヘッドレスで承認待ちに陥らないための保険）が入る＝英語正本
        options.DeveloperInstructions.Should().Contain("non-interactive, headless run");
        options.DeveloperInstructions.Should().Contain("wait for approval");
        client.LastTurnPrompt.Should().Contain("Do not ask for confirmation or approval");
    }

    /// <summary>追加指示が非空ならプロンプト末尾へ見出し付きで連結されることを検証する（共有ヘルパと一致）</summary>
    [Fact(DisplayName = "追加指示はプロンプト末尾へ連結される")]
    public async Task RunAsync_AppendsAdditionalInstructions()
    {
        var client = new FakeCodexAppServerClient();
        var agent = new CodexMockProjectAgent(client);

        var task = agent.RunAsync(
            Request("ダークテーマで実装して"),
            _ => { },
            TestContext.Current.CancellationToken
        );
        client.RaiseTurnCompleted("completed");
        await task;

        var heading = MockProjectPromptBuilder.AdditionalInstructionsHeading;
        client.LastTurnPrompt.Should().Contain(heading);
        client.LastTurnPrompt.Should().Contain("ダークテーマで実装して");
        client
            .LastTurnPrompt.Should()
            .Be(
                MockProjectPromptBuilder.BuildPrompt(
                    MockProjectTargetProfile.Wpf,
                    "AcmeMock",
                    "ダークテーマで実装して"
                )
            );
    }

    /// <summary>ターン完了の成否（completed / failed）が結果へ写ることを検証する</summary>
    [Theory(DisplayName = "ターン完了の成否が結果へ写る")]
    [InlineData("completed", true)]
    [InlineData("failed", false)]
    public async Task RunAsync_MapsTurnStatus(string status, bool expectedSuccess)
    {
        var client = new FakeCodexAppServerClient();
        var agent = new CodexMockProjectAgent(client);

        var task = agent.RunAsync(Request(), _ => { }, TestContext.Current.CancellationToken);
        client.RaiseTurnCompleted(status, status == "failed" ? "boom" : null);
        var outcome = await task;

        outcome.Success.Should().Be(expectedSuccess);

        if (!expectedSuccess)
        {
            outcome.Error.Should().Be("boom");
        }
    }

    /// <summary>openai プロバイダーで認証が必要かつ未ログインのとき、スレッドを開始せず NotLoggedIn を返すことを検証する</summary>
    [Fact(DisplayName = "未ログインは NotLoggedIn 失敗を返す（スレッド未開始）")]
    public async Task RunAsync_NotLoggedIn_ReturnsFailureWithoutThread()
    {
        var client = new FakeCodexAppServerClient
        {
            NextAccountInfo = new CodexAccountInfo
            {
                RequiresOpenAiAuth = true,
                AuthMode = CodexAuthMode.None,
            },
        };
        var agent = new CodexMockProjectAgent(client);

        // openai プロバイダーなので認証が必要。未ログインのため即失敗する（完了通知を待たない）
        var outcome = await agent.RunAsync(
            Request(),
            _ => { },
            TestContext.Current.CancellationToken
        );

        outcome.Success.Should().BeFalse();
        outcome.NotLoggedIn.Should().BeTrue();
        // 認証で弾かれるためスレッドは開始しない
        client.LastThreadStartOptions.Should().BeNull();
    }

    /// <summary>非 openai プロバイダーは認証不要で先へ進む（未ログイン扱いにしない）ことを検証する</summary>
    [Fact(DisplayName = "非 openai プロバイダーは認証不要で進む")]
    public async Task RunAsync_NonOpenAiProvider_SkipsAuth()
    {
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
            Request(modelProvider: "ollama-launch"),
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

    /// <summary>中断で InterruptTurnAsync が呼ばれ、実行がキャンセルとして終了することを検証する</summary>
    [Fact(DisplayName = "中断で InterruptTurnAsync を呼びキャンセルする")]
    public async Task InterruptAsync_CallsInterruptTurn()
    {
        var client = new FakeCodexAppServerClient();
        var agent = new CodexMockProjectAgent(client);

        var task = agent.RunAsync(Request(), _ => { }, TestContext.Current.CancellationToken);

        await agent.InterruptAsync();

        client.InterruptTurnCount.Should().Be(1);
        client.LastInterruptThreadId.Should().Be("thr_test");
        client.LastInterruptTurnId.Should().Be("turn_test");

        // 中断ステータスのターン完了が届くとキャンセルとして伝播する
        client.RaiseTurnCompleted("interrupted");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task);
    }

    /// <summary>キャンセルトークンの発火で実行中ターンを中断し OCE を伝播することを検証する</summary>
    [Fact(DisplayName = "トークンキャンセルでターン中断・OCE 伝播")]
    public async Task RunAsync_TokenCancellation_InterruptsAndThrows()
    {
        var client = new FakeCodexAppServerClient();
        var agent = new CodexMockProjectAgent(client);
        using var cts = new CancellationTokenSource();

        var task = agent.RunAsync(Request(), _ => { }, cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task);
        client.InterruptTurnCount.Should().Be(1);
    }

    /// <summary>
    /// 成功で完了したのに *.xaml が 1 つも無いとき（承認待ちで止まった疑い）、同一スレッドへ 1 回だけ
    /// 続行ナッジを送り、その完了を成功として採用し、進捗へ検知の一行を流すことを検証する。
    /// </summary>
    [Fact(DisplayName = "xaml なしなら自動続行ナッジを 1 回送り成功する")]
    public async Task RunAsync_NoXaml_SendsSingleContinuationNudge()
    {
        var client = new FakeCodexAppServerClient();
        // 1 ターン目・ナッジターンともに completed を自動発火させる
        client.AutoTurnCompletions.Enqueue(("completed", null));
        client.AutoTurnCompletions.Enqueue(("completed", null));
        var agent = new CodexMockProjectAgent(client);
        var progress = new List<string>();

        // WorkingDirectory は存在しない（＝xaml なし）ためナッジが発火する
        var outcome = await agent.RunAsync(
            Request(workingDirectory: @"C:\work\NoXamlHere"),
            progress.Add,
            TestContext.Current.CancellationToken
        );

        outcome.Success.Should().BeTrue();

        // ちょうど 2 ターン（初回＋ナッジ 1 回だけ）送られる（2 回目以降はナッジしない）
        client.StartTurnCount.Should().Be(2);
        client
            .TurnPrompts[0]
            .Should()
            .Be(
                MockProjectPromptBuilder.BuildPrompt(MockProjectTargetProfile.Wpf, "AcmeMock", null)
            );
        client.TurnPrompts[1].Should().Be(MockProjectPromptBuilder.CodexContinuationNudge);

        // 承認待ち検知の一行が進捗へ流れる
        string.Concat(progress).Should().Contain(MockStrings.Mock_Codex_AutoContinueNotice.Trim());
    }

    /// <summary>作業フォルダに *.xaml があれば（実装が進んだ兆候）自動続行ナッジを送らないことを検証する</summary>
    [Fact(DisplayName = "xaml があれば自動続行ナッジを送らない")]
    public async Task RunAsync_XamlPresent_DoesNotNudge()
    {
        var folder = NewTempFolder();
        File.WriteAllText(Path.Combine(folder, "App.xaml"), "<Application/>");

        var client = new FakeCodexAppServerClient();
        client.AutoTurnCompletions.Enqueue(("completed", null));
        var agent = new CodexMockProjectAgent(client);
        var progress = new List<string>();

        try
        {
            var outcome = await agent.RunAsync(
                Request(workingDirectory: folder),
                progress.Add,
                TestContext.Current.CancellationToken
            );

            outcome.Success.Should().BeTrue();
            // xaml があるためナッジせず 1 ターンのみ
            client.StartTurnCount.Should().Be(1);
            string.Concat(progress)
                .Should()
                .NotContain(MockStrings.Mock_Codex_AutoContinueNotice.Trim());
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>Blazor プロファイルでは developer instructions・初回プロンプトに Blazor 固有規約と共有規約が入ることを検証する</summary>
    [Fact(DisplayName = "Blazor はプロンプトに Blazor 規約と共有規約を含む")]
    public async Task RunAsync_BlazorProfile_PromptFragments()
    {
        var folder = CreateFolderWithRazor();
        var client = new FakeCodexAppServerClient();
        var agent = new CodexMockProjectAgent(client);

        try
        {
            var task = agent.RunAsync(
                Request(workingDirectory: folder, profile: MockProjectTargetProfile.Blazor),
                _ => { },
                TestContext.Current.CancellationToken
            );
            client.RaiseTurnCompleted("completed");
            await task;

            // developer instructions（system 相当）＋初回プロンプトを併せて Blazor 固有の語彙を確認する
            var combined =
                client.LastThreadStartOptions!.DeveloperInstructions + "\n" + client.LastTurnPrompt;
            combined.Should().Contain(".razor");
            combined.Should().Contain("@page");
            combined.Should().Contain("InteractiveServer");
            combined.Should().Contain("style.css");

            // 共有規約（Blazor でも変わらず入る）
            combined.Should().Contain("assign the primary key in the application");
            combined.Should().Contain("NuGet.Config");

            // 共有ヘルパ由来で Blazor プロファイル版と一致する
            client
                .LastThreadStartOptions!.DeveloperInstructions.Should()
                .Be(
                    MockProjectPromptBuilder.BuildSystemPrompt(
                        MockProjectTargetProfile.Blazor,
                        "AcmeMock"
                    )
                );
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>Blazor ターゲットで *.razor が無いとき、自動続行ナッジを 1 回だけ送ることを検証する（xaml テストと対称）</summary>
    [Fact(DisplayName = "Blazor・razor なしなら自動続行ナッジを 1 回送る")]
    public async Task RunAsync_Blazor_NoRazor_SendsSingleContinuationNudge()
    {
        var client = new FakeCodexAppServerClient();
        client.AutoTurnCompletions.Enqueue(("completed", null));
        client.AutoTurnCompletions.Enqueue(("completed", null));
        var agent = new CodexMockProjectAgent(client);
        var progress = new List<string>();

        // 存在しない（＝razor なし）作業フォルダでナッジを発火させる
        var outcome = await agent.RunAsync(
            Request(
                workingDirectory: @"C:\work\NoRazorHere",
                profile: MockProjectTargetProfile.Blazor
            ),
            progress.Add,
            TestContext.Current.CancellationToken
        );

        outcome.Success.Should().BeTrue();
        // 初回＋ナッジ 1 回だけ
        client.StartTurnCount.Should().Be(2);
        client.TurnPrompts[1].Should().Be(MockProjectPromptBuilder.CodexContinuationNudge);
        string.Concat(progress).Should().Contain(MockStrings.Mock_Codex_AutoContinueNotice.Trim());
    }

    /// <summary>Blazor ターゲットで *.razor があれば自動続行ナッジを送らないことを検証する（xaml テストと対称）</summary>
    [Fact(DisplayName = "Blazor・razor があれば自動続行ナッジを送らない")]
    public async Task RunAsync_Blazor_RazorPresent_DoesNotNudge()
    {
        var folder = CreateFolderWithRazor();
        var client = new FakeCodexAppServerClient();
        client.AutoTurnCompletions.Enqueue(("completed", null));
        var agent = new CodexMockProjectAgent(client);
        var progress = new List<string>();

        try
        {
            var outcome = await agent.RunAsync(
                Request(workingDirectory: folder, profile: MockProjectTargetProfile.Blazor),
                progress.Add,
                TestContext.Current.CancellationToken
            );

            outcome.Success.Should().BeTrue();
            // razor があるためナッジせず 1 ターンのみ
            client.StartTurnCount.Should().Be(1);
            string.Concat(progress)
                .Should()
                .NotContain(MockStrings.Mock_Codex_AutoContinueNotice.Trim());
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>可用性判定（codex CLI の PATH 解決）が例外なく行えることを検証する</summary>
    [Fact(DisplayName = "可用性判定は例外なく実行できる")]
    public void IsAvailable_DoesNotThrow()
    {
        var agent = new CodexMockProjectAgent(new FakeCodexAppServerClient());

        // 値は PATH 依存のため問わない。App Server を起動せず PATH 走査のみで判定できることを確認する
        agent.Invoking(a => a.IsAvailable()).Should().NotThrow();
    }

    /// <summary>
    /// 可用性判定が共有ロケーター（<see cref="CodexCliLocator"/>）と同じ結果になることを検証する。
    /// モック側だけ独自走査を持つと、チャット側の未検出表示と食い違うため委譲を固定する。
    /// </summary>
    [Fact(DisplayName = "可用性判定は共有ロケーターと同じ結果になる")]
    public void IsAvailable_MatchesSharedLocator()
    {
        var agent = new CodexMockProjectAgent(new FakeCodexAppServerClient());

        agent.IsAvailable().Should().Be(CodexCliLocator.IsAvailable());
    }
}
