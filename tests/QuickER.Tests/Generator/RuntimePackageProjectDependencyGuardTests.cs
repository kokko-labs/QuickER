using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace QuickER.Tests.Generator;

/// <summary>
/// ランタイムパッケージ 3 プロジェクト（<c>QuickER.Runtime</c> / <c>.SqlServer</c> / <c>.Sqlite</c>）の
/// csproj が宣言する依存集合（PackageReference / ProjectReference）を検証し、パッケージ境界での依存排他を守る。
/// </summary>
/// <remarks>
/// <para>
/// 生成物レベルの依存排他ガード（<c>CSharpCodeGenerationServiceTests</c> の「EF 単独出力に SqlClient なし」等・
/// <c>MultiTargetRepositoryGenerationTests</c> の方言別排他）と対をなす、csproj レベルの排他ガード。
/// これにより、公開される .nupkg の nuspec 依存が意図どおり（Core=依存ゼロ・方言相互排他）であることを構造上保証する。
/// </para>
/// <para>
/// DI 登録拡張（<c>AddGenerated*Repositories</c>）はエンティティ別登録を含むスキーマ依存物として常に生成側へ
/// 出力される（パッケージ書き出しでは抑止）ため、パッケージは <c>Microsoft.Extensions.DependencyInjection</c> 系にも
/// 依存しない。依存集合は完全一致で検証する。
/// </para>
/// </remarks>
public sealed class RuntimePackageProjectDependencyGuardTests
{
    /// <summary>コアパッケージは PackageReference を 1 つも持たない（BCL のみ依存）</summary>
    [Fact(DisplayName = "QuickER.Runtime（コア）は PackageReference ゼロ（BCL のみ依存）")]
    public void Core_HasNoPackageReferences()
    {
        var packages = PackageReferences("src/QuickER.Runtime/QuickER.Runtime.csproj");

        packages.Should().BeEmpty("コアは BCL のみに依存し、いかなる NuGet 依存も持ってはならない");
    }

    /// <summary>SqlServer パッケージは SqlClient のみを持ち、Sqlite / EF Core 系を持たない</summary>
    [Fact(
        DisplayName = "QuickER.Runtime.SqlServer は Microsoft.Data.SqlClient のみ（Sqlite / EF Core なし）"
    )]
    public void SqlServer_HasSqlClientOnly()
    {
        var packages = PackageReferences(
            "src/QuickER.Runtime.SqlServer/QuickER.Runtime.SqlServer.csproj"
        );

        packages
            .Should()
            .BeEquivalentTo(
                new[] { "Microsoft.Data.SqlClient" },
                "SqlServer パッケージの NuGet 依存は ADO（SqlClient）だけに保つ"
                    + "（Sqlite / EF Core / DI 系が混ざってはならない。DI 登録拡張はスキーマ依存物として生成側に出力される）"
            );

        // 依存はコアへの ProjectReference のみ（他方言プロジェクトを参照しない）
        var projects = ProjectReferences(
            "src/QuickER.Runtime.SqlServer/QuickER.Runtime.SqlServer.csproj"
        );
        projects.Should().ContainSingle().Which.Should().EndWith("QuickER.Runtime.csproj");
    }

    /// <summary>Sqlite パッケージは Sqlite + SQLitePCLRaw のみを持ち、SqlClient / EF Core 系を持たない</summary>
    [Fact(
        DisplayName = "QuickER.Runtime.Sqlite は Microsoft.Data.Sqlite + SQLitePCLRaw のみ（SqlClient / EF Core なし）"
    )]
    public void Sqlite_HasSqliteOnly()
    {
        var packages = PackageReferences(
            "src/QuickER.Runtime.Sqlite/QuickER.Runtime.Sqlite.csproj"
        );

        packages
            .Should()
            .BeEquivalentTo(
                new[] { "Microsoft.Data.Sqlite", "SQLitePCLRaw.bundle_e_sqlite3" },
                "Sqlite パッケージの NuGet 依存は ADO（Microsoft.Data.Sqlite）＋既知脆弱性対策の SQLitePCLRaw ピンだけに保つ"
                    + "（SqlClient / EF Core / DI 系が混ざってはならない。DI 登録拡張はスキーマ依存物として生成側に出力される）"
            );

        // 依存はコアへの ProjectReference のみ（他方言プロジェクトを参照しない）
        var projects = ProjectReferences(
            "src/QuickER.Runtime.Sqlite/QuickER.Runtime.Sqlite.csproj"
        );
        projects.Should().ContainSingle().Which.Should().EndWith("QuickER.Runtime.csproj");
    }

    /// <summary>指定 csproj の PackageReference Include 集合を読む</summary>
    private static IReadOnlyList<string> PackageReferences(string repoRelativePath) =>
        ReadIncludes(repoRelativePath, "PackageReference");

    /// <summary>指定 csproj の ProjectReference Include（相対パス末尾）集合を読む</summary>
    private static IReadOnlyList<string> ProjectReferences(string repoRelativePath) =>
        ReadIncludes(repoRelativePath, "ProjectReference")
            .Select(p => p.Replace('\\', '/').TrimEnd('/'))
            .ToList();

    /// <summary>csproj を XML として解析し、指定要素の Include 属性値を集める（名前空間非依存）</summary>
    private static IReadOnlyList<string> ReadIncludes(string repoRelativePath, string elementName)
    {
        var path = ResolveRepoRelativePath(repoRelativePath);
        var doc = XDocument.Load(path);

        return doc.Descendants()
            .Where(e => e.Name.LocalName == elementName)
            .Select(e => (string?)e.Attribute("Include"))
            .Where(v => !string.IsNullOrEmpty(v))
            .Select(v => v!)
            .ToList();
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
