using System.Reflection;
using Anthropic.Models.Messages;
using AwesomeAssertions;
using OpenAI.Chat;
using QuickER.AI;
using QuickER.AI.Chat;
using AiStrings = QuickER.AI.Resources.Strings;

namespace QuickER.Tests.AI;

/// <summary><see cref="ChatTurnEngine"/> のツール呼び出しループ・ストリーミング・完了通知を検証するテストクラス</summary>
public class ChatTurnEngineTests
{
    /// <summary>UI スレッドへのマーシャリングを同期実行で代替するテスト用ディスパッチャ</summary>
    private sealed class SyncUiDispatcher : IUiDispatcher
    {
        public T Invoke<T>(Func<T> func) => func();
    }

    /// <summary>スクリプト化したアシスタント応答を順に返すフェイクドライバ</summary>
    private sealed class ScriptedTurnDriver : IChatTurnDriver
    {
        private readonly Queue<ChatAssistantTurn> _turns;

        public ScriptedTurnDriver(IEnumerable<ChatAssistantTurn> turns) =>
            _turns = new Queue<ChatAssistantTurn>(turns);

        /// <summary>各ターン実行時点の履歴件数を記録する</summary>
        public List<int> HistoryCountsAtCall { get; } = new();

        /// <summary>各ターン実行時点の履歴スナップショット（添付検証用）</summary>
        public List<IReadOnlyList<ChatHistoryItem>> HistoriesAtCall { get; } = new();

        public Task<ChatAssistantTurn> RunAsync(
            IReadOnlyList<ChatHistoryItem> history,
            Action<string> onTextDelta,
            CancellationToken cancellationToken
        )
        {
            HistoryCountsAtCall.Add(history.Count);
            HistoriesAtCall.Add(history.ToList());
            var turn = _turns.Dequeue();

            if (!string.IsNullOrEmpty(turn.Text))
            {
                onTextDelta(turn.Text);
            }

            return Task.FromResult(turn);
        }
    }

    // RecordingToolHost は共有版（QuickER.Tests.AI.RecordingToolHost）を使用する

    private static ChatTurnEngine CreateEngine(
        ScriptedTurnDriver driver,
        RecordingToolHost host,
        bool isReady = true
    ) => new(driver, host, new SyncUiDispatcher(), () => isReady, ErDesignProfile.ErDesign);

    /// <summary>任意のツールホストでエンジンを生成する（例外を投げるフェイク等の検証用）</summary>
    private static ChatTurnEngine CreateEngineFor(
        ScriptedTurnDriver driver,
        IErDiagramToolHost host
    ) => new(driver, host, new SyncUiDispatcher(), () => true, ErDesignProfile.ErDesign);

    /// <summary>画像添付を受け付けるエンジンを生成する（添付履歴系テスト用）</summary>
    private static ChatTurnEngine CreateImageEngine(
        ScriptedTurnDriver driver,
        RecordingToolHost host
    ) =>
        new(
            driver,
            host,
            new SyncUiDispatcher(),
            () => true,
            ErDesignProfile.ErDesign,
            attachmentSupport: () => AttachmentSupport.Images
        );

    /// <summary>ツール呼び出しの無いターンが、ストリーミングと成功完了で終わることを検証する</summary>
    [Fact(DisplayName = "ツール無しターンは delta を流し成功完了する")]
    public async Task SendAsync_NoToolCalls_StreamsAndCompletes()
    {
        var driver = new ScriptedTurnDriver([new ChatAssistantTurn("こんにちは", [])]);
        var host = new RecordingToolHost();
        var engine = CreateEngine(driver, host);

        var deltas = new List<string>();
        ErChatTurnResult? completed = null;
        engine.AssistantDeltaReceived += (_, d) => deltas.Add(d);
        engine.TurnCompleted += (_, r) => completed = r;

        await engine.StartConversationAsync(TestContext.Current.CancellationToken);
        await engine.SendAsync("やあ", TestContext.Current.CancellationToken);

        deltas.Should().ContainSingle().Which.Should().Be("こんにちは");
        host.Calls.Should().BeEmpty();
        completed.Should().NotBeNull();
        completed!.Value.Success.Should().BeTrue();
    }

