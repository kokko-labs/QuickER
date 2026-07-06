using System.IO;
using FluentAssertions;
using QuickER.AI;
using QuickER.Model;
using QuickER.Services;
using QuickER.Services.Chat;
using QuickER.ViewModels;

namespace QuickER.Tests.ViewModels;

/// <summary>
/// <see cref="MockGenerationDialogViewModel"/> の生成可否・HTML 提出通知・ターン中の入力禁止・
/// フィードバック送信・HTML 保存を、フェイクエンジン／セッションで検証するテストクラス。
/// </summary>
public class MockGenerationDialogViewModelTests
{
    private sealed class SyncUiDispatcher : IUiDispatcher
    {
        public T Invoke<T>(Func<T> func) => func();
    }

    /// <summary>現在の ER 図を供給するスタブ（空判定を切り替え可能）</summary>
    private sealed class StubDiagramSource : IMockDiagramSource
    {
        private readonly ErDiagram _diagram;

        public StubDiagramSource(ErDiagram diagram) => _diagram = diagram;

        public bool IsEmpty => _diagram.Entities.Count == 0;

        public ErDiagram GetDiagram() => _diagram;

        public QuickER.Provider.DatabaseProviderRegistry Providers { get; } =
            new([new QuickER.SqlServer.SqlServerProvider()]);
    }

    /// <summary>スクリプトされたツール呼び出しをツールホストへ橋渡しするフェイクエンジン</summary>
    private sealed class FakeChatEngine : IErChatEngine
    {
        private readonly IErDiagramToolHost _toolHost;

        public FakeChatEngine(IErDiagramToolHost toolHost) => _toolHost = toolHost;

        public List<string> SentPrompts { get; } = new();

        /// <summary>各 SendAsync で受け取った添付（添付付きオーバーロード検証用）</summary>
        public List<IReadOnlyList<ChatAttachment>> SentAttachments { get; } = new();

        /// <summary>次の SendAsync で再生するツール呼び出し（ツール名・引数 JSON）</summary>
        public (string Tool, string Args)? ScriptedToolCall { get; set; }

        public event EventHandler<string>? AssistantDeltaReceived;
        public event EventHandler<ErChatToolActivity>? ToolActivityReceived;
        public event EventHandler<ErChatTurnResult>? TurnCompleted;
        public event EventHandler<string>? StatusChanged;

        public bool IsReady => true;

        public Task InitializeAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task StartConversationAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SendAsync(
            string prompt,
            IReadOnlyList<ChatAttachment> attachments,
            CancellationToken cancellationToken = default
        )
        {
            SentAttachments.Add(attachments);
            return SendAsync(prompt, cancellationToken);
        }

        public Task SendAsync(string prompt, CancellationToken cancellationToken = default)
        {
            SentPrompts.Add(prompt);
            StatusChanged?.Invoke(this, "生成中...");
            AssistantDeltaReceived?.Invoke(this, "了解しました。");

            if (ScriptedToolCall is { } call)
            {
                var result = _toolHost.Execute(call.Tool, call.Args);
                ToolActivityReceived?.Invoke(
                    this,
                    new ErChatToolActivity(call.Tool, result.Result, result.Success)
                );
                ScriptedToolCall = null;
            }

            TurnCompleted?.Invoke(this, new ErChatTurnResult(true, null));
            return Task.CompletedTask;
        }

        public Task InterruptAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>WPF モック生成の第2ステップをスクリプト化するフェイク生成器</summary>
    private sealed class FakeMockProjectGenerator : IMockProjectGenerator
    {
        public bool ClaudeAvailable { get; set; } = true;
        public bool DotnetAvailable { get; set; } = true;
        public bool ResultSuccess { get; set; } = true;
        public bool Interrupted { get; private set; }
        public int GenerateCallCount { get; private set; }
        public string? CapturedOutputFolder { get; private set; }
        public string? CapturedProjectName { get; private set; }

        public bool IsClaudeAvailable() => ClaudeAvailable;

        public Task<bool> IsDotnetAvailableAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(DotnetAvailable);

