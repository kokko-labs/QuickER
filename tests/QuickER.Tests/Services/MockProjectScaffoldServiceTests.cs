using System.IO;
using FluentAssertions;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Services;
using QuickER.Sqlite;
using QuickER.SqlServer;

namespace QuickER.Tests.Services;

/// <summary>
/// <see cref="MockProjectScaffoldService"/> の決定的スキャフォールド（Generated/ 生成コード・csproj・
/// README・design/mock.html の書き出し）と、図の方言に応じた自作 Repository 出力を検証するテストクラス。
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

    /// <summary>スキャフォールドが Generated/・csproj・README・design/mock.html を出力することを検証する</summary>
    [Fact(DisplayName = "スキャフォールドは土台一式を出力フォルダへ書き出す")]
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

    /// <summary>SQL Server 方言の図では自作 SQL Server Repository（と SqlClient 依存）が出力されることを検証する</summary>
    [Fact(DisplayName = "SQL Server 方言では自作 Repository と SqlClient 依存を出す")]
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

    /// <summary>SQLite 方言の図では自作 SQLite Repository（と Sqlite 依存）が出力されることを検証する</summary>
    [Fact(DisplayName = "SQLite 方言では自作 Repository と Sqlite 依存を出す")]
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

    /// <summary>非対応方言（PostgreSQL 等）の図では自作 Repository を出さず、ADO 依存も含めないことを検証する</summary>
    [Fact(DisplayName = "非対応方言では自作 Repository を出さない")]
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
}
