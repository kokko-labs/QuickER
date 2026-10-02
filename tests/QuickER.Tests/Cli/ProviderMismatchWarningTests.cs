using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Cli;
using QuickER.Documents;
using QuickER.Mcp.Tools;
using QuickER.Model;
using Xunit;
using CliStrings = QuickER.Cli.Resources.Strings;

namespace QuickER.Tests.Cli;

/// <summary>
/// 図の対象 DBMS と違うプロバイダを明示したとき、型を変換せずに生成することを警告する（生成は続ける）ことを、
/// CLI の generate と MCP の generate_csharp / generate_ddl の両方で検証する。あわせて、図の読込失敗の文言を
/// MCP の 2 種類のツールが共有していることを検証する。
/// </summary>
public class ProviderMismatchWarningTests : IDisposable
{
    private const string McpWarningPrefix = "Warning: provider '";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "QuickERProviderMismatch",
        Guid.NewGuid().ToString("N")
    );

    private readonly string _schemaPath;

    public ProviderMismatchWarningTests()
    {
        Directory.CreateDirectory(_root);
        _schemaPath = Path.Combine(_root, "diagram.json");

        var document = new DiagramDocument { Schema = { TargetDbms = "sqlite" } };
        var entity = new Entity { TableName = "Customer" };
        entity.Columns.Add(
            new Column
            {
                Name = "Id",
                DataType = "INTEGER",
                IsPrimaryKey = true,
                IsNullable = false,
            }
        );
        document.Schema.Entities.Add(entity);
        JsonStorageService.Save(_schemaPath, document);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // 一時フォルダの後始末の失敗は検証の結果に関係しない
        }
    }

    [Fact(DisplayName = "MCP: generate_ddl は図と違う provider で警告を先頭に付けて生成する")]
    public void GenerateDdl_DifferentProvider_WarnsFirst()
    {
        var (result, success) = Execute(
            "generate_ddl",
            new
            {
                file = _schemaPath,
                out_file = Path.Combine(_root, "schema.sql"),
                provider = "postgresql",
            }
        );

        success.Should().BeTrue(result);
        result
            .Should()
            .StartWith(
                "Warning: provider 'postgresql' differs from the diagram's target DBMS 'sqlite'."
            );
        result.Should().Contain("Generated postgresql DDL");
    }

    [Theory(DisplayName = "MCP: generate_ddl は図と同じ方言・provider 省略では警告しない")]
    [InlineData("sqlite")]
    [InlineData("SQLite")]
    [InlineData(null)]
    public void GenerateDdl_SameOrOmittedProvider_DoesNotWarn(string? provider)
    {
        var (result, success) = Execute(
            "generate_ddl",
            new
            {
                file = _schemaPath,
                out_file = Path.Combine(_root, "schema.sql"),
                provider,
            }
        );

        success.Should().BeTrue(result);
        result.Should().NotContain(McpWarningPrefix);
    }

    [Fact(DisplayName = "MCP: generate_csharp は図と違う provider で警告を付けて生成する")]
    public void GenerateCSharp_DifferentProvider_Warns()
    {
        var (result, success) = Execute(
            "generate_csharp",
            new
            {
                file = _schemaPath,
                out_dir = Path.Combine(_root, "out"),
                provider = "sqlserver",
            }
        );

        success.Should().BeTrue(result);
        result
            .Should()
            .StartWith(
                "Warning: provider 'sqlserver' differs from the diagram's target DBMS 'sqlite'."
            );
    }

    [Fact(
        DisplayName = "MCP: generate_csharp は失敗の結果にも図と違う provider の警告を先頭に付ける"
    )]
    public void GenerateCSharp_DifferentProviderAndFailure_WarnsFirst()
    {
        var (result, success) = Execute(
            "generate_csharp",
            new
            {
                file = _schemaPath,
                out_dir = Path.Combine(_root, "out"),
                provider = "sqlserver",
                config = Path.Combine(_root, "missing.json"),
            }
        );

        success.Should().BeFalse(result);
        result.Should().StartWith(McpWarningPrefix);
        result.Should().Contain("Config file not found");
    }

    [Fact(DisplayName = "CLI: generate は図と違う --provider で警告を標準エラーへ出して生成する")]
    public async Task Cli_DifferentProvider_WarnsOnStderr()
    {
        var (exit, stderr) = await RunGenerateAsync("--provider", "sqlserver");

        exit.Should().Be(0, stderr);
        stderr
            .Should()
            .Contain(
                string.Format(CliStrings.Cli_ProviderDiffersFromDiagram, "sqlserver", "sqlite")
            );
    }

    [Fact(DisplayName = "CLI: generate は図と同じ方言・--provider 省略では警告しない")]
    public async Task Cli_SameOrOmittedProvider_DoesNotWarn()
    {
        var warningPrefix = CliStrings.Cli_ProviderDiffersFromDiagram.Split("{0}")[0];

        var (sameExit, sameStderr) = await RunGenerateAsync("--provider", "SQLite");
        var (omittedExit, omittedStderr) = await RunGenerateAsync();

        sameExit.Should().Be(0, sameStderr);
        omittedExit.Should().Be(0, omittedStderr);
        sameStderr.Should().NotContain(warningPrefix);
        omittedStderr.Should().NotContain(warningPrefix);
    }

    /// <summary>
    /// 図の読込失敗の文言は、コード生成ツールと ER 図操作ツールで同じ（共有の <see cref="DiagramLoadErrors"/>）
    /// </summary>
    [Fact(DisplayName = "MCP: 図の読込失敗の文言はコード生成ツールと ER 図操作ツールで同じ")]
    public void LoadError_IsSharedBetweenToolSets()
    {
        var notDiagram = Path.Combine(_root, "not-a-diagram.json");
        File.WriteAllText(notDiagram, """{ "foo": 1 }""");
        var expected = DiagramLoadErrors.Describe(
            notDiagram,
            DocumentLoadError.NotDiagramDocument,
            null
        );

        var (codeGen, codeGenSuccess) = Execute(
            "generate_ddl",
            new { file = notDiagram, out_file = Path.Combine(_root, "schema.sql") }
        );
        var (diagramTool, diagramToolSuccess) = DocumentErDiagramToolSet
            .Create()
            .Execute("get_diagram_summary", JsonSerializer.Serialize(new { file = notDiagram }));

        codeGenSuccess.Should().BeFalse();
        diagramToolSuccess.Should().BeFalse();
        codeGen.Should().Be(expected);
        diagramTool.Should().Be(expected);
    }

    private static (string Result, bool Success) Execute(string tool, object args) =>
        CodeGenToolSet.Create().Execute(tool, JsonSerializer.Serialize(args));

    private async Task<(int Exit, string Stderr)> RunGenerateAsync(params string[] extra)
    {
        var stderr = new StringWriter();
        string[] args =
        [
            "generate",
            "--schema",
            _schemaPath,
            "--out",
            Path.Combine(_root, "cli-out-" + Guid.NewGuid().ToString("N")),
            .. extra,
        ];

        var exit = await CliApp.InvokeAsync(args, new StringWriter(), stderr);

        return (exit, stderr.ToString());
    }
}