    /// <summary>
    /// 活動通知の購読側が例外を投げても、実行したツールの結果が履歴へ 1 件だけ積まれることを検証する。
    /// </summary>
    /// <remarks>
    /// 通知を保護せず、しかも履歴へ積む前に通知していると、購読側の例外が外側の catch へ落ちて
    /// <b>実行済みの呼び出しへ「実行されなかった」合成結果</b>が積まれる。AI はそれを見て同じ操作を
    /// やり直す（エンティティの二重追加）。保護と順序の両方で守る。
    /// </remarks>
    [Fact(DisplayName = "活動通知の購読側が例外を投げても、実結果が履歴へ 1 件だけ積まれる")]
    public async Task SendAsync_WhenToolActivitySubscriberThrows_KeepsRealToolResult()
    {
        var driver = new ScriptedTurnDriver([
            new ChatAssistantTurn(
                string.Empty,
                [new ChatToolCallRequest("call_1", "add_entity", "{\"table_name\":\"Book\"}")]
            ),
            new ChatAssistantTurn("テーブルを追加しました", []),
        ]);
        var host = new RecordingToolHost();
        var engine = CreateEngine(driver, host);
        var statuses = new List<string>();

        engine.ToolActivityReceived += (_, _) => throw new InvalidOperationException("UI が落ちた");
        engine.StatusChanged += (_, m) => statuses.Add(m);

        await engine.StartConversationAsync(TestContext.Current.CancellationToken);
        await engine.SendAsync("本のテーブルを作って", TestContext.Current.CancellationToken);

        // ツールは 1 回だけ実行され、ターンは最後まで進む
        host.Calls.Should().ContainSingle();
        driver.HistoryCountsAtCall.Should().HaveCount(2, "通知の失敗でターンを止めない");

        // 2 回目のドライバ呼び出し時点の履歴に、ツール結果が実結果 1 件だけ積まれている
        // （合成の「実行されなかった」が混ざらない）
        var toolResults = driver
            .HistoriesAtCall[1]
            .Where(item => item.Role == ChatHistoryRole.Tool)
            .ToList();
        toolResults.Should().ContainSingle();
        toolResults[0].ToolCallId.Should().Be("call_1");
        toolResults[0]
            .Text.Should()
            .NotContain("not executed", "実行済みの呼び出しへ合成結果を積まない");

        // 通知の失敗は握り潰さず、状態メッセージで知らせる
        statuses
            .Should()
            .Contain(string.Format(AiStrings.Chat_ToolActivityNotifyFailed, "UI が落ちた"));
    }

    /// <summary>ツール要求ターン→ツール実行→完了ターンのループが正しく回ることを検証する</summary>
    [Fact(DisplayName = "ツール要求ターンはツールを実行し結果を履歴へ積んで継続する")]
    public async Task SendAsync_WithToolCall_ExecutesToolThenCompletes()
    {
        var driver = new ScriptedTurnDriver([
            new ChatAssistantTurn(
                string.Empty,
                [new ChatToolCallRequest("call_1", "add_entity", "{\"table_name\":\"Book\"}")]
            ),
            new ChatAssistantTurn("テーブルを追加しました", []),
        ]);
        var host = new RecordingToolHost();
        var engine = CreateEngine(driver, host);

        var activities = new List<ErChatToolActivity>();
        ErChatTurnResult? completed = null;
        engine.ToolActivityReceived += (_, a) => activities.Add(a);
        engine.TurnCompleted += (_, r) => completed = r;

        await engine.StartConversationAsync(TestContext.Current.CancellationToken);
        await engine.SendAsync("本のテーブルを作って", TestContext.Current.CancellationToken);

        host.Calls.Should().ContainSingle();
        host.Calls[0].Tool.Should().Be("add_entity");
        host.Calls[0].Args.Should().Contain("Book");
        activities.Should().ContainSingle();
        activities[0].ToolName.Should().Be("add_entity");
        activities[0].Success.Should().BeTrue();
        completed!.Value.Success.Should().BeTrue();

        // 2 回目のドライバ呼び出し時点では、user＋assistant(tool)＋tool 結果が履歴へ積まれている
        driver.HistoryCountsAtCall.Should().HaveCount(2);
        driver.HistoryCountsAtCall[1].Should().BeGreaterThan(driver.HistoryCountsAtCall[0]);
    }

    /// <summary>
    /// ツールが例外を投げても、失敗のツール結果へ畳んでターンが続くことを検証する（RA2）。
    /// </summary>
    /// <remarks>
    /// 例外をそのまま伝播させると 1 件の失敗でターン全体が失敗し、後続のツールが実行されない。
    /// Claude Code / Codex / Copilot の 3 エンジンは失敗結果を返して続行するため、API キー接続も揃える。
    /// </remarks>
    [Fact(DisplayName = "ツールの例外は失敗のツール結果になりターンは続く")]
    public async Task SendAsync_ToolThrows_ReportsFailedResultAndContinues()
    {
        var driver = new ScriptedTurnDriver([
            new ChatAssistantTurn(
                string.Empty,
                [
                    new ChatToolCallRequest("call_1", "boom", "{}"),
                    new ChatToolCallRequest("call_2", "add_entity", "{}"),
                ]
            ),
            new ChatAssistantTurn("続行しました", []),
        ]);
        var host = new ThrowingToolHost("boom", "disk on fire");
        var engine = CreateEngineFor(driver, host);

        var activities = new List<ErChatToolActivity>();
        ErChatTurnResult? completed = null;
        engine.ToolActivityReceived += (_, a) => activities.Add(a);
        engine.TurnCompleted += (_, r) => completed = r;

        await engine.StartConversationAsync(TestContext.Current.CancellationToken);
        await engine.SendAsync("やって", TestContext.Current.CancellationToken);

        // ターンは成功として完了し、後続のツールも実行されている
        completed!.Value.Success.Should().BeTrue();
        host.Calls.Select(call => call.Tool).Should().Equal("boom", "add_entity");

        // 例外は「失敗のツール結果」として AI にも UI にも伝わる
        activities.Should().HaveCount(2);
        activities[0].Success.Should().BeFalse();
        activities[0].Result.Should().Be("The tool 'boom' threw an exception: disk on fire");
        activities[1].Success.Should().BeTrue();
    }

