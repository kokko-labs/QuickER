namespace QuickER.AI;

/// <summary>
/// AI チャット系ダイアログの UI 状態設定（最後に使った接続タブなど）。
/// <see cref="AiSettings"/> のセクション（<see cref="AiSettings.ChatUi"/> / <see cref="AiSettings.MockUi"/>）
/// としてダイアログ別に保持する。
/// </summary>
public class ChatUiSettings
{
    /// <summary>最後に使った接続方式（<see cref="ErChatBackendKind"/> の名前。空・不正値なら既定タブ）</summary>
    public string LastBackend { get; set; } = string.Empty;

    /// <summary><see cref="LastBackend"/> を列挙値として解釈する（空・不正値は null）</summary>
    public ErChatBackendKind? ParseLastBackend() =>
        Enum.TryParse<ErChatBackendKind>(LastBackend, ignoreCase: true, out var backend)
            ? backend
            : null;
}
