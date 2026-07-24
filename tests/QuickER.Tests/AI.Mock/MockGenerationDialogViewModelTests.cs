using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using FluentAssertions;
using QuickER.AI;
using QuickER.AI.Mock;
using QuickER.Gui.Abstractions;
using QuickER.Model;
using QuickER.Tests.TestDoubles;
using MockStrings = QuickER.AI.Mock.Resources.Strings;

namespace QuickER.Tests.AI.Mock;

/// <summary>
/// モックフォルダ方式の <see cref="MockGenerationDialogViewModel"/> の会話開始（新規／再開）・会話前ビュー・
/// 画面保存によるサイドバー更新／プレビュー要求・単一 HTML 出力・第2ステップ可否を、
/// フェイクエンジン／セッションで検証するテストクラス。
/// </summary>
public class MockGenerationDialogViewModelTests
{
    private sealed class SyncUiDispatcher : IUiDispatcher
    {
        public T Invoke<T>(Func<T> func) => func();
    }

    // StubDiagramSource / FakeChatEngine は共有版（QuickER.Tests.AI.Mock）を使用する

    /// <summary>WPF モック生成の第2ステップをスクリプト化するフェイク生成器</summary>
    private sealed class FakeMockProjectGenerator : IMockProjectGenerator
    {
        public bool ClaudeAvailable { get; set; } = true;
        public bool CodexAvailable { get; set; } = true;
        public bool DotnetAvailable { get; set; } = true;
        public bool ResultSuccess { get; set; } = true;

        /// <summary>返す結果の中断フラグ（true でユーザー中断＝VM は完了ダイアログを出さない）</summary>
        public bool ResultInterrupted { get; set; }

        public bool Interrupted { get; private set; }
        public int GenerateCallCount { get; private set; }
        public string? CapturedOutputFolder { get; private set; }
        public string? CapturedProjectName { get; private set; }
        public string? CapturedMockFolder { get; private set; }
        public string? CapturedInstructions { get; private set; }
        public ErChatBackendKind? CapturedBackend { get; private set; }
        public string? CapturedModel { get; private set; }
        public string? CapturedModelProvider { get; private set; }

        public IReadOnlyList<MockProjectTarget> Targets { get; } =
        [MockProjectTarget.Blazor, MockProjectTarget.Wpf];

        public MockProjectTarget? CapturedTarget { get; private set; }

        public bool IsAgentAvailable(ErChatBackendKind backend) =>
            backend == ErChatBackendKind.Codex ? CodexAvailable : ClaudeAvailable;

        public Task<bool> IsDotnetAvailableAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(DotnetAvailable);