    /// <summary>
    /// ツールが例外を投げた後も、履歴の tool_use と tool 結果の対応（id の 1 対 1）が崩れないことを検証する（RA2）。
    /// </summary>
    [Fact(DisplayName = "ツールの例外の後も履歴の tool 結果対応は保たれる")]
    public async Task SendAsync_ToolThrows_KeepsToolResultPairing()
    {
        var driver = new ScriptedTurnDriver([
            new ChatAssistantTurn(
                string.Empty,
                [
                    new ChatToolCallRequest("call_1", "boom", "{}"),
                    new ChatToolCallRequest("call_2", "add_entity", "{}"),
                ]
            ),
            new ChatAssistantTurn("完了", []),
        ]);
        var host = new ThrowingToolHost("boom", "disk on fire");
        var engine = CreateEngineFor(driver, host);

        await engine.StartConversationAsync(TestContext.Current.CancellationToken);
        await engine.SendAsync("やって", TestContext.Current.CancellationToken);

        // 2 回目のドライバ呼び出し時点の履歴で、tool_use の id すべてに tool 結果が 1 対 1 で対応する
        var history = driver.HistoriesAtCall[1];
        var requestedIds = history
            .Where(item => item.ToolCalls is not null)
            .SelectMany(item => item.ToolCalls!)
            .Select(call => call.Id)
            .ToList();
        var resultIds = history
            .Where(item => item.Role == ChatHistoryRole.Tool)
            .Select(item => item.ToolCallId)
            .ToList();

        requestedIds.Should().Equal("call_1", "call_2");
        resultIds.Should().Equal("call_1", "call_2");
    }

    /// <summary>
    /// ツールの例外は往復として数えない（失敗結果を返して同じ往復の中で続けるだけ）ことを検証する（RA2 × C3）。
    /// </summary>
    [Fact(DisplayName = "ツールの例外はツールループの往復数を増やさない")]
    public async Task SendAsync_ToolThrows_DoesNotChangeRoundTripCount()
    {
        var driver = new ScriptedTurnDriver([
            new ChatAssistantTurn(string.Empty, [new ChatToolCallRequest("call_1", "boom", "{}")]),
            new ChatAssistantTurn("完了", []),
        ]);
        var engine = CreateEngineFor(driver, new ThrowingToolHost("boom", "x"));

        await engine.StartConversationAsync(TestContext.Current.CancellationToken);
        await engine.SendAsync("やって", TestContext.Current.CancellationToken);

        // ドライバ呼び出しは 2 回（ツール要求ターン＋続きのターン）＝例外があっても往復の数え方は変わらない
        driver.HistoryCountsAtCall.Should().HaveCount(2);
    }

    /// <summary>指定した名前のツールだけ例外を投げ、それ以外は成功を返すフェイクツールホスト</summary>
    private sealed class ThrowingToolHost(string throwingTool, string message) : IErDiagramToolHost
    {
        public List<(string Tool, string Args)> Calls { get; } = new();

        public (string Result, bool Success) Execute(string toolName, string argumentsJson)
        {
            Calls.Add((toolName, argumentsJson));

            if (toolName == throwingTool)
            {
                throw new InvalidOperationException(message);
            }

            return ($"{toolName} 実行済み", true);
        }
    }

    /// <summary>会話開始で履歴がシステムプロンプトのみにリセットされることを検証する</summary>
    [Fact(DisplayName = "StartConversation はシステムプロンプトで履歴を初期化する")]
    public async Task StartConversation_InitializesHistoryWithSystemPrompt()
    {
        var driver = new ScriptedTurnDriver([new ChatAssistantTurn("ok", [])]);
        var engine = CreateEngine(driver, new RecordingToolHost());

        await engine.StartConversationAsync(TestContext.Current.CancellationToken);
        await engine.SendAsync("hi", TestContext.Current.CancellationToken);

        // 1 回目の呼び出し時点の履歴 = system + user の 2 件
        driver.HistoryCountsAtCall[0].Should().Be(2);
    }

    /// <summary>添付付き送信で、添付が User 履歴項目に載ることを検証する</summary>
    [Fact(DisplayName = "添付は User 履歴項目に載る")]
    public async Task SendAsync_WithAttachments_StoredOnUserHistoryItem()
    {
        var driver = new ScriptedTurnDriver([new ChatAssistantTurn("ok", [])]);
        var engine = CreateImageEngine(driver, new RecordingToolHost());

        byte[] pngData = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        var attachment = new ChatAttachment(
            "a.png",
            ChatAttachmentKind.Image,
            "image/png",
            pngData
        );

        await engine.StartConversationAsync(TestContext.Current.CancellationToken);
        await engine.SendAsync("図を見て", [attachment], TestContext.Current.CancellationToken);

        var history = driver.HistoriesAtCall[0];
        var userItem = history.Single(item => item.Role == ChatHistoryRole.User);
        userItem.Attachments.Should().ContainSingle();
        userItem.Attachments![0].FileName.Should().Be("a.png");
    }

