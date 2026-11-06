using System.IO;
using System.Xml.Linq;
using AwesomeAssertions;
using QuickER.AI.Mock;
using QuickER.Tests.Resources;

namespace QuickER.Tests.AI.Mock;

/// <summary>
/// モックプロジェクトの csproj が参照するパッケージの版が、リポジトリの <c>Directory.Packages.props</c>
/// （唯一の正本）と食い違わないことを固定するガードテスト。
/// </summary>
/// <remarks>
/// 版はビルド時に <c>QuickER.AI.Mock.csproj</c> がアセンブリメタデータへ写す。生成物だけが古い版
/// （脆弱性対策の前の版）のまま残っていた食い違いを、写しの漏れ・props 側の削除まで含めて検知する。
/// 埋め込んだ版と props の照合は照合の表（<c>ParityTests</c> の行 <c>mock-project-package-ids</c>・<c>mock-project-package-versions</c>）が見る。
/// ここに残すのは csproj の生成と未知のパッケージの例外（製品を実行する振る舞い）と、行が呼ぶ読み取り。
/// </remarks>
public class MockProjectPackageVersionsTests
{
    /// <summary>生成する csproj が参照し得るパッケージ（テスト側で独立に列挙する＝一覧からの削除も検知する）</summary>
    internal static readonly string[] ExpectedPackageIds =
    [
        "CommunityToolkit.Mvvm",
        "Microsoft.Extensions.DependencyInjection",
        "Microsoft.Data.SqlClient",
        "Microsoft.Data.Sqlite",
        "SQLitePCLRaw.bundle_e_sqlite3",
        "System.Security.Cryptography.Pkcs",
    ];

    /// <summary>Directory.Packages.props の PackageVersion（ID → 版）</summary>
    internal static Dictionary<string, string> ReadCentralPackageVersions()
    {
        var props = XDocument.Load(
            Path.Combine(NeutralResxFiles.FindRepositoryRoot(), "Directory.Packages.props")
        );

        return props
            .Descendants("PackageVersion")
            .ToDictionary(
                element => (string)element.Attribute("Include")!,
                element => (string)element.Attribute("Version")!,
                StringComparer.Ordinal
            );
    }

    /// <summary>写されていないパッケージを尋ねると、版なしの参照を書かずに英語の例外で止まることを検証する</summary>
    [Fact(DisplayName = "写されていないパッケージの版は英語の例外で止まる")]
    public void Get_UnknownPackage_ThrowsEnglish()
    {
        var get = () => MockProjectPackageVersions.Get("Not.A.Package");

        get.Should()
            .Throw<InvalidOperationException>()
            .Which.Message.Should()
            .Contain("Not.A.Package")
            .And.MatchRegex(@"^[\x20-\x7E]+$");
    }

    /// <summary>
    /// 生成する csproj（WPF / Blazor × 方言）の PackageReference がすべて正本の版で書かれ、SQLite では
    /// SQLite の版をそろえる SQLitePCLRaw のピンを、Blazor × SQL Server では System.Security.Cryptography.Pkcs のピンを
    /// 伴うことを検証する（WPF は共有フレームワークが Pkcs を刈り込むので書かない＝書くと NU1510）。
    /// </summary>
    [Theory(
        DisplayName = "生成 csproj の PackageReference は正本の版で書かれ、SQLite と Blazor × SQL Server はピンを伴う"
    )]
    [InlineData("wpf", "sqlite")]
    [InlineData("wpf", "sqlserver")]
    [InlineData("wpf", null)]
    [InlineData("blazor", "sqlite")]
    [InlineData("blazor", "sqlserver")]
    [InlineData("blazor", null)]
    public void GeneratedCsproj_UsesCentralVersions(string target, string? dialect)
    {
        var central = ReadCentralPackageVersions();
        var profile = MockProjectTargetProfile.Resolve(
            target == "wpf" ? MockProjectTarget.Wpf : MockProjectTarget.Blazor
        );

        var csproj = XDocument.Parse(profile.BuildCsproj("AcmeMock", dialect));
        var references = csproj
            .Descendants("PackageReference")
            .ToDictionary(
                element => (string)element.Attribute("Include")!,
                element => (string)element.Attribute("Version")!
            );

        foreach (var (id, version) in references)
        {
            central.Should().ContainKey(id);
            version.Should().Be(central[id], id);
        }

        references.ContainsKey("SQLitePCLRaw.bundle_e_sqlite3").Should().Be(dialect == "sqlite");
        references.ContainsKey("Microsoft.Data.Sqlite").Should().Be(dialect == "sqlite");
        references.ContainsKey("Microsoft.Data.SqlClient").Should().Be(dialect == "sqlserver");
        references
            .ContainsKey("System.Security.Cryptography.Pkcs")
            .Should()
            .Be(target == "blazor" && dialect == "sqlserver");
    }
}