        public Task<MockProjectGenerationResult> GenerateAsync(
            ErDiagram diagram,
            string mockFolder,
            string? additionalInstructions,
            string outputDirectory,
            string projectName,
            MockProjectTarget target,
            ErChatBackendKind backend,
            string model,
            string modelProvider,
            Action<string> onProgress,
            CancellationToken cancellationToken = default
        )
        {
            GenerateCallCount++;
            CapturedOutputFolder = outputDirectory;
            CapturedProjectName = projectName;
            CapturedTarget = target;
            CapturedMockFolder = mockFolder;
            CapturedInstructions = additionalInstructions;
            CapturedBackend = backend;
            CapturedModel = model;
            CapturedModelProvider = modelProvider;
            onProgress("進捗: 生成中...\n");

            return Task.FromResult(
                new MockProjectGenerationResult(
                    ResultSuccess,
                    ResultSuccess ? "完了しました。" : "失敗しました。",
                    outputDirectory,
                    Path.Combine(outputDirectory, "quickr-mock-generation.log"),
                    ResultInterrupted
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
        "<!DOCTYPE html><html lang=\"ja\"><head><link rel=\"stylesheet\" href=\"style.css\">"
        + "<style>body{}</style></head><body><h1>顧客一覧</h1></body></html>";

    /// <summary>顧客テーブル 1 つを持つ非空の図を返す</summary>
    private static ErDiagram NonEmptyDiagram() =>
        new()
        {
            Entities =
            {
                new Entity { TableName = "Customer", Description = "顧客" },
            },
        };

    /// <summary>save_screen ツール引数の JSON を組み立てる</summary>
    private static string SaveScreenArgs(
        string file = "OrderList.html",
        string name = "注文一覧",
        string html = ValidHtml
    ) =>
        JsonSerializer.Serialize(
            new Dictionary<string, object?>
            {
                ["file"] = file,
                ["name"] = name,
                ["html"] = html,
            }
        );

    /// <summary>既存モックフォルダを作成し、画面を 1 つ入れておく（会話前ビュー・再開の前提）</summary>
    private static void SeedMockFolder(string mockFolder, string screenFile = "OrderList.html")
    {
        var store = MockFolderStore.CreateNew(mockFolder, "受注管理", "# schema");
        store.SaveScreen(
            screenFile,
            "注文一覧",
            "注文の一覧",
            ValidHtml,
            Array.Empty<MockTransition>(),
            "初版"
        );
    }

    /// <summary>フェイク（API キー）エンジンを注入して VM を生成する（settings は一時フォルダへ隔離）</summary>
    private static (
        MockGenerationDialogViewModel vm,
        FakeChatEngine[] engineBox,
        string baseFolder,
        string mockFolder
    ) CreateVm(ErDiagram diagram, bool setMockFolder = true, StubDialogService? dialogs = null)
    {
        var baseFolder = Path.Combine(
            Path.GetTempPath(),
            "QuickERTests",
            Guid.NewGuid().ToString("N")
        );
        var mockFolder = Path.Combine(baseFolder, "mock");
        var engineBox = new FakeChatEngine[1];

        var vm = new MockGenerationDialogViewModel(
            new StubDiagramSource(diagram),
            new SyncUiDispatcher(),
            files: null,
            // 設定・UI 状態・モデル履歴を集約した 1 ファイルを一時フォルダへ隔離する（実 %APPDATA% を保護）
            settingsStore: new AiSettingsStore(Path.Combine(baseFolder, "settings")),
            apiKeyEngineFactory: (_, toolHost) => engineBox[0] = new FakeChatEngine(toolHost),
            codexEngineFactory: null,
            claudeCodeEngineFactory: null,
            dialogService: dialogs ?? new StubDialogService()
        );
        vm.Connection.ApiProvider = AiProvider.Ollama; // 認証不要にして接続 OK 状態にする

        if (setMockFolder)
        {
            vm.MockFolder = mockFolder;
        }

        return (vm, engineBox, baseFolder, mockFolder);
    }

    /// <summary>フェイク（Claude Code）エンジンとフェイク生成器を注入して VM を生成する（第2ステップ検証用）</summary>
    private static (
        MockGenerationDialogViewModel vm,
        FakeChatEngine[] engineBox,
        FakeMockProjectGenerator generator,
        string baseFolder,
        string mockFolder
    ) CreateVmWithGenerator(ErDiagram diagram, StubDialogService? dialogs = null)
    {
        var baseFolder = Path.Combine(
            Path.GetTempPath(),
            "QuickERTests",
            Guid.NewGuid().ToString("N")
        );
        var mockFolder = Path.Combine(baseFolder, "mock");
        var engineBox = new FakeChatEngine[1];
        var generator = new FakeMockProjectGenerator();

        var vm = new MockGenerationDialogViewModel(
            new StubDiagramSource(diagram),
            new SyncUiDispatcher(),
            files: null,
            settingsStore: new AiSettingsStore(Path.Combine(baseFolder, "settings")),
            apiKeyEngineFactory: null,
            codexEngineFactory: null,
            claudeCodeEngineFactory: (_, toolHost) => engineBox[0] = new FakeChatEngine(toolHost),
            mockProjectGenerator: generator,
            dialogService: dialogs ?? new StubDialogService()
        );

        return (vm, engineBox, generator, baseFolder, mockFolder);
    }

    private static void Cleanup(string folder)
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>Claude Code バックエンドで新規モックフォルダに会話開始→初回送信で 1 画面を保存し、第2ステップの前提を整える</summary>
    private static async Task SaveScreenOnClaudeCode(
        MockGenerationDialogViewModel vm,
        FakeChatEngine[] engineBox,
        string mockFolder
    )
    {
        vm.Connection.SelectedBackend = ErChatBackendKind.ClaudeCode;
        // Claude Code バックエンドは接続 OK を外部から反映する
        vm.ApplyClaudeCodeReadiness(true, "ログイン済み", ConnectionHealth.Ready, string.Empty);
        vm.MockFolder = mockFolder;

        // 「新しい会話」でセッションを用意すると、その中でエンジンが構築され engineBox に入る
        vm.StartConversationCommand.Execute(null);
        engineBox[0].ScriptedToolCall = (
            MockFolderDesignTools.SaveScreenToolName,
            SaveScreenArgs()
        );
        vm.UserInput = "提出";
        await vm.SendMessageCommand.ExecuteAsync(null);
    }

    /// <summary>SaveSettings が接続タブを保存し、次回構築時の InitialBackend として復元されることを検証する</summary>
    [Fact(DisplayName = "SaveSettings が接続タブを保存し次回の InitialBackend に復元される")]
    public void SaveSettings_PersistsSelectedBackend_AndRestoresOnNextLoad()
    {
        var folder = Path.Combine(Path.GetTempPath(), "QuickERTests", Guid.NewGuid().ToString("N"));

        try
        {
            var vm = new MockGenerationDialogViewModel(
                new StubDiagramSource(NonEmptyDiagram()),
                new SyncUiDispatcher(),
                files: null,
                settingsStore: new AiSettingsStore(folder),
                apiKeyEngineFactory: null,
                codexEngineFactory: null,
                claudeCodeEngineFactory: null
            );

            // 保存が無い初回は API キータブが既定
            vm.Connection.InitialBackend.Should().Be(ErChatBackendKind.ApiKey);

            vm.Connection.SelectedBackend = ErChatBackendKind.ClaudeCode;
            vm.SaveSettings();

            var restored = new MockGenerationDialogViewModel(
                new StubDiagramSource(NonEmptyDiagram()),
                new SyncUiDispatcher(),
                files: null,
                settingsStore: new AiSettingsStore(folder),
                apiKeyEngineFactory: null,
                codexEngineFactory: null,
                claudeCodeEngineFactory: null
            );

            restored.Connection.InitialBackend.Should().Be(ErChatBackendKind.ClaudeCode);
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>モックフォルダ未指定では会話開始不可、指定（空フォルダ）で有効になることを検証する</summary>
    [Fact(DisplayName = "フォルダ未指定では会話開始不可・指定で可能")]
    public void CanStartConversation_RequiresMockFolder()
    {
        var (vm, _, baseFolder, mockFolder) = CreateVm(NonEmptyDiagram(), setMockFolder: false);

        try
        {
            // フォルダ未指定では開始不可
            vm.CanStartConversation.Should().BeFalse();
            vm.StartConversationCommand.CanExecute(null).Should().BeFalse();

            // 空フォルダを指定すると開始可能
            vm.MockFolder = mockFolder;
            vm.CanStartConversation.Should().BeTrue();
        }
        finally
        {
            Cleanup(baseFolder);
        }
    }

    /// <summary>空図では会話開始不可、非空なら可能（フォルダ指定済み）であることを検証する</summary>
    [Fact(DisplayName = "空図では会話開始不可・非空なら可能")]
    public void CanStartConversation_DependsOnDiagramEmptiness()
    {
        var (emptyVm, _, emptyBase, _) = CreateVm(new ErDiagram());

        try
        {
            emptyVm.IsDiagramEmpty.Should().BeTrue();
            emptyVm.CanStartConversation.Should().BeFalse();
        }
        finally
        {
            Cleanup(emptyBase);
        }

        var (vm, _, baseFolder, _) = CreateVm(NonEmptyDiagram());

        try
        {
            vm.IsDiagramEmpty.Should().BeFalse();
            vm.CanStartConversation.Should().BeTrue();
        }
        finally
        {
            Cleanup(baseFolder);
        }
    }

    /// <summary>既存モックフォルダを指定するだけで（会話前でも）サイドバーに画面が並ぶことを検証する</summary>
    [Fact(DisplayName = "既存モックフォルダ指定だけでサイドバーに画面が並ぶ")]
    public void SettingExistingMockFolder_PopulatesSidebar()
    {
        var (vm, _, baseFolder, mockFolder) = CreateVm(NonEmptyDiagram(), setMockFolder: false);

        try
        {
            SeedMockFolder(mockFolder);

            vm.MockFolder = mockFolder;

            vm.Screens.Should().ContainSingle();
            vm.Screens[0].File.Should().Be("OrderList.html");
            vm.Screens[0].Name.Should().Be("注文一覧");
            vm.HasScreens.Should().BeTrue();
        }
        finally
        {
            Cleanup(baseFolder);
        }
    }

    /// <summary>モックフォルダでない既存フォルダの指定では会話開始を抑止し、案内を出すことを検証する</summary>
    [Fact(DisplayName = "モックフォルダでない既存フォルダは開始抑止")]
    public void SettingNonMockFolderWithFiles_BlocksStart()
    {
        var (vm, _, baseFolder, mockFolder) = CreateVm(NonEmptyDiagram(), setMockFolder: false);

        try
        {
            // mock.json は無いが HTML がある既存フォルダ
            Directory.CreateDirectory(mockFolder);
            File.WriteAllText(Path.Combine(mockFolder, "index.html"), "<html></html>");

            vm.MockFolder = mockFolder;

            vm.CanStartConversation.Should().BeFalse();
            vm.StatusMessage.Should().Be(MockStrings.Mock_NotAMockFolder);
        }
        finally
        {
            Cleanup(baseFolder);
        }
    }

    /// <summary>破損した mock.json のフォルダ指定では、ステータスにエラーを出し会話開始を抑止することを検証する</summary>
    [Fact(DisplayName = "破損 mock.json ではステータスにエラー")]
    public void SettingCorruptMockFolder_ShowsErrorStatus()
    {
        var (vm, _, baseFolder, mockFolder) = CreateVm(NonEmptyDiagram(), setMockFolder: false);

        try
        {
            Directory.CreateDirectory(mockFolder);
            File.WriteAllText(Path.Combine(mockFolder, MockManifest.ManifestFileName), "{ broken");

            vm.MockFolder = mockFolder;

            vm.CanStartConversation.Should().BeFalse();
            vm.StatusMessage.Should().NotBeEmpty();
            vm.Screens.Should().BeEmpty();
        }
        finally
        {
            Cleanup(baseFolder);
        }
    }

    /// <summary>会話開始前は送信不可・開始後に入力ありで送信可能になることを検証する</summary>
    [Fact(DisplayName = "会話開始前は送信不可・開始後は入力ありで可能")]
    public void CanSendMessage_RequiresStartedConversationAndInput()
    {
        var (vm, _, baseFolder, _) = CreateVm(NonEmptyDiagram());

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
            Cleanup(baseFolder);
        }
    }

    /// <summary>新規フォルダで開始→初回送信で mock.json が作られ、初回プロンプトにスキーマ＋要望が含まれることを検証する</summary>
    [Fact(DisplayName = "新規フォルダ開始で mock.json 生成・初回プロンプトにスキーマ含有")]
    public async Task StartNew_CreatesManifest_AndFirstPromptHasSchema()
    {
        var (vm, engineBox, baseFolder, mockFolder) = CreateVm(NonEmptyDiagram());

        try
        {
            vm.StartConversationCommand.Execute(null);
            engineBox[0].Should().NotBeNull();

            // 会話開始（CreateNew）で mock.json が生成される
            File.Exists(Path.Combine(mockFolder, MockManifest.ManifestFileName)).Should().BeTrue();

            vm.UserInput = "モダンな配色にして";
            await vm.SendMessageCommand.ExecuteAsync(null);

            engineBox[0].SentPrompts.Should().ContainSingle();
            engineBox[0].SentPrompts[0].Should().Contain("Customer");
            engineBox[0].SentPrompts[0].Should().Contain("モダンな配色にして");
            vm.UserInput.Should().BeEmpty();
        }
        finally
        {
            Cleanup(baseFolder);
        }
    }

    /// <summary>既存モックフォルダで開始→初回送信が再開プロンプト（画面一覧を含む）になることを検証する</summary>
    [Fact(DisplayName = "既存モックフォルダ開始で再開プロンプト（画面一覧）")]
    public async Task StartResume_FirstPromptHasScreenList()
    {
        var (vm, engineBox, baseFolder, mockFolder) = CreateVm(
            NonEmptyDiagram(),
            setMockFolder: false
        );

        try
        {
            SeedMockFolder(mockFolder);
            vm.MockFolder = mockFolder;

            vm.StartConversationCommand.Execute(null);

            vm.UserInput = "続きをお願い";
            await vm.SendMessageCommand.ExecuteAsync(null);

            engineBox[0].SentPrompts.Should().ContainSingle();
            engineBox[0].SentPrompts[0].Should().Contain("OrderList.html");
            engineBox[0].SentPrompts[0].Should().Contain("続きをお願い");
        }
        finally
        {
            Cleanup(baseFolder);
        }
    }

    /// <summary>2 回目以降の送信はフィードバックとして（スキーマ添付なしで）送られることを検証する</summary>
    [Fact(DisplayName = "2 回目の送信はフィードバックとして送られる")]
    public async Task SecondSend_SendsFeedbackWithoutSchema()
    {
        var (vm, engineBox, baseFolder, _) = CreateVm(NonEmptyDiagram());

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
            Cleanup(baseFolder);
        }
    }

    /// <summary>ターン実行中は送信できないことを検証する</summary>
    [Fact(DisplayName = "ターン実行中は送信不可")]
    public void CanSendMessage_FalseDuringTurn()
    {
        var (vm, _, baseFolder, _) = CreateVm(NonEmptyDiagram());

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
            Cleanup(baseFolder);
        }
    }

    /// <summary>画面保存でサイドバーが更新され、プレビュー要求（実ファイルパス）が飛ぶことを検証する</summary>
    [Fact(DisplayName = "画面保存でサイドバー更新＋プレビュー要求")]
    public async Task ScreenSaved_UpdatesSidebar_AndRequestsPreview()
    {
        var (vm, engineBox, baseFolder, mockFolder) = CreateVm(NonEmptyDiagram());

        try
        {
            MockPreviewRequest? request = null;
            vm.PreviewRequested += (_, r) => request = r;

            vm.StartConversationCommand.Execute(null);
            engineBox[0].ScriptedToolCall = (
                MockFolderDesignTools.SaveScreenToolName,
                SaveScreenArgs()
            );

            vm.UserInput = "OrderList を作って";
            await vm.SendMessageCommand.ExecuteAsync(null);

            // サイドバーに画面が並ぶ
            vm.Screens.Should().ContainSingle();
            vm.Screens[0].File.Should().Be("OrderList.html");
            vm.SelectedScreen.Should().NotBeNull();

            // プレビュー要求はモックフォルダ内の実ファイルを指す
            request.Should().NotBeNull();
            request!.FilePath.Should().Be(Path.Combine(mockFolder, "OrderList.html"));
            request.Folder.Should().Be(mockFolder);

            // 実ファイルが書き出されている
            File.Exists(Path.Combine(mockFolder, "OrderList.html")).Should().BeTrue();
        }
        finally
        {
            Cleanup(baseFolder);
        }
    }

    /// <summary>単一 HTML 出力コマンドが、モックフォルダを結合したファイルを選択パスへ書き出すことを検証する</summary>
    [Fact(DisplayName = "単一 HTML 出力は選択パスへ結合 HTML を書き出す")]
    public void ExportBundle_WritesCombinedHtmlToPickedPath()
    {
        var baseFolder = Path.Combine(
            Path.GetTempPath(),
            "QuickERTests",
            Guid.NewGuid().ToString("N")
        );
        var mockFolder = Path.Combine(baseFolder, "mock");
        var outPath = Path.Combine(baseFolder, "out.html");
        SeedMockFolder(mockFolder);

        var files = new RecordingFileDialogService(new FileDialogResult(outPath, 1));
        var dialogs = new StubDialogService();

        var vm = new MockGenerationDialogViewModel(
            new StubDiagramSource(NonEmptyDiagram()),
            new SyncUiDispatcher(),
            files: files,
            settingsStore: new AiSettingsStore(Path.Combine(baseFolder, "settings")),
            apiKeyEngineFactory: null,
            codexEngineFactory: null,
            claudeCodeEngineFactory: null,
            dialogService: dialogs
        );
        vm.Connection.ApiProvider = AiProvider.Ollama;

        try
        {
            // 既存モックフォルダを開くだけで画面が並び、出力が有効になる（会話不要）
            vm.MockFolder = mockFolder;
            vm.CanExportBundle.Should().BeTrue();

            vm.ExportBundleCommand.Execute(null);

            files.LastInitialFileName.Should().Be("mock.html");
            File.Exists(outPath).Should().BeTrue();
            var written = File.ReadAllText(outPath);
            // 結合結果は画面本文を含む自己完結 HTML
            written.Should().Contain("顧客一覧");
            written.Should().Contain("data-screen");

            // 保存先パス付きの完了メッセージが通知される
            dialogs.InformationMessages.Should().ContainSingle().Which.Should().Contain(outPath);
        }
        finally
        {
            Cleanup(baseFolder);
        }
    }

    /// <summary>クリア（確認 OK）で会話・フォルダ選択・第2ステップ入力が初期状態へ戻ることを検証する</summary>
    [Fact(DisplayName = "クリアは確認後に画面全体を初期状態へ戻す")]
    public void Clear_ResetsEverything_WhenConfirmed()
    {
        var dialogs = new StubDialogService { ConfirmResult = true };
        var (vm, _, baseFolder, mockFolder) = CreateVm(
            NonEmptyDiagram(),
            setMockFolder: false,
            dialogs
        );
        SeedMockFolder(mockFolder);

        try
        {
            vm.MockFolder = mockFolder;
            vm.Screens.Should().NotBeEmpty();
            vm.UserInput = "入力途中のテキスト";
            vm.OutputFolder = Path.Combine(baseFolder, "out");
            vm.ProjectName = "ShopMock";
            vm.MockGenInstructions = "ダークテーマで";

            vm.ClearCommand.Execute(null);

            // 確認メッセージが表示されている
            dialogs.ConfirmMessages.Should().ContainSingle();

            // 会話まわり・フォルダ選択・サイドバー
            vm.MockFolder.Should().BeEmpty();
            vm.Screens.Should().BeEmpty();
            vm.HasScreens.Should().BeFalse();
            vm.Messages.Should().BeEmpty();
            vm.UserInput.Should().BeEmpty();

            // 第2ステップの入力
            vm.OutputFolder.Should().BeEmpty();
            vm.ProjectName.Should().Be("MockApp");
            vm.MockGenInstructions.Should().BeEmpty();

            // ディスク上のモックフォルダは削除されない
            MockFolderStore.IsMockFolder(mockFolder).Should().BeTrue();
        }
        finally
        {
            Cleanup(baseFolder);
        }
    }

    /// <summary>モックフォルダ未選択のときはクリアできないことを検証する</summary>
    [Fact(DisplayName = "モックフォルダ未選択ではクリア不可")]
    public void Clear_DisabledWithoutMockFolder()
    {
        var dialogs = new StubDialogService();
        var (vm, _, baseFolder, mockFolder) = CreateVm(
            NonEmptyDiagram(),
            setMockFolder: false,
            dialogs
        );

        try
        {
            // 未選択では押せない
            vm.CanClear.Should().BeFalse();
            vm.ClearCommand.CanExecute(null).Should().BeFalse();

            // フォルダを選択すると押せるようになる
            vm.MockFolder = mockFolder;
            vm.CanClear.Should().BeTrue();
            vm.ClearCommand.CanExecute(null).Should().BeTrue();

            // クリアで未選択へ戻ると再び押せなくなる
            vm.ClearCommand.Execute(null);
            vm.CanClear.Should().BeFalse();
            vm.ClearCommand.CanExecute(null).Should().BeFalse();
        }
        finally
        {
            Cleanup(baseFolder);
        }
    }

    /// <summary>クリアの確認をキャンセルすると何も変わらないことを検証する</summary>
    [Fact(DisplayName = "クリアの確認キャンセルでは何も変わらない")]
    public void Clear_DoesNothing_WhenCancelled()
    {
        var dialogs = new StubDialogService { ConfirmResult = false };
        var (vm, _, baseFolder, mockFolder) = CreateVm(
            NonEmptyDiagram(),
            setMockFolder: false,
            dialogs
        );
        SeedMockFolder(mockFolder);

        try
        {
            vm.MockFolder = mockFolder;
            vm.UserInput = "入力途中のテキスト";
            vm.ProjectName = "ShopMock";

            vm.ClearCommand.Execute(null);

            dialogs.ConfirmMessages.Should().ContainSingle();
            vm.MockFolder.Should().Be(mockFolder);
            vm.Screens.Should().NotBeEmpty();
            vm.UserInput.Should().Be("入力途中のテキスト");
            vm.ProjectName.Should().Be("ShopMock");
        }
        finally
        {
            Cleanup(baseFolder);
        }
    }

    /// <summary>単一 HTML 出力は画面が無いと不可であることを検証する</summary>
    [Fact(DisplayName = "画面が無いと単一 HTML 出力は不可")]
    public void CanExportBundle_FalseWithoutScreens()
    {
        var (vm, _, baseFolder, _) = CreateVm(NonEmptyDiagram());

        try
        {
            vm.HasScreens.Should().BeFalse();
            vm.CanExportBundle.Should().BeFalse();
            vm.ExportBundleCommand.CanExecute(null).Should().BeFalse();
        }
        finally
        {
            Cleanup(baseFolder);
        }
    }

    // ── 設計書出力（README.md）とその自動追従 ──

    /// <summary>save_screen ツールを 1 回再生する（会話開始済み前提）</summary>
    private static async Task SaveScreenViaEngine(
        MockGenerationDialogViewModel vm,
        FakeChatEngine engine,
        string file,
        string name
    )
    {
        engine.ScriptedToolCall = (
            MockFolderDesignTools.SaveScreenToolName,
            SaveScreenArgs(file, name)
        );
        vm.UserInput = "追加";
        await vm.SendMessageCommand.ExecuteAsync(null);
    }

    /// <summary>指定ツールを引数ディクショナリで 1 回再生する（会話開始済み前提）</summary>
    private static async Task RunToolViaEngine(
        MockGenerationDialogViewModel vm,
        FakeChatEngine engine,
        string toolName,
        Dictionary<string, object?> args
    )
    {
        engine.ScriptedToolCall = (toolName, JsonSerializer.Serialize(args));
        vm.UserInput = "実行";
        await vm.SendMessageCommand.ExecuteAsync(null);
    }

    private const string DesignDocSentinel = "SENTINEL-DESIGN-DOC";

    /// <summary>設計書出力ボタンで README.md をモックフォルダへ書き出し、内容が一致し情報ダイアログが出ることを検証する</summary>
    [Fact(DisplayName = "設計書出力ボタンで README.md を書き出し情報ダイアログを出す")]
    public void ExportDesignDoc_WritesReadme_AndShowsDialog()
    {
        var dialogs = new StubDialogService();
        var (vm, _, baseFolder, mockFolder) = CreateVm(
            NonEmptyDiagram(),
            setMockFolder: false,
            dialogs
        );
        SeedMockFolder(mockFolder);

        try
        {
            vm.MockFolder = mockFolder;
            // 単一 HTML 出力と同じ可否条件（画面が 1 つ以上）
            vm.ExportDesignDocCommand.CanExecute(null).Should().BeTrue();

            vm.ExportDesignDocCommand.Execute(null);

            var readmePath = Path.Combine(mockFolder, MockDesignDocExporter.FileName);
            File.Exists(readmePath).Should().BeTrue();

            // 内容は決定的エクスポータの出力（同フォルダを開き直しても同一）と一致する
            var expected = MockDesignDocExporter.Export(MockFolderStore.Open(mockFolder));
            File.ReadAllText(readmePath).Should().Be(expected);

            // 保存先パス付きの完了情報ダイアログが出る
            dialogs
                .InformationMessages.Should()
                .ContainSingle()
                .Which.Should()
                .Contain(readmePath);
        }
        finally
        {
            Cleanup(baseFolder);
        }
    }

    /// <summary>画面が無いと設計書出力ボタンが押せないことを検証する（単一 HTML 出力と同条件）</summary>
    [Fact(DisplayName = "画面が無いと設計書出力は不可")]
    public void CanExportDesignDoc_FalseWithoutScreens()
    {
        var (vm, _, baseFolder, _) = CreateVm(NonEmptyDiagram());

        try
        {
            vm.HasScreens.Should().BeFalse();
            vm.ExportDesignDocCommand.CanExecute(null).Should().BeFalse();
        }
        finally
        {
            Cleanup(baseFolder);
        }
    }

    /// <summary>README.md がある状態で画面保存すると、無音（ダイアログなし）で新しい画面を含む内容へ再生成されることを検証する</summary>
    [Fact(DisplayName = "README.md ありで画面保存すると無音で追従再生成される")]
    public async Task ScreenSaved_WithReadmePresent_RegeneratesSilently()
    {
        var dialogs = new StubDialogService();
        var (vm, engineBox, _, baseFolder, mockFolder) = CreateVmWithGenerator(
            NonEmptyDiagram(),
            dialogs
        );

        try
        {
            await SaveScreenOnClaudeCode(vm, engineBox, mockFolder);

            // 設計書を出力済みの状態を作る（センチネルを書いて、再生成で置き換わることを検出可能にする）
            var readmePath = Path.Combine(mockFolder, MockDesignDocExporter.FileName);
            File.WriteAllText(readmePath, DesignDocSentinel);
            var dialogsBefore = dialogs.InformationMessages.Count;

            // 新しい画面を保存する
            await SaveScreenViaEngine(vm, engineBox[0], "Detail.html", "注文詳細");

            // README は再生成され、センチネルが消え新しい画面名を含む
            var content = File.ReadAllText(readmePath);
            content.Should().NotContain(DesignDocSentinel);
            content.Should().Contain("注文詳細");

            // 無音＝情報ダイアログは増えない
            dialogs.InformationMessages.Count.Should().Be(dialogsBefore);
        }
        finally
        {
            Cleanup(baseFolder);
        }
    }

    /// <summary>README.md が無い状態で画面保存しても README.md が作られない（オプトイン維持）ことを検証する</summary>
    [Fact(DisplayName = "README.md が無ければ画面保存で作られない")]
    public async Task ScreenSaved_WithoutReadme_DoesNotCreateReadme()
    {
        var (vm, engineBox, _, baseFolder, mockFolder) = CreateVmWithGenerator(NonEmptyDiagram());

        try
        {
            await SaveScreenOnClaudeCode(vm, engineBox, mockFolder);

            // 設計書を出力していないので README は無いまま
            var readmePath = Path.Combine(mockFolder, MockDesignDocExporter.FileName);
            File.Exists(readmePath).Should().BeFalse();

            await SaveScreenViaEngine(vm, engineBox[0], "Detail.html", "注文詳細");

            // 追従は起きず README は作られない
            File.Exists(readmePath).Should().BeFalse();
        }
        finally
        {
            Cleanup(baseFolder);
        }
    }

    /// <summary>README.md がある状態で画面削除すると追従再生成されることを検証する</summary>
    [Fact(DisplayName = "README.md ありで画面削除すると追従再生成される")]
    public async Task ScreenRemoved_WithReadmePresent_Regenerates()
    {
        var (vm, engineBox, _, baseFolder, mockFolder) = CreateVmWithGenerator(NonEmptyDiagram());

        try
        {
            await SaveScreenOnClaudeCode(vm, engineBox, mockFolder);
            await SaveScreenViaEngine(vm, engineBox[0], "Detail.html", "注文詳細");

            var readmePath = Path.Combine(mockFolder, MockDesignDocExporter.FileName);
            File.WriteAllText(readmePath, DesignDocSentinel);

            // 画面を 1 つ削除する
            await RunToolViaEngine(
                vm,
                engineBox[0],
                MockFolderDesignTools.RemoveScreenToolName,
                new Dictionary<string, object?> { ["file"] = "Detail.html" }
            );

            // README は再生成され、削除画面（注文詳細）を含まない
            var content = File.ReadAllText(readmePath);
            content.Should().NotContain(DesignDocSentinel);
            content.Should().NotContain("注文詳細");
        }
        finally
        {
            Cleanup(baseFolder);
        }
    }

    /// <summary>共有スタイルシート保存では README.md を再生成しない（設計書の内容に影響しないため）ことを検証する</summary>
    [Fact(DisplayName = "save_stylesheet では README.md を再生成しない")]
    public async Task StylesheetSaved_WithReadmePresent_DoesNotRegenerate()
    {
        var (vm, engineBox, _, baseFolder, mockFolder) = CreateVmWithGenerator(NonEmptyDiagram());

        try
        {
            await SaveScreenOnClaudeCode(vm, engineBox, mockFolder);

            var readmePath = Path.Combine(mockFolder, MockDesignDocExporter.FileName);
            File.WriteAllText(readmePath, DesignDocSentinel);

            // 共有スタイルシートを保存する
            await RunToolViaEngine(
                vm,
                engineBox[0],
                MockFolderDesignTools.SaveStylesheetToolName,
                new Dictionary<string, object?> { ["css"] = "body{}", ["revision_note"] = "配色" }
            );

            // README は再生成されず、センチネルのまま
            File.ReadAllText(readmePath).Should().Be(DesignDocSentinel);
        }
        finally
        {
            Cleanup(baseFolder);
        }
    }

    /// <summary>
    /// 子 <see cref="ChatConnectionSettingsViewModel.ApiProvider"/> の変更で、親の
    /// <see cref="MockGenerationDialogViewModel.IsBackendReady"/> の PropertyChanged が発火することを検証する。
    /// </summary>
    [Fact(DisplayName = "Connection.ApiProvider 変更で親の IsBackendReady が通知される")]
    public void ConnectionApiProviderChange_RaisesIsBackendReadyOnParent()
    {
        var (vm, _, baseFolder, _) = CreateVm(NonEmptyDiagram());

        try
        {
            // 既定は Ollama（CreateVm）。一旦 OpenAI へ寄せてから Ollama へ戻して変化を作る
            vm.Connection.ApiProvider = AiProvider.OpenAI;

            var raised = new List<string>();
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is not null)
                {
                    raised.Add(e.PropertyName);
                }
            };

            vm.Connection.ApiProvider = AiProvider.Ollama;

            raised.Should().Contain(nameof(MockGenerationDialogViewModel.IsBackendReady));
        }
        finally
        {
            Cleanup(baseFolder);
        }
    }