    /// <summary>
    /// 2 ターン目（添付なし）でも、1 ターン目の添付付き User 項目が履歴に残り再送されることを検証する
    /// （ステートレス API の毎ターン全履歴送信で添付が再構築される）。
    /// </summary>
    [Fact(DisplayName = "添付付き履歴は次ターンでも履歴に残り再送される")]
    public async Task SendAsync_SecondTurn_RetainsAttachmentHistory()
    {
        var driver = new ScriptedTurnDriver([
            new ChatAssistantTurn("ok1", []),
            new ChatAssistantTurn("ok2", []),
        ]);
        var engine = CreateImageEngine(driver, new RecordingToolHost());

        byte[] pngData = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        var attachment = new ChatAttachment(
            "a.png",
            ChatAttachmentKind.Image,
            "image/png",
            pngData
        );

        await engine.StartConversationAsync(TestContext.Current.CancellationToken);
        await engine.SendAsync("図を見て", [attachment], TestContext.Current.CancellationToken);
        await engine.SendAsync("続けて", TestContext.Current.CancellationToken);

        // 2 ターン目の履歴にも 1 ターン目の添付付き User 項目が含まれる
        var secondTurnHistory = driver.HistoriesAtCall[1];
        secondTurnHistory
            .Count(item => item.Role == ChatHistoryRole.User && item.Attachments is { Count: > 0 })
            .Should()
            .Be(1);
    }

    /// <summary>
    /// サポート外種別の添付を含む送信は、防御的に NotSupportedException で弾かれ、
    /// TurnCompleted に失敗（エラーメッセージ付き）が通知されることを検証する。
    /// </summary>
    [Fact(DisplayName = "サポート外種別の添付は分かる失敗になる")]
    public async Task SendAsync_UnsupportedAttachmentKind_FailsTurn()
    {
        var driver = new ScriptedTurnDriver([new ChatAssistantTurn("ok", [])]);
        // Images のみ対応のエンジンへ PDF を渡す
        var engine = new ChatTurnEngine(
            driver,
            new RecordingToolHost(),
            new SyncUiDispatcher(),
            () => true,
            ErDesignProfile.ErDesign,
            attachmentSupport: () => AttachmentSupport.Images
        );

        ErChatTurnResult? completed = null;
        engine.TurnCompleted += (_, r) => completed = r;

        var pdf = new ChatAttachment(
            "spec.pdf",
            ChatAttachmentKind.Pdf,
            "application/pdf",
            "%PDF-1.7"u8.ToArray()
        );

        await engine.StartConversationAsync(TestContext.Current.CancellationToken);
        await engine.SendAsync("見て", [pdf], TestContext.Current.CancellationToken);

        completed.Should().NotBeNull();
        completed!.Value.Success.Should().BeFalse();

        // 製品コードと同じ resx キーからフォーマット済みメッセージを導出し、カルチャに依らず完全一致で検証する
        completed!
            .Value.Error.Should()
            .Be(
                string.Format(
                    AiStrings.Chat_UnsupportedAttachment,
                    pdf.FileName,
                    ChatAttachmentKind.Pdf
                )
            );

        // ドライバは呼ばれない（ガードで送信前に弾かれる）
        driver.HistoryCountsAtCall.Should().BeEmpty();
    }

    /// <summary>
    /// ツール実行中に中断（<see cref="ChatTurnEngine.InterruptAsync"/>）を要求するフェイクホスト。
    /// 1 回目の Execute 内で中断要求を出し、2 回目以降が実行されないことを検証するのに使う。
    /// エンジン自身をコンストラクタで受け取れない（構築が相互依存になる）ため、後差しのプロパティで持つ。
    /// </summary>
    private sealed class InterruptingToolHost : IErDiagramToolHost
    {
        public ChatTurnEngine? Engine { get; set; }

        public List<(string Tool, string Args)> Calls { get; } = new();

        public (string Result, bool Success) Execute(string toolName, string argumentsJson)
        {
            Calls.Add((toolName, argumentsJson));
            Engine!.InterruptAsync().GetAwaiter().GetResult();
            return ($"{toolName} 実行済み", true);
        }
    }

    /// <summary>
    /// 最初の RunAsync 呼び出しの中でエンジンへ中断を要求してからターンを返すフェイクドライバ
    /// （ツール実行前の中断＝foreach 先頭の ThrowIfCancellationRequested での中断を再現する）。
    /// </summary>
    private sealed class InterruptingTurnDriver : IChatTurnDriver
    {
        private readonly Queue<ChatAssistantTurn> _turns;
        private bool _shouldInterruptNextCall = true;

        public InterruptingTurnDriver(IEnumerable<ChatAssistantTurn> turns) =>
            _turns = new Queue<ChatAssistantTurn>(turns);

        /// <summary>後差しで設定するエンジン参照（構築が相互依存になるため）</summary>
        public ChatTurnEngine? Engine { get; set; }

