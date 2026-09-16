using System.IO;
using System.Linq;

namespace QuickER.AI.Mock;

/// <summary>
/// モックプロジェクトの出力フォルダを走査する共有ヘルパー（成果物検証・データ層の一覧化が共有する）。
/// </summary>
/// <remarks>
/// <para>
/// 素の <see cref="Directory.EnumerateFiles(string, string, SearchOption)"/> は
/// <c>IgnoreInaccessible=false</c> 相当で走るため、権限のないサブフォルダが 1 つあるだけで
/// <see cref="UnauthorizedAccessException"/> を投げる。成果物検証は「あるか無いか」を見たいだけなので、
/// 読めないフォルダは黙って飛ばす（<see cref="EnumerationOptions.IgnoreInaccessible"/>）。
/// </para>
/// <para>
/// 探索範囲はプロジェクトフォルダ（<c>{出力フォルダ}/{プロジェクト名}/</c>）に限る。出力フォルダ全体を
/// 走査すると、たまたま同じ場所にある無関係な csproj／UI ファイルで成果物検証が通ってしまう。
/// あわせてビルド中間物（<c>obj</c> / <c>bin</c>）は除外する（前回のビルドが残した生成物を
/// 「AI が書いた成果物」と数えないため）。除外するのは<b>起点フォルダ直下の 2 つだけ</b>で、
/// 深い階層の同名フォルダ（<c>Components/bin/</c> 等）は数える＝最終ビルドの前に消す対象
/// （<c>{プロジェクト}/obj</c>・<c>{プロジェクト}/bin</c>）と範囲を揃える。
/// </para>
/// </remarks>
internal static class MockProjectFiles
{
    /// <summary>ビルド中間物のフォルダ名（探索から除外する）</summary>
    private static readonly string[] ExcludedDirectoryNames = ["obj", "bin"];

    /// <summary>再帰列挙のオプション（アクセス拒否は無視する＝権限のないフォルダで落ちない）</summary>
    internal static readonly EnumerationOptions RecursiveOptions = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
    };

    /// <summary>
    /// 指定フォルダ配下を再帰列挙する（<c>obj</c> / <c>bin</c> 配下は除外・アクセス拒否は黙って飛ばす）。
    /// </summary>
    /// <param name="root">探索の起点（存在しなければ空を返す）</param>
    /// <param name="searchPattern">検索パターン（例 <c>*.csproj</c>）</param>
    internal static IEnumerable<string> Enumerate(string root, string searchPattern)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(root, searchPattern, RecursiveOptions)
            .Where(path => !IsUnderExcludedDirectory(root, path));
    }

    /// <summary>指定フォルダ配下に検索パターンへ一致するファイルが 1 つでもあるか</summary>
    /// <param name="root">探索の起点（存在しなければ false）</param>
    /// <param name="searchPattern">検索パターン（例 <c>*.razor</c>）</param>
    internal static bool HasAny(string root, string searchPattern) =>
        Enumerate(root, searchPattern).Any();

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
            && ExcludedDirectoryNames.Contains(segments[0], StringComparer.OrdinalIgnoreCase);
    }
}
