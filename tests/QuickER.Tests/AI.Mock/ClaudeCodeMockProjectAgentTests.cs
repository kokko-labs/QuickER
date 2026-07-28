using FluentAssertions;
using QuickER.AI;
using QuickER.AI.Mock;

namespace QuickER.Tests.AI.Mock;

/// <summary>
/// <see cref="ClaudeCodeMockProjectAgent"/> の Claude Code 固有部（ヘッドレス起動オプション・システムプロンプト・
/// 初回プロンプト・追加指示の連結・可用性／中断の委譲）を、フェイクの Claude Code クライアントで検証するテストクラス。
/// </summary>
public class ClaudeCodeMockProjectAgentTests
{
    // 注: MockProjectAgentRunnerTests とはフェイクの粒度が異なる（あちらはエージェント自体をフェイク化）。
    // こちらは実エージェントに対し、下位の Claude Code クライアントをフェイク化してプロンプト／オプションを捕捉する。
    /// <summary>プロンプト・起動オプションを捕捉するフェイク Claude Code クライアント</summary>
    private sealed class FakeClaudeCodeClient : IClaudeCodeClient
    {
        public ClaudeCodeTurnOutcome Outcome { get; set; } = new(true, null, "s1", false);
        public ClaudeCodeLaunchOptions? CapturedOptions { get; private set; }
        public string? CapturedPrompt { get; private set; }
        public bool Interrupted { get; private set; }
        public bool Available { get; set; } = true;

        public bool IsAvailable() => Available;

        public Task<ClaudeCodeTurnOutcome> RunTurnAsync(
            string prompt,
            string? resumeSessionId,
            ClaudeCodeLaunchOptions options,
            Action<string> onAssistantText,
            CancellationToken cancellationToken
        )
        {
            CapturedPrompt = prompt;
            CapturedOptions = options;
            onAssistantText("作業を開始します。\n");
            return Task.FromResult(Outcome);
        }

        public Task<ClaudeLoginProbeResult> ProbeLoginAsync(CancellationToken cancellationToken) =>
            Task.FromResult(ClaudeLoginProbeResult.LoggedIn);

        public void Interrupt() => Interrupted = true;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static MockProjectAgentRequest Request(string? additionalInstructions = null) =>
        new(
            WorkingDirectory: @"C:\work\AcmeMock",
            ProjectName: "AcmeMock",
            AdditionalInstructions: additionalInstructions,
            Model: "sonnet",
            Profile: MockProjectTargetProfile.Wpf
        );

    private static MockProjectAgentRequest BlazorRequest() =>
        new(
            WorkingDirectory: @"C:\work\AcmeMock",
            ProjectName: "AcmeMock",
            AdditionalInstructions: null,
            Model: "sonnet",
            Profile: MockProjectTargetProfile.Blazor
        );

    /// <summary>起動オプションにヘッドレス許可（acceptEdits・Edit/Write/Bash・MCP なし）が入り、プロンプトが規約を案内することを検証する</summary>
    [Fact(DisplayName = "起動オプションはヘッドレス許可・規約プロンプトを含む")]
    public async Task RunAsync_PromptAndOptions()
    {
        var client = new FakeClaudeCodeClient();
        var agent = new ClaudeCodeMockProjectAgent(client);
        var progress = new List<string>();

        var outcome = await agent.RunAsync(
            Request(),
            progress.Add,
            TestContext.Current.CancellationToken
        );

        // クライアントの成否がそのままエージェント結果へ写る
        outcome.Success.Should().BeTrue();
        // 進捗（クライアントのアシスタントテキスト）が転送される
        string.Concat(progress).Should().Contain("作業を開始します");

        var options = client.CapturedOptions!;
        options.PermissionMode.Should().Be("acceptEdits");
        options.AdditionalAllowedTools.Should().Contain("Edit");
        options.AdditionalAllowedTools.Should().Contain("Write");
        options.AdditionalAllowedTools.Should().Contain("Bash");
        // MCP（ER ツール）は使わせない
        options.McpConfigPath.Should().BeEmpty();
        options.WorkingDirectory.Should().Be(@"C:\work\AcmeMock");
        options.Model.Should().Be("sonnet");

        // プロンプトはモックフォルダのマニフェスト・README 規約・読み取り専用を案内する（VS 標準構成のプロジェクトフォルダ配下パス）
        client.CapturedPrompt.Should().Contain("AcmeMock/design/mock/mock.json");
        client.CapturedPrompt.Should().Contain("AcmeMock/README-QuickER.md");
        client.CapturedPrompt.Should().Contain("AcmeMock/Generated/");
        // ソリューション直下でビルドする案内が入る
        client.CapturedPrompt.Should().Contain("AcmeMock.sln");

        // システムプロンプトも VS 標準構成（sln・プロジェクトフォルダ）とモックフォルダのマニフェストを案内する
        options.SystemPrompt.Should().Contain("AcmeMock.sln");
        options.SystemPrompt.Should().Contain("AcmeMock/Generated/");
        options.SystemPrompt.Should().Contain("AcmeMock/design/mock/mock.json");

        // 非対話・確認禁止の明示（ヘッドレスで承認待ちに陥らないための保険）が入る＝英語正本
        options.SystemPrompt.Should().Contain("non-interactive, headless run");
        options.SystemPrompt.Should().Contain("wait for approval");
        client.CapturedPrompt.Should().Contain("Do not ask for confirmation or approval");

        // 生成コードの必須使用・XAML View 必須（コード組み立て禁止）の規約が入る
        options.SystemPrompt.Should().Contain("XAML view");
        options.SystemPrompt.Should().Contain("forbidden");
        options.SystemPrompt.Should().Contain("hard-coded list");

        // 主キーのアプリ側採番（GuidKey の例外込み）とパッケージソース設定の禁止が入る
        options.SystemPrompt.Should().Contain("assign the primary key in the application");
        options.SystemPrompt.Should().Contain("GuidKey");
        options.SystemPrompt.Should().Contain("NuGet.Config");
        // 初回プロンプトの完了条件チェックリスト
        client.CapturedPrompt.Should().Contain("Completion criteria");
        client.CapturedPrompt.Should().Contain("through I{Entity}Repository");
    }