        /// <summary>各ターン実行時点の履歴スナップショット</summary>
        public List<IReadOnlyList<ChatHistoryItem>> HistoriesAtCall { get; } = new();

        public Task<ChatAssistantTurn> RunAsync(
            IReadOnlyList<ChatHistoryItem> history,
            Action<string> onTextDelta,
            CancellationToken cancellationToken
        )
        {
            HistoriesAtCall.Add(history.ToList());
            var turn = _turns.Dequeue();

            if (_shouldInterruptNextCall)
            {
                _shouldInterruptNextCall = false;
                Engine!.InterruptAsync().GetAwaiter().GetResult();
            }

            return Task.FromResult(turn);
        }
    }

    /// <summary>
    /// 複数ツール呼び出しの 1 件目実行中に中断しても、2 件目に対応する Tool 項目が
    /// 合成結果として補われ、Assistant の ToolCalls と履歴の Tool 項目が過不足なく対応することを検証する。
    /// （API キー接続はステートレスで毎ターン全履歴を再送するため、対応漏れがあると以後の送信が
    /// LLM API の構造検証で失敗し続ける）
    /// </summary>
    [Fact(DisplayName = "複数ツールの途中で中断しても履歴の tool 結果対応が保たれる")]
    public async Task SendAsync_InterruptedMidMultiToolCall_SynthesizesMissingToolResult()
    {
        var driver = new ScriptedTurnDriver([
            new ChatAssistantTurn(
                string.Empty,
                [
                    new ChatToolCallRequest("call_1", "add_entity", "{\"table_name\":\"Book\"}"),
                    new ChatToolCallRequest("call_2", "add_column", "{\"column_name\":\"Title\"}"),
                ]
            ),
            new ChatAssistantTurn("続けます", []),
        ]);
        var host = new InterruptingToolHost();
        var engine = new ChatTurnEngine(
            driver,
            host,
            new SyncUiDispatcher(),
            () => true,
            ErDesignProfile.ErDesign
        );
        host.Engine = engine;

        ErChatTurnResult? completed = null;
        engine.TurnCompleted += (_, r) => completed = r;

        await engine.StartConversationAsync(TestContext.Current.CancellationToken);
        await engine.SendAsync("2 つ変更して", TestContext.Current.CancellationToken);

        // 中断は OperationCanceledException として捕捉され、失敗完了（エラーメッセージなし）になる
        completed.Should().NotBeNull();
        completed!.Value.Success.Should().BeFalse();
        completed!.Value.Error.Should().BeNull();

        // ホストの実行は 1 回だけ（2 件目は実行前に弾かれる）
        host.Calls.Should().ContainSingle();
        host.Calls[0].Tool.Should().Be("add_entity");

        // 2 ターン目を送って、1 ターン目の履歴がどう積まれたかをドライバの受信履歴から観測する
        await engine.SendAsync("再開して", TestContext.Current.CancellationToken);

        var historyAtSecondCall = driver.HistoriesAtCall[1];
        historyAtSecondCall.Should().HaveCount(6);
        historyAtSecondCall[2].Role.Should().Be(ChatHistoryRole.Assistant);
        historyAtSecondCall[2].ToolCalls.Should().HaveCount(2);

        // call_1 は実行済みの実結果
        historyAtSecondCall[3].Role.Should().Be(ChatHistoryRole.Tool);
        historyAtSecondCall[3].ToolCallId.Should().Be("call_1");
        historyAtSecondCall[3].Text.Should().Contain("実行済み");

        // call_2 は未実行のため合成結果（英語固定・「実行されなかった」ことを述べる中立文言）が補われる
        historyAtSecondCall[4].Role.Should().Be(ChatHistoryRole.Tool);
        historyAtSecondCall[4].ToolCallId.Should().Be("call_2");
        historyAtSecondCall[4].Text.Should().Contain("was not executed");
    }

    /// <summary>
    /// 1 件だけのツール呼び出しでも、実行前（foreach 先頭の ThrowIfCancellationRequested）で
    /// 中断されると、そのツール呼び出しに対応する合成 Tool 項目が履歴へ補われることを検証する。
    /// </summary>
    [Fact(DisplayName = "単一ツールでも実行前に中断すると合成結果が補われる")]
    public async Task SendAsync_InterruptedBeforeSingleToolExecutes_SynthesizesToolResult()
    {
        var driver = new InterruptingTurnDriver([
            new ChatAssistantTurn(
                string.Empty,
                [new ChatToolCallRequest("call_1", "add_entity", "{\"table_name\":\"Book\"}")]
            ),
            new ChatAssistantTurn("ok", []),
        ]);
        var host = new RecordingToolHost();
        var engine = new ChatTurnEngine(
            driver,
            host,
            new SyncUiDispatcher(),
            () => true,
            ErDesignProfile.ErDesign
        );
        driver.Engine = engine;

        ErChatTurnResult? completed = null;
        engine.TurnCompleted += (_, r) => completed = r;

        await engine.StartConversationAsync(TestContext.Current.CancellationToken);
        await engine.SendAsync("テーブルを追加して", TestContext.Current.CancellationToken);

        completed.Should().NotBeNull();
        completed!.Value.Success.Should().BeFalse();
        completed!.Value.Error.Should().BeNull();

        // ツールは 1 度も実行されない（中断が実行前に発生）
        host.Calls.Should().BeEmpty();

        // 2 ターン目を送って、合成された Tool 項目を観測する
        await engine.SendAsync("もう一度", TestContext.Current.CancellationToken);

        var historyAtSecondCall = driver.HistoriesAtCall[1];
        historyAtSecondCall.Should().HaveCount(5);
        historyAtSecondCall[2].Role.Should().Be(ChatHistoryRole.Assistant);
        historyAtSecondCall[2].ToolCalls.Should().HaveCount(1);

        historyAtSecondCall[3].Role.Should().Be(ChatHistoryRole.Tool);
        historyAtSecondCall[3].ToolCallId.Should().Be("call_1");
        historyAtSecondCall[3].Text.Should().Contain("was not executed");
    }

