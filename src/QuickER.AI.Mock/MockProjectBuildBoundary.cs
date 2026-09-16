using System.IO;
using System.IO.Enumeration;
using System.Linq;
using System.Security.Cryptography;

namespace QuickER.AI.Mock;

/// <summary>
/// 最終ビルド（<c>dotnet build</c>）を実行する前に、エージェントが出力フォルダへ書いた
/// 「ビルドが設定として読み得るファイル」を洗い出すための差分検知。
/// </summary>
/// <remarks>
/// <para>
/// 最終ビルドは QuickER がサンドボックスの外・ユーザー権限で実行する。エージェント型バックエンドは
/// 自分のサンドボックス内で csproj などを書けるので、そこに書かれた内容をこのビルドが実行すると、
/// CLI の権限モデルを QuickER の処理が越えることになる。そこでスキャフォールド直後の状態を記録し、
/// ビルド直前に差分を取って、UI 層のソース・静的資産以外が増減していれば利用者へ確認する。
/// </para>
/// <para>
/// <b>判定はホワイトリストで行う（名前の列挙にしない）。</b> MSBuild／dotnet が自動で読む入口は
/// バージョンとともに増えるので、危険なファイル名を列挙する方式では原理的に追いつけない
/// （<see cref="MockProjectEmitTools.ResolveEmitPath"/> と同じ理屈）。通すのは「ビルドが設定として
/// 解釈することのない UI 層のソースと静的資産」だけで、それ以外は正当な追加（<c>appsettings.json</c> 等）
/// であっても一覧に出す＝一覧が少し長くなるだけで、危険な入口を取りこぼさない。
/// </para>
/// <para>
/// <b>比較は内容のハッシュで行う（更新日時・サイズでは比べない）。</b> エージェントは書き換えたあとで
/// <see cref="File.SetLastWriteTimeUtc(string, DateTime)"/> によりタイムスタンプを元へ戻せるため、
/// メタデータの比較は回避できてしまう。
/// </para>
/// </remarks>
internal static class MockProjectBuildBoundary
{
    /// <summary>
    /// 確認なしで通す拡張子（UI 層のソースと静的資産。大文字小文字を無視して照合する）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 実測（.NET SDK 10.0.401 / Windows 11 26200）で決めた集合。<c>Microsoft.NET.Sdk.Web</c> の
    /// プロジェクトを <c>dotnet msbuild -pp</c> で前処理して import 実体 301 件を数えると、内訳は
    /// <c>.targets</c> 211・<c>.props</c> 86・<c>.csproj</c> 3・<c>.pubxml</c> 1 で、この 4 種以外は
    /// 1 件も import されない。加えて import 宣言には
    /// <c>$(MSBuildProjectFullPath).user</c>（<c>.csproj.user</c>）と
    /// <c>$(_PublishProfilesDir)$(PublishProfileName).pubxml</c> が含まれ、実際に <c>.csproj.user</c> へ
    /// 置いたターゲットは、ランナーの隔離引数一式（自動 import 無効化 5 プロパティ＋<c>-noAutoResponse</c>）を
    /// 付けたビルドでも実行された。<c>global.json</c> は MSBuild より前に dotnet が読むため、
    /// 出力フォルダへ置くだけで SDK の解決先が変わる（実測）。
    /// </para>
    /// <para>
    /// 同じ実測で、ここに載せた拡張子（<c>.css</c> / <c>.js</c> / <c>.html</c> / <c>.razor</c>）へ
    /// MSBuild のターゲットを書いても、ビルドは一切それを実行しなかった。<c>.cs</c> と <c>.razor</c> は
    /// コンパイル対象であってビルド時に実行されるわけではない（ソースジェネレータは csproj 経由でしか
    /// 追加できず、csproj の変更はこの差分に出る）。
    /// </para>
    /// <para>
    /// <c>.json</c> / <c>.props</c> / <c>.targets</c> / <c>.user</c> / <c>.pubxml</c> / <c>.config</c> /
    /// <c>.rsp</c> / <c>.csproj</c> / <c>.sln</c> は<b>決してここへ入れない</b>。
    /// （API キー方式の提出ホワイトリスト <see cref="MockProjectEmitTools.SupportedEmitExtensions"/> とは
    /// 別の集合＝あちらは「AI に書かせてよいもの」、こちらは「書かれていても確認を要しないもの」で、
    /// 静的資産の分だけこちらが広い。）
    /// </para>
    /// </remarks>
    internal static readonly IReadOnlySet<string> SafeExtensions = new HashSet<string>(
        StringComparer.OrdinalIgnoreCase
    )
    {
        // UI 層のソース
        ".cs",
        ".razor",
        ".xaml",
        // 静的資産
        ".css",
        ".js",
        ".html",
        ".htm",
        ".svg",
        ".png",
        ".jpg",
        ".jpeg",
        ".gif",
        ".webp",
        ".ico",
    };

