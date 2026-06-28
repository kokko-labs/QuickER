using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace QuickER.Services;

/// <summary>Codex App Server の認証モード</summary>
public enum CodexAuthMode
{
    /// <summary>未認証</summary>
    None,

    /// <summary>API キー認証</summary>
    ApiKey,

    /// <summary>ChatGPT アカウント認証</summary>
    ChatGpt,
}

/// <summary>Codex App Server のログイン開始種別</summary>
public enum CodexLoginType
{
    /// <summary>API キーによるログイン</summary>
    ApiKey,

    /// <summary>ChatGPT ブラウザログイン</summary>
    ChatGpt,
}

/// <summary>Codex App Server から取得したアカウント情報</summary>
public sealed class CodexAccountInfo
{
    /// <summary>OpenAI 認証が必要かどうか</summary>
    public bool RequiresOpenAiAuth { get; init; }

    /// <summary>現在の認証モード</summary>
    public CodexAuthMode AuthMode { get; init; }

    /// <summary>ChatGPT ログイン時のメールアドレス</summary>
    public string? Email { get; init; }

    /// <summary>ChatGPT ログイン時のプラン名</summary>
    public string? PlanType { get; init; }

    /// <summary>ログイン済みかどうか</summary>
    public bool IsLoggedIn => AuthMode != CodexAuthMode.None;
}

/// <summary>Codex App Server へのログイン開始結果</summary>
public sealed class CodexLoginStartResult
{
    /// <summary>ログイン方式</summary>
    public required CodexLoginType Type { get; init; }

    /// <summary>ChatGPT ログイン時のログイン ID</summary>
    public string? LoginId { get; init; }

    /// <summary>ブラウザ認証 URL</summary>
    public string? AuthUrl { get; init; }
}

/// <summary>Codex App Server のログイン完了通知</summary>
public sealed class CodexLoginCompletedNotification
{
    /// <summary>ログイン ID（API キーログイン時は null になることがある）</summary>
    public string? LoginId { get; init; }

    /// <summary>ログイン成否</summary>
    public bool Success { get; init; }

    /// <summary>失敗時のエラー文言</summary>
    public string? Error { get; init; }
}

/// <summary>Codex App Server のアカウント更新通知</summary>
public sealed class CodexAccountUpdatedNotification
{
    /// <summary>更新後の認証モード</summary>
    public CodexAuthMode AuthMode { get; init; }

    /// <summary>更新後の ChatGPT プラン</summary>
    public string? PlanType { get; init; }
}

/// <summary>Codex App Server の JSON-RPC 通知</summary>
public sealed class CodexJsonRpcNotification
{
    /// <summary>通知メソッド名</summary>
    public required string Method { get; init; }

    /// <summary>通知パラメータ</summary>
    public JsonElement? Params { get; init; }
}

/// <summary>Codex App Server へ登録する動的ツール定義</summary>
public sealed class CodexDynamicToolDefinition
{
    /// <summary>ツール名</summary>
    public required string Name { get; init; }

    /// <summary>ツールの説明</summary>
    public required string Description { get; init; }

    /// <summary>遅延ロードするかどうか</summary>
    public bool DeferLoading { get; init; } = true;

    /// <summary>入力 JSON Schema</summary>
    public required object InputSchema { get; init; }
}

/// <summary>スレッド開始時の設定</summary>
public sealed class CodexThreadStartOptions
{
    /// <summary>作業フォルダ</summary>
    public string? Cwd { get; init; }

    /// <summary>モデルプロバイダー（例: ollama, openai）null なら codex の既定を使う</summary>
    public string? ModelProvider { get; init; }

    /// <summary>モデル名（例: gemma4:31b-cloud）null なら codex の既定を使う</summary>
    public string? Model { get; init; }

    /// <summary>承認ポリシー</summary>
    public string? ApprovalPolicy { get; init; }

    /// <summary>サンドボックス設定</summary>
    public string? Sandbox { get; init; }

    /// <summary>動的ツール定義</summary>
    public IReadOnlyList<CodexDynamicToolDefinition>? DynamicTools { get; init; }

    /// <summary>開発者指示（Codex の基本プロンプトへ追加されるアプリ固有の指示。null なら送らない）</summary>
    public string? DeveloperInstructions { get; init; }
}

/// <summary>Codex スレッド情報</summary>
public sealed class CodexThreadInfo
{
    /// <summary>スレッド ID</summary>
    public required string Id { get; init; }

