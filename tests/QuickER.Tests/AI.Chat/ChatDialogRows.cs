using System.Reflection;
using QuickER.AI;
using QuickER.AI.Chat;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.AI.Chat;

/// <summary>チャットのダイアログの表の行が置く状態（ターン・会話の状態）</summary>
internal enum ChatDialogState
{
    /// <summary>作った直後（会話なし）</summary>
    Idle,

    /// <summary>会話を開いた（ターンの外）</summary>
    Conversation,

    /// <summary>会話の開始を待っている</summary>
    Starting,

    /// <summary>ターンの実行中</summary>
    TurnInProgress,
}

/// <summary>チャットのダイアログの表の行の手の種類</summary>
internal enum ChatDialogOp
{
    /// <summary>接続方式の切替（TryChangeBackend・タブ・Initialize）</summary>
    SwitchBackend,

    /// <summary>API キー接続のプロバイダー・キーの変更</summary>
    ChangeProvider,

    /// <summary>接続の確認（CLI の検出・ログイン・再確認・接続断）</summary>
    Connect,

    /// <summary>会話の開始・送信・ターンの終わり（成功・失敗・中断）</summary>
    Turn,

    /// <summary>添付の追加・送信・残り方</summary>
    Attach,

    /// <summary>設定の保存とモデルの履歴の記録</summary>
    Persist,

    /// <summary>エンジンの破棄（ShutdownEngines）</summary>
    Shutdown,
}

/// <summary>チャットのダイアログの表の行の入力（置く状態と手の種類・本体）</summary>
/// <param name="Touches">触る（状態, 手の種類）の組（網羅の計数に使う）</param>
/// <param name="Body">流して表明する本体</param>
internal sealed record ChatDialogCase(
    IReadOnlyList<(ChatDialogState State, ChatDialogOp Op)> Touches,
    Func<ChatDialogHarness, Task> Body
);

/// <summary>チャットのダイアログ（<see cref="QuickER.AI.Chat.AiChatDialogViewModel"/>）の操作の列の表</summary>
/// <remarks>
/// 行は元のテストの入力（初期状態・手の列）と期待をそのまま持つ。組（<see cref="ChatDialogHarness"/>）が
/// どの手の後にも掛ける不変条件 3 つを見張り、行の終わりにエンジンを破棄して表明する。主題ごとの行は
/// <c>ChatDialogRows.{主題}.cs</c> に置く。
/// </remarks>
internal static partial class ChatDialogRows
{
    /// <summary>行の表</summary>
    public static ScenarioTable<ChatDialogCase> Table =>
        new(
            EngineIds.Ai,
            [
                .. BackendRows(),
                .. ConnectRows(),
                .. TurnRows(),
                .. AttachmentRows(),
                .. PersistRows(),
                .. ShutdownRows(),
            ]
        );

    /// <summary>行を作る</summary>
    private static ScenarioRow<ChatDialogCase> Row(
        string id,
        string description,
        (ChatDialogState State, ChatDialogOp Op)[] touches,
        Func<ChatDialogHarness, Task> body
    ) => new(id, null, description, new ChatDialogCase(touches, body));

    /// <summary>同期の本体の行を作る</summary>
    private static ScenarioRow<ChatDialogCase> Row(
        string id,
        string description,
        (ChatDialogState State, ChatDialogOp Op)[] touches,
        Action<ChatDialogHarness> body
    ) =>
        Row(
            id,
            description,
            touches,
            h =>
            {
                body(h);
                return Task.CompletedTask;
            }
        );

    /// <summary>PNG シグネチャ付きバイト列を作る（添付の行用）</summary>
    private static byte[] PngBytes()
    {
        var data = new byte[16];
        byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        Array.Copy(signature, data, signature.Length);
        return data;
    }

    /// <summary>PDF として判定される最小バイト列（先頭シグネチャのみ意味を持つ）</summary>
    private static byte[] PdfBytes()
    {
        var bytes = new byte[16];
        "%PDF-1.7"u8.CopyTo(bytes);
        return bytes;
    }

    /// <summary>
    /// VM が内部で持つ API キーエンジン（<c>_apiKeyEngine</c>）の会話履歴（<c>_history</c>）へ、
    /// 実ドライバ（実 HTTP 呼び出し）を経由せずに直接 1 件差し込む。
    /// 「既に送信済みの添付付きメッセージが会話履歴に残っている」状態を、ネットワークなしで再現する。
    /// </summary>
    private static void InjectApiKeyEngineHistoryItem(
        AiChatDialogViewModel vm,
        ChatHistoryItem item
    )
    {
        var engineField = typeof(AiChatDialogViewModel).GetField(
            "_apiKeyEngine",
            BindingFlags.NonPublic | BindingFlags.Instance
        )!;
        var engine = (ChatTurnEngine)engineField.GetValue(vm)!;
        var historyField = typeof(ChatTurnEngine).GetField(
            "_history",
            BindingFlags.NonPublic | BindingFlags.Instance
        )!;
        var history = (List<ChatHistoryItem>)historyField.GetValue(engine)!;
        history.Add(item);
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

    /// <summary>検出の段階で投げる Claude Code クライアント（初期化・再確認の失敗の受け止めの検証用）</summary>
    private sealed class ThrowingClaudeCodeClient : IClaudeCodeClient
    {
        public bool IsAvailable() => throw new InvalidOperationException("claude-detect-failed");

        public Task<ClaudeCodeTurnOutcome> RunTurnAsync(
            string prompt,
            string? resumeSessionId,
            ClaudeCodeLaunchOptions options,
            Action<string> onAssistantText,
            CancellationToken cancellationToken
        ) => Task.FromResult(new ClaudeCodeTurnOutcome(true, null, null, false));

        public Task<ClaudeLoginProbeResult> ProbeLoginAsync(CancellationToken cancellationToken) =>
            Task.FromResult(ClaudeLoginProbeResult.LoggedIn);

        public void Interrupt() { }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
