using System.IO;
using AwesomeAssertions;
using QuickER.AI.Mock;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Sqlite;
using QuickER.SqlServer;

namespace QuickER.Tests.AI.Mock;

/// <summary>
/// <see cref="MockProjectScaffoldService"/> の決定的スキャフォールド（Generated/ 生成コード・csproj・
/// README・design/mock/ 同梱）と、図の方言に応じたQuickER 版 Repository 出力を検証するテストクラス。
/// </summary>
public class MockProjectScaffoldServiceTests
{
    private const string ScreenHtml =
        "<!DOCTYPE html><html lang=\"ja\"><head><link rel=\"stylesheet\" href=\"style.css\"></head>"
        + "<body><h1>顧客一覧</h1></body></html>";

    private static DatabaseProviderRegistry BuildRegistry() =>
        new([new SqlServerProvider(), new SqliteProvider()]);

    /// <summary>mock.json＋画面 2 枚＋共有 style.css を持つモックフォルダを作って返す（同梱元）</summary>
    private static string SeedMockFolder()
    {
        var folder = NewTempFolder();
        var store = MockFolderStore.CreateNew(folder, "AcmeMock", "# schema");
        store.SaveStylesheet("body { font-family: sans-serif; }", "初版");
        store.SaveScreen(
            "CustomerList.html",
            "顧客一覧",
            "顧客の一覧",
            ScreenHtml,
            Array.Empty<MockTransition>(),
            "初版"
        );
        store.SaveScreen(
            "CustomerEdit.html",
            "顧客編集",
            "顧客の編集",
            ScreenHtml,
            Array.Empty<MockTransition>(),
            "初版"
        );

        return folder;
    }

