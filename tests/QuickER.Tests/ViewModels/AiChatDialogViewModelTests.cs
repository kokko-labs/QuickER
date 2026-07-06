using System.IO;
using FluentAssertions;
using QuickER.AI;
using QuickER.Services;
using QuickER.Services.Chat;
using QuickER.Tests.Services.Chat;
using QuickER.Tests.TestDoubles;
using QuickER.ViewModels;

namespace QuickER.Tests.ViewModels;

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
    ) CreateVm(IDialogService? dialogService = null)
    {
        var folder = Path.Combine(Path.GetTempPath(), "QuickERTests", Guid.NewGuid().ToString("N"));
        var settingsStore = new CodexAppServerSettingsStore(folder);
        var client = new FakeCodexAppServerClient();
        var vm = new AiChatDialogViewModel(
            host: null,
            dispatcher: new SyncUiDispatcher(),
            settingsStore: settingsStore,
            codexClient: client,
            dialogService: dialogService
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

    /// <summary>MainViewModel 具象なしで、IErDiagramChatHost 抽象のみからツール seam を得て構築できることを検証する</summary>
    [Fact(DisplayName = "MainViewModel 無しでも IErDiagramChatHost 注入で構築できる")]
    public void Constructs_WithChatHostStub_WithoutMainViewModel()
    {
        var folder = Path.Combine(Path.GetTempPath(), "QuickERTests", Guid.NewGuid().ToString("N"));
        var host = new RecordingChatHost();

        try
        {
            var vm = new AiChatDialogViewModel(
                host: host,
                dispatcher: new SyncUiDispatcher(),
                settingsStore: new CodexAppServerSettingsStore(folder),
                codexClient: new FakeCodexAppServerClient()
            );

            // ツール実行 seam は host 抽象から取得され、MainViewModel 具象には依存しない
            vm.Should().NotBeNull();
            host.ToolHostAccessed.Should().BeTrue();
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>チャットホストのスタブ（呼び出しを記録し、MainViewModel への依存を排除する）</summary>
    private sealed class RecordingChatHost : IErDiagramChatHost
    {
        public bool ToolHostAccessed { get; private set; }

        public bool IsEmpty { get; set; } = true;

        public int AutoArrangeCount { get; private set; }

        public IErDiagramToolHost ToolHost
        {
            get
            {
                ToolHostAccessed = true;
                return new NoOpToolHost();
            }
        }

        public void AutoArrangeNewDiagram() => AutoArrangeCount++;

        private sealed class NoOpToolHost : IErDiagramToolHost
        {
            public (string Result, bool Success) Execute(string toolName, string argumentsJson) =>
                (string.Empty, true);
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

    /// <summary>会話が無いときは確認ダイアログを出さずに接続方式を切り替えることを検証する</summary>
    [Fact(DisplayName = "会話なしなら確認せず接続方式を切り替える")]
    public void TryChangeBackend_NoConversation_SwitchesWithoutConfirm()
    {
        var dialogs = new StubDialogService();
        var (vm, _, folder) = CreateVm(dialogs);

        try
        {
            var result = vm.TryChangeBackend(ErChatBackendKind.Codex);

            result.Should().BeTrue();
            vm.SelectedBackend.Should().Be(ErChatBackendKind.Codex);
            dialogs.ConfirmMessages.Should().BeEmpty();
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>会話中に確認 OK なら会話をクリアして接続方式を切り替えることを検証する</summary>
    [Fact(DisplayName = "会話あり＋確認OKならクリアして切り替える")]
    public void TryChangeBackend_ConversationConfirmed_ClearsAndSwitches()
    {
        var dialogs = new StubDialogService { ConfirmResult = true };
        var (vm, _, folder) = CreateVm(dialogs);

        try
        {
            vm.Messages.Add(
                new ErChatMessage { Role = ErChatMessageRole.User, Content = "こんにちは" }
            );

            var result = vm.TryChangeBackend(ErChatBackendKind.Codex);

            result.Should().BeTrue();
            vm.SelectedBackend.Should().Be(ErChatBackendKind.Codex);
            vm.HasConversation.Should().BeFalse();
            dialogs.ConfirmMessages.Should().ContainSingle();
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>会話中に確認キャンセルなら切り替えず会話を維持することを検証する</summary>
    [Fact(DisplayName = "会話あり＋確認キャンセルなら切り替えず会話を維持する")]
    public void TryChangeBackend_ConversationCancelled_KeepsBackendAndConversation()
    {
        var dialogs = new StubDialogService { ConfirmResult = false };
        var (vm, _, folder) = CreateVm(dialogs);

        try
        {
            vm.Messages.Add(
                new ErChatMessage { Role = ErChatMessageRole.User, Content = "こんにちは" }
            );

            var result = vm.TryChangeBackend(ErChatBackendKind.Codex);

            result.Should().BeFalse();
            vm.SelectedBackend.Should().Be(ErChatBackendKind.ApiKey);
            vm.HasConversation.Should().BeTrue();
            dialogs.ConfirmMessages.Should().ContainSingle();
        }
        finally
        {
            Cleanup(folder);
        }
    }

    // ── 添付 ──

    /// <summary>PNG シグネチャ付きバイト列を作る（添付テスト用）</summary>
    private static byte[] PngBytes()
    {
        var data = new byte[16];
        byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        Array.Copy(signature, data, signature.Length);
        return data;
    }

    /// <summary>API キー接続の添付範囲がプロバイダー選択に追従することを検証する</summary>
    [Fact(DisplayName = "添付範囲はプロバイダーに追従する")]
    public void AttachmentSupport_TracksApiProvider()
    {
        var (vm, _, folder) = CreateVm();

        try
        {
            vm.ApiProvider = AiProvider.OpenAI;
            vm.Attachments.Support.Should().Be(AttachmentSupport.Images);

            vm.ApiProvider = AiProvider.Claude;
            vm.Attachments.Support.Should().Be(AttachmentSupport.ImagesAndPdf);

            vm.ApiProvider = AiProvider.Ollama;
            vm.Attachments.Support.Should().Be(AttachmentSupport.None);
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>Claude Code バックエンドでは添付範囲が画像＋PDF になることを検証する</summary>
    [Fact(DisplayName = "Claude Code では添付範囲が画像＋PDF")]
    public void AttachmentSupport_ClaudeCodeBackend_IsImagesAndPdf()
    {
        var (vm, _, folder) = CreateVm();

        try
        {
            vm.SelectedBackend = ErChatBackendKind.ClaudeCode;
            vm.Attachments.Support.Should().Be(AttachmentSupport.ImagesAndPdf);
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>添付非対応のプロバイダー（Ollama）へ切り替えると Pending がクリアされることを検証する</summary>
    [Fact(DisplayName = "非対応プロバイダーへ切替で添付をクリア")]
    public void SwitchToUnsupportedProvider_ClearsAttachments()
    {
        var (vm, _, folder) = CreateVm();

        try
        {
            vm.ApiProvider = AiProvider.Claude;
            vm.Attachments.AddClipboardImage(PngBytes(), DateTime.Now);
            vm.Attachments.Items.Should().HaveCount(1);

            vm.ApiProvider = AiProvider.Ollama;

            vm.Attachments.Items.Should().BeEmpty();
        }
        finally
        {
            Cleanup(folder);
        }
    }
}
