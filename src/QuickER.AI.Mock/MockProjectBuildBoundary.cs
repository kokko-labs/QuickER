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
    /// 消せずに残ったビルド中間物を列挙するときの走査オプション（読めないフォルダを黙って飛ばさない）。
    /// </summary>
    /// <remarks>
    /// 残存の一覧は「確認なしでビルドしてよいか」の唯一の根拠なので、読めないところは例外にして
    /// 呼び出し側でフォルダごと残存として扱う（<see cref="ListResidue"/>）。
    /// </remarks>
    private static readonly EnumerationOptions ResidueEnumerationOptions = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = false,
        AttributesToSkip = 0,
    };

    /// <summary>
    /// 変更検知が 1 段ずつフォルダを開くときの走査オプション（読めないフォルダを黙って飛ばさない・属性で何も飛ばさない）。
    /// </summary>
    /// <remarks>
    /// 成果物の探索（<see cref="MockProjectFiles.Enumerate"/>）はアクセス拒否を無視するが、変更検知はそうしてはいけない。
    /// エージェントは出力フォルダの ACL を変えて自分に「一覧の読み取り」だけを拒否でき、ファイルは名前で開けるので
    /// MSBuild は読むのに、無視する走査では差分が空になって確認なしでビルドへ進む。開けなかったフォルダは
    /// <see cref="EnumerateComparedEntries"/> がフォルダ自身を 1 件として返し、比較は毎回「変更」として一覧に出す。
    /// </remarks>
    private static readonly EnumerationOptions ComparedLevelOptions = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = false,
        AttributesToSkip = 0,
    };

    /// <summary>ディレクトリのリンクをスナップショットへ記録するときの値の接頭辞（ファイルのハッシュと衝突しない）</summary>
    private const string DirectoryLinkMarker = "link:";

    /// <summary>
    /// 最終ビルドの前にビルド中間物（<c>{プロジェクト}/obj</c>・<c>bin</c>）を削除し、消せずに残ったものを返す。
    /// </summary>
    /// <param name="outputDirectory">出力フォルダ（ソリューション直下）</param>
    /// <param name="projectName">プロジェクト名</param>
    /// <returns>残ったファイルと中へ入らないディレクトリ（出力フォルダからの相対パス・区切りは <c>/</c>・昇順）</returns>
    /// <remarks>
    /// <para>
    /// <c>obj/{プロジェクト}.csproj.*.targets</c> / <c>*.props</c> は <c>Microsoft.Common.targets</c> の
    /// ワイルドカード import で読み込まれるため、AI がそこへ書いたターゲットは最終ビルドで実行される
    /// （実測で確認。<c>nuget.g.targets</c> だけは復元が作り直すが、同じワイルドカードに当たる別名は残る）。
    /// 変更検知はこの 2 つを見ない（消す側だから）ので、<b>消せなかったものはここで必ず拾って返す</b>
    /// ＝削除の失敗を黙って握らない（フェイルクローズ）。
    /// </para>
    /// <para>
    /// 削除の前に読み取り専用の属性を外す（Windows の <see cref="Directory.Delete(string, bool)"/> は外さないため、
    /// 読み取り専用のファイルが 1 つあるだけで削除が失敗する）。リンクの中へは入らず、リンクはリンクだけを外す
    /// （リンク先の中身には触れない）。
    /// </para>
    /// </remarks>
    internal static IReadOnlyList<string> ClearBuildOutputs(
        string outputDirectory,
        string projectName
    )
    {
        var projectDirectory = MockProjectScaffoldService.GetProjectDirectory(
            outputDirectory,
            projectName
        );
        var residue = new List<string>();

        foreach (var name in MockProjectFiles.BuildOutputDirectoryNames)
        {
            var root = Path.Combine(projectDirectory, name);
            MockProjectFiles.TryDeleteDirectory(root);
            residue.AddRange(ListResidue(root, outputDirectory));
        }

        residue.Sort(StringComparer.OrdinalIgnoreCase);
        return residue;
    }

    /// <summary>
    /// 削除しようとしたフォルダに残っているものを返す（フォルダが無ければ空）。
    /// </summary>
    /// <remarks>
    /// 拡張子のホワイトリストは掛けない（<c>obj</c> の中は拡張子に依らず怪しい）。フォルダ自体が中へ入らない
    /// ディレクトリのとき、または中を読み切れないときはフォルダそのものを 1 件として返す。空のフォルダだけが
    /// 残ったときは何も返さない（ビルドが読むものが無い）。
    /// </remarks>
    private static IEnumerable<string> ListResidue(string root, string outputDirectory)
    {
        if (!Directory.Exists(root))
        {
            return [];
        }

        var rootRelative = ToRelative(outputDirectory, root);

        if (MockProjectFiles.IsOpaqueDirectory(root))
        {
            return [rootRelative];
        }

        try
        {
            return EnumerateEntries(root, outputDirectory, _ => false, ResidueEnumerationOptions)
                .Select(entry => entry.Relative)
                .ToList();
        }
        catch (IOException)
        {
            return [rootRelative];
        }
        catch (UnauthorizedAccessException)
        {
            return [rootRelative];
        }
    }

    /// <summary>
    /// 出力フォルダ配下のファイル内容ハッシュを記録したスナップショット。
    /// </summary>
    /// <remarks>
    /// <c>{プロジェクト名}/obj</c> と <c>{プロジェクト名}/bin</c> は（フォルダ自身も含めて）最終ビルドの直前に
    /// 削除されるため記録も比較もしない（削除する範囲と検知の範囲を揃える）。消せずに残ったものは
    /// <see cref="ClearBuildOutputs"/> が返す。
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
        /// <param name="cancellationToken">
        /// キャンセルトークン（1 件ごとに確かめる。打ち切られたら <see cref="OperationCanceledException"/>）
        /// </param>
        internal static Snapshot Capture(
            string outputDirectory,
            string projectName,
            CancellationToken cancellationToken = default
        )
        {
            var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in EnumerateComparedEntries(outputDirectory, projectName))
            {
                cancellationToken.ThrowIfCancellationRequested();

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

            foreach (var entry in EnumerateComparedEntries(outputDirectory, projectName))
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

    /// <summary>走査で見つけた 1 件（ファイル、中へ入らなかったディレクトリ、または一覧を読めなかったディレクトリ）</summary>
    /// <param name="Relative">出力フォルダからの相対パス（区切りは <c>/</c>。出力フォルダ自身は <c>.</c>）</param>
    /// <param name="FullPath">絶対パス</param>
    /// <param name="IsOpaqueDirectory">
    /// 中へ入らなかったディレクトリ（リンク、または中を列挙できない reparse フォルダ）か
    /// </param>
    /// <param name="IsUnlistable">一覧を読めなかったディレクトリ（アクセス拒否など）か</param>
    private readonly record struct Entry(
        string Relative,
        string FullPath,
        bool IsOpaqueDirectory,
        bool IsUnlistable = false
    );

    /// <summary>
    /// 比較から外すか＝<see cref="SafeExtensions"/> に当たる<b>ファイル</b>。中へ入らなかったディレクトリと
    /// 一覧を読めなかったディレクトリは名前に依らず必ず比較する（<c>assets.css</c> という名前のジャンクションで
    /// 素通りさせない）。
    /// </summary>
    private static bool IsExemptFromComparison(Entry entry) =>
        !entry.IsOpaqueDirectory
        && !entry.IsUnlistable
        && SafeExtensions.Contains(Path.GetExtension(entry.Relative));

    /// <summary>
    /// 比較に使う値（ファイルは内容の SHA-256・ディレクトリのリンクは接頭辞付きのリンク先）。読めなければ null。
    /// </summary>
    /// <remarks>
    /// 一覧を読めなかったディレクトリと、リンクでないのに中を列挙できない reparse フォルダは null を返す＝記録されず、
    /// 比較では毎回「変更」として一覧に出る（中身を確かめられない以上、変わっていないとは言えないため）。
    /// </remarks>
    private static string? Fingerprint(Entry entry)
    {
        if (entry.IsUnlistable)
        {
            return null;
        }

        if (!entry.IsOpaqueDirectory)
        {
            return TryComputeHash(entry.FullPath);
        }

        try
        {
            var target = new DirectoryInfo(entry.FullPath).LinkTarget;
            return target is null ? null : DirectoryLinkMarker + target;
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

    /// <summary>出力フォルダからの相対パス（区切りは <c>/</c>）</summary>
    private static string ToRelative(string outputDirectory, string fullPath) =>
        Path.GetRelativePath(outputDirectory, fullPath).Replace('\\', '/');

    /// <summary>
    /// 比較の対象を列挙する＝出力フォルダ配下（<c>{プロジェクト}/obj</c>・<c>bin</c> はフォルダ自身も含めて除く）。
    /// </summary>
    /// <remarks>
    /// フォルダは 1 段ずつ開き（<see cref="ComparedLevelOptions"/>）、開けなかったフォルダ（出力フォルダ自身を含む）は
    /// 中を読まずにフォルダ自身を 1 件として返す（<see cref="Entry.IsUnlistable"/>）。リンクと中を列挙できない reparse
    /// フォルダの中へは入らない・属性で何も飛ばさない、は <see cref="EnumerateEntries"/> と同じ。
    /// </remarks>
    private static IEnumerable<Entry> EnumerateComparedEntries(
        string outputDirectory,
        string projectName
    )
    {
        if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
        {
            return [];
        }

        var buildOutputs = MockProjectFiles
            .BuildOutputDirectoryNames.Select(name => $"{projectName}/{name}")
            .ToArray();

        bool IsBuildOutput(string relative) =>
            buildOutputs.Any(root =>
                relative.Equals(root, StringComparison.OrdinalIgnoreCase)
                || relative.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase)
            );

        var entries = new List<Entry>();
        CollectComparedEntries(outputDirectory, outputDirectory, IsBuildOutput, entries);
        return entries;
    }

    /// <summary>
    /// フォルダを 1 段開いて、ファイル・中へ入らないディレクトリを集め、入れるディレクトリへ再帰する。
    /// </summary>
    /// <param name="directory">開くフォルダ</param>
    /// <param name="outputDirectory">相対パスの基準（出力フォルダ）</param>
    /// <param name="isExcluded">除外する相対パスか（除外したディレクトリの中へも入らない）</param>
    /// <param name="entries">集めた項目</param>
    /// <remarks>
    /// 開けなかったとき・途中で列挙が失敗したときは、そのフォルダで読めた分も捨ててフォルダ自身を 1 件として返す
    /// （一部だけ比べると、読めなかった残りの変更を「変わっていない」として見逃す）。
    /// </remarks>
    private static void CollectComparedEntries(
        string directory,
        string outputDirectory,
        Func<string, bool> isExcluded,
        List<Entry> entries
    )
    {
        List<(string FullPath, bool IsDirectory, bool IsOpaque)> children;

        try
        {
            children = new FileSystemEnumerable<(string, bool, bool)>(
                directory,
                (ref FileSystemEntry entry) =>
                    (
                        entry.ToFullPath(),
                        entry.IsDirectory,
                        MockProjectFiles.IsOpaqueDirectory(ref entry)
                    ),
                ComparedLevelOptions
            ).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            entries.Add(
                new Entry(
                    ToRelative(outputDirectory, directory),
                    directory,
                    IsOpaqueDirectory: false,
                    IsUnlistable: true
                )
            );
            return;
        }

        foreach (var (fullPath, isDirectory, isOpaque) in children)
        {
            var relative = ToRelative(outputDirectory, fullPath);

            if (isExcluded(relative))
            {
                continue;
            }

            if (!isDirectory || isOpaque)
            {
                entries.Add(new Entry(relative, fullPath, isDirectory));
                continue;
            }

            CollectComparedEntries(fullPath, outputDirectory, isExcluded, entries);
        }
    }

    /// <summary>
    /// 指定フォルダ配下のファイルと、中へ入らないディレクトリ（<see cref="MockProjectFiles.IsOpaqueDirectory(string)"/>）を列挙する。
    /// </summary>
    /// <param name="root">走査の起点</param>
    /// <param name="outputDirectory">相対パスの基準（出力フォルダ）</param>
    /// <param name="isExcluded">除外する相対パスか（除外したディレクトリの中へも入らない）</param>
    /// <param name="options">走査オプション</param>
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
    private static IEnumerable<Entry> EnumerateEntries(
        string root,
        string outputDirectory,
        Func<string, bool> isExcluded,
        EnumerationOptions options
    ) =>
        new FileSystemEnumerable<Entry>(
            root,
            (ref FileSystemEntry entry) =>
            {
                var fullPath = entry.ToFullPath();
                return new Entry(
                    ToRelative(outputDirectory, fullPath),
                    fullPath,
                    entry.IsDirectory
                );
            },
            options
        )
        {
            // ファイルと、中へ入らないディレクトリだけを返す（除外したものは返さない）
            ShouldIncludePredicate = (ref FileSystemEntry entry) =>
                (!entry.IsDirectory || MockProjectFiles.IsOpaqueDirectory(ref entry))
                && !isExcluded(ToRelative(outputDirectory, entry.ToFullPath())),
            // 再帰は中へ入れるディレクトリだけ（リンク・列挙できない reparse フォルダ・除外の中へは入らない）
            ShouldRecursePredicate = (ref FileSystemEntry entry) =>
                !MockProjectFiles.IsOpaqueDirectory(ref entry)
                && !isExcluded(ToRelative(outputDirectory, entry.ToFullPath())),
        };

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