    /// <summary>
    /// 中断で合成補完された履歴（Assistant の複数 ToolCalls のうち一部だけ実行され、残りへ合成 Tool 項目が
    /// 補われた履歴）を、実際に本番ドライバの変換関数 <see cref="AnthropicChatTurnDriver.ToMessageParams"/>
    /// に通し、assistant メッセージ内の tool_use ブロック ID 集合と、直後に続く tool_result（ToolResultBlockParam）
    /// の ToolUseID 集合が 1 対 1 で一致することを検証する。
    /// （エンジンの補完がドライバの実変換まで壊さずに届くことの表明＝合成補完が無いと Anthropic API の
    /// 構造検証で 400 になる箇所そのものを通す）
    /// </summary>
    [Fact(
        DisplayName = "中断補完済み履歴は Anthropic 変換でも tool_use と tool_result の id が 1 対 1 になる"
    )]
    public async Task SendAsync_InterruptedHistory_AnthropicConversionKeepsToolIdsAligned()
    {
        var driver = new ScriptedTurnDriver([
            new ChatAssistantTurn(
                string.Empty,
                [
                    new ChatToolCallRequest("call_1", "add_entity", "{\"table_name\":\"Book\"}"),
                    new ChatToolCallRequest("call_2", "add_column", "{\"column_name\":\"Title\"}"),
                ]
            ),
            new ChatAssistantTurn("続けます", []),
        ]);
        var host = new InterruptingToolHost();
        var engine = new ChatTurnEngine(
            driver,
            host,
            new SyncUiDispatcher(),
            () => true,
            ErDesignProfile.ErDesign
        );
        host.Engine = engine;

        await engine.StartConversationAsync(TestContext.Current.CancellationToken);
        await engine.SendAsync("2 つ変更して", TestContext.Current.CancellationToken);
        await engine.SendAsync("再開して", TestContext.Current.CancellationToken);

        // 2 ターン目にドライバへ渡った履歴（中断で合成補完された Tool 項目を含む）を Anthropic 形式へ変換する
        var history = driver.HistoriesAtCall[1];
        var messages = AnthropicChatTurnDriver.ToMessageParams(history);

        // System は積まれないため、messages = [User, Assistant(tool_use x2), Tool(call_1), Tool(call_2), User] の 5 件
        messages.Should().HaveCount(5);

        var assistantBlocks = messages[1].Content.Value.As<IReadOnlyList<ContentBlockParam>>();
        var toolUseIds = assistantBlocks
            .Select(b => b.Value)
            .OfType<ToolUseBlockParam>()
            .Select(t => t.ID)
            .ToHashSet();

        // 直後に続く 2 件（実行済みの call_1・合成補完の call_2）の tool_result ブロックから ToolUseID を取り出す
        var toolResultIds = new[] { messages[2], messages[3] }
            .Select(m => m.Content.Value.As<IReadOnlyList<ContentBlockParam>>())
            .Select(blocks => ((ToolResultBlockParam)blocks.Single().Value!).ToolUseID)
            .ToHashSet();

        toolResultIds.Should().BeEquivalentTo(toolUseIds);
        toolUseIds.Should().BeEquivalentTo(new[] { "call_1", "call_2" });
    }

