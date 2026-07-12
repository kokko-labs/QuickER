namespace QuickER.Services;

/// <summary>
/// 更新フィード URL の解決を担う定数と純粋関数のヘルパ（UI・Velopack に依存しないため単体テスト可能）。
/// </summary>
/// <remarks>
/// 既定のフィードは <see cref="GitHubRepositoryUrl"/>（リポジトリ公開時に差し替える定数）。
/// 環境変数 <see cref="FeedEnvironmentVariable"/>（ローカルパス or URL）が設定されていれば
/// そちらを優先する（E2E 検証用）。どちらも空なら更新チェック自体を行わない。
/// </remarks>
public static class UpdateFeed
{
    /// <summary>
    /// 更新フィードとする GitHub リポジトリ URL。リポジトリ公開時に
    /// <c>https://github.com/&lt;owner&gt;/&lt;repo&gt;</c> を設定する（現状は未公開のため空文字）。
    /// </summary>
    public const string GitHubRepositoryUrl = "";

    /// <summary>
    /// 更新フィードを上書きする環境変数名（ローカルパス or URL）。設定時は
    /// <see cref="GitHubRepositoryUrl"/> より優先する（E2E 検証用）。
    /// </summary>
    public const string FeedEnvironmentVariable = "QUICKER_UPDATE_FEED";

    /// <summary>
    /// 実効の更新フィード文字列を解決する。環境変数（前後空白除去後に非空）を最優先し、
    /// 次に定数 <see cref="GitHubRepositoryUrl"/>（非空）を採用する。どちらも空なら <c>null</c>。
    /// </summary>
    /// <param name="getEnvironmentVariable">環境変数取得関数（テストで差し替え可能にするため注入する）</param>
    /// <returns>更新フィード文字列。フィード未設定のときは <c>null</c></returns>
    public static string? Resolve(Func<string, string?> getEnvironmentVariable)
    {
        // 環境変数が非空ならそれを最優先する（E2E 検証で任意のフィードへ差し替えられる）
        var fromEnvironment = getEnvironmentVariable(FeedEnvironmentVariable)?.Trim();

        if (!string.IsNullOrEmpty(fromEnvironment))
        {
            return fromEnvironment;
        }

        // 次に定数フィードを採用する（リポジトリ未公開なら空文字＝更新チェックしない）
        var fromConstant = GitHubRepositoryUrl.Trim();

        if (!string.IsNullOrEmpty(fromConstant))
        {
            return fromConstant;
        }

        return null;
    }
}