    /// <summary>単一 PK を持つ顧客テーブル 1 つの図を、指定方言で作る</summary>
    private static ErDiagram BuildDiagram(string targetDbms) =>
        new()
        {
            TargetDbms = targetDbms,
            Entities =
            [
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "customers",
                    Columns =
                    [
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "customer_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "name",
                            DataType = "nvarchar(100)",
                            IsNullable = false,
                        },
                    ],
                },
            ],
        };

    private static string NewTempFolder() =>
        Path.Combine(Path.GetTempPath(), "QuickERTests", Guid.NewGuid().ToString("N"));

    private static void Cleanup(string folder)
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>スキャフォールドが sln・Generated/・csproj・README・design/mock/ を VS 標準構成で出力することを検証する</summary>
    [Fact(DisplayName = "スキャフォールドは土台一式を VS 標準構成で書き出す")]
    public void Scaffold_WritesFullSkeleton()
    {
        var folder = NewTempFolder();
        var mockFolder = SeedMockFolder();
        var service = new MockProjectScaffoldService(BuildRegistry());

        try
        {
            var result = service.Scaffold(
                BuildDiagram(SqlServerProvider.ProviderName),
                folder,
                "AcmeMock",
                mockFolder,
                MockProjectTargetProfile.Wpf
            );

            // VS 標準構成: sln は出力フォルダ直下、プロジェクト一式はプロジェクトフォルダ配下
            var projectDirectory = Path.Combine(folder, "AcmeMock");
            result.ProjectDirectory.Should().Be(projectDirectory);
            result.SolutionFilePath.Should().Be(Path.Combine(folder, "AcmeMock.sln"));
            File.Exists(result.SolutionFilePath).Should().BeTrue();

            // csproj・README・design・Generated はプロジェクトフォルダ配下にある
            result.ProjectFilePath.Should().StartWith(projectDirectory);
            result.ReadmePath.Should().StartWith(projectDirectory);
            result.DesignFolderPath.Should().StartWith(projectDirectory);
            result.GeneratedDirectory.Should().StartWith(projectDirectory);

            // csproj は WPF・net10.0-windows・MVVM 依存を持つ
            File.Exists(result.ProjectFilePath).Should().BeTrue();
            var csproj = File.ReadAllText(result.ProjectFilePath);
            csproj.Should().Contain("<UseWPF>true</UseWPF>");
            csproj.Should().Contain("net10.0-windows");
            csproj.Should().Contain("CommunityToolkit.Mvvm");
            csproj.Should().Contain("Microsoft.Extensions.DependencyInjection");

            // README はデータ層読み取り専用・InMemory DI 登録・実 DB 切替と、design/mock/ の複数画面前提を案内する
            File.Exists(result.ReadmePath).Should().BeTrue();
            var readme = File.ReadAllText(result.ReadmePath);
            readme.Should().Contain("Generated/");
            readme.Should().Contain("AddGeneratedInMemoryRepositories");
            readme.Should().Contain("I{Entity}Repository");
            readme.Should().Contain("design/mock/mock.json");
            readme.Should().Contain("style.css");

            // design/mock/ にモックフォルダの内容（mock.json・複数 HTML・style.css）がフラットに同梱される
            Directory.Exists(result.DesignFolderPath).Should().BeTrue();
            File.Exists(Path.Combine(result.DesignFolderPath, MockManifest.ManifestFileName))
                .Should()
                .BeTrue();
            File.Exists(Path.Combine(result.DesignFolderPath, MockManifest.StylesheetFileName))
                .Should()
                .BeTrue();
            File.Exists(Path.Combine(result.DesignFolderPath, "CustomerList.html"))
                .Should()
                .BeTrue();
            File.Exists(Path.Combine(result.DesignFolderPath, "CustomerEdit.html"))
                .Should()
                .BeTrue();
            File.ReadAllText(Path.Combine(result.DesignFolderPath, "CustomerList.html"))
                .Should()
                .Be(ScreenHtml);

            // Generated/ 配下にデータ層コードが分割出力される
            Directory.Exists(result.GeneratedDirectory).Should().BeTrue();
            var generatedFiles = Directory.GetFiles(
                result.GeneratedDirectory,
                "*.g.cs",
                SearchOption.AllDirectories
            );
            generatedFiles.Should().NotBeEmpty();

            // InMemory 実装（AddGeneratedInMemoryRepositories）が生成物のどこかに含まれる
            var allGenerated = string.Concat(generatedFiles.Select(File.ReadAllText));
            allGenerated.Should().Contain("AddGeneratedInMemoryRepositories");
            allGenerated.Should().Contain("CustomerEntity");
        }
        finally
        {
            Cleanup(folder);
            Cleanup(mockFolder);
        }
    }

    /// <summary>不正なプロジェクト名（トラバーサル・絶対パス・パス区切り・不正文字）は ArgumentException になることを検証する</summary>
    /// <remarks>出力フォルダ外への書き込み（<c>Path.Combine(outputDirectory, projectName)</c> 等）を防ぐための検証。</remarks>
    [Theory(DisplayName = "不正なプロジェクト名は ArgumentException")]
    [InlineData("..\\outside")]
    [InlineData("../outside")]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("C:\\Windows")]
    [InlineData("sub/dir")]
    [InlineData("sub\\dir")]
    [InlineData("bad:name")]
    [InlineData("bad*name")]
    public void Scaffold_InvalidProjectName_Throws(string projectName)
    {
        var folder = NewTempFolder();
        var service = new MockProjectScaffoldService(BuildRegistry());

        try
        {
            var act = () =>
                service.Scaffold(
                    BuildDiagram(SqlServerProvider.ProviderName),
                    folder,
                    projectName,
                    // 検証は Scaffold の入口で完結する（本テストではモックフォルダの実在は不要）
                    "unused-mock-folder",
                    MockProjectTargetProfile.Wpf
                );

            act.Should().Throw<ArgumentException>();

            // 出力フォルダ自体も作成されない（検証は副作用の前で完結する）
            Directory.Exists(folder).Should().BeFalse();
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary><see cref="MockProjectScaffoldService.IsValidProjectName"/> が妥当な名前を許可することを検証する</summary>
    [Theory(DisplayName = "妥当なプロジェクト名は IsValidProjectName で許可される")]
    [InlineData("AcmeMock")]
    [InlineData("Acme.Mock")]
    [InlineData("Acme_Mock2")]
    [InlineData("MockApp")]
    [InlineData("Foo")]
    public void IsValidProjectName_AcceptsValidNames(string projectName) =>
        MockProjectScaffoldService.IsValidProjectName(projectName).Should().BeTrue();

    /// <summary><see cref="MockProjectScaffoldService.IsValidProjectName"/> が不正な名前・空文字・null を拒否することを検証する</summary>
    [Theory(DisplayName = "不正なプロジェクト名は IsValidProjectName で拒否される")]
    [InlineData("..\\outside")]
    [InlineData("../outside")]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("C:\\Windows")]
    [InlineData("sub/dir")]
    [InlineData("sub\\dir")]
    [InlineData("bad:name")]
    // 末尾の空白・ピリオドは Windows のフォルダ作成で切り捨てられ、名前不一致・書き込み失敗を招く
    [InlineData("Foo ")]
    [InlineData(" Foo")]
    [InlineData("Bar.")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void IsValidProjectName_RejectsInvalidNames(string? projectName) =>
        MockProjectScaffoldService.IsValidProjectName(projectName).Should().BeFalse();

    /// <summary>SQL Server 方言の図ではQuickER の SQL Server Repository（と SqlClient 依存）が出力されることを検証する</summary>
    [Fact(DisplayName = "SQL Server 方言ではQuickER 版 Repository と SqlClient 依存を出す")]
    public void Scaffold_SqlServer_EmitsRepositoryAndAdoPackage()
    {
        var folder = NewTempFolder();
        var mockFolder = SeedMockFolder();
        var service = new MockProjectScaffoldService(BuildRegistry());

        try
        {
            var result = service.Scaffold(
                BuildDiagram(SqlServerProvider.ProviderName),
                folder,
                "AcmeMock",
                mockFolder,
                MockProjectTargetProfile.Wpf
            );

            result.RepositoryDialect.Should().Be("sqlserver");
            File.ReadAllText(result.ProjectFilePath).Should().Contain("Microsoft.Data.SqlClient");

            var allGenerated = string.Concat(
                Directory
                    .GetFiles(result.GeneratedDirectory, "*.g.cs", SearchOption.AllDirectories)
                    .Select(File.ReadAllText)
            );
            // DI 登録はエンジン別（AddGenerated{方言}Repositories）で統一。sqlserver 生成では方言別名が出る
            allGenerated.Should().Contain("AddGeneratedSqlServerRepositories");
        }
        finally
        {
            Cleanup(folder);
            Cleanup(mockFolder);
        }
    }

    /// <summary>SQLite 方言の図ではQuickER の SQLite Repository（と Sqlite 依存）が出力されることを検証する</summary>
    [Fact(DisplayName = "SQLite 方言ではQuickER 版 Repository と Sqlite 依存を出す")]
    public void Scaffold_Sqlite_EmitsRepositoryAndAdoPackage()
    {
        var folder = NewTempFolder();
        var mockFolder = SeedMockFolder();
        var service = new MockProjectScaffoldService(BuildRegistry());

        try
        {
            var result = service.Scaffold(
                BuildDiagram(SqliteProvider.ProviderName),
                folder,
                "AcmeMock",
                mockFolder,
                MockProjectTargetProfile.Wpf
            );

            result.RepositoryDialect.Should().Be("sqlite");
            File.ReadAllText(result.ProjectFilePath).Should().Contain("Microsoft.Data.Sqlite");
        }
        finally
        {
            Cleanup(folder);
            Cleanup(mockFolder);
        }
    }

    /// <summary>非対応方言（PostgreSQL 等）の図ではQuickER 版 Repository を出さず、ADO 依存も含めないことを検証する</summary>
    [Fact(DisplayName = "非対応方言ではQuickER 版 Repository を出さない")]
    public void Scaffold_UnsupportedDialect_OmitsRepository()
    {
        var folder = NewTempFolder();
        var mockFolder = SeedMockFolder();
        var service = new MockProjectScaffoldService(BuildRegistry());

        try
        {
            var result = service.Scaffold(
                BuildDiagram("postgresql"),
                folder,
                "AcmeMock",
                mockFolder,
                MockProjectTargetProfile.Wpf
            );

            result.RepositoryDialect.Should().BeNull();
            var csproj = File.ReadAllText(result.ProjectFilePath);
            csproj.Should().NotContain("Microsoft.Data.SqlClient");
            csproj.Should().NotContain("Microsoft.Data.Sqlite");

            // それでも Entity/EditModel/Mapper/InMemory は出る（InMemory は方言非依存）
            var allGenerated = string.Concat(
                Directory
                    .GetFiles(result.GeneratedDirectory, "*.g.cs", SearchOption.AllDirectories)
                    .Select(File.ReadAllText)
            );
            allGenerated.Should().Contain("AddGeneratedInMemoryRepositories");
        }
        finally
        {
            Cleanup(folder);
            Cleanup(mockFolder);
        }
    }

    /// <summary>Blazor プロファイルでは Blazor Web App の csproj（Microsoft.NET.Sdk.Web・net10.0）と規約 README を出すことを検証する</summary>
    [Fact(DisplayName = "Blazor は Web App csproj と Blazor 規約 README を出す")]
    public void Scaffold_Blazor_WritesBlazorCsprojAndReadme()
    {
        var folder = NewTempFolder();
        var mockFolder = SeedMockFolder();
        var service = new MockProjectScaffoldService(BuildRegistry());

        try
        {
            var result = service.Scaffold(
                BuildDiagram(SqliteProvider.ProviderName),
                folder,
                "AcmeMock",
                mockFolder,
                MockProjectTargetProfile.Blazor
            );

            // csproj は Blazor Web App（Microsoft.NET.Sdk.Web・net10.0）で、WPF 固有の依存は持たない
            var csproj = File.ReadAllText(result.ProjectFilePath);
            csproj.Should().Contain("Microsoft.NET.Sdk.Web");
            csproj.Should().Contain("<TargetFramework>net10.0</TargetFramework>");
            csproj.Should().NotContain("UseWPF");
            csproj.Should().NotContain("CommunityToolkit.Mvvm");
            csproj.Should().NotContain("Microsoft.Extensions.DependencyInjection");
            // sqlite 方言なので ADO パッケージは入る
            csproj.Should().Contain("Microsoft.Data.Sqlite");

            // README は Blazor の実装規約（@page／InteractiveServer／wwwroot/style.css／dotnet run）を案内する
            var readme = File.ReadAllText(result.ReadmePath);
            readme.Should().Contain("Blazor Web App");
            readme.Should().Contain("@page");
            readme.Should().Contain("InteractiveServer");
            readme.Should().Contain("wwwroot/style.css");
            readme.Should().Contain("dotnet run");
            readme.Should().Contain("AddGeneratedInMemoryRepositories");
        }
        finally
        {
            Cleanup(folder);
            Cleanup(mockFolder);
        }
    }

    /// <summary>Blazor で非対応方言の場合、ADO パッケージも空の ItemGroup 要素も出さないことを検証する</summary>
    [Fact(DisplayName = "Blazor・非対応方言は ItemGroup ごと省略する")]
    public void Scaffold_Blazor_UnsupportedDialect_OmitsItemGroup()
    {
        var folder = NewTempFolder();
        var mockFolder = SeedMockFolder();
        var service = new MockProjectScaffoldService(BuildRegistry());

        try
        {
            var result = service.Scaffold(
                BuildDiagram("postgresql"),
                folder,
                "AcmeMock",
                mockFolder,
                MockProjectTargetProfile.Blazor
            );

            result.RepositoryDialect.Should().BeNull();
            var csproj = File.ReadAllText(result.ProjectFilePath);
            csproj.Should().Contain("Microsoft.NET.Sdk.Web");
            csproj.Should().NotContain("Microsoft.Data.SqlClient");
            csproj.Should().NotContain("Microsoft.Data.Sqlite");
            // 方言 ADO が無いので ItemGroup 自体を出さない（空要素を作らない）
            csproj.Should().NotContain("<ItemGroup>");
        }
        finally
        {
            Cleanup(folder);
            Cleanup(mockFolder);
        }
    }

    /// <summary>Blazor 方言の図でもQuickER 版 Repository と ADO 依存の分岐は WPF と同様に効くことを検証する</summary>
    [Fact(DisplayName = "Blazor・SQL Server 方言でもQuickER 版 Repository を出す")]
    public void Scaffold_Blazor_SqlServer_EmitsRepositoryAndAdoPackage()
    {
        var folder = NewTempFolder();
        var mockFolder = SeedMockFolder();
        var service = new MockProjectScaffoldService(BuildRegistry());

        try
        {
            var result = service.Scaffold(
                BuildDiagram(SqlServerProvider.ProviderName),
                folder,
                "AcmeMock",
                mockFolder,
                MockProjectTargetProfile.Blazor
            );

            result.RepositoryDialect.Should().Be("sqlserver");
            File.ReadAllText(result.ProjectFilePath).Should().Contain("Microsoft.Data.SqlClient");

            var allGenerated = string.Concat(
                Directory
                    .GetFiles(result.GeneratedDirectory, "*.g.cs", SearchOption.AllDirectories)
                    .Select(File.ReadAllText)
            );
            allGenerated.Should().Contain("AddGeneratedSqlServerRepositories");
        }
        finally
        {
            Cleanup(folder);
            Cleanup(mockFolder);
        }
    }

    /// <summary>Generated/（データ層コード）はターゲットに依らず同一（WPF と Blazor で一致）であることを検証する</summary>
    [Fact(DisplayName = "Generated/ は WPF と Blazor で同一")]
    public void Scaffold_GeneratedIsIdenticalAcrossTargets()
    {
        var wpfFolder = NewTempFolder();
        var blazorFolder = NewTempFolder();
        var mockFolder = SeedMockFolder();
        var service = new MockProjectScaffoldService(BuildRegistry());

        try
        {
            var wpf = service.Scaffold(
                BuildDiagram(SqliteProvider.ProviderName),
                wpfFolder,
                "AcmeMock",
                mockFolder,
                MockProjectTargetProfile.Wpf
            );
            var blazor = service.Scaffold(
                BuildDiagram(SqliteProvider.ProviderName),
                blazorFolder,
                "AcmeMock",
                mockFolder,
                MockProjectTargetProfile.Blazor
            );

            // 相対パス→内容のマップを両ターゲットで作り、完全一致を検証する
            var wpfGenerated = ReadGeneratedRelative(wpf.GeneratedDirectory);
            var blazorGenerated = ReadGeneratedRelative(blazor.GeneratedDirectory);

            blazorGenerated.Should().BeEquivalentTo(wpfGenerated);
        }
        finally
        {
            Cleanup(wpfFolder);
            Cleanup(blazorFolder);
            Cleanup(mockFolder);
        }
    }

    /// <summary>Generated/ 配下の *.g.cs を「相対パス→内容」の辞書として読み出す（ターゲット間の同一性比較用）</summary>
    private static Dictionary<string, string> ReadGeneratedRelative(string generatedDirectory) =>
        Directory
            .GetFiles(generatedDirectory, "*.g.cs", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(generatedDirectory, path), File.ReadAllText);

    /// <summary>生成した .sln の構文（Format Version・Project/EndProject・構成セクション・プロジェクト参照）を検証する</summary>
    [Fact(DisplayName = "sln は VS 標準の構文とプロジェクト参照を含む")]
    public void Scaffold_SolutionHasValidSyntax()
    {
        var folder = NewTempFolder();
        var mockFolder = SeedMockFolder();
        var service = new MockProjectScaffoldService(BuildRegistry());

        try
        {
            var result = service.Scaffold(
                BuildDiagram(SqlServerProvider.ProviderName),
                folder,
                "AcmeMock",
                mockFolder,
                MockProjectTargetProfile.Wpf
            );

            var sln = File.ReadAllText(result.SolutionFilePath);

            // ヘッダ（Format Version 12.00）
            sln.Should().Contain("Microsoft Visual Studio Solution File, Format Version 12.00");

            // C# プロジェクト種別 GUID と、プロジェクトフォルダ配下の csproj を参照する Project 行
            sln.Should()
                .Contain("Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"AcmeMock\"");
            sln.Should().Contain(@"AcmeMock\AcmeMock.csproj");
            sln.Should().Contain("EndProject");

            // 構成セクション（Debug/Release × Any CPU）
            sln.Should().Contain("GlobalSection(SolutionConfigurationPlatforms) = preSolution");
            sln.Should().Contain("Debug|Any CPU = Debug|Any CPU");
            sln.Should().Contain("Release|Any CPU = Release|Any CPU");
            sln.Should().Contain("GlobalSection(ProjectConfigurationPlatforms) = postSolution");
            sln.Should().Contain(".Debug|Any CPU.ActiveCfg = Debug|Any CPU");
            sln.Should().Contain(".Release|Any CPU.Build.0 = Release|Any CPU");
            sln.Should().Contain("EndGlobal");

            // 改行は CRLF（VS 標準）
            sln.Should().Contain("\r\n");
        }
        finally
        {
            Cleanup(folder);
            Cleanup(mockFolder);
        }
    }

    /// <summary>プロジェクト GUID が名前から決定的に導出される（同名なら同 GUID・別名なら別 GUID）ことを検証する</summary>
    [Fact(DisplayName = "sln のプロジェクト GUID は名前から決定的")]
    public void Scaffold_SolutionProjectGuidIsDeterministic()
    {
        var folder1 = NewTempFolder();
        var folder2 = NewTempFolder();
        var folder3 = NewTempFolder();
        var mockFolder = SeedMockFolder();
        var service = new MockProjectScaffoldService(BuildRegistry());

        try
        {
            var same1 = service.Scaffold(
                BuildDiagram(SqlServerProvider.ProviderName),
                folder1,
                "AcmeMock",
                mockFolder,
                MockProjectTargetProfile.Wpf
            );
            var same2 = service.Scaffold(
                BuildDiagram(SqlServerProvider.ProviderName),
                folder2,
                "AcmeMock",
                mockFolder,
                MockProjectTargetProfile.Wpf
            );
            var other = service.Scaffold(
                BuildDiagram(SqlServerProvider.ProviderName),
                folder3,
                "OtherMock",
                mockFolder,
                MockProjectTargetProfile.Wpf
            );

            var guid1 = ExtractProjectGuid(File.ReadAllText(same1.SolutionFilePath));
            var guid2 = ExtractProjectGuid(File.ReadAllText(same2.SolutionFilePath));
            var guidOther = ExtractProjectGuid(File.ReadAllText(other.SolutionFilePath));

            // 同名なら同一 GUID（決定的）
            guid1.Should().Be(guid2);
            // 別名なら別 GUID
            guid1.Should().NotBe(guidOther);
        }
        finally
        {
            Cleanup(folder1);
            Cleanup(folder2);
            Cleanup(folder3);
            Cleanup(mockFolder);
        }
    }

    /// <summary>.sln テキストから Project 行のプロジェクト GUID（末尾の "{...}"）を取り出す</summary>
    private static string ExtractProjectGuid(string sln)
    {
        var projectLine = sln.Split('\n')
            .First(line => line.StartsWith("Project(", StringComparison.Ordinal));
        // Project("{型GUID}") = "名前", "パス", "{プロジェクトGUID}" の末尾 GUID を取る
        var lastBrace = projectLine.LastIndexOf('{');
        return projectLine[lastBrace..].Trim().TrimEnd('"');
    }
}
