using System.IO;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using QuickER.Documents;
using QuickER.Mcp.Tools;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Mcp.Tools;

/// <summary>
/// MCP の ER 図操作ツール（<see cref="DocumentErDiagramToolSet"/> の 16 ツール）へ、形の崩れた引数を総当たりで流す
/// </summary>
/// <remarks>
/// <para>
/// 入口は MCP サーバが呼ぶのと同じ <see cref="QuickER.Mcp.McpToolSet.Execute"/>。ケースは各ツールの入力スキーマから
/// <see cref="ToolArgumentFuzz"/> が作る（必須の欠落・<c>null</c>・型違い・配列の要素の種類違い・要素の中の崩し）。
/// 流れと表明は <see cref="ToolArgumentShapeFuzzRunner"/> が持ち、ここはこの入口に固有の部分だけを渡す。
/// </para>
/// <para>
/// 表明は 3 つ：(1) 生の例外にならない（ホストが例外を受け止めて返す <c>Error: </c> の文で返らない）、
/// (2) 断るべきケースは失敗で返る（必須の欠落・型違いを既定値や読み捨てで黙って進めない）、
/// (3) 失敗したら図のファイルはバイト単位で変わらない（<c>create_diagram</c> はファイルを作らない）。
/// </para>
/// </remarks>
public sealed class ToolArgumentShapeFuzzTests : IDisposable
{
    /// <summary>新しい図ファイルを作るツールへ渡す <c>file</c> の目印（ケースごとに未使用のパスへ置き換える）</summary>
    private const string NewFileMarker = "<<new-file>>";

    /// <summary>ホストが例外を受け止めたときの結果の接頭辞（<see cref="DocumentErDiagramToolHost.Execute"/>）</summary>
    private const string RawExceptionPrefix = "Error: ";

    /// <summary>既知の失敗（直す前の現状）。直したら外す</summary>
    private static readonly KnownCaseLedger Ledger = new(
        "ToolArgumentShapeFuzz.mcp",
        ToolArgumentShapeFuzzKnownFailures.Mcp
    );

    /// <summary>各テスト専用の一時ディレクトリ</summary>
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(),
        "quicker-arg-fuzz-" + Guid.NewGuid().ToString("N")
    );

    public ToolArgumentShapeFuzzTests()
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

    [Fact(
        DisplayName = "MCP の ER 図操作ツールは、形の崩れた引数で生の例外にならず、断るべきものは断り、失敗で図を変えない"
    )]
    public void MalformedArguments_AreRefusedWithoutSideEffects()
    {
        var toolSet = DocumentErDiagramToolSet.Create();
        var file = Path.Combine(_dir, "diagram.json");
        JsonStorageService.SaveAtomic(
            file,
            new DiagramDocument
            {
                Version = DiagramDocument.CurrentVersion,
                Schema = ToolArgumentFuzzSeed.BuildDiagram(),
            }
        );
        var seed = File.ReadAllBytes(file);

        var baselines = ToolArgumentFuzzSeed
            .CatalogBaselines()
            .ToDictionary(
                pair => pair.Key,
                pair =>
                    (IReadOnlyList<(string, JsonObject)>)
                        pair
                            .Value.Select(baseline =>
                            {
                                var args = (JsonObject)baseline.Args.DeepClone();
                                args["file"] = file;
                                return (baseline.Label, args);
                            })
                            .ToList()
            );
        baselines[DocumentErDiagramToolHost.CreateDiagramToolName] =
        [
            ("base", new JsonObject { ["file"] = NewFileMarker, ["target_dbms"] = "sqlite" }),
        ];

        toolSet
            .Tools.Select(tool => tool.Name)
            .Should()
            .BeEquivalentTo(baselines.Keys, "公開しているツールすべてにベースラインを持つ");

        ToolArgumentShapeFuzzRunner.Run(new Entrance(toolSet, baselines, file, seed, _dir));
    }

    /// <summary>MCP の ER 図操作ツールの入口（図ファイルをケースごとに種へ戻す）</summary>
    private sealed class Entrance(
        QuickER.Mcp.McpToolSet toolSet,
        IReadOnlyDictionary<string, IReadOnlyList<(string Label, JsonObject Args)>> baselines,
        string file,
        byte[] seed,
        string dir
    ) : ToolArgumentShapeFuzzRunner.Entrance
    {
        public override string Scope => ToolArgumentFuzzDimensions.McpScope;

        public override KnownCaseLedger Ledger => ToolArgumentShapeFuzzTests.Ledger;

        public override int MinimumCases => 301;

        public override IReadOnlyList<ToolArgumentShapeFuzzRunner.Tool> Tools() =>
            toolSet
                .Tools.Select(tool => new ToolArgumentShapeFuzzRunner.Tool(
                    tool.Name,
                    tool.InputSchema,
                    baselines[tool.Name]
                ))
                .ToList();

        public override (string Result, bool Success) RunBaseline(string toolName, JsonObject args)
        {
            File.WriteAllBytes(file, seed);
            return Run(toolName, args, out _);
        }

        public override void PrepareCase() => File.WriteAllBytes(file, seed);

        public override ToolArgumentShapeFuzzRunner.CaseOutcome RunCase(
            string toolName,
            JsonObject args
        )
        {
            var (result, success) = Run(toolName, args, out var newFile);

            return new(
                result,
                success,
                () =>
                {
                    if (!File.ReadAllBytes(file).AsSpan().SequenceEqual(seed))
                    {
                        return "失敗したのに図が変わった";
                    }

                    return newFile is not null && File.Exists(newFile)
                        ? "失敗したのにファイルを作った"
                        : null;
                }
            );
        }

        public override bool IsRawException(string result) =>
            result.StartsWith(RawExceptionPrefix, StringComparison.Ordinal);

        /// <summary>MCP サーバと同じ入口でツールを実行する（新しい図ファイルの目印は未使用のパスへ置き換える）</summary>
        private (string Result, bool Success) Run(
            string toolName,
            JsonObject arguments,
            out string? newFile
        )
        {
            newFile = null;
            var args = (JsonObject)arguments.DeepClone();

            if (
                args["file"] is JsonValue marker
                && marker.TryGetValue<string>(out var text)
                && text == NewFileMarker
            )
            {
                newFile = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".json");
                args["file"] = newFile;
            }

            try
            {
                return toolSet.Execute(toolName, args.ToJsonString());
            }
            catch (Exception ex)
            {
                return (
                    $"{RawExceptionPrefix}（入口から漏れた {ex.GetType().Name}）{ex.Message}",
                    false
                );
            }
        }
    }
}
