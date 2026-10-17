using System.IO;
using System.IO.Enumeration;
using System.Linq;
using QuickER.Platform;

namespace QuickER.AI.Mock;

/// <summary>
/// モックプロジェクトの出力フォルダを走査・削除する共有ヘルパー（成果物検証・データ層の一覧化・
/// 最終ビルド前の変更検知と中間物の削除・スキャフォールドの書き直しが共有する）。
/// </summary>
/// <remarks>
/// <para>
/// 素の <see cref="Directory.EnumerateFiles(string, string, SearchOption)"/> は
/// <c>IgnoreInaccessible=false</c> 相当で走るため、権限のないサブフォルダが 1 つあるだけで
/// <see cref="UnauthorizedAccessException"/> を投げる。成果物検証は「あるか無いか」を見たいだけなので、
/// 読めないフォルダは黙って飛ばす（<see cref="EnumerationOptions.IgnoreInaccessible"/>）。
/// </para>
/// <para>
/// 同じく素の再帰列挙はディレクトリのリンク（ジャンクション・シンボリックリンク）の中まで辿り、
/// 中を列挙できない reparse フォルダに当たると <see cref="IOException"/> で止まる。そこで再帰は
/// 中へ入れるディレクトリ（<see cref="IsOpaqueDirectory(string)"/> でないもの）にだけ行う
/// （<c>C:\</c> を指すジャンクション 1 つでディスク全体を走査しない・ランごと落ちない）。
/// 変更検知（<see cref="MockProjectBuildBoundary"/>）も同じ判定で再帰を止めるので、数えない範囲が揃う。
/// </para>
/// <para>
/// 探索範囲はプロジェクトフォルダ（<c>{出力フォルダ}/{プロジェクト名}/</c>）に限る。出力フォルダ全体を
/// 走査すると、たまたま同じ場所にある無関係な csproj／UI ファイルで成果物検証が通ってしまう。
/// あわせてビルド中間物（<see cref="BuildOutputDirectoryNames"/>）は除外する（前回のビルドが残した生成物を
/// 「AI が書いた成果物」と数えないため）。除外するのは<b>起点フォルダ直下の 2 つだけ</b>で、
/// 深い階層の同名フォルダ（<c>Components/bin/</c> 等）は数える＝最終ビルドの前に消す対象
/// （<c>{プロジェクト}/obj</c>・<c>{プロジェクト}/bin</c>）と範囲を揃える。
/// </para>
/// </remarks>
internal static class MockProjectFiles
{
    /// <summary>
    /// ビルド中間物のフォルダ名（プロジェクトフォルダ直下の 2 つ）。成果物の探索から除外し、
    /// 最終ビルドの前に削除し、API キー方式の提出先として拒否する（3 箇所がこの 1 つを共有する）。
    /// </summary>
    internal static readonly IReadOnlyList<string> BuildOutputDirectoryNames = ["obj", "bin"];

