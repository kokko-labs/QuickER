using System.IO;
using System.Text.Json.Nodes;
using QuickER.Cli;
using QuickER.Documents;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Cli;

/// <summary>
/// MCP のコード生成ツール（<see cref="CodeGenToolSet"/> の <c>generate_csharp</c> / <c>generate_ddl</c>）へ、
/// 形の崩れた引数を総当たりで流す
/// </summary>
/// <remarks>
/// 入口は MCP サーバが呼ぶのと同じ <see cref="QuickER.Mcp.McpToolSet.Execute"/>。表明は ER 図操作ツールの
/// 総当たり（<c>ToolArgumentShapeFuzzTests</c>）と同じ 3 つで、「失敗で何も変えない」には出力先を作らないことも含める。
/// 流れと表明は <see cref="ToolArgumentShapeFuzzRunner"/> が持ち、ここはこの入口に固有の部分だけを渡す。
/// 引数を取らない <c>get_generation_config_schema</c> には崩す引数が無いので対象外。
/// </remarks>
public sealed class CodeGenToolArgumentShapeFuzzTests : IDisposable
{
    /// <summary>出力先の目印（ケースごとに未使用のパスへ置き換える）</summary>
    private const string OutDirMarker = "<<out-dir>>";

    /// <summary>DDL の出力先の目印（ケースごとに未使用のパスへ置き換える）</summary>
    private const string OutSqlMarker = "<<out-sql>>";

    /// <summary>ツールセットが例外を受け止めたときの結果の接頭辞</summary>
    private const string RawExceptionPrefix = "Error: ";

    /// <summary>既知の失敗（直す前の現状）。直したら外す</summary>
    private static readonly KnownCaseLedger Ledger = new(
        "ToolArgumentShapeFuzz.codegen",
        ToolArgumentShapeFuzzKnownFailures.CodeGen
    );

    /// <summary>各テスト専用の一時ディレクトリ</summary>
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(),
        "quicker-codegen-arg-fuzz-" + Guid.NewGuid().ToString("N")
    );

    public CodeGenToolArgumentShapeFuzzTests()
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
        DisplayName = "MCP のコード生成ツールは、形の崩れた引数で生の例外にならず、断るべきものは断り、失敗で何も書かない"
    )]
    public void MalformedArguments_AreRefusedWithoutSideEffects()
    {
        var toolSet = CodeGenToolSet.Create();
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

        var baselines = new Dictionary<string, JsonObject>
        {
            ["generate_csharp"] = new()
            {
                ["file"] = file,
                ["out_dir"] = OutDirMarker,
                ["provider"] = "sqlserver",
                ["force"] = false,
            },
            ["generate_ddl"] = new()
            {
                ["file"] = file,
                ["out_file"] = OutSqlMarker,
                ["provider"] = "sqlserver",
            },
        };

        var tools = baselines
            .Select(pair => new ToolArgumentShapeFuzzRunner.Tool(
                pair.Key,
                toolSet.Tools.Single(candidate => candidate.Name == pair.Key).InputSchema,
                [("base", pair.Value)]
            ))
            .ToList();

        ToolArgumentShapeFuzzRunner.Run(new Entrance(toolSet, tools, file, seed, _dir));
    }

    /// <summary>MCP のコード生成ツールの入口（図ファイルをケースごとに種へ戻し、出力先の目印を未使用のパスへ置き換える）</summary>
    private sealed class Entrance(
        QuickER.Mcp.McpToolSet toolSet,
        IReadOnlyList<ToolArgumentShapeFuzzRunner.Tool> tools,
        string file,
        byte[] seed,
        string dir
    ) : ToolArgumentShapeFuzzRunner.Entrance
    {
        public override string Scope => ToolArgumentFuzzDimensions.CodegenScope;

        public override KnownCaseLedger Ledger => CodeGenToolArgumentShapeFuzzTests.Ledger;

        public override int MinimumCases => 41;

        public override IReadOnlyList<ToolArgumentShapeFuzzRunner.Tool> Tools() => tools;

        public override (string Result, bool Success) RunBaseline(
            string toolName,
            JsonObject args
        ) => Run(toolName, args, out _);

        public override void PrepareCase() => File.WriteAllBytes(file, seed);

        public override ToolArgumentShapeFuzzRunner.CaseOutcome RunCase(
            string toolName,
            JsonObject args
        )
        {
            var (result, success) = Run(toolName, args, out var output);

            return new(
                result,
                success,
                () =>
                {
                    if (!File.ReadAllBytes(file).AsSpan().SequenceEqual(seed))
                    {
                        return "失敗したのに図が変わった";
                    }

                    return output is not null && (File.Exists(output) || Directory.Exists(output))
                        ? "失敗したのに出力先へ書いた"
                        : null;
                }
            );
        }

        public override bool IsRawException(string result) =>
            result.StartsWith(RawExceptionPrefix, StringComparison.Ordinal);

        /// <summary>MCP サーバと同じ入口でツールを実行する（出力先の目印は未使用のパスへ置き換える）</summary>
        private (string Result, bool Success) Run(
            string toolName,
            JsonObject arguments,
            out string? output
        )
        {
            output = null;
            var args = (JsonObject)arguments.DeepClone();

            foreach (
                var (name, marker, extension) in new[]
                {
                    ("out_dir", OutDirMarker, string.Empty),
                    ("out_file", OutSqlMarker, ".sql"),
                }
            )
            {
                if (
                    args[name] is JsonValue value
                    && value.TryGetValue<string>(out var text)
                    && text == marker
                )
                {
                    output = Path.Combine(dir, Guid.NewGuid().ToString("N") + extension);
                    args[name] = output;
                }
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
