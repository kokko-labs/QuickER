using System.IO;
using QuickER.Tests.Resources;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// ソースを走査するガードが読むファイルを、リポジトリの中のフォルダから列挙する（ビルド生成物の <c>bin</c> / <c>obj</c> を除く）
/// </summary>
/// <remarks>
/// 列挙するだけで、パースはしない。C# のソースの構文は、条件コンパイルのどの分岐も読む <see cref="ConditionalSourceParse"/>
/// （シンボルが要るなら <see cref="SourceSymbolScan"/>）で読む。
/// </remarks>
internal static class SourceFiles
{
    /// <summary>列挙したファイル 1 つ</summary>
    /// <param name="FullPath">フルパス</param>
    /// <param name="RelativePath">リポジトリのルートからの相対パス（区切りは <c>/</c>）</param>
    /// <param name="AreaRelativePath">列挙したフォルダからの相対パス（区切りは <c>/</c>）</param>
    internal sealed record Entry(string FullPath, string RelativePath, string AreaRelativePath);

    /// <summary>リポジトリのルート</summary>
    public static string RepositoryRoot => NeutralResxFiles.FindRepositoryRoot();

    /// <summary>リポジトリの中のフォルダ（例 <c>src</c>・<c>tests</c>・<c>src/QuickER.Model</c>）のファイルを、下のフォルダまで列挙する</summary>
    /// <param name="area">リポジトリのルートからのフォルダ（区切りは <c>/</c>）</param>
    /// <param name="pattern">ファイル名のパターン（既定は C# のソース）</param>
    public static IEnumerable<Entry> Enumerate(string area, string pattern = "*.cs")
    {
        var root = RepositoryRoot;
        var directory = Path.Combine([root, .. area.Split('/')]);

        return Directory
            .EnumerateFiles(directory, pattern, SearchOption.AllDirectories)
            .Select(path => new Entry(
                path,
                Path.GetRelativePath(root, path).Replace('\\', '/'),
                Path.GetRelativePath(directory, path).Replace('\\', '/')
            ))
            .Where(entry => !IsBuildOutput(entry.AreaRelativePath));
    }

    /// <summary>
    /// <c>src</c> のプロジェクトの csproj を、フォルダごとに 1 つ（<c>{フォルダ}/{フォルダ}.csproj</c>）だけ列挙する（フルパス・序数の順）
    /// </summary>
    /// <param name="root">リポジトリのルート（自己検査は合成の木の根を渡す）</param>
    /// <remarks>
    /// <para>
    /// 走査はフォルダを 1 プロジェクトとみなす（フォルダの下の .cs を全部そのプロジェクトのソースにする）。同じフォルダのほかの
    /// csproj（WPF のビルドが作って途中で止まると残る一時のプロジェクト <c>*_wpftmp.csproj</c> など）をプロジェクトとして読むと、
    /// 同じフォルダのソースを 2 回数える。<c>bin</c> / <c>obj</c> の下の csproj も読まない。
    /// </para>
    /// <para>
    /// csproj を持つのにフォルダの名前の csproj が無いフォルダは、フォルダを名指しして例外にする（名前の違う新しいプロジェクトを
    /// 黙って読まないことにしない）。
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> SrcProjectFiles(string root)
    {
        var src = Path.Combine(root, "src");
        var folders = Directory
            .EnumerateFiles(src, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(Path.GetRelativePath(src, path)))
            .Select(path => Path.GetDirectoryName(path)!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ToList();
        var unnamed = folders.Where(folder => !File.Exists(ProjectFileOf(folder))).ToList();

        if (unnamed.Count > 0)
        {
            throw new InvalidOperationException(
                "src の csproj を持つフォルダに、フォルダの名前の csproj（{フォルダ}/{フォルダ}.csproj）が無い："
                    + string.Join(
                        "、",
                        unnamed.Select(folder =>
                            Path.GetRelativePath(root, folder).Replace('\\', '/')
                        )
                    )
            );
        }

        return [.. folders.Select(ProjectFileOf)];
    }

    /// <summary>フォルダのプロジェクトの csproj（<c>{フォルダ}/{フォルダ}.csproj</c>）</summary>
    private static string ProjectFileOf(string folder) =>
        Path.Combine(folder, Path.GetFileName(folder) + ".csproj");

    /// <summary>相対パスがビルド生成物（<c>bin</c> / <c>obj</c> のフォルダの中）か</summary>
    /// <param name="relativePath">列挙したフォルダからの相対パス（区切りは <c>/</c> でも <c>\</c> でもよい）</param>
    public static bool IsBuildOutput(string relativePath) =>
        relativePath.Split('/', '\\').Any(segment => segment is "bin" or "obj");
}
