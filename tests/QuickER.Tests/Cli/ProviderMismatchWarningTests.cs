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
        JsonStorageService.SaveAtomic(_schemaPath, document);
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

    [Theory(
        DisplayName = "MCP: 採番の列を持てない provider で生成すると、方言違いの警告で印の列を名指しする"
    )]
    [InlineData("generate_ddl")]
    [InlineData("generate_csharp")]
    public void Mcp_ProviderWithoutIdentity_NamesMarkedColumns(string tool)
    {
        var schemaPath = WriteSqlServerDiagramWithIdentity();

        var (result, success) = Execute(tool, ToolArgs(tool, schemaPath, "sqlite"));

        success.Should().BeTrue(result);
        result.Should().StartWith(McpWarningPrefix);
        result
            .Should()
            .Contain(
                "The diagram marks columns the database numbers (identity columns), which sqlite cannot have: Orders.Seq."
            );
    }

    [Theory(DisplayName = "MCP: 採番の列を持てる provider・図と同じ方言では、印の列を名指ししない")]
    [InlineData("generate_ddl", "postgresql")]
    [InlineData("generate_ddl", "sqlserver")]
    [InlineData("generate_csharp", "postgresql")]
    public void Mcp_ProviderWithIdentity_DoesNotNameMarkedColumns(string tool, string provider)
    {
        var schemaPath = WriteSqlServerDiagramWithIdentity();

        var (result, success) = Execute(tool, ToolArgs(tool, schemaPath, provider));

        success.Should().BeTrue(result);
        result.Should().NotContain("The diagram marks columns the database numbers");
    }

    [Fact(
        DisplayName = "CLI: generate は採番の列を持てない --provider で、印の列を標準エラーで名指しする"
    )]
    public async Task Cli_ProviderWithoutIdentity_NamesMarkedColumns()
    {
        var schemaPath = WriteSqlServerDiagramWithIdentity();

        var (exit, stderr) = await RunGenerateWithSchemaAsync(schemaPath, "--provider", "sqlite");

        exit.Should().Be(0, stderr);
        stderr
            .Should()
            .Contain(
                string.Format(
                    CliStrings.Cli_IdentityColumnsUnsupportedByProvider,
                    "sqlite",
                    "Orders.Seq"
                )
            );
    }

    [Fact(DisplayName = "CLI: generate は採番の列を持てる --provider では、印の列を名指ししない")]
    public async Task Cli_ProviderWithIdentity_DoesNotNameMarkedColumns()
    {
        var schemaPath = WriteSqlServerDiagramWithIdentity();
        var prefix = CliStrings.Cli_IdentityColumnsUnsupportedByProvider.Split("{0}")[0];

        var (exit, stderr) = await RunGenerateWithSchemaAsync(
            schemaPath,
            "--provider",
            "postgresql"
        );

        exit.Should().Be(0, stderr);
        stderr.Should().NotContain(prefix);
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

    /// <summary>SQL Server の図（主キーでない採番の列 Orders.Seq を持つ）を書き出し、そのパスを返す</summary>
    private string WriteSqlServerDiagramWithIdentity()
    {
        var path = Path.Combine(_root, "identity-" + Guid.NewGuid().ToString("N") + ".json");
        var document = new DiagramDocument { Schema = { TargetDbms = "sqlserver" } };
        var entity = new Entity { TableName = "Orders" };
        entity.Columns.Add(
            new Column
            {
                Name = "Id",
                DataType = "int",
                IsPrimaryKey = true,
                IsNullable = false,
            }
        );
        entity.Columns.Add(
            new Column
            {
                Name = "Seq",
                DataType = "int",
                IsNullable = false,
                IsIdentity = true,
            }
        );
        document.Schema.Entities.Add(entity);
        JsonStorageService.SaveAtomic(path, document);

        return path;
    }

    /// <summary>コード生成ツールの引数（出力先はツールごとに違う）</summary>
    private object ToolArgs(string tool, string schemaPath, string provider) =>
        tool == "generate_ddl"
            ? new
            {
                file = schemaPath,
                out_file = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".sql"),
                provider,
            }
            : new
            {
                file = schemaPath,
                out_dir = Path.Combine(_root, "out-" + Guid.NewGuid().ToString("N")),
                provider,
            };

    private Task<(int Exit, string Stderr)> RunGenerateAsync(params string[] extra) =>
        RunGenerateWithSchemaAsync(_schemaPath, extra);

    private async Task<(int Exit, string Stderr)> RunGenerateWithSchemaAsync(
        string schemaPath,
        params string[] extra
    )
    {
        var stderr = new StringWriter();
        string[] args =
        [
            "generate",
            "--schema",
            schemaPath,
            "--out",
            Path.Combine(_root, "cli-out-" + Guid.NewGuid().ToString("N")),
            .. extra,
        ];

        var exit = await CliApp.InvokeAsync(args, new StringWriter(), stderr);

        return (exit, stderr.ToString());
    }
}
