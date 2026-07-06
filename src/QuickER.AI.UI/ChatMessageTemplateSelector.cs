using System.Windows;
using System.Windows.Controls;
using QuickER.AI;

namespace QuickER.AI.UI;

/// <summary>チャットメッセージのロール別にデータテンプレートを選択するセレクター</summary>
public class ChatMessageTemplateSelector : DataTemplateSelector
{
    /// <summary>ユーザーメッセージ用テンプレート</summary>
    public DataTemplate? UserTemplate { get; set; }

    /// <summary>アシスタントメッセージ用テンプレート</summary>
    public DataTemplate? AssistantTemplate { get; set; }

    /// <summary>システムメッセージ用テンプレート</summary>
    public DataTemplate? SystemTemplate { get; set; }

    /// <summary>ツール呼び出し（折り畳み表示）メッセージ用テンプレート</summary>
    public DataTemplate? ToolCallTemplate { get; set; }

    /// <inheritdoc />
    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        if (item is ErChatMessage message)
        {
            return message.Role switch
            {
                ErChatMessageRole.User => UserTemplate,
                ErChatMessageRole.Assistant => AssistantTemplate,
                ErChatMessageRole.System => SystemTemplate,
                ErChatMessageRole.ToolCall => ToolCallTemplate,
                _ => base.SelectTemplate(item, container),
            };
        }

        return base.SelectTemplate(item, container);
    }
}
