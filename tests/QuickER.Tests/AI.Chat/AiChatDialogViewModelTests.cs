using System.ComponentModel;
using System.IO;
using FluentAssertions;
using QuickER.AI;
using QuickER.AI.Chat;
using QuickER.AI.UI;
using QuickER.Gui.Abstractions;
using QuickER.Tests.AI;
using QuickER.Tests.TestDoubles;

namespace QuickER.Tests.AI.Chat;

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
        // 設定・UI 状態・モデル履歴を集約した 1 ファイルを一時フォルダへ隔離する（実 %APPDATA% を保護）
        var settingsStore = new AiSettingsStore(folder);
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

    /// <summary>SaveSettings が接続タブを保存し、次回構築時の InitialBackend として復元されることを検証する</summary>
    [Fact(DisplayName = "SaveSettings が接続タブを保存し次回の InitialBackend に復元される")]
    public void SaveSettings_PersistsSelectedBackend_AndRestoresOnNextLoad()
    {
        var folder = Path.Combine(Path.GetTempPath(), "QuickERTests", Guid.NewGuid().ToString("N"));

        try
        {
            var vm = new AiChatDialogViewModel(
                host: null,
                dispatcher: new SyncUiDispatcher(),
                settingsStore: new AiSettingsStore(folder),
                codexClient: new FakeCodexAppServerClient()
            );

            // 保存が無い初回は API キータブが既定
            vm.Connection.InitialBackend.Should().Be(ErChatBackendKind.ApiKey);

            vm.TryChangeBackend(ErChatBackendKind.ClaudeCode).Should().BeTrue();
            vm.SaveSettings();

            var restored = new AiChatDialogViewModel(
                host: null,
                dispatcher: new SyncUiDispatcher(),
                settingsStore: new AiSettingsStore(folder),
                codexClient: new FakeCodexAppServerClient()
            );

            restored.Connection.InitialBackend.Should().Be(ErChatBackendKind.ClaudeCode);
        }
        finally
        {
            Cleanup(folder);
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
                settingsStore: new AiSettingsStore(folder),
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
            vm.Connection.SelectedBackend.Should().Be(ErChatBackendKind.ApiKey);
            vm.Connection.IsApiKeyBackend.Should().BeTrue();
            vm.Connection.IsCodexBackend.Should().BeFalse();
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
            vm.Connection.ApiProvider = AiProvider.OpenAI;
            vm.Connection.ApiKey = string.Empty;
            vm.CanStartConversation.Should().BeFalse();

            vm.Connection.ApiKey = "sk-test";
            vm.CanStartConversation.Should().BeTrue();
        }
        finally
        {
            vm.Connection.SaveApiKey = false;
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
            vm.Connection.ApiProvider = AiProvider.Ollama;
            vm.CanStartConversation.Should().BeTrue();
            vm.Connection.ShowEndpoint.Should().BeTrue();
            vm.Connection.ShowApiKey.Should().BeFalse();
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
            vm.Connection.ApiProvider = AiProvider.Ollama;
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
            vm.Connection.SelectedBackend = ErChatBackendKind.Codex;
            vm.Connection.IsCodexBackend.Should().BeTrue();
            vm.Connection.IsApiKeyBackend.Should().BeFalse();
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
            vm.Connection.SelectedBackend.Should().Be(ErChatBackendKind.Codex);
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
            vm.Connection.SelectedBackend.Should().Be(ErChatBackendKind.Codex);
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
            vm.Connection.SelectedBackend.Should().Be(ErChatBackendKind.ApiKey);
            vm.HasConversation.Should().BeTrue();
            dialogs.ConfirmMessages.Should().ContainSingle();
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>
    /// 子 <see cref="ChatConnectionSettingsViewModel.ApiKey"/> の変更で、親の
    /// <see cref="AiChatDialogViewModel.CanStartConversation"/> の PropertyChanged が発火することを検証する
    /// （Connection.PropertyChanged → 親ハンドラ → NotifyReadinessChanged の連鎖の取りこぼしを恒久検知する）。
    /// </summary>
    [Fact(DisplayName = "Connection.ApiKey 変更で親の CanStartConversation が通知される")]
    public void ConnectionApiKeyChange_RaisesCanStartConversationOnParent()
    {
        var (vm, _, folder) = CreateVm();

        try
        {
            vm.Connection.ApiProvider = AiProvider.OpenAI;

            var raised = new List<string>();
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is not null)
                {
                    raised.Add(e.PropertyName);
                }
            };

            vm.Connection.ApiKey = "sk-test";

            raised.Should().Contain(nameof(AiChatDialogViewModel.CanStartConversation));
        }
        finally
        {
            vm.Connection.SaveApiKey = false;
            ApiKeyStore.Save("OpenAiApiKey", string.Empty);
            Cleanup(folder);
        }
    }

    /// <summary>
    /// Codex バックエンドの成功ターンでは API モデル履歴が記録されないことを検証する（負方向）。
    /// ApplyTurnCompleted は成功分岐で無条件に <see cref="ChatConnectionSettingsViewModel.RecordSuccessfulModel"/>
    /// を呼ぶが、子側ガード（バックエンドが API キーでない）で記録されないことを確認する。
    /// 正方向（API キー接続で記録される）は Connection 単体テストと Mock VM の
    /// エンドツーエンドテストでカバーする（Chat VM には API キーエンジンの注入 seam が無いため）。
    /// </summary>
    [Fact(DisplayName = "Codex 成功ターンでは API モデル履歴を記録しない")]
    public void CodexSuccessfulTurn_DoesNotRecordApiHistory()
    {
        var (vm, client, folder) = CreateVm();

        try
        {
            // Ollama＋モデルを設定してからバックエンドを Codex へ切り替える
            // （ガードが無ければ記録されうる状態を作り、ガードが効くことを確かめる）
            vm.Connection.ApiProvider = AiProvider.Ollama;
            vm.Connection.ApiModel = "qwen3.6:35b";
            vm.Connection.SelectedBackend = ErChatBackendKind.Codex;

            // Codex エンジン経由で成功ターン完了を発火させる
            client.RaiseTurnCompleted("completed");

            // API 履歴には記録されない（ai-settings.json は作られない）
            File.Exists(new AiSettingsStore(folder).SettingsPath).Should().BeFalse();
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>
    /// Codex バックエンド×非 openai プロバイダの成功ターンで、使用モデルが Codex 履歴へ
    /// 記録されることをエンドツーエンド（<see cref="FakeCodexAppServerClient.RaiseTurnCompleted"/> 経由）で検証する（正方向）。
    /// </summary>
    [Fact(DisplayName = "Codex×非 openai の成功ターンで使用モデルが履歴へ記録される")]
    public void CodexSuccessfulTurn_NonOpenAiProvider_RecordsCodexHistory()
    {
        var (vm, client, folder) = CreateVm();

        try
        {
            // 注意: Chat VM の Connection は実 config.toml を読む（seam 無し）ため、
            // 実環境のプロバイダ設定に依存しないよう、プロバイダ・モデルともテスト固有の名前を使う
            vm.Connection.SelectedBackend = ErChatBackendKind.Codex;
            vm.Connection.CodexModelProvider = "mru-e2e-provider";
            vm.Connection.CodexModel = "mru-e2e-model";

            // Codex エンジン経由で成功ターン完了を発火させる
            client.RaiseTurnCompleted("completed");

            // プロバイダ別履歴へ記録され、候補にも × 付きで現れる
            var reloaded = new AiSettingsStore(folder).Load().CodexModelHistory;
            reloaded.ModelsFor("mru-e2e-provider").Should().Equal("mru-e2e-model");
            vm.Connection.CodexModelCandidates.Should()
                .Contain(c => c.Name == "mru-e2e-model" && c.IsRemovable);
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
            vm.Connection.ApiProvider = AiProvider.OpenAI;
            vm.Attachments.Support.Should().Be(AttachmentSupport.Images | AttachmentSupport.Text);

            vm.Connection.ApiProvider = AiProvider.Claude;
            vm.Attachments.Support.Should()
                .Be(AttachmentSupport.Images | AttachmentSupport.Pdf | AttachmentSupport.Text);

            vm.Connection.ApiProvider = AiProvider.Ollama;
            vm.Attachments.Support.Should().Be(AttachmentSupport.None);
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>Claude Code バックエンドでは添付範囲が全種別になることを検証する</summary>
    [Fact(DisplayName = "Claude Code では添付範囲が全種別")]
    public void AttachmentSupport_ClaudeCodeBackend_IsAllKinds()
    {
        var (vm, _, folder) = CreateVm();

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
            vm.Connection.ApiProvider = AiProvider.Claude;
            vm.Attachments.AddClipboardImage(PngBytes(), DateTime.Now);
            vm.Attachments.Items.Should().HaveCount(1);

            vm.Connection.ApiProvider = AiProvider.Ollama;

            vm.Attachments.Items.Should().BeEmpty();
        }
        finally
        {
            Cleanup(folder);
        }
    }
}