        public Task<MockProjectGenerationResult> GenerateAsync(
            ErDiagram diagram,
            string designHtml,
            string outputDirectory,
            string projectName,
            string model,
            Action<string> onProgress,
            CancellationToken cancellationToken = default
        )
        {
            GenerateCallCount++;
            CapturedOutputFolder = outputDirectory;
            CapturedProjectName = projectName;
            onProgress("進捗: 生成中...\n");

            return Task.FromResult(
                new MockProjectGenerationResult(
                    ResultSuccess,
                    ResultSuccess ? "完了しました。" : "失敗しました。",
                    outputDirectory,
                    Path.Combine(outputDirectory, "quickr-mock-generation.log")
                )
            );
        }

        public Task InterruptAsync()
        {
            Interrupted = true;
            return Task.CompletedTask;
        }
    }

    private const string ValidHtml =
        "<!DOCTYPE html><html lang=\"ja\"><head><style>body{}</style></head>"
        + "<body><h1>顧客一覧</h1></body></html>";

    /// <summary>顧客テーブル 1 つを持つ非空の図を返す</summary>
    private static ErDiagram NonEmptyDiagram() =>
        new()
        {
            Entities =
            {
                new Entity { TableName = "Customer", Description = "顧客" },
            },
        };

    /// <summary>フェイクエンジンを注入して ViewModel を生成する（settings は一時フォルダへ隔離）</summary>
    private static (
        MockGenerationDialogViewModel vm,
        FakeChatEngine[] engineBox,
        string folder
    ) CreateVm(ErDiagram diagram)
    {
        var folder = Path.Combine(Path.GetTempPath(), "QuickERTests", Guid.NewGuid().ToString("N"));
        var engineBox = new FakeChatEngine[1];

        var vm = new MockGenerationDialogViewModel(
            new StubDiagramSource(diagram),
            new SyncUiDispatcher(),
            files: null,
            codexSettingsStore: new CodexAppServerSettingsStore(folder),
            apiKeyEngineFactory: (_, toolHost) => engineBox[0] = new FakeChatEngine(toolHost),
            codexEngineFactory: null,
            claudeCodeEngineFactory: null
        );
        vm.ApiProvider = AiProvider.Ollama; // 認証不要にして接続 OK 状態にする
        return (vm, engineBox, folder);
    }

    private static void Cleanup(string folder)
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>フェイク生成器を注入して VM を生成する（第2ステップ検証用）</summary>
    private static (
        MockGenerationDialogViewModel vm,
        FakeChatEngine[] engineBox,
        FakeMockProjectGenerator generator,
        string folder
    ) CreateVmWithGenerator(ErDiagram diagram)
    {
        var folder = Path.Combine(Path.GetTempPath(), "QuickERTests", Guid.NewGuid().ToString("N"));
        var engineBox = new FakeChatEngine[1];
        var generator = new FakeMockProjectGenerator();

        var vm = new MockGenerationDialogViewModel(
            new StubDiagramSource(diagram),
            new SyncUiDispatcher(),
            files: null,
            codexSettingsStore: new CodexAppServerSettingsStore(folder),
            apiKeyEngineFactory: null,
            codexEngineFactory: null,
            claudeCodeEngineFactory: (_, toolHost) => engineBox[0] = new FakeChatEngine(toolHost),
            mockProjectGenerator: generator
        );

        return (vm, engineBox, generator, folder);
    }

    /// <summary>Claude Code バックエンドで会話開始→初回送信で HTML 提出まで進め、第2ステップの前提を整える</summary>
    private static async Task SubmitHtmlOnClaudeCode(
        MockGenerationDialogViewModel vm,
        FakeChatEngine[] engineBox
    )
    {
        vm.SelectedBackend = ErChatBackendKind.ClaudeCode;
        // Claude Code バックエンドは接続 OK を外部から反映する
        vm.ApplyClaudeCodeReadiness(true, "ログイン済み", ConnectionHealth.Ready, string.Empty);

        // 「＋新しい会話」でセッションを用意すると、その中でエンジンが構築され engineBox に入る
        vm.StartConversationCommand.Execute(null);
        var args = $"{{\"html\":{System.Text.Json.JsonSerializer.Serialize(ValidHtml)}}}";
        engineBox[0].ScriptedToolCall = (MockDesignTools.SaveMockHtmlToolName, args);
        vm.UserInput = "提出";
        await vm.SendMessageCommand.ExecuteAsync(null);
    }

