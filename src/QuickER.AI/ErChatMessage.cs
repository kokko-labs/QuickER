using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace QuickER.AI;

/// <summary>チャットメッセージの送信者種別</summary>
public enum ErChatMessageRole
{
    /// <summary>ユーザー発言</summary>
    User,

    /// <summary>アシスタント（AI）応答</summary>
    Assistant,

    /// <summary>システムメッセージ（案内・エラー）</summary>
    System,

    /// <summary>AI のツール呼び出し作業内容（折り畳み表示用）</summary>
    ToolCall,
}

/// <summary>AI チャットの表示用メッセージエントリ（エンジン非依存）</summary>
public sealed class ErChatMessage : INotifyPropertyChanged
{
    /// <summary>メッセージ本文の実体フィールド</summary>
    private string _content = string.Empty;

    /// <summary>展開状態の実体フィールド</summary>
    private bool _isExpanded;

    /// <summary>応答中かどうかの実体フィールド</summary>
    private bool _isStreaming;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>送信者種別</summary>
    public required ErChatMessageRole Role { get; init; }

    /// <summary>メッセージ本文（ストリーミング更新で変化するため変更通知する）</summary>
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

    /// <summary>ToolCall メッセージの展開状態（作業中は true、完了後は false）</summary>
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

    /// <summary>
    /// アシスタントの回答をストリーミングで組み立てている最中か（応答中は true、ターンが終わると false）。
    /// 表示側は応答中の回答をテキストで出し、終わってから Markdown 表示へ切り替える。
    /// 既定は false＝ストリーミングを経ずに作ったメッセージは最初から応答済みとして扱う。
    /// </summary>
    public bool IsStreaming
    {
        get => _isStreaming;
        set
        {
            if (_isStreaming != value)
            {
                _isStreaming = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// このメッセージに同梱された添付の要約（ユーザー発話の吹き出しに「📎 ファイル名」で表示するための文言・省略時は null）。
    /// 添付なしの既存メッセージは null のままで挙動不変。
    /// </summary>
    public string? AttachmentSummary { get; init; }

    /// <summary>添付要約を持つか（吹き出しの添付行の表示制御に使う）</summary>
    public bool HasAttachments => !string.IsNullOrEmpty(AttachmentSummary);

    /// <summary>
    /// 応答中の回答をすべて応答済みにする（ターンの終了時に呼ぶ。完了・失敗・中断・送信の例外のどれでも同じ）
    /// </summary>
    /// <param name="messages">チャットのメッセージ一覧</param>
    public static void EndStreaming(IEnumerable<ErChatMessage> messages)
    {
        foreach (var message in messages)
        {
            message.IsStreaming = false;
        }
    }

    /// <summary>指定プロパティの変更を通知する</summary>
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
