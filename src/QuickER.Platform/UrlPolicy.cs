namespace QuickER.Platform;

/// <summary>既定のブラウザで開いてよい URL を決める規則</summary>
/// <remarks>
/// 開いてよいのは http / https の絶対 URL でホストを持ち、ユーザー情報（<c>user:pass@</c>）を持たないものだけ。AI の回答や外部の CLI が返す URL は外部の入力なので、
/// <c>file:</c>・UNC（<c>\\server</c>）・<c>//server/share</c>・<c>javascript:</c>・<c>ms-settings:</c> のようなアプリ起動 URI・
/// 相対 URL を通すと、クリック 1 つでローカルのファイルやアプリを起動させられる。既定のブラウザへ渡す前に、ここで閉じる。
/// 開く処理（<see cref="ExternalLauncher.OpenUrl"/>）もこの規則で確かめ直す。
/// ユーザー情報を持つ URL（<c>https://example.com@evil.test/</c>）は、ホストの前に別のホスト名に見える文字列を置けるので断る。
/// 利用者に見せる表記は <see cref="ToDisplayText"/>（国際化ドメイン名を punycode にして、見た目の似た別の文字のホストを見分けられるようにする）。
/// </remarks>
public static class UrlPolicy
{
    /// <summary>文字列を、開いてよい URL として解釈する</summary>
    /// <param name="url">URL の文字列（リンク先・CLI が返した URL など）</param>
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
    /// <returns>http / https の絶対 URL でホストを持ち、ユーザー情報を持たないとき true</returns>
    public static bool IsSafe(Uri uri) =>
        uri.IsAbsoluteUri
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && !string.IsNullOrEmpty(uri.Host)
        && string.IsNullOrEmpty(uri.UserInfo);

    /// <summary>
    /// 利用者に見せる URL の表記（ホストが国際化ドメイン名なら punycode にする。パス・クエリはエスケープした形）
    /// </summary>
    /// <remarks>
    /// <see cref="Uri.AbsoluteUri"/> は国際化ドメイン名を Unicode のまま出すので、キリル文字の a（U+0430）を使ったホストが
    /// ラテン文字の <c>apple.com</c> と見分けられない。punycode（<c>xn--pple-43d.com</c>）にすれば違いが見える。開く URL そのものは変えない。
    /// </remarks>
    /// <param name="uri">開いてよい URL（<see cref="IsSafe"/>）</param>
    public static string ToDisplayText(Uri uri)
    {
        var host = uri.HostNameType == UriHostNameType.Dns ? uri.IdnHost : uri.Host;
        var port = uri.IsDefaultPort
            ? string.Empty
            : ":" + uri.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return $"{uri.Scheme}://{host}{port}{uri.PathAndQuery}{uri.Fragment}";
    }
}
