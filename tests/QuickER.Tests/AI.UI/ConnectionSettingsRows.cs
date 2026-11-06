using AwesomeAssertions;
using QuickER.AI;
using QuickER.AI.UI;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.AI.UI;

/// <summary>接続設定の表の行が触る面（接続方式ごとの設定）</summary>
internal enum ConnectionArea
{
    /// <summary>API キー接続（プロバイダー・モデル・キー・エンドポイント）</summary>
    ApiKey,

    /// <summary>Codex 接続（プロバイダー・モデル・履歴）</summary>
    Codex,

    /// <summary>Claude Code 接続（モデル）</summary>
    ClaudeCode,

    /// <summary>Copilot 接続（モデル・列挙・履歴）</summary>
    Copilot,

    /// <summary>接続方式に依らない面（タブ・表示・ダイアログの種類）</summary>
    Common,
}

/// <summary>接続設定の表の行の手の種類</summary>
internal enum ConnectionOp
{
    /// <summary>作った直後の既定</summary>
    Defaults,

    /// <summary>プロパティの代入・切替（保存しない）</summary>
    Change,

    /// <summary>設定ファイルからの読み込み（LoadSettings・別の VM での読み直し）</summary>
    Load,

    /// <summary>設定の保存（SaveSettings）</summary>
    Save,

    /// <summary>成功したモデルの記録（RecordSuccessfulModel）</summary>
    Record,

    /// <summary>履歴からの削除（Remove*HistoryCommand）</summary>
    Remove,

    /// <summary>API キーの確定と保存（CommitApiKey・保存のオン／オフ）</summary>
    CommitKey,

    /// <summary>接続の組み立て（BuildOpenAiConnection・CreateApiKeyEngine）</summary>
    Build,
}

/// <summary>接続設定の表の行の入力（触る面と手の種類・本体）</summary>
/// <param name="Touches">触る（面, 手の種類）の組（網羅の計数に使う）</param>
/// <param name="Body">流して表明する本体</param>
internal sealed record ConnectionSettingsCase(
    IReadOnlyList<(ConnectionArea Area, ConnectionOp Op)> Touches,
    Action<ConnectionSettingsHarness> Body
);

/// <summary>
/// 接続設定（<see cref="QuickER.AI.UI.ChatConnectionSettingsViewModel"/>・<see cref="QuickER.AI.UI.BackendTabSelection"/>・
/// <see cref="QuickER.AI.UI.BackendConnectionChecks"/>）の操作の列の表
/// </summary>
/// <remarks>
/// 行は元のテストの入力（初期状態・手の列）と期待をそのまま持つ。組（<see cref="ConnectionSettingsHarness"/>）が
/// どの手の後にも掛ける不変条件 4 つを見張り、行の本体の後に表明する。主題ごとの行は
/// <c>ConnectionSettingsRows.{主題}.cs</c> に置く。
/// </remarks>
internal static partial class ConnectionSettingsRows
{
    /// <summary>行の表</summary>
    public static ScenarioTable<ConnectionSettingsCase> Table =>
        new(
            EngineIds.Ai,
            [
                .. ApiKeyRows(),
                .. CodexRows(),
                .. CopilotRows(),
                .. PersistenceRows(),
                .. FailureRows(),
                .. TabRows(),
            ]
        );

    /// <summary>行を作る</summary>
    /// <param name="id">行の Id（{面}-{守っていること}）</param>
    /// <param name="description">説明（元のテストの DisplayName）</param>
    /// <param name="touches">触る（面, 手の種類）の組</param>
    /// <param name="body">流して表明する本体</param>
    private static ScenarioRow<ConnectionSettingsCase> Row(
        string id,
        string description,
        (ConnectionArea Area, ConnectionOp Op)[] touches,
        Action<ConnectionSettingsHarness> body
    ) => new(id, null, description, new ConnectionSettingsCase(touches, body));

