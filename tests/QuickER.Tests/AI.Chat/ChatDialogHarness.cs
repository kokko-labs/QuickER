using System.IO;
using AwesomeAssertions;
using QuickER.AI;
using QuickER.AI.Chat;
using QuickER.Gui.Abstractions;
using QuickER.Tests.AI.ChatEngines;
using QuickER.Tests.TestDoubles;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.AI.Chat;

/// <summary>
/// チャットのダイアログの表の 1 ケースの組（一時フォルダの設定ストア・偽のクライアント 3 つ・メモリの API キー・VM）と、
/// どの手の後にも掛ける不変条件の見張り
/// </summary>
/// <remarks>
/// <para>
/// claude と copilot のクライアントは必ず偽物を渡す（claude は見つからない＝CI と同じ結果）。実物を渡すと、機械に claude.exe が
/// あるかで結果と残りの作業フォルダが変わる。Codex は偽の app-server のクライアント。
/// </para>
/// <para>不変条件（行の本体の後に <see cref="FinishAsync"/> がまとめて表明する）：</para>
/// <list type="number">
/// <item>行の終わりにターンの実行中でなければ、応答中（IsStreaming）の吹き出しが残っていない。</item>
/// <item>ターンの実行中に接続方式が変わらない。</item>
/// <item><see cref="TryChangeBackend"/> を通した切替は、切り替えられない状態（CanChangeBackend が偽）なら確認を出さずに断り、接続方式を変えない。</item>
/// </list>
/// 行の終わりにエンジンをすべて破棄する（作業フォルダ <c>%TEMP%\QuickER\{backend}\&lt;guid&gt;</c> を残さない）。
/// </remarks>
internal sealed class ChatDialogHarness : IDisposable
{
    private readonly TestTempFolder _folder = new();
    private readonly List<string> _violations = [];
    private readonly List<AiChatDialogViewModel> _vms = [];

    /// <summary>設定ストアのフォルダ（一時フォルダの下の settings。<see cref="BlockSettingsFolder"/> で書けなくできる）</summary>
    public string SettingsFolder { get; private set; }

    /// <summary>組を作る</summary>
    public ChatDialogHarness()
    {
        SettingsFolder = Path.Combine(_folder.Path, "settings");
    }

    /// <summary>Codex の偽のクライアント</summary>
    public FakeCodexAppServerClient Codex { get; set; } = new();

    /// <summary>Claude Code のクライアント（既定は見つからない偽物）</summary>
    public IClaudeCodeClient ClaudeCode { get; set; } =
        new DisposeTrackingClaudeCodeClient { Available = false };

    /// <summary>Copilot の偽のクライアント</summary>
    public FakeCopilotRuntimeClient Copilot { get; set; } = new();

    /// <summary>ダイアログ（null なら渡さない）</summary>
    public IDialogService? Dialogs { get; set; }

    /// <summary>図のホスト（null なら渡さない）</summary>
    public IErDiagramChatHost? Host { get; set; }

    /// <summary>メモリの API キーのストア</summary>
    public InMemoryApiKeyStore Keys { get; } = new();

    /// <summary>設定ストア（同じフォルダ）</summary>
    public AiSettingsStore Store() => new(SettingsFolder);

    /// <summary>設定の保存先を、同じ名前のファイルを置いたパスへ替えて、保存を必ず失敗させる</summary>
    public void BlockSettingsFolder()
    {
        SettingsFolder = Path.Combine(_folder.Path, "blocked");
        File.WriteAllText(SettingsFolder, string.Empty);
    }

    /// <summary>一時フォルダへファイルを書いてパスを返す</summary>
    public string WriteFile(string name, byte[] data)
    {
        var path = Path.Combine(_folder.Path, name);
        File.WriteAllBytes(path, data);
        return path;
    }