    // ── 第2ステップ: WPF モックプロジェクト生成 ──

    /// <summary>画面あり・接続・claude/dotnet 検出・入力が揃うと生成可能になることを検証する</summary>
    [Fact(DisplayName = "第2ステップの有効条件がすべて揃うと生成可能")]
    public async Task CanGenerateMockProject_RequiresAllConditions()
    {
        var (vm, engineBox, generator, baseFolder, mockFolder) = CreateVmWithGenerator(
            NonEmptyDiagram()
        );

        try
        {
            generator.ClaudeAvailable = true;
            generator.DotnetAvailable = true;
            await vm.RefreshMockGenAvailabilityAsync();

            // 画面が無い状態では不可・理由は「画面を追加」
            vm.CanGenerateMockProject.Should().BeFalse();
            vm.MockGenDisabledReason.Should().Be(MockStrings.Mock_DisabledReason_NoScreens);

            await SaveScreenOnClaudeCode(vm, engineBox, mockFolder);
            vm.OutputFolder = Path.Combine(baseFolder, "out");
            vm.ProjectName = "AcmeMock";

            // ここまでで全条件が揃う
            vm.CanGenerateMockProject.Should().BeTrue();

            // 出力フォルダを空にすると不可・理由が出る
            vm.OutputFolder = string.Empty;
            vm.CanGenerateMockProject.Should().BeFalse();
            vm.MockGenDisabledReason.Should().Be(MockStrings.Mock_DisabledReason_OutputFolder);
        }
        finally
        {
            Cleanup(baseFolder);
        }
    }