    /// <summary>Blazor プロファイルではプロンプトに Blazor 固有規約（.razor／@page／InteractiveServer／style.css 移植）と共有規約が入ることを検証する</summary>
    [Fact(DisplayName = "Blazor はプロンプトに Blazor 規約と共有規約を含む")]
    public async Task RunAsync_BlazorProfile_PromptFragments()
    {
        var client = new FakeClaudeCodeClient();
        var agent = new ClaudeCodeMockProjectAgent(client);

        await agent.RunAsync(BlazorRequest(), _ => { }, TestContext.Current.CancellationToken);

        // システムプロンプト＋初回プロンプトを併せて Blazor 固有の語彙を確認する
        var combined = client.CapturedOptions!.SystemPrompt + "\n" + client.CapturedPrompt;
        combined.Should().Contain(".razor");
        combined.Should().Contain("@page");
        combined.Should().Contain("InteractiveServer");
        combined.Should().Contain("style.css");

        // 共有規約（Blazor でも変わらず入る）
        combined.Should().Contain("assign the primary key in the application");
        combined.Should().Contain("NuGet.Config");

        // WPF 固有の語彙は出ない
        combined.Should().NotContain("CommunityToolkit");
        combined.Should().NotContain(".xaml");
    }

    /// <summary>追加指示が非空ならプロンプト末尾へ「# Additional instructions」見出し付きで連結され、空なら付かないことを検証する</summary>
    [Fact(DisplayName = "追加指示は非空なら末尾へ連結・空なら付かない")]
    public async Task RunAsync_AppendsAdditionalInstructions()
    {
        var client = new FakeClaudeCodeClient();
        var agent = new ClaudeCodeMockProjectAgent(client);

        // 追加指示ありのとき、見出し（英語固定）と本文がプロンプト末尾に連結される
        await agent.RunAsync(
            Request("ダークテーマで実装して"),
            _ => { },
            TestContext.Current.CancellationToken
        );

        var heading = MockProjectPromptBuilder.AdditionalInstructionsHeading;
        client.CapturedPrompt.Should().Contain(heading);
        client.CapturedPrompt.Should().Contain("ダークテーマで実装して");

        // 追加指示なしのとき、見出しは付かない
        await agent.RunAsync(Request(), _ => { }, TestContext.Current.CancellationToken);

        client.CapturedPrompt.Should().NotContain(heading);
    }

    /// <summary>クライアントの失敗（未ログイン含む）がエージェント結果へ写ることを検証する</summary>
    [Fact(DisplayName = "クライアント失敗・未ログインがエージェント結果へ写る")]
    public async Task RunAsync_MapsClientOutcome()
    {
        var client = new FakeClaudeCodeClient
        {
            Outcome = new ClaudeCodeTurnOutcome(false, "Not logged in", null, true),
        };
        var agent = new ClaudeCodeMockProjectAgent(client);

        var outcome = await agent.RunAsync(
            Request(),
            _ => { },
            TestContext.Current.CancellationToken
        );

        outcome.Success.Should().BeFalse();
        outcome.Error.Should().Be("Not logged in");
        outcome.NotLoggedIn.Should().BeTrue();
    }

    /// <summary>可用性がクライアントへ委譲され、中断でクライアントの Interrupt が呼ばれることを検証する</summary>
    [Fact(DisplayName = "可用性はクライアントへ委譲・中断で Interrupt を呼ぶ")]
    public async Task AvailabilityAndInterrupt_DelegateToClient()
    {
        var client = new FakeClaudeCodeClient { Available = false };
        var agent = new ClaudeCodeMockProjectAgent(client);

        agent.IsAvailable().Should().BeFalse();

        client.Available = true;
        agent.IsAvailable().Should().BeTrue();

        await agent.InterruptAsync();
        client.Interrupted.Should().BeTrue();
    }
}
