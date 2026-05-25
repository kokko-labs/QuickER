using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace ERDesigner.Services;

/// <summary>Codex App Server の認証モードです。</summary>
public enum CodexAuthMode
{
    None,
    ApiKey,
    ChatGpt,
}

/// <summary>Codex App Server のログイン開始種別です。</summary>
public enum CodexLoginType
{
    ApiKey,
    ChatGpt,
}

/// <summary>Codex App Server から取得したアカウント情報です。</summary>
public sealed class CodexAccountInfo
{
    /// <summary>OpenAI 認証が必要かどうかです。</summary>
    public bool RequiresOpenAiAuth { get; init; }

    /// <summary>現在の認証モードです。</summary>
    public CodexAuthMode AuthMode { get; init; }

    /// <summary>ChatGPT ログイン時のメールアドレスです。</summary>
    public string? Email { get; init; }

    /// <summary>ChatGPT ログイン時のプラン名です。</summary>
    public string? PlanType { get; init; }

    /// <summary>ログイン済みかどうかです。</summary>
    public bool IsLoggedIn => AuthMode != CodexAuthMode.None;
}

/// <summary>Codex App Server へのログイン開始結果です。</summary>
public sealed class CodexLoginStartResult
{
    /// <summary>ログイン方式です。</summary>
    public required CodexLoginType Type { get; init; }

    /// <summary>ChatGPT ログイン時のログイン ID です。</summary>
    public string? LoginId { get; init; }

    /// <summary>ブラウザ認証 URL です。</summary>
    public string? AuthUrl { get; init; }
}

/// <summary>Codex App Server のログイン完了通知です。</summary>
public sealed class CodexLoginCompletedNotification
{
    /// <summary>ログイン ID です。API キーログイン時は null のことがあります。</summary>
    public string? LoginId { get; init; }

    /// <summary>ログイン成否です。</summary>
    public bool Success { get; init; }

    /// <summary>失敗時のエラー文言です。</summary>
    public string? Error { get; init; }
}

/// <summary>Codex App Server のアカウント更新通知です。</summary>
public sealed class CodexAccountUpdatedNotification
{
    /// <summary>更新後の認証モードです。</summary>
    public CodexAuthMode AuthMode { get; init; }

    /// <summary>更新後の ChatGPT プランです。</summary>
    public string? PlanType { get; init; }
}

/// <summary>Codex App Server の JSON-RPC 通知です。</summary>
public sealed class CodexJsonRpcNotification
{
    /// <summary>通知メソッド名です。</summary>
    public required string Method { get; init; }

    /// <summary>通知パラメータです。</summary>
    public JsonElement? Params { get; init; }
}

/// <summary>Codex App Server の動的ツール定義です。</summary>
public sealed class CodexDynamicToolDefinition
{
    /// <summary>ツール名です。</summary>
    public required string Name { get; init; }

    /// <summary>ツールの説明です。</summary>
    public required string Description { get; init; }

    /// <summary>遅延ロードするかどうかです。</summary>
    public bool DeferLoading { get; init; } = true;

    /// <summary>入力 JSON Schema です。</summary>
    public required object InputSchema { get; init; }
}

/// <summary>スレッド開始時の設定です。</summary>
public sealed class CodexThreadStartOptions
{
    /// <summary>作業フォルダです。</summary>
    public string? Cwd { get; init; }

    /// <summary>モデルプロバイダーです（例: ollama, openai）。null なら codex の既定を使います。</summary>
    public string? ModelProvider { get; init; }

    /// <summary>モデル名です（例: gemma4:31b-cloud）。null なら codex の既定を使います。</summary>
    public string? Model { get; init; }

    /// <summary>承認ポリシーです。</summary>
    public string? ApprovalPolicy { get; init; }

    /// <summary>サンドボックス設定です。</summary>
    public string? Sandbox { get; init; }

    /// <summary>動的ツール定義です。</summary>
    public IReadOnlyList<CodexDynamicToolDefinition>? DynamicTools { get; init; }
}

/// <summary>Codex スレッド情報です。</summary>
public sealed class CodexThreadInfo
{
    /// <summary>スレッド ID です。</summary>
    public required string Id { get; init; }

    /// <summary>プレビュー文字列です。</summary>
    public string Preview { get; init; } = string.Empty;

    /// <summary>モデルプロバイダーです。</summary>
    public string? ModelProvider { get; init; }

    /// <summary>メモリ上のみの一時スレッドかどうかです。</summary>
    public bool Ephemeral { get; init; }
}

/// <summary>Codex ターン情報です。</summary>
public sealed class CodexTurnInfo
{
    /// <summary>ターン ID です。</summary>
    public required string Id { get; init; }

    /// <summary>ターン状態です。</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>ターン失敗時のエラーメッセージです。</summary>
    public string? Error { get; init; }
}

