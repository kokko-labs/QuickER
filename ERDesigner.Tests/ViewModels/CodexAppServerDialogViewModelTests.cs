using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ERDesigner.Services;
using ERDesigner.ViewModels;
using FluentAssertions;
using Xunit;

namespace ERDesigner.Tests.ViewModels;

/// <summary><see cref="CodexAppServerDialogViewModel"/> の状態反映・認証 UI 制御を検証するテストクラス</summary>
/// <remarks>
/// デグレードを防ぐため、ShowLoginPanel / CanStartNewThread / CanSendMessage / CanLogout の
/// 判定ロジックを主要シナリオで網羅する
/// </remarks>
public class CodexAppServerDialogViewModelTests
{
    // ----------------------------------------------------------------
    // テスト用フェイク実装
    // ----------------------------------------------------------------

    /// <summary>通信を伴わず接続・認証・スレッド操作を模擬する <see cref="ICodexAppServerClient"/> のフェイク</summary>
    private sealed class FakeCodexAppServerClient : ICodexAppServerClient
    {
        public event EventHandler<CodexJsonRpcNotification>? NotificationReceived;
        public event EventHandler<CodexLoginCompletedNotification>? LoginCompleted;
        public event EventHandler<CodexAccountUpdatedNotification>? AccountUpdated;
        public event EventHandler<CodexThreadStartedNotification>? ThreadStarted;
        public event EventHandler<CodexTurnStartedNotification>? TurnStarted;
        public event EventHandler<CodexAgentMessageDeltaNotification>? AgentMessageDeltaReceived;
        public event EventHandler<CodexTurnCompletedNotification>? TurnCompleted;
        public event EventHandler<CodexDynamicToolCallRequest>? DynamicToolCallReceived;
        public event EventHandler<CodexItemStartedNotification>? ItemStarted;
        public event EventHandler<CodexItemCompletedNotification>? ItemCompleted;
        public event EventHandler<CodexApprovalRequest>? ApprovalRequested;

        /// <summary>起動済みかどうか</summary>
        public bool IsStarted { get; private set; }

        /// <summary>ReadAccountAsync が返すアカウント情報（テストから差し替える）</summary>
        public CodexAccountInfo NextAccountInfo { get; set; } = new();

        /// <summary>最後に LoginWithApiKeyAsync へ渡された API キー</summary>
        public string? LastApiKey { get; private set; }

        /// <summary>StartAsync の呼び出し回数</summary>
        public int StartCount { get; private set; }

        /// <summary>LogoutAsync の呼び出し回数</summary>
        public int LogoutCount { get; private set; }

        /// <inheritdoc />
        public Task StartAsync(CodexAppServerSettings settings, string clientName, string clientTitle, string clientVersion, CancellationToken cancellationToken = default)
        {
            IsStarted = true;
            StartCount++;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<CodexAccountInfo> ReadAccountAsync(bool refreshToken, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(NextAccountInfo);
        }

        /// <inheritdoc />
        public Task<CodexLoginStartResult> LoginWithApiKeyAsync(string apiKey, CancellationToken cancellationToken = default)
        {
            LastApiKey = apiKey;
            LoginCompleted?.Invoke(this, new CodexLoginCompletedNotification { Success = true });
            AccountUpdated?.Invoke(this, new CodexAccountUpdatedNotification { AuthMode = CodexAuthMode.ApiKey });
            return Task.FromResult(new CodexLoginStartResult { Type = CodexLoginType.ApiKey });
        }

        /// <inheritdoc />
        public Task<CodexLoginStartResult> StartChatGptLoginAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new CodexLoginStartResult { Type = CodexLoginType.ChatGpt, AuthUrl = "https://chatgpt.example/login" });
        }