    /// <summary>VM を作る（不変条件の見張りを付ける）</summary>
    public AiChatDialogViewModel Create()
    {
        var vm = new AiChatDialogViewModel(
            host: Host,
            dispatcher: new ChatSyncDispatcher(),
            settingsStore: Store(),
            codexClient: Codex,
            claudeCodeClient: ClaudeCode,
            copilotClient: Copilot,
            dialogService: Dialogs,
            apiKeyLoader: Keys.Load,
            apiKeySaver: Keys.Save
        );

        // 不変条件 1 は行の終わり（Finish）に見る。VM はターンの終わりの通知を出し終えてから応答中の印を外すので、
        // 通知の最中に見ると正しい VM でも違反に見える（行が自分で応答中の吹き出しを足してからターンを始めることもある）
        vm.Connection.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.Connection.SelectedBackend) && vm.IsTurnInProgress)
            {
                _violations.Add(
                    $"不変条件 2：ターンの実行中に接続方式が {vm.Connection.SelectedBackend} へ変わった"
                );
            }
        };
        _vms.Add(vm);
        return vm;
    }

    /// <summary>接続方式を切り替える（不変条件 3 を確かめる）</summary>
    public bool TryChangeBackend(AiChatDialogViewModel vm, ErChatBackendKind backend)
    {
        var could = vm.CanChangeBackend;
        var before = vm.Connection.SelectedBackend;
        var confirms = (Dialogs as StubDialogService)?.ConfirmMessages.Count;

        var changed = vm.TryChangeBackend(backend);

        if (!could && before != backend)
        {
            if (changed || vm.Connection.SelectedBackend != before)
            {
                _violations.Add($"不変条件 3：切り替えられない状態で {backend} へ切り替わった");
            }

            if ((Dialogs as StubDialogService)?.ConfirmMessages.Count != confirms)
            {
                _violations.Add("不変条件 3：切り替えられない状態で確認を出した");
            }
        }

        return changed;
    }

    /// <summary>VM の <see cref="AiChatDialogViewModel.IsTurnInProgress"/> が偽になるのを待つ（上限は長く、時間そのものは表明しない）</summary>
    public static Task WaitTurnEndedAsync(AiChatDialogViewModel vm)
    {
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.PropertyChanged += (_, e) =>
        {
            if (
                e.PropertyName == nameof(AiChatDialogViewModel.IsTurnInProgress)
                && !vm.IsTurnInProgress
            )
            {
                ended.TrySetResult();
            }
        };

        if (!vm.IsTurnInProgress)
        {
            ended.TrySetResult();
        }

        return ended.Task.WaitAsync(TimeSpan.FromSeconds(60));
    }

    /// <summary>Copilot へ切り替えて会話を開始し、送信してターンを実行中にする</summary>
    public static void StartCopilotTurn(AiChatDialogViewModel vm)
    {
        vm.Connection.SelectedBackend = ErChatBackendKind.Copilot;
        vm.StartConversationCommand.Execute(null);
        vm.UserInput = "本のテーブルを作って";
        vm.SendMessageCommand.Execute(null);
        vm.IsTurnInProgress.Should().BeTrue();
    }

    /// <summary>
    /// Copilot へ切り替えて会話の開始を門で止める（開始待ちの状態にする）。門を開けて開始を待つのは行
    /// </summary>
    public (Task Starting, TaskCompletionSource Gate) BeginCopilotStart(AiChatDialogViewModel vm)
    {
        vm.Connection.SelectedBackend = ErChatBackendKind.Copilot;
        var gate = new TaskCompletionSource();
        Copilot.StartSessionGate = gate.Task;
        var starting = vm.StartConversationCommand.ExecuteAsync(null);

        // 開始待ちの印（CanStartConversation と CanChangeBackend が偽）が立っている
        vm.CanStartConversation.Should().BeFalse();
        vm.CanChangeBackend.Should().BeFalse();
        return (starting, gate);
    }

    /// <summary>
    /// アプリの終了の経路（<see cref="AiChatDialog.ForceClose"/>）が VM に対して行う段を、同じ順
    /// （中断の要求 → 設定の保存 → エンジンの破棄）で流す
    /// </summary>
    /// <remarks>ウィンドウが段をこの順で呼ぶ配線は AiChatDialogForceCloseTests が実ウィンドウで見る。</remarks>
    public static void RunForceCloseSteps(AiChatDialogViewModel vm)
    {
        vm.RequestInterrupt();
        vm.SaveSettings();

        // 破棄はスレッドプールで走る。全件の並列実行でプールが詰まっても届くまで待てる上限にする
        vm.ShutdownEngines(TimeSpan.FromSeconds(60));
    }

    /// <summary>書式文字列の先頭（最初のプレースホルダより前）を返す（文言の照合用）</summary>
    public static string FormatPrefix(string format) => format[..format.IndexOf('{')];

    /// <summary>行の本体の後に、エンジンを破棄して見張った不変条件を表明する</summary>
    public void Finish()
    {
        foreach (var vm in _vms)
        {
            if (!vm.IsTurnInProgress)
            {
                CheckNoStreaming(vm, "行の終わり");
            }

            vm.ShutdownEngines(TimeSpan.FromSeconds(10));
        }

        _violations.Should().BeEmpty();
    }

    public void Dispose() => _folder.Dispose();

    /// <summary>ターンの外で応答中の吹き出しが残っていれば違反を記録する</summary>
    private void CheckNoStreaming(AiChatDialogViewModel vm, string when)
    {
        if (vm.Messages.Any(message => message.IsStreaming))
        {
            _violations.Add($"不変条件 1：ターンの外（{when}）に応答中の吹き出しが残った");
        }
    }
}
