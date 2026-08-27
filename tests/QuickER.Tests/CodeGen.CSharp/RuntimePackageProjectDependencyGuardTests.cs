using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AwesomeAssertions;
using Xunit;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// ランタイムパッケージ 7 プロジェクト（<c>QuickER.Runtime</c> / <c>.SqlServer</c> / <c>.Sqlite</c> /
/// <c>.EntityFrameworkCore</c> / <c>.InMemory</c> / <c>.AspNetCore</c> / <c>.Sync</c>）の csproj が宣言する依存集合
/// （PackageReference / ProjectReference / FrameworkReference）を検証し、パッケージ境界での依存排他を守る。
/// </summary>
/// <remarks>
/// <para>
/// 生成物レベルの依存排他ガード（<c>CSharpCodeGenerationServiceTests</c> の「EF Core 単独出力に SqlClient なし」等・
/// <c>MultiTargetRepositoryGenerationTests</c> の方言別排他）と対をなす、csproj レベルの排他ガード。
/// これにより、公開される .nupkg の nuspec 依存が意図どおり（Core=依存ゼロ・方言相互排他）であることを構造上保証する。
/// </para>
/// <para>
/// DI 登録拡張（<c>AddGenerated*Repositories</c>）はエンティティ別登録を含むスキーマ依存物として常に生成側へ
/// 出力される（パッケージ書き出しでは抑止）ため、パッケージは <c>Microsoft.Extensions.DependencyInjection</c> 系にも
/// 依存しない。依存集合は完全一致で検証する。
/// </para>
/// <para>
/// <c>FrameworkReference</c>（共有フレームワーク参照）を持ってよいのは <c>.AspNetCore</c> だけで、他 5 つは
/// 持たない（逆表明も固定する）。共有フレームワーク参照は「そのパッケージを参照する側のプロジェクト種別を縛る」
/// ため、うっかり他パッケージへ足すと非 Web プロジェクトで解決できない依存が広がる。
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

        FrameworkReferences("src/QuickER.Runtime/QuickER.Runtime.csproj")
            .Should()
            .BeEmpty(NoFrameworkReferenceReason);
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

        FrameworkReferences("src/QuickER.Runtime.SqlServer/QuickER.Runtime.SqlServer.csproj")
            .Should()
            .BeEmpty(NoFrameworkReferenceReason);
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

        FrameworkReferences("src/QuickER.Runtime.Sqlite/QuickER.Runtime.Sqlite.csproj")
            .Should()
            .BeEmpty(NoFrameworkReferenceReason);
    }

    /// <summary>EntityFrameworkCore パッケージは EF Core（Relational）のみを持ち、ADO / DI 系を持たない</summary>
    [Fact(
        DisplayName = "QuickER.Runtime.EntityFrameworkCore は Microsoft.EntityFrameworkCore.Relational のみ（ADO / DI なし）"
    )]
    public void EntityFrameworkCore_HasEfCoreRelationalOnly()
    {
        var packages = PackageReferences(
            "src/QuickER.Runtime.EntityFrameworkCore/QuickER.Runtime.EntityFrameworkCore.csproj"
        );

        packages
            .Should()
            .BeEquivalentTo(
                new[] { "Microsoft.EntityFrameworkCore.Relational" },
                "EF Core パッケージの NuGet 依存は EF Core（Relational・本体は推移取得）だけに保つ"
                    + "（ADO（SqlClient / Sqlite）／DI 系が混ざってはならない。DI 登録拡張は具象 DbContext を参照するスキーマ依存物として生成側に出力される）"
            );

        // 依存はコアへの ProjectReference のみ（方言プロジェクトを参照しない）
        var projects = ProjectReferences(
            "src/QuickER.Runtime.EntityFrameworkCore/QuickER.Runtime.EntityFrameworkCore.csproj"
        );
        projects.Should().ContainSingle().Which.Should().EndWith("QuickER.Runtime.csproj");

        FrameworkReferences(
                "src/QuickER.Runtime.EntityFrameworkCore/QuickER.Runtime.EntityFrameworkCore.csproj"
            )
            .Should()
            .BeEmpty(NoFrameworkReferenceReason);
    }

    /// <summary>InMemory パッケージは PackageReference を 1 つも持たない（BCL のみ・ADO / EF Core / DI なし）</summary>
    [Fact(
        DisplayName = "QuickER.Runtime.InMemory は PackageReference ゼロ（BCL のみ・Core への ProjectReference だけ）"
    )]
    public void InMemory_HasNoPackageReferences()
    {
        var packages = PackageReferences(
            "src/QuickER.Runtime.InMemory/QuickER.Runtime.InMemory.csproj"
        );

        packages
            .Should()
            .BeEmpty(
                "インメモリエンジンは DB へ触らないため BCL のみに依存し、いかなる NuGet 依存も持ってはならない"
                    + "（ADO（SqlClient / Sqlite）／EF Core／DI 系が混ざってはならない。DI 登録拡張はスキーマ依存物として生成側に出力される）"
            );

        // 依存はコアへの ProjectReference のみ（方言プロジェクトを参照しない）
        var projects = ProjectReferences(
            "src/QuickER.Runtime.InMemory/QuickER.Runtime.InMemory.csproj"
        );
        projects.Should().ContainSingle().Which.Should().EndWith("QuickER.Runtime.csproj");

        FrameworkReferences("src/QuickER.Runtime.InMemory/QuickER.Runtime.InMemory.csproj")
            .Should()
            .BeEmpty(NoFrameworkReferenceReason);
    }

    /// <summary>
    /// AspNetCore パッケージは PackageReference を 1 つも持たず、共有フレームワーク参照は
    /// <c>Microsoft.AspNetCore.App</c> 1 つだけを持つ（ADO / EF Core / DI の NuGet 依存なし）。
    /// </summary>
    [Fact(
        DisplayName = "QuickER.Runtime.AspNetCore は PackageReference ゼロ＋FrameworkReference は Microsoft.AspNetCore.App のみ"
    )]
    public void AspNetCore_HasAspNetCoreFrameworkReferenceOnly()
    {
        var packages = PackageReferences(
            "src/QuickER.Runtime.AspNetCore/QuickER.Runtime.AspNetCore.csproj"
        );

        packages
            .Should()
            .BeEmpty(
                "サーバー固定エンジンが必要とするもの（Minimal API・DI・ロギング）は ASP.NET Core の共有フレームワークが"
                    + "推移的に提供するため、NuGet 依存は 1 つも持ってはならない"
                    + "（ADO（SqlClient / Sqlite）／EF Core が混ざってはならない。per-entity のエンドポイント・DI 登録は"
                    + "スキーマ依存物として生成側に出力される）"
            );

        // 共有フレームワーク参照は ASP.NET Core だけ（Windows Desktop 等が混ざらない）
        FrameworkReferences("src/QuickER.Runtime.AspNetCore/QuickER.Runtime.AspNetCore.csproj")
            .Should()
            .BeEquivalentTo(
                new[] { "Microsoft.AspNetCore.App" },
                "サーバー固定部は Minimal API（RouteGroupBuilder / HttpContext）を使うため ASP.NET Core の"
                    + "共有フレームワークだけを参照する"
            );

        // 依存はコアへの ProjectReference のみ（方言・EF Core プロジェクトを参照しない）
        var projects = ProjectReferences(
            "src/QuickER.Runtime.AspNetCore/QuickER.Runtime.AspNetCore.csproj"
        );
        projects.Should().ContainSingle().Which.Should().EndWith("QuickER.Runtime.csproj");
    }

    /// <summary>Sync パッケージは PackageReference を 1 つも持たない（BCL のみ・ADO / EF Core / DI なし）</summary>
    [Fact(
        DisplayName = "QuickER.Runtime.Sync は PackageReference ゼロ（BCL のみ・Core への ProjectReference だけ）"
    )]
    public void Sync_HasNoPackageReferences()
    {
        var packages = PackageReferences("src/QuickER.Runtime.Sync/QuickER.Runtime.Sync.csproj");

        packages
            .Should()
            .BeEmpty(
                "同期エンジンは中立契約（IRepository / ISqlExecutor / ConcurrencyMode）だけを使うため BCL のみに依存し、"
                    + "いかなる NuGet 依存も持ってはならない（ADO（SqlClient / Sqlite）／EF Core／DI 系が混ざってはならない。"
                    + "DI 登録拡張 AddGeneratedSyncSupport と per-entity の記述子・デコレータ・直結差分ソースは"
                    + "スキーマ依存物として生成側に出力される）"
            );

        // 依存はコアへの ProjectReference のみ（方言プロジェクトを参照しない）
        var projects = ProjectReferences("src/QuickER.Runtime.Sync/QuickER.Runtime.Sync.csproj");
        projects.Should().ContainSingle().Which.Should().EndWith("QuickER.Runtime.csproj");

        FrameworkReferences("src/QuickER.Runtime.Sync/QuickER.Runtime.Sync.csproj")
            .Should()
            .BeEmpty(NoFrameworkReferenceReason);
    }

    // ── README の記述と csproj の照合 ──────────────────────────────────────────────
    //
    // 上のガードは csproj が宣言する依存だけを見ており、パッケージ README がその依存を
    // どう説明しているかは見ていない。実際、ProjectReference 由来の QuickER.Runtime 依存を
    // README が「NuGet 依存ゼロ」と書いていた期間があり、上のガードは緑のままだった。
    // 公開後の README は差し替えられない（訂正には新しいバージョンの push が要る）ため、
    // 記述と実体のずれはここで落とす。

    /// <summary>README の依存記述が csproj の宣言と一致することを検証する（7 ランタイムパッケージ）</summary>
    /// <remarks>
    /// 期待値は「PackageReference ＋ packable な ProjectReference の PackageId ＋ FrameworkReference」で、
    /// これは pack 時に nuspec へ出る依存の組み立て規則をそのまま写したもの。ProjectReference は
    /// PrivateAssets 等で伝播を止めない限りパッケージ依存として現れるため、宣言だけを見る上のガードでは
    /// 捉えられない差が生じる。
    /// </remarks>
    [Theory(DisplayName = "パッケージ README が主張する依存集合は csproj の宣言と一致する")]
    [InlineData("QuickER.Runtime")]
    [InlineData("QuickER.Runtime.SqlServer")]
    [InlineData("QuickER.Runtime.Sqlite")]
    [InlineData("QuickER.Runtime.EntityFrameworkCore")]
    [InlineData("QuickER.Runtime.InMemory")]
    [InlineData("QuickER.Runtime.AspNetCore")]
    [InlineData("QuickER.Runtime.Sync")]
    public void ReadmeDependencyClaim_MatchesProjectFile(string packageId)
    {
        var sentence = FindDependencySentence($"src/{packageId}/README.md");

        sentence
            .Should()
            .NotBeNull(
                "{0} の README 冒頭に依存を述べる文（\"{1}\" または \"{2}\"）が見つからない。"
                    + "文言を変えたのなら、変更後の記述が実際の依存と一致することを確かめたうえで"
                    + "本テストのアンカー句を更新すること（見つからないときに検証を飛ばすと、"
                    + "文を書き換えた瞬間にこのガードが静かに無効化される）",
                packageId,
                DependsOnlyOnPhrase,
                NoDependencyPhrase
            );

        ClaimedDependencyIds(sentence!)
            .Should()
            .BeEquivalentTo(
                ExpectedDependencyIds($"src/{packageId}/{packageId}.csproj"),
                "{0} の README が主張する依存集合は、pack 時に nuspec へ出る依存"
                    + "（PackageReference ＋ packable な ProjectReference ＋ FrameworkReference）と一致していなければならない。"
                    + "依存を足し引きしたら README の当該文も直すこと",
                packageId
            );
    }

    /// <summary>README が表明する対象フレームワークが csproj の TargetFramework と一致することを検証する</summary>
    [Theory(
        DisplayName = "パッケージ README の対象フレームワーク表記は csproj の TargetFramework と一致する"
    )]
    [InlineData("QuickER.Runtime")]
    [InlineData("QuickER.Runtime.SqlServer")]
    [InlineData("QuickER.Runtime.Sqlite")]
    [InlineData("QuickER.Runtime.EntityFrameworkCore")]
    [InlineData("QuickER.Runtime.InMemory")]
    [InlineData("QuickER.Runtime.AspNetCore")]
    [InlineData("QuickER.Runtime.Sync")]
    [InlineData("QuickER.Cli")]
    public void ReadmeTargetFrameworkClaim_MatchesProjectFile(string packageId)
    {
        var moniker = TargetFramework($"src/{packageId}/{packageId}.csproj");
        var expected = FrameworkDisplayName(moniker);

        FirstParagraph($"src/{packageId}/README.md")
            .Should()
            .Contain(
                expected,
                "{0} の csproj は {1} を対象にしているため、README 冒頭も \"{2}\" と述べていなければならない"
                    + "（対象フレームワークは利用者が参照可否を判断する最重要事実で、"
                    + "公開後の README は差し替えられない）",
                packageId,
                moniker,
                expected
            );
    }

    /// <summary>dotnet tool の CLI パッケージは nuspec の依存がゼロのため、README も依存を主張してはならない</summary>
    /// <remarks>
    /// <c>PackAsTool</c> は <c>SuppressDependenciesWhenPacking</c> を強制し、参照は依存として現れず
    /// tools/ 配下へ同梱される。そのため「depends only on …」と書いた時点で事実と食い違う。
    /// 書き足されたら落ちるよう、無いことを表明しておく。
    /// </remarks>
    [Fact(
        DisplayName = "QuickER.Cli の README は依存を主張しない（dotnet tool は依存ゼロで同梱される）"
    )]
    public void CliReadme_MakesNoDependencyClaim()
    {
        BooleanProperty("src/QuickER.Cli/QuickER.Cli.csproj", "PackAsTool")
            .Should()
            .BeTrue("本テストは QuickER.Cli が dotnet tool としてパックされることを前提にしている");

        FindDependencySentence("src/QuickER.Cli/README.md")
            .Should()
            .BeNull(
                "dotnet tool は依存を nuspec へ出さず tools/ へ同梱するため、"
                    + "README が依存を主張すると事実と食い違う（同梱物を伝えたいなら "
                    + "THIRD-PARTY-NOTICES.md への導線で示すこと）"
            );
    }

    /// <summary>AspNetCore 以外のパッケージが FrameworkReference を持たないことの理由文（逆表明の共有文言）</summary>
    private const string NoFrameworkReferenceReason =
        "共有フレームワーク参照（FrameworkReference）を持ってよいのは QuickER.Runtime.AspNetCore だけで、"
        + "他パッケージが持つと参照側プロジェクトの種別（Web SDK 等）を不必要に縛ってしまう";

    /// <summary>指定 csproj の PackageReference Include 集合を読む</summary>
    private static IReadOnlyList<string> PackageReferences(string repoRelativePath) =>
        ReadIncludes(repoRelativePath, "PackageReference");

    /// <summary>指定 csproj の ProjectReference Include（相対パス末尾）集合を読む</summary>
    private static IReadOnlyList<string> ProjectReferences(string repoRelativePath) =>
        ReadIncludes(repoRelativePath, "ProjectReference")
            .Select(p => p.Replace('\\', '/').TrimEnd('/'))
            .ToList();

    /// <summary>指定 csproj の FrameworkReference Include（共有フレームワーク名）集合を読む</summary>
    private static IReadOnlyList<string> FrameworkReferences(string repoRelativePath) =>
        ReadIncludes(repoRelativePath, "FrameworkReference");

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

    /// <summary>依存を列挙する文のアンカー句（この句を含む文だけを照合対象にする）</summary>
    private const string DependsOnlyOnPhrase = "depends only on";

    /// <summary>依存ゼロを述べる文のアンカー句</summary>
    private const string NoDependencyPhrase = "has no NuGet package dependencies";

    /// <summary>README 冒頭段落から、依存を述べる文を 1 つ取り出す（見つからなければ null）</summary>
    /// <remarks>
    /// 段落全体ではなく文まで絞るのは、冒頭段落の他の文にも <c>IRepository</c> や <c>FOR JSON PATH</c> の
    /// ようなコードスパンが出てくるため。文を特定しないと、依存でないものまで依存として拾ってしまう。
    /// </remarks>
    private static string? FindDependencySentence(string readmeRepoRelativePath)
    {
        var sentences = Regex.Split(FirstParagraph(readmeRepoRelativePath), @"(?<=\.)\s+(?=[A-Z])");

        return sentences.FirstOrDefault(sentence =>
            sentence.Contains(DependsOnlyOnPhrase, StringComparison.Ordinal)
            || sentence.Contains(NoDependencyPhrase, StringComparison.Ordinal)
        );
    }

    /// <summary>README の H1 に続く最初の非空行（冒頭段落）を返す</summary>
    private static string FirstParagraph(string readmeRepoRelativePath)
    {
        var lines = File.ReadAllLines(ResolveRepoRelativePath(readmeRepoRelativePath));
        var paragraph = lines
            .SkipWhile(line => !line.StartsWith("# ", StringComparison.Ordinal))
            .Skip(1)
            .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line));

        return paragraph
            ?? throw new InvalidOperationException(
                $"{readmeRepoRelativePath} に H1 とそれに続く冒頭段落が見つかりませんでした。"
            );
    }

    /// <summary>依存を述べる文が名指ししているパッケージ ID（インラインコードスパン）を返す</summary>
    private static IReadOnlyList<string> ClaimedDependencyIds(string dependencySentence)
    {
        if (dependencySentence.Contains(NoDependencyPhrase, StringComparison.Ordinal))
        {
            return [];
        }

        return Regex
            .Matches(dependencySentence, "`([^`]+)`")
            .Select(match => match.Groups[1].Value)
            .ToList();
    }

    /// <summary>
    /// pack 時に nuspec へ出る依存の集合を csproj から組み立てる
    /// （PackageReference ＋ packable な ProjectReference の PackageId ＋ FrameworkReference）。
    /// </summary>
    private static IReadOnlyList<string> ExpectedDependencyIds(string csprojRepoRelativePath)
    {
        // dotnet tool（PackAsTool）は SuppressDependenciesWhenPacking が効き、依存を 1 つも宣言しない
        if (
            BooleanProperty(csprojRepoRelativePath, "PackAsTool")
            || BooleanProperty(csprojRepoRelativePath, "SuppressDependenciesWhenPacking")
        )
        {
            return [];
        }

        return
        [
            .. PackageReferences(csprojRepoRelativePath),
            .. PackableProjectReferenceIds(csprojRepoRelativePath),
            .. FrameworkReferences(csprojRepoRelativePath),
        ];
    }

    /// <summary>ProjectReference のうち、パッケージ依存として nuspec に出るものの PackageId を返す</summary>
    /// <remarks>
    /// PrivateAssets に all を指定した参照は依存を伝播しないため除外する。参照先が packable でない
    /// （IsPackable が false）場合も依存にはならない。
    /// </remarks>
    private static IReadOnlyList<string> PackableProjectReferenceIds(string csprojRepoRelativePath)
    {
        var path = ResolveRepoRelativePath(csprojRepoRelativePath);
        var directory = Path.GetDirectoryName(path)!;
        var ids = new List<string>();

        var references = XDocument
            .Load(path)
            .Descendants()
            .Where(e => e.Name.LocalName == "ProjectReference");

        foreach (var element in references)
        {
            var include = (string?)element.Attribute("Include");

            if (string.IsNullOrEmpty(include) || SuppressesAssets(element))
            {
                continue;
            }

            var referencedPath = Path.GetFullPath(
                Path.Combine(directory, include.Replace('\\', Path.DirectorySeparatorChar))
            );

            if (!File.Exists(referencedPath))
            {
                throw new FileNotFoundException(
                    $"{csprojRepoRelativePath} の ProjectReference の参照先 {include} が見つかりませんでした。"
                );
            }

            var referenced = XDocument.Load(referencedPath);
            var isPackable = PropertyValue(referenced, "IsPackable");

            if (!string.Equals(isPackable, "false", StringComparison.OrdinalIgnoreCase))
            {
                ids.Add(
                    PropertyValue(referenced, "PackageId")
                        ?? Path.GetFileNameWithoutExtension(referencedPath)
                );
            }
        }

        return ids;
    }

    /// <summary>PrivateAssets に all を指定（属性・子要素いずれか）して依存の伝播を止めているか</summary>
    private static bool SuppressesAssets(XElement projectReference)
    {
        var value =
            (string?)projectReference.Attribute("PrivateAssets")
            ?? projectReference
                .Elements()
                .FirstOrDefault(e => e.Name.LocalName == "PrivateAssets")
                ?.Value;

        return value is not null && value.Contains("all", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>csproj の TargetFramework（単一ターゲット前提）を読む</summary>
    private static string TargetFramework(string csprojRepoRelativePath)
    {
        var document = XDocument.Load(ResolveRepoRelativePath(csprojRepoRelativePath));

        return PropertyValue(document, "TargetFramework")
            ?? throw new InvalidOperationException(
                $"{csprojRepoRelativePath} に TargetFramework が見つかりませんでした"
                    + "（マルチターゲットへ変えたなら本テストの前提を見直すこと）。"
            );
    }

    /// <summary>TargetFramework モニカ（net10.0）を README の表記（.NET 10）へ変換する</summary>
    private static string FrameworkDisplayName(string moniker)
    {
        var match = Regex.Match(moniker, @"^net(?<major>\d+)\.(?<minor>\d+)$");

        if (!match.Success)
        {
            throw new InvalidOperationException(
                $"TargetFramework {moniker} を README の表記へ変換できませんでした。"
            );
        }

        var major = match.Groups["major"].Value;
        var minor = match.Groups["minor"].Value;

        return minor == "0" ? $".NET {major}" : $".NET {major}.{minor}";
    }

    /// <summary>csproj の bool プロパティを読む（未指定は false 扱い）</summary>
    private static bool BooleanProperty(string csprojRepoRelativePath, string propertyName)
    {
        var document = XDocument.Load(ResolveRepoRelativePath(csprojRepoRelativePath));

        return string.Equals(
            PropertyValue(document, propertyName),
            "true",
            StringComparison.OrdinalIgnoreCase
        );
    }

    /// <summary>プロジェクト XML から指定プロパティの値を読む（名前空間非依存・最初の 1 つ）</summary>
    private static string? PropertyValue(XDocument document, string propertyName) =>
        document.Descendants().FirstOrDefault(e => e.Name.LocalName == propertyName)?.Value.Trim();
}