    /// <summary>claude CLI 未検出では生成不可・理由が案内されることを検証する</summary>
    [Fact(DisplayName = "claude 未検出では生成不可")]
    public async Task CanGenerateMockProject_FalseWhenClaudeMissing()
    {
        var (vm, engineBox, generator, baseFolder, mockFolder) = CreateVmWithGenerator(
            NonEmptyDiagram()
        );

        try
        {
            generator.ClaudeAvailable = false;
            generator.DotnetAvailable = true;
            await vm.RefreshMockGenAvailabilityAsync();

            await SaveScreenOnClaudeCode(vm, engineBox, mockFolder);
            vm.OutputFolder = Path.Combine(baseFolder, "out");
            vm.ProjectName = "AcmeMock";

            vm.CanGenerateMockProject.Should().BeFalse();
            vm.MockGenDisabledReason.Should().Contain("claude");
        }
        finally
        {
            Cleanup(baseFolder);
        }
    }

    /// <summary>生成実行で状態遷移・進捗転送・成功時のフォルダ表示が起きることを検証する</summary>
    [Fact(DisplayName = "生成実行で進捗転送・成功でフォルダ表示")]
    public async Task GenerateMockProject_TransitionsAndForwardsProgress()
    {
        var (vm, engineBox, generator, baseFolder, mockFolder) = CreateVmWithGenerator(
            NonEmptyDiagram()
        );

        try
        {
            generator.ResultSuccess = true;
            await vm.RefreshMockGenAvailabilityAsync();
            await SaveScreenOnClaudeCode(vm, engineBox, mockFolder);
            var outFolder = Path.Combine(baseFolder, "out");
            vm.OutputFolder = outFolder;
            vm.ProjectName = "AcmeMock";
            vm.MockGenInstructions = "ダークテーマで実装して";

            await vm.GenerateMockProjectCommand.ExecuteAsync(null);

            generator.GenerateCallCount.Should().Be(1);
            generator.CapturedOutputFolder.Should().Be(outFolder);
            generator.CapturedProjectName.Should().Be("AcmeMock");
            // デザイン仕様としてモックフォルダのパスがそのまま渡る
            generator.CapturedMockFolder.Should().Be(mockFolder);
            // 追加指示が生成器へ渡る
            generator.CapturedInstructions.Should().Be("ダークテーマで実装して");
            vm.MockGenLog.Should().Contain("進捗: 生成中");
            vm.IsMockGenInProgress.Should().BeFalse();
            vm.MockGenCompleted.Should().BeTrue();
            vm.MockGenSucceeded.Should().BeTrue();
            vm.ShowOpenFolder.Should().BeTrue();
        }
        finally
        {
            Cleanup(baseFolder);
        }
    }