    /// <summary>
    /// 同じ中断補完済み履歴を、今度は OpenAI 側の変換関数 <see cref="OpenAiTurnDriver.ToChatMessage"/>
    /// に通し、AssistantChatMessage.ToolCalls の ID 集合と ToolChatMessage.ToolCallId 集合が
    /// 1 対 1 で一致することを検証する（ToolChatMessage は ToolCallId を 1:1 で写す＝ToChatMessage 参照）。
    /// </summary>
    [Fact(
        DisplayName = "中断補完済み履歴は OpenAI 変換でも tool_call と tool 結果の id が 1 対 1 になる"
    )]
    public async Task SendAsync_InterruptedHistory_OpenAiConversionKeepsToolIdsAligned()
    {
        var driver = new ScriptedTurnDriver([
            new ChatAssistantTurn(
                string.Empty,
                [
                    new ChatToolCallRequest("call_1", "add_entity", "{\"table_name\":\"Book\"}"),
                    new ChatToolCallRequest("call_2", "add_column", "{\"column_name\":\"Title\"}"),
                ]
            ),
            new ChatAssistantTurn("続けます", []),
        ]);
        var host = new InterruptingToolHost();
        var engine = new ChatTurnEngine(
            driver,
            host,
            new SyncUiDispatcher(),
            () => true,
            ErDesignProfile.ErDesign
        );
        host.Engine = engine;

        await engine.StartConversationAsync(TestContext.Current.CancellationToken);
        await engine.SendAsync("2 つ変更して", TestContext.Current.CancellationToken);
        await engine.SendAsync("再開して", TestContext.Current.CancellationToken);

        var history = driver.HistoriesAtCall[1];
        var messages = history.Select(OpenAiTurnDriver.ToChatMessage).ToList();

        var assistantMessage = messages
            .OfType<AssistantChatMessage>()
            .Single(m => m.ToolCalls.Count > 0);
        var toolUseIds = assistantMessage.ToolCalls.Select(tc => tc.Id).ToHashSet();

        var toolResultIds = messages
            .OfType<ToolChatMessage>()
            .Select(m => m.ToolCallId)
            .ToHashSet();

        toolResultIds.Should().BeEquivalentTo(toolUseIds);
        toolUseIds.Should().BeEquivalentTo(new[] { "call_1", "call_2" });
    }

    /// <summary>
    /// ツール要求を延々と返し続けるターンを上限＋1 個用意し、ループが上限で打ち切られ、
    /// resx 文言（往復上限）で失敗完了することを検証する。
    /// あわせてドライバ・ツール実行がちょうど上限回数だけ呼ばれることも確認する
    /// （上限判定はドライバ呼び出しの「前」に行うため、超過分の 1 回はドライバへ渡らない）。
    /// </summary>
    [Fact(DisplayName = "ツールループは上限に達すると失敗として打ち切られる")]
    public async Task SendAsync_ToolLoopExceedsLimit_FailsWithLimitMessage()
    {
        var turns = Enumerable
            .Range(0, ChatTurnEngine.MaxToolRoundTripsPerTurn + 1)
            .Select(i => new ChatAssistantTurn(
                string.Empty,
                [new ChatToolCallRequest($"call_{i}", "add_entity", "{\"table_name\":\"Book\"}")]
            ))
            .ToList();
        var driver = new ScriptedTurnDriver(turns);
        var host = new RecordingToolHost();
        var engine = CreateEngine(driver, host);

        ErChatTurnResult? completed = null;
        engine.TurnCompleted += (_, r) => completed = r;

        await engine.StartConversationAsync(TestContext.Current.CancellationToken);
        await engine.SendAsync("ずっとツールを呼び続けて", TestContext.Current.CancellationToken);

        completed.Should().NotBeNull();
        completed!.Value.Success.Should().BeFalse();
        completed!
            .Value.Error.Should()
            .Be(
                string.Format(
                    AiStrings.Chat_ToolLoopLimitReached,
                    ChatTurnEngine.MaxToolRoundTripsPerTurn
                )
            );

        driver.HistoryCountsAtCall.Should().HaveCount(ChatTurnEngine.MaxToolRoundTripsPerTurn);
        host.Calls.Should().HaveCount(ChatTurnEngine.MaxToolRoundTripsPerTurn);
    }

