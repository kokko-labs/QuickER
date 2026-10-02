using System.IO;
using System.Text.Json;
using System.Threading;
using AwesomeAssertions;
using QuickER.Documents;
using QuickER.Mcp.Tools;

namespace QuickER.Tests.Mcp.Tools;

/// <summary>
/// 同じファイルへの並行したツール呼び出しが、<see cref="DiagramFileGate"/> によって 1 本ずつ処理されることを検証する。
/// </summary>
/// <remarks>
/// MCP クライアントは 1 ターン内の独立したツール呼び出しを並列に送る。ロックが無いと「読込 → 変更 → 保存」が重なり、
/// 後から保存したほうが先の更新を消す（両方とも成功を返す）。各テストは複数回繰り返し、たまたま重ならなかった実行で
/// 緑になることを避ける。
/// </remarks>
public sealed class DocumentErDiagramToolConcurrencyTests : IDisposable
{
    /// <summary>同時に走らせるスレッドの数</summary>
    private const int ThreadCount = 32;

    /// <summary>繰り返しの回数</summary>
    private const int Rounds = 3;

    /// <summary>読みと書きを重ねるテストで、1 スレッドが続けて呼ぶ回数</summary>
    internal const int IterationsPerThread = 10;

    /// <summary>各テスト専用の一時ディレクトリ</summary>
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(),
        "quicker-mcp-concurrency-" + Guid.NewGuid().ToString("N")
    );

    public DocumentErDiagramToolConcurrencyTests()
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

    /// <summary>引数オブジェクトを JsonElement 化してツールを実行する</summary>
    private static (string Result, bool Success) Exec(string tool, string file, object args) =>
        DocumentErDiagramToolHost.Execute(tool, file, JsonSerializer.SerializeToElement(args));

    /// <summary>
    /// 処理を専用スレッドで一斉に走らせ、全スレッドの戻り値を返す（開始をそろえて重なりを起こしやすくする）
    /// </summary>
    internal static List<T> RunConcurrently<T>(int count, Func<int, T> action)
    {
        var results = new T[count];
        using var start = new ManualResetEventSlim(false);
        var threads = Enumerable
            .Range(0, count)
            .Select(index => new Thread(() =>
            {
                start.Wait();
                results[index] = action(index);
            }))
            .ToList();

        threads.ForEach(thread => thread.Start());
        start.Set();
        threads.ForEach(thread => thread.Join());

        return results.ToList();
    }

    [Fact(DisplayName = "同じファイルへの並行した add_column は 1 つも失われない")]
    public void ConcurrentAddColumn_KeepsEveryColumn()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var file = Path.Combine(_dir, $"round{round}.json");
            Exec(
                DocumentErDiagramToolHost.CreateDiagramToolName,
                file,
                new { target_dbms = "sqlite" }
            )
                .Success.Should()
                .BeTrue();
            Exec("add_entity", file, new { table_name = "Item" }).Success.Should().BeTrue();

            var results = RunConcurrently(
                ThreadCount,
                index =>
                    Exec(
                        "add_column",
                        file,
                        new
                        {
                            table_name = "Item",
                            column_name = $"Col{index}",
                            data_type = "int",
                        }
                    )
            );

            results.Should().AllSatisfy(result => result.Success.Should().BeTrue(result.Result));

            var columns = JsonStorageService
                .Load(file)
                .Schema.Entities.Single()
                .Columns.Select(column => column.Name)
                .ToList();
            columns
                .Should()
                .BeEquivalentTo(
                    Enumerable.Range(0, ThreadCount).Select(index => $"Col{index}"),
                    $"round {round}: every concurrent add_column must survive"
                );
        }
    }

    [Fact(DisplayName = "同じパスへの並行した create_diagram はちょうど 1 本だけが成功する")]
    public void ConcurrentCreateDiagram_ExactlyOneSucceeds()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var file = Path.Combine(_dir, $"create{round}.json");

            var results = RunConcurrently(
                ThreadCount,
                _ =>
                    Exec(
                        DocumentErDiagramToolHost.CreateDiagramToolName,
                        file,
                        new { target_dbms = "sqlite" }
                    )
            );

            results.Count(result => result.Success).Should().Be(1, $"round {round}");
            results
                .Where(result => !result.Success)
                .Should()
                .AllSatisfy(result => result.Result.Should().Contain("already exists"));
        }
    }

    [Fact(DisplayName = "保存と並行した get_diagram_summary は共有違反で失敗しない")]
    public void ConcurrentSummaryAndWrites_AllSucceed()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var file = Path.Combine(_dir, $"summary{round}.json");
            Exec(
                DocumentErDiagramToolHost.CreateDiagramToolName,
                file,
                new { target_dbms = "sqlite" }
            )
                .Success.Should()
                .BeTrue();
            Exec("add_entity", file, new { table_name = "Item" }).Success.Should().BeTrue();

            // 偶数番は列を足し続け、奇数番は要約を読み続ける（読みと保存の差し替えを何度も重ねる。
            // 1 回ずつだと重なる確率が低く、ロックが無くても緑になり得る）
            var results = RunConcurrently(
                ThreadCount,
                index =>
                    Enumerable
                        .Range(0, IterationsPerThread)
                        .Select(iteration =>
                            index % 2 == 0
                                ? Exec(
                                    "add_column",
                                    file,
                                    new
                                    {
                                        table_name = "Item",
                                        column_name = $"Col{index}_{iteration}",
                                        data_type = "int",
                                    }
                                )
                                : Exec("get_diagram_summary", file, new { })
                        )
                        .ToList()
            );

            results
                .SelectMany(perThread => perThread)
                .Should()
                .AllSatisfy(result => result.Success.Should().BeTrue(result.Result));
        }
    }
}