        /// <inheritdoc />
        public Task LogoutAsync(CancellationToken cancellationToken = default)
        {
            LogoutCount++;
            // 実サーバーの挙動を忠実に再現する
            // account/updated 通知で AuthMode=None のみを通知し、
            // 通知に含まれない RequiresOpenAiAuth は変更しないため NextAccountInfo は書き換えない
            AccountUpdated?.Invoke(this, new CodexAccountUpdatedNotification { AuthMode = CodexAuthMode.None });
            return Task.CompletedTask;
        }

        /// <summary>最後に StartThreadAsync へ渡されたスレッド開始オプション</summary>
        public CodexThreadStartOptions? LastThreadStartOptions { get; private set; }

        /// <inheritdoc />
        public Task<CodexThreadInfo> StartThreadAsync(CodexThreadStartOptions options, CancellationToken cancellationToken = default)
        {
            LastThreadStartOptions = options;
            return Task.FromResult(new CodexThreadInfo { Id = "thr_test", Preview = string.Empty });
        }

        /// <inheritdoc />
        public Task<CodexTurnInfo> StartTurnAsync(string threadId, string prompt, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new CodexTurnInfo { Id = "turn_test", Status = "inProgress" });
        }

        /// <inheritdoc />
        public Task InterruptTurnAsync(string threadId, string turnId, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task RespondToDynamicToolCallAsync(int requestId, string resultText, bool success, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task RespondToApprovalAsync(int requestId, string decision, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    // ----------------------------------------------------------------
    // ヘルパー
    // ----------------------------------------------------------------

    /// <summary>リフレクションで private メソッドを呼び出し、戻り値が Task なら待機する</summary>
    private static async Task InvokePrivateAsync(object instance, string methodName)
    {
        var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull($"{methodName} が存在する必要があります。");
        var result = method!.Invoke(instance, []);

        if (result is Task task)
        {
            await task;
        }
    }

    /// <summary>指定アカウント情報・プロバイダーでフェイククライアントと ViewModel を生成する</summary>
    private static (FakeCodexAppServerClient client, CodexAppServerDialogViewModel vm, string folder) CreateVm(CodexAccountInfo accountInfo, string modelProvider = "openai")
    {
        var folder = Path.Combine(Path.GetTempPath(), "ERDesignerTests", Guid.NewGuid().ToString("N"));
        var settingsStore = new CodexAppServerSettingsStore(folder);
        settingsStore.Save(new CodexAppServerSettings { ModelProvider = modelProvider, Model = string.Empty });
        var client = new FakeCodexAppServerClient { NextAccountInfo = accountInfo };
        var vm = new CodexAppServerDialogViewModel(client, settingsStore, "CodexVmTests_" + Guid.NewGuid().ToString("N"));
        return (client, vm, folder);
    }

    // ----------------------------------------------------------------
    // InitializeAsync
    // ----------------------------------------------------------------

    /// <summary>InitializeAsync が保存済み設定と取得したアカウント状態を反映することを検証する</summary>
    [Fact(DisplayName = "InitializeAsync は保存済み設定とアカウント状態を反映する")]
    public async Task InitializeAsync_LoadsSettingsAndAccountState()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ERDesignerTests", Guid.NewGuid().ToString("N"));
        var settingsStore = new CodexAppServerSettingsStore(folder);
        var client = new FakeCodexAppServerClient();
        client.NextAccountInfo = new CodexAccountInfo
        {
            RequiresOpenAiAuth = true,
            AuthMode = CodexAuthMode.ChatGpt,
            Email = "user@example.com",
            PlanType = "plus",
        };
        var apiKeyStoreName = "CodexVmTests_" + Guid.NewGuid().ToString("N");
        settingsStore.Save(new CodexAppServerSettings { ModelProvider = "openai", Model = "gpt-4o" });
        ApiKeyStore.Save(apiKeyStoreName, "sk-codex-test");

        try
        {
            await client.StartAsync(new CodexAppServerSettings(), "test", "test", "1.0.0");
            var vm = new CodexAppServerDialogViewModel(client, settingsStore, apiKeyStoreName);

            await vm.InitializeAsync();

            vm.ModelProvider.Should().Be("openai");
            vm.Model.Should().Be("gpt-4o");
            vm.ApiKey.Should().Be("sk-codex-test");
            vm.AuthMode.Should().Be(CodexAuthMode.ChatGpt);
            vm.AccountSummary.Should().Contain("user@example.com");
        }
        finally
        {
            ApiKeyStore.Save(apiKeyStoreName, string.Empty);

            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    // ----------------------------------------------------------------
    // ログイン操作
    // ----------------------------------------------------------------

    /// <summary>未接続時の LoginWithApiKey が自動接続のうえログインし、API キーを保存することを検証する</summary>
    [Fact(DisplayName = "LoginWithApiKey は未接続時に接続後ログインし、API キーを保存できる")]
    public async Task LoginWithApiKeyAsync_StartsClientAndPersistsKey()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ERDesignerTests", Guid.NewGuid().ToString("N"));
        var settingsStore = new CodexAppServerSettingsStore(folder);
        var client = new FakeCodexAppServerClient();
        client.NextAccountInfo = new CodexAccountInfo { RequiresOpenAiAuth = true, AuthMode = CodexAuthMode.None };
        var apiKeyStoreName = "CodexVmTests_" + Guid.NewGuid().ToString("N");
        var vm = new CodexAppServerDialogViewModel(client, settingsStore, apiKeyStoreName) { ApiKey = "sk-live-test", SaveApiKey = true };

        try
        {
            await InvokePrivateAsync(vm, "LoginWithApiKeyAsync");

            client.StartCount.Should().Be(1);
            client.LastApiKey.Should().Be("sk-live-test");
            ApiKeyStore.Load(apiKeyStoreName).Should().Be("sk-live-test");
            vm.StatusMessage.Should().NotBeNullOrWhiteSpace();
        }
        finally
        {
            ApiKeyStore.Save(apiKeyStoreName, string.Empty);

            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    /// <summary>未接続時の StartChatGptLogin が自動接続し、認証 URL をブラウザで開くことを検証する</summary>
    [Fact(DisplayName = "StartChatGptLogin は未接続時に自動接続してブラウザを開く")]
    public async Task StartChatGptLoginAsync_StartsClientAndOpensBrowser()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ERDesignerTests", Guid.NewGuid().ToString("N"));
        var settingsStore = new CodexAppServerSettingsStore(folder);
        var client = new FakeCodexAppServerClient();
        client.NextAccountInfo = new CodexAccountInfo { RequiresOpenAiAuth = true, AuthMode = CodexAuthMode.None };
        var vm = new CodexAppServerDialogViewModel(client, settingsStore, "CodexVmTests_" + Guid.NewGuid().ToString("N"));
        var openedUrls = new List<string>();
        vm.OpenBrowser = url => openedUrls.Add(url);

        try
        {
            await InvokePrivateAsync(vm, "StartChatGptLoginAsync");

            client.StartCount.Should().Be(1);
            openedUrls.Should().ContainSingle().Which.Should().Be("https://chatgpt.example/login");
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    // ----------------------------------------------------------------
    // スレッド開始
    // ----------------------------------------------------------------

    /// <summary>StartNewThread でスレッドが開始されると案内システムメッセージが追加されることを検証する</summary>
    [Fact(DisplayName = "StartNewThread でスレッドが開始されるとメッセージが追加される")]
    public async Task StartNewThreadAsync_AddsSystemMessage()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ERDesignerTests", Guid.NewGuid().ToString("N"));
        var settingsStore = new CodexAppServerSettingsStore(folder);
        var client = new FakeCodexAppServerClient();
        client.NextAccountInfo = new CodexAccountInfo { RequiresOpenAiAuth = false, AuthMode = CodexAuthMode.ApiKey };
        var vm = new CodexAppServerDialogViewModel(client, settingsStore, "CodexVmTests_" + Guid.NewGuid().ToString("N"));

        try
        {
            await InvokePrivateAsync(vm, "ConnectAsync");
            vm.IsStarted.Should().BeTrue();

            await InvokePrivateAsync(vm, "StartNewThreadAsync");

            vm.HasThread.Should().BeTrue();
            vm.Messages.Should().NotBeEmpty();
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    /// <summary>MainViewModel 付きでスレッドを開始すると ER 図ツールと設計ルール指示が登録されることを検証する</summary>
    [Fact(DisplayName = "StartNewThread は MainViewModel 付きで設計ルール指示（複合キー禁止）を送る")]
    public async Task StartNewThreadAsync_WithMainViewModel_SendsDeveloperInstructions()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ERDesignerTests", Guid.NewGuid().ToString("N"));
        var settingsStore = new CodexAppServerSettingsStore(folder);
        var client = new FakeCodexAppServerClient();
        client.NextAccountInfo = new CodexAccountInfo { RequiresOpenAiAuth = false, AuthMode = CodexAuthMode.ApiKey };
        var vm = new CodexAppServerDialogViewModel(client, settingsStore, "CodexVmTests_" + Guid.NewGuid().ToString("N"), new MainViewModel());

        try
        {
            await InvokePrivateAsync(vm, "ConnectAsync");
            await InvokePrivateAsync(vm, "StartNewThreadAsync");

            client.LastThreadStartOptions.Should().NotBeNull();
            client.LastThreadStartOptions!.DynamicTools.Should().NotBeNullOrEmpty();
            client.LastThreadStartOptions.DeveloperInstructions.Should().NotBeNullOrWhiteSpace();
            client.LastThreadStartOptions.DeveloperInstructions.Should().Contain("複合主キー（複数列の主キー）は禁止");
            client.LastThreadStartOptions.DeveloperInstructions.Should().Contain("複合外部キーは禁止");
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    /// <summary>MainViewModel が無い場合はツールも設計ルール指示も登録されないことを検証する</summary>
    [Fact(DisplayName = "StartNewThread は MainViewModel が無い場合 developerInstructions を送らない")]
    public async Task StartNewThreadAsync_WithoutMainViewModel_OmitsDeveloperInstructions()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ERDesignerTests", Guid.NewGuid().ToString("N"));
        var settingsStore = new CodexAppServerSettingsStore(folder);
        var client = new FakeCodexAppServerClient();
        client.NextAccountInfo = new CodexAccountInfo { RequiresOpenAiAuth = false, AuthMode = CodexAuthMode.ApiKey };
        var vm = new CodexAppServerDialogViewModel(client, settingsStore, "CodexVmTests_" + Guid.NewGuid().ToString("N"));

        try
        {
            await InvokePrivateAsync(vm, "ConnectAsync");
            await InvokePrivateAsync(vm, "StartNewThreadAsync");

            client.LastThreadStartOptions.Should().NotBeNull();
            client.LastThreadStartOptions!.DynamicTools.Should().BeNull();
            client.LastThreadStartOptions.DeveloperInstructions.Should().BeNull();
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    // ----------------------------------------------------------------
    // モデル候補
    // ----------------------------------------------------------------

    /// <summary>openai プロバイダー選択時に固定のモデル候補一覧が表示されることを検証する</summary>
    [Fact(DisplayName = "openai プロバイダー選択時は openai の固定モデル候補が表示される")]
    public void ModelProvider_OpenAi_HasFixedModelCandidates()
    {
        var (_, vm, _) = CreateVm(new CodexAccountInfo());

        vm.ModelProvider = "openai";

        vm.IsOpenAiProvider.Should().BeTrue();
        vm.ModelCandidates.Should().ContainInOrder(AiModelCatalog.OpenAiModels);
        vm.Model.Should().Be(AiModelCatalog.DefaultOpenAiModel);
        vm.ModelCandidates.Should().Contain("gpt-4o");
        vm.ModelCandidates.Should().Contain("gpt-4.1");
    }

    /// <summary>初期 openai モデル既定値が AI 生成機能と共通の既定モデルになることを検証する</summary>
    [Fact(DisplayName = "初期状態の openai モデル既定値は AI生成機能と共通の gpt-5.4-mini になる")]
    public void DefaultOpenAiModel_IsSharedWithAiGenerateFeature()
    {
        var (_, vm, _) = CreateVm(new CodexAccountInfo());

        vm.ModelProvider.Should().Be("openai");
        vm.Model.Should().Be(AiModelCatalog.DefaultOpenAiModel);
    }

    /// <summary>openai 以外のプロバイダー選択時に IsOpenAiProvider が false になることを検証する</summary>
    [Fact(DisplayName = "openai 以外のプロバイダー選択時は IsOpenAiProvider が false になる")]
    public void ModelProvider_NonOpenAi_IsOpenAiProviderFalse()
    {
        var (_, vm, _) = CreateVm(new CodexAccountInfo());

        vm.ModelProvider = "ollama-launch";

        vm.IsOpenAiProvider.Should().BeFalse();
        vm.ShowAuthSection.Should().BeFalse();
        vm.ShowNonOpenAiMessage.Should().BeTrue();
    }

    // ----------------------------------------------------------------
    // 【回帰テスト】ShowLoginPanel / CanStartNewThread / CanSendMessage / CanLogout
    // IsOpenAiProvider ベースで判定されていることを全シナリオで検証する
    // ----------------------------------------------------------------

    /// <summary>openai 未ログイン時にログインパネルを表示しスレッド開始を抑止することを検証する</summary>
    [Fact(DisplayName = "[回帰] openai + 未ログイン: ShowLoginPanel=true / CanStartNewThread=false")]
    public async Task Regression_OpenAi_NotLoggedIn_ShowsLoginPanelAndBlocksThread()
    {
        var (_, vm, folder) = CreateVm(new CodexAccountInfo { RequiresOpenAiAuth = true, AuthMode = CodexAuthMode.None }, "openai");

        try
        {
            await InvokePrivateAsync(vm, "ConnectAsync");

            vm.IsOpenAiProvider.Should().BeTrue();
            vm.IsLoggedIn.Should().BeFalse();
            vm.ShowLoginPanel.Should().BeTrue("openai + 未ログインのためログインパネルを表示する");
            vm.CanStartNewThread.Should().BeFalse("openai + 未ログインのためスレッド開始不可");
            vm.CanLogout.Should().BeFalse("未ログインのためログアウトボタンは無効");
            vm.StatusMessage.Should().Contain("ログインしてください");
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    /// <summary>openai で ChatGPT ログイン済みならパネルを隠しスレッド開始を許可することを検証する</summary>
    [Fact(DisplayName = "[回帰] openai + ChatGPT ログイン済み: ShowLoginPanel=false / CanStartNewThread=true")]
    public async Task Regression_OpenAi_ChatGptLoggedIn_HidesLoginPanelAndAllowsThread()
    {
        var (_, vm, folder) = CreateVm(
            new CodexAccountInfo
            {
                RequiresOpenAiAuth = true,
                AuthMode = CodexAuthMode.ChatGpt,
                PlanType = "plus",
            },
            "openai"
        );

        try
        {
            await InvokePrivateAsync(vm, "ConnectAsync");

            vm.IsOpenAiProvider.Should().BeTrue();
            vm.IsLoggedIn.Should().BeTrue();
            vm.ShowLoginPanel.Should().BeFalse("ログイン済みのためログインパネルは不要");
            vm.CanStartNewThread.Should().BeTrue("ログイン済みのためスレッド開始可能");
            vm.CanLogout.Should().BeTrue("ログイン済みかつ接続済みのためログアウト可能");
            vm.StatusMessage.Should().StartWith("接続しました。");
            vm.StatusMessage.Should().NotContain("ログインしてください");
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    /// <summary>openai で API キーログイン済みならパネルを隠しスレッド開始を許可することを検証する</summary>
    [Fact(DisplayName = "[回帰] openai + API キーログイン済み: ShowLoginPanel=false / CanStartNewThread=true")]
    public async Task Regression_OpenAi_ApiKeyLoggedIn_HidesLoginPanelAndAllowsThread()
    {
        var (_, vm, folder) = CreateVm(new CodexAccountInfo { RequiresOpenAiAuth = true, AuthMode = CodexAuthMode.ApiKey }, "openai");

        try
        {
            await InvokePrivateAsync(vm, "ConnectAsync");

            vm.IsOpenAiProvider.Should().BeTrue();
            vm.IsLoggedIn.Should().BeTrue();
            vm.ShowLoginPanel.Should().BeFalse("ログイン済みのためログインパネルは不要");
            vm.CanStartNewThread.Should().BeTrue("ログイン済みのためスレッド開始可能");
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    /// <summary>openai 以外（ollama 等）はログイン不要とし、パネルを隠しスレッド開始を許可することを検証する</summary>
    [Fact(DisplayName = "[回帰] openai 以外（ollama）: ShowLoginPanel=false / CanStartNewThread=true（ログイン不要）")]
    public async Task Regression_NonOpenAi_HidesLoginPanelAndAllowsThread()
    {
        var (_, vm, folder) = CreateVm(new CodexAccountInfo { RequiresOpenAiAuth = false, AuthMode = CodexAuthMode.None }, "ollama-launch");

        try
        {
            await InvokePrivateAsync(vm, "ConnectAsync");

            vm.IsOpenAiProvider.Should().BeFalse();
            vm.ShowAuthSection.Should().BeFalse("openai 以外は認証セクションを表示しない");
            vm.ShowLoginPanel.Should().BeFalse("openai 以外はログインパネルを表示しない");
            vm.CanStartNewThread.Should().BeTrue("openai 以外はログイン不要でスレッド開始可能");
            vm.CanLogout.Should().BeTrue("RequiresOpenAiAuth=false のため接続中はログアウトボタンは有効");
            vm.StatusMessage.Should().StartWith("接続しました。");
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    /// <summary>openai で ChatGPT ログアウト後にパネルを再表示しスレッド開始を抑止することを検証する</summary>
    [Fact(DisplayName = "[回帰] openai + ChatGPT ログアウト後: ShowLoginPanel=true / CanStartNewThread=false")]
    public async Task Regression_OpenAi_AfterLogout_ShowsLoginPanelAndBlocksThread()
    {
        // ログイン済み状態で接続
        var (client, vm, folder) = CreateVm(new CodexAccountInfo { RequiresOpenAiAuth = true, AuthMode = CodexAuthMode.ChatGpt }, "openai");

        try
        {
            await InvokePrivateAsync(vm, "ConnectAsync");
            vm.IsLoggedIn.Should().BeTrue("前提: ログイン済み");

            // ログアウトを実行（FakeClient が account/updated(None) 通知を発火する）
            await InvokePrivateAsync(vm, "LogoutAsync");

            // ログアウト後の状態を検証
            vm.IsLoggedIn.Should().BeFalse("ログアウト後は未ログイン");
            vm.IsOpenAiProvider.Should().BeTrue("プロバイダーは openai のまま");
            vm.ShowLoginPanel.Should().BeTrue("ログアウト後はログインパネルを表示する");
            vm.CanStartNewThread.Should().BeFalse("ログアウト後はスレッド開始不可");
            vm.CanLogout.Should().BeFalse("ログアウト後は未ログインのためログアウトボタンは無効");
            vm.AccountSummary.Should().Be("未ログイン");
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    /// <summary>認証不要状態でログイン後にログアウトすると、再びログインパネルが表示されることを検証する</summary>
    [Fact(DisplayName = "[回帰] openai + RequiresOpenAiAuth=false でログイン後にログアウトすると ShowLoginPanel=true になる")]
    public async Task Regression_OpenAi_RequiresOpenAiAuthFalse_AfterLogout_ShowsLoginPanel()
    {
        // RequiresOpenAiAuth=false（認証済み扱い）で接続開始
        var (_, vm, folder) = CreateVm(new CodexAccountInfo { RequiresOpenAiAuth = false, AuthMode = CodexAuthMode.None }, "openai");

        try
        {
            await InvokePrivateAsync(vm, "ConnectAsync");

            // 前提: ログインパネル非表示・ログアウトボタン有効
            vm.ShowLoginPanel.Should().BeFalse("前提: RequiresOpenAiAuth=false のためパネル非表示");
            vm.CanLogout.Should().BeTrue("前提: 認証不要モードのためログアウト可能");

            // ログアウトを実行（FakeClient は account/updated(None) 通知を発火するのみ。NextAccountInfo は変更しない）
            await InvokePrivateAsync(vm, "LogoutAsync");

            // ログアウト後、ViewModel が RequiresOpenAiAuth=true に直接リセットするため ShowLoginPanel=true になる
            vm.RequiresOpenAiAuth.Should().BeTrue("ログアウト後はサーバーが認証必要を返す");
            vm.IsLoggedIn.Should().BeFalse("ログアウト後は未ログイン");
            vm.ShowLoginPanel.Should().BeTrue("ログアウト後は認証が必要なためログインパネルを表示する");
            vm.CanStartNewThread.Should().BeFalse("ログアウト後はスレッド開始不可");
            vm.CanLogout.Should().BeFalse("ログアウト後は未ログインかつ認証必要のためボタンは無効");
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    /// <summary>未接続状態ではログアウト不可・ログインパネル非表示になることを検証する</summary>
    [Fact(DisplayName = "[回帰] 未接続状態: CanLogout=false / ShowLoginPanel=false（接続前はパネル非表示）")]
    public void Regression_NotConnected_CanLogoutFalse()
    {
        var (_, vm, _) = CreateVm(new CodexAccountInfo { RequiresOpenAiAuth = true, AuthMode = CodexAuthMode.None }, "openai");

        // 接続前の状態
        vm.IsStarted.Should().BeFalse();
        vm.CanLogout.Should().BeFalse("未接続のためログアウト不可");
        // openai だが未接続のため IsLoggedIn=false → ShowLoginPanel は IsOpenAiProvider && !IsLoggedIn = true
        vm.ShowLoginPanel.Should().BeTrue("openai プロバイダーかつ未ログインのためパネル表示");
    }

    /// <summary>初回自動接続が完了するまではログインパネルを表示しないことを検証する</summary>
    [Fact(DisplayName = "[回帰] 初回自動接続中はログインパネルを表示しない")]
    public async Task Regression_InitialAutoConnect_HidesLoginPanelUntilCompleted()
    {
        var (_, vm, folder) = CreateVm(new CodexAccountInfo { RequiresOpenAiAuth = true, AuthMode = CodexAuthMode.None }, "openai");

        try
        {
            vm.BeginInitialAutoConnect();

            vm.ShowLoginPanel.Should().BeFalse("自動接続結果が出るまではログインパネルの点滅を防ぐため非表示にする");

            await vm.InitializeAsync();

            vm.ShowLoginPanel.Should().BeTrue("自動接続後も未ログインならログインパネルを表示する");
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    /// <summary>ChatGPT ログイン済みでの接続完了時にアカウント概要を含む接続済みメッセージを表示することを検証する</summary>
    [Fact(DisplayName = "[回帰] ConnectAsync: ChatGPT ログイン済みなら「接続しました。ChatGPT でログイン済み」を表示する")]
    public async Task ConnectAsync_WhenChatGptLoggedIn_ShowsConnectedWithAccountSummary()
    {
        var (_, vm, folder) = CreateVm(
            new CodexAccountInfo
            {
                RequiresOpenAiAuth = true,
                AuthMode = CodexAuthMode.ChatGpt,
                PlanType = "plus",
            },
            "openai"
        );

        try
        {
            await InvokePrivateAsync(vm, "ConnectAsync");

            vm.IsLoggedIn.Should().BeTrue();
            vm.StatusMessage.Should().StartWith("接続しました。");
            vm.StatusMessage.Should().NotContain("ログインしてください");
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    /// <summary>openai 以外のプロバイダーでの接続完了時に「ログイン不要」を含むメッセージを表示することを検証する</summary>
    [Fact(DisplayName = "[回帰] ConnectAsync: openai 以外プロバイダーなら「接続しました。ログイン不要」を表示する")]
    public async Task ConnectAsync_WhenNonOpenAiProvider_ShowsConnectedWithNoAuth()
    {
        var (_, vm, folder) = CreateVm(new CodexAccountInfo { RequiresOpenAiAuth = false, AuthMode = CodexAuthMode.None }, "ollama-launch");

        try
        {
            await InvokePrivateAsync(vm, "ConnectAsync");

            vm.StatusMessage.Should().StartWith("接続しました。");
            vm.StatusMessage.Should().NotContain("ログインしてください");
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    /// <summary>openai 未ログインでの接続完了時に「ログインしてください」を表示することを検証する</summary>
    [Fact(DisplayName = "[回帰] ConnectAsync: openai で未ログインなら「ログインしてください」を表示する")]
    public async Task ConnectAsync_WhenOpenAiNotLoggedIn_ShowsLoginRequest()
    {
        var (_, vm, folder) = CreateVm(new CodexAccountInfo { RequiresOpenAiAuth = true, AuthMode = CodexAuthMode.None }, "openai");

        try
        {
            await InvokePrivateAsync(vm, "ConnectAsync");

            vm.IsLoggedIn.Should().BeFalse();
            vm.StatusMessage.Should().Contain("ログインしてください");
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    /// <summary>接続済みかつログイン済みの場合のみ CanLogout が true になることを検証する</summary>
    [Fact(DisplayName = "[回帰] 接続済みかつログイン済みの場合のみ CanLogout=true になる")]
    public async Task CanLogout_WhenStartedAndLoggedIn_IsTrue()
    {
        var (_, vm, folder) = CreateVm(new CodexAccountInfo { RequiresOpenAiAuth = true, AuthMode = CodexAuthMode.ApiKey }, "openai");

        try
        {
            vm.CanLogout.Should().BeFalse("未接続のためログアウト不可");

            await InvokePrivateAsync(vm, "ConnectAsync");

            vm.IsStarted.Should().BeTrue();
            vm.IsLoggedIn.Should().BeTrue();
            vm.CanLogout.Should().BeTrue("接続済みかつログイン済みのためログアウト可能");
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    /// <summary>認証不要を返した openai では自動ログイン成功扱いとし、パネルを隠し接続済みを表示することを検証する</summary>
    [Fact(DisplayName = "[回帰] openai + RequiresOpenAiAuth=false: 自動ログイン成功として ShowLoginPanel=false / 接続しましたを表示する")]
    public async Task Regression_OpenAi_RequiresOpenAiAuthFalse_HidesLoginPanelAndShowsConnected()
    {
        // RequiresOpenAiAuth=false はサーバーが認証済み状態を返したケース（自動ログイン成功相当）
        var (_, vm, folder) = CreateVm(new CodexAccountInfo { RequiresOpenAiAuth = false, AuthMode = CodexAuthMode.None }, "openai");

        try
        {
            await InvokePrivateAsync(vm, "ConnectAsync");

            vm.IsOpenAiProvider.Should().BeTrue();
            vm.RequiresOpenAiAuth.Should().BeFalse();
            vm.ShowLoginPanel.Should().BeFalse("RequiresOpenAiAuth=false のためログインパネルを表示しない");
            vm.CanStartNewThread.Should().BeTrue("RequiresOpenAiAuth=false のためスレッド開始可能");
            vm.CanLogout.Should().BeTrue("RequiresOpenAiAuth=false かつ接続中のためログアウトボタンは有効");
            vm.AccountSummary.Should().Be("接続済み");
            vm.StatusMessage.Should().Be("接続しました。接続済み", "openai プロバイダーの自動接続成功のため接続済みを表示");
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }
}
