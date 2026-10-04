namespace QuickER.AI.UI;

/// <summary>
/// チャットの回答に含まれるリンクのうち、クリックで開いてよいものを決める規則
/// （Markdown の組み立てと、実際に開く処理の両方が使う）。
/// </summary>
/// <remarks>
/// 開いてよいのは http / https の絶対 URL だけ。AI の回答は外部の入力なので、<c>file:</c>・UNC（<c>\\server</c>）・
/// <c>javascript:</c>・<c>ms-settings:</c> のようなアプリ起動 URI・相対 URL を通すと、クリック 1 つで
/// ローカルのファイルやアプリを起動させられる。既定のブラウザへ渡す前に、ここで閉じる。
/// </remarks>
public static class ChatLinkPolicy
{
    /// <summary>リンク先の文字列を、開いてよい URL として解釈する</summary>
    /// <param name="url">リンク先の文字列（Markdown のリンクや自動リンクの URL）</param>
    /// <param name="uri">開いてよい場合はその URL。開いてはいけない場合は null</param>
    /// <returns>開いてよい（http / https の絶対 URL でホストを持つ）とき true</returns>
    public static bool TryGetSafeUri(string? url, out Uri? uri)
    {
        uri = null;

        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) || !IsSafe(parsed))
        {
            return false;
        }

        uri = parsed;
        return true;
    }

    /// <summary>解釈済みの URL が開いてよいものかを判定する</summary>
    /// <param name="uri">判定する URL</param>
    /// <returns>http / https の絶対 URL でホストを持つとき true</returns>
    public static bool IsSafe(Uri uri) =>
        uri.IsAbsoluteUri
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && !string.IsNullOrEmpty(uri.Host);
}
