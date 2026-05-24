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
                _ => base.SelectTemplate(item, container),
            };
        }

        return base.SelectTemplate(item, container);
    }
}
