using QuickER.Settings;

namespace QuickER.AI;

/// <summary>AI チャット系ダイアログの UI 状態設定（最後に使った接続タブなど）</summary>
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

/// <summary>
/// AI チャット系ダイアログの UI 状態設定を JSON ファイルへ保存・読込するストア。
/// ダイアログごとにファイル名を分けて使う（例: <c>ai-chat-ui.json</c> / <c>mock-generation-ui.json</c>）。
/// </summary>
public class ChatUiSettingsStore : JsonSettingsStore<ChatUiSettings>
{
    /// <summary>既定の保存先（%APPDATA%\QuickER）で設定ストアを生成する</summary>
    /// <param name="fileName">設定ファイル名（例: <c>ai-chat-ui.json</c>）</param>
    public ChatUiSettingsStore(string fileName)
        : base(fileName) { }

    /// <summary>保存先フォルダを指定して設定ストアを生成する（テスト用）</summary>
    /// <param name="fileName">設定ファイル名</param>
    /// <param name="folder">保存先フォルダ</param>
    public ChatUiSettingsStore(string fileName, string folder)
        : base(fileName, folder) { }
}
