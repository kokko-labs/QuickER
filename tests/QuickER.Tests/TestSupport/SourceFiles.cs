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

    /// <summary>相対パスがビルド生成物（<c>bin</c> / <c>obj</c> のフォルダの中）か</summary>
    /// <param name="relativePath">列挙したフォルダからの相対パス（区切りは <c>/</c> でも <c>\</c> でもよい）</param>
    public static bool IsBuildOutput(string relativePath) =>
        relativePath.Split('/', '\\').Any(segment => segment is "bin" or "obj");
}