    /// <summary>
    /// 上限で打ち切られた直後の履歴（private フィールドをリフレクションで直接読む）でも、
    /// Assistant の tool_use と Tool 結果が過不足なく対応していることを
    /// Anthropic 変換（<see cref="AnthropicChatTurnDriver.ToMessageParams"/>）を通して検証する。
    /// 上限判定は「次の往復を駆動する前」に行うため、打ち切り時点の履歴は常に
    /// 直前の tool_use すべてに tool 結果が揃った整合状態のはず（中断時の合成補完の経路は通らない）。
    /// tool_use の件数がちょうど上限回数であることも表明するため、
    /// 上限チェックを外すと（ドライバのターンが尽きるまで回り続け）この件数がずれて赤くなる。
    /// </summary>
    [Fact(DisplayName = "上限打ち切り後も履歴の tool 結果対応は保たれている")]
    public async Task SendAsync_ToolLoopExceedsLimit_HistoryStaysAlignedForAnthropic()
    {
        var turns = Enumerable
            .Range(0, ChatTurnEngine.MaxToolRoundTripsPerTurn + 1)
            .Select(i => new ChatAssistantTurn(
                string.Empty,
                [new ChatToolCallRequest($"call_{i}", "add_entity", "{\"table_name\":\"Book\"}")]
            ))
            .ToList();
        var driver = new ScriptedTurnDriver(turns);
        var host = new RecordingToolHost();
        var engine = CreateEngine(driver, host);

        await engine.StartConversationAsync(TestContext.Current.CancellationToken);
        await engine.SendAsync("ずっとツールを呼び続けて", TestContext.Current.CancellationToken);

        // 打ち切り直後の内部履歴（private フィールド）を直接読む
        var history =
            (List<ChatHistoryItem>)
                typeof(ChatTurnEngine)
                    .GetField("_history", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .GetValue(engine)!;
        var messages = AnthropicChatTurnDriver.ToMessageParams(history);

        // Content が ContentBlockParam 一覧の形の項目だけを対象にする（プレーンテキストの System/User 項目は除外される）
        var blockMessages = messages
            .Select(m => m.Content.Value)
            .OfType<IReadOnlyList<ContentBlockParam>>()
            .SelectMany(blocks => blocks.Select(b => b.Value))
            .ToList();

        var toolUseIds = blockMessages.OfType<ToolUseBlockParam>().Select(t => t.ID).ToHashSet();
        var toolResultIds = blockMessages
            .OfType<ToolResultBlockParam>()
            .Select(t => t.ToolUseID)
            .ToHashSet();

        toolResultIds.Should().BeEquivalentTo(toolUseIds);
        toolUseIds.Should().HaveCount(ChatTurnEngine.MaxToolRoundTripsPerTurn);
    }

    /// <summary>
    /// <see cref="ChatTurnEngine.HistoryHasAttachmentsBeyond"/> が、履歴中の添付のうち
    /// 指定した対応範囲を超えるものが 1 件でもあれば true、その範囲内なら false を返すことを検証する
    /// （C5: プロバイダー切替時に「送信すると黙って落ちる添付があるか」を判定する材料）。
    /// </summary>
    [Fact(DisplayName = "HistoryHasAttachmentsBeyond は対応範囲外の添付の有無を返す")]
    public async Task HistoryHasAttachmentsBeyond_DetectsAttachmentsOutsideGivenSupport()
    {
        var driver = new ScriptedTurnDriver([new ChatAssistantTurn("ok", [])]);
        var host = new RecordingToolHost();
        // Claude 相当（画像＋PDF＋テキスト対応）のエンジンで PDF を送る
        var engine = new ChatTurnEngine(
            driver,
            host,
            new SyncUiDispatcher(),
            () => true,
            ErDesignProfile.ErDesign,
            attachmentSupport: () =>
                AttachmentSupport.Images | AttachmentSupport.Pdf | AttachmentSupport.Text
        );

        var pdf = new ChatAttachment(
            "spec.pdf",
            ChatAttachmentKind.Pdf,
            "application/pdf",
            "%PDF-1.7"u8.ToArray()
        );

        await engine.StartConversationAsync(TestContext.Current.CancellationToken);
        await engine.SendAsync("見て", [pdf], TestContext.Current.CancellationToken);

        // OpenAI／ローカル LLM 相当（画像＋テキストのみ）へ切り替えたと仮定すると、PDF は対応範囲外
        engine
            .HistoryHasAttachmentsBeyond(AttachmentSupport.Images | AttachmentSupport.Text)
            .Should()
            .BeTrue();

        // 元の対応範囲（画像＋PDF＋テキスト）のままなら対応範囲外の添付は無い
        engine
            .HistoryHasAttachmentsBeyond(
                AttachmentSupport.Images | AttachmentSupport.Pdf | AttachmentSupport.Text
            )
            .Should()
            .BeFalse();
    }

    /// <summary>添付の無い会話では、どの対応範囲を指定しても false を返すことを検証する</summary>
    [Fact(DisplayName = "添付の無い会話は HistoryHasAttachmentsBeyond が常に false")]
    public async Task HistoryHasAttachmentsBeyond_NoAttachments_ReturnsFalse()
    {
        var driver = new ScriptedTurnDriver([new ChatAssistantTurn("ok", [])]);
        var host = new RecordingToolHost();
        var engine = CreateEngine(driver, host);

        await engine.StartConversationAsync(TestContext.Current.CancellationToken);
        await engine.SendAsync("やあ", TestContext.Current.CancellationToken);

        engine.HistoryHasAttachmentsBeyond(AttachmentSupport.None).Should().BeFalse();
        engine.HistoryHasAttachmentsBeyond(AttachmentSupport.Images).Should().BeFalse();
    }

    /// <summary>AttachmentSupport はコンストラクタ注入の関数で決まることを検証する（既定は None）</summary>
    [Fact(DisplayName = "AttachmentSupport は注入関数で決まる（既定 None）")]
    public void AttachmentSupport_ReflectsInjectedSelector()
    {
        var driver = new ScriptedTurnDriver([]);
        var host = new RecordingToolHost();

        var defaultEngine = new ChatTurnEngine(
            driver,
            host,
            new SyncUiDispatcher(),
            () => true,
            ErDesignProfile.ErDesign
        );
        defaultEngine.AttachmentSupport.Should().Be(AttachmentSupport.None);

        var imageEngine = new ChatTurnEngine(
            driver,
            host,
            new SyncUiDispatcher(),
            () => true,
            ErDesignProfile.ErDesign,
            attachmentSupport: () => AttachmentSupport.Images
        );
        imageEngine.AttachmentSupport.Should().Be(AttachmentSupport.Images);
    }
}
