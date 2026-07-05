using System.IO;
using System.Linq;
using FluentAssertions;
using QuickER.Cli;
using QuickER.Documents;
using QuickER.Model;

namespace QuickER.Tests.Cli;

/// <summary><see cref="CliApp"/> の generate コマンドが ER 図 JSON から実際にコードを書き出すことを検証するテストクラス</summary>
public class CliAppTests
{
    /// <summary>一時フォルダに保存形式の ER 図 JSON を作成する</summary>
    private static (string schemaPath, string outDir, string root) CreateSampleSchema()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "QuickERCliTests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(root);
        var schemaPath = Path.Combine(root, "schema.json");

        var document = new DiagramDocument();
        var entity = new Entity { TableName = "Customer" };
        entity.Columns.Add(
            new Column
            {
                Name = "Id",
                DataType = "int",
                IsPrimaryKey = true,
                IsNullable = false,
            }
        );
        entity.Columns.Add(new Column { Name = "Name", DataType = "nvarchar(100)" });
        document.Schema.Entities.Add(entity);
        JsonStorageService.Save(schemaPath, document);

        return (schemaPath, Path.Combine(root, "out"), root);
    }

    /// <summary>generate が JSON スキーマから .g.cs を出力し終了コード 0 を返すことを検証する</summary>
    [Fact(DisplayName = "generate は JSON スキーマからコードを出力する")]
    public async Task Generate_WritesCode_FromSchemaJson()
    {
        var (schemaPath, outDir, root) = CreateSampleSchema();

        try
        {
            var exit = await CliApp.InvokeAsync([
                "generate",
                "--schema",
                schemaPath,
                "--out",
                outDir,
                "--namespace",
                "Test.Ns",
            ]);

            exit.Should().Be(0);
            var files = Directory.GetFiles(outDir, "*.g.cs");
            files.Should().NotBeEmpty();
            File.ReadAllText(files[0]).Should().Contain("namespace Test.Ns");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>未対応プロバイダを指定すると終了コード 1 を返すことを検証する</summary>
    [Fact(DisplayName = "未対応プロバイダ指定は終了コード 1")]
    public async Task Generate_UnknownProvider_ReturnsError()
    {
        var (schemaPath, outDir, root) = CreateSampleSchema();

        try
        {
            var exit = await CliApp.InvokeAsync([
                "generate",
                "--schema",
                schemaPath,
                "--out",
                outDir,
                "--provider",
                "no_such_provider",
            ]);

            exit.Should().Be(1);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>
    /// 自作 Repository の生成が要求されているが（quicker.json の GenerateRepositories=true）、
    /// プロバイダが未対応方言（postgresql / mysql / oracle）の場合は終了コード 1 でエラーメッセージを出すことを検証する
    /// </summary>
    [Theory(DisplayName = "未対応方言＋GenerateRepositories 要求は終了コード 1")]
    [InlineData("postgresql")]
    [InlineData("mysql")]
    [InlineData("oracle")]
    public async Task Generate_UnsupportedDialectWithRepositories_ReturnsError(string providerName)
    {
        var (schemaPath, outDir, root) = CreateSampleSchema();
        var configPath = Path.Combine(root, "quicker.json");
        File.WriteAllText(configPath, """{ "GenerateRepositories": true }""");

        try
        {
            var exit = await CliApp.InvokeAsync([
                "generate",
                "--schema",
                schemaPath,
                "--out",
                outDir,
                "--provider",
                providerName,
                "--config",
                configPath,
            ]);

            exit.Should().Be(1);
            Directory
                .Exists(outDir)
                .Should()
                .BeFalse("生成前にエラーで中止するため出力は作られない");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>
    /// 対応方言（sqlite）＋ GenerateRepositories=true では生成が成功し、
    /// RepositoryDialect が --provider の値（sqlite）で確定していることを検証する
    /// </summary>
    [Fact(DisplayName = "対応方言（sqlite）＋GenerateRepositories は生成が成功する")]
    public async Task Generate_SupportedDialectWithRepositories_Succeeds()
    {
        var (schemaPath, outDir, root) = CreateSampleSchema();
        var configPath = Path.Combine(root, "quicker.json");
        File.WriteAllText(configPath, """{ "GenerateRepositories": true }""");

        try
        {
            var exit = await CliApp.InvokeAsync([
                "generate",
                "--schema",
                schemaPath,
                "--out",
                outDir,
                "--provider",
                "sqlite",
                "--config",
                configPath,
            ]);

            exit.Should().Be(0);
            var files = Directory.GetFiles(outDir, "*.g.cs");
            var code = string.Join("\n", files.Select(File.ReadAllText));
            code.Should().Contain("Microsoft.Data.Sqlite");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
