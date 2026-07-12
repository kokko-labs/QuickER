using FluentAssertions;
using QuickER.AI;
using QuickER.AI.Chat;
using AiStrings = QuickER.AI.Resources.Strings;

namespace QuickER.Tests.Services.Chat;

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

    /// <summary>ツール呼び出しを記録し、定型結果を返すフェイクホスト</summary>
    private sealed class RecordingToolHost : IErDiagramToolHost
    {
        public List<(string Tool, string Args)> Calls { get; } = new();

        public (string Result, bool Success) Execute(string toolName, string argumentsJson)
        {
            Calls.Add((toolName, argumentsJson));
            return ($"{toolName} 実行済み", true);
        }
    }

    private static ChatTurnEngine CreateEngine(
        ScriptedTurnDriver driver,
        RecordingToolHost host,
        bool isReady = true
    ) => new(driver, host, new SyncUiDispatcher(), () => isReady, ErDesignProfile.ErDesign);

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
