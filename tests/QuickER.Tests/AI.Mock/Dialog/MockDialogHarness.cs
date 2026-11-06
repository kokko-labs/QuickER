using System.ComponentModel;
using System.IO;
using System.Text.Json;
using AwesomeAssertions;
using QuickER.AI;
using QuickER.AI.Mock;
using QuickER.AI.UI;
using QuickER.Model;
using QuickER.Tests.TestDoubles;
using QuickER.Tests.TestSupport;
using MockStrings = QuickER.AI.Mock.Resources.Strings;

namespace QuickER.Tests.AI.Mock.Dialog;

/// <summary>
/// モック生成のダイアログの表の 1 行の組（行ごとの一時フォルダ・設定ストア・記録するダイアログ・偽のファイルダイアログ・
/// 偽の生成器・4 つの接続方式の偽のエンジン・メモリの API キー・VM）と、どの手の後にも掛ける不変条件の見張り
/// </summary>
/// <remarks>
/// <para>
/// 組は記録するダイアログ・偽のファイルダイアログ・偽の生成器・4 つの接続方式のエンジンの作り方を必ず渡す
/// （省くと失敗の経路で実の MessageBox が出て止まり、PATH の claude / dotnet や実の CLI のプロセスへ行く）。
/// フォルダを開く処理も記録へ差し替える。設定の保存先は行ごとの一時フォルダの下。
/// </para>
/// <para>不変条件（行の本体の後に <see cref="FinishAsync"/> がまとめて表明する。出典は src/QuickER.AI.Mock/MockGenerationDialogViewModel.cs の行）：</para>
/// <list type="number">
/// <item>ターンの外に応答中（IsStreaming）の吹き出しを残さない（:194-198。VM はターンの終わりの通知を出し終えてから印を外すので、通知の中でなく行の終わりに見る）。</item>
/// <item>ターンの実行中に接続方式が変わらない。<see cref="TryChangeBackend"/> を通した切替は、切り替えられない状態（CanChangeBackend が偽）なら断って理由を状態の文へ出す（:258・:2063-2067）。</item>
/// <item>ターンの実行中にモックフォルダが変わらない（:146-153）。</item>
/// <item>行の終わりにターン・生成の実行中なら、保留のターンを終え・止めた生成を進めると、どちらの実行中も解ける＝拒否した手がセッションを切り離して固まらない（:131-134・:2050-2054・:1360-1368）。</item>
/// <item>作ったエンジンは、行の終わりの破棄（ShutdownEngines と切り離した破棄の待ち）の後にちょうど 1 回破棄されている（:1527-1578・:1749-1777）。</item>
/// <item>初回ターンの済みは成功で終わったときだけ立つ：エンジンごとに、成功したターンの完了より前に送ったプロンプトは開始のプロンプト（利用者の指示の見出し入り）、後に送ったものは生のフィードバック（:1024-1044・:1055-1061・:1918-1922）。</item>
/// <item>CanGenerateMockProject と無効の理由の文（MockGenDisabledReason が空か）が食い違わない（判定 :382-389 と理由 :410-481 は別のコード）。</item>
/// <item>モックプロジェクトの生成中は会話の開始も送信もできず、止めている理由（ConversationDisabledReason）があり、生成中でなければ理由は無い（:220-248）。</item>
/// <item>選択中の画面は null か画面一覧に実在するインスタンスで、画面があること（HasScreens）は画面一覧が空でないことと一致する（:264・:801-845）。</item>
/// <item>添付の対応範囲は接続方式（API キーならプロバイダー）から決まる範囲と常に一致する（:651-655・:2010-2028）。</item>
/// <item>フルパスでないモックフォルダでは会話を開始できない（:739-747）。</item>
/// </list>
/// 7・8 は VM のどの変更通知の後にも、11 はモックフォルダの変更通知の後に、2・3・10 は接続・フォルダの変更通知の中で見張り、
/// 1・4・5・6・9 と 7〜11 の最後の状態は行の終わりに見る。
/// </remarks>
internal sealed class MockDialogHarness : IDisposable
{
    /// <summary>画面の HTML（共有の style.css を参照する完全な HTML）</summary>
    public const string ValidHtml =
        "<!DOCTYPE html><html lang=\"ja\"><head><link rel=\"stylesheet\" href=\"style.css\">"
        + "<style>body{}</style></head><body><h1>顧客一覧</h1></body></html>";

