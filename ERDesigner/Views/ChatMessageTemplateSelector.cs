using System.Windows;
using System.Windows.Controls;
using ERDesigner.Services;

namespace ERDesigner.Views;

/// <summary>Codex チャットメッセージのロール別にデータテンプレートを選択します。</summary>
public class ChatMessageTemplateSelector : DataTemplateSelector
{
    /// <summary>ユーザーメッセージ用テンプレートです。</summary>
    public DataTemplate? UserTemplate { get; set; }

    /// <summary>アシスタントメッセージ用テンプレートです。</summary>
    public DataTemplate? AssistantTemplate { get; set; }

    /// <summary>システムメッセージ用テンプレートです。</summary>
    public DataTemplate? SystemTemplate { get; set; }

    /// <summary>ツール呼び出し（折り畳み）メッセージ用テンプレートです。</summary>
    public DataTemplate? ToolCallTemplate { get; set; }

    /// <inheritdoc />
    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        if (item is CodexChatMessage message)
        {
            return message.Role switch
            {
                CodexChatMessageRole.User => UserTemplate,
                CodexChatMessageRole.Assistant => AssistantTemplate,
                CodexChatMessageRole.System => SystemTemplate,
                CodexChatMessageRole.ToolCall => ToolCallTemplate,
                _ => base.SelectTemplate(item, container),
            };
        }

        return base.SelectTemplate(item, container);
    }
}
