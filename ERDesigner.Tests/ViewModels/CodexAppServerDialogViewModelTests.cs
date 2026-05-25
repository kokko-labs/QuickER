using System;
using System.IO;
using System.Reflection;
using ERDesigner.Services;
using ERDesigner.ViewModels;
using FluentAssertions;

namespace ERDesigner.Tests.ViewModels;

/// <summary>
/// <see cref="CodexAppServerDialogViewModel"/> の状態反映を検証します。
/// </summary>
public class CodexAppServerDialogViewModelTests
{
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

        public bool IsStarted { get; private set; }
        public CodexAccountInfo NextAccountInfo { get; set; } = new();
        public string? LastApiKey { get; private set; }
        public int StartCount { get; private set; }
        public int LogoutCount { get; private set; }

        public Task StartAsync(CodexAppServerSettings settings, string clientName, string clientTitle, string clientVersion, CancellationToken cancellationToken = default)
        {
            IsStarted = true;
            StartCount++;
            return Task.CompletedTask;
        }

        public Task<CodexAccountInfo> ReadAccountAsync(bool refreshToken, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(NextAccountInfo);
        }

        public Task<CodexLoginStartResult> LoginWithApiKeyAsync(string apiKey, CancellationToken cancellationToken = default)
        {
            LastApiKey = apiKey;
            LoginCompleted?.Invoke(this, new CodexLoginCompletedNotification { Success = true });
            AccountUpdated?.Invoke(this, new CodexAccountUpdatedNotification { AuthMode = CodexAuthMode.ApiKey });
            return Task.FromResult(new CodexLoginStartResult { Type = CodexLoginType.ApiKey });
        }

        public Task<CodexLoginStartResult> StartChatGptLoginAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new CodexLoginStartResult { Type = CodexLoginType.ChatGpt, AuthUrl = "https://chatgpt.example/login" });
        }

        public Task LogoutAsync(CancellationToken cancellationToken = default)
        {
            LogoutCount++;
            AccountUpdated?.Invoke(this, new CodexAccountUpdatedNotification { AuthMode = CodexAuthMode.None });
            return Task.CompletedTask;
        }

        public Task<CodexThreadInfo> StartThreadAsync(CodexThreadStartOptions options, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new CodexThreadInfo { Id = "thr_test", Preview = string.Empty });
        }

        public Task<CodexTurnInfo> StartTurnAsync(string threadId, string prompt, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new CodexTurnInfo { Id = "turn_test", Status = "inProgress" });
        }

        public Task InterruptTurnAsync(string threadId, string turnId, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task RespondToDynamicToolCallAsync(int requestId, string resultText, bool success, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task RespondToApprovalAsync(int requestId, string decision, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

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

    [Fact(DisplayName = "StartChatGptLogin は未接続時に自動接続してブラウザを開く")]
    public async Task StartChatGptLoginAsync_StartsClientAndOpensBrowser()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ERDesignerTests", Guid.NewGuid().ToString("N"));
        var settingsStore = new CodexAppServerSettingsStore(folder);
        var client = new FakeCodexAppServerClient();
        client.NextAccountInfo = new CodexAccountInfo { RequiresOpenAiAuth = true, AuthMode = CodexAuthMode.None };
        var vm = new CodexAppServerDialogViewModel(client, settingsStore, "CodexVmTests_" + Guid.NewGuid().ToString("N"));
        var openedUrls = new List<string>();
        vm.OpenBrowser = url => openedUrls.Add(url); // ブラウザ起動をキャプチャする

        try
        {
            await InvokePrivateAsync(vm, "StartChatGptLoginAsync");

            // 自動接続が行われること
            client.StartCount.Should().Be(1);
            // ブラウザが authUrl で開かれること
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
            // 接続済み状態にする
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

    [Fact(DisplayName = "openai プロバイダー選択時は openai の固定モデル候補が表示される")]
    public void ModelProvider_OpenAi_HasFixedModelCandidates()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ERDesignerTests", Guid.NewGuid().ToString("N"));
        var settingsStore = new CodexAppServerSettingsStore(folder);
        var client = new FakeCodexAppServerClient();
        var vm = new CodexAppServerDialogViewModel(client, settingsStore, "CodexVmTests_" + Guid.NewGuid().ToString("N"));

        vm.ModelProvider = "openai";

        vm.IsOpenAiProvider.Should().BeTrue();
        vm.ModelCandidates.Should().ContainInOrder(AiModelCatalog.OpenAiModels);
        vm.Model.Should().Be(AiModelCatalog.DefaultOpenAiModel);
        vm.ModelCandidates.Should().Contain("gpt-4o");
        vm.ModelCandidates.Should().Contain("gpt-4.1");
    }

    [Fact(DisplayName = "初期状態の openai モデル既定値は AI生成機能と共通の gpt-5.4-mini になる")]
    public void DefaultOpenAiModel_IsSharedWithAiGenerateFeature()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ERDesignerTests", Guid.NewGuid().ToString("N"));
        var settingsStore = new CodexAppServerSettingsStore(folder);
        var client = new FakeCodexAppServerClient();
        var vm = new CodexAppServerDialogViewModel(client, settingsStore, "CodexVmTests_" + Guid.NewGuid().ToString("N"));

        vm.ModelProvider.Should().Be("openai");
        vm.Model.Should().Be(AiModelCatalog.DefaultOpenAiModel);
    }

    [Fact(DisplayName = "openai 以外のプロバイダー選択時は IsOpenAiProvider が false になる")]
    public void ModelProvider_NonOpenAi_IsOpenAiProviderFalse()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ERDesignerTests", Guid.NewGuid().ToString("N"));
        var settingsStore = new CodexAppServerSettingsStore(folder);
        var client = new FakeCodexAppServerClient();
        var vm = new CodexAppServerDialogViewModel(client, settingsStore, "CodexVmTests_" + Guid.NewGuid().ToString("N"));

        vm.ModelProvider = "ollama-launch";

        vm.IsOpenAiProvider.Should().BeFalse();
        vm.ShowAuthSection.Should().BeFalse();
        vm.ShowNonOpenAiMessage.Should().BeTrue();
    }
}
