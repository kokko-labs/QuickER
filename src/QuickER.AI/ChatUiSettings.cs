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

    /// <summary>
    /// 最後に選んだ API キー接続のプロバイダー（<see cref="AiProvider"/> の名前。空・不正値なら既定プロバイダー）。
    /// 列挙値そのものではなく名前で持つのは、値の増減やリネームがあっても JSON の前後互換を壊さないため
    /// （読み手は <see cref="ParseApiProvider"/> で解釈し、解釈できない値は既定へ落とす）。
    /// </summary>
    public string ApiProvider { get; set; } = string.Empty;

    /// <summary>
    /// API キー接続のエンドポイント上書き URL（ローカル LLM 用。空なら上書きなし＝プロバイダー既定）。
    /// プロバイダーに依らず入力値をそのまま保持し、実際に適用するかどうかは UI 側が判断する。
    /// </summary>
    public string EndpointOverride { get; set; } = string.Empty;

    /// <summary><see cref="LastBackend"/> を列挙値として解釈する（空・不正値は null）</summary>
    public ErChatBackendKind? ParseLastBackend() =>
        Enum.TryParse<ErChatBackendKind>(LastBackend, ignoreCase: true, out var backend)
            ? backend
            : null;

    /// <summary>
    /// <see cref="ApiProvider"/> を列挙値として解釈する（空・不正値は null）。
    /// 数値文字列は <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)"/> が定義外の値でも
    /// 成功させてしまうため、<see cref="Enum.IsDefined{TEnum}(TEnum)"/> で定義済みかどうかも確かめる。
    /// </summary>
    public AiProvider? ParseApiProvider() =>
        Enum.TryParse<AiProvider>(ApiProvider, ignoreCase: true, out var provider)
        && Enum.IsDefined(provider)
            ? provider
            : null;
}
