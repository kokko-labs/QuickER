namespace QuickER.Platform;

/// <summary>パスがフォルダの中にあるかの判定の正本</summary>
/// <remarks>
/// <para>
/// 書いてよい・読んでよい・消してよい範囲を「このフォルダの中」で決める入口（Copilot の自動承認・API キー方式の提出・
/// モックのプレビューの遷移とサブリソース・削除してよいかの重なりの判定）は、すべてここで判定する。入口ごとに文字列の前方一致を
/// 書くと、末尾の区切りの扱いが入口ごとに食い違う（ドライブ直下 <c>D:\</c> へ区切りを足して <c>D:\\</c> と比べ、配下を 1 件も
/// 認めない入口が出る）。
/// </para>
/// <para>
/// 両方を <see cref="Path.GetFullPath(string)"/> で正規化し（<c>..</c>・区切りの重なり・Windows が落とす区間の末尾の <c>.</c> と空白）、
/// 末尾の区切りを揃えてから、大文字小文字を区別せずに比べる。ドライブ直下（<c>C:\</c>）は区切りで終わる形のまま比べ、
/// 中にあるかの前方一致では区切りを足さない。比べるのはパスの文字列なので、リンク・8.3 の短い名前・<c>\\?\</c> の形のような別名は追わない
/// （別名が要る入口は呼び出し側で実体を解決してから渡す）。正規化できないパス（不正な文字・長すぎる）は「中にない」とする。
/// </para>
/// </remarks>
public static class PathContainment
{
    /// <summary><paramref name="path"/> が <paramref name="root"/> と同じか、その中にあるか</summary>
    /// <param name="path">調べるパス（相対パスは現在のフォルダから解決するので、絶対パスで渡す）</param>
    /// <param name="root">フォルダ</param>
    public static bool IsSameOrUnder(string? path, string? root) =>
        TryNormalize(path, out var full)
        && TryNormalize(root, out var folder)
        && (IsSame(full, folder) || IsStrictlyUnder(full, folder));

    /// <summary><paramref name="path"/> が <paramref name="root"/> の中にあるか（同じパスは含まない）</summary>
    /// <param name="path">調べるパス（絶対パスで渡す）</param>
    /// <param name="root">フォルダ</param>
    public static bool IsUnder(string? path, string? root) =>
        TryNormalize(path, out var full)
        && TryNormalize(root, out var folder)
        && IsStrictlyUnder(full, folder);

    /// <summary><paramref name="path"/> が <paramref name="folder"/> の直下にあるか（孫以下は含まない）</summary>
    /// <param name="path">調べるパス（絶対パスで渡す）</param>
    /// <param name="folder">フォルダ</param>
    public static bool IsDirectChild(string? path, string? folder)
    {
        if (!TryNormalize(path, out var full) || !TryNormalize(folder, out var parent))
        {
            return false;
        }

        var directory = Path.GetDirectoryName(full);
        return directory is not null
            && IsSame(Path.TrimEndingDirectorySeparator(directory), parent);
    }

    /// <summary>
    /// パスをフルパスへ正規化し、末尾の区切りを落とす（ドライブ直下は区切りで終わる形のまま）
    /// </summary>
    /// <param name="path">パス</param>
    /// <exception cref="ArgumentException">正規化できないパス</exception>
    public static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static bool TryNormalize(string? path, out string normalized)
    {
        normalized = string.Empty;

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            normalized = Normalize(path);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
        catch (PathTooLongException)
        {
            return false;
        }
    }

    private static bool IsSame(string full, string folder) =>
        string.Equals(full, folder, StringComparison.OrdinalIgnoreCase);

    private static bool IsStrictlyUnder(string full, string folder)
    {
        var prefix = Path.EndsInDirectorySeparator(folder)
            ? folder
            : folder + Path.DirectorySeparatorChar;
        return full.Length > prefix.Length
            && full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