    /// <summary>成果物探索の再帰列挙のオプション（アクセス拒否は無視する＝権限のないフォルダで落ちない）</summary>
    private static readonly EnumerationOptions RecursiveOptions = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
    };

    /// <summary>
    /// 属性で何も飛ばさない再帰列挙のオプション（最終ビルド前の読み取り専用属性の解除が使う）。
    /// </summary>
    /// <remarks>
    /// 既定の <see cref="EnumerationOptions"/> は Hidden / System を飛ばすため、隠し属性を付けた
    /// <c>*.csproj.user</c> のようなファイルが列挙から消える＝検知の回避路になる。reparse point も属性では
    /// 飛ばさない（外を指すファイルのシンボリックリンクまで消える）＝再帰を止めるのは再帰判定の仕事。
    /// アクセス拒否は無視する（解除できなかった中身は、削除の後の残存検査が拾う）。変更検知はアクセス拒否を
    /// 無視してはいけないので、このオプションを使わない（<see cref="MockProjectBuildBoundary"/> の 1 段ずつの走査）。
    /// </remarks>
    internal static readonly EnumerationOptions AllEntriesOptions = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = 0,
    };

    /// <summary>
    /// 指定フォルダ配下を再帰列挙する（起点直下の <c>obj</c> / <c>bin</c> 配下は除外・アクセス拒否は黙って飛ばす・
    /// 中へ入れないディレクトリ〔<see cref="IsOpaqueDirectory(string)"/>〕の中へは入らない）。
    /// </summary>
    /// <param name="root">探索の起点（存在しなければ空を返す）</param>
    /// <param name="searchPattern">検索パターン（例 <c>*.csproj</c>）</param>
    internal static IEnumerable<string> Enumerate(string root, string searchPattern)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return [];
        }

        return new FileSystemEnumerable<string>(
            root,
            (ref FileSystemEntry entry) => entry.ToFullPath(),
            RecursiveOptions
        )
        {
            ShouldIncludePredicate = (ref FileSystemEntry entry) =>
                !entry.IsDirectory
                && FileSystemName.MatchesSimpleExpression(searchPattern, entry.FileName),
            ShouldRecursePredicate = (ref FileSystemEntry entry) => !IsOpaqueDirectory(ref entry),
        }.Where(path => !IsUnderExcludedDirectory(root, path));
    }

    /// <summary>指定フォルダ配下に検索パターンへ一致するファイルが 1 つでもあるか</summary>
    /// <param name="root">探索の起点（存在しなければ false）</param>
    /// <param name="searchPattern">検索パターン（例 <c>*.razor</c>）</param>
    internal static bool HasAny(string root, string searchPattern) =>
        Enumerate(root, searchPattern).Any();

    /// <summary>
    /// プロジェクトフォルダ（<c>{出力フォルダ}/{プロジェクト名}/</c>）配下に検索パターンへ一致するファイルが
    /// 1 つでもあるか（成果物検証と、承認待ちで止まった兆候の検出が共有する）。
    /// </summary>
    /// <param name="outputDirectory">出力フォルダ</param>
    /// <param name="projectName">プロジェクト名</param>
    /// <param name="searchPattern">検索パターン（UI 成果物ならターゲットのプロファイルが与える）</param>
    internal static bool HasAnyInProject(
        string outputDirectory,
        string projectName,
        string searchPattern
    ) =>
        HasAny(
            MockProjectScaffoldService.GetProjectDirectory(outputDirectory, projectName),
            searchPattern
        );

    /// <summary>
    /// リンク（ジャンクション・シンボリックリンク）か。判定は <see cref="FileSystemInfo.LinkTarget"/> で行う。
    /// </summary>
    /// <param name="info">判定するファイルまたはフォルダ</param>
    /// <returns>リンクなら true（リンク先を読めないときも true＝中へ辿らない側へ倒す）</returns>
    /// <remarks>
    /// <para>
    /// <see cref="FileAttributes.ReparsePoint"/> の属性ビットでは判定しない。reparse point にはリンク以外の
    /// 種類もあり、OneDrive の Files On-Demand のようなクラウドファイルのフォルダは、属性ビットが立っていても
    /// 中身は普通に読み書きできる。属性ビットで「辿らない」と決めると、そこに書かれたファイルが変更検知から
    /// 見えなくなり、削除も非再帰で試みて失敗する。
    /// </para>
    /// <para>
    /// 属性ビットは「立っていなければリンクではない」ことの安価な事前判定としてだけ使ってよい
    /// （リンクは必ず属性ビットを持つ）。変更検知と最終ビルド前の削除が同じこの判定を使う。
    /// </para>
    /// </remarks>
    internal static bool IsLink(FileSystemInfo info)
    {
        try
        {
            return info.LinkTarget is not null;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>
    /// 中へ入らずに 1 件として扱うディレクトリか＝リンク（<see cref="IsLink"/>）、または中を列挙できない
    /// reparse フォルダ。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 属性ビット（<see cref="FileAttributes.ReparsePoint"/>）だけで決めてはいけない。OneDrive の
    /// Files On-Demand のようなリンクでない reparse フォルダは中身を普通に読み書きでき、属性ビットで
    /// 止めるとそこへ書かれたファイルが変更検知から見えなくなる。属性ビットは安価な事前判定にだけ使う。
    /// </para>
    /// <para>
    /// 列挙できない reparse フォルダ（処理するフィルタが居ないタグ・停止したクラウド同期のフォルダなど）を
    /// 中へ入らない側へ倒すのは、再帰すると列挙そのものが <see cref="IOException"/> で止まり、ランごと落ちるため。
    /// </para>
    /// </remarks>
    internal static bool IsOpaqueDirectory(string path)
    {
        var info = new DirectoryInfo(path);

        try
        {
            if ((info.Attributes & FileAttributes.ReparsePoint) == 0)
            {
                return false;
            }
        }
        catch (IOException)
        {
            return true;
        }

        return IsLink(info) || !CanList(path);
    }

    /// <inheritdoc cref="IsOpaqueDirectory(string)"/>
    internal static bool IsOpaqueDirectory(ref FileSystemEntry entry) =>
        entry.IsDirectory
        && (entry.Attributes & FileAttributes.ReparsePoint) != 0
        && IsOpaqueDirectory(entry.ToFullPath());

    /// <summary>
    /// 2 つのフォルダが同じか、どちらかがもう一方の中にあるか（削除してよいかの判定用）。
    /// </summary>
    /// <param name="first">フォルダのパス</param>
    /// <param name="second">フォルダのパス</param>
    /// <returns>重なるなら true。リンクを解決しきれないときも true（重ならないと言い切れない側へ倒す）</returns>
    /// <remarks>
    /// 両方をフルパスへ正規化し、途中にあるリンク（ジャンクション・シンボリックリンク）を実体まで解決してから、
    /// 大文字小文字を区別せずに比べる。比べるのはパスの文字列なので、8.3 短縮名やハードリンクのような
    /// 別名は追わない（フォルダ選択ダイアログが返すのは長い名前の実パス）。
    /// </remarks>
    internal static bool PathsOverlap(string first, string second)
    {
        var a = ResolveRealPath(first);
        var b = ResolveRealPath(second);

        if (a is null || b is null)
        {
            return true;
        }

        return PathContainment.IsSameOrUnder(a, b) || PathContainment.IsSameOrUnder(b, a);
    }

    /// <summary>
    /// パスをフルパスへ正規化し、途中のリンクを実体まで解決したパスを返す（解決できなければ null）。
    /// 存在しない部分はそのまま残す（作られる前のフォルダも判定できる）。
    /// </summary>
    private static string? ResolveRealPath(string path)
    {
        try
        {
            var current = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

            // リンク先がさらにリンクを含む場合に備えて繰り返す（循環は回数の上限で打ち切る＝解決できない扱い）
            for (var hop = 0; hop < 64; hop++)
            {
                var resolved = ResolveFirstLink(current);

                if (resolved is null)
                {
                    return current;
                }

                current = Path.TrimEndingDirectorySeparator(Path.GetFullPath(resolved));
            }

            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// パスの先頭から見て最初のリンクを実体へ置き換えたパスを返す（リンクが無ければ null）。
    /// </summary>
    private static string? ResolveFirstLink(string fullPath)
    {
        var root = Path.GetPathRoot(fullPath) ?? string.Empty;
        var segments = fullPath[root.Length..]
            .Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries
            );
        var prefix = root;

        for (var i = 0; i < segments.Length; i++)
        {
            prefix = Path.Combine(prefix, segments[i]);
            var info = new DirectoryInfo(prefix);

            if (!info.Exists)
            {
                // ここから先はまだ無い＝リンクも無い
                return null;
            }

            if ((info.Attributes & FileAttributes.ReparsePoint) == 0 || info.LinkTarget is null)
            {
                continue;
            }

            // リンク先を最終的な実体まで辿る（辿れなければ例外＝呼び出し側で「解決できない」扱い）
            var target =
                info.ResolveLinkTarget(returnFinalTarget: true)
                ?? throw new IOException($"Could not resolve the link '{prefix}'.");
            return Path.Combine([target.FullName, .. segments[(i + 1)..]]);
        }

        return null;
    }

    /// <summary>
    /// フォルダを削除する（読み取り専用を外してから・リンクはリンクだけ外す）。失敗は握る
    /// （消せたかどうかは呼び出し側が存在確認で判断する）。
    /// </summary>
    /// <param name="path">削除するフォルダ（無ければ何もしない）</param>
    /// <remarks>
    /// Windows の <see cref="Directory.Delete(string, bool)"/> は読み取り専用の属性を外さないため、
    /// 読み取り専用のファイルが 1 つあるだけで削除が失敗する。リンクの中へは入らず、リンク先の中身には触れない。
    /// </remarks>
    internal static void TryDeleteDirectory(string path)
    {
        try
        {
            var info = new DirectoryInfo(path);

            if (!info.Exists)
            {
                return;
            }

            if (IsOpaqueDirectory(path))
            {
                // リンク先の中身は別物なので消さない（リンクの実体だけを外す）
                info.Delete(recursive: false);
                return;
            }

            ClearReadOnlyAttributes(path);
            info.Delete(recursive: true);
        }
        catch (IOException)
        {
            // 掴まれている等で消せない＝呼び出し側が残存として扱う
        }
        catch (UnauthorizedAccessException)
        {
            // 権限不足も同じ扱い
        }
    }

    /// <summary>
    /// フォルダとその配下（中へ入らないディレクトリの中は除く）の読み取り専用属性を外す。
    /// ファイルのリンクには触れない（リンク先の属性を変えないため）。
    /// </summary>
    private static void ClearReadOnlyAttributes(string root)
    {
        ClearReadOnly(root);

        var targets = new FileSystemEnumerable<string>(
            root,
            (ref FileSystemEntry entry) => entry.ToFullPath(),
            AllEntriesOptions
        )
        {
            ShouldIncludePredicate = (ref FileSystemEntry entry) =>
                (entry.Attributes & FileAttributes.ReadOnly) != 0
                && !(
                    entry.IsDirectory
                        ? IsOpaqueDirectory(ref entry)
                        : (entry.Attributes & FileAttributes.ReparsePoint) != 0
                            && IsLink(new FileInfo(entry.ToFullPath()))
                ),
            ShouldRecursePredicate = (ref FileSystemEntry entry) => !IsOpaqueDirectory(ref entry),
        };

        foreach (var target in targets)
        {
            ClearReadOnly(target);
        }
    }

    /// <summary>1 件の読み取り専用属性を外す（失敗しても続ける＝残れば呼び出し側が残存として扱う）</summary>
    private static void ClearReadOnly(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);

            if ((attributes & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
            }
        }
        catch (IOException)
        {
            // 削除の失敗として残存検査が拾う
        }
        catch (UnauthorizedAccessException)
        {
            // 同上
        }
    }

    /// <summary>フォルダの中を列挙できるか（最初の 1 件まで開いてみる）</summary>
    private static bool CanList(string path)
    {
        try
        {
            using var entries = Directory.EnumerateFileSystemEntries(path).GetEnumerator();
            entries.MoveNext();
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>ファイルが起点直下の <c>obj</c> / <c>bin</c> の配下にあるか</summary>
    /// <remarks>
    /// 見るのは相対パスの<b>先頭セグメントだけ</b>。途中の階層まで名前で弾くと、生成物が正当に作った
    /// <c>Components/bin/</c> のようなフォルダごと成果物検証から消える（実害は小さいが、削除側が
    /// 消すのは起点直下の 2 つだけなので、数えない範囲もそこへ揃えるのが筋）。
    /// </remarks>
    private static bool IsUnderExcludedDirectory(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        var segments = relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries
        );

        // segments.Length < 2 は起点直下のファイル＝フォルダを経由していない
        return segments.Length >= 2
            && BuildOutputDirectoryNames.Contains(segments[0], StringComparer.OrdinalIgnoreCase);
    }
}