    /// <summary>生成失敗時はフォルダ表示せず完了フラグのみ立つことを検証する</summary>
    [Fact(DisplayName = "生成失敗ではフォルダを開くボタンを出さない")]
    public async Task GenerateMockProject_FailureHidesOpenFolder()
    {
        var (vm, engineBox, generator, baseFolder, mockFolder) = CreateVmWithGenerator(
            NonEmptyDiagram()
        );

        try
        {
            generator.ResultSuccess = false;
            await vm.RefreshMockGenAvailabilityAsync();
            await SaveScreenOnClaudeCode(vm, engineBox, mockFolder);
            vm.OutputFolder = Path.Combine(baseFolder, "out");
            vm.ProjectName = "AcmeMock";

            await vm.GenerateMockProjectCommand.ExecuteAsync(null);

            vm.MockGenCompleted.Should().BeTrue();
            vm.MockGenSucceeded.Should().BeFalse();
            vm.ShowOpenFolder.Should().BeFalse();
            // 追加指示が空のときは null が渡る（空白→null の正規化）
            generator.CapturedInstructions.Should().BeNull();
        }
        finally
        {
            Cleanup(baseFolder);
        }
    }

    /// <summary>生成成功時は完了を情報ダイアログで明示する（出力フォルダ入り）ことを検証する</summary>
    [Fact(DisplayName = "生成成功で完了情報ダイアログを出す")]
    public async Task GenerateMockProject_Success_ShowsInformationDialog()
    {
        var dialogs = new StubDialogService();
        var (vm, engineBox, generator, baseFolder, mockFolder) = CreateVmWithGenerator(
            NonEmptyDiagram(),
            dialogs
        );

        try
        {
            generator.ResultSuccess = true;
            await vm.RefreshMockGenAvailabilityAsync();
            await SaveScreenOnClaudeCode(vm, engineBox, mockFolder);
            var outFolder = Path.Combine(baseFolder, "out");
            vm.OutputFolder = outFolder;
            vm.ProjectName = "AcmeMock";

            await vm.GenerateMockProjectCommand.ExecuteAsync(null);

            // 成功は ShowInformation を 1 回・出力フォルダを含む・エラーは出ない
            dialogs.InformationMessages.Should().ContainSingle().Which.Should().Contain(outFolder);
            dialogs.ErrorMessages.Should().BeEmpty();
        }
        finally
        {
            Cleanup(baseFolder);
        }
    }