    private readonly TestTempFolder _folder = new();
    private readonly List<string> _violations = [];
    private readonly List<MockGenerationDialogViewModel> _vms = [];
    private readonly HashSet<MockGenerationDialogViewModel> _shutDown = [];
    private readonly List<(string Name, Func<int> DisposeCount)> _disposeProbes = [];
    private readonly Dictionary<FakeChatEngine, List<(int SentCount, bool Success)>> _completions =
    [];
    private readonly List<Task> _heldGenerations = [];

    /// <summary>組を作る</summary>
    public MockDialogHarness()
    {
        SettingsFolder = Path.Combine(_folder.Path, "settings");
    }

    /// <summary>行の一時フォルダ</summary>
    public string BaseFolder => _folder.Path;

    /// <summary>既定のモックフォルダ（一時フォルダの下の mock）</summary>
    public string MockFolder => Path.Combine(_folder.Path, "mock");

    /// <summary>設定ストアのフォルダ（一時フォルダの下の settings。<see cref="BlockSettingsFolder"/> で書けなくできる）</summary>
    public string SettingsFolder { get; private set; }

    /// <summary>図（呼び出し側が後から書き換えられる）。既定は顧客テーブル 1 つ</summary>
    public ErDiagram Diagram { get; set; } = NonEmptyDiagram();

    /// <summary>記録するダイアログ</summary>
    public StubDialogService Dialogs { get; } = new();

    /// <summary>偽のファイルダイアログ</summary>
    public MockDialogFileDialogService Files { get; } = new();

    /// <summary>偽の生成器</summary>
    public FakeMockProjectGenerator Generator { get; } = new();

    /// <summary>メモリの API キーのストア</summary>
    public InMemoryApiKeyStore Keys { get; } = new();

    /// <summary>進捗ログの反映の予約（既定は実行しない＝反映は生成の完了時の全量だけになり、時間に依存しない）</summary>
    public Action<Action> ScheduleLogFlush { get; set; } = _ => { };

    /// <summary>「フォルダを開く」で開いたフォルダ</summary>
    public List<string> Opened { get; } = [];

    /// <summary>作った偽のエンジン（作った順）</summary>
    public List<FakeChatEngine> Engines { get; } = [];

    /// <summary>最後に作った偽のエンジン</summary>
    public FakeChatEngine Engine => Engines[^1];

    /// <summary>Codex と Claude Code を実のエンジンの型（偽のクライアント）で作る（<see cref="UseRealCliEngines"/>）</summary>
    private bool _realCliEngines;

    /// <summary>最後に作った実の Claude Code のエンジン（<see cref="UseRealCliEngines"/> のときだけ）</summary>
    public ClaudeCodeChatEngine? ClaudeCodeEngine { get; private set; }

    /// <summary>最後に作った実の Codex のエンジン（<see cref="UseRealCliEngines"/> のときだけ）</summary>
    public CodexChatEngine? CodexEngine { get; private set; }

    /// <summary>顧客テーブル 1 つを持つ非空の図</summary>
    public static ErDiagram NonEmptyDiagram() =>
        new()
        {
            Entities =
            {
                new Entity { TableName = "Customer", Description = "顧客" },
            },
        };

    /// <summary>一時フォルダの下のパス</summary>
    public string PathOf(params string[] parts) => Path.Combine([_folder.Path, .. parts]);

    /// <summary>設定ストア（同じフォルダ）</summary>
    public AiSettingsStore Store() => new(SettingsFolder);

    /// <summary>設定の保存先を、同じ名前のファイルを置いたパスへ替えて、保存を必ず失敗させる（VM を作る前に呼ぶ）</summary>
    public void BlockSettingsFolder()
    {
        SettingsFolder = Path.Combine(_folder.Path, "blocked");
        File.WriteAllText(SettingsFolder, "block");
    }

