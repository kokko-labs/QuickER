namespace QuickER.AI;

/// <summary>AI チャットのバックエンド種別（接続方式）</summary>
public enum ErChatBackendKind
{
    /// <summary>API キー接続（OpenAI/Ollama を自前のチャット制御で利用）</summary>
    ApiKey,

    /// <summary>Codex 接続（Codex App Server を利用）</summary>
    Codex,

    /// <summary>Claude Code 接続（ローカルの Claude Code CLI をヘッドレス利用）</summary>
    ClaudeCode,
}

/// <summary>ツール実行の活動内容（ToolCall 吹き出し表示用）</summary>
/// <param name="ToolName">実行したツール名</param>
/// <param name="Result">実行結果テキスト</param>
/// <param name="Success">成否</param>
public readonly record struct ErChatToolActivity(string ToolName, string Result, bool Success);

/// <summary>ターン（1 回の応答処理）の完了結果</summary>
/// <param name="Success">成功したかどうか</param>
/// <param name="Error">失敗時のエラーメッセージ（成功時は null）</param>
public readonly record struct ErChatTurnResult(bool Success, string? Error);

/// <summary>
/// AI チャットのエンジン抽象。OpenAI SDK の自前制御と Codex App Server の双方を
/// 同一インターフェースで扱い、ViewModel をバックエンド実装から切り離す。
/// </summary>
public interface IErChatEngine : IAsyncDisposable
{
    /// <summary>応答テキストの逐次断片（ストリーミング）</summary>
    event EventHandler<string>? AssistantDeltaReceived;

    /// <summary>ツールを実行した活動内容</summary>
    event EventHandler<ErChatToolActivity>? ToolActivityReceived;

    /// <summary>ターンの完了（成否・エラー）</summary>
    event EventHandler<ErChatTurnResult>? TurnCompleted;

    /// <summary>ステータスバーへ表示する状態文言</summary>
    event EventHandler<string>? StatusChanged;

    /// <summary>チャット送信が可能な状態か（接続済み・必要なら認証済み）</summary>
    bool IsReady { get; }

    /// <summary>
    /// このエンジンが受け付けられる添付の範囲（UI の添付可否判定に使う）。
    /// 既定は <see cref="AttachmentSupport.None"/> で、添付対応エンジンのみが上書きする。
    /// </summary>
    AttachmentSupport AttachmentSupport => AttachmentSupport.None;

    /// <summary>エンジンを初期化する（接続・設定読込など）</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>新しい会話を開始する（履歴・スレッドをリセットする）</summary>
    Task StartConversationAsync(CancellationToken cancellationToken = default);

    /// <summary>ユーザー発話を 1 ターンとして送信し、応答・ツール実行を進める</summary>
    Task SendAsync(string prompt, CancellationToken cancellationToken = default);

    /// <summary>
    /// 添付（画像・PDF）を同梱してユーザー発話を 1 ターンとして送信する。
    /// 既定実装は添付を無視して <see cref="SendAsync(string, CancellationToken)"/> へ委譲するため、
    /// 添付対応エンジンだけがこのオーバーロードを上書きすればよい（既存実装は不変）。
    /// </summary>
    /// <param name="prompt">ユーザー発話</param>
    /// <param name="attachments">同梱する添付（空なら添付なしと同じ挙動）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    Task SendAsync(
        string prompt,
        IReadOnlyList<ChatAttachment> attachments,
        CancellationToken cancellationToken = default
    ) => SendAsync(prompt, cancellationToken);

    /// <summary>実行中のターンを中断する</summary>
    Task InterruptAsync(CancellationToken cancellationToken = default);
}
