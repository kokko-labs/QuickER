using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using AwesomeAssertions;
using Xunit;

namespace QuickER.Tests.Dialects;

/// <summary>
/// 方言の宣言をまとめるプロジェクト <c>QuickER.Dialects</c> が、何も参照しない（依存ゼロ）ことを固定する。
/// </summary>
/// <remarks>
/// <para>
/// 方言名・識別子の上限などの方言の宣言は、意味モデル（<c>QuickER.Model</c>）・MCP のツール定義（<c>QuickER.Mcp</c>）から
/// 参照される最下層に置く。ここが別のプロジェクトやパッケージを参照すると、それが参照する側（Model・Mcp）の
/// 依存へそのまま広がる。
/// </para>
/// <para>
/// 依存の 3 種類（PackageReference / ProjectReference / FrameworkReference）をすべて見る。
/// </para>
/// </remarks>
public sealed class DialectsProjectDependencyGuardTests
{
    /// <summary>QuickER.Dialects の csproj（リポジトリ直下からの相対パス）</summary>
    private const string ProjectPath = "src/QuickER.Dialects/QuickER.Dialects.csproj";

    [Fact(
        DisplayName = "QuickER.Dialects は PackageReference・ProjectReference・FrameworkReference を持たない"
    )]
    public void Dialects_HasNoDependencies()
    {
        var path = ResolveRepoRelativePath(ProjectPath);
        File.Exists(path).Should().BeTrue($"{ProjectPath} が存在すること");

        var document = XDocument.Load(path);
        var references = document
            .Descendants()
            .Where(element =>
                element.Name.LocalName
                    is "PackageReference"
                        or "ProjectReference"
                        or "FrameworkReference"
            )
            .Select(element => $"{element.Name.LocalName} {(string?)element.Attribute("Include")}")
            .ToList();

        references
            .Should()
            .BeEmpty("方言の宣言は最下層に置き、何も参照しない（参照する側の依存の層を崩さない）");
    }

    /// <summary>リポジトリ直下（QuickER.slnx）を目印に相対パスを絶対パスへ解決する</summary>
    private static string ResolveRepoRelativePath(string repoRelativePath)
    {
        var dir = new DirectoryInfo(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!
        );

        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "QuickER.slnx")))
            {
                return Path.Combine(
                    dir.FullName,
                    repoRelativePath.Replace('/', Path.DirectorySeparatorChar)
                );
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            $"リポジトリ直下（QuickER.slnx）が見つからず {repoRelativePath} を解決できませんでした。"
        );
    }
}