    /// <summary>Codex と Claude Code のエンジンを、偽のクライアントで包んだ実のエンジンの型で作る（モデルの反映を見る行）</summary>
    public void UseRealCliEngines() => _realCliEngines = true;

    /// <summary>既存のモックフォルダを作り、画面を 1 つ入れておく（会話前の表示・再開の前提）</summary>
    public void SeedMockFolder(string? folder = null, string screenFile = "OrderList.html")
    {
        var store = MockFolderStore.CreateNew(folder ?? MockFolder, "受注管理", "# schema");
        store.SaveScreen(
            screenFile,
            "注文一覧",
            "注文の一覧",
            ValidHtml,
            Array.Empty<MockTransition>(),
            "初版"
        );
    }

    /// <summary>VM を作る（接続はそのまま・モックフォルダなし。不変条件の見張りを付ける）</summary>
    public MockGenerationDialogViewModel Create()
    {
        var dispatcher = new MockDialogSyncDispatcher();
        var vm = new MockGenerationDialogViewModel(
            new StubDiagramSource(Diagram),
            dispatcher,
            files: Files,
            settingsStore: Store(),
            apiKeyEngineFactory: (_, toolHost) => NewFakeEngine(toolHost),
            codexEngineFactory: (profile, toolHost) =>
                _realCliEngines
                    ? NewCodexEngine(profile, toolHost, dispatcher)
                    : NewFakeEngine(toolHost),
            claudeCodeEngineFactory: (profile, toolHost) =>
                _realCliEngines
                    ? NewClaudeCodeEngine(profile, toolHost, dispatcher)
                    : NewFakeEngine(toolHost),
            copilotEngineFactory: (_, toolHost) => NewFakeEngine(toolHost),
            mockProjectGenerator: Generator,
            dialogService: Dialogs,
            apiKeyLoader: Keys.Load,
            apiKeySaver: Keys.Save,
            scheduleLogFlush: action => ScheduleLogFlush(action)
        );
        vm.OpenFolder = Opened.Add;
        Watch(vm);
        _vms.Add(vm);
        return vm;
    }

    /// <summary>API キー（ローカル LLM＝キー任意で接続済み）の VM を作り、既定ではモックフォルダも指定する</summary>
    public MockGenerationDialogViewModel CreateApiKeyReady(bool setMockFolder = true)
    {
        var vm = Create();
        vm.Connection.ApiProvider = AiProvider.LocalLlm;

        if (setMockFolder)
        {
            vm.MockFolder = MockFolder;
        }

        return vm;
    }

    /// <summary>接続方式を切り替える（不変条件 2 の後半を確かめる）</summary>
    public bool TryChangeBackend(MockGenerationDialogViewModel vm, ErChatBackendKind backend)
    {
        var could = vm.CanChangeBackend;
        var before = vm.Connection.SelectedBackend;

        var changed = vm.TryChangeBackend(backend);

        if (before != backend)
        {
            if (!could)
            {
                if (changed || vm.Connection.SelectedBackend != before)
                {
                    _violations.Add($"不変条件 2：切り替えられない状態で {backend} へ切り替わった");
                }

                if (vm.StatusMessage != MockStrings.Mock_SwitchBackendBlockedDuringTurn)
                {
                    _violations.Add("不変条件 2：切り替えを断った理由が状態の文に出ていない");
                }
            }
            else if (!changed || vm.Connection.SelectedBackend != backend)
            {
                _violations.Add($"不変条件 2：切り替えられる状態で {backend} へ切り替わらなかった");
            }
        }

        return changed;
    }

    /// <summary>会話を開始し、ターンの完了を保留した状態で 1 ターン送る（ターンの実行中を作る）</summary>
    public async Task<FakeChatEngine> StartHeldTurnAsync(MockGenerationDialogViewModel vm)
    {
        vm.StartConversationCommand.Execute(null);
        var engine = Engine;
        engine.HoldTurnCompletion = true;

        vm.UserInput = "要望";
        await vm.SendMessageCommand.ExecuteAsync(null);
        vm.IsTurnInProgress.Should().BeTrue("ターン完了を保留しているので実行中のまま");

        return engine;
    }

