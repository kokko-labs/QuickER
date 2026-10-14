using System.IO;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using QuickER.Documents;
using QuickER.Mcp.Tools;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Mcp.Tools;

/// <summary>
/// MCP の ER 図操作ツール（<see cref="DocumentErDiagramToolSet"/> の 16 ツール）へ、形の崩れた引数を総当たりで流す
/// </summary>
/// <remarks>
/// <para>
/// 入口は MCP サーバが呼ぶのと同じ <see cref="QuickER.Mcp.McpToolSet.Execute"/>。ケースは各ツールの入力スキーマから
/// <see cref="ToolArgumentFuzz"/> が作る（必須の欠落・<c>null</c>・型違い・配列の要素の種類違い・要素の中の崩し）。
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

    /// <summary>各テスト専用の一時ディレクトリ</summary>
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(),
        "quicker-arg-fuzz-" + Guid.NewGuid().ToString("N")
    );

    /// <summary>既知の失敗（直す前の現状）。直したら外す</summary>
    private static readonly KnownFailureLedger Ledger = new(ToolArgumentShapeFuzzKnownFailures.Mcp);

    /// <summary>この実行で通ったケース ID（台帳の古い項目を見つけるため）</summary>
    private readonly HashSet<string> _seenIds = [];

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
                    pair.Value.Select(baseline =>
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

        var problems = new List<string>();
        var caseCount = 0;

        foreach (var tool in toolSet.Tools)
        {
            var schema = ToolArgumentFuzz.SchemaOf(tool.InputSchema);

            foreach (var (label, baseline) in baselines[tool.Name])
            {
                var prefix = $"{tool.Name}#{label}";

                // 起点が成功しないと、崩したケースの失敗が崩し方のせいか分からない
                File.WriteAllBytes(file, seed);
                var (baseResult, baseSuccess) = Run(toolSet, tool.Name, baseline, out _);

                if (!baseSuccess)
                {
                    problems.Add($"[{prefix}] ベースラインが失敗した: {baseResult}");
                    continue;
                }

                foreach (var fuzzCase in ToolArgumentFuzz.Generate(prefix, schema, baseline))
                {
                    caseCount++;
                    File.WriteAllBytes(file, seed);
                    Ledger.Run(
                        fuzzCase.Id,
                        () => Check(toolSet, tool.Name, fuzzCase, file, seed),
                        problems
                    );
                }
            }
        }

        foreach (var stale in Ledger.Known.Keys.Except(_seenIds))
        {
            problems.Add($"[{stale}] 台帳にあるが、どのケースにも当たらない。台帳から外す。");
        }

        caseCount.Should().BeGreaterThan(300, "総当たりのケースが組み立てられている");
        KnownFailureLedger.ThrowIfAny(problems);
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
        var (result, success) = Run(toolSet, toolName, fuzzCase.Arguments, out var newFile);

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

        if (newFile is not null && File.Exists(newFile))
        {
            throw new Xunit.Sdk.XunitException($"失敗したのにファイルを作った: {result}");
        }
    }

    /// <summary>MCP サーバと同じ入口でツールを実行する（新しい図ファイルの目印は未使用のパスへ置き換える）</summary>
    private (string Result, bool Success) Run(
        QuickER.Mcp.McpToolSet toolSet,
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
            newFile = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".json");
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