    /// <summary>生成失敗時はエラーダイアログでログパスを添えて詳細確認へ誘導することを検証する</summary>
    [Fact(DisplayName = "生成失敗でエラーダイアログ（ログパス含む）を出す")]
    public async Task GenerateMockProject_Failure_ShowsErrorDialogWithLogPath()
    {
        var dialogs = new StubDialogService();
        var (vm, engineBox, generator, baseFolder, mockFolder) = CreateVmWithGenerator(
            NonEmptyDiagram(),
            dialogs
        );

        try
        {
            generator.ResultSuccess = false;
            await vm.RefreshMockGenAvailabilityAsync();
            await SaveScreenOnClaudeCode(vm, engineBox, mockFolder);
            vm.OutputFolder = Path.Combine(baseFolder, "out");
            vm.ProjectName = "AcmeMock";

            await vm.GenerateMockProjectCommand.ExecuteAsync(null);

            // 失敗は ShowError を 1 回・ログパスを含む・情報ダイアログは出ない
            dialogs
                .ErrorMessages.Should()
                .ContainSingle()
                .Which.Should()
                .Contain("quickr-mock-generation.log");
            dialogs.InformationMessages.Should().BeEmpty();
        }
        finally
        {
            Cleanup(baseFolder);
        }
    }

    /// <summary>ユーザー自身の中断による終了時は、情報・エラーいずれのダイアログも出さないことを検証する</summary>
    [Fact(DisplayName = "中断による終了ではダイアログを出さない")]
    public async Task GenerateMockProject_Interrupted_ShowsNoDialog()
    {
        var dialogs = new StubDialogService();
        var (vm, engineBox, generator, baseFolder, mockFolder) = CreateVmWithGenerator(
            NonEmptyDiagram(),
            dialogs
        );

        try
        {
            generator.ResultSuccess = false;
            generator.ResultInterrupted = true;
            await vm.RefreshMockGenAvailabilityAsync();
            await SaveScreenOnClaudeCode(vm, engineBox, mockFolder);
            vm.OutputFolder = Path.Combine(baseFolder, "out");
            vm.ProjectName = "AcmeMock";

            await vm.GenerateMockProjectCommand.ExecuteAsync(null);

            // 中断ではどちらのダイアログも出ない
            dialogs.InformationMessages.Should().BeEmpty();
            dialogs.ErrorMessages.Should().BeEmpty();
        }
        finally
        {
            Cleanup(baseFolder);
        }
    }