    /// <summary>save_screen の引数の JSON</summary>
    public static string SaveScreenArgs(
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

    /// <summary>save_screen を 1 回再生する（会話の開始済みが前提）</summary>
    public static async Task SaveScreenViaEngine(
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

    /// <summary>ツールを引数で 1 回再生する（会話の開始済みが前提）</summary>
    public static async Task RunToolViaEngine(
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

    /// <summary>Claude Code で新しいモックフォルダの会話を開始し、初回の送信で画面を 1 つ保存する（第 2 ステップの前提）</summary>
    public async Task SaveScreenOnClaudeCodeAsync(MockGenerationDialogViewModel vm)
    {
        vm.Connection.SelectedBackend = ErChatBackendKind.ClaudeCode;
        vm.ApplyClaudeCodeReadiness(true, "ログイン済み", ConnectionHealth.Ready, string.Empty);
        vm.MockFolder = MockFolder;

        vm.StartConversationCommand.Execute(null);
        Engine.ScriptedToolCall = (MockFolderDesignTools.SaveScreenToolName, SaveScreenArgs());
        vm.UserInput = "提出";
        await vm.SendMessageCommand.ExecuteAsync(null);
    }

    /// <summary>生成の可否を確認し、画面を 1 つ作り、出力フォルダ（out）とプロジェクト名（AcmeMock）を入れる</summary>
    public async Task PrepareGenerationAsync(MockGenerationDialogViewModel vm)
    {
        await vm.RefreshMockGenAvailabilityAsync();
        await SaveScreenOnClaudeCodeAsync(vm);
        vm.OutputFolder = PathOf("out");
        vm.ProjectName = "AcmeMock";
    }

    /// <summary>生成を止めた状態で始める（生成の実行中を作る。行の終わりに組が進める）</summary>
    public Task StartHeldGeneration(MockGenerationDialogViewModel vm)
    {
        Generator.HoldNext();
        var run = vm.GenerateMockProjectCommand.ExecuteAsync(null);
        vm.IsMockGenInProgress.Should().BeTrue("生成を止めているので実行中のまま");
        _heldGenerations.Add(run);
        return run;
    }

    /// <summary>エンジンを破棄する（行の終わりの破棄は、ここを通した VM には重ねない）</summary>
    public void ShutdownEngines(MockGenerationDialogViewModel vm, TimeSpan timeout)
    {
        lock (_shutDown)
        {
            _shutDown.Add(vm);
        }

        vm.ShutdownEngines(timeout);
    }

    /// <summary>行の本体の後に、実行中を解き、エンジンを破棄して、見張った不変条件を表明する</summary>
    public async Task FinishAsync()
    {
        foreach (var vm in _vms)
        {
            if (!vm.IsTurnInProgress)
            {
                CheckNoStreaming(vm, "行の終わり");
            }
        }

        // 不変条件 4：止めた生成を進め、保留のターンを終えれば、どちらの実行中も解ける
        Generator.Release();

        foreach (var run in _heldGenerations)
        {
            await run.WaitAsync(TimeSpan.FromSeconds(60));
        }

        foreach (var engine in Engines.Where(HasPendingTurn).ToList())
        {
            engine.CompleteHeldTurn();
        }

        foreach (var vm in _vms)
        {
            if (vm.IsTurnInProgress)
            {
                _violations.Add("不変条件 4：保留のターンを終えてもターンの実行中が解けない");
            }

            if (vm.IsMockGenInProgress)
            {
                _violations.Add("不変条件 4：止めた生成を進めても生成の実行中が解けない");
            }

            CheckNoStreaming(vm, "実行中を解いた後");
            CheckState(vm);
            CheckScreens(vm);
            CheckAttachmentSupport(vm);
            CheckFullyQualified(vm);
        }

        // 不変条件 5：行の終わりの破棄の後、作ったエンジンはちょうど 1 回破棄されている
        foreach (var vm in _vms)
        {
            bool already;

            lock (_shutDown)
            {
                already = _shutDown.Contains(vm);
            }

            if (!already)
            {
                vm.ShutdownEngines(TimeSpan.FromSeconds(60));
            }

            if (vm.DetachedDisposalForTests is { } detached)
            {
                await detached.WaitAsync(TimeSpan.FromSeconds(60));
            }
        }

        foreach (var (name, disposeCount) in _disposeProbes)
        {
            var count = disposeCount();

            if (count != 1)
            {
                _violations.Add($"不変条件 5：{name} の破棄が {count} 回（ちょうど 1 回のはず）");
            }
        }

        CheckFirstTurnModel();

        _violations.Should().BeEmpty();
    }

    public void Dispose() => _folder.Dispose();

    /// <summary>偽のエンジンを作り、破棄の回数と完了の記録を見張る</summary>
    private FakeChatEngine NewFakeEngine(IErDiagramToolHost toolHost)
    {
        var engine = new FakeChatEngine(toolHost);
        var completions = new List<(int SentCount, bool Success)>();
        _completions[engine] = completions;
        // セッションより先に購読する（完了の時点で送り終えたプロンプトの数を控える）
        engine.TurnCompleted += (_, result) =>
            completions.Add((engine.SentPrompts.Count, result.Success));
        Engines.Add(engine);
        _disposeProbes.Add(($"偽のエンジン {Engines.Count}", () => engine.DisposeCount));
        return engine;
    }

    private ClaudeCodeChatEngine NewClaudeCodeEngine(
        ErChatProfile profile,
        IErDiagramToolHost toolHost,
        IUiDispatcher dispatcher
    )
    {
        var client = new QuickER.Tests.AI.DisposeTrackingClaudeCodeClient();
        var engine = new ClaudeCodeChatEngine(client, toolHost, dispatcher, profile);
        ClaudeCodeEngine = engine;
        _disposeProbes.Add(("実の Claude Code のエンジン", () => client.DisposeCount));
        return engine;
    }

    private CodexChatEngine NewCodexEngine(
        ErChatProfile profile,
        IErDiagramToolHost toolHost,
        IUiDispatcher dispatcher
    )
    {
        var client = new QuickER.Tests.AI.FakeCodexAppServerClient();
        var engine = new CodexChatEngine(client, toolHost, dispatcher, profile);
        CodexEngine = engine;
        _disposeProbes.Add(("実の Codex のエンジン", () => client.DisposeCount));
        return engine;
    }

    /// <summary>保留のターン（送ったのに完了していないターン）があるか</summary>
    private bool HasPendingTurn(FakeChatEngine engine) =>
        engine.HoldTurnCompletion && engine.SentPrompts.Count > _completions[engine].Count;

    /// <summary>VM の変更通知と接続の変更通知に見張りを付ける</summary>
    private void Watch(MockGenerationDialogViewModel vm)
    {
        var lastFolder = vm.MockFolder;

        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MockGenerationDialogViewModel.MockFolder))
            {
                if (vm.IsTurnInProgress && vm.MockFolder != lastFolder)
                {
                    _violations.Add(
                        $"不変条件 3：ターンの実行中にモックフォルダが {vm.MockFolder} へ変わった"
                    );
                }

                lastFolder = vm.MockFolder;
                CheckFullyQualified(vm);
            }

            CheckState(vm);
        };

        vm.Connection.PropertyChanged += (_, e) => OnConnectionChanged(vm, e);
    }

