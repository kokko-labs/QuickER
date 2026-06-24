using System.IO;
using ERDesigner.Services;
using ERDesigner.Services.Chat;
using ERDesigner.Tests.Services.Chat;
using ERDesigner.ViewModels;
using FluentAssertions;

namespace ERDesigner.Tests.ViewModels;

/// <summary><see cref="AiChatDialogViewModel"/> のタブ切替・送信可否・接続方式判定を検証するテストクラス</summary>
public class AiChatDialogViewModelTests
{
    private sealed class SyncUiDispatcher : IUiDispatcher
    {
        public T Invoke<T>(Func<T> func) => func();
    }

    private static (
        AiChatDialogViewModel vm,
        FakeCodexAppServerClient client,
        string folder
    ) CreateVm()
    {
        var folder = Path.Combine(
            Path.GetTempPath(),
            "ERDesignerTests",
            Guid.NewGuid().ToString("N")
        );
        var settingsStore = new CodexAppServerSettingsStore(folder);
        var client = new FakeCodexAppServerClient();
        var vm = new AiChatDialogViewModel(
            mainViewModel: null,
            dispatcher: new SyncUiDispatcher(),
            settingsStore: settingsStore,
            codexClient: client
        );
        return (vm, client, folder);
    }

    private static void Cleanup(string folder)
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>既定の接続方式が API キーであることを検証する</summary>
    [Fact(DisplayName = "既定の接続方式は API キーである")]
    public void DefaultBackend_IsApiKey()
    {
        var (vm, _, folder) = CreateVm();

        try
        {
            vm.SelectedBackend.Should().Be(ErChatBackendKind.ApiKey);
            vm.IsApiKeyBackend.Should().BeTrue();
            vm.IsCodexBackend.Should().BeFalse();
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>API キー接続(OpenAI)では API キーが無いと会話開始不可、入力後に可能になることを検証する</summary>
    [Fact(DisplayName = "OpenAI 接続は API キー入力で会話開始可能になる")]
    public void ApiKeyBackend_OpenAi_RequiresApiKey()
    {
        var (vm, _, folder) = CreateVm();

        try
        {
            vm.ApiProvider = AiProvider.OpenAI;
            vm.ApiKey = string.Empty;
            vm.CanStartConversation.Should().BeFalse();

            vm.ApiKey = "sk-test";
            vm.CanStartConversation.Should().BeTrue();
        }
        finally
        {
            vm.SaveApiKey = false;
            ApiKeyStore.Save("OpenAiApiKey", string.Empty);
            Cleanup(folder);
        }
    }

    /// <summary>API キー接続(Ollama)では認証不要で会話開始可能になることを検証する</summary>
    [Fact(DisplayName = "Ollama 接続は API キー不要で会話開始可能")]
    public void ApiKeyBackend_Ollama_NeedsNoKey()
    {
        var (vm, _, folder) = CreateVm();

        try
        {
            vm.ApiProvider = AiProvider.Ollama;
            vm.CanStartConversation.Should().BeTrue();
            vm.ShowEndpoint.Should().BeTrue();
            vm.ShowApiKey.Should().BeFalse();
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>送信は会話開始前は不可、開始かつ入力ありで可能になることを検証する</summary>
    [Fact(DisplayName = "送信は会話開始済みかつ入力ありのときのみ可能")]
    public void CanSendMessage_RequiresStartedConversationAndInput()
    {
        var (vm, _, folder) = CreateVm();

        try
        {
            vm.ApiProvider = AiProvider.Ollama;
            vm.UserInput = "本のテーブルを作って";

            // 会話未開始のうちは送信不可
            vm.CanSendMessage.Should().BeFalse();
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>接続方式を Codex へ切り替えると判定プロパティが更新されることを検証する</summary>
    [Fact(DisplayName = "Codex へ切り替えると IsCodexBackend が true になる")]
    public void SwitchToCodex_UpdatesBackendFlags()
    {
        var (vm, _, folder) = CreateVm();

        try
        {
            vm.SelectedBackend = ErChatBackendKind.Codex;
            vm.IsCodexBackend.Should().BeTrue();
            vm.IsApiKeyBackend.Should().BeFalse();
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>ユーザー／アシスタント発言があるときのみ HasConversation が true になることを検証する</summary>
    [Fact(DisplayName = "HasConversation はユーザー／アシスタント発言で true になる")]
    public void HasConversation_TrueOnlyWithUserOrAssistantMessage()
    {
        var (vm, _, folder) = CreateVm();

        try
        {
            vm.HasConversation.Should().BeFalse();

            // システムメッセージだけでは会話とみなさない
            vm.Messages.Add(
                new ErChatMessage { Role = ErChatMessageRole.System, Content = "案内" }
            );
            vm.HasConversation.Should().BeFalse();

            vm.Messages.Add(
                new ErChatMessage { Role = ErChatMessageRole.User, Content = "こんにちは" }
            );
            vm.HasConversation.Should().BeTrue();
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>ClearConversation がメッセージを空にし会話なし状態へ戻すことを検証する</summary>
    [Fact(DisplayName = "ClearConversation は会話を空にする")]
    public void ClearConversation_EmptiesMessages()
    {
        var (vm, _, folder) = CreateVm();

        try
        {
            vm.Messages.Add(
                new ErChatMessage { Role = ErChatMessageRole.User, Content = "こんにちは" }
            );
            vm.Messages.Add(
                new ErChatMessage { Role = ErChatMessageRole.Assistant, Content = "どうぞ" }
            );

            vm.ClearConversation();

            vm.Messages.Should().BeEmpty();
            vm.HasConversation.Should().BeFalse();
        }
        finally
        {
            Cleanup(folder);
        }
    }
}