    /// <summary>API キー接続では、キー未入力なら無効（理由＝ApiKeyNotReady・注記フラグが立つ）／キー不要の Ollama なら有効になることを検証する</summary>
    [Fact(DisplayName = "API キー接続はキー未入力で無効・Ollama で有効")]
    public async Task CanGenerateMockProject_ApiKeyBackend_NeedsKeyThenAllows()
    {
        var (vm, engineBox, generator, baseFolder, mockFolder) = CreateVmWithGenerator(
            NonEmptyDiagram()
        );

        try
        {
            await vm.RefreshMockGenAvailabilityAsync();
            await SaveScreenOnClaudeCode(vm, engineBox, mockFolder);
            vm.OutputFolder = Path.Combine(baseFolder, "out");
            vm.ProjectName = "AcmeMock";
            vm.CanGenerateMockProject.Should().BeTrue();

            // API キーへ切替（既定プロバイダ=OpenAI・キー未入力）→ 無効・理由は ApiKeyNotReady・注記フラグが立つ
            vm.Connection.SelectedBackend = ErChatBackendKind.ApiKey;
            vm.IsApiKeyMockGenBackend.Should().BeTrue();
            vm.CanGenerateMockProject.Should().BeFalse();
            vm.MockGenDisabledReason.Should().Be(MockStrings.Mock_DisabledReason_ApiKeyNotReady);

            // Ollama（キー不要）にすると生成可能になる
            vm.Connection.ApiProvider = AiProvider.Ollama;
            vm.CanGenerateMockProject.Should().BeTrue();
        }
        finally
        {
            Cleanup(baseFolder);
        }
    }

    /// <summary>API キーバックエンドで生成を実行すると、backend=ApiKey とモデルが生成器へ渡ることを検証する</summary>
    [Fact(DisplayName = "API キーバックエンドで生成すると backend=ApiKey が渡る")]
    public async Task GenerateMockProject_ApiKeyBackend_PassesBackend()
    {
        var (vm, engineBox, generator, baseFolder, mockFolder) = CreateVmWithGenerator(
            NonEmptyDiagram()
        );

        try
        {
            generator.DotnetAvailable = true;
            await vm.RefreshMockGenAvailabilityAsync();
            await SaveScreenOnClaudeCode(vm, engineBox, mockFolder);

            // API キー（Ollama＝キー不要）へ切替
            vm.Connection.SelectedBackend = ErChatBackendKind.ApiKey;
            vm.Connection.ApiProvider = AiProvider.Ollama;
            vm.Connection.ApiModel = "llama3";
            vm.OutputFolder = Path.Combine(baseFolder, "out");
            vm.ProjectName = "AcmeMock";
            vm.CanGenerateMockProject.Should().BeTrue();

            await vm.GenerateMockProjectCommand.ExecuteAsync(null);

            generator.CapturedBackend.Should().Be(ErChatBackendKind.ApiKey);
            generator.CapturedModel.Should().Be("llama3");
            // API キーはプロバイダーを渡さない（エンジンファクトリが閉じ込める）
            generator.CapturedModelProvider.Should().BeEmpty();
        }
        finally
        {
            Cleanup(baseFolder);
        }
    }

    /// <summary>Codex バックエンドで認証プローブ結果（_codexReady）が立つと生成可能になり、Codex のモデル／プロバイダが渡ることを検証する</summary>
    [Fact(
        DisplayName = "Codex バックエンドで readiness が立つと生成可能・Codex のモデル/プロバイダが渡る"
    )]
    public async Task CanGenerateMockProject_CodexBackend_ReadyAndPassesModel()
    {
        var (vm, engineBox, generator, baseFolder, mockFolder) = CreateVmWithGenerator(
            NonEmptyDiagram()
        );

        try
        {
            generator.DotnetAvailable = true;
            await vm.RefreshMockGenAvailabilityAsync();

            // まず画面を 1 つ用意する（Claude Code 経由で保存）
            await SaveScreenOnClaudeCode(vm, engineBox, mockFolder);

            // Codex へ切替＋認証プローブ結果（ready）を外部から反映する
            vm.Connection.SelectedBackend = ErChatBackendKind.Codex;
            vm.ApplyCodexReadiness(true, "ログイン済み", ConnectionHealth.Ready);
            vm.Connection.CodexModelProvider = "openai";
            vm.Connection.CodexModel = "gpt-5-codex";
            vm.OutputFolder = Path.Combine(baseFolder, "out");
            vm.ProjectName = "AcmeMock";

            vm.CanGenerateMockProject.Should().BeTrue();

            await vm.GenerateMockProjectCommand.ExecuteAsync(null);

            // Codex のバックエンド・モデル・プロバイダが生成器へ渡る
            generator.CapturedBackend.Should().Be(ErChatBackendKind.Codex);
            generator.CapturedModel.Should().Be("gpt-5-codex");
            generator.CapturedModelProvider.Should().Be("openai");
        }
        finally
        {
            Cleanup(baseFolder);
        }
    }

    /// <summary>Codex バックエンドで認証プローブ未成立のときは生成不可・理由が「Codex 未接続」になることを検証する</summary>
    [Fact(DisplayName = "Codex 未接続では生成不可・理由が Codex 未接続")]
    public async Task CanGenerateMockProject_CodexNotReady_ShowsReason()
    {
        var (vm, engineBox, generator, baseFolder, mockFolder) = CreateVmWithGenerator(
            NonEmptyDiagram()
        );

        try
        {
            await vm.RefreshMockGenAvailabilityAsync();
            await SaveScreenOnClaudeCode(vm, engineBox, mockFolder);
            vm.OutputFolder = Path.Combine(baseFolder, "out");
            vm.ProjectName = "AcmeMock";

            // Codex へ切替（_codexReady は未成立のまま）
            vm.Connection.SelectedBackend = ErChatBackendKind.Codex;

            vm.CanGenerateMockProject.Should().BeFalse();
            vm.MockGenDisabledReason.Should().Be(MockStrings.Mock_DisabledReason_CodexNotReady);
        }
        finally
        {
            Cleanup(baseFolder);
        }
    }