    /// <summary>プレビュー文字列</summary>
    public string Preview { get; init; } = string.Empty;

    /// <summary>モデルプロバイダー</summary>
    public string? ModelProvider { get; init; }

    /// <summary>メモリ上のみの一時スレッドかどうか</summary>
    public bool Ephemeral { get; init; }
}

/// <summary>Codex ターン情報</summary>
public sealed class CodexTurnInfo
{
    /// <summary>ターン ID</summary>
    public required string Id { get; init; }

    /// <summary>ターン状態</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>ターン失敗時のエラーメッセージ</summary>
    public string? Error { get; init; }
}

/// <summary>Codex のテキスト入力項目</summary>
public sealed class CodexTextInputItem
{
    /// <summary>入力種別（常に text）</summary>
    public string Type => "text";

    /// <summary>入力テキスト</summary>
    public required string Text { get; init; }
}

/// <summary>スレッド開始通知</summary>
public sealed class CodexThreadStartedNotification
{
    /// <summary>開始されたスレッド情報</summary>
    public required CodexThreadInfo Thread { get; init; }
}

/// <summary>ターン開始通知</summary>
public sealed class CodexTurnStartedNotification
{
    /// <summary>スレッド ID</summary>
    public required string ThreadId { get; init; }

    /// <summary>ターン情報</summary>
    public required CodexTurnInfo Turn { get; init; }
}

/// <summary>エージェントメッセージ差分通知（ストリーミング出力の断片）</summary>
public sealed class CodexAgentMessageDeltaNotification
{
    /// <summary>スレッド ID</summary>
    public string? ThreadId { get; init; }

    /// <summary>ターン ID</summary>
    public string? TurnId { get; init; }

    /// <summary>差分テキスト</summary>
    public string Delta { get; init; } = string.Empty;
}

/// <summary>ターン完了通知</summary>
public sealed class CodexTurnCompletedNotification
{
    /// <summary>スレッド ID</summary>
    public required string ThreadId { get; init; }

    /// <summary>完了したターン情報</summary>
    public required CodexTurnInfo Turn { get; init; }
}

/// <summary>dynamicTool 呼び出しリクエスト（サーバーからクライアントへの JSON-RPC リクエスト）</summary>
public sealed class CodexDynamicToolCallRequest
{
    /// <summary>JSON-RPC リクエスト ID（レスポンス返送に使用する）</summary>
    public required int RequestId { get; init; }

    /// <summary>スレッド ID</summary>
    public required string ThreadId { get; init; }

    /// <summary>ターン ID</summary>
    public required string TurnId { get; init; }

    /// <summary>呼び出し ID</summary>
    public required string CallId { get; init; }

    /// <summary>呼び出されたツール名</summary>
    public required string Tool { get; init; }

    /// <summary>ツールへの引数（JSON ドキュメント）</summary>
    public System.Text.Json.JsonElement Arguments { get; init; }
}

/// <summary>item/started 通知</summary>
public sealed class CodexItemStartedNotification
{
    /// <summary>スレッド ID</summary>
    public string? ThreadId { get; init; }

    /// <summary>ターン ID</summary>
    public string? TurnId { get; init; }

    /// <summary>アイテム ID</summary>
    public string? ItemId { get; init; }

    /// <summary>アイテム種別（agentMessage, commandExecution, fileChange など）</summary>
    public string? ItemType { get; init; }
}

/// <summary>item/completed 通知</summary>
public sealed class CodexItemCompletedNotification
{
    /// <summary>スレッド ID</summary>
    public string? ThreadId { get; init; }

    /// <summary>ターン ID</summary>
    public string? TurnId { get; init; }

    /// <summary>アイテム ID</summary>
    public string? ItemId { get; init; }

    /// <summary>アイテム種別</summary>
    public string? ItemType { get; init; }
}

/// <summary>承認リクエスト（commandExecution や fileChange の承認）</summary>
public sealed class CodexApprovalRequest
{
    /// <summary>JSON-RPC リクエスト ID（レスポンス返送に使用する）</summary>
    public required int RequestId { get; init; }

    /// <summary>スレッド ID</summary>
    public string? ThreadId { get; init; }

    /// <summary>ターン ID</summary>
    public string? TurnId { get; init; }

    /// <summary>アイテム ID</summary>
    public string? ItemId { get; init; }

    /// <summary>承認メソッド名</summary>
    public required string Method { get; init; }
}