    private void OnConnectionChanged(MockGenerationDialogViewModel vm, PropertyChangedEventArgs e)
    {
        if (
            e.PropertyName == nameof(ChatConnectionSettingsViewModel.SelectedBackend)
            && vm.IsTurnInProgress
        )
        {
            _violations.Add(
                $"不変条件 2：ターンの実行中に接続方式が {vm.Connection.SelectedBackend} へ変わった"
            );
        }

        if (
            e.PropertyName
            is nameof(ChatConnectionSettingsViewModel.SelectedBackend)
                or nameof(ChatConnectionSettingsViewModel.ApiProvider)
        )
        {
            CheckAttachmentSupport(vm);
        }
    }

    /// <summary>不変条件 7・8（生成の可否と理由・生成中の会話）</summary>
    private void CheckState(MockGenerationDialogViewModel vm)
    {
        if (vm.CanGenerateMockProject != (vm.MockGenDisabledReason.Length == 0))
        {
            _violations.Add(
                $"不変条件 7：CanGenerateMockProject={vm.CanGenerateMockProject} と無効の理由「{vm.MockGenDisabledReason}」が食い違う"
            );
        }

        if (vm.IsMockGenInProgress)
        {
            if (
                vm.CanStartConversation
                || vm.CanSendMessage
                || vm.ConversationDisabledReason is null
            )
            {
                _violations.Add(
                    "不変条件 8：生成中に会話を開始・送信できるか、止めている理由が無い"
                );
            }
        }
        else if (vm.ConversationDisabledReason is not null)
        {
            _violations.Add("不変条件 8：生成中でないのに会話を止めている理由がある");
        }
    }