    /// <summary>既定で MockProjectTargets が 2 件（Blazor, WPF）・SelectedMockProjectTarget が Blazor であることを検証する</summary>
    [Fact(DisplayName = "既定のターゲットは Blazor・候補は Blazor/WPF の 2 件")]
    public void MockProjectTargets_DefaultsToBlazor_WithTwoCandidates()
    {
        var (vm, _, _, baseFolder, _) = CreateVmWithGenerator(NonEmptyDiagram());

        try
        {
            vm.MockProjectTargets.Should().Equal(MockProjectTarget.Blazor, MockProjectTarget.Wpf);
            vm.SelectedMockProjectTarget.Should().Be(MockProjectTarget.Blazor);
        }
        finally
        {
            Cleanup(baseFolder);
        }
    }

    /// <summary>選択中のターゲットが生成器へ渡ることを検証する（既定の Blazor と、選択後の WPF）</summary>
    [Fact(DisplayName = "選択したターゲットが生成器へ渡る（既定 Blazor・選択後 WPF）")]
    public async Task GenerateMockProject_PassesSelectedTarget()
    {
        var (vm, engineBox, generator, baseFolder, mockFolder) = CreateVmWithGenerator(
            NonEmptyDiagram()
        );

        try
        {
            generator.ResultSuccess = true;
            await vm.RefreshMockGenAvailabilityAsync();
            await SaveScreenOnClaudeCode(vm, engineBox, mockFolder);
            vm.OutputFolder = Path.Combine(baseFolder, "out");
            vm.ProjectName = "AcmeMock";

            // 既定（Blazor）のまま生成すると Blazor が渡る
            await vm.GenerateMockProjectCommand.ExecuteAsync(null);
            generator.CapturedTarget.Should().Be(MockProjectTarget.Blazor);

            // WPF を選択して再生成すると WPF が渡る
            vm.SelectedMockProjectTarget = MockProjectTarget.Wpf;
            await vm.GenerateMockProjectCommand.ExecuteAsync(null);
            generator.CapturedTarget.Should().Be(MockProjectTarget.Wpf);
        }
        finally
        {
            Cleanup(baseFolder);
        }
    }

    /// <summary>クリア（確認 OK）で SelectedMockProjectTarget が既定（Blazor）へ戻ることを検証する</summary>
    [Fact(DisplayName = "クリアでターゲットが既定（Blazor）へ戻る")]
    public void Clear_ResetsSelectedTargetToBlazor()
    {
        var dialogs = new StubDialogService { ConfirmResult = true };
        var (vm, _, _, baseFolder, mockFolder) = CreateVmWithGenerator(NonEmptyDiagram(), dialogs);
        SeedMockFolder(mockFolder);

        try
        {
            vm.MockFolder = mockFolder;
            vm.SelectedMockProjectTarget = MockProjectTarget.Wpf;

            vm.ClearCommand.Execute(null);

            vm.SelectedMockProjectTarget.Should().Be(MockProjectTarget.Blazor);
        }
        finally
        {
            Cleanup(baseFolder);
        }
    }

    /// <summary>
    /// Ollama＋モデル設定で成功ターンが完了すると、使用モデルが候補・JSON 履歴へ記録されることを
    /// エンドツーエンド（フェイクエンジンの成功 TurnCompleted 経由）で検証する。
    /// </summary>
    [Fact(DisplayName = "Ollama 成功ターンで使用モデルが候補・履歴へ記録される")]
    public async Task SuccessfulOllamaTurn_RecordsModelToHistory()
    {
        var (vm, _, baseFolder, _) = CreateVm(NonEmptyDiagram());

        try
        {
            // CreateVm で ApiProvider=Ollama・バックエンドは既定の API キー
            vm.Connection.ApiModel = "qwen3.6:35b";

            vm.StartConversationCommand.Execute(null);
            vm.UserInput = "管理画面を作って";
            await vm.SendMessageCommand.ExecuteAsync(null);

            // フェイクエンジンが成功 TurnCompleted を発火し、記録が走る
            vm.Connection.ApiModelCandidates.Select(c => c.Name).Should().Contain("qwen3.6:35b");

            var reloaded = new AiSettingsStore(Path.Combine(baseFolder, "settings"))
                .Load()
                .ApiModelHistory;
            reloaded.ModelsFor("ollama").Should().Contain("qwen3.6:35b");
        }
        finally
        {
            Cleanup(baseFolder);
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

    /// <summary>Claude Code バックエンドでは添付範囲が全種別になることを検証する</summary>
    [Fact(DisplayName = "Claude Code では添付範囲が全種別")]
    public void AttachmentSupport_ClaudeCode_IsAllKinds()
    {
        var (vm, _, baseFolder, _) = CreateVm(NonEmptyDiagram());

        try
        {
            vm.Connection.SelectedBackend = ErChatBackendKind.ClaudeCode;
            vm.Attachments.Support.Should()
                .Be(
                    AttachmentSupport.Images
                        | AttachmentSupport.Pdf
                        | AttachmentSupport.Text
                        | AttachmentSupport.Binary
                );

            // Codex はエンジンが添付非対応
            vm.Connection.SelectedBackend = ErChatBackendKind.Codex;
            vm.Attachments.Support.Should().Be(AttachmentSupport.None);
        }
        finally
        {
            Cleanup(baseFolder);
        }
    }

    /// <summary>添付付き送信がセッション経由でエンジンへ渡り、送信後にチップがクリアされることを検証する</summary>
    [Fact(DisplayName = "添付付き送信がエンジンへ渡りクリアされる")]
    public async Task SendWithAttachment_PassesToEngine_AndClears()
    {
        // Claude Code バックエンドのエンジンをフェイクへ差し替える（engineBox で捕捉する）
        var (vm, engineBox, _, baseFolder, mockFolder) = CreateVmWithGenerator(NonEmptyDiagram());

        try
        {
            vm.Connection.SelectedBackend = ErChatBackendKind.ClaudeCode;
            vm.ApplyClaudeCodeReadiness(true, "ログイン済み", ConnectionHealth.Ready, string.Empty);
            vm.MockFolder = mockFolder;
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
            Cleanup(baseFolder);
        }
    }

    /// <summary>バックエンドを添付非対応（Codex）へ切り替えると Pending がクリアされることを検証する</summary>
    [Fact(DisplayName = "非対応バックエンドへ切替で添付をクリア")]
    public void SwitchToUnsupportedBackend_ClearsAttachments()
    {
        var (vm, _, baseFolder, _) = CreateVm(NonEmptyDiagram());

        try
        {
            vm.Connection.SelectedBackend = ErChatBackendKind.ClaudeCode;
            vm.Attachments.AddClipboardImage(PngBytes(), DateTime.Now);
            vm.Attachments.Items.Should().HaveCount(1);

            vm.Connection.SelectedBackend = ErChatBackendKind.Codex;

            vm.Attachments.Items.Should().BeEmpty();
        }
        finally
        {
            Cleanup(baseFolder);
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