    /// <summary>候補のモデル名一覧（アサート簡略化用）</summary>
    private static IEnumerable<string> CandidateNames(ChatConnectionSettingsViewModel vm) =>
        vm.ApiModelCandidates.Select(c => c.Name);

    /// <summary>非 openai プロバイダ（ollama-launch）を含む config.toml のスタブ</summary>
    private static CodexConfigToml NonOpenAiConfig() =>
        new() { ProviderNames = new List<string> { "ollama-launch" } };

    /// <summary>非 openai プロバイダを選択済みの Codex バックエンド VM を用意する</summary>
    private static ChatConnectionSettingsViewModel CreateCodexVm(
        ConnectionSettingsHarness h,
        AiDialogKind dialogKind = AiDialogKind.AiChat
    )
    {
        h.CodexConfig = NonOpenAiConfig;
        var vm = h.Create(dialogKind);
        vm.LoadSettings();
        vm.SelectedBackend = ErChatBackendKind.Codex;
        vm.CodexModelProvider = "ollama-launch";
        return vm;
    }

    /// <summary>カスタムモデルを 1 つ履歴に記録した VM（表示中のモデルは別のもの）</summary>
    private static ChatConnectionSettingsViewModel CreateVmWithHistory(ConnectionSettingsHarness h)
    {
        var vm = h.Create();
        vm.LoadSettings();
        vm.ApiModel = "custom-openai";
        vm.RecordSuccessfulModel();
        vm.ApiModel = "other";
        vm.ApiModelCandidates.Select(c => c.Name).Should().Contain("custom-openai");
        return vm;
    }

    /// <summary>解釈できない保存値のプロバイダーを読むと既定（OpenAI・上書きなし）になる</summary>
    private static void UnknownSavedProviderFallsBackToOpenAi(
        ConnectionSettingsHarness h,
        string saved
    )
    {
        var seeded = h.Store().Load();
        seeded.ChatUi.ApiProvider = saved;
        h.Store().Save(seeded);

        var vm = h.Create();
        vm.LoadSettings();

        vm.ApiProvider.Should().Be(AiProvider.OpenAI);
        vm.EndpointOverride.Should().BeEmpty();
    }

    /// <summary>通知のたびに <see cref="BackendTabSelection.Selected"/> の値を記録する</summary>
    private static List<ErChatBackendKind> RecordSelected(BackendTabSelection selection)
    {
        var values = new List<ErChatBackendKind>();
        selection.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BackendTabSelection.Selected))
            {
                values.Add(selection.Selected);
            }
        };
        return values;
    }

    /// <summary>切替を認める試行（親 VM の TryChangeBackend の代わり）</summary>
    private static Func<ErChatBackendKind, bool> Accepting(
        ChatConnectionSettingsViewModel connection
    ) =>
        backend =>
        {
            connection.SelectedBackend = backend;
            return true;
        };

    /// <summary>起動された確認の名前と、知らせ先へ渡された失敗を記録する組を作る</summary>
    private static (
        BackendConnectionChecks Checks,
        List<string> Started,
        List<Exception> Failures
    ) CreateChecks(ChatConnectionSettingsViewModel connection, Exception? failure = null)
    {
        var started = new List<string>();
        var failures = new List<Exception>();

        Func<Task> Check(string name) =>
            () =>
            {
                started.Add(name);
                return failure is null ? Task.CompletedTask : Task.FromException(failure);
            };

        var checks = new BackendConnectionChecks(
            connection,
            Check("codex"),
            Check("claude"),
            Check("copilot"),
            failures.Add
        );
        return (checks, started, failures);
    }

    /// <summary>UI スレッドへのマーシャリングを同期実行で代替するテスト用ディスパッチャ</summary>
    private sealed class SyncUiDispatcher : IUiDispatcher
    {
        public T Invoke<T>(Func<T> func) => func();
    }

    /// <summary>何もしないツールホスト</summary>
    private sealed class NoopToolHost : IErDiagramToolHost
    {
        public (string Result, bool Success) Execute(string toolName, string argumentsJson) =>
            (string.Empty, true);
    }
}
