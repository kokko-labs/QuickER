using System.IO;
using System.Text.Json;
using AwesomeAssertions;
using QuickER.Cli;
using QuickER.Mcp.Tools;

namespace QuickER.Tests.Cli;

/// <summary>
/// コード生成ツールが図ファイルを <see cref="DiagramFileGate"/> のロックの中で読むことを検証する。
/// </summary>
/// <remarks>
/// 同じ MCP サーバの ER 図操作ツールは図を一時ファイルからの差し替えで保存し、差し替えと同じファイルの読み取りが
/// 重なると読み取り側が共有違反で失敗する（実測で確認）。生成ツールの読み取りが同じロックを取っていれば重ならない。
/// 重なりの発生は確率的で、並行実行のテストでは読み取りの外し忘れを安定して検出できないため、
/// ロックを持ったまま呼び出し、ロックを手放すまで生成が終わらないことを確かめる。
/// </remarks>
public sealed class CodeGenToolSetConcurrencyTests : IDisposable
{
    /// <summary>ロックを持っている間、生成が終わらないことを確かめる待ち時間</summary>
    private static readonly TimeSpan BlockedWait = TimeSpan.FromSeconds(1);

    /// <summary>ロックを手放した後、生成が終わるまでの上限</summary>
    private static readonly TimeSpan CompletionTimeout = TimeSpan.FromSeconds(30);

    /// <summary>各テスト専用の一時ディレクトリ</summary>
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(),
        "quicker-codegen-concurrency-" + Guid.NewGuid().ToString("N")
    );

    public CodeGenToolSetConcurrencyTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // クリーンアップ失敗はテスト結果に影響させない
        }
    }

    [Theory(DisplayName = "コード生成ツールは図の読み取りの前に図ファイルのロックを待つ")]
    [InlineData("generate_ddl")]
    [InlineData("generate_csharp")]
    public void GenerationTool_WaitsForDiagramFileGate(string toolName)
    {
        var diagramTools = DocumentErDiagramToolSet.Create();
        var codeGenTools = CodeGenToolSet.Create();
        var file = Path.Combine(_dir, "diagram.json");

        diagramTools
            .Execute(
                DocumentErDiagramToolHost.CreateDiagramToolName,
                JsonSerializer.Serialize(new { file, target_dbms = "sqlite" })
            )
            .Success.Should()
            .BeTrue();
        diagramTools
            .Execute("add_entity", JsonSerializer.Serialize(new { file, table_name = "Item" }))
            .Success.Should()
            .BeTrue();
        diagramTools
            .Execute(
                "add_column",
                JsonSerializer.Serialize(
                    new
                    {
                        file,
                        table_name = "Item",
                        column_name = "Id",
                        data_type = "int",
                        is_primary_key = true,
                    }
                )
            )
            .Success.Should()
            .BeTrue();

        string ArgumentsFor(string name) =>
            toolName == "generate_ddl"
                ? JsonSerializer.Serialize(
                    new { file, out_file = Path.Combine(_dir, name + ".sql") }
                )
                : JsonSerializer.Serialize(new { file, out_dir = Path.Combine(_dir, name) });

        // 1 回目で JIT と初期化を済ませる（待ち時間の判定を初回の遅さで誤らないため）
        codeGenTools.Execute(toolName, ArgumentsFor("warmup")).Success.Should().BeTrue();

        (string Result, bool Success) result = default;
        var worker = new Thread(() =>
            result = codeGenTools.Execute(toolName, ArgumentsFor("blocked"))
        );

        DiagramFileGate.Run(() =>
        {
            worker.Start();

            worker
                .Join(BlockedWait)
                .Should()
                .BeFalse($"{toolName} must not read the diagram while the gate is held");

            return 0;
        });

        worker.Join(CompletionTimeout).Should().BeTrue();
        result.Success.Should().BeTrue(result.Result);
    }
}
