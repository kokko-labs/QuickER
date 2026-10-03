using System.Text.RegularExpressions;

namespace QuickER.AI.Mock;

/// <summary>
/// 画面 HTML 内の相対画面リンク（<c>href="Foo.html"</c>）の抽出と、画面ファイル名から画面 ID を導く規則の単一正本。
/// </summary>
/// <remarks>
/// 保存時の検証（<see cref="MockContentValidator"/>）・単一 HTML のリンク書き換え（<see cref="MockBundleExporter"/>）・
/// 設計書の遷移図のノード ID（<see cref="MockDesignDocExporter"/>）が共有する。別々に持つと、検証が
/// 「解決できる」と判定したリンクを単一 HTML が書き換えない、といった食い違いが静かに生まれる。
/// </remarks>
internal static class MockScreenLinks
{
    /// <summary>
    /// <c>href="*.html"</c>（任意の <c>#フラグメント</c> 付き）を捕捉する正規表現。
    /// 群 1＝引用符・群 2＝リンク先・群 3＝フラグメント（<c>#</c> を含む・無ければ空）。
    /// 閉じの引用符は開きと同じものに限る（<c>href="X.html'</c> のような食い違いはリンクとして扱わない）。
    /// </summary>
    internal static readonly Regex RelativeScreenLink = new(
        "href\\s*=\\s*([\"'])([^\"']+?\\.html)(#[^\"']*)?\\1",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    /// <summary>
    /// リンク先（<see cref="RelativeScreenLink"/> の群 2）から画面ファイル名を取り出す。
    /// 絶対 URL（<c>://</c> を含む）は対象外。<c>\</c> は <c>/</c> として扱い、最後のセグメントを
    /// ファイル名とする（モックフォルダはフラット構成のため、<c>./</c> やフォルダ部分は捨てる）。
    /// </summary>
    /// <param name="href">リンク先の文字列</param>
    /// <param name="fileName">画面ファイル名（取り出せたとき）</param>
    /// <returns>相対の画面リンクとしてファイル名を取り出せたら true</returns>
    internal static bool TryGetScreenFile(string href, out string fileName)
    {
        fileName = string.Empty;

        if (href.Contains("://", StringComparison.Ordinal))
        {
            return false;
        }

        var normalized = href.Replace('\\', '/');
        var slash = normalized.LastIndexOf('/');
        var candidate = slash >= 0 ? normalized.Substring(slash + 1) : normalized;

        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        fileName = candidate;
        return true;
    }

    /// <summary>画面ファイル名から拡張子を除いた画面 ID を得る（例 <c>OrderList.html</c> → <c>OrderList</c>）</summary>
    /// <param name="file">画面ファイル名（フォルダ部分を含んでいても最後のセグメントを使う）</param>
    internal static string ScreenId(string file)
    {
        var normalized = file.Replace('\\', '/');
        var slash = normalized.LastIndexOf('/');

        if (slash >= 0)
        {
            normalized = normalized.Substring(slash + 1);
        }

        var dot = normalized.LastIndexOf('.');

        return dot > 0 ? normalized.Substring(0, dot) : normalized;
    }
}