    /// <summary>不変条件 9（画面一覧と選択）</summary>
    private void CheckScreens(MockGenerationDialogViewModel vm)
    {
        if (
            vm.SelectedScreen is { } selected
            && !vm.Screens.Any(screen => ReferenceEquals(screen, selected))
        )
        {
            _violations.Add($"不変条件 9：選択中の画面 {selected.File} が画面一覧に無い");
        }

        if (vm.HasScreens != (vm.Screens.Count > 0))
        {
            _violations.Add("不変条件 9：HasScreens と画面一覧が食い違う");
        }
    }

    /// <summary>不変条件 10（添付の対応範囲）</summary>
    private void CheckAttachmentSupport(MockGenerationDialogViewModel vm)
    {
        var expected = AttachmentSupportResolver.ForBackend(
            vm.Connection.SelectedBackend,
            vm.Connection.ApiProvider
        );

        if (vm.Attachments.Support != expected)
        {
            _violations.Add(
                $"不変条件 10：添付の対応範囲 {vm.Attachments.Support} が {vm.Connection.SelectedBackend} の範囲 {expected} と違う"
            );
        }
    }

    /// <summary>不変条件 11（フルパスでないモックフォルダでは会話を開始できない）</summary>
    private void CheckFullyQualified(MockGenerationDialogViewModel vm)
    {
        var folder = vm.MockFolder.Trim();

        if (folder.Length > 0 && !Path.IsPathFullyQualified(folder) && vm.CanStartConversation)
        {
            _violations.Add(
                $"不変条件 11：フルパスでないモックフォルダ {folder} で会話を開始できる"
            );
        }
    }

    /// <summary>ターンの外で応答中の吹き出しが残っていれば違反を記録する（不変条件 1）</summary>
    private void CheckNoStreaming(MockGenerationDialogViewModel vm, string when)
    {
        if (vm.Messages.Any(message => message.IsStreaming))
        {
            _violations.Add($"不変条件 1：ターンの外（{when}）に応答中の吹き出しが残った");
        }
    }

    /// <summary>不変条件 6（エンジンごとに、成功した完了より前のプロンプトだけが開始のプロンプト）</summary>
    private void CheckFirstTurnModel()
    {
        var heading = MockStrings.Mock_PromptUserInstructionsHeading;

        foreach (var (engine, completions) in _completions)
        {
            for (var i = 0; i < engine.SentPrompts.Count; i++)
            {
                var expectedInitial = !completions.Any(c => c.Success && c.SentCount <= i);
                var isInitial = engine.SentPrompts[i].Contains(heading, StringComparison.Ordinal);

                if (expectedInitial != isInitial)
                {
                    _violations.Add(
                        expectedInitial
                            ? $"不変条件 6：初回ターンが成功する前の {i + 1} 回目の送信がフィードバックになった"
                            : $"不変条件 6：初回ターンが成功した後の {i + 1} 回目の送信が開始のプロンプトになった"
                    );
                }
            }
        }
    }
}
