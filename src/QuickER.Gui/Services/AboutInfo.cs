using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace QuickER.Services;

/// <summary>バージョン情報ダイアログに表示する内容（アプリの版・実行環境・著作権・リポジトリ・ドキュメント）</summary>
/// <param name="Version">表示用の版（ビルドメタデータ <c>+…</c> を除いたもの。例: <c>0.1.0</c>）</param>
/// <param name="BuildVersion">ビルドメタデータ込みの版（不具合報告でコミットまで特定するため。例: <c>0.1.0+abc123</c>）</param>
/// <param name="Runtime">実行中の .NET ランタイムの説明（例: <c>.NET 10.0.0</c>）</param>
/// <param name="OperatingSystem">OS の説明</param>
/// <param name="Copyright">著作権表示（取得できなければ空）</param>
/// <param name="RepositoryUrl">ソースコードのリポジトリ URL</param>
/// <param name="DocumentationUrl">ドキュメントの目次の URL（表示言語に合わせた版）</param>
public sealed record AboutInfo(
    string Version,
    string BuildVersion,
    string Runtime,
    string OperatingSystem,
    string Copyright,
    string RepositoryUrl,
    string DocumentationUrl
)
{
    /// <summary>版を取得できなかったときの表示</summary>
    public const string UnknownVersion = "unknown";

    /// <summary>ソースコードのリポジトリ URL</summary>
    /// <remarks>
    /// 更新フィード（<see cref="UpdateFeed.GitHubRepositoryUrl"/>）と値は同じだが別の定数にする。
    /// あちらは空にすると更新チェックを無効にする設定を兼ねており、ソースコードの所在とは意味が違う
    /// </remarks>
    public const string DefaultRepositoryUrl = "https://github.com/kokko-labs/QuickER";

    /// <summary>英語版ドキュメントの目次の URL（既定ブランチの docs/README.md）</summary>
    public const string EnglishDocumentationUrl =
        DefaultRepositoryUrl + "/blob/main/docs/README.md";

    /// <summary>日本語版ドキュメントの目次の URL（既定ブランチの docs/README.ja.md）</summary>
    public const string JapaneseDocumentationUrl =
        DefaultRepositoryUrl + "/blob/main/docs/README.ja.md";

    /// <summary>GUI 本体（このクラスを含むアセンブリ）の版情報と実行環境から表示内容を組み立てる</summary>
    /// <remarks>
    /// エントリアセンブリは読まない。本番では GUI 本体と同じだが、テストホストの下では別物を指すため
    /// </remarks>
    public static AboutInfo Current() => FromAssembly(typeof(AboutInfo).Assembly);

    /// <summary>指定アセンブリの版情報・著作権と、現在の実行環境から表示内容を組み立てる</summary>
    /// <param name="assembly">版情報を読むアセンブリ</param>
    /// <param name="uiCulture">ドキュメントの言語を選ぶ表示言語（未指定は <see cref="CultureInfo.CurrentUICulture"/>）</param>
    public static AboutInfo FromAssembly(Assembly assembly, CultureInfo? uiCulture = null)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        var copyright = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright;

        return new AboutInfo(
            ToDisplayVersion(informationalVersion),
            string.IsNullOrWhiteSpace(informationalVersion) ? UnknownVersion : informationalVersion,
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription,
            copyright ?? string.Empty,
            DefaultRepositoryUrl,
            DocumentationUrlFor(uiCulture ?? CultureInfo.CurrentUICulture)
        );
    }

    /// <summary>表示言語に合わせたドキュメントの目次の URL を返す（日本語なら日本語版・それ以外は英語版）</summary>
    /// <remarks>
    /// GitHub はフォルダを開いたときに README.md（英語）しか表示しないため、日本語版はファイルを直接指す
    /// </remarks>
    /// <param name="uiCulture">表示言語</param>
    public static string DocumentationUrlFor(CultureInfo uiCulture)
    {
        ArgumentNullException.ThrowIfNull(uiCulture);

        return uiCulture.TwoLetterISOLanguageName == AppLanguage.Japanese
            ? JapaneseDocumentationUrl
            : EnglishDocumentationUrl;
    }

    /// <summary>
    /// 版の文字列からビルドメタデータ（<c>+</c> 以降のコミットハッシュ等）を除いた表示用の版を返す
    /// </summary>
    /// <param name="informationalVersion">アセンブリの InformationalVersion（null・空なら <see cref="UnknownVersion"/>）</param>
    public static string ToDisplayVersion(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            return UnknownVersion;
        }

        var plusIndex = informationalVersion.IndexOf('+');
        var version = plusIndex >= 0 ? informationalVersion[..plusIndex] : informationalVersion;

        return string.IsNullOrWhiteSpace(version) ? UnknownVersion : version;
    }

    /// <summary>
    /// 「版情報をコピー」でクリップボードへ渡すテキスト（不具合報告に貼る用途。表示言語に依らず英語固定）
    /// </summary>
    /// <remarks>クラッシュレポートと同じく、コミットまで特定できるようビルドメタデータ込みの版を載せる</remarks>
    public string ToClipboardText()
    {
        var builder = new StringBuilder();
        builder.AppendLine(CultureInfo.InvariantCulture, $"QuickER {Version}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Build: {BuildVersion}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Runtime: {Runtime}");
        builder.Append(CultureInfo.InvariantCulture, $"OS: {OperatingSystem}");

        return builder.ToString();
    }
}
