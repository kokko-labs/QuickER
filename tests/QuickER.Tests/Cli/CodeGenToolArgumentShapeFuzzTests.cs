using System.IO;
using System.Text.Json.Nodes;
using AwesomeAssertions;
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

    /// <summary>この実行で通ったケース ID（台帳の古い項目を見つけるため）</summary>
    private readonly HashSet<string> _seenIds = [];

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

        var problems = new List<string>();
        var caseCount = 0;

        foreach (var (toolName, baseline) in baselines)
        {
            var tool = toolSet.Tools.Single(candidate => candidate.Name == toolName);
            var schema = ToolArgumentFuzz.SchemaOf(tool.InputSchema);
            var (baseResult, baseSuccess) = Run(toolSet, toolName, baseline, out _);

            if (!baseSuccess)
            {
                problems.Add($"[{toolName}#base] ベースラインが失敗した: {baseResult}");
                continue;
            }

            foreach (
                var fuzzCase in ToolArgumentFuzz.Generate($"{toolName}#base", schema, baseline)
            )
            {
                caseCount++;
                File.WriteAllBytes(file, seed);
                Ledger.Run(
                    fuzzCase.Id,
                    () => Check(toolSet, toolName, fuzzCase, file, seed),
                    problems
                );
            }
        }

        foreach (var stale in Ledger.Ids.Except(_seenIds))
        {
            problems.Add($"[{stale}] 台帳にあるが、どのケースにも当たらない。台帳から外す。");
        }

        CoverageGuard.AtLeast("総当たりで組み立てたケース", caseCount, 41);
        KnownCaseLedger.ThrowIfAny(problems);
    }

    /// <summary>崩したケース 1 件を実行して 3 つの表明を確かめる</summary>
    private void Check(
        QuickER.Mcp.McpToolSet toolSet,
        string toolName,
        ToolArgumentFuzz.Case fuzzCase,
        string file,
        byte[] seed
    )
    {
        _seenIds.Add(fuzzCase.Id);
        var (result, success) = Run(toolSet, toolName, fuzzCase.Arguments, out var output);

        if (result.StartsWith(RawExceptionPrefix, StringComparison.Ordinal))
        {
            throw new Xunit.Sdk.XunitException($"生の例外: {result}");
        }

        if (fuzzCase.MustFail && success)
        {
            throw new Xunit.Sdk.XunitException($"成功した（断るべき）: {result}");
        }

        if (success)
        {
            return;
        }

        if (!File.ReadAllBytes(file).AsSpan().SequenceEqual(seed))
        {
            throw new Xunit.Sdk.XunitException($"失敗したのに図が変わった: {result}");
        }

        if (output is not null && (File.Exists(output) || Directory.Exists(output)))
        {
            throw new Xunit.Sdk.XunitException($"失敗したのに出力先へ書いた: {result}");
        }
    }

    /// <summary>MCP サーバと同じ入口でツールを実行する（出力先の目印は未使用のパスへ置き換える）</summary>
    private (string Result, bool Success) Run(
        QuickER.Mcp.McpToolSet toolSet,
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
                output = Path.Combine(_dir, Guid.NewGuid().ToString("N") + extension);
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