/// <summary>Codex のテキスト入力項目です。</summary>
public sealed class CodexTextInputItem
{
    /// <summary>入力種別です。</summary>
    public string Type => "text";

    /// <summary>入力テキストです。</summary>
    public required string Text { get; init; }
}

/// <summary>スレッド開始通知です。</summary>
public sealed class CodexThreadStartedNotification
{
    /// <summary>開始されたスレッド情報です。</summary>
    public required CodexThreadInfo Thread { get; init; }
}

/// <summary>ターン開始通知です。</summary>
public sealed class CodexTurnStartedNotification
{
    /// <summary>スレッド ID です。</summary>
    public required string ThreadId { get; init; }

    /// <summary>ターン情報です。</summary>
    public required CodexTurnInfo Turn { get; init; }
}

/// <summary>エージェントメッセージ差分通知です。</summary>
public sealed class CodexAgentMessageDeltaNotification
{
    /// <summary>スレッド ID です。</summary>
    public string? ThreadId { get; init; }

    /// <summary>ターン ID です。</summary>
    public string? TurnId { get; init; }

    /// <summary>差分テキストです。</summary>
    public string Delta { get; init; } = string.Empty;
}

/// <summary>ターン完了通知です。</summary>
public sealed class CodexTurnCompletedNotification
{
    /// <summary>スレッド ID です。</summary>
    public required string ThreadId { get; init; }

    /// <summary>完了したターン情報です。</summary>
    public required CodexTurnInfo Turn { get; init; }
}

/// <summary>dynamicTool 呼び出しリクエストです（サーバーからクライアントへの JSON-RPC リクエスト）。</summary>
public sealed class CodexDynamicToolCallRequest
{
    /// <summary>JSON-RPC リクエスト ID です（レスポンス返送に使用）。</summary>
    public required int RequestId { get; init; }

    /// <summary>スレッド ID です。</summary>
    public required string ThreadId { get; init; }

    /// <summary>ターン ID です。</summary>
    public required string TurnId { get; init; }

    /// <summary>呼び出し ID です。</summary>
    public required string CallId { get; init; }

    /// <summary>呼び出されたツール名です。</summary>
    public required string Tool { get; init; }

    /// <summary>ツールへの引数（JSON ドキュメント）です。</summary>
    public System.Text.Json.JsonElement Arguments { get; init; }
}

/// <summary>item/started 通知です。</summary>
public sealed class CodexItemStartedNotification
{
    /// <summary>スレッド ID です。</summary>
    public string? ThreadId { get; init; }

    /// <summary>ターン ID です。</summary>
    public string? TurnId { get; init; }

    /// <summary>アイテム ID です。</summary>
    public string? ItemId { get; init; }

    /// <summary>アイテム種別です（agentMessage, commandExecution, fileChange, etc.）。</summary>
    public string? ItemType { get; init; }
}

/// <summary>item/completed 通知です。</summary>
public sealed class CodexItemCompletedNotification
{
    /// <summary>スレッド ID です。</summary>
    public string? ThreadId { get; init; }

    /// <summary>ターン ID です。</summary>
    public string? TurnId { get; init; }

    /// <summary>アイテム ID です。</summary>
    public string? ItemId { get; init; }

    /// <summary>アイテム種別です。</summary>
    public string? ItemType { get; init; }
}

/// <summary>承認リクエスト（commandExecution や fileChange の承認）です。</summary>
public sealed class CodexApprovalRequest
{
    /// <summary>JSON-RPC リクエスト ID です（レスポンス返送に使用）。</summary>
    public required int RequestId { get; init; }

    /// <summary>スレッド ID です。</summary>
    public string? ThreadId { get; init; }

    /// <summary>ターン ID です。</summary>
    public string? TurnId { get; init; }

    /// <summary>アイテム ID です。</summary>
    public string? ItemId { get; init; }

    /// <summary>承認メソッド名です。</summary>
    public required string Method { get; init; }
}

/// <summary>チャットメッセージの送信者種別です。</summary>
public enum CodexChatMessageRole
{
    User,
    Assistant,
    System,

    /// <summary>AI のツール呼び出し作業内容（折り畳み表示用）です。</summary>
    ToolCall,
}

/// <summary>Codex 会話のチャット表示用メッセージエントリです。</summary>
public sealed class CodexChatMessage : INotifyPropertyChanged
{
    private string _content = string.Empty;
    private bool _isExpanded;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>送信者種別です。</summary>
    public required CodexChatMessageRole Role { get; init; }

    /// <summary>メッセージ本文です。</summary>
    public required string Content
    {
        get => _content;
        set
        {
            if (_content != value)
            {
                _content = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>ToolCall メッセージの展開状態です（作業中は true、完了後は false）。</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded != value)
            {
                _isExpanded = value;
                OnPropertyChanged();
            }
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
