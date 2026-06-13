using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ERDesigner.Services.Chat;

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

    /// <summary>指定プロパティの変更を通知する</summary>
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
