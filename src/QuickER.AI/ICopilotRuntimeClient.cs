using QuickER.Mcp;

namespace QuickER.AI;

/// <summary>GitHub Copilot CLI の認証状態スナップショット（接続パネル表示用）</summary>
/// <param name="IsAuthenticated">ログイン済みか</param>
/// <param name="Login">ログインアカウント名（取得できないときは空）</param>
/// <param name="AuthType">認証方式（OAuth / トークンなど。取得できないときは空）</param>
/// <param name="StatusMessage">CLI が返す状態メッセージ（取得できないときは空）</param>
public readonly record struct CopilotAuthInfo(
    bool IsAuthenticated,
    string Login,
    string AuthType,
    string StatusMessage
);

/// <summary>手動解決が必要なツール呼び出し要求 1 件（Codex の dynamicTool 呼び出しに相当）</summary>
/// <param name="RequestId">応答時に指定する要求 ID</param>
/// <param name="ToolName">呼び出されたツール名</param>
/// <param name="ArgumentsJson">引数の生 JSON（<see cref="IErDiagramToolHost.Execute"/> へそのまま渡す）</param>
public readonly record struct CopilotToolCallRequest(
    string RequestId,
    string ToolName,
    string ArgumentsJson
);

/// <summary>Copilot セッション（＝1 会話）の生成オプション</summary>
public sealed class CopilotSessionOptions
{
    /// <summary>使用するモデル ID（空なら Copilot CLI の既定モデルに任せる）</summary>
    public string Model { get; init; } = string.Empty;

    /// <summary>
    /// セッションへ公開するツール定義（空ならツールなし）。
    /// ここに挙げたツールだけが使える状態にし、Copilot 組込みのファイル編集・シェル・
    /// GitHub MCP といったツールは一切使わせない。
    /// </summary>
    public IReadOnlyList<ToolDefinition> Tools { get; init; } = [];

    /// <summary>システムメッセージへ追記する設計ルール（空なら追記しない）</summary>
    public string Instructions { get; init; } = string.Empty;

    /// <summary>
    /// セッションの作業フォルダ（空ならクライアントが用意する無害な一時フォルダ）。
    /// </summary>
    /// <remarks>
    /// ER 設計チャットは図をファイルとして触らせないため空のまま（＝一時フォルダに閉じ込める）。
    /// モックプロジェクト生成は出力フォルダを指定し、そこを作業場所かつ許可範囲の基準にする。
    /// </remarks>
    public string WorkingDirectory { get; init; } = string.Empty;

    /// <summary>
    /// Copilot 組込みのファイル編集・シェル実行ツールを使わせ、<see cref="WorkingDirectory"/> 配下の
    /// 操作を自動承認するか（既定 false）。
    /// </summary>
    /// <remarks>
    /// false（既定＝ER 設計チャット）は「宣言したツールだけ許可・それ以外の許可要求はすべて拒否」。
    /// true（モックプロジェクト生成）は組込みツールの締め出しを解き、作業フォルダ配下の読み書きと
    /// コマンド実行だけを自動承認する（Codex の workspace-write / Claude Code の acceptEdits 相当）。
    /// </remarks>
    public bool AllowWorkspaceTools { get; init; }
}

/// <summary>
/// GitHub Copilot ランタイム（copilot CLI の子プロセス）への接続を抽象化するクライアント。
/// <see cref="CopilotChatEngine"/> を SDK から切り離してテスト可能に保つためのシームで、
/// 実装は <see cref="CopilotRuntimeClient"/>（GitHub.Copilot.SDK 版）。
/// </summary>
/// <remarks>
/// Codex の <see cref="ICodexAppServerClient"/> と同じ役割・同じ粒度（接続 → 会話開始 → 送信 →
/// 通知イベント → ツール呼び出しの手動応答）で揃えてある。
/// </remarks>
public interface ICopilotRuntimeClient : IAsyncDisposable
{
    /// <summary>ランタイムへ接続済みか</summary>
    bool IsStarted { get; }

    /// <summary>会話セッションを生成済みか</summary>
    bool HasSession { get; }

    /// <summary>copilot 実行ファイルが PATH で解決できるか</summary>
    bool IsAvailable();

    /// <summary>ランタイム（copilot 子プロセス）を起動して接続する</summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>認証状態を取得する</summary>
    Task<CopilotAuthInfo> GetAuthStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>利用可能なモデル ID を列挙する（静的カタログを持たず実行時に問い合わせる）</summary>
    Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken = default);

    /// <summary>会話セッションを開始する（既存セッションがあれば破棄して作り直す）</summary>
    Task StartSessionAsync(
        CopilotSessionOptions options,
        CancellationToken cancellationToken = default
    );

    /// <summary>ユーザー発話（＋添付）を送信する。完了は <see cref="SessionIdle"/> で通知される</summary>
    Task SendAsync(
        string prompt,
        IReadOnlyList<ChatAttachment> attachments,
        CancellationToken cancellationToken = default
    );

    /// <summary>実行中のターンを中断する</summary>
    Task AbortAsync(CancellationToken cancellationToken = default);

    /// <summary>ツール呼び出し要求へ結果を返す</summary>
    /// <param name="requestId"><see cref="CopilotToolCallRequest.RequestId"/></param>
    /// <param name="result">結果テキスト（失敗時はエラー内容）</param>
    /// <param name="success">成否</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    Task RespondToToolCallAsync(
        string requestId,
        string result,
        bool success,
        CancellationToken cancellationToken = default
    );

    /// <summary>アシスタント応答テキストの逐次断片</summary>
    event EventHandler<string>? AssistantDeltaReceived;

    /// <summary>手動解決が必要なツール呼び出し要求</summary>
    event EventHandler<CopilotToolCallRequest>? ToolCallRequested;

    /// <summary>組込みツールの実行が始まった（引数はツール名）</summary>
    /// <remarks>
    /// 組込みツールを許可するセッション（<see cref="CopilotSessionOptions.AllowWorkspaceTools"/>）で
    /// 進捗を可視化するための通知。手動解決のツールは <see cref="ToolCallRequested"/> 側で扱う。
    /// </remarks>
    event EventHandler<string>? ToolExecutionStarted;

    /// <summary>セッションがアイドルへ戻った（＝ターン完了。引数は中断された場合 true）</summary>
    event EventHandler<bool>? SessionIdle;

    /// <summary>セッションのエラー（引数はエラーメッセージ）</summary>
    event EventHandler<string>? SessionErrorReceived;

    /// <summary>ER 図編集に不要な許可要求を拒否した（引数は拒否した要求の種別名）</summary>
    event EventHandler<string>? PermissionDeclined;
}
