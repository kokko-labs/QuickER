namespace QuickER.Services;

/// <summary>
/// 更新フィード URL の解決を担う定数と純粋関数のヘルパ（UI・Velopack に依存しないため単体テスト可能）。
/// </summary>
/// <remarks>
/// 配布ビルド（Release）のフィードは <see cref="GitHubRepositoryUrl"/> 固定で、空なら更新チェック自体を行わない。
/// Debug ビルドに限り、環境変数 <see cref="FeedEnvironmentVariable"/>（ローカルパス or URL）が設定されていれば
/// そちらを優先する（E2E 検証用）。配布物でこの上書きを効かせないのは、更新元は利用者の環境変数で
/// 差し替えられてよい設定ではないため（任意のフィードを指せると、更新の受け口がそのまま
/// 任意コードの配布経路になる）。
/// </remarks>
public static class UpdateFeed
{
    /// <summary>
    /// 更新フィードとする GitHub リポジトリ URL（このリポジトリの GitHub Releases が配布フィード）。
    /// 空文字にすると既定の更新チェックを行わない。
    /// </summary>
    public const string GitHubRepositoryUrl = "https://github.com/kokko-labs/QuickER";

    /// <summary>
    /// 更新フィードを上書きする環境変数名（ローカルパス or URL）。**Debug ビルドでのみ**
    /// <see cref="GitHubRepositoryUrl"/> より優先する（E2E 検証用）。
    /// </summary>
    public const string FeedEnvironmentVariable = "QUICKER_UPDATE_FEED";

    /// <summary>
    /// 実効の更新フィード文字列を解決する。Debug ビルドでは環境変数（前後空白除去後に非空）を
    /// 最優先し、次に定数 <see cref="GitHubRepositoryUrl"/>（非空）を採用する。
    /// Release ビルドでは定数のみを見る。いずれも空なら <c>null</c>。
    /// </summary>
    /// <param name="getEnvironmentVariable">環境変数取得関数（テストで差し替え可能にするため注入する）</param>
    /// <returns>更新フィード文字列。フィード未設定のときは <c>null</c></returns>
    public static string? Resolve(Func<string, string?> getEnvironmentVariable)
    {
#if DEBUG
        // Debug ビルドに限り、環境変数が非空ならそれを最優先する
        // （E2E 検証でローカルフィードへ差し替えられる。配布物ではこの分岐ごとコンパイルされない）
        var fromEnvironment = getEnvironmentVariable(FeedEnvironmentVariable)?.Trim();

        if (!string.IsNullOrEmpty(fromEnvironment))
        {
            return fromEnvironment;
        }
#endif

        // 定数フィードを採用する（空文字なら更新チェックしない）
        var fromConstant = GitHubRepositoryUrl.Trim();

        if (!string.IsNullOrEmpty(fromConstant))
        {
            return fromConstant;
        }

        return null;
    }
}
