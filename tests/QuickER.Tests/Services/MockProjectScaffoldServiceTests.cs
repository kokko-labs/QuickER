using System.IO;
using FluentAssertions;
using QuickER.AI.Mock;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Sqlite;
using QuickER.SqlServer;

namespace QuickER.Tests.Services;

/// <summary>
/// <see cref="MockProjectScaffoldService"/> の決定的スキャフォールド（Generated/ 生成コード・csproj・
/// README・design/mock.html の書き出し）と、図の方言に応じたRepository (QuickER) 出力を検証するテストクラス。
/// </summary>
public class MockProjectScaffoldServiceTests
{
    private const string DesignHtml =
        "<!DOCTYPE html><html lang=\"ja\"><body><h1>顧客一覧</h1></body></html>";

    private static DatabaseProviderRegistry BuildRegistry() =>
        new([new SqlServerProvider(), new SqliteProvider()]);

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

    /// <summary>スキャフォールドが sln・Generated/・csproj・README・design/mock.html を VS 標準構成で出力することを検証する</summary>
    [Fact(DisplayName = "スキャフォールドは土台一式を VS 標準構成で書き出す")]
    public void Scaffold_WritesFullSkeleton()
    {
        var folder = NewTempFolder();
        var service = new MockProjectScaffoldService(BuildRegistry());

        try
        {
            var result = service.Scaffold(
                BuildDiagram(SqlServerProvider.ProviderName),
                folder,
                "AcmeMock",
                DesignHtml
            );

            // VS 標準構成: sln は出力フォルダ直下、プロジェクト一式はプロジェクトフォルダ配下
            var projectDirectory = Path.Combine(folder, "AcmeMock");
            result.ProjectDirectory.Should().Be(projectDirectory);
            result.SolutionFilePath.Should().Be(Path.Combine(folder, "AcmeMock.sln"));
            File.Exists(result.SolutionFilePath).Should().BeTrue();

            // csproj・README・design・Generated はプロジェクトフォルダ配下にある
            result.ProjectFilePath.Should().StartWith(projectDirectory);
            result.ReadmePath.Should().StartWith(projectDirectory);
            result.DesignHtmlPath.Should().StartWith(projectDirectory);
            result.GeneratedDirectory.Should().StartWith(projectDirectory);

            // csproj は WPF・net10.0-windows・MVVM 依存を持つ
            File.Exists(result.ProjectFilePath).Should().BeTrue();
            var csproj = File.ReadAllText(result.ProjectFilePath);
            csproj.Should().Contain("<UseWPF>true</UseWPF>");
            csproj.Should().Contain("net10.0-windows");
            csproj.Should().Contain("CommunityToolkit.Mvvm");
            csproj.Should().Contain("Microsoft.Extensions.DependencyInjection");

            // README はデータ層読み取り専用・InMemory DI 登録・実 DB 切替を案内する
            File.Exists(result.ReadmePath).Should().BeTrue();
            var readme = File.ReadAllText(result.ReadmePath);
            readme.Should().Contain("Generated/");
            readme.Should().Contain("AddGeneratedInMemoryRepositories");
            readme.Should().Contain("I{Entity}Repository");

            // design/mock.html に確定 HTML がそのまま入る
            File.Exists(result.DesignHtmlPath).Should().BeTrue();
            File.ReadAllText(result.DesignHtmlPath).Should().Be(DesignHtml);

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
        }
    }

    /// <summary>SQL Server 方言の図ではQuickER の SQL Server Repository（と SqlClient 依存）が出力されることを検証する</summary>
    [Fact(DisplayName = "SQL Server 方言ではRepository (QuickER) と SqlClient 依存を出す")]
    public void Scaffold_SqlServer_EmitsRepositoryAndAdoPackage()
    {
        var folder = NewTempFolder();
        var service = new MockProjectScaffoldService(BuildRegistry());

        try
        {
            var result = service.Scaffold(
                BuildDiagram(SqlServerProvider.ProviderName),
                folder,
                "AcmeMock",
                DesignHtml
            );

            result.RepositoryDialect.Should().Be("sqlserver");
            File.ReadAllText(result.ProjectFilePath).Should().Contain("Microsoft.Data.SqlClient");

            var allGenerated = string.Concat(
                Directory
                    .GetFiles(result.GeneratedDirectory, "*.g.cs", SearchOption.AllDirectories)
                    .Select(File.ReadAllText)
            );
            // 単一方言生成では方言接尾辞なしの AddGeneratedRepositories を出す（マルチターゲット時のみ方言別）
            allGenerated.Should().Contain("AddGeneratedRepositories");
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>SQLite 方言の図ではQuickER の SQLite Repository（と Sqlite 依存）が出力されることを検証する</summary>
    [Fact(DisplayName = "SQLite 方言ではRepository (QuickER) と Sqlite 依存を出す")]
    public void Scaffold_Sqlite_EmitsRepositoryAndAdoPackage()
    {
        var folder = NewTempFolder();
        var service = new MockProjectScaffoldService(BuildRegistry());

        try
        {
            var result = service.Scaffold(
                BuildDiagram(SqliteProvider.ProviderName),
                folder,
                "AcmeMock",
                DesignHtml
            );

            result.RepositoryDialect.Should().Be("sqlite");
            File.ReadAllText(result.ProjectFilePath).Should().Contain("Microsoft.Data.Sqlite");
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>非対応方言（PostgreSQL 等）の図ではRepository (QuickER) を出さず、ADO 依存も含めないことを検証する</summary>
    [Fact(DisplayName = "非対応方言ではRepository (QuickER) を出さない")]
    public void Scaffold_UnsupportedDialect_OmitsRepository()
    {
        var folder = NewTempFolder();
        var service = new MockProjectScaffoldService(BuildRegistry());

        try
        {
            var result = service.Scaffold(
                BuildDiagram("postgresql"),
                folder,
                "AcmeMock",
                DesignHtml
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
        }
    }

    /// <summary>生成した .sln の構文（Format Version・Project/EndProject・構成セクション・プロジェクト参照）を検証する</summary>
    [Fact(DisplayName = "sln は VS 標準の構文とプロジェクト参照を含む")]
    public void Scaffold_SolutionHasValidSyntax()
    {
        var folder = NewTempFolder();
        var service = new MockProjectScaffoldService(BuildRegistry());

        try
        {
            var result = service.Scaffold(
                BuildDiagram(SqlServerProvider.ProviderName),
                folder,
                "AcmeMock",
                DesignHtml
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
        }
    }

    /// <summary>プロジェクト GUID が名前から決定的に導出される（同名なら同 GUID・別名なら別 GUID）ことを検証する</summary>
    [Fact(DisplayName = "sln のプロジェクト GUID は名前から決定的")]
    public void Scaffold_SolutionProjectGuidIsDeterministic()
    {
        var folder1 = NewTempFolder();
        var folder2 = NewTempFolder();
        var folder3 = NewTempFolder();
        var service = new MockProjectScaffoldService(BuildRegistry());

        try
        {
            var same1 = service.Scaffold(
                BuildDiagram(SqlServerProvider.ProviderName),
                folder1,
                "AcmeMock",
                DesignHtml
            );
            var same2 = service.Scaffold(
                BuildDiagram(SqlServerProvider.ProviderName),
                folder2,
                "AcmeMock",
                DesignHtml
            );
            var other = service.Scaffold(
                BuildDiagram(SqlServerProvider.ProviderName),
                folder3,
                "OtherMock",
                DesignHtml
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
