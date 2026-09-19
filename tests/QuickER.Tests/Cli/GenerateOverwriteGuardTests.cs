using System.IO;
using System.Text.Json;
using AwesomeAssertions;
using QuickER.Cli;
using QuickER.Documents;
using QuickER.Model;
using CliStrings = QuickER.Cli.Resources.Strings;

namespace QuickER.Tests.Cli;

/// <summary>
/// 生成後に手で編集された生成ファイルの上書き前チェックを、CLI（<c>generate</c> / <c>scaffold</c> の <c>--force</c>）と
/// MCP（<c>generate_csharp</c> の <c>force</c> 引数）の両入口で検証するテストクラス。
/// </summary>
/// <remarks>
/// 判定は共有の生成コア（<see cref="GenerationExecutor"/>）の書き出し直前にあり、拒否は
/// <see cref="GenerationExecutor.ModifiedFilesExitCode"/> で表す。上書き方法の案内だけが入口ごとに違う
/// （CLI＝<c>--force</c> の resx 文言・MCP＝<c>force: true</c> の英語文）。
/// </remarks>
public sealed class GenerateOverwriteGuardTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "QuickERCliTests",
        nameof(GenerateOverwriteGuardTests),
        Guid.NewGuid().ToString("N")
    );

    private readonly string _schemaPath;
    private readonly string _outDir;

    public GenerateOverwriteGuardTests()
    {
        Directory.CreateDirectory(_root);
        _schemaPath = Path.Combine(_root, "schema.json");
        _outDir = Path.Combine(_root, "out");

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
        document.Schema.Entities.Add(entity);
        JsonStorageService.Save(_schemaPath, document);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>generate を実行し、終了コードと標準出力・標準エラーを返す</summary>
    private async Task<(int Exit, string Stdout, string Stderr)> GenerateAsync(
        params string[] extra
    )
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = await CliApp.InvokeAsync(
            ["generate", "--schema", _schemaPath, "--out", _outDir, .. extra],
            stdout,
            stderr
        );

        return (exit, stdout.ToString(), stderr.ToString());
    }

    /// <summary>1 回生成して、出力された唯一の .g.cs に手書きの 1 行を足す。編集後の内容とパスを返す</summary>
    private async Task<(string Path, string Edited)> GenerateAndEditAsync()
    {
        (await GenerateAsync()).Exit.Should().Be(0);
        var path = Directory.GetFiles(_outDir, "*.g.cs").Should().ContainSingle().Subject;
        var edited = File.ReadAllText(path) + "// hand-written\r\n";
        File.WriteAllText(path, edited);

        return (path, edited);
    }

    [Fact(DisplayName = "CLI: 編集されていなければ再生成はそのまま成功する")]
    public async Task Generate_Twice_WithoutEdits_Succeeds()
    {
        (await GenerateAsync()).Exit.Should().Be(0);

        var (exit, _, stderr) = await GenerateAsync();

        exit.Should().Be(0, stderr);
    }

    [Fact(
        DisplayName = "CLI: 手で編集された生成ファイルがあれば何も書かず終了コード 2 と一覧・--force の案内を出す"
    )]
    public async Task Generate_EditedFile_RefusesWithExitCode2()
    {
        var (path, edited) = await GenerateAndEditAsync();

        var (exit, stdout, stderr) = await GenerateAsync();

        exit.Should().Be(GenerationExecutor.ModifiedFilesExitCode).And.Be(2);
        stderr.Should().Contain(CliStrings.Cli_ModifiedFilesDetected);
        stderr.Should().Contain(path);
        stderr.Should().Contain(CliStrings.Cli_ModifiedFilesForceHint);
        stdout.Should().BeEmpty("1 ファイルも書いていないので生成一覧も出さない");
        File.ReadAllText(path).Should().Be(edited);
    }

    [Fact(DisplayName = "CLI: --force を付けると編集済みファイルを上書きする")]
    public async Task Generate_EditedFileWithForce_Overwrites()
    {
        var (path, edited) = await GenerateAndEditAsync();

        var (exit, _, stderr) = await GenerateAsync("--force");

        exit.Should().Be(0, stderr);
        File.ReadAllText(path).Should().NotBe(edited);
        File.ReadAllText(path).Should().NotContain("// hand-written");
    }

    [Fact(DisplayName = "CLI: scaffold も --force を受け付ける")]
    public async Task Scaffold_AcceptsForceOption()
    {
        var stdout = new StringWriter();

        var exit = await CliApp.InvokeAsync(["scaffold", "--help"], stdout, new StringWriter());

        exit.Should().Be(0);
        stdout.ToString().Should().Contain("--force");
    }

    /// <summary>MCP の generate_csharp を実行する</summary>
    private (string Result, bool Success) GenerateViaMcp(object? force)
    {
        var args = force is null
            ? JsonSerializer.Serialize(new { file = _schemaPath, out_dir = _outDir })
            : JsonSerializer.Serialize(
                new
                {
                    file = _schemaPath,
                    out_dir = _outDir,
                    force,
                }
            );

        return CodeGenToolSet.Create().Execute("generate_csharp", args);
    }

    [Fact(
        DisplayName = "MCP: 手で編集された生成ファイルがあれば失敗として一覧と force: true の案内を返す"
    )]
    public async Task Mcp_EditedFile_RefusesWithForceGuidance()
    {
        var (path, edited) = await GenerateAndEditAsync();

        var (result, success) = GenerateViaMcp(force: null);

        success.Should().BeFalse();
        result.Should().Contain(path);
        result.Should().Contain("force: true");
        result.Should().NotContain("--force", "CLI 向けの案内は MCP の結果へ出さない");
        File.ReadAllText(path).Should().Be(edited);
    }

    [Fact(DisplayName = "MCP: force: true なら編集済みファイルを上書きする")]
    public async Task Mcp_EditedFileWithForce_Overwrites()
    {
        var (path, _) = await GenerateAndEditAsync();

        var (result, success) = GenerateViaMcp(force: true);

        success.Should().BeTrue(result);
        File.ReadAllText(path).Should().NotContain("// hand-written");
    }

    [Fact(DisplayName = "MCP: force が真偽値でなければ何も書かずに拒否する")]
    public async Task Mcp_NonBooleanForce_IsRejected()
    {
        var (path, edited) = await GenerateAndEditAsync();

        var (result, success) = GenerateViaMcp(force: "yes");

        success.Should().BeFalse();
        result.Should().Contain("force must be a boolean");
        File.ReadAllText(path).Should().Be(edited);
    }
}
