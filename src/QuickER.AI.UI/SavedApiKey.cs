namespace QuickER.AI.UI;

/// <summary>保存済みの API キーの読み取りの結果分類</summary>
public enum SavedApiKeyStatus
{
    /// <summary>保存されていない</summary>
    Missing,

    /// <summary>読めた</summary>
    Loaded,

    /// <summary>
    /// 暗号ファイルはあるが読めない（別のユーザー・別の PC で暗号化したもの・壊れたファイル・一時的なロック）。
    /// 空のキーとして扱って書き戻すと、暗号ファイルを消してしまう
    /// </summary>
    Unreadable,
}

/// <summary>保存済みの API キー（読み取りの結果分類と、読めたときのキー）</summary>
/// <param name="Status">読み取りの結果分類</param>
/// <param name="Key">読めたときのキー（それ以外は空文字）</param>
public sealed record SavedApiKey(SavedApiKeyStatus Status, string Key)
{
    /// <summary>保存されていない</summary>
    public static SavedApiKey Missing { get; } = new(SavedApiKeyStatus.Missing, string.Empty);

    /// <summary>暗号ファイルはあるが読めない</summary>
    public static SavedApiKey Unreadable { get; } = new(SavedApiKeyStatus.Unreadable, string.Empty);

    /// <summary>キーの文字列から作る（空・null は保存されていない）</summary>
    /// <param name="key">読めたキー</param>
    public static implicit operator SavedApiKey(string? key) =>
        string.IsNullOrEmpty(key) ? Missing : new SavedApiKey(SavedApiKeyStatus.Loaded, key);
}