    /// <summary>空の図では「＋新しい会話」が無効、非空なら有効になることを検証する</summary>
    [Fact(DisplayName = "空図では新しい会話不可・非空なら可能")]
    public void CanStartConversation_DependsOnDiagramEmptiness()
    {
        var (emptyVm, _, emptyFolder) = CreateVm(new ErDiagram());

        try
        {
            emptyVm.IsDiagramEmpty.Should().BeTrue();
            emptyVm.CanStartConversation.Should().BeFalse();
        }
        finally
        {
            Cleanup(emptyFolder);
        }

        var (vm, _, folder) = CreateVm(NonEmptyDiagram());

        try
        {
            vm.IsDiagramEmpty.Should().BeFalse();
            vm.CanStartConversation.Should().BeTrue();
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>会話開始前は送信不可・開始後に入力ありで送信可能になることを検証する</summary>
    [Fact(DisplayName = "会話開始前は送信不可・開始後は入力ありで可能")]
    public void CanSendMessage_RequiresStartedConversationAndInput()
    {
        var (vm, _, folder) = CreateVm(NonEmptyDiagram());

        try
        {
            // 会話開始前は入力があっても送信不可
            vm.UserInput = "シンプルな管理画面で";
            vm.CanSendMessage.Should().BeFalse();

            // 会話を開始し、入力ありなら送信可能
            vm.StartConversationCommand.Execute(null);
            vm.CanSendMessage.Should().BeTrue();

            // 入力が空になると送信不可
            vm.UserInput = string.Empty;
            vm.CanSendMessage.Should().BeFalse();
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>初回送信でスキーマ（テーブル名）＋要望が送られることを検証する</summary>
    [Fact(DisplayName = "初回送信で図＋要望がエンジンへ送られる")]
    public async Task FirstSend_SendsSchemaWithUserRequest()
    {
        var (vm, engineBox, folder) = CreateVm(NonEmptyDiagram());

        try
        {
            vm.StartConversationCommand.Execute(null);
            engineBox[0].Should().NotBeNull();

            vm.UserInput = "モダンな配色にして";
            await vm.SendMessageCommand.ExecuteAsync(null);

            engineBox[0].SentPrompts.Should().ContainSingle();
            engineBox[0].SentPrompts[0].Should().Contain("Customer");
            engineBox[0].SentPrompts[0].Should().Contain("モダンな配色にして");
            // 送信後は入力欄がクリアされる
            vm.UserInput.Should().BeEmpty();
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>2 回目以降の送信はフィードバックとして（スキーマ添付なしで）送られることを検証する</summary>
    [Fact(DisplayName = "2 回目の送信はフィードバックとして送られる")]
    public async Task SecondSend_SendsFeedbackWithoutSchema()
    {
        var (vm, engineBox, folder) = CreateVm(NonEmptyDiagram());

        try
        {
            vm.StartConversationCommand.Execute(null);

            vm.UserInput = "初回の要望";
            await vm.SendMessageCommand.ExecuteAsync(null);

            vm.UserInput = "列を減らして";
            await vm.SendMessageCommand.ExecuteAsync(null);

            engineBox[0].SentPrompts.Should().HaveCount(2);
            // 2 回目はスキーマ（テーブル名）を含まない生のフィードバック
            engineBox[0].SentPrompts[1].Should().Be("列を減らして");
            engineBox[0].SentPrompts[1].Should().NotContain("Customer");
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>ターン実行中は送信できないことを検証する</summary>
    [Fact(DisplayName = "ターン実行中は送信不可")]
    public void CanSendMessage_FalseDuringTurn()
    {
        var (vm, _, folder) = CreateVm(NonEmptyDiagram());

        try
        {
            vm.StartConversationCommand.Execute(null);
            vm.UserInput = "要望";
            vm.CanSendMessage.Should().BeTrue();

            vm.IsTurnInProgress = true;
            vm.CanSendMessage.Should().BeFalse();
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>HTML 提出で HtmlUpdated が発火し、保存コマンドが有効化されることを検証する</summary>
    [Fact(DisplayName = "HTML 提出で HtmlUpdated 発火・保存コマンド有効化")]
    public async Task HtmlUpdated_RaisesEventAndEnablesSave()
    {
        var (vm, engineBox, folder) = CreateVm(NonEmptyDiagram());

        try
        {
            MockHtmlUpdate? received = null;
            vm.HtmlUpdated += (_, u) => received = u;

            var args =
                $"{{\"html\":{System.Text.Json.JsonSerializer.Serialize(ValidHtml)},\"revision_note\":\"初版\"}}";

            // 「＋新しい会話」でエンジンが構築されるので、その直後にツール呼び出しを仕込み、
            // 初回送信（StartAsync 経由の SendAsync）でツールが再生されるようにする。
            vm.StartConversationCommand.Execute(null);
            engineBox[0].ScriptedToolCall = (MockDesignTools.SaveMockHtmlToolName, args);

            vm.CanSaveHtml.Should().BeFalse();
            vm.SaveHtmlCommand.CanExecute(null).Should().BeFalse();

            vm.UserInput = "HTML を提出して";
            await vm.SendMessageCommand.ExecuteAsync(null);

            received.Should().NotBeNull();
            received!.Value.Html.Should().Be(ValidHtml);
            vm.CanSaveHtml.Should().BeTrue();
            vm.SaveHtmlCommand.CanExecute(null).Should().BeTrue();
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>HTML 提出後に「＋新しい会話」でリセットしても保存が有効なまま（VM が確定 HTML を保持）を検証する</summary>
    [Fact(DisplayName = "新しい会話後も保存が有効のまま")]
    public async Task StartConversation_KeepsSaveEnabledAfterReset()
    {
        var (vm, engineBox, folder) = CreateVm(NonEmptyDiagram());

        try
        {
            var args = $"{{\"html\":{System.Text.Json.JsonSerializer.Serialize(ValidHtml)}}}";

            vm.StartConversationCommand.Execute(null);
            engineBox[0].ScriptedToolCall = (MockDesignTools.SaveMockHtmlToolName, args);
            vm.UserInput = "提出";
            await vm.SendMessageCommand.ExecuteAsync(null);

            vm.CanSaveHtml.Should().BeTrue();

            // 新しい会話でセッションを破棄しても、確定 HTML は VM に残り保存は有効のまま
            vm.StartConversationCommand.Execute(null);
            vm.CanSaveHtml.Should().BeTrue();
            vm.SaveHtmlCommand.CanExecute(null).Should().BeTrue();
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>提出済み HTML が保存ダイアログのパスへ書き出されることを検証する</summary>
    [Fact(DisplayName = "HTML 保存は選択パスへ書き出す")]
    public async Task SaveHtml_WritesToPickedPath()
    {
        var folder = Path.Combine(Path.GetTempPath(), "QuickERTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(folder, "out.html");
        Directory.CreateDirectory(folder);
        var files = new RecordingFileDialogService(new FileDialogResult(path, 1));
        var engineBox = new FakeChatEngine[1];

        var vm = new MockGenerationDialogViewModel(
            new StubDiagramSource(NonEmptyDiagram()),
            new SyncUiDispatcher(),
            files: files,
            codexSettingsStore: new CodexAppServerSettingsStore(folder),
            apiKeyEngineFactory: (_, toolHost) => engineBox[0] = new FakeChatEngine(toolHost),
            codexEngineFactory: null,
            claudeCodeEngineFactory: null
        );
        vm.ApiProvider = AiProvider.Ollama;

        try
        {
            vm.StartConversationCommand.Execute(null);
            var args = $"{{\"html\":{System.Text.Json.JsonSerializer.Serialize(ValidHtml)}}}";
            engineBox[0].ScriptedToolCall = (MockDesignTools.SaveMockHtmlToolName, args);
            vm.UserInput = "提出";
            await vm.SendMessageCommand.ExecuteAsync(null);

            vm.SaveHtmlCommand.Execute(null);

            File.Exists(path).Should().BeTrue();
            File.ReadAllText(path).Should().Be(ValidHtml);
        }
        finally
        {
            Cleanup(folder);
        }
    }

    // ── 第2ステップ: WPF モックプロジェクト生成 ──

    /// <summary>確定 HTML・接続・claude/dotnet 検出・入力が揃うと生成可能になることを検証する</summary>
    [Fact(DisplayName = "第2ステップの有効条件がすべて揃うと生成可能")]
    public async Task CanGenerateMockProject_RequiresAllConditions()
    {
        var (vm, engineBox, generator, folder) = CreateVmWithGenerator(NonEmptyDiagram());

        try
        {
            generator.ClaudeAvailable = true;
            generator.DotnetAvailable = true;
            await vm.RefreshMockGenAvailabilityAsync();

            // HTML 未提出では不可
            vm.CanGenerateMockProject.Should().BeFalse();

            await SubmitHtmlOnClaudeCode(vm, engineBox);
            vm.OutputFolder = folder;
            vm.ProjectName = "AcmeMock";

            // ここまでで全条件が揃う
            vm.CanGenerateMockProject.Should().BeTrue();

            // 出力フォルダを空にすると不可・理由が出る
            vm.OutputFolder = string.Empty;
            vm.CanGenerateMockProject.Should().BeFalse();
            vm.MockGenDisabledReason.Should().Contain("出力フォルダ");
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>claude CLI 未検出では生成不可・理由が案内されることを検証する</summary>
    [Fact(DisplayName = "claude 未検出では生成不可")]
    public async Task CanGenerateMockProject_FalseWhenClaudeMissing()
    {
        var (vm, engineBox, generator, folder) = CreateVmWithGenerator(NonEmptyDiagram());

        try
        {
            generator.ClaudeAvailable = false;
            generator.DotnetAvailable = true;
            await vm.RefreshMockGenAvailabilityAsync();

            await SubmitHtmlOnClaudeCode(vm, engineBox);
            vm.OutputFolder = folder;
            vm.ProjectName = "AcmeMock";

            vm.CanGenerateMockProject.Should().BeFalse();
            vm.MockGenDisabledReason.Should().Contain("claude");
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>生成実行で状態遷移・進捗転送・成功時のフォルダ表示が起きることを検証する</summary>
    [Fact(DisplayName = "生成実行で進捗転送・成功でフォルダ表示")]
    public async Task GenerateMockProject_TransitionsAndForwardsProgress()
    {
        var (vm, engineBox, generator, folder) = CreateVmWithGenerator(NonEmptyDiagram());

        try
        {
            generator.ResultSuccess = true;
            await vm.RefreshMockGenAvailabilityAsync();
            await SubmitHtmlOnClaudeCode(vm, engineBox);
            vm.OutputFolder = folder;
            vm.ProjectName = "AcmeMock";

            await vm.GenerateMockProjectCommand.ExecuteAsync(null);

            generator.GenerateCallCount.Should().Be(1);
            generator.CapturedOutputFolder.Should().Be(folder);
            generator.CapturedProjectName.Should().Be("AcmeMock");
            vm.MockGenLog.Should().Contain("進捗: 生成中");
            vm.IsMockGenInProgress.Should().BeFalse();
            vm.MockGenCompleted.Should().BeTrue();
            vm.MockGenSucceeded.Should().BeTrue();
            vm.ShowOpenFolder.Should().BeTrue();
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>生成失敗時はフォルダ表示せず完了フラグのみ立つことを検証する</summary>
    [Fact(DisplayName = "生成失敗ではフォルダを開くボタンを出さない")]
    public async Task GenerateMockProject_FailureHidesOpenFolder()
    {
        var (vm, engineBox, generator, folder) = CreateVmWithGenerator(NonEmptyDiagram());

        try
        {
            generator.ResultSuccess = false;
            await vm.RefreshMockGenAvailabilityAsync();
            await SubmitHtmlOnClaudeCode(vm, engineBox);
            vm.OutputFolder = folder;
            vm.ProjectName = "AcmeMock";

            await vm.GenerateMockProjectCommand.ExecuteAsync(null);

            vm.MockGenCompleted.Should().BeTrue();
            vm.MockGenSucceeded.Should().BeFalse();
            vm.ShowOpenFolder.Should().BeFalse();
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>API キー接続では第2ステップが無効（Claude Code 限定）であることを検証する</summary>
    [Fact(DisplayName = "API キー接続では第2ステップ無効")]
    public async Task CanGenerateMockProject_FalseOnNonClaudeBackend()
    {
        var (vm, engineBox, generator, folder) = CreateVmWithGenerator(NonEmptyDiagram());

        try
        {
            await vm.RefreshMockGenAvailabilityAsync();
            await SubmitHtmlOnClaudeCode(vm, engineBox);
            vm.OutputFolder = folder;
            vm.ProjectName = "AcmeMock";
            vm.CanGenerateMockProject.Should().BeTrue();

            // バックエンドを API キーへ切り替えると無効になる
            vm.SelectedBackend = ErChatBackendKind.ApiKey;
            vm.CanGenerateMockProject.Should().BeFalse();
            vm.MockGenDisabledReason.Should().Contain("Claude Code");
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>PNG シグネチャ付きバイト列を作る（添付テスト用）</summary>
    private static byte[] PngBytes()
    {
        var data = new byte[16];
        byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        Array.Copy(signature, data, signature.Length);
        return data;
    }

    /// <summary>Claude Code バックエンド（ImagesAndPdf）では添付範囲が画像＋PDF になることを検証する</summary>
    [Fact(DisplayName = "Claude Code では添付範囲が画像＋PDF")]
    public void AttachmentSupport_ClaudeCode_IsImagesAndPdf()
    {
        var (vm, _, folder) = CreateVm(NonEmptyDiagram());

        try
        {
            vm.SelectedBackend = ErChatBackendKind.ClaudeCode;
            vm.Attachments.Support.Should().Be(AttachmentSupport.ImagesAndPdf);

            // Codex はエンジンが添付非対応
            vm.SelectedBackend = ErChatBackendKind.Codex;
            vm.Attachments.Support.Should().Be(AttachmentSupport.None);
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>添付付き送信がセッション経由でエンジンへ渡り、送信後にチップがクリアされることを検証する</summary>
    [Fact(DisplayName = "添付付き送信がエンジンへ渡りクリアされる")]
    public async Task SendWithAttachment_PassesToEngine_AndClears()
    {
        // Claude Code バックエンドのエンジンをフェイクへ差し替える（engineBox で捕捉する）
        var (vm, engineBox, _, folder) = CreateVmWithGenerator(NonEmptyDiagram());

        try
        {
            vm.SelectedBackend = ErChatBackendKind.ClaudeCode;
            vm.ApplyClaudeCodeReadiness(true, "ログイン済み", ConnectionHealth.Ready, string.Empty);
            vm.StartConversationCommand.Execute(null);

            vm.Attachments.AddClipboardImage(PngBytes(), DateTime.Now);
            vm.Attachments.Items.Should().HaveCount(1);

            vm.UserInput = "この画面イメージで作って";
            await vm.SendMessageCommand.ExecuteAsync(null);

            // 初回送信の添付がエンジンへ渡っている
            engineBox[0].SentAttachments.Should().ContainSingle();
            engineBox[0].SentAttachments[0].Should().HaveCount(1);

            // 送信後にチップはクリアされる
            vm.Attachments.Items.Should().BeEmpty();

            // ユーザー吹き出しに添付要約が載る
            vm.Messages.Should()
                .ContainSingle(m => m.Role == ErChatMessageRole.User && m.HasAttachments);
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>バックエンドを添付非対応（Codex）へ切り替えると Pending がクリアされることを検証する</summary>
    [Fact(DisplayName = "非対応バックエンドへ切替で添付をクリア")]
    public void SwitchToUnsupportedBackend_ClearsAttachments()
    {
        var (vm, _, folder) = CreateVm(NonEmptyDiagram());

        try
        {
            vm.SelectedBackend = ErChatBackendKind.ClaudeCode;
            vm.Attachments.AddClipboardImage(PngBytes(), DateTime.Now);
            vm.Attachments.Items.Should().HaveCount(1);

            vm.SelectedBackend = ErChatBackendKind.Codex;

            vm.Attachments.Items.Should().BeEmpty();
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>選択済み結果を返し、初期ファイル名を記録するファイルダイアログスタブ</summary>
    private sealed class RecordingFileDialogService : IFileDialogService
    {
        private readonly FileDialogResult? _saveResult;

        public RecordingFileDialogService(FileDialogResult? saveResult) => _saveResult = saveResult;

        public string? LastInitialFileName { get; private set; }

        public FileDialogResult? PickOpenFile(string filter) => null;

        public FileDialogResult? PickSaveFile(
            string filter,
            string defaultExt,
            string? initialFileName = null,
            string? initialDirectory = null
        )
        {
            LastInitialFileName = initialFileName;
            return _saveResult;
        }

        public string? PickFolder(string title, string? initialDirectory = null) => null;
    }
}