    /// <summary>
    /// 走査オプション。<see cref="FileAttributes.Hidden"/> / <see cref="FileAttributes.System"/> /
    /// <see cref="FileAttributes.ReparsePoint"/> を<b>列挙からは飛ばさない</b>（既定の
    /// <see cref="EnumerationOptions"/> は前 2 属性を飛ばすため、隠し属性を付けた <c>*.csproj.user</c> が
    /// 差分から消える＝検知の回避路になる）。
    /// </summary>
    /// <remarks>
    /// reparse point を属性で飛ばしてはいけない＝外を指すファイルのシンボリックリンク
    /// （<c>X.csproj.user</c>）まで列挙から消える。辿らないのは<b>ディレクトリのリンクの中への再帰だけ</b>で、
    /// それは <see cref="EnumerateEntries"/> の再帰判定が担う。
    /// </remarks>
    private static readonly EnumerationOptions EnumerationOptions = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = 0,
    };

    /// <summary>ディレクトリのリンクをスナップショットへ記録するときの値の接頭辞（ファイルのハッシュと衝突しない）</summary>
    private const string DirectoryLinkMarker = "link:";

    /// <summary>ビルド中間物のフォルダ名（プロジェクトフォルダ直下の 2 つ）</summary>
    private static readonly string[] BuildOutputDirectoryNames = ["obj", "bin"];

    /// <summary>
    /// 出力フォルダ配下のファイル内容ハッシュを記録したスナップショット。
    /// </summary>
    /// <remarks>
    /// <c>{プロジェクト名}/obj</c> と <c>{プロジェクト名}/bin</c> は、最終ビルドの直前に削除されるため
    /// 記録も比較もしない（削除する範囲と検知の範囲を揃える）。
    /// </remarks>
    internal sealed class Snapshot
    {
        private readonly Dictionary<string, string> _hashes;

        private Snapshot(Dictionary<string, string> hashes) => _hashes = hashes;

        /// <summary>
        /// 出力フォルダの現状を記録する（読めないファイルは記録しない＝後で「追加」として出る）。
        /// <see cref="SafeExtensions"/> に当たるファイルは比較しないので読まない。
        /// </summary>
        /// <param name="outputDirectory">出力フォルダ（ソリューション直下）</param>
        /// <param name="projectName">プロジェクト名（obj / bin の除外位置を決める）</param>
        internal static Snapshot Capture(string outputDirectory, string projectName)
        {
            var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in EnumerateEntries(outputDirectory, projectName))
            {
                if (IsExemptFromComparison(entry))
                {
                    continue;
                }

                var value = Fingerprint(entry);

                if (value is not null)
                {
                    hashes[entry.Relative] = value;
                }
            }

            return new Snapshot(hashes);
        }

        /// <summary>
        /// 記録時点から追加・変更されたファイルのうち、<see cref="SafeExtensions"/> に該当しないものを
        /// 相対パスの昇順で返す（削除は対象外＝ビルドが失敗するだけで、ビルドに何かを実行させはしない）。
        /// </summary>
        /// <param name="outputDirectory">出力フォルダ（記録時と同じもの）</param>
        /// <param name="projectName">プロジェクト名（記録時と同じもの）</param>
        internal IReadOnlyList<string> DetectUnexpectedChanges(
            string outputDirectory,
            string projectName
        )
        {
            var changed = new List<string>();

            foreach (var entry in EnumerateEntries(outputDirectory, projectName))
            {
                if (IsExemptFromComparison(entry))
                {
                    continue;
                }

                var value = Fingerprint(entry);

                // 読めなくなったファイルは「変わっていない」と言い切れないので変更側へ倒す
                if (
                    value is null
                    || !_hashes.TryGetValue(entry.Relative, out var baseline)
                    || value != baseline
                )
                {
                    changed.Add(entry.Relative);
                }
            }

            changed.Sort(StringComparer.OrdinalIgnoreCase);
            return changed;
        }
    }

    /// <summary>走査で見つけた 1 件（ファイル、または辿らなかったディレクトリのリンク）</summary>
    /// <param name="Relative">出力フォルダからの相対パス（区切りは <c>/</c>）</param>
    /// <param name="FullPath">絶対パス</param>
    /// <param name="IsDirectoryLink">ディレクトリの reparse point（ジャンクション・シンボリックリンク）か</param>
    private readonly record struct Entry(string Relative, string FullPath, bool IsDirectoryLink);

    /// <summary>
    /// 比較から外すか＝<see cref="SafeExtensions"/> に当たる<b>ファイル</b>。ディレクトリのリンクは名前に
    /// 依らず必ず比較する（<c>assets.css</c> という名前のジャンクションで素通りさせない）。
    /// </summary>
    private static bool IsExemptFromComparison(Entry entry) =>
        !entry.IsDirectoryLink && SafeExtensions.Contains(Path.GetExtension(entry.Relative));

    /// <summary>
    /// 比較に使う値（ファイルは内容の SHA-256・ディレクトリのリンクは接頭辞付きのリンク先）。読めなければ null。
    /// </summary>
    private static string? Fingerprint(Entry entry)
    {
        if (!entry.IsDirectoryLink)
        {
            return TryComputeHash(entry.FullPath);
        }

        try
        {
            return DirectoryLinkMarker
                + (new DirectoryInfo(entry.FullPath).LinkTarget ?? string.Empty);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// 出力フォルダ配下のファイルとディレクトリのリンクを列挙する（<c>{プロジェクト}/obj</c>・<c>bin</c> は除く）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>ディレクトリのリンクの中へは再帰しない</b>。辿ると (1) <c>C:\</c> を指すジャンクション 1 つで
    /// ディスク全体をハッシュし続け（この走査は中断トークンを見ない）ランが終わらない (2) 出力フォルダの外の
    /// ファイルが「出力フォルダ内の相対パス」で一覧に紛れ込む。代わりにリンクそのものを 1 件として返し、
    /// 比較側がリンク先ごと「追加・変更」として一覧に出す。
    /// </para>
    /// <para>
    /// ファイルのシンボリックリンクは通常のファイルとして返す（内容はリンク先から読まれる＝外を指す
    /// <c>X.csproj.user</c> も一覧に出る）。
    /// </para>
    /// </remarks>
    private static IEnumerable<Entry> EnumerateEntries(string outputDirectory, string projectName)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
        {
            return [];
        }

        var excludedPrefixes = BuildOutputDirectoryNames
            .Select(name => $"{projectName}/{name}/")
            .ToArray();

        bool IsExcluded(string relative) =>
            excludedPrefixes.Any(prefix =>
                relative.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            );

        string ToRelative(string fullPath) =>
            Path.GetRelativePath(outputDirectory, fullPath).Replace('\\', '/');

        return new FileSystemEnumerable<Entry>(
            outputDirectory,
            (ref FileSystemEntry entry) =>
            {
                var fullPath = entry.ToFullPath();
                return new Entry(ToRelative(fullPath), fullPath, entry.IsDirectory);
            },
            EnumerationOptions
        )
        {
            // ファイルと、辿らないディレクトリのリンクだけを返す（obj / bin 配下は返さない）
            ShouldIncludePredicate = (ref FileSystemEntry entry) =>
                (!entry.IsDirectory || (entry.Attributes & FileAttributes.ReparsePoint) != 0)
                && !IsExcluded(ToRelative(entry.ToFullPath())),
            // 再帰は通常のディレクトリだけ（リンクの中と obj / bin の中へは入らない）
            ShouldRecursePredicate = (ref FileSystemEntry entry) =>
                (entry.Attributes & FileAttributes.ReparsePoint) == 0
                && !IsExcluded(ToRelative(entry.ToFullPath()) + "/"),
        };
    }

    /// <summary>ファイル内容の SHA-256 を 16 進で返す（読めなければ null）</summary>
    private static string? TryComputeHash(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
